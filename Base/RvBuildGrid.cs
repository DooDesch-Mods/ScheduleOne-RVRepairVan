using System;
using DooDesch.Localization;
using System.Collections;
using System.Collections.Generic;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Tiles;
using RVRepairVan.Config;
using RVRepairVan.Managers;
using MelonLoader;

namespace RVRepairVan.Base
{
    /// <summary>
    /// The "workshop floor" upgrade. Extends the RV's build grid.
    ///
    /// Measurement decided the shape of this one, twice over. The RV ships two grids on the interior floor
    /// (<c>@Properties/RV/RV/Container/Grid</c> with 18 tiles and <c>Grid (1)</c> with 14). They read as inactive
    /// on a wrecked RV, but that is only because they hang off the intact model - repairing the RV switches them
    /// on by itself. So the vanilla 32 tiles come FREE with the repair, and selling "turn the grid on" would be
    /// selling nothing.
    ///
    /// What the upgrade actually buys is more floor: <c>BuildGridExtraTiles</c> cloned tiles registered into the
    /// grid on top of the vanilla ones. Activation is still done here, harmlessly and idempotently, so the
    /// upgrade also covers any case where the grids are not already on.
    ///
    /// The floor only covers what the furniture leaves free: a spot under a part that is fitted
    /// (<see cref="RvInterior"/>) gets no tile, a part taken out frees its spots, and a part put back takes its
    /// spots again. Putting a part back is refused while anything stands there (<see cref="RefitBlocked"/>).
    /// </summary>
    internal static class RvBuildGrid
    {
        private static bool _applied;
        private static int _clonedTiles;
        private static object _sweep;
        private static int _inheritedOccupants;
        private static Dictionary<int, Shadow> _shadows = new Dictionary<int, Shadow>();

        internal static void Reset()
        {
            _applied = false;
            _clonedTiles = 0;
            _keptBare = 0;
            if (_sweep != null) { MelonCoroutines.Stop(_sweep); _sweep = null; }
            ReleaseShadows();
        }

        /// <summary>
        /// Rebuild the added tiles while the property is loading, before the game restores the objects placed on
        /// them. The extra tiles only exist at runtime: if a saved machine sits on one and the tile is not back
        /// yet, <c>Grid.GetTile</c> returns null and <c>GridItem</c> destroys itself - the next save then loses
        /// the machine permanently. Called from the PropertyManager.LoadProperty postfix, which returns before
        /// PropertyLoader starts loading object records.
        /// </summary>
        internal static void ProvisionForLoad(Property prop)
        {
            try
            {
                if (prop == null || !RVRepairVanPreferences.UpgradesEnabled) return;
                if (!RvUpgrades.Owned(RvUpgrade.WorkshopFloor)) return;
                ApplyTo(prop);
            }
            catch (Exception e) { Core.Log.Warning("[Grid] load provisioning failed: " + e.Message); }
        }

        internal static void Apply()
        {
            if (_applied) return;
            Property prop = PropertyOf();
            if (prop == null) { Core.LogDebug("[Grid] RV property not ready - will retry on the next apply."); return; }
            ApplyTo(prop);
        }

        private static void ApplyTo(Property prop)
        {
            try
            {
                int enabled = 0, tiles = 0;
                var grids = prop.Grids;
                if (grids != null)
                    for (int i = 0; i < grids.Count; i++)
                    {
                        Grid g = grids[i];
                        if (g == null) continue;
                        if (!g.gameObject.activeSelf) { g.gameObject.SetActive(true); enabled++; }
                        tiles += g.Tiles != null ? g.Tiles.Count : 0;
                    }

                if (enabled == 0 && tiles == 0)
                {
                    Core.Log.Warning("[Grid] the RV reports no build grid - equipment cannot be placed inside.");
                    return;   // leave _applied false so the next pass retries
                }

                int extra = RVRepairVanPreferences.BuildGridExtraTiles;
                if (extra > 0)
                {
                    ExpandGrid(prop, extra);
                    Sweep();
                    if (_sweep == null) _sweep = MelonCoroutines.Start(SweepLoop());
                    tiles = 0;
                    for (int i = 0; grids != null && i < grids.Count; i++)
                        if (grids[i] != null && grids[i].gameObject.activeSelf && grids[i].Tiles != null) tiles += grids[i].Tiles.Count;
                }

                // Latch only once the work is actually done, so a failed pass is retried rather than swallowed.
                _applied = true;
                Core.Log.Msg("[Grid] workshop floor open - " + enabled + " grid(s) switched on, " + tiles
                    + " tile(s) total, " + _clonedTiles + " of them added by this upgrade.");
            }
            catch (Exception e) { Core.Log.Warning("[Grid] apply failed: " + e.Message); }
        }

