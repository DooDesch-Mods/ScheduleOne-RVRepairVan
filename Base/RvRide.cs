using System;
using HarmonyLib;
using Il2CppScheduleOne.Vehicles;

namespace RVRepairVan.Base
{
    /// <summary>
    /// Riding along in the RV while someone else drives it.
    ///
    /// The RV's floor and walls ride on the chassis as kinematic bodies. A CharacterController does not move with a
    /// platform it stands on, so the local player standing in the RV is carried here: each frame, before the player's
    /// own movement runs, by the chassis' motion since the last frame. Each client carries only its own player; the
    /// network shows the others where they stand.
    ///
    /// A station sets the camera to a fixed world pose (<c>PlayerCamera.OverrideTransform</c>) and nothing moves it
    /// after that. On board, that pose is kept in chassis space instead, so the view rides along with the station.
    ///
    /// The chassis' own colliders and the hull box sit inside the RV body. Near or in the RV, the local player's
    /// colliders ignore them, so the hull neither pushes the player out of the cabin nor gets pushed by the player.
    /// </summary>
    internal static class RvRide
    {
        private const float NearMargin = 1f;   // past the hull's sides: close enough to step in through the door

        private static IntPtr _chassisPtr;
        private static bool _have;
        private static Matrix4x4 _prevInv;
        private static float _prevYaw;
        private static bool _onBoard, _ignoring;
        private static readonly System.Collections.Generic.HashSet<IntPtr> _ignoringPlayers = new System.Collections.Generic.HashSet<IntPtr>();
        private static int _failures;

        // The station camera, in chassis space: where it lerps from and to, and over how long.
        private static bool _cam;
        private static Vector3 _camFrom, _camTo;
        private static Quaternion _camFromRot, _camToRot;
        private static float _camStart, _camLerp;

        internal static bool OnBoard => _onBoard;
        internal static bool Ignoring => _ignoring;
        internal static bool CameraPinned => _cam;

        internal static void Reset()
        {
            _chassisPtr = IntPtr.Zero;
            _have = _onBoard = _ignoring = _cam = false;
            _ignoringPlayers.Clear();
        }

        /// <summary>Every frame, before the local player's own movement: carry them by the chassis' motion.</summary>
        internal static void Step(PlayerMovement pm)
        {
            try { HealCameraOffset(pm); }
            catch (Exception e) { if (_healFailures++ == 0) Core.Log.Error("[Ride] checking the camera's place failed: " + e); }
            LandVehicle chassis = RvMobile.Chassis;
            if (chassis == null)
            {
                if (_onBoard) Core.Log.Msg("[Ride] the RV is home - the player is no longer carried.");
                _have = _onBoard = _cam = false;
                _chassisPtr = IntPtr.Zero;
                return;
            }
            try
            {
                Transform ct = chassis.transform;
                if (_chassisPtr != chassis.Pointer) { _chassisPtr = chassis.Pointer; _have = false; }
                Matrix4x4 now = ct.localToWorldMatrix;
                float yaw = Yaw(ct);

                Player local = Player.Local;
                bool seated = local != null && local.CurrentVehicle != null;
                CharacterController cc = pm.Controller;
                // The game moves the player by the controller's transform. PlayerMovement sits on a child of it that
                // carries the camera; moving that child alone leaves the body behind, and the camera then swings around
                // the body whenever the player looks left or right.
                Transform pt = cc != null ? cc.transform : pm.transform;
                bool onBoard = !seated && cc != null && cc.enabled && RvMobile.Inside(pt.position, 0.3f);
                if (onBoard != _onBoard)
                {
                    Core.Log.Msg(onBoard ? "[Ride] the player stands in the RV - carried while it moves." : "[Ride] the player left the RV.");
                    _onBoard = onBoard;
                }
                if (onBoard && _have)
                {
                    Vector3 from = pt.position;
                    Vector3 to = now.MultiplyPoint3x4(_prevInv.MultiplyPoint3x4(from));
                    float turn = Mathf.DeltaAngle(_prevYaw, yaw);
                    // autoSyncTransforms is on in this game, so the controller picks the new position up before it moves.
                    if ((to - from).sqrMagnitude > 1e-8f) pt.position = to;
                    if (Mathf.Abs(turn) > 1e-4f && local != null) local.transform.Rotate(0f, turn, 0f, Space.World);
                }
                if (_cam) PinCamera(ct);

                _prevInv = now.inverse;
                _prevYaw = yaw;
                _have = true;
                _failures = 0;
            }
            catch (Exception e)
            {
                if (_failures++ == 0) Core.Log.Error("[Ride] carrying the player failed: " + e);
                _have = false;
            }
        }

