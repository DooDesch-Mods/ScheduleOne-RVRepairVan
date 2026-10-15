using System;
using HarmonyLib;
using Il2CppScheduleOne.Vehicles;
using RVRepairVan.Managers;

namespace RVRepairVan.Base
{
    /// <summary>
    /// Steadies the chase camera while the RV is driven.
    ///
    /// The game keeps the vehicle camera out of walls with one zero-width ray from the look point to the camera:
    /// whatever it touches on the Default layer pulls the camera in, in the same frame, by the full distance. With
    /// the RV's camera 11 m back that ray crosses utility poles, crossbeams and posts all the time, and each one
    /// yanks the camera in and lets it go a frame later.
    ///
    /// For the RV's own camera only, the obstacle test is replaced:
    /// <list type="bullet">
    /// <item>a sphere cast, so a sliver of a collider does not flicker in and out of the test;</item>
    /// <item>sticks are ignored - colliders thin in two of three directions, such as poles, beams and cables -
    /// while walls, which are long in two directions, still pull the camera in;</item>
    /// <item>the camera moves in at once but goes back out only after the way has been clear for a moment, and then
    /// at a steady speed.</item>
    /// </list>
    /// </summary>
    [HarmonyPatch(typeof(VehicleCamera), nameof(VehicleCamera.LimitCameraPosition))]
    internal static class RvCamera
    {
        private const float Radius = 0.3f;          // sphere cast radius
        private const float Margin = 0.45f;         // how far the camera stays off a hit, as the game keeps it
        private const float StickThickness = 0.6f;  // a collider thinner than this in two directions is a stick
        private const float ReleaseDelay = 0.35f;   // seconds of clear way before the camera goes back out
        private const float ReleaseSpeed = 5f;      // metres per second on the way back out
        private const float MinDistance = 1f;

        private static readonly int DefaultMask = 1 << LayerMask.NameToLayer("Default");

        private static VehicleCamera _cam;
        private static float _distance = -1f;      // the distance the camera holds, -1 before the first frame
        private static float _clearFor;
        private static int _frame = -1;
        private static bool _announced;

        private static bool Prefix(VehicleCamera __instance, Vector3 targetPosition, ref Vector3 __result)
        {
            try
            {
                LandVehicle chassis = RvMobile.Chassis;
                if (chassis == null || __instance.vehicle != chassis || __instance.cameraOrigin == null) return true;

                if (!ReferenceEquals(_cam, __instance)) { _cam = __instance; _distance = -1f; _clearFor = 0f; }
                if (!_announced) { _announced = true; Core.Log.Msg("[Camera] the RV's chase camera ignores poles and eases back out after an obstacle."); }

                Vector3 origin = __instance.cameraOrigin.position;
                Vector3 offset = targetPosition - origin;
                float full = offset.magnitude;
                if (full < 0.01f) { __result = targetPosition; return false; }
                Vector3 dir = offset / full;

                float free = FreeDistance(origin, dir, full, chassis);
                if (_distance < 0f) _distance = free;

                if (free < _distance) { _distance = free; _clearFor = 0f; }
                else if (Time.frameCount != _frame)
                {
                    // The game asks up to twice a frame (follow and mouse orbit); the way back out advances once.
                    _clearFor += Time.deltaTime;
                    if (_clearFor >= ReleaseDelay) _distance = Mathf.MoveTowards(_distance, free, ReleaseSpeed * Time.deltaTime);
                }
                _frame = Time.frameCount;

                __result = origin + dir * Mathf.Min(_distance, full);
                return false;
            }
            catch (Exception e)
            {
                Core.Log.Warning("[Camera] obstacle test failed, the game's own test runs instead: " + e.Message);
                return true;
            }
        }

        /// <summary>How far from the look point the camera can sit before it touches something that is not a stick.</summary>
        private static float FreeDistance(Vector3 origin, Vector3 dir, float full, LandVehicle chassis)
        {
            RaycastHit[] hits = Physics.SphereCastAll(origin, Radius, dir, full + Margin, DefaultMask, QueryTriggerInteraction.Ignore);
            float nearest = full + Margin;
            for (int i = 0; hits != null && i < hits.Length; i++)
            {
                Collider c = hits[i].collider;
                // A distance of 0 is a collider the sphere starts inside; the game's ray does not see those either.
                if (c == null || hits[i].distance <= 0f || hits[i].distance >= nearest) continue;
                if (IsOwn(c.transform, chassis)) continue;
                if (IsStick(c)) { NoteStick(c); continue; }
                nearest = hits[i].distance;
            }
            float free = Mathf.Min(full, nearest + Radius - Margin);
            return Mathf.Max(Mathf.Min(MinDistance, full), free);
        }