        /// <summary>
        /// Lay a CONTIGUOUS floor across the whole cabin, on one grid.
        ///
        /// The first version cloned tiles in rings around an arbitrary template tile, which produced a small island
        /// of tiles near that grid's origin and bare floor everywhere else. In the build UI that reads as "I can't
        /// place anything": a 2x2 machine needs four adjacent valid tiles, and outside the island there were none.
        /// The RV also ships TWO grid objects at different origins, so what tiles did exist looked like separate
        /// rasters rather than one floor.
        ///
        /// So: sweep the whole coordinate rectangle the property's bounds cover and fill every hole, all on grid 0.
        /// A cloned tile only counts once it is in the grid's books - RegisterTile writes Tiles and
        /// CoordinateTilePairs, and _coordinateToTile (what GetTile reads) has to be written separately.
        /// </summary>
        private static void ExpandGrid(Property prop, int extra)
        {
            try
            {
                if (_clonedTiles >= extra) return;
                var grids = prop.Grids;
                if (grids == null || grids.Count == 0) return;

                Grid grid = grids[0];
                if (grid == null || grid.Tiles == null || grid.Tiles.Count == 0)
                {
                    Core.Log.Warning("[Grid] no template tile to clone from - extra tiles skipped.");
                    return;
                }

                Tile template = grid.Tiles[0];
                if (template == null) return;
                float size = Grid.TileSize;          // static on Grid, not per-instance
                if (size <= 0.01f) { Core.Log.Warning("[Grid] tile size is " + size + " - cannot lay a floor."); return; }
                float offset = template.AvailableOffset;

                // How far the property reaches, expressed in this grid's own tile steps. Generous on purpose - the
                // per-tile bounds test below is what actually decides, this only bounds the search.
                int reach = 24;
                try
                {
                    Collider box = prop.BoundingBox != null ? prop.BoundingBox.GetComponent<Collider>() : null;
                    Vector3 ext = box != null ? box.bounds.size : Vector3.one * 12f;
                    reach = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(ext.x, ext.z) / size) + 2, 4, 48);
                }
                catch (Exception e) { Core.Swallowed("RvBuildGrid.ExpandGrid", e); }

                // Measured in the RV's own frame at home. In world space the rectangle of a turned RV takes in the
                // ground beside it: after a drive the RV can stand at any angle, and tiles were laid behind the walls.
                Transform root = RVManager.Root;
                Matrix4x4 frame = root != null ? RvMobile.WorldToHomeRoot(root) : Matrix4x4.identity;
                if (root == null) Core.Log.Warning("[Grid] no RV root - the floor is measured in world space.");
                Bounds? cabin = CabinFloorBounds(frame);
                Bounds? rear = root != null ? RearFloorLocal(frame) : null;
                Transform model = RVManager.Model;
                Bounds? floor = model != null ? FloorArea(model) : null;
                int added = 0, skippedOutside = 0, skippedFurniture = 0;
                for (int cx = template.x - reach; cx <= template.x + reach && added < extra; cx++)
                    for (int cz = template.y - reach; cz <= template.y + reach && added < extra; cz++)
                    {
                        if (grid.GetTile(new Coordinate(cx, cz)) != null) continue;   // already covered

                        // Only lay floor that is actually on the cabin floor. The property's own bounding box is
                        // far too generous - it covers the ground around the RV as well, so testing against it
                        // still pushed tiles out through the wall (measured extent reached x 18.7 on a vehicle
                        // that ends near 16.2). The floor MESH is the honest boundary.
                        Vector3 world = grid.transform.TransformPoint(new Vector3(cx * size, 0f, cz * size));
                        Vector3 local = frame.MultiplyPoint3x4(world);
                        bool inside = cabin.HasValue
                            ? cabin.Value.Contains(new Vector3(local.x, cabin.Value.center.y, local.z))
                            : SafeInBounds(prop, world);
                        if (!inside && rear.HasValue)
                            inside = rear.Value.Contains(new Vector3(local.x, rear.Value.center.y, local.z));
                        if (!inside && floor.HasValue) inside = OnFloor(floor.Value, model, world, local, size);
                        if (!inside) { skippedOutside++; continue; }
                        if (UnderFittedPart(world, size) != RvPart.None) { skippedFurniture++; continue; }

                        if (CloneTile(grid, template, cx, cz, size, offset)) added++;
                    }

