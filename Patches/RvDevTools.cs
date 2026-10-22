#if DEBUG
using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Building;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Tiles;
using RVRepairVan.Managers;

namespace RVRepairVan.Patches
{
    /// <summary>
    /// DEBUG-only console tools for the crew and the gutted interior:
    /// <list type="bullet">
    /// <item><c>rvparts [path]</c> lists the children of a node under the intact model, with their position in the RV's frame.</item>
    /// <item><c>rvtoggle &lt;path&gt; [on|off]</c> switches one model node, to see what a hidden part was covering.</item>
    /// <item><c>rvplace &lt;itemId&gt; [rotation]</c> builds an item on the RV floor through BuildManager, the call a player's placement makes.</item>
    /// <item><c>rvcrew</c> dumps every employee of the RV: where they stand, home, pay, work issues, active behaviour.</item>
    /// <item><c>rvassign</c> gives each RV botanist a free locker in the RV, the first storage rack as supplies and every pot there.</item>
    /// <item><c>rvreach</c> replays the botanist's accessibility test (bounds + NavMesh path) for every access point in the RV.</item>
    /// <item><c>rvstock</c> fills the first storage rack in the RV with soil and seeds, the botanists' supplies.</item>
    /// <item><c>rvpay [amount]</c> puts a cash stack into every locker in the RV; vanilla pays the wage out of it.</item>
    /// <item><c>rvgrid</c> logs how far each RV build grid reaches, in the RV's frame, to compare with the cab at z -4.</item>
    /// </list>
    /// </summary>
    internal static class RvDevTools
    {
        internal static void Parts(string path)
        {
            Transform model = Model();
            if (model == null) { Core.Log.Warning("[Debug] rvparts: intact RV model not found."); return; }
            Transform node = string.IsNullOrEmpty(path) ? model : model.Find(path);
            if (node == null) { Core.Log.Warning("[Debug] rvparts: '" + path + "' not found under the model."); return; }
            Transform root = RVManager.Root;
            Core.Log.Msg("[Debug] rvparts '" + (string.IsNullOrEmpty(path) ? "<model>" : path) + "': " + node.childCount + " child(ren)");
            for (int i = 0; i < node.childCount; i++)
            {
                Transform c = node.GetChild(i);
                Vector3 local = root.InverseTransformPoint(c.position);
                Core.Log.Msg("  " + (c.gameObject.activeSelf ? "ON  " : "off ") + c.name + "  at " + local + "  children=" + c.childCount);
            }
        }

        internal static void Toggle(string path, string state)
        {
            Transform model = Model();
            if (model == null) { Core.Log.Warning("[Debug] rvtoggle: intact RV model not found."); return; }
            Transform t = model.Find(path);
            if (t == null) { Core.Log.Warning("[Debug] rvtoggle: '" + path + "' not found under the model."); return; }
            bool on = state == "on" || (state != "off" && !t.gameObject.activeSelf);
            t.gameObject.SetActive(on);
            Core.Log.Msg("[Debug] rvtoggle " + path + " -> " + (on ? "on" : "off"));
        }

        internal static void Place(string itemId, int rotation, int onlyGrid = -1)
        {
            Property rv = RvProperty();
            if (rv == null) { Core.Log.Warning("[Debug] rvplace: RV property not found."); return; }
            var def = Il2CppScheduleOne.Registry.GetItem(itemId)?.TryCast<Il2CppScheduleOne.ItemFramework.BuildableItemDefinition>();
            if (def == null) { Core.Log.Warning("[Debug] rvplace: '" + itemId + "' is not a buildable item."); return; }
            GridItem prefab = def.BuiltItem != null ? def.BuiltItem.TryCast<GridItem>() : null;
            if (prefab == null) { Core.Log.Warning("[Debug] rvplace: '" + itemId + "' does not go on a grid."); return; }
            var build = Il2CppScheduleOne.DevUtilities.NetworkSingleton<BuildManager>.Instance;
            if (build == null) { Core.Log.Warning("[Debug] rvplace: no BuildManager in this scene."); return; }

            int w = prefab.FootprintX, h = prefab.FootprintY;
            if (rotation == 90 || rotation == 270) { int s = w; w = h; h = s; }

            for (int g = 0; rv.Grids != null && g < rv.Grids.Count; g++)
            {
                if (onlyGrid >= 0 && g != onlyGrid) continue;
                Grid grid = rv.Grids[g];
                if (grid == null || !grid.gameObject.activeInHierarchy || grid.Tiles == null) continue;
                for (int t = 0; t < grid.Tiles.Count; t++)
                {
                    Tile tile = grid.Tiles[t];
                    if (tile == null || !BlockFree(grid, tile.x, tile.y, w, h)) continue;
                    GridItem placed = build.CreateGridItem(def.GetDefaultInstance(1), grid, new Vector2(tile.x, tile.y), rotation);
                    if (placed == null) { Core.Log.Warning("[Debug] rvplace: CreateGridItem returned null at (" + tile.x + "," + tile.y + ")."); return; }
                    Core.Log.Msg("[Debug] rvplace: '" + itemId + "' (" + w + "x" + h + ") on grid '" + grid.name + "' at tile ("
                        + tile.x + "," + tile.y + "), world " + placed.transform.position);
                    return;
                }
            }
            Core.Log.Warning("[Debug] rvplace: no free " + w + "x" + h + " block on the RV floor.");
        }

        internal static void Crew()
        {
            Property rv = RvProperty();
            if (rv == null) { Core.Log.Warning("[Debug] rvcrew: RV property not found."); return; }
            int count = rv.Employees != null ? rv.Employees.Count : 0;
            Core.Log.Msg("[Debug] rvcrew: capacity=" + rv.EmployeeCapacity + " employees=" + count
                + " idlePoints=" + (rv.EmployeeIdlePoints != null ? rv.EmployeeIdlePoints.Length : 0));
            Vector3 rvPos = RVManager.Root != null ? RVManager.Root.position : Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                Employee e = rv.Employees[i];
                if (e == null) continue;
                string active = e.Behaviour != null && e.Behaviour.activeBehaviour != null ? e.Behaviour.activeBehaviour.Name : "<none>";
                string issues = "";
                for (int k = 0; e.WorkIssues != null && k < e.WorkIssues.Count; k++) issues += " | " + e.WorkIssues[k].Reason;
                Core.Log.Msg("  " + e.FullName + " (" + e.EmployeeType + ") at " + e.transform.position
                    + " dist=" + Vector3.Distance(e.transform.position, rvPos).ToString("F1")
                    + " home=" + (e.GetHome() != null) + " paid=" + e.PaidForToday + " canWork=" + e.CanWork()
                    + " waitingOutside=" + e.IsWaitingOutside + " behaviour=" + active + " issues:" + (issues == "" ? " none" : issues));
            }
        }

