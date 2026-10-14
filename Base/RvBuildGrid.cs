using System;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Tiles;
using RVRepairVan.Config;
using RVRepairVan.Managers;

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

        internal static void Reset() { _applied = false; _clonedTiles = 0; }

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
                    RetireSecondGrid(prop);
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
        /// The RV ships two grid objects at different origins. Once grid 0 covers the whole cabin they overlap,
        /// and two rasters over one floor is both confusing to look at and a source of placement conflicts.
        /// Retire the second one - but only while nothing is standing on it, so this can never delete a player's
        /// equipment.
        /// </summary>
        private static void RetireSecondGrid(Property prop)
        {
            try
            {
                var grids = prop.Grids;
                if (grids == null || grids.Count < 2) return;
                for (int i = 1; i < grids.Count; i++)
                {
                    Grid g = grids[i];
                    if (g == null || !g.gameObject.activeSelf) continue;

                    bool occupied = false;
                    if (g.Tiles != null)
                        for (int k = 0; k < g.Tiles.Count && !occupied; k++)
                        {
                            Tile t = g.Tiles[k];
                            if (t != null && t.BuildableOccupants != null && t.BuildableOccupants.Count > 0) occupied = true;
                        }

                    if (occupied) { Core.LogDebug("[Grid] '" + g.name + "' still has items on it - leaving it alone."); continue; }
                    g.gameObject.SetActive(false);
                    Core.Log.Msg("[Grid] retired the RV's second grid '" + g.name + "' - one continuous floor now.");
                }
            }
            catch (Exception e) { Core.Log.Warning("[Grid] retiring the second grid failed: " + e.Message); }
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
