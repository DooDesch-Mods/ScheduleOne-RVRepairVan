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
    /// <c>transform.Rotate(&lt;a world axis&gt;, angle)</c>, and that call reads the axis as a local one. Items on a table
    /// or rack get their facing from <c>transform.forward</c>, which keeps world up. Both only hold while the floor is
    /// level: on a slope a bed or a station turned by 90, 180 or 270 degrees came out tilted against the RV floor.
    /// Here the game's own math runs in the frame the RV had at home, where the floor is level, and the result is
    /// carried to where the RV stands now. At home that is exactly the game's pose.
    /// </summary>
    internal static class RvItemTilt
    {
        // Below this the RV stands as it did at home, and the game's own pose is already right.
        private const float SameDegrees = 0.05f;

#if DEBUG
        /// <summary>rvtilt vanilla: leave the game's pose alone, to show the tilt it produces.</summary>
        internal static bool DebugVanilla;
#endif

        // How the RV is turned and tilted against its pose at home, for an item on the chassis. False at home, off the
        // RV, or with the RV standing as it does at home.
        private static bool AwayFrame(Transform t, out Quaternion away)
        {
            away = Quaternion.identity;
            LandVehicle chassis = RvMobile.Chassis;
            if (chassis == null || t == null || !t.IsChildOf(chassis.transform)) return false;
            away = RvMobile.AwayRotation;
            return Quaternion.Angle(away, Quaternion.identity) > SameDegrees;
        }

        internal static void FixGridItem(GridItem item)
        {
#if DEBUG
            if (DebugVanilla) return;
#endif
            Grid grid = item.OwnerGrid;
            if (grid == null || !AwayFrame(grid.transform, out Quaternion away)) return;
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
            // The game: rotation = grid * inverse(build point, local) then Rotate(build point up, angle), read as local.
            Quaternion toHome = Quaternion.Inverse(away);
            Quaternion gridHome = toHome * grid.transform.rotation;
            Quaternion buildPointLocal = Quaternion.Inverse(t.rotation) * bp.rotation;
            Quaternion home = gridHome * Quaternion.Inverse(buildPointLocal);
            home *= Quaternion.AngleAxis(item._rotation, gridHome * Vector3.up);
            t.rotation = away * home;
            t.position = tile.transform.position - (origin.transform.position - t.position);
            Core.Log.Msg("[Tilt] '" + item.name + "' set as at home on the tilted RV floor (turned " + item._rotation + ").");
        }

        internal static void FixProceduralItem(ProceduralGridItem item)
        {
#if DEBUG
            if (DebugVanilla) return;
#endif
            var matches = item.footprintTileMatches;
            if (matches == null || matches.Count == 0) return;
            ProceduralTile tile = matches[0].tile;
            if (tile == null || !AwayFrame(tile.transform, out Quaternion away)) return;
            Transform t = item.transform;
            FootprintTile foot = item.GetFootprintTile(matches[0].coord);
            if (foot == null)
            {
                Core.Log.Warning("[Tilt] '" + item.name + "' has no footprint tile - its pose stays as the game set it.");
                return;
            }
            // The game: forward = tile forward (with world up), then Rotate(tile up, angle), read as local.
            Quaternion toHome = Quaternion.Inverse(away);
            Vector3 forward = toHome * tile.transform.forward, up = toHome * tile.transform.up;
            Quaternion home = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.AngleAxis(item.Rotation, up);
            t.rotation = away * home;
            t.position = tile.transform.position - (foot.transform.position - t.position);
            Core.Log.Msg("[Tilt] '" + item.name + "' set as at home on its tilted surface in the RV (turned " + item.Rotation + ").");
        }

#if DEBUG
        /// <summary>rvtilt lean: rolls the parked RV by that many degrees, to put its floor on a slope.</summary>
        internal static void Lean(float degrees)
        {
            LandVehicle chassis = RvMobile.Chassis;
            if (chassis == null) { Core.Log.Warning("[Tilt] lean: no chassis out - 'rvdrive', then 'rvdrive exit'."); return; }
            Transform ct = chassis.transform;
            ct.rotation = Quaternion.AngleAxis(degrees, ct.forward) * ct.rotation;
            Core.Log.Msg("[Tilt] lean: RV rolled by " + degrees.ToString("F1") + " deg, floor tilt now "
                + Vector3.Angle(ct.up, Vector3.up).ToString("F1") + " deg.");
        }

        /// <summary>
        /// rvtilt: how far each placed item in the RV leans against the floor it stands on. 'redo' lets the game set
        /// every pose again first, as it does when an item is placed or joins a client.
        /// </summary>
        internal static void Report(bool redo)
        {
            Transform root = RVManager.Root;
            RV rv = root != null ? root.GetComponent<RV>() : null;
            if (rv == null || rv.Container == null) { Core.Log.Warning("[Tilt] no RV contents to measure."); return; }
            // Every item stands upright at home, ceiling lights included, so its lean is measured against the RV's up.
            Vector3 rvUp = RvMobile.AwayRotation * Vector3.up;
            int n = 0, leaning = 0;
            float worst = 0f;
            foreach (GridItem g in rv.Container.GetComponentsInChildren<GridItem>(true))
            {
                if (g == null || g.OwnerGrid == null) continue;
                if (redo) g.RefreshTransform();
                float a = Vector3.Angle(g.transform.up, rvUp);
                n++; if (a > 0.5f) leaning++; worst = Mathf.Max(worst, a);
                Core.Log.Msg("[Tilt]   " + g.name + " turned " + g._rotation + ": leans " + a.ToString("F2") + " deg against the RV.");
            }
            foreach (ProceduralGridItem p in rv.Container.GetComponentsInChildren<ProceduralGridItem>(true))
            {
                var m = p != null ? p.footprintTileMatches : null;
                if (m == null || m.Count == 0 || m[0].tile == null) continue;
                if (redo) p.RefreshTransform();
                float a = Vector3.Angle(p.transform.up, rvUp);
                n++; if (a > 0.5f) leaning++; worst = Mathf.Max(worst, a);
                Core.Log.Msg("[Tilt]   " + p.name + " turned " + p.Rotation + ": leans " + a.ToString("F2") + " deg against the RV.");
            }
            Core.Log.Msg("[Tilt] " + n + " item(s), " + leaning + " leaning more than 0.5 deg, worst " + worst.ToString("F2")
                + " deg. RV floor tilt " + Vector3.Angle(rvUp, Vector3.up).ToString("F1") + " deg, fix " + (DebugVanilla ? "off" : "on")
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
