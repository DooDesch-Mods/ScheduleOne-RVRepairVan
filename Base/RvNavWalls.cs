using UnityEngine.AI;
using RVRepairVan.Managers;

namespace RVRepairVan.Base
{
    /// <summary>
    /// The RV's walls block the police. An NPC walks on the NavMesh and not through physics, so the hull's colliders
    /// alone never stopped an officer: the world NavMesh runs on under the RV, and a chase walked straight through
    /// its side. Carving NavMesh obstacles along the outer walls cut that mesh there and leave the doorway open, so
    /// the way in is the door, at home and wherever the RV is parked.
    /// </summary>
    internal static class RvNavWalls
    {
        private const string RootName = "RVRepairVan_NavWalls";
        // rv/Main is z-up, x along the RV (front +x), y across with the door side at +y.
        private const float WallThickness = 0.3f;
        private const float DoorMargin = 0.6f;          // on each side of the door leaf, so an officer fits through
        private const float GroundZ = -2.3f;            // the road under the wheels in rv/Main
        private const float BelowGround = 0.5f, AboveGround = 0.7f;   // ends below the crew island on the cabin floor (about 1.1 m up)
        // Measured outer hull and doorway, used when the hull colliders cannot be read.
        private static readonly Bounds FallbackHull = new Bounds(new Vector3(-0.3f, 0f, 0f), new Vector3(10.9f, 3.35f, 3f));
        private const float FallbackDoorMinX = 1.45f, FallbackDoorMaxX = 2.6f;

        private static Transform _root, _model;

        /// <summary>Put the wall obstacles on the current intact model. Later calls for the same model return.</summary>
        internal static void Ensure()
        {
            try
            {
                Transform model = RVManager.Model;
                if (model == null) { Core.LogDebug("[NavWalls] RV model not found yet - retrying on the next apply."); return; }
                if (_root != null && _model != null && _model.Pointer == model.Pointer) return;
                if (RVManager.IsDestroyed()) { Core.LogDebug("[NavWalls] the RV is wrecked - no walls to keep the police out of."); return; }
                Transform main = model.Find("rv/Main");
                if (main == null) { Core.Log.Warning("[NavWalls] 'rv/Main' not found on the RV model - police can walk through the RV's walls."); return; }

                Transform old = main.Find(RootName);
                if (old != null) UnityEngine.Object.Destroy(old.gameObject);
                Bounds hull = Hull(model, main, out int colliders);
                Door(model, main, out float doorMin, out float doorMax);
                float gapMin = doorMin - DoorMargin, gapMax = doorMax + DoorMargin;

                var root = new GameObject(RootName);
                root.transform.SetParent(main, false);
                // Obstacles carve along their own up axis; this frame has y on rv/Main's z (up), x on its x.
                root.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                float t = WallThickness, zMid = GroundZ + (AboveGround - BelowGround) * 0.5f, h = AboveGround + BelowGround;
                float x0 = hull.min.x, x1 = hull.max.x, y0 = hull.min.y, y1 = hull.max.y;
                int walls = 0;
                walls += Wall(root.transform, "Front", new Vector3(x1 - t * 0.5f, 0f, zMid), new Vector3(t, hull.size.y, h));
                walls += Wall(root.transform, "Rear", new Vector3(x0 + t * 0.5f, 0f, zMid), new Vector3(t, hull.size.y, h));
                walls += Wall(root.transform, "Side", new Vector3(hull.center.x, y0 + t * 0.5f, zMid), new Vector3(hull.size.x, t, h));
                // The door side, open between gapMin and gapMax.
                if (gapMin > x0) walls += Wall(root.transform, "DoorSideRear", new Vector3((x0 + gapMin) * 0.5f, y1 - t * 0.5f, zMid), new Vector3(gapMin - x0, t, h));
                if (gapMax < x1) walls += Wall(root.transform, "DoorSideFront", new Vector3((gapMax + x1) * 0.5f, y1 - t * 0.5f, zMid), new Vector3(x1 - gapMax, t, h));
                _root = root.transform;
                _model = model;
                Core.Log.Msg("[NavWalls] " + walls + " wall obstacle(s) around the RV, hull x " + x0.ToString("F2") + ".." + x1.ToString("F2") + " y " + y0.ToString("F2")
                    + ".." + y1.ToString("F2") + " (" + colliders + " hull collider(s)), doorway open x " + gapMin.ToString("F2") + ".." + gapMax.ToString("F2") + ".");
            }
            catch (Exception e) { Core.Log.Error("[NavWalls] building the wall obstacles failed - police can walk through the RV's walls: " + e); }
        }

        /// <summary>Scene change: forget the walls. They go with their scene.</summary>
        internal static void Reset()
        {
            _root = null;
            _model = null;
        }