        internal static void Assign()
        {
            Property rv = RvProperty();
            if (rv == null) { Core.Log.Warning("[Debug] rvassign: RV property not found."); return; }

            var homes = new List<BuildableItem>();
            var pots = new List<Pot>();
            for (int i = 0; rv.BuildableItems != null && i < rv.BuildableItems.Count; i++)
            {
                BuildableItem b = rv.BuildableItems[i];
                if (b == null) continue;
                EmployeeHome home = b.GetComponent<EmployeeHome>();
                if (home != null && home.AssignedEmployee == null) homes.Add(b);
                Pot pot = b.TryCast<Pot>();
                if (pot != null) pots.Add(pot);
            }
            PlaceableStorageEntity rack = Rack(rv);
            Core.Log.Msg("[Debug] rvassign: " + homes.Count + " free locker(s), " + pots.Count + " pot(s) in the RV.");

            for (int i = 0; rv.Employees != null && i < rv.Employees.Count; i++)
            {
                Botanist bot = rv.Employees[i]?.TryCast<Botanist>();
                if (bot == null) continue;
                var cfg = bot.configuration;
                if (cfg == null) { Core.Log.Warning("[Debug] rvassign: " + bot.FullName + " has no configuration yet."); continue; }
                if (bot.GetHome() == null && homes.Count > 0)
                {
                    cfg.Home.SetObject(homes[0], true);
                    homes.RemoveAt(0);
                    Core.Log.Msg("[Debug] rvassign: locker -> " + bot.FullName);
                }
                if (rack != null && cfg.Supplies.SelectedObject == null)
                {
                    cfg.Supplies.SetObject(rack, true);
                    Core.Log.Msg("[Debug] rvassign: supplies rack -> " + bot.FullName);
                }
                foreach (Pot p in pots)
                {
                    if (cfg.Assigns.SelectedObjects.Contains(p)) continue;
                    cfg.Assigns.AddItem(p);
                    Core.Log.Msg("[Debug] rvassign: pot at " + p.transform.position + " -> " + bot.FullName);
                }
            }
        }

        /// <summary>
        /// rvgrid tiles [item]: every tile of every RV grid through the check the build ghost runs on a footprint tile
        /// (Grid.IsTileValidAtCoordinate), split into the RV's own tiles and the ones the workshop floor added, with
        /// why a tile says no: occupants (dead ones counted apart), offset, not owned, or no collider for the ghost.
        /// </summary>
        internal static void TileCheck(string itemId)
        {
            Property rv = RvProperty();
            if (rv == null) { Core.Log.Warning("[Debug] rvgrid tiles: RV property not found."); return; }
            var def = Il2CppScheduleOne.Registry.GetItem(itemId)?.TryCast<Il2CppScheduleOne.ItemFramework.BuildableItemDefinition>();
            GridItem prefab = def != null && def.BuiltItem != null ? def.BuiltItem.TryCast<GridItem>() : null;
            if (prefab == null || prefab.CoordinateFootprintTilePairs == null || prefab.CoordinateFootprintTilePairs.Count == 0)
            { Core.Log.Warning("[Debug] rvgrid tiles: '" + itemId + "' is no grid item with a footprint."); return; }
            FootprintTile foot = prefab.CoordinateFootprintTilePairs[0].footprintTile;
            for (int g = 0; rv.Grids != null && g < rv.Grids.Count; g++)
            {
                Grid grid = rv.Grids[g];
                if (grid == null || grid.Tiles == null) continue;
                foreach (bool ours in new[] { false, true })
                {
                    int n = 0, ok = 0, occupied = 0, dead = 0, offset = 0, unowned = 0, noCollider = 0, notMapped = 0;
                    string firstOccupant = null;
                    for (int t = 0; t < grid.Tiles.Count; t++)
                    {
                        Tile tile = grid.Tiles[t];
                        if (tile == null || tile.gameObject.name.StartsWith("RVRepairVan_Tile_", StringComparison.Ordinal) != ours) continue;
                        n++;
                        var c = new Coordinate(tile.x, tile.y);
                        if (grid.IsTileValidAtCoordinate(c, foot, prefab)) ok++;
                        if (grid.GetTile(c) == null || grid.GetTile(c).Pointer != tile.Pointer) notMapped++;
                        var occ = tile.BuildableOccupants;
                        if (occ != null && occ.Count > 0)
                        {
                            occupied++;
                            for (int o = 0; o < occ.Count; o++)
                            {
                                GridItem gi = occ[o];
                                if (gi == null) { dead++; if (firstOccupant == null) firstOccupant = "(destroyed)"; }
                                else if (firstOccupant == null) firstOccupant = gi.name;
                            }
                        }
                        if (tile.AvailableOffset != 0f && foot.RequiredOffset != 0f && tile.AvailableOffset < foot.RequiredOffset) offset++;
                        if (!tile.CanBeBuiltOn()) unowned++;
                        bool col = false;
                        foreach (Collider cl in tile.GetComponentsInChildren<Collider>(false)) if (cl.enabled) { col = true; break; }
                        if (!col) noCollider++;
                    }
                    if (n == 0) continue;
                    Core.Log.Msg("[Debug] rvgrid tiles '" + grid.name + "' (active=" + grid.gameObject.activeInHierarchy + ") " + (ours ? "added" : "RV's own")
                        + ": " + n + " tile(s), " + ok + " take '" + itemId + "'. No because: occupied " + occupied + " (" + dead + " by destroyed items"
                        + (firstOccupant != null ? ", first '" + firstOccupant + "'" : "") + "), offset " + offset + ", not owned " + unowned
                        + ", not in the grid's map " + notMapped + "; no collider for the ghost " + noCollider + ".");
                }
            }
        }