        private static bool IsOwn(Transform t, LandVehicle chassis)
        {
            Transform root = RVManager.Root;
            return t.IsChildOf(chassis.transform) || (root != null && t.IsChildOf(root));
        }

        private static bool IsStick(Collider c)
        {
            Vector3 s = c.bounds.size;
            int thin = (s.x < StickThickness ? 1 : 0) + (s.y < StickThickness ? 1 : 0) + (s.z < StickThickness ? 1 : 0);
            return thin >= 2;
        }

        private static readonly System.Collections.Generic.HashSet<string> _sticksSeen = new System.Collections.Generic.HashSet<string>();

        private static void NoteStick(Collider c)
        {
            if (_sticksSeen.Count >= 40 || !_sticksSeen.Add(c.name)) return;
            Core.LogDebug("[Camera] ignored a stick: '" + c.name + "' size " + c.bounds.size.ToString("F2") + ".");
        }

#if DEBUG
        // --- console: 'rvdrive cam pole|wall|clear|log' -------------------------------------------------------

        private static GameObject _testObstacle;

        /// <summary>
        /// <c>pole</c> and <c>wall</c> put a test obstacle across the camera's line of sight, <c>clear</c> removes it,
        /// <c>log</c> reports for three seconds where the camera sits and what the game's own ray would have hit.
        /// </summary>
        internal static void Debug(string arg)
        {
            LandVehicle chassis = RvMobile.Chassis;
            VehicleCamera vc = chassis != null ? chassis.GetComponentInChildren<VehicleCamera>(true) : null;
            if (vc == null || vc.cameraOrigin == null) { Core.Log.Warning("[Camera] rvdrive cam: the RV has no chase camera - drive it first."); return; }
            switch (arg)
            {
                case "pole":
                case "wall":
                {
                    if (_testObstacle != null) UnityEngine.Object.Destroy(_testObstacle);
                    Vector3 origin = vc.cameraOrigin.position, target = vc.GetTargetCameraPosition();
                    Vector3 dir = (target - origin).normalized;
                    _testObstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    _testObstacle.name = "RVRepairVan_Test" + (arg == "pole" ? "Pole" : "Wall");
                    _testObstacle.transform.position = origin + (target - origin) * 0.6f;
                    _testObstacle.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z).normalized);
                    _testObstacle.transform.localScale = arg == "pole" ? new Vector3(0.25f, 10f, 0.25f) : new Vector3(8f, 10f, 0.3f);
                    Core.Log.Msg("[Camera] rvdrive cam: " + arg + " placed " + (Vector3.Distance(origin, target) * 0.6f).ToString("F1")
                        + " m from the look point, layer " + LayerMask.LayerToName(_testObstacle.layer) + ".");
                    break;
                }
                case "clear":
                    if (_testObstacle != null) { UnityEngine.Object.Destroy(_testObstacle); _testObstacle = null; Core.Log.Msg("[Camera] rvdrive cam: test obstacle removed."); }
                    else Core.Log.Msg("[Camera] rvdrive cam: no test obstacle to remove.");
                    break;
                case "log":
                    MelonLoader.MelonCoroutines.Start(LogFor(vc, 3f));
                    break;
                default:
                    Core.Log.Warning("[Camera] rvdrive cam pole|wall|clear|log");
                    break;
            }
        }

        private static System.Collections.IEnumerator LogFor(VehicleCamera vc, float seconds)
        {
            for (float t = 0f; t <= seconds && vc != null; t += 0.5f)
            {
                Vector3 origin = vc.cameraOrigin.position, target = vc.GetTargetCameraPosition();
                Vector3 cam = PlayerSingleton<PlayerCamera>.Instance != null ? PlayerSingleton<PlayerCamera>.Instance.transform.position : Vector3.zero;
                Vector3 dir = (target - origin).normalized;
                string game = Physics.Raycast(origin, dir, out RaycastHit hit, Vector3.Distance(origin, target) + Margin, DefaultMask)
                    ? "'" + hit.collider.name + "' at " + hit.distance.ToString("F1") + " m" : "nothing";
                Core.Log.Msg("[Camera] cam " + Vector3.Distance(origin, cam).ToString("F1") + " m from the look point (wanted "
                    + Vector3.Distance(origin, target).ToString("F1") + ", held " + _distance.ToString("F1") + "), the game's ray hits " + game + ".");
                yield return new WaitForSeconds(0.5f);
            }
        }
#endif
    }
}