                if (added >= extra)
                    Core.Log.Warning("[Grid] hit the BuildGridExtraTiles cap of " + extra
                        + " - raise it if parts of the floor are still bare.");
                Core.LogDebug("[Grid] fill: reach=" + reach + " tileSize=" + size + " outsideBounds=" + skippedOutside + " underFurniture=" + skippedFurniture);

                _clonedTiles += added;
                Core.Log.Msg("[Grid] added " + added + " extra tile(s) (requested " + extra + ").");
                if (_inheritedOccupants > 0)
                    Core.Log.Msg("[Grid] cleared " + _inheritedOccupants + " occupant entr(ies) the added tiles copied from the template tile ("
                        + template.x + "," + template.y + ").");
                _inheritedOccupants = 0;
            }
            catch (Exception e) { Core.Log.Warning("[Grid] expand failed: " + e.Message); }
        }

        // ---- furniture ------------------------------------------------------------------------------------------

        // A part counts as standing on the floor when its bottom is this close to it (the wall cabinets do not).
        private const float FloorContact = 0.5f;
        // How far a tile may reach into a part's box before it counts as under it.
        private const float Overlap = 0.1f;

        /// <summary>The fitted part whose floor a tile at <paramref name="world"/> would cover, or None.</summary>
        private static RvPart UnderFittedPart(Vector3 world, float size)
        {
            Transform model = RVManager.Model;
            if (model == null) return RvPart.None;
            foreach (var p in RvInterior.Parts)
                if (!RvInterior.IsRemoved(p) && Covers(model, p, world, size)) return p;
            return RvPart.None;
        }

        /// <summary>
        /// Does a tile at <paramref name="world"/> reach into one of the part's pieces that stands on the floor? Piece by
        /// piece: the eight lower cabinets and the decor props spread over the whole cabin, and their joint box took the
        /// floor between them as well.
        /// </summary>
        private static bool Covers(Transform model, RvPart p, Vector3 world, float size)
        {
            var boxes = RvInterior.PieceBoxes(p);
            if (boxes.Count == 0) return false;
            float floorY = RvInterior.FloorY(model);
            Vector3 l = model.InverseTransformPoint(world);
            float half = size * 0.5f - Overlap;
            foreach (Bounds box in boxes)
            {
                if (box.min.y - floorY > FloorContact) continue;
                if (l.x + half > box.min.x && l.x - half < box.max.x && l.z + half > box.min.z && l.z - half < box.max.z) return true;
            }
            return false;
        }

        // ---- the cabin floor -------------------------------------------------------------------------------------

        // The model frame: x along the RV with the cab at -x, z across. Used when a wall or the bed cannot be measured.
        private const float FallbackCabWallX = -3.43f, FallbackRearX = 5.93f, FallbackSideZ = 1.57f;
        // How far a tile may reach past a wall's inner face; the walls are measured from their meshes, not their colliders.
        private const float WallSlack = 0.05f;

        /// <summary>
        /// The cabin floor in the model frame: from the wall behind the driver to the rear wall, between the side walls.
        /// The partition stays the front edge when it is taken out, so the driver's cab never gets tiles. The bed reaches
        /// the rear wall and the partition spans the cabin, so their boxes (measured while they stood) give the walls.
        /// </summary>
        private static Bounds FloorArea(Transform model)
        {
            float front = FallbackCabWallX, back = FallbackRearX, left = -FallbackSideZ, right = FallbackSideZ;
            if (RvInterior.Box(RvPart.CabWall, out Bounds cab)) { front = cab.max.x; left = cab.min.z; right = cab.max.z; }
            else Core.Log.Warning("[Grid] the wall behind the driver is not measured - the floor starts at x " + front + ".");
            if (RvInterior.Box(RvPart.Bed, out Bounds bed)) back = bed.max.x;
            else Core.Log.Warning("[Grid] the bed is not measured - the floor ends at x " + back + ".");
            float y = RvInterior.FloorY(model);
            var area = new Bounds();
            area.SetMinMax(new Vector3(front, y - 10f, left), new Vector3(back, y + 10f, right));
            return area;
        }

        /// <summary>
        /// Does a whole tile at <paramref name="world"/> lie on the cabin floor? <paramref name="root"/> is the same point
        /// in the RV root's home frame, where the rear and side walls were measured: the bed reaches into the rear wall.
        /// </summary>
        private static bool OnFloor(Bounds area, Transform model, Vector3 world, Vector3 root, float size)
        {
            Vector3 l = model.InverseTransformPoint(world);
            float half = size * 0.5f - WallSlack;
            if (l.x - half < area.min.x || l.x + half > area.max.x || l.z - half < area.min.z || l.z + half > area.max.z) return false;
            return root.z + half <= RearWallZ && Mathf.Abs(root.x) + half <= SideWallX;
        }

        /// <summary>
        /// Why a part that is out cannot go back in, or null. Anything standing on its floor blocks it, on any grid,
        /// whether the floor was bought or not.
        /// </summary>
        internal static string RefitBlocked(RvPart p)
        {
            try
            {
                if (!RvInterior.IsRemoved(p)) return null;
                Transform model = RVManager.Model;
                Property prop = PropertyOf();
                var grids = prop != null ? prop.Grids : null;
                if (model == null || grids == null) return null;
                float size = Grid.TileSize;
                for (int i = 0; i < grids.Count; i++)
                {
                    Grid g = grids[i];
                    if (g == null || g.Tiles == null) continue;
                    for (int k = 0; k < g.Tiles.Count; k++)
                    {
                        Tile t = g.Tiles[k];
                        if (t == null || t.BuildableOccupants == null || t.BuildableOccupants.Count == 0) continue;
                        if (Covers(model, p, t.transform.position, size)) return L10n.T("Clear the floor here first.");
                    }
                }
                return null;
            }
            catch (Exception e)
            {
                Core.Log.Error("[Grid] checking the floor under " + p + " failed - refitting is refused: " + e.Message);
                return L10n.T("Clear the floor here first.");
            }
        }

        /// <summary>
        /// The furniture changed: take back the added tiles under parts that went in, then lay floor where parts came
        /// out. Only with the workshop floor; the vanilla tiles are never touched.
        /// </summary>
        internal static void PartsChanged()
        {
            if (!_applied || !RvUpgrades.Owned(RvUpgrade.WorkshopFloor)) return;
            try
            {
                Property prop = PropertyOf();
                var grids = prop != null ? prop.Grids : null;
                if (grids == null || grids.Count == 0 || grids[0] == null) return;
                Grid grid = grids[0];
                float size = Grid.TileSize;
                int removed = 0, kept = 0;
                for (int k = grid.Tiles.Count - 1; k >= 0; k--)
                {
                    Tile t = grid.Tiles[k];
                    if (t == null || !IsOurs(t)) continue;
                    RvPart under = UnderFittedPart(t.transform.position, size);
                    if (under == RvPart.None) continue;
                    if (t.BuildableOccupants != null && t.BuildableOccupants.Count > 0)
                    {
                        kept++;
                        Core.Log.Warning("[Grid] tile " + t.x + "," + t.y + " under the " + under + " carries an item - it stays.");
                        continue;
                    }
                    RemoveTile(grid, t);
                    removed++;
                }
                _clonedTiles = Math.Max(0, _clonedTiles - removed);
                if (removed > 0 || kept > 0) Core.Log.Msg("[Grid] furniture went in: " + removed + " tile(s) taken back" + (kept > 0 ? ", " + kept + " kept under items" : "") + ".");
                ExpandGrid(prop, RVRepairVanPreferences.BuildGridExtraTiles);
                Sweep();
            }
            catch (Exception e) { Core.Log.Error("[Grid] following the furniture failed: " + e); }
        }

        private static void RemoveTile(Grid grid, Tile t)
        {
            var c = new Coordinate(t.x, t.y);
            try { grid.DeregisterTile(t); } catch (Exception e) { Core.Log.Warning("[Grid] DeregisterTile failed: " + e.Message); }
            try { if (grid._coordinateToTile != null && grid._coordinateToTile.ContainsKey(c)) grid._coordinateToTile.Remove(c); }
            catch (Exception e) { Core.Log.Warning("[Grid] removing tile " + c.x + "," + c.y + " from the lookup failed: " + e.Message); }
            UnityEngine.Object.Destroy(t.gameObject);
        }

        /// <summary>
        /// The RV ships two grid objects at different origins. Once grid 0 covers the whole cabin they overlap: the
        /// second grid's tiles sit 0.09 m across and half a tile along from the new ones. Build mode snaps to the
        /// nearest tile of ANY active grid and then judges the whole footprint on that tile's grid alone, so two
        /// rasters over one floor make the ghost jump between them and turn red where the other grid has no tile.
        ///
        /// A grid with nothing on it is switched off. Equipment placed on it before the floor was bought stays where
        /// it is: that grid stays on (its tiles also carry light and temperature for whatever stands there), but its
        /// tile colliders go off so build mode only sees grid 0, and the grid-0 tiles under the equipment count as
        /// occupied so nothing can be built into it. The sweep repeats every second, which frees those tiles and
        /// switches the grid off once the last item is picked up, and catches items the save restores onto it.
        /// </summary>
        private static void Sweep()
        {
            Property prop = PropertyOf();
            var grids = prop != null ? prop.Grids : null;
            if (grids == null || grids.Count < 2) return;
            Grid floor = grids[0];
            if (floor == null || floor.Tiles == null) return;

            float size = Grid.TileSize;
            float reach = size - 0.05f;   // tiles overlap unless they only touch at an edge
            var wanted = new Dictionary<int, Shadow>();

            for (int i = 1; i < grids.Count; i++)
            {
                Grid g = grids[i];
                if (g == null || g.Tiles == null) continue;

                int items = 0;
                var seen = new HashSet<IntPtr>();
                for (int k = 0; k < g.Tiles.Count; k++)
                {
                    Tile t = g.Tiles[k];
                    if (t == null || t.BuildableOccupants == null || t.BuildableOccupants.Count == 0) continue;
                    Vector3 local = floor.transform.InverseTransformPoint(t.transform.position);
                    for (int f = 0; f < floor.Tiles.Count; f++)
                    {
                        Tile ft = floor.Tiles[f];
                        if (ft == null) continue;
                        Vector3 d = floor.transform.InverseTransformPoint(ft.transform.position) - local;
                        if (Mathf.Abs(d.x) >= reach || Mathf.Abs(d.z) >= reach) continue;
                        int id = ft.GetInstanceID();
                        if (!wanted.TryGetValue(id, out Shadow sh)) wanted[id] = sh = new Shadow(ft);
                        for (int o = 0; o < t.BuildableOccupants.Count; o++)
                        {
                            GridItem item = t.BuildableOccupants[o];
                            if (item != null && !sh.Items.ContainsKey(item.Pointer)) sh.Items[item.Pointer] = item;
                        }
                    }
                    for (int o = 0; o < t.BuildableOccupants.Count; o++)
                        if (t.BuildableOccupants[o] != null && seen.Add(t.BuildableOccupants[o].Pointer)) items++;
                }

                bool active = g.gameObject.activeSelf;
                int bare = items > 0 ? 0 : Uncovered(g, floor, reach);
                if (bare > 0)
                {
                    // Switching it off would take floor away: the workshop floor has no tile there.
                    if (!active) g.gameObject.SetActive(true);
                    SetTileColliders(g, true);
                    if (!active || _keptBare != bare)
                        Core.Log.Warning("[Grid] '" + g.name + "' stays on: " + bare + " of its tile(s) have no workshop-floor tile beside them.");
                    _keptBare = bare;
                }
                else if (items > 0)
                {
                    if (!active) g.gameObject.SetActive(true);
                    int switched = SetTileColliders(g, false);
                    if (!active || switched > 0)
                        Core.Log.Msg("[Grid] '" + g.name + "' carries " + items + " item(s) from before the workshop floor - they stay, "
                            + "its raster is hidden from build mode.");
                }
                else
                {
                    SetTileColliders(g, true);
                    _keptBare = 0;
                    if (active)
                    {
                        g.gameObject.SetActive(false);
                        Core.Log.Msg("[Grid] retired the RV's second grid '" + g.name + "' - one continuous floor now.");
                    }
                }
            }

            int added = 0, removed = 0;
            foreach (var kv in _shadows)
            {
                Tile ft = kv.Value.Tile;
                wanted.TryGetValue(kv.Key, out Shadow keep);
                foreach (var it in kv.Value.Items)
                    if ((keep == null || !keep.Items.ContainsKey(it.Key)) && ft != null && RemoveOccupant(ft, it.Key)) removed++;
            }
            foreach (var kv in wanted)
            {
                _shadows.TryGetValue(kv.Key, out Shadow had);
                foreach (var it in kv.Value.Items)
                    if (had == null || !had.Items.ContainsKey(it.Key))
                    {
                        if (!HasOccupant(kv.Value.Tile, it.Key)) kv.Value.Tile.BuildableOccupants.Add(it.Value);
                        added++;
                    }
            }
            _shadows = wanted;
            if (added > 0 || removed > 0)
                Core.Log.Msg("[Grid] floor tiles under items on the second grid: " + wanted.Count + " held ("
                    + added + " taken, " + removed + " freed).");
        }

        private static int _keptBare;

        /// <summary>How many tiles of <paramref name="g"/> have no tile of the floor grid over them.</summary>
        private static int Uncovered(Grid g, Grid floor, float reach)
        {
            int bare = 0;
            for (int k = 0; k < g.Tiles.Count; k++)
            {
                Tile t = g.Tiles[k];
                if (t == null) continue;
                Vector3 local = floor.transform.InverseTransformPoint(t.transform.position);
                bool covered = false;
                for (int f = 0; f < floor.Tiles.Count && !covered; f++)
                {
                    Tile ft = floor.Tiles[f];
                    if (ft == null) continue;
                    Vector3 d = floor.transform.InverseTransformPoint(ft.transform.position) - local;
                    covered = Mathf.Abs(d.x) < reach && Mathf.Abs(d.z) < reach;
                }
                if (!covered) bare++;
            }
            return bare;
        }

        /// <summary>Hand every held floor tile back. Tiles of a scene that is already gone are skipped.</summary>
        private static void ReleaseShadows()
        {
            int freed = 0;
            foreach (var kv in _shadows)
                foreach (var it in kv.Value.Items)
                    if (kv.Value.Tile != null && RemoveOccupant(kv.Value.Tile, it.Key)) freed++;
            if (freed > 0) Core.Log.Msg("[Grid] freed " + freed + " held floor tile entr(ies).");
            _shadows = new Dictionary<int, Shadow>();
        }

        private static IEnumerator SweepLoop()
        {
            int failures = 0;
            while (true)
            {
                yield return new WaitForSeconds(1f);
                try { Sweep(); failures = 0; }
                catch (Exception e)
                {
                    if (failures++ == 0) Core.Log.Error("[Grid] second-grid sweep failed: " + e);
                }
            }
        }

        // Il2Cpp wrappers are not unique per object, so occupants are matched on the native pointer. ReferenceEquals,
        // not Unity's ==: a picked-up item is already destroyed when its held tiles are handed back, and Unity's
        // null check would hide it.
        private static bool HasOccupant(Tile tile, IntPtr item) => IndexOf(tile, item) >= 0;

        private static bool RemoveOccupant(Tile tile, IntPtr item)
        {
            int i = IndexOf(tile, item);
            if (i < 0) return false;
            tile.BuildableOccupants.RemoveAt(i);
            return true;
        }

        private static int IndexOf(Tile tile, IntPtr item)
        {
            var list = tile.BuildableOccupants;
            for (int i = 0; list != null && i < list.Count; i++)
            {
                GridItem o = list[i];
                if (!ReferenceEquals(o, null) && o.Pointer == item) return i;
            }
            return -1;
        }

        /// <summary>Build mode finds tiles through their colliders. Returns how many colliders changed state.</summary>
        private static int SetTileColliders(Grid g, bool on)
        {
            int changed = 0;
            for (int k = 0; k < g.Tiles.Count; k++)
            {
                Tile t = g.Tiles[k];
                if (t == null) continue;
                var cols = t.GetComponentsInChildren<Collider>(true);
                for (int c = 0; c < cols.Length; c++)
                    if (cols[c] != null && cols[c].enabled != on) { cols[c].enabled = on; changed++; }
            }
            return changed;
        }

        private sealed class Shadow
        {
            internal readonly Tile Tile;
            internal readonly Dictionary<IntPtr, GridItem> Items = new Dictionary<IntPtr, GridItem>();
            internal Shadow(Tile tile) { Tile = tile; }
        }

        /// <summary>
        /// The area the game itself considers placeable: the bounding rectangle of the tiles the RV already ships,
        /// across both of its grids. Self-calibrating and impossible to leak out of, which the alternatives were
        /// not - the property's bounding box also covers the ground around the vehicle (tiles reached x 18.7), and
        /// the Floor mesh turned out to be wider than the cabin as well (x 7.7 to 18.2). The developers put those
        /// 32 tiles exactly where equipment fits, so filling the gaps between them is the honest interpretation of
        /// "extend the floor".
        ///
        /// Must be read BEFORE anything is cloned, or it grows with its own output. In the frame <paramref name="frame"/>
        /// maps world points into.
        /// </summary>
        private static Bounds? CabinFloorBounds(Matrix4x4 frame)
        {
            try
            {
                Property prop = PropertyOf();
                var grids = prop != null ? prop.Grids : null;
                if (grids == null || grids.Count == 0) return null;

                bool any = false;
                Bounds b = default;
                for (int i = 0; i < grids.Count; i++)
                {
                    Grid g = grids[i];
                    if (g == null || g.Tiles == null) continue;
                    for (int k = 0; k < g.Tiles.Count; k++)
                    {
                        Tile t = g.Tiles[k];
                        if (t == null || IsOurs(t)) continue;
                        Vector3 p = frame.MultiplyPoint3x4(t.transform.position);
                        if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                    }
                }
                if (!any) return null;

                // One tile of slack so the rectangle includes the outer tile centres themselves, plus a generous y.
                float pad = Mathf.Max(Grid.TileSize, 0.5f);
                b.Expand(new Vector3(pad, 10f, pad));
                return b;
            }
            catch (Exception e) { Core.Swallowed("RvBuildGrid.CabinFloorBounds", e); return null; }
        }

        // Inner face of the RV's rear wall (Collider_Rear) in the root frame. The side walls stand at x +-1.57.
        private const float RearWallZ = 5.65f;
        private const float SideWallX = 1.57f;
        private const string ClonePrefix = "RVRepairVan_Tile_";

        private static bool IsOurs(Tile t) => t.gameObject.name.StartsWith(ClonePrefix, StringComparison.Ordinal);

        /// <summary>
        /// The back of the cabin, where the bed stood before the gut. The vanilla tiles end at z 3.09, so the
        /// rectangle above leaves 2.5 m of empty floor up to the rear wall. This one takes the same width and runs
        /// from the vanilla tiles to the rear wall, in the root frame, with a tile's half width kept clear of the wall.
        /// Only ever adds to the area above: a saved machine on a tile that is no longer laid would be destroyed.
        /// Must be read BEFORE anything is cloned, like <see cref="CabinFloorBounds"/>.
        /// </summary>
        private static Bounds? RearFloorLocal(Matrix4x4 frame)
        {
            try
            {
                Property prop = PropertyOf();
                var grids = prop != null ? prop.Grids : null;
                if (grids == null) return null;
                bool any = false;
                Vector3 min = Vector3.zero, max = Vector3.zero;
                for (int i = 0; i < grids.Count; i++)
                {
                    Grid g = grids[i];
                    if (g == null || g.Tiles == null) continue;
                    for (int k = 0; k < g.Tiles.Count; k++)
                    {
                        Tile t = g.Tiles[k];
                        if (t == null || IsOurs(t)) continue;
                        Vector3 p = frame.MultiplyPoint3x4(t.transform.position);
                        if (!any) { min = max = p; any = true; } else { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                    }
                }
                if (!any) { Core.Log.Warning("[Grid] no vanilla tiles to measure the rear floor from - the back stays bare."); return null; }
                float half = Grid.TileSize * 0.5f;
                float back = RearWallZ - half;
                if (back <= max.z) return null;
                var b = new Bounds();
                b.SetMinMax(new Vector3(min.x - half, min.y - 10f, max.z), new Vector3(max.x + half, max.y + 10f, back));
                Core.LogDebug("[Grid] rear floor: x " + b.min.x.ToString("F2") + ".." + b.max.x.ToString("F2")
                    + " z " + b.min.z.ToString("F2") + ".." + b.max.z.ToString("F2") + " (root frame).");
                return b;
            }
            catch (Exception e) { Core.Log.Error("[Grid] measuring the rear floor failed - the back stays bare: " + e); return null; }
        }

        private static bool SafeInBounds(Property prop, Vector3 world)
        {
            try { return prop.DoBoundsContainPoint(world); } catch (Exception e) { Core.Swallowed("RvBuildGrid.SafeInBounds", e); return true; }
        }

        private static bool CloneTile(Grid grid, Tile template, int cx, int cz, float size, float offset)
        {
            try
            {
                GameObject go = UnityEngine.Object.Instantiate(template.gameObject, grid.transform);
                go.name = ClonePrefix + cx + "_" + cz;
                go.transform.localPosition = new Vector3(cx * size, 0f, cz * size);
                go.SetActive(true);

                Tile tile = go.GetComponent<Tile>();
                if (tile == null) { UnityEngine.Object.Destroy(go); return false; }
                tile.InitializePropertyTile(cx, cz, offset, grid);

                // Instantiate copies the template's serialized occupant lists. With equipment standing on the
                // template tile when the floor is bought, every added tile would report that equipment as its
                // occupant, and build mode would turn red on the whole new floor until the next load.
                int inherited = (tile.BuildableOccupants != null ? tile.BuildableOccupants.Count : 0)
                    + (tile.OccupantTiles != null ? tile.OccupantTiles.Count : 0);
                if (tile.BuildableOccupants != null) tile.BuildableOccupants.Clear();
                if (tile.OccupantTiles != null) tile.OccupantTiles.Clear();
                if (inherited > 0) _inheritedOccupants += inherited;

                // RegisterTile already appends to BOTH Tiles and CoordinateTilePairs, so adding to those by hand
                // registers every tile twice - the measured symptom was a grid reporting 66 tiles after 24 were
                // added to 18. The one book it does NOT write is _coordinateToTile, which is what GetTile reads.
                try { grid.RegisterTile(tile); } catch (Exception e) { Core.Swallowed("RvBuildGrid.CloneTile.RegisterTile", e); }
                try { if (grid._coordinateToTile != null) grid._coordinateToTile[new Coordinate(cx, cz)] = tile; } catch (Exception e) { Core.Swallowed("RvBuildGrid.CloneTile.coordinateToTile", e); }
                return true;
            }
            catch (Exception e) { Core.Swallowed("RvBuildGrid.CloneTile", e); return false; }
        }

#if DEBUG
        /// <summary>
        /// rvgrid: the tiles a fill would lay at the RV's pose right now, measured the old way (world space) and in
        /// the RV's frame, and how many of each stand behind a side or the rear wall. Lays nothing.
        /// </summary>
        internal static void DebugPlan()
        {
            Property prop = PropertyOf();
            Transform root = RVManager.Root;
            var grids = prop != null ? prop.Grids : null;
            if (root == null || grids == null || grids.Count == 0 || grids[0] == null || grids[0].Tiles == null || grids[0].Tiles.Count == 0)
            {
                Core.Log.Warning("[Grid] plan: RV, grid or tiles not ready.");
                return;
            }
            Grid grid = grids[0];
            Tile template = null;
            for (int k = 0; k < grid.Tiles.Count && template == null; k++) if (grid.Tiles[k] != null && !IsOurs(grid.Tiles[k])) template = grid.Tiles[k];
            if (template == null) { Core.Log.Warning("[Grid] plan: no vanilla tile on the floor grid."); return; }
            Matrix4x4 frame = RvMobile.WorldToHomeRoot(root);
            Plan("world space", grid, template, Matrix4x4.identity, root.worldToLocalMatrix, frame, prop);
            Plan("RV frame", grid, template, frame, frame, frame, prop);
            int ours = 0, outside = 0;
            for (int k = 0; k < grid.Tiles.Count; k++)
            {
                Tile t = grid.Tiles[k];
                if (t == null || !IsOurs(t)) continue;
                ours++;
                if (BehindWall(frame.MultiplyPoint3x4(t.transform.position))) outside++;
            }
            Core.Log.Msg("[Grid] plan: laid now: " + ours + " added tile(s), " + outside + " behind a wall. RV " + (RvMobile.Away ? "away" : "home") + ".");
        }

        private static bool BehindWall(Vector3 local) => Mathf.Abs(local.x) > SideWallX || local.z > RearWallZ;

        private static void Plan(string how, Grid grid, Tile template, Matrix4x4 cabinFrame, Matrix4x4 rearFrame, Matrix4x4 judge, Property prop)
        {
            float size = Grid.TileSize;
            Bounds? cabin = CabinFloorBounds(cabinFrame);
            Bounds? rear = RearFloorLocal(rearFrame);
            Transform model = RVManager.Model;
            Bounds? floor = model != null ? FloorArea(model) : null;
            if (floor.HasValue)
                Core.Log.Msg("[Grid] plan " + how + ": cabin floor x " + floor.Value.min.x.ToString("F2") + ".." + floor.Value.max.x.ToString("F2")
                    + " z " + floor.Value.min.z.ToString("F2") + ".." + floor.Value.max.z.ToString("F2") + " (model frame).");
            int inside = 0, behind = 0, furniture = 0;
            const int reach = 24;
            for (int cx = template.x - reach; cx <= template.x + reach; cx++)
                for (int cz = template.y - reach; cz <= template.y + reach; cz++)
                {
                    Vector3 world = grid.transform.TransformPoint(new Vector3(cx * size, 0f, cz * size));
                    Vector3 c = cabinFrame.MultiplyPoint3x4(world), r = rearFrame.MultiplyPoint3x4(world);
                    bool ok = cabin.HasValue ? cabin.Value.Contains(new Vector3(c.x, cabin.Value.center.y, c.z)) : SafeInBounds(prop, world);
                    if (!ok && rear.HasValue) ok = rear.Value.Contains(new Vector3(r.x, rear.Value.center.y, r.z));
                    if (!ok && floor.HasValue) ok = OnFloor(floor.Value, model, world, r, size);
                    if (!ok) continue;
                    if (UnderFittedPart(world, size) != RvPart.None) { furniture++; continue; }
                    inside++;
                    if (BehindWall(judge.MultiplyPoint3x4(world))) behind++;
                }
            Core.Log.Msg("[Grid] plan " + how + ": " + inside + " free tile spot(s) on the floor, " + behind + " of them behind a wall; " + furniture + " under fitted furniture.");
        }
#endif

        private static Property PropertyOf()
        {
            Transform root = RVManager.Root;
            return root != null ? root.GetComponent<Property>() : null;
        }
    }
}