        private static bool _healedOnce;
        private static int _healFailures;

        // Earlier versions carried only the camera's child, which leaves it off the body's centre for good: it survives
        // a death and the switch to third person. Put it back once the player stands on their own.
        private static void HealCameraOffset(PlayerMovement pm)
        {
            CharacterController cc = pm.Controller;
            Player local = Player.Local;
            if (cc == null || !cc.enabled || local == null || local.CurrentVehicle != null) return;
            Transform child = pm.transform;
            if (child.Pointer == cc.transform.Pointer || child.parent == null || child.parent.Pointer != cc.transform.Pointer) return;
            Vector3 off = child.localPosition;
            if (off.sqrMagnitude < 0.0025f) return;
            child.localPosition = Vector3.zero;
            if (!_healedOnce) { _healedOnce = true; Core.Log.Warning("[Ride] the camera stood " + off.ToString("F2") + " off the player's body - put back."); }
            else Core.Log.Msg("[Ride] camera offset " + off.ToString("F2") + " put back.");
        }

        private static float Yaw(Transform t)
        {
            Vector3 f = t.forward;
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        // --- station camera ------------------------------------------------------------------------------

        /// <summary>The camera is about to be set to a world pose. On board, remember that pose in chassis space.</summary>
        internal static void OnCameraOverride(PlayerCamera pc, Vector3 worldPos, Quaternion rot, float lerpTime, bool keepParented)
        {
            _cam = false;
            LandVehicle chassis = RvMobile.Chassis;
            Player local = Player.Local;
            if (keepParented || chassis == null || pc == null || !_onBoard || (local != null && local.CurrentVehicle != null)) return;
            Transform ct = chassis.transform;
            Quaternion inv = Quaternion.Inverse(ct.rotation);
            _camFrom = ct.InverseTransformPoint(pc.transform.position);
            _camFromRot = inv * pc.transform.rotation;
            _camTo = ct.InverseTransformPoint(worldPos);
            _camToRot = inv * rot;
            _camStart = Time.time;
            _camLerp = lerpTime;
            _cam = true;
            Core.Log.Msg("[Ride] station camera pinned to the RV at chassis-local " + _camTo.ToString("F2") + ".");
        }

        // The game's own lerp writes the world pose at the end of the frame, after rendering; this runs before the
        // frame renders, so the view shows the pose that rides with the RV.
        private static void PinCamera(Transform ct)
        {
            var pc = PlayerSingleton<PlayerCamera>.Instance;
            Player local = Player.Local;
            // Taking a seat moves the camera to the vehicle's own view; that one is not a station's.
            if (pc == null || !pc.transformOverriden || pc.transform.parent != null || (local != null && local.CurrentVehicle != null))
            {
                _cam = false;
                Core.Log.Msg("[Ride] station camera released.");
                return;
            }
            float t = _camLerp > 0f ? Mathf.Clamp01((Time.time - _camStart) / _camLerp) : 1f;
            pc.transform.SetPositionAndRotation(ct.TransformPoint(Vector3.Lerp(_camFrom, _camTo, t)),
                ct.rotation * Quaternion.Lerp(_camFromRot, _camToRot, t));
        }

        // --- collisions with the chassis -----------------------------------------------------------------

        /// <summary>
        /// Near or in the RV, every player's colliders ignore the chassis' colliders and the hull; further out they
        /// collide again, so the RV still stops at a player in the road. Measured at the capsule, which follows the
        /// avatar into a seat while the player's root stays where they got in, and on a seat in the chassis itself.
        /// Re-applied on every watch tick, because switching a collider on or off, or leaving a vehicle, drops the
        /// ignores again. A capsule left colliding inside the chassis shoves it sideways and throws the RV over.
        ///
        /// Two colliders per player: the capsule and the root's CharacterController. On a remote player the capsule is
        /// a trigger and the controller is the solid one, so where a client drives, the controller of a player
        /// standing in the cabin is what the chassis would push against.
        /// </summary>
        internal static void KeepClear(bool force = false)
        {
            LandVehicle chassis = RvMobile.Chassis;
            var players = Player.PlayerList;
            if (players == null) return;
            var pm = PlayerSingleton<PlayerMovement>.Instance;
            IntPtr chassisObj = chassis != null && chassis.NetworkObject != null ? chassis.NetworkObject.Pointer : IntPtr.Zero;
            foreach (Player p in players)
            {
                if (p == null) continue;
                bool local = p.IsLocalPlayer;
                Collider cap = p.CapCol;
                Collider body = local && pm != null ? pm.Controller : p.GetComponent<CharacterController>();
#if DEBUG
                if (DebugRemoteBodyCollides && !local) body = null;
#endif
                bool seated = chassisObj != IntPtr.Zero && p.CurrentVehicle != null && p.CurrentVehicle.Pointer == chassisObj;
                bool near = chassis != null && (seated
                    || (cap != null && RvMobile.Inside(cap.transform.position, NearMargin))
                    || (body != null && RvMobile.Inside(body.transform.position, NearMargin)));
                bool was = _ignoringPlayers.Contains(p.Pointer);
                if (!near && !was && !force) continue;
                int pairs = 0;
                foreach (Collider c in RvMobile.SolidChassisColliders())
                {
                    if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                    pairs += Ignore(cap, c, near) + Ignore(body, c, near);
                }
                if (near == was) continue;
                if (near) _ignoringPlayers.Add(p.Pointer); else _ignoringPlayers.Remove(p.Pointer);
                if (local) _ignoring = near;
                Core.Log.Msg("[Ride] " + (local ? "the player" : "player " + p.PlayerName) + " " + (near ? "passes through" : "collides with")
                    + " the chassis and hull again (" + pairs + " pair(s)" + (seated ? ", seated" : "") + ").");
            }
        }

        private static int Ignore(Collider a, Collider b, bool ignore)
        {
            if (a == null || !a.enabled || !a.gameObject.activeInHierarchy) return 0;
            Physics.IgnoreCollision(a, b, ignore);
            return 1;
        }

#if DEBUG
        internal static string DebugState()
        {
            var pm = PlayerSingleton<PlayerMovement>.Instance;
            LandVehicle chassis = RvMobile.Chassis;
            string where = pm != null && chassis != null ? " chassisLocal=" + chassis.transform.InverseTransformPoint(pm.transform.position).ToString("F2") : "";
            return "onBoard=" + _onBoard + " ignoring=" + _ignoring + " cameraPinned=" + _cam + where;
        }

        /// <summary>rvdrive ride oldbody: remote players' controllers collide with the chassis again, as before the fix.</summary>
        internal static bool DebugRemoteBodyCollides;

        /// <summary>rvdrive ride [seconds]: log where the player and the camera stand in chassis space, twice a second.</summary>
        internal static void DebugWatch(string arg)
        {
            float seconds = float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s) ? s : 0f;
            if (arg == "oldbody")
            {
                DebugRemoteBodyCollides = !DebugRemoteBodyCollides;
                Core.Log.Msg("[Ride] remote players' controllers " + (DebugRemoteBodyCollides ? "collide with" : "ignore") + " the chassis (debug).");
                return;
            }
            if (seconds <= 0f) { Core.Log.Msg("[Ride] " + RvMobile.DebugState()); return; }
            MelonLoader.MelonCoroutines.Start(Watch(seconds));
        }