        internal static void GridExtent()
        {
            Property rv = RvProperty();
            Transform root = RVManager.Root;
            if (rv == null || root == null) { Core.Log.Warning("[Debug] rvgrid: RV property or root not found."); return; }
            for (int g = 0; rv.Grids != null && g < rv.Grids.Count; g++)
            {
                Grid grid = rv.Grids[g];
                if (grid == null) continue;
                int n = grid.Tiles != null ? grid.Tiles.Count : 0, occupied = 0, colliders = 0, collidersOn = 0;
                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                for (int t = 0; t < n; t++)
                {
                    Tile tile = grid.Tiles[t];
                    if (tile == null) continue;
                    if (tile.BuildableOccupants != null && tile.BuildableOccupants.Count > 0) occupied++;
                    foreach (Collider c in tile.GetComponentsInChildren<Collider>(true)) { colliders++; if (c.enabled) collidersOn++; }
                    Vector3 p = root.InverseTransformPoint(tile.transform.position);
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                }
                Core.Log.Msg("[Debug] rvgrid '" + grid.name + "' active=" + grid.gameObject.activeInHierarchy + " tiles=" + n + " occupied=" + occupied + " colliders on=" + collidersOn + "/" + colliders
                    + (n > 0 ? " x " + min.x.ToString("F2") + ".." + max.x.ToString("F2") + " z " + min.z.ToString("F2") + ".." + max.z.ToString("F2") : ""));
            }
            Transform model = RVManager.Model ?? root;
            for (int i = 0; rv.BuildableItems != null && i < rv.BuildableItems.Count; i++)
            {
                BuildableItem item = rv.BuildableItems[i];
                if (item == null) { Core.Log.Warning("[Debug] rvgrid item " + i + " is null."); continue; }
                Transform t = item.transform;
                int shown = 0, all = 0;
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true)) { all++; if (r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff) shown++; }
                Core.Log.Msg("[Debug] rvgrid item '" + item.name + "' at model " + model.InverseTransformPoint(t.position).ToString("F2")
                    + " active=" + item.gameObject.activeInHierarchy + " renderers " + shown + "/" + all
                    + " parent=" + (t.parent != null ? t.parent.name : "none") + " layer=" + LayerMask.LayerToName(item.gameObject.layer));
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>(false))
                    Core.Log.Msg("[Debug] rvgrid   " + r.name + " enabled=" + r.enabled + " forcedOff=" + r.forceRenderingOff + " visible=" + r.isVisible
                        + " layer=" + LayerMask.LayerToName(r.gameObject.layer) + " at model " + model.InverseTransformPoint(r.bounds.center).ToString("F2")
                        + " size " + r.bounds.size.ToString("F2"));
            }
        }

        /// <summary>rvgrid scan: where the cabin's walls and floor stand in the RV root frame, by raycasts from the grid.</summary>
        internal static void ScanCabin()
        {
            Property rv = RvProperty();
            Transform root = RVManager.Root;
            if (rv == null || root == null || rv.Grids == null || rv.Grids.Count == 0 || rv.Grids[0] == null || rv.Grids[0].Tiles.Count == 0)
            { Core.Log.Warning("[Debug] rvgrid scan: RV property, root or grid not found."); return; }
            float floorY = root.InverseTransformPoint(rv.Grids[0].Tiles[0].transform.position).y;
            foreach (float h in new[] { 0.3f, 1.2f })
            {
                float y = floorY + h;
                var sb = new System.Text.StringBuilder("[Debug] rvgrid scan h=" + h + " x walls per z:");
                for (float z = -7f; z <= 7.01f; z += 0.5f)
                    sb.Append(" z" + z.ToString("0.0") + "[" + Hit(root, new Vector3(0f, y, z), Vector3.left, 4f) + "|" + Hit(root, new Vector3(0f, y, z), Vector3.right, 4f) + "]");
                Core.Log.Msg(sb.ToString());
                sb = new System.Text.StringBuilder("[Debug] rvgrid scan h=" + h + " z walls per x:");
                for (float x = -1.5f; x <= 1.51f; x += 0.5f)
                    sb.Append(" x" + x.ToString("0.0") + "[" + Hit(root, new Vector3(x, y, 0f), Vector3.back, 9f) + "|" + Hit(root, new Vector3(x, y, 0f), Vector3.forward, 9f) + "]");
                Core.Log.Msg(sb.ToString());
            }
            var fl = new System.Text.StringBuilder("[Debug] rvgrid scan floor y per z (x=0):");
            for (float z = -7f; z <= 7.01f; z += 0.5f)
                fl.Append(" z" + z.ToString("0.0") + "=" + Hit(root, new Vector3(0f, floorY + 1.5f, z), Vector3.down, 3f));
            Core.Log.Msg(fl.ToString());
        }

        // First solid hit along a root-frame ray, as the root-frame coordinate on the ray's axis plus the collider name.
        private static string Hit(Transform root, Vector3 localFrom, Vector3 localDir, float max)
        {
            Vector3 from = root.TransformPoint(localFrom), dir = root.TransformDirection(localDir);
            var hits = Physics.RaycastAll(from, dir, max, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit best = default; bool any = false;
            foreach (RaycastHit hit in hits)
            {
                Collider c = hit.collider;
                if (c == null || c.GetComponentInParent<Tile>() != null || c.GetComponentInParent<Player>() != null) continue;
                if (!any || hit.distance < best.distance) { best = hit; any = true; }
            }
            if (!any) return "-";
            Vector3 p = root.InverseTransformPoint(best.point);
            float v = localDir.x != 0f ? p.x : localDir.z != 0f ? p.z : p.y;
            return v.ToString("0.00") + " " + best.collider.name;
        }

        internal static void Reach()
        {
            Property rv = RvProperty();
            Transform root = RVManager.Root;
            if (rv == null || root == null) { Core.Log.Warning("[Debug] rvreach: RV property or root not found."); return; }
            Employee emp = rv.Employees != null && rv.Employees.Count > 0 ? rv.Employees[0] : null;
            if (emp == null) { Core.Log.Warning("[Debug] rvreach: no employee in the RV to test with."); return; }
            Vector3 from = emp.transform.position;
            var nav = emp.GetComponent<UnityEngine.AI.NavMeshAgent>();
            Core.Log.Msg("[Debug] rvreach: from " + emp.FullName + " at RV-frame " + root.InverseTransformPoint(from)
                + " onMesh=" + OnMesh(from, out float d0) + " (" + d0.ToString("F2") + "m) agentType="
                + (nav != null ? nav.agentTypeID.ToString() : "none") + " agentOnMesh=" + (nav != null && nav.isOnNavMesh));

            for (int i = 0; rv.BuildableItems != null && i < rv.BuildableItems.Count; i++)
            {
                BuildableItem b = rv.BuildableItems[i];
                if (b == null) continue;
                Il2CppReferenceArray<Transform> points = null;
                var grow = b.TryCast<Il2CppScheduleOne.Growing.GrowContainer>();
                var rack = b.TryCast<PlaceableStorageEntity>();
                if (grow != null) points = grow.AccessPoints;
                else if (rack != null) points = rack.AccessPoints;
                else continue;
                Core.Log.Msg("  " + b.name + " at RV-frame " + root.InverseTransformPoint(b.transform.position) + ", " + (points != null ? points.Length : 0) + " access point(s)");
                for (int k = 0; points != null && k < points.Length; k++)
                {
                    Transform ap = points[k];
                    if (ap == null) { Core.Log.Msg("    [" + k + "] null"); continue; }
                    Vector3 p = ap.position;
                    bool inBounds = rv.DoBoundsContainPoint(p);
                    bool canGet = emp.Movement.CanGetTo(p, 1f);
                    var path = new UnityEngine.AI.NavMeshPath();
                    bool sampled = OnMesh(p, out float d);
                    string status = "-";
                    if (UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 2f, -1)
                        && UnityEngine.AI.NavMesh.CalculatePath(from, hit.position, -1, path))
                        status = path.status.ToString();
                    Core.Log.Msg("    [" + k + "] RV-frame " + root.InverseTransformPoint(p) + " inBounds=" + inBounds + " canGetTo=" + canGet
                        + " onMesh=" + sampled + " (" + d.ToString("F2") + "m) path=" + status);
                }
            }
        }

        private static bool OnMesh(Vector3 p, out float distance)
        {
            distance = -1f;
            if (!UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 2f, -1)) return false;
            distance = Vector3.Distance(p, hit.position);
            return true;
        }

        internal static void Stock()
        {
            Property rv = RvProperty();
            if (rv == null) { Core.Log.Warning("[Debug] rvstock: RV property not found."); return; }
            PlaceableStorageEntity rack = Rack(rv);
            if (rack == null || rack.StorageEntity == null) { Core.Log.Warning("[Debug] rvstock: no storage rack in the RV - place one with rvplace."); return; }
            foreach (string id in new[] { "soil", "ogkushseed" })
            {
                var item = Il2CppScheduleOne.Registry.GetItem(id)?.GetDefaultInstance(10);
                if (item == null) { Core.Log.Warning("[Debug] rvstock: '" + id + "' is not an item."); continue; }
                if (!rack.StorageEntity.CanItemFit(item, 10)) { Core.Log.Warning("[Debug] rvstock: no room for '" + id + "'."); continue; }
                rack.StorageEntity.InsertItem(item, true);
                Core.Log.Msg("[Debug] rvstock: 10x '" + id + "' into the rack at " + rack.transform.position + ".");
            }
        }

        private static PlaceableStorageEntity Rack(Property rv)
        {
            for (int i = 0; rv.BuildableItems != null && i < rv.BuildableItems.Count; i++)
            {
                BuildableItem b = rv.BuildableItems[i];
                if (b == null || b.GetComponent<EmployeeHome>() != null) continue;
                PlaceableStorageEntity rack = b.TryCast<PlaceableStorageEntity>();
                if (rack != null) return rack;
            }
            return null;
        }

        internal static void Pay(float amount)
        {
            Property rv = RvProperty();
            if (rv == null) { Core.Log.Warning("[Debug] rvpay: RV property not found."); return; }
            int paid = 0;
            for (int i = 0; rv.BuildableItems != null && i < rv.BuildableItems.Count; i++)
            {
                EmployeeHome home = rv.BuildableItems[i]?.GetComponent<EmployeeHome>();
                if (home == null || home.Storage == null) continue;
                var cash = Il2CppScheduleOne.Registry.GetItem("cash")?.GetDefaultInstance(1)?.TryCast<Il2CppScheduleOne.ItemFramework.CashInstance>();
                if (cash == null) { Core.Log.Warning("[Debug] rvpay: could not create a cash item."); return; }
                cash.SetBalance(amount);
                if (!home.Storage.CanItemFit(cash, 1)) { Core.Log.Warning("[Debug] rvpay: locker at " + home.transform.position + " is full."); continue; }
                home.Storage.InsertItem(cash, true);
                paid++;
                Core.Log.Msg("[Debug] rvpay: $" + amount + " into the locker at " + home.transform.position + " - it now holds $" + home.GetCashSum() + ".");
            }
            if (paid == 0) Core.Log.Warning("[Debug] rvpay: no locker in the RV took cash.");
        }

        private static bool BlockFree(Grid grid, int x0, int y0, int w, int h)
        {
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    Tile tile = grid.GetTile(new Coordinate(x0 + x, y0 + y));
                    if (tile == null) return false;
                    if (tile.BuildableOccupants != null && tile.BuildableOccupants.Count > 0) return false;
                    if (!tile.CanBeBuiltOn()) return false;
                }
            return true;
        }

        /// <summary>
        /// rvsow [elsewhere]: drop a seed into the first empty pot of the RV (or of any other property) the way
        /// SowSeedTask does - soil parted, chunk colliders and task bounds on, the seed loose under _Temp - and log
        /// where it falls. The vial is left out: in the task the seed leaves it before it falls.
        /// </summary>
        internal static void Sow(bool elsewhere)
        {
            Property rv = RvProperty();
            Pot pot = null;
            foreach (Pot p in UnityEngine.Object.FindObjectsOfType<Pot>())
            {
                if (p == null || p.ParentProperty == null) continue;
                bool inRv = rv != null && p.ParentProperty == rv;
                if (inRv == elsewhere) continue;
                pot = p;
                break;
            }
            if (pot == null) { Core.Log.Warning("[Debug] rvsow: no pot " + (elsewhere ? "outside" : "in") + " the RV."); return; }
            var def = Il2CppScheduleOne.Registry.GetItem("ogkushseed")?.TryCast<Il2CppScheduleOne.Growing.SeedDefinition>();
            if (def == null || def.FunctionSeedPrefab == null) { Core.Log.Warning("[Debug] rvsow: no seed prefab for 'ogkushseed'."); return; }
            GameObject temp = GameObject.Find("_Temp");
            if (temp == null) { Core.Log.Warning("[Debug] rvsow: no _Temp object in the scene."); return; }

            if (pot.CurrentSoil == null || pot.NormalizedSoilAmount < 0.99f)
            {
                var soil = Il2CppScheduleOne.Registry.GetItem("soil")?.TryCast<Il2CppScheduleOne.ItemFramework.SoilDefinition>();
                if (soil == null) { Core.Log.Warning("[Debug] rvsow: no soil definition 'soil'."); return; }
                pot.SetSoil(soil);
                pot.SetSoilAmount(pot.SoilCapacity);
                pot.SyncSoilData();
                Core.Log.Msg("[Debug] rvsow: pot filled with soil.");
            }
            Vector3 start = pot.SeedStartPoint.position;
            Core.Log.Msg("[Debug] rvsow: pot '" + pot.name + "' of '" + pot.ParentProperty.name + "' at " + start.ToString("F2")
                + " rest " + pot.SeedRestingPoint.position.ToString("F2") + " parent=" + (pot.transform.parent != null ? pot.transform.parent.name : "none")
                + " rb=" + (pot.GetComponentInParent<Rigidbody>() != null ? pot.GetComponentInParent<Rigidbody>().name : "none"));

            pot.TaskBounds.gameObject.SetActive(true);
            pot.SetSoilState(Pot.ESoilState.Parted);
            foreach (var chunk in pot.SoilChunks) { chunk.SetChunkClickable(false); chunk.SetColliderEnabled(true); }
            foreach (Collider c in pot.GetComponentsInChildren<Collider>(false))
            {
                if (!c.enabled || c.isTrigger) continue;
                MeshCollider mc = c.TryCast<MeshCollider>();
                Core.Log.Msg("[Debug] rvsow:   pot collider '" + c.name + "' " + c.GetIl2CppType().Name + (mc != null ? " convex=" + mc.convex : "")
                    + " layer " + LayerMask.LayerToName(c.gameObject.layer) + " rb=" + (c.attachedRigidbody != null ? c.attachedRigidbody.name + " kin=" + c.attachedRigidbody.isKinematic : "none"));
            }

            var seed = UnityEngine.Object.Instantiate(def.FunctionSeedPrefab.gameObject, temp.transform).GetComponent<Il2CppScheduleOne.Growing.FunctionalSeed>();
            seed.transform.position = start;
            seed.Vial.gameObject.SetActive(false);
            Vector3 drop = start + Vector3.down * 0.05337f;
            seed.SeedRigidbody.transform.position = drop;
            seed.SeedRigidbody.position = drop;
            seed.SeedRigidbody.isKinematic = false;
            int seedLayer = seed.SeedCollider.gameObject.layer;
            Core.Log.Msg("[Debug] rvsow: seed layer " + LayerMask.LayerToName(seedLayer) + " excludeLayers=" + seed.SeedRigidbody.excludeLayers.value);

            foreach (RaycastHit h in Physics.RaycastAll(drop, Vector3.down, 1.5f, -1, QueryTriggerInteraction.Collide))
            {
                Collider c = h.collider;
                Rigidbody arb = c.attachedRigidbody;
                if (Physics.GetIgnoreLayerCollision(seedLayer, c.gameObject.layer)) continue;
                Core.Log.Msg("[Debug] rvsow:   below " + h.distance.ToString("F3") + " '" + c.name + "' (" + (c.transform.parent != null ? c.transform.parent.name : "-") + ") layer "
                    + LayerMask.LayerToName(c.gameObject.layer) + " trigger=" + c.isTrigger + " ignoresSeed=" + Physics.GetIgnoreLayerCollision(seedLayer, c.gameObject.layer)
                    + " rb=" + (arb != null ? arb.name + " kin=" + arb.isKinematic + " exclude=" + arb.excludeLayers.value : "none"));
            }
            MelonLoader.MelonCoroutines.Start(Track(pot, seed));
        }

        private static System.Collections.IEnumerator Track(Pot pot, Il2CppScheduleOne.Growing.FunctionalSeed seed)
        {
            for (int i = 0; i < 12; i++)
            {
                yield return new WaitForSeconds(0.25f);
                if (seed == null || pot == null) { Core.Log.Warning("[Debug] rvsow: seed or pot gone mid-drop."); yield break; }
                Vector3 p = seed.SeedRigidbody.position;
                Core.Log.Msg("[Debug] rvsow: t=" + ((i + 1) * 0.25f).ToString("F2") + " seed " + p.ToString("F3") + " toRest " + Vector3.Distance(seed.SeedCollider.transform.position, pot.SeedRestingPoint.position).ToString("F3")
                    + " v=" + seed.SeedRigidbody.velocity.magnitude.ToString("F3"));
            }
            int seedLayer = seed.SeedCollider.gameObject.layer;
            foreach (Collider c in Physics.OverlapSphere(seed.SeedRigidbody.position, 0.06f, -1, QueryTriggerInteraction.Ignore))
            {
                if (c == seed.SeedCollider || Physics.GetIgnoreLayerCollision(seedLayer, c.gameObject.layer)) continue;
                Core.Log.Msg("[Debug] rvsow:   resting on '" + c.name + "' (" + (c.transform.parent != null ? c.transform.parent.name : "-") + ") layer "
                    + LayerMask.LayerToName(c.gameObject.layer) + " rb=" + (c.attachedRigidbody != null ? c.attachedRigidbody.name : "none"));
            }
            UnityEngine.Object.Destroy(seed.gameObject);
            pot.SetSoilState(Pot.ESoilState.Flat);
            foreach (var chunk in pot.SoilChunks) { chunk.StopLerp(); chunk.SetColliderEnabled(false); }
            pot.TaskBounds.gameObject.SetActive(false);
            Core.Log.Msg("[Debug] rvsow: done, pot reset.");
        }

        /// <summary>rvbed: what the RV's bed is made of - Bed scripts, interactables and colliders under the model's 'Bed'.</summary>
        internal static void BedInfo(string action)
        {
            Transform root = RVManager.Root;
            if (root == null) { Core.Log.Warning("[Debug] rvbed: RV root not found."); return; }
            foreach (var b in (RVManager.Model ?? root).GetComponentsInChildren<Il2CppScheduleOne.ObjectScripts.Bed>(true))
            {
                var io = b.intObj;
                Core.Log.Msg("[Debug] rvbed: Bed script on '" + b.name + "' active=" + b.gameObject.activeInHierarchy + " enabled=" + b.enabled
                    + " spawned=" + b.IsSpawned + " intObj=" + (io != null ? "'" + Path(io.transform) + "' enabled=" + io.enabled + " active=" + io.gameObject.activeInHierarchy
                    + " layer " + LayerMask.LayerToName(io.gameObject.layer) + " hoverListeners=" + io.onHovered.GetPersistentEventCount() + " interactListeners=" + io.onInteractStart.GetPersistentEventCount() : "null"));
                if (io == null) continue;
                foreach (Collider c in io.GetComponentsInChildren<Collider>(true))
                    Core.Log.Msg("[Debug] rvbed:   intObj collider '" + c.name + "' " + c.GetIl2CppType().Name + " enabled=" + c.enabled + " trigger=" + c.isTrigger
                        + " layer " + LayerMask.LayerToName(c.gameObject.layer) + " size " + c.bounds.size.ToString("F2"));
                // InteractionManager.CheckHover from eight standing spots around the bed, looking at its middle: an
                // interactable only wins when the sphere cast meets it no later than the first plain collider on the ray.
                var im = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Interaction.InteractionManager>.Instance;
                int mask = im != null ? im.Interaction_SearchMask.value : -1;
                Core.Log.Msg("[Debug] rvbed:   search mask " + mask + " range " + io.MaxInteractionRange + " angleLimit=" + io.LimitInteractionAngle);
                Vector3 target = io.transform.position;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 dir = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
                    Vector3 eye = target + dir * 1.3f + Vector3.up * 0.9f;
                    var ray = new UnityEngine.Ray(eye, (target - eye).normalized);
                    float block = float.MaxValue; string blocker = "none";
                    foreach (RaycastHit h in Physics.RaycastAll(ray, 4f, mask, QueryTriggerInteraction.Collide))
                        if (h.collider.GetComponentInParent<Il2CppScheduleOne.Interaction.InteractableObject>() == null && h.distance < block) { block = h.distance; blocker = Path(h.collider.transform); }
                    string won = "nothing";
                    float best = float.MaxValue;
                    foreach (RaycastHit h in Physics.SphereCastAll(ray, 0.075f, 4f, mask, QueryTriggerInteraction.Collide))
                    {
                        if (h.distance + 0.075f > block + 0.01f || h.distance >= best) continue;
                        var hit = h.collider.GetComponentInParent<Il2CppScheduleOne.Interaction.InteractableObject>();
                        if (hit == null) continue;
                        best = h.distance; won = Path(hit.transform);
                    }
                    Core.Log.Msg("[Debug] rvbed:   from " + (k * 45) + "deg: first plain collider " + (blocker == "none" ? "none" : "'" + blocker + "' at " + block.ToString("F2"))
                        + ", hover -> " + won);
                }
                b.Hovered();
                Core.Log.Msg("[Debug] rvbed:   after Hovered(): state=" + io._interactionState + " message='" + io.message + "'");
                if (action == "use")
                {
                    // What pressing E on it does: InteractableObject.onInteractStart -> Bed.Interacted -> the sleep menu.
                    io.onInteractStart.Invoke();
                    var menu = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.UI.SleepMenu>.Instance;
                    Core.Log.Msg("[Debug] rvbed use: sleep menu open=" + (menu != null && menu.IsMenuOpen));
                }
            }
            Transform model = RVManager.Model;
            foreach (Transform t in (model ?? root).GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.Contains("Bed") && !t.name.Contains("Mattress") && !t.name.Contains("Pillow") && !t.name.Contains("Blanket")) continue;
                var io = t.GetComponent<Il2CppScheduleOne.Interaction.InteractableObject>();
                string cols = "";
                foreach (Collider c in t.GetComponents<Collider>()) cols += " " + c.GetIl2CppType().Name + (c.enabled ? "" : "(off)") + (c.isTrigger ? "(trig)" : "") + " size " + c.bounds.size.ToString("F2");
                Core.Log.Msg("[Debug] rvbed: '" + Path(t) + "' active=" + t.gameObject.activeInHierarchy + " layer " + LayerMask.LayerToName(t.gameObject.layer)
                    + " at model " + (model != null ? model.InverseTransformPoint(t.position).ToString("F2") : "-") + " io=" + (io != null) + " colliders:" + (cols == "" ? " none" : cols));
            }
            var beds = UnityEngine.Object.FindObjectsOfType<Il2CppScheduleOne.ObjectScripts.Bed>(true);
            Core.Log.Msg("[Debug] rvbed: " + beds.Length + " Bed script(s) in the scene.");
            foreach (var b in beds) Core.Log.Msg("[Debug] rvbed:   " + Path(b.transform) + " active=" + b.gameObject.activeInHierarchy);
        }

        /// <summary>rvdoor: every door script on or near the RV, with the access a player gets from outside; 'open' presses the outside handle.</summary>
        internal static void DoorInfo(string action)
        {
            Transform root = RVManager.Root;
            if (root == null) { Core.Log.Warning("[Debug] rvdoor: RV root not found."); return; }
            Property rv = root.GetComponent<Property>();
            Core.Log.Msg("[Debug] rvdoor: property " + (rv != null ? "'" + rv.PropertyName + "' owned=" + rv.IsOwned : "none")
                + " destroyed=" + RVManager.IsDestroyed() + " away=" + Base.RvMobile.Away);
            foreach (var d in root.GetComponentsInChildren<Il2CppScheduleOne.Doors.DoorController>(true))
                Core.Log.Msg("[Debug] rvdoor: DoorController '" + Path(d.transform) + "' active=" + d.gameObject.activeInHierarchy + " enabled=" + d.enabled
                    + " access=" + d.PlayerAccess + " outside=" + d.CanPlayerAccess(Il2CppScheduleOne.Doors.EDoorSide.Exterior)
                    + " inside=" + d.CanPlayerAccess(Il2CppScheduleOne.Doors.EDoorSide.Interior) + " open=" + d.IsOpen);
            if (action == "open")
                foreach (var d in root.GetComponentsInChildren<Il2CppScheduleOne.Doors.DoorController>(false))
                {
                    d.ExteriorHandleInteracted();
                    MelonLoader.MelonCoroutines.Start(ReportDoor(d));
                }
            Vector3 at = root.position;
            foreach (var s in UnityEngine.Object.FindObjectsOfType<Il2CppScheduleOne.Doors.StaticDoor>(true))
            {
                float dist = Vector3.Distance(s.transform.position, at);
                bool under = s.transform.IsChildOf(root);
                if (!under && dist > 15f) continue;
                Core.Log.Msg("[Debug] rvdoor: StaticDoor '" + Path(s.transform) + "' " + dist.ToString("0.0") + " m from the RV, underRv=" + under
                    + " active=" + s.gameObject.activeInHierarchy + " usable=" + s.Usable + " canKnock=" + s.CanKnock
                    + " building=" + (s.Building != null ? "'" + s.Building.name + "'" : "none"));
            }
            foreach (var io in UnityEngine.Object.FindObjectsOfType<Il2CppScheduleOne.Interaction.InteractableObject>(false))
            {
                float dist = Vector3.Distance(io.transform.position, at);
                if (dist > 8f || io.name.IndexOf("door", StringComparison.OrdinalIgnoreCase) < 0 && (io.transform.parent == null
                    || io.transform.parent.name.IndexOf("door", StringComparison.OrdinalIgnoreCase) < 0)) continue;
                Core.Log.Msg("[Debug] rvdoor: interactable '" + Path(io.transform) + "' " + dist.ToString("0.0") + " m, enabled=" + io.enabled
                    + " message='" + io.message + "'");
            }
        }

        private static System.Collections.IEnumerator ReportDoor(Il2CppScheduleOne.Doors.DoorController d)
        {
            yield return new WaitForSeconds(1f);
            Core.Log.Msg("[Debug] rvdoor: '" + (d != null ? d.name : "gone") + "' open=" + (d != null && d.IsOpen) + " one second after the handle.");
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            for (Transform up = t.parent; up != null; up = up.parent) p = up.name + "/" + p;
            return p;
        }

        /// <summary>
        /// <c>rvghost aim &lt;x&gt; &lt;z&gt; [distance]</c> stands the player a short way from a floor point (RV frame, as
        /// <c>rvgrid</c> prints it) and looks at it, so a held buildable item shows its ghost there.
        /// <c>rvghost</c> then logs which grid the ghost snapped to and, per footprint tile, the check
        /// BuildUpdate_Grid.CheckIntersections fails: no tile, a corner obstacle, an occupant, or the offset.
        /// </summary>
        internal static void Ghost(string[] parts)
        {
            if (parts.Length >= 4 && parts[1].ToLower() == "aim"
                && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)
                && float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
            {
                float d = parts.Length > 4 && float.TryParse(parts[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dd) ? dd : 1.2f;
                AimAtFloor(x, z, d);
                return;
            }
            if (parts.Length >= 3 && parts[1].ToLower() == "rot" && float.TryParse(parts[2], out float deg)) { RotateGhost(deg); return; }
            if (parts.Length >= 2 && parts[1].ToLower() == "place") { PlaceGhost(); return; }
            if (parts.Length >= 2 && parts[1].ToLower() == "pickup") { PickupSecondGrid(parts.Length > 2 && parts[2].ToLower() == "all"); return; }
            if (parts.Length >= 2 && parts[1].ToLower() == "sweep") { MelonLoader.MelonCoroutines.Start(SweepGhost()); return; }
            LogGhost();
        }

        /// <summary>
        /// <c>rvghost sweep</c>: point the held item's ghost at every tile of the RV's first grid in turn, the way
        /// a player looks at the floor, and count per group (the RV's own tiles, the ones the workshop floor added)
        /// how often the ghost turned out valid. The first few red ones are explained through <c>rvghost</c>.
        /// </summary>
        private static System.Collections.IEnumerator SweepGhost()
        {
            Property rv = RvProperty();
            Transform root = RVManager.Root;
            var pm = PlayerSingleton<PlayerMovement>.Instance;
            var pc = PlayerSingleton<PlayerCamera>.Instance;
            if (rv == null || root == null || rv.Grids == null || rv.Grids.Count == 0) { Core.Log.Warning("[Debug] rvghost sweep: RV property, root or grid not found."); yield break; }
            if (pm == null || pc == null) { Core.Log.Warning("[Debug] rvghost sweep: no local player."); yield break; }
            if (CurrentGhost("sweep") == null) yield break;
            Grid grid = rv.Grids[0];
            var tiles = new System.Collections.Generic.List<Tile>();
            for (int t = 0; grid.Tiles != null && t < grid.Tiles.Count; t++) if (grid.Tiles[t] != null) tiles.Add(grid.Tiles[t]);
            int[] n = new int[2], ok = new int[2], explained = new int[2];
            foreach (Tile tile in tiles)
            {
                if (tile == null) continue;
                int g = tile.gameObject.name.StartsWith("RVRepairVan_Tile_", StringComparison.Ordinal) ? 1 : 0;
                Vector3 local = root.InverseTransformPoint(tile.transform.position);
                Vector3 dir = Mathf.Abs(local.x) > 0.1f ? new Vector3(-Mathf.Sign(local.x), 0f, 0f) : Vector3.back;
                pm.Teleport(root.TransformPoint(local + dir * 1.2f + Vector3.up * 1f), true);
                yield return new WaitForSeconds(0.3f);
                if (pc == null) { Core.Log.Warning("[Debug] rvghost sweep: camera gone mid-sweep."); yield break; }
                pc.LookAt(tile.transform.position, 0.05f);
                yield return new WaitForSeconds(0.35f);
                BuildUpdate_Grid bu = CurrentGhost("sweep");
                if (bu == null) yield break;
                n[g]++;
                if (bu._validPosition) { ok[g]++; continue; }
                if (explained[g]++ < 3)
                {
                    Core.Log.Msg("[Debug] rvghost sweep: red at " + (g == 1 ? "added" : "RV's own") + " tile (" + tile.x + "," + tile.y + ") " + local.ToString("F2") + ":");
                    LogGhost();
                }
            }
            Core.Log.Msg("[Debug] rvghost sweep: RV's own tiles " + ok[0] + "/" + n[0] + " valid, added tiles " + ok[1] + "/" + n[1] + " valid.");
        }

        /// <summary>Pick up every item standing on the RV's second grid (or on every grid with 'all'), the way the player's hands do.</summary>
        private static void PickupSecondGrid(bool all)
        {
            Property rv = RvProperty();
            if (rv == null || rv.Grids == null || rv.Grids.Count < 2) { Core.Log.Warning("[Debug] rvghost pickup: the RV has no second grid."); return; }
            var done = new System.Collections.Generic.HashSet<System.IntPtr>();
            for (int g = all ? 0 : 1; g < rv.Grids.Count; g++)
            {
                var grid = rv.Grids[g];
                if (grid == null || grid.Tiles == null) continue;
                for (int k = 0; k < grid.Tiles.Count; k++)
                {
                    var t = grid.Tiles[k];
                    if (t == null || t.BuildableOccupants == null) continue;
                    for (int o = t.BuildableOccupants.Count - 1; o >= 0; o--)
                    {
                        var item = t.BuildableOccupants[o];
                        if (item == null || !done.Add(item.Pointer)) continue;
                        Core.Log.Msg("[Debug] rvghost pickup: '" + item.name + "' from '" + grid.name + "'.");
                        item.PickupItem();
                    }
                }
            }
            if (done.Count == 0) Core.Log.Msg("[Debug] rvghost pickup: nothing stands on the second grid.");
        }

        private static void AimAtFloor(float x, float z, float distance)
        {
            Property rv = RvProperty();
            Transform root = RVManager.Root;
            var pm = PlayerSingleton<PlayerMovement>.Instance;
            var pc = PlayerSingleton<PlayerCamera>.Instance;
            if (rv == null || root == null) { Core.Log.Warning("[Debug] rvghost aim: RV property or root not found."); return; }
            if (pm == null || pc == null) { Core.Log.Warning("[Debug] rvghost aim: no local player."); return; }

            float floorY = 0f;
            for (int g = 0; rv.Grids != null && g < rv.Grids.Count; g++)
                if (rv.Grids[g] != null && rv.Grids[g].Tiles != null && rv.Grids[g].Tiles.Count > 0)
                { floorY = root.InverseTransformPoint(rv.Grids[g].Tiles[0].transform.position).y; break; }

            // Stand on the cabin's centre line, toward the target from there, so the look never crosses a wall.
            Vector3 target = new Vector3(x, floorY, z);
            Vector3 toCentre = new Vector3(-x, 0f, 0f);
            // On the centre line itself, stand toward the cab: the rear wall is close behind the back rows.
            Vector3 dir = toCentre.sqrMagnitude > 0.01f ? toCentre.normalized : Vector3.back;
            Vector3 stand = target + dir * distance + Vector3.up * 1f;
            pm.Teleport(root.TransformPoint(stand), true);
            MelonLoader.MelonCoroutines.Start(LookAtFloorLater(pc, root.TransformPoint(target)));
        }

        // The look direction is taken from the player's position, which the teleport only moves on a later frame.
        private static System.Collections.IEnumerator LookAtFloorLater(PlayerCamera pc, Vector3 at)
        {
            yield return new WaitForSeconds(0.5f);
            if (pc == null) { Core.Log.Warning("[Debug] rvghost aim: camera gone before the look."); yield break; }
            pc.LookAt(at, 0.1f);
            Core.Log.Msg("[Debug] rvghost aim: looking at " + at.ToString("F2") + ".");
        }

        private static BuildUpdate_Grid CurrentGhost(string what)
        {
            var bm = NetworkSingleton<BuildManager>.Instance;
            if (bm == null || !bm.isBuilding || bm.currentBuildHandler == null) { Core.Log.Warning("[Debug] rvghost " + what + ": not in build mode - hold a buildable item."); return null; }
            BuildUpdate_Grid bu = bm.currentBuildHandler.GetComponent<BuildUpdate_Grid>();
            if (bu == null) Core.Log.Warning("[Debug] rvghost " + what + ": the build handler '" + bm.currentBuildHandler.name + "' is not a grid build.");
            return bu;
        }

        /// <summary><c>rvghost rot &lt;degrees&gt;</c>: turn the ghost the way Q/E does.</summary>
        private static void RotateGhost(float degrees)
        {
            BuildUpdate_Grid bu = CurrentGhost("rot");
            if (bu == null) return;
            bu._rotation = degrees;
            Core.Log.Msg("[Debug] rvghost rot: ghost turned to " + degrees + " degrees.");
        }

        /// <summary><c>rvghost place</c>: place the ghost where it stands, through the same Place() a click calls.</summary>
        private static void PlaceGhost()
        {
            BuildUpdate_Grid bu = CurrentGhost("place");
            if (bu == null) return;
            if (!bu._validPosition) { Core.Log.Warning("[Debug] rvghost place: the ghost is red here - nothing placed."); return; }
            GridItem placed = bu.Place();
            if (placed == null) { Core.Log.Warning("[Debug] rvghost place: Place() returned null."); return; }
            Core.Log.Msg("[Debug] rvghost place: '" + placed.name + "' on grid '" + (placed.OwnerGrid != null ? placed.OwnerGrid.name : "null") + "'.");
        }

        private static void LogGhost()
        {
            var bm = NetworkSingleton<BuildManager>.Instance;
            if (bm == null || !bm.isBuilding || bm.currentBuildHandler == null) { Core.Log.Warning("[Debug] rvghost: not in build mode - hold a buildable item."); return; }
            BuildUpdate_Grid bu = bm.currentBuildHandler.GetComponent<BuildUpdate_Grid>();
            if (bu == null) { Core.Log.Warning("[Debug] rvghost: the build handler '" + bm.currentBuildHandler.name + "' is not a grid build."); return; }
            GridItem item = bu.BuildableItemClass;
            TileIntersection hit = bu.closestIntersection;
            if (item == null) { Core.Log.Warning("[Debug] rvghost: the ghost has no GridItem."); return; }
            if (hit == null || hit.tile == null) { Core.Log.Msg("[Debug] rvghost: the ghost touches no tile."); return; }

            Grid grid = hit.tile.OwnerGrid;
            Transform root = RVManager.Root;
            Vector3 ghostAt = bu.GhostModel != null ? bu.GhostModel.transform.position : Vector3.zero;
            Core.Log.Msg("[Debug] rvghost: valid=" + bu._validPosition + " grid='" + (grid != null ? grid.name : "null") + "' snapped tile ("
                + hit.tile.x + "," + hit.tile.y + ") ghost at " + (root != null ? root.InverseTransformPoint(ghostAt) : ghostAt).ToString("F2"));
            if (grid == null) return;

            for (int i = 0; item.CoordinateFootprintTilePairs != null && i < item.CoordinateFootprintTilePairs.Count; i++)
            {
                FootprintTile ft = item.CoordinateFootprintTilePairs[i].footprintTile;
                if (ft == null) { Core.Log.Warning("[Debug] rvghost   footprint " + i + " is null."); continue; }
                Coordinate c = grid.GetMatchedCoordinate(ft);
                Tile t = grid.GetTile(c);
                string why;
                if (t == null) why = "no tile on this grid";
                else if (ft.AreCornerObstaclesBlocked(t)) why = "corner obstacle";
                else if (!grid.IsTileValidAtCoordinate(c, ft, item))
                {
                    if (t.BuildableOccupants != null && t.BuildableOccupants.Count > 0)
                    {
                        why = "occupied by";
                        for (int k = 0; k < t.BuildableOccupants.Count; k++) why += " '" + (t.BuildableOccupants[k] != null ? t.BuildableOccupants[k].name : "null") + "'";
                    }
                    else why = "offset " + t.AvailableOffset.ToString("F2") + " < required " + ft.RequiredOffset.ToString("F2") + ", or the property is not owned";
                }
                else why = "ok";

                string seen = "";
                var detected = ft.tileDetector != null ? ft.tileDetector.intersectedTiles : null;
                for (int k = 0; detected != null && k < detected.Count; k++)
                    if (detected[k] != null) seen += " " + (detected[k].OwnerGrid != null ? detected[k].OwnerGrid.name : "?") + "(" + detected[k].x + "," + detected[k].y + ")";
                Core.Log.Msg("[Debug] rvghost   footprint " + i + " -> (" + c.x + "," + c.y + ") " + why
                    + " at " + (root != null ? root.InverseTransformPoint(ft.transform.position) : ft.transform.position).ToString("F2")
                    + " | touches:" + (seen.Length > 0 ? seen : " nothing"));
            }
        }

        private static Property RvProperty()
        {
            var pm = Il2CppScheduleOne.DevUtilities.Singleton<PropertyManager>.Instance;
            return pm != null ? pm.GetProperty("rv") : null;
        }

        private static Transform Model()
        {
            Transform root = RVManager.Root;
            return root != null ? root.Find("RV") : null;
        }
    }
}
#endif