        // rv/Main point and size -> the walls frame (x, z up, -y).
        private static int Wall(Transform root, string name, Vector3 centreMain, Vector3 sizeMain)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(centreMain.x, centreMain.z, -centreMain.y);
            var ob = go.AddComponent<NavMeshObstacle>();
            ob.shape = NavMeshObstacleShape.Box;
            ob.center = Vector3.zero;
            ob.size = new Vector3(Mathf.Abs(sizeMain.x), Mathf.Abs(sizeMain.z), Mathf.Abs(sizeMain.y));
            ob.carving = true;
            ob.carveOnlyStationary = true;   // a driving RV carves nothing; parked, it carves once
            return 1;
        }

        // The outer hull in rv/Main from the shell colliders (Collider_* under the model's "rv"), else the measured one.
        private static Bounds Hull(Transform model, Transform main, out int count)
        {
            count = 0;
            var b = new Bounds();
            foreach (Collider c in model.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger || !c.name.StartsWith("Collider_")) continue;
                Bounds w = c.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = new Vector3((i & 1) == 0 ? w.min.x : w.max.x, (i & 2) == 0 ? w.min.y : w.max.y, (i & 4) == 0 ? w.min.z : w.max.z);
                    Vector3 p = main.InverseTransformPoint(corner);
                    if (count == 0 && i == 0) b = new Bounds(p, Vector3.zero); else b.Encapsulate(p);
                }
                count++;
            }
            if (count == 0 || b.size.x < 6f || b.size.y < 2f)
            {
                Core.Log.Warning("[NavWalls] the hull colliders give " + b.size.ToString("F2") + " from " + count + " collider(s) - using the measured hull.");
                count = 0;
                return FallbackHull;
            }
            return b;
        }

        // The doorway along x from the door leaf (the model's PivotDoor), else the measured one.
        private static void Door(Transform model, Transform main, out float min, out float max)
        {
            min = FallbackDoorMinX;
            max = FallbackDoorMaxX;
            var door = model.GetComponentInChildren<Il2CppScheduleOne.Doors.PivotDoor>(true);
            if (door == null) { Core.Log.Warning("[NavWalls] no door leaf on the RV model - the doorway is left open where it was measured."); return; }
            bool any = false;
            foreach (Renderer r in door.GetComponentsInChildren<Renderer>(true))
            {
                Bounds w = r.bounds;
                foreach (float x in new[] { main.InverseTransformPoint(w.min).x, main.InverseTransformPoint(w.max).x })
                {
                    if (!any) { min = max = x; any = true; }
                    else { min = Mathf.Min(min, x); max = Mathf.Max(max, x); }
                }
            }
            if (!any || max - min < 0.5f || max - min > 2f)
            {
                Core.Log.Warning("[NavWalls] the door leaf measures " + (max - min).ToString("F2") + " m - the doorway is left open where it was measured.");
                min = FallbackDoorMinX;
                max = FallbackDoorMaxX;
            }
        }

