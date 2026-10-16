using System;
using HarmonyLib;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Tiles;
using Il2CppScheduleOne.Vehicles;
using RVRepairVan.Managers;
using UnityEngine;

namespace RVRepairVan.Base
{
    /// <summary>
    /// Placed items in an RV that stands tilted. The game turns an item by its build rotation with
    /// <c>transform.Rotate(&lt;the grid's up in world space&gt;, angle)</c>, and that call reads the axis as a local one.
    /// On a level floor both are the same axis; on a slope a bed or a station turned by 90, 180 or 270 degrees came out
    /// tilted against the RV floor. Items on a table or rack also lost the floor's roll, because their facing is set
    /// with <c>transform.forward</c>, which keeps world up. Here the pose is set again from the floor's own rotation,
    /// which is what the game gets on a level floor.
    /// </summary>
    internal static class RvItemTilt
    {
        // Below this the floor counts as level, and the game's own pose is already right.
        private const float LevelDegrees = 0.05f;

#if DEBUG
        /// <summary>rvtilt vanilla: leave the game's pose alone, to show the tilt it produces.</summary>
        internal static bool DebugVanilla;
#endif

        private static bool OnTheRv(Transform t)
        {
            LandVehicle chassis = RvMobile.Chassis;
            return chassis != null && t != null && t.IsChildOf(chassis.transform);
        }

        private static bool Level(Transform floor) => Vector3.Angle(floor.up, Vector3.up) < LevelDegrees;

        internal static void FixGridItem(GridItem item)
        {
#if DEBUG
            if (DebugVanilla) return;
#endif
            Grid grid = item.OwnerGrid;
            if (grid == null || !OnTheRv(grid.transform) || Level(grid.transform)) return;
            Transform t = item.transform, bp = item.BuildPoint;
            var pairs = item.CoordinatePairs;
            FootprintTile origin = item.OriginFootprint;
            if (bp == null || pairs == null || pairs.Count == 0 || origin == null)
            {
                Core.Log.Warning("[Tilt] '" + item.name + "' has no build point, footprint or tiles - its pose stays as the game set it.");
                return;
            }
            Tile tile = grid.GetTile(pairs[0].coord2);
            if (tile == null)
            {
                Core.Log.Warning("[Tilt] '" + item.name + "' stands on no tile - its pose stays as the game set it.");
                return;
            }
            Quaternion buildPointLocal = Quaternion.Inverse(t.rotation) * bp.rotation;
            t.rotation = grid.transform.rotation * Quaternion.Inverse(buildPointLocal) * Quaternion.AngleAxis(item._rotation, Vector3.up);
            t.position = tile.transform.position - (origin.transform.position - t.position);
            Core.Log.Msg("[Tilt] '" + item.name + "' set flat on the tilted RV floor (turned " + item._rotation + ").");
        }

        internal static void FixProceduralItem(ProceduralGridItem item)
        {
#if DEBUG
            if (DebugVanilla) return;
#endif
            var matches = item.footprintTileMatches;
            if (matches == null || matches.Count == 0) return;
            ProceduralTile tile = matches[0].tile;
            if (tile == null || !OnTheRv(tile.transform) || Level(tile.transform)) return;
            Transform t = item.transform;
            FootprintTile foot = item.GetFootprintTile(matches[0].coord);
            if (foot == null)
            {
                Core.Log.Warning("[Tilt] '" + item.name + "' has no footprint tile - its pose stays as the game set it.");
                return;
            }
            t.rotation = tile.transform.rotation * Quaternion.AngleAxis(item.Rotation, Vector3.up);
            t.position = tile.transform.position - (foot.transform.position - t.position);
            Core.Log.Msg("[Tilt] '" + item.name + "' set flat on its tilted surface in the RV (turned " + item.Rotation + ").");
        }

#if DEBUG
        /// <summary>
        /// rvtilt: how far each placed item in the RV leans against the floor it stands on. 'redo' lets the game set
        /// every pose again first, as it does when an item is placed or joins a client.
        /// </summary>
        internal static void Report(bool redo)
        {
            Transform root = RVManager.Root;
            RV rv = root != null ? root.GetComponent<RV>() : null;
            if (rv == null || rv.Container == null) { Core.Log.Warning("[Tilt] no RV contents to measure."); return; }
            int n = 0, leaning = 0;
            float worst = 0f;
            foreach (GridItem g in rv.Container.GetComponentsInChildren<GridItem>(true))
            {
                if (g == null || g.OwnerGrid == null || g.BuildPoint == null) continue;
                if (redo) g.RefreshTransform();
                float a = Vector3.Angle(g.BuildPoint.up, g.OwnerGrid.transform.up);
                n++; if (a > 0.5f) leaning++; worst = Mathf.Max(worst, a);
                Core.Log.Msg("[Tilt]   " + g.name + " turned " + g._rotation + ": leans " + a.ToString("F2") + " deg against the floor.");
            }
            foreach (ProceduralGridItem p in rv.Container.GetComponentsInChildren<ProceduralGridItem>(true))
            {
                var m = p != null ? p.footprintTileMatches : null;
                if (m == null || m.Count == 0 || m[0].tile == null) continue;
                if (redo) p.RefreshTransform();
                float a = Vector3.Angle(p.transform.up, m[0].tile.transform.up);
                n++; if (a > 0.5f) leaning++; worst = Mathf.Max(worst, a);
                Core.Log.Msg("[Tilt]   " + p.name + " turned " + p.Rotation + ": leans " + a.ToString("F2") + " deg against its surface.");
            }
            Transform floor = RvMobile.Chassis != null ? RvMobile.Chassis.transform : root;
            Core.Log.Msg("[Tilt] " + n + " item(s), " + leaning + " leaning more than 0.5 deg, worst " + worst.ToString("F2")
                + " deg. RV floor tilt " + Vector3.Angle(floor.up, Vector3.up).ToString("F1") + " deg, fix " + (DebugVanilla ? "off" : "on")
                + (redo ? ", poses set again." : "."));
        }
#endif
    }

    [HarmonyPatch(typeof(GridItem), nameof(GridItem.RefreshTransform))]
    internal static class RvGridItemTiltPatch
    {
        private static void Postfix(GridItem __instance)
        {
            try { RvItemTilt.FixGridItem(__instance); }
            catch (Exception e) { Core.Log.Error("[Tilt] setting a placed item flat on the RV floor failed - it may stand tilted: " + e); }
        }
    }

    [HarmonyPatch(typeof(ProceduralGridItem), nameof(ProceduralGridItem.RefreshTransform))]
    internal static class RvProceduralItemTiltPatch
    {
        private static void Postfix(ProceduralGridItem __instance)
        {
            try { RvItemTilt.FixProceduralItem(__instance); }
            catch (Exception e) { Core.Log.Error("[Tilt] setting an item flat on its surface in the RV failed - it may stand tilted: " + e); }
        }
    }
}
