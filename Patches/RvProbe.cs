#if DEBUG
using System;
using Il2CppScheduleOne.DevUtilities;      // NavMeshUtility
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Tiles;             // Grid, Tile
using RVRepairVan.Managers;
using UnityEngine.AI;

namespace RVRepairVan.Patches
{
    /// <summary>
    /// DEBUG-only measurement pass for the 3.0.0 base upgrades. Reads the values that decide how the crew tier has
    /// to be built and that cannot be answered from the decompiled source, because they are serialized into the
    /// scene: how many employee idle points the RV ships with, what its employee capacity is, whether it has a
    /// loading dock or a build grid, and whether the interior sits on a baked NavMesh.
    ///
    /// Run <c>rvprobe</c> on a loaded save. Compiled out of Release.
    /// </summary>
    internal static class RvProbe
    {
        // Interior sample points, derived from the RV root so they follow the vehicle instead of being pinned to
        // world coordinates. Offsets are in RV local space; the interior floor sits roughly a metre above the root.
        private static readonly Vector3[] InteriorOffsets =
        {
            new Vector3(0f, 1.1f, 0f),
            new Vector3(0f, 1.1f, 1.5f),
            new Vector3(0f, 1.1f, -1.5f),
            new Vector3(1f, 1.1f, 0f),
            new Vector3(-1f, 1.1f, 0f),
        };

        internal static void Run()
        {
            try
            {
                Core.Log.Msg("[Probe] ===== RV property measurement =====");
                Transform root = RVManager.Root;
                if (root == null) { Core.Log.Warning("[Probe] RV root not found - load a save first."); return; }
                Core.Log.Msg("[Probe] root '" + root.name + "' at " + root.position);

                Property prop = root.GetComponent<Property>();
                if (prop == null) { Core.Log.Warning("[Probe] no Property component on the RV root."); return; }

                DumpProperty(prop);
                DumpIdlePoints(prop);
                DumpGrids(prop, root);
                DumpNavMesh(root);
                DumpInterior(root);
                DumpTree(root);
                Core.Log.Msg("[Probe] ===== end =====");
            }
            catch (Exception e) { Core.Log.Warning("[Probe] failed: " + e); }
        }

        private static void DumpProperty(Property prop)
        {
            try
            {
                Core.Log.Msg("[Probe] name='" + prop.PropertyName + "' code='" + prop.PropertyCode
                    + "' owned=" + prop.IsOwned + " capacity=" + prop.EmployeeCapacity);
                int docks = prop.LoadingDocks != null ? prop.LoadingDocks.Length : -1;
                int emps = prop.Employees != null ? prop.Employees.Count : -1;
                Core.Log.Msg("[Probe] loadingDocks=" + docks + " loadingDockCount=" + prop.LoadingDockCount
                    + " employees=" + emps);
            }
            catch (Exception e) { Core.Log.Warning("[Probe] property fields: " + e.Message); }
        }

        // The load-bearing number: Employee.AssignProperty indexes EmployeeIdlePoints[EmployeeIndex] unguarded, so an
        // array shorter than the capacity throws mid-Initialize and leaves a half-built employee standing around.
        private static void DumpIdlePoints(Property prop)
        {
            try
            {
                var pts = prop.EmployeeIdlePoints;
                int n = pts != null ? pts.Length : -1;
                Core.Log.Msg("[Probe] employeeIdlePoints=" + n + " (capacity=" + prop.EmployeeCapacity + ")"
                    + (n >= 0 && n < prop.EmployeeCapacity ? "  <-- SHORTER THAN CAPACITY" : ""));
                for (int i = 0; i < n; i++)
                {
                    Transform t = pts[i];
                    Core.Log.Msg("[Probe]   idle[" + i + "] " + (t == null ? "<null>" : t.name + " @ " + t.position));
                }
            }
            catch (Exception e) { Core.Log.Warning("[Probe] idle points: " + e.Message); }
        }

