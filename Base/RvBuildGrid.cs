using System;
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
    /// Upgrade 2 - "workshop floor". Extends the RV's build grid.
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
    /// </summary>
    internal static class RvBuildGrid
    {
        private static bool _applied;
        private static int _clonedTiles;
        private static object _sweep;
        private static Dictionary<int, Shadow> _shadows = new Dictionary<int, Shadow>();

        internal static void Reset()
        {
            _applied = false;
            _clonedTiles = 0;
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

                Bounds? cabin = CabinFloorBounds();
                int added = 0, skippedOutside = 0;
                for (int cx = template.x - reach; cx <= template.x + reach && added < extra; cx++)
                    for (int cz = template.y - reach; cz <= template.y + reach && added < extra; cz++)
                    {
                        if (grid.GetTile(new Coordinate(cx, cz)) != null) continue;   // already covered

                        // Only lay floor that is actually on the cabin floor. The property's own bounding box is
                        // far too generous - it covers the ground around the RV as well, so testing against it
                        // still pushed tiles out through the wall (measured extent reached x 18.7 on a vehicle
                        // that ends near 16.2). The floor MESH is the honest boundary.
                        Vector3 world = grid.transform.TransformPoint(new Vector3(cx * size, 0f, cz * size));
                        bool inside = cabin.HasValue
                            ? cabin.Value.Contains(new Vector3(world.x, cabin.Value.center.y, world.z))
                            : SafeInBounds(prop, world);
                        if (!inside) { skippedOutside++; continue; }

                        if (CloneTile(grid, template, cx, cz, size, offset)) added++;
                    }

                if (added >= extra)
                    Core.Log.Warning("[Grid] hit the BuildGridExtraTiles cap of " + extra
                        + " - raise it if parts of the floor are still bare.");
                Core.LogDebug("[Grid] fill: reach=" + reach + " tileSize=" + size + " outsideBounds=" + skippedOutside);

                _clonedTiles += added;
                Core.Log.Msg("[Grid] added " + added + " extra tile(s) (requested " + extra + ").");
            }
            catch (Exception e) { Core.Log.Warning("[Grid] expand failed: " + e.Message); }
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
                if (items > 0)
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
        /// Must be read BEFORE anything is cloned, or it grows with its own output.
        /// </summary>
        private static Bounds? CabinFloorBounds()
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
                        if (t == null) continue;
                        Vector3 p = t.transform.position;
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

        private static bool SafeInBounds(Property prop, Vector3 world)
        {
            try { return prop.DoBoundsContainPoint(world); } catch (Exception e) { Core.Swallowed("RvBuildGrid.SafeInBounds", e); return true; }
        }

        private static bool CloneTile(Grid grid, Tile template, int cx, int cz, float size, float offset)
        {
            try
            {
                GameObject go = UnityEngine.Object.Instantiate(template.gameObject, grid.transform);
                go.name = "RVRepairVan_Tile_" + cx + "_" + cz;
                go.transform.localPosition = new Vector3(cx * size, 0f, cz * size);
                go.SetActive(true);

                Tile tile = go.GetComponent<Tile>();
                if (tile == null) { UnityEngine.Object.Destroy(go); return false; }
                tile.InitializePropertyTile(cx, cz, offset, grid);

                // RegisterTile already appends to BOTH Tiles and CoordinateTilePairs, so adding to those by hand
                // registers every tile twice - the measured symptom was a grid reporting 66 tiles after 24 were
                // added to 18. The one book it does NOT write is _coordinateToTile, which is what GetTile reads.
                try { grid.RegisterTile(tile); } catch (Exception e) { Core.Swallowed("RvBuildGrid.CloneTile.RegisterTile", e); }
                try { if (grid._coordinateToTile != null) grid._coordinateToTile[new Coordinate(cx, cz)] = tile; } catch (Exception e) { Core.Swallowed("RvBuildGrid.CloneTile.coordinateToTile", e); }
                return true;
            }
            catch (Exception e) { Core.Swallowed("RvBuildGrid.CloneTile", e); return false; }
        }

        private static Property PropertyOf()
        {
            Transform root = RVManager.Root;
            return root != null ? root.GetComponent<Property>() : null;
        }
    }
}
