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

        internal static void Place(string itemId, int rotation)
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

        internal static void GridExtent()
        {
            Property rv = RvProperty();
            Transform root = RVManager.Root;
            if (rv == null || root == null) { Core.Log.Warning("[Debug] rvgrid: RV property or root not found."); return; }
            for (int g = 0; rv.Grids != null && g < rv.Grids.Count; g++)
            {
                Grid grid = rv.Grids[g];
                if (grid == null) continue;
                int n = grid.Tiles != null ? grid.Tiles.Count : 0;
                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                for (int t = 0; t < n; t++)
                {
                    Tile tile = grid.Tiles[t];
                    if (tile == null) continue;
                    Vector3 p = root.InverseTransformPoint(tile.transform.position);
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                }
                Core.Log.Msg("[Debug] rvgrid '" + grid.name + "' active=" + grid.gameObject.activeInHierarchy + " tiles=" + n
                    + (n > 0 ? " x " + min.x.ToString("F2") + ".." + max.x.ToString("F2") + " z " + min.z.ToString("F2") + ".." + max.z.ToString("F2") : ""));
            }
        }

        internal static void Reach()
        {
            Property rv = RvProperty();
            Transform root = RVManager.Root;
            if (rv == null || root == null) { Core.Log.Warning("[Debug] rvreach: RV property or root not found."); return; }
            Employee emp = rv.Employees != null && rv.Employees.Count > 0 ? rv.Employees[0] : null;
            if (emp == null) { Core.Log.Warning("[Debug] rvreach: no employee in the RV to test with."); return; }
            Vector3 from = emp.transform.position;
            Core.Log.Msg("[Debug] rvreach: from " + emp.FullName + " at RV-frame " + root.InverseTransformPoint(from)
                + " onMesh=" + OnMesh(from, out float d0) + " (" + d0.ToString("F2") + "m)");

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