        private static void DumpGrids(Property prop, Transform root)
        {
            try
            {
                var grids = prop.Grids;
                Core.Log.Msg("[Probe] property.Grids=" + (grids != null ? grids.Count.ToString() : "null"));
                if (grids != null)
                    for (int i = 0; i < grids.Count; i++)
                    {
                        Grid g = grids[i];
                        if (g == null) { Core.Log.Msg("[Probe]   grid[" + i + "] <null>"); continue; }
                        int tiles = g.Tiles != null ? g.Tiles.Count : -1;
                        Core.Log.Msg("[Probe]   grid[" + i + "] '" + g.name + "' active=" + g.gameObject.activeInHierarchy
                            + " tiles=" + tiles + " pos=" + g.transform.position);
                    }

                // Also sweep the hierarchy: a grid parented under the RV but not registered on the Property would
                // not show above, and that difference decides whether the workshop-floor tier can reuse it.
                // Where do the tiles actually sit, and does the property's bounding box still contain them? A tile
                // outside the RV reads as unplaceable ("red") in the build UI, which is the first thing to rule
                // out when the floor looks blocked.
                if (grids != null)
                    for (int i = 0; i < grids.Count; i++)
                    {
                        Grid g = grids[i];
                        if (g == null || g.Tiles == null) continue;
                        int outside = 0, occupied = 0;
                        Vector3 min = Vector3.one * 9999f, max = Vector3.one * -9999f;
                        for (int k = 0; k < g.Tiles.Count; k++)
                        {
                            Tile tl = g.Tiles[k];
                            if (tl == null) continue;
                            Vector3 p = tl.transform.position;
                            min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                            try { if (!prop.DoBoundsContainPoint(p)) outside++; } catch (Exception e) { Core.Swallowed("RvProbe.DumpGrids.DoBoundsContainPoint", e); }
                            try { if (tl.BuildableOccupants != null && tl.BuildableOccupants.Count > 0) occupied++; } catch (Exception e) { Core.Swallowed("RvProbe.DumpGrids.BuildableOccupants", e); }
                        }
                        Core.Log.Msg("[Probe]   grid[" + i + "] tile extent x " + min.x.ToString("F1") + ".." + max.x.ToString("F1")
                            + "  z " + min.z.ToString("F1") + ".." + max.z.ToString("F1") + "  y " + min.y.ToString("F2")
                            + "   outsideBounds=" + outside + " occupied=" + occupied);
                    }

                var inChildren = root.GetComponentsInChildren<Grid>(true);
                Core.Log.Msg("[Probe] grids under the RV hierarchy=" + (inChildren != null ? inChildren.Length : 0));
                if (inChildren != null)
                    foreach (Grid g in inChildren)
                    {
                        if (g == null) continue;
                        int tiles = g.Tiles != null ? g.Tiles.Count : -1;
                        Core.Log.Msg("[Probe]   child grid '" + g.name + "' active=" + g.gameObject.activeInHierarchy
                            + " tiles=" + tiles + " path=" + PathOf(g.transform));
                    }
            }
            catch (Exception e) { Core.Log.Warning("[Probe] grids: " + e.Message); }
        }

        // Employees walk on the "Humanoid" agent (NPCMovement.SetAgentType). If the interior is off-mesh, every
        // reachability check the work loop makes returns null and the employee reports nothing to do.
        private static void DumpNavMesh(Transform root)
        {
            try
            {
                int humanoid = NavMeshUtility.GetNavMeshAgentID("Humanoid");
                Core.Log.Msg("[Probe] agentTypeID Humanoid=" + humanoid);

                var filter = new NavMeshQueryFilter { agentTypeID = humanoid, areaMask = -1 };
                foreach (Vector3 off in InteriorOffsets)
                {
                    Vector3 world = root.TransformPoint(off);
                    NavMeshHit hit;
                    bool ok = NavMesh.SamplePosition(world, out hit, 2f, filter);
                    Core.Log.Msg("[Probe]   navmesh at " + world + " -> " + (ok
                        ? "HIT " + hit.position + " dist=" + hit.distance.ToString("F2")
                        : "no mesh within 2m"));
                }
            }
            catch (Exception e) { Core.Log.Warning("[Probe] navmesh: " + e.Message); }
        }

        private static void DumpInterior(Transform root)
        {
            try
            {
                Transform interior = root.Find("RV/rv/Main/Interior") ?? root.Find("rv/Main/Interior");
                if (interior == null) { Core.Log.Msg("[Probe] interior transform not found."); return; }
                Core.Log.Msg("[Probe] interior '" + PathOf(interior) + "' children=" + interior.childCount);
                for (int i = 0; i < interior.childCount; i++)
                {
                    Transform c = interior.GetChild(i);
                    Core.Log.Msg("[Probe]   [" + i + "] '" + c.name + "' active=" + c.gameObject.activeSelf);
                }
                Transform wall = root.Find("RV/rv/Main/Wall.001") ?? root.Find("rv/Main/Wall.001");
                Core.Log.Msg("[Probe] Wall.001 " + (wall == null ? "not found" : "found, active=" + wall.gameObject.activeSelf));
            }
            catch (Exception e) { Core.Log.Warning("[Probe] interior: " + e.Message); }
        }

        /// <summary>
        /// Dump the whole intact-RV model tree with each node's active state and whether it renders anything.
        /// The "gut the interior" list has to come from this, not from another mod's hard-coded names: clutter
        /// like the tarp, radio, vase and ashtray hangs directly off the model root, not under Interior.
        /// </summary>
        private static void DumpTree(Transform root)
        {
            try
            {
                Transform model = root.Find("RV");
                if (model == null) { Core.Log.Msg("[Probe] intact model 'RV' not found."); return; }
                Core.Log.Msg("[Probe] ----- intact model tree (r = has a Renderer) -----");
                Walk(model, 0);
                Core.Log.Msg("[Probe] ----- end tree -----");
            }
            catch (Exception e) { Core.Log.Warning("[Probe] tree: " + e.Message); }
        }

        private static void Walk(Transform t, int depth)
        {
            if (depth > 4) return;   // deep enough to find clutter, shallow enough to stay readable
            string pad = new string(' ', depth * 2);
            bool rend = false;
            try { rend = t.GetComponent<Renderer>() != null; } catch (Exception e) { Core.Swallowed("RvProbe.Walk", e); }
            Core.Log.Msg("[Probe]   " + pad + (t.gameObject.activeSelf ? "[on ] " : "[off] ")
                + (rend ? "r " : "  ") + t.name + "  (" + t.childCount + ")");
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1);
        }

        private static string PathOf(Transform t)
        {
            string s = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }
    }
}
#endif