#if DEBUG
        // --- console --------------------------------------------------------------------------------------

        /// <summary>rvnav: the RV's shell, floor and ground in rv/Main space, and how the NavMesh reaches in.</summary>
        internal static void DebugCmd(string[] args)
        {
            Transform model = RVManager.Model;
            Transform main = model != null ? model.Find("rv/Main") : null;
            if (main == null) { Core.Log.Warning("[NavWalls] rvnav: rv/Main not found."); return; }
            Core.Log.Msg("[NavWalls] rv/Main pos=" + main.position.ToString("F2") + " right=" + main.right.ToString("F2") + " up=" + main.up.ToString("F2")
                + " fwd=" + main.forward.ToString("F2") + " lossy=" + main.lossyScale.ToString("F3") + " away=" + RvMobile.Away);
            var obstacles = new List<NavMeshObstacle>(model.GetComponentsInChildren<NavMeshObstacle>(true));
            if (RvMobile.Chassis != null) obstacles.AddRange(RvMobile.Chassis.GetComponentsInChildren<NavMeshObstacle>(true));
            foreach (NavMeshObstacle o in obstacles)
                Core.Log.Msg("[NavWalls] obstacle " + o.name + " (" + (o.transform.parent != null ? o.transform.parent.name : "-") + ") active=" + o.isActiveAndEnabled
                    + " carving=" + o.carving + " centre in main " + main.InverseTransformPoint(o.transform.TransformPoint(o.center)).ToString("F2") + " size " + o.size.ToString("F2"));
            Vector3 worldUp = main.InverseTransformDirection(Vector3.up);
            Core.Log.Msg("[NavWalls] world up in rv/Main = " + worldUp.ToString("F2"));
            // Shell: rays out of the cabin middle along each rv/Main axis.
            foreach (Vector3 dir in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
                Ray(main, Vector3.zero, dir);
            foreach (float x in new[] { -4f, -2f, 0f, 2f, 4f })
            {
                Ray(main, new Vector3(x, 0f, 0f), Vector3.up);
                Ray(main, new Vector3(x, 0f, 0f), Vector3.down);
            }
            // Down through the floor to the road, inside and outside the RV.
            Vector3 down = main.InverseTransformDirection(Vector3.down);
            foreach (Vector3 p in new[] { Vector3.zero, new Vector3(0f, 3f, 0f), new Vector3(0f, -3f, 0f), new Vector3(8f, 0f, 0f), new Vector3(-8f, 0f, 0f) })
                RayAll(main, p, down);

            int humanoid = NavMeshUtility.GetNavMeshAgentID("Humanoid");
            var filter = new NavMeshQueryFilter { agentTypeID = humanoid, areaMask = -1 };
            Core.Log.Msg("[NavWalls] humanoid agent id " + humanoid + ", " + NavMesh.GetSettingsCount() + " agent type(s).");
            Vector3 player = Player.Local != null ? Player.Local.transform.position : main.position;
            foreach (Vector3 p in new[] { Vector3.zero, new Vector3(0f, 3.5f, 0f), new Vector3(0f, -3.5f, 0f), new Vector3(8f, 0f, 0f), new Vector3(-8f, 0f, 0f) })
            {
                Vector3 w = main.TransformPoint(p);
                if (NavMesh.SamplePosition(w, out NavMeshHit hit, 4f, filter))
                    Core.Log.Msg("[NavWalls] sample at main " + p + " -> " + main.InverseTransformPoint(hit.position).ToString("F2") + " (" + hit.distance.ToString("0.00") + " m)");
                else Core.Log.Msg("[NavWalls] sample at main " + p + " -> no NavMesh within 4 m");
            }
            // Paths from outside on every side to the player, the way a chase picks its goal.
            // 'rvnav at x y': the goal is the ground at that rv/Main point instead of the player.
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var num = System.Globalization.NumberStyles.Float;
            if (args.Length >= 3 && args[0] == "at" && float.TryParse(args[1], num, inv, out float gx) && float.TryParse(args[2], num, inv, out float gy))
                player = main.TransformPoint(new Vector3(gx, gy, GroundZ));
            if (!NavMeshUtility.SamplePosition(player + Vector3.up, out NavMeshHit goal, 3f, -1, false)) { Core.Log.Msg("[NavWalls] no NavMesh within 3 m of the player."); return; }
            Core.Log.Msg("[NavWalls] chase goal for the player at main " + main.InverseTransformPoint(player).ToString("F2") + " -> " + main.InverseTransformPoint(goal.position).ToString("F2"));
            foreach (Vector3 p in new[] { new Vector3(0f, 6f, 0f), new Vector3(0f, -6f, 0f), new Vector3(9f, 0f, 0f), new Vector3(-9f, 0f, 0f) })
            {
                if (!NavMesh.SamplePosition(main.TransformPoint(p), out NavMeshHit start, 6f, filter)) { Core.Log.Msg("[NavWalls] path from main " + p + ": no start"); continue; }
                var path = new NavMeshPath();
                NavMesh.CalculatePath(start.position, goal.position, filter, path);
                var sb = new System.Text.StringBuilder();
                foreach (Vector3 c in path.corners) sb.Append(' ').Append(main.InverseTransformPoint(c).ToString("F1"));
                Core.Log.Msg("[NavWalls] path from main " + p + " " + path.status + ":" + sb);
            }
        }

        private static void Ray(Transform main, Vector3 from, Vector3 dir)
        {
            Vector3 o = main.TransformPoint(from), d = main.TransformDirection(dir);
            if (Physics.Raycast(o, d, out RaycastHit h, 12f, ~0, QueryTriggerInteraction.Ignore))
                Core.Log.Msg("[NavWalls]   ray " + from + " " + dir + " -> " + main.InverseTransformPoint(h.point).ToString("F2") + " " + h.collider.name
                    + " layer=" + LayerMask.LayerToName(h.collider.gameObject.layer));
            else Core.Log.Msg("[NavWalls]   ray " + from + " " + dir + " -> nothing within 12 m");
        }

        private static void RayAll(Transform main, Vector3 from, Vector3 dir)
        {
            Vector3 o = main.TransformPoint(from), d = main.TransformDirection(dir);
            var hits = new List<RaycastHit>(Physics.RaycastAll(o, d, 8f, ~0, QueryTriggerInteraction.Ignore));
            hits.Sort((a, b) => a.distance.CompareTo(b.distance));
            var sb = new System.Text.StringBuilder();
            foreach (RaycastHit h in hits) sb.Append(' ').Append(h.collider.name).Append('@').Append(main.InverseTransformPoint(h.point).ToString("F2"));
            Core.Log.Msg("[NavWalls]   down from " + from + ":" + (hits.Count == 0 ? " nothing" : sb.ToString()));
        }
#endif
    }
}