        private static System.Collections.IEnumerator Watch(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                LandVehicle chassis = RvMobile.Chassis;
                var pc = PlayerSingleton<PlayerCamera>.Instance;
                string cam = chassis != null && pc != null ? " camLocal=" + chassis.transform.InverseTransformPoint(pc.transform.position).ToString("F2") : "";
                string speed = chassis != null ? " speed=" + chassis.Speed_Kmh.ToString("0.0") : "";
                Core.Log.Msg("[Ride] watch " + DebugState() + cam + speed);
                yield return new WaitForSeconds(0.5f);
            }
        }

        /// <summary>rvdrive pose: the local player's root and camera, to tell a tilted root from a moved camera.</summary>
        internal static void DebugPose()
        {
            var pm = PlayerSingleton<PlayerMovement>.Instance;
            var pc = PlayerSingleton<PlayerCamera>.Instance;
            Player local = Player.Local;
            if (pm == null || pc == null || local == null) { Core.Log.Warning("[Ride] pose: no local player or camera."); return; }
            CharacterController cc = pm.Controller;
            Transform ct = pc.transform;
            Core.Log.Msg("[Ride] pose root=" + pm.transform.position.ToString("F2") + " euler=" + pm.transform.eulerAngles.ToString("F1")
                + " playerRootSame=" + (local.transform.Pointer == pm.transform.Pointer)
                + " controller=" + (cc != null ? cc.enabled + " h=" + cc.height.ToString("F2") + " c=" + cc.center.ToString("F2") : "none")
                + " vehicle=" + (local.CurrentVehicle != null));
            Core.Log.Msg("[Ride] pose camera parent=" + (ct.parent != null ? ct.parent.name : "none") + " local=" + ct.localPosition.ToString("F2")
                + " localEuler=" + ct.localEulerAngles.ToString("F1") + " world=" + ct.position.ToString("F2") + " overridden=" + pc.transformOverriden
                + " avatarParent=" + (local.Avatar != null && local.Avatar.transform.parent != null ? local.Avatar.transform.parent.name : "none")
                + " avatarLocal=" + (local.Avatar != null ? local.Avatar.transform.localPosition.ToString("F2") : "-"));
            Transform lt = local.transform, mt = pm.transform;
            Core.Log.Msg("[Ride] pose player='" + lt.name + "' parent=" + (lt.parent != null ? lt.parent.name : "none") + " world=" + lt.position.ToString("F2")
                + " euler=" + lt.eulerAngles.ToString("F1") + " | movement='" + mt.name + "' parent=" + (mt.parent != null ? mt.parent.name : "none")
                + " local=" + mt.localPosition.ToString("F2") + " localEuler=" + mt.localEulerAngles.ToString("F1")
                + " | controllerOn='" + (cc != null ? cc.transform.name : "-") + "'");
        }
#endif
    }

    [HarmonyPatch(typeof(PlayerMovement), nameof(PlayerMovement.Update))]
    internal static class RvRidePlayerPatch
    {
        private static void Prefix(PlayerMovement __instance) => RvRide.Step(__instance);
    }

    [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.OverrideTransform))]
    internal static class RvRideCameraPatch
    {
        private static void Prefix(PlayerCamera __instance, Vector3 worldPos, Quaternion rot, float lerpTime, bool keepParented)
        {
            try { RvRide.OnCameraOverride(__instance, worldPos, rot, lerpTime, keepParented); }
            catch (Exception e) { Core.Log.Error("[Ride] pinning the station camera failed: " + e); }
        }
    }
}
