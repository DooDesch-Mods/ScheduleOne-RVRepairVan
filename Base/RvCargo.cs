using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.PlayerTasks;
using Il2CppScheduleOne.Vehicles;
using UnityEngine;

namespace RVRepairVan.Base
{
    /// <summary>
    /// Loose bodies in the driving RV: the bags, buds and ingredients of a station task, a bag of soil being poured.
    /// </summary>
    /// <remarks>
    /// On every peer but the driver's the chassis is kinematic and moved by its transform each frame, and a loose body
    /// under it is teleported along with its parent. Two things in the game undo that: the body is interpolated, so
    /// every frame it writes a pose between its last two world positions back to its transform and drags behind the RV,
    /// and a pourable is held within reach of a world point (<see cref="Draggable.Origin"/>) that stays where the task
    /// started. Without either, a body would ride along as if nailed down and never feel the drive at all. So it rides
    /// as part of the RV, and the RV's own acceleration, smoothed over a few frames, pushes it the other way: it stays
    /// put at a steady speed and slides when the RV speeds up, brakes or turns.
    /// </remarks>
    internal static class RvCargo
    {
        // Smoothing of the RV's speed and of its acceleration. The transform moves once per frame and the physics steps
        // fall between frames at their own rate, so a single step sees no motion or two frames of it.
        private const float VelocityTau = 0.12f, AccelTau = 0.1f;
        // A jump of the RV (back home, a correction from the driver) is not an acceleration the cargo should feel.
        private const float MaxSpeed = 60f, MaxAccel = 15f;
        // Below this the smoothed acceleration is frame noise of a steady drive.
        private const float AccelDeadZone = 0.4f;
        private const float ScanInterval = 0.25f;

#if DEBUG
        internal static bool DebugVanilla;
#endif

        private sealed class Cargo
        {
            internal Rigidbody Rb;
            internal Draggable Drag;
            internal RigidbodyInterpolation Was;
        }

        private static readonly Dictionary<IntPtr, Cargo> _cargo = new Dictionary<IntPtr, Cargo>();
        private static readonly List<IntPtr> _gone = new List<IntPtr>();
        private static float _nextScan;
        private static bool _dynamicNoted;

        // Measured each frame after the RV moved.
        private static Vector3 _framePrevPos, _vel, _acc;
        private static bool _frameHave;
        // Measured each physics step, for the turn the bodies' velocities follow.
        private static Quaternion _stepPrevRot;
        private static bool _stepHave;

        /// <summary>The RV's smoothed acceleration the cargo feels, zero below the noise floor.</summary>
        internal static Vector3 Acceleration => _acc.sqrMagnitude < AccelDeadZone * AccelDeadZone ? Vector3.zero : _acc;

        internal static void FixedStep()
        {
#if DEBUG
            ProbeStep();
#endif
            LandVehicle ch = RvMobile.Chassis;
            if (ch == null) { if (_cargo.Count > 0) Release("the RV is home"); _stepHave = false; return; }
#if DEBUG
            if (DebugVanilla) { if (_cargo.Count > 0) Release("the fix is off"); _stepHave = false; return; }
#endif
            Rigidbody chRb = ch.Rb;
            if (chRb != null && !chRb.isKinematic)
            {
                // The driver's own game simulates the chassis, and the driver works no station.
                if (_cargo.Count > 0) Release("this game drives the RV");
                if (!_dynamicNoted) { _dynamicNoted = true; Core.Log.Msg("[Cargo] this game drives the RV - loose items keep the game's physics."); }
                _stepHave = false;
                return;
            }
            _dynamicNoted = false;
            Transform ct = ch.transform;
            if (Time.time >= _nextScan) { _nextScan = Time.time + ScanInterval; Scan(ch); }
            Quaternion rot = ct.rotation;
            Quaternion turn = _stepHave ? rot * Quaternion.Inverse(_stepPrevRot) : Quaternion.identity;
            _stepPrevRot = rot;
            _stepHave = true;
            if (_cargo.Count == 0) return;

            Vector3 push = -Acceleration;
            _gone.Clear();
            foreach (KeyValuePair<IntPtr, Cargo> kv in _cargo)
            {
                Rigidbody rb = kv.Value.Rb;
                if (rb == null || rb.WasCollected) { _gone.Add(kv.Key); continue; }
                if (rb.isKinematic) continue;
                // The teleport along with the RV turns the body but not its velocity, so a turn would fling it sideways.
                rb.velocity = turn * rb.velocity;
                rb.angularVelocity = turn * rb.angularVelocity;
                Draggable d = kv.Value.Drag;
                if (push != Vector3.zero && (d == null || !d.IsHeld)) rb.AddForce(push, ForceMode.Acceleration);
            }
            foreach (IntPtr p in _gone) _cargo.Remove(p);
        }

        internal static void LateStep()
        {
#if DEBUG
            ProbeFrame();
#endif
            LandVehicle ch = RvMobile.Chassis;
            if (ch == null) { _frameHave = false; _vel = _acc = Vector3.zero; return; }
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 pos = ch.transform.position;
            if (!_frameHave) { _framePrevPos = pos; _frameHave = true; return; }
            Vector3 v = (pos - _framePrevPos) / dt;
            _framePrevPos = pos;
            if (v.magnitude > MaxSpeed)
            {
                Core.Log.Msg("[Cargo] the RV jumped " + (v.magnitude * dt).ToString("F1") + " m in one frame - the cargo does not feel that.");
                _vel = _acc = Vector3.zero;
                _frameHave = false;
                return;
            }
            Vector3 lastVel = _vel;
            _vel += (v - _vel) * (1f - Mathf.Exp(-dt / VelocityTau));
            Vector3 a = Vector3.ClampMagnitude((_vel - lastVel) / dt, MaxAccel);
            _acc += (a - _acc) * (1f - Mathf.Exp(-dt / AccelTau));
        }

        private static void Scan(LandVehicle ch)
        {
            int added = 0;
            foreach (Rigidbody rb in ch.GetComponentsInChildren<Rigidbody>(false))
            {
                if (rb == null || rb.isKinematic || rb.Pointer == ch.Rb.Pointer || _cargo.ContainsKey(rb.Pointer)) continue;
                Cargo c = new Cargo { Rb = rb, Drag = rb.GetComponent<Draggable>(), Was = rb.interpolation };
                // An interpolated body writes its own pose to its transform every frame and stays behind the RV.
                rb.interpolation = RigidbodyInterpolation.None;
                _cargo[rb.Pointer] = c;
                added++;
            }
            if (added > 0) Core.Log.Msg("[Cargo] " + added + " loose item(s) in the RV ride along now (" + _cargo.Count + " in all).");
        }

        private static void Release(string why)
        {
            int n = 0;
            foreach (Cargo c in _cargo.Values)
            {
                if (c.Rb == null || c.Rb.WasCollected) continue;
                c.Rb.interpolation = c.Was;
                n++;
            }
            _cargo.Clear();
            _origins.Clear();
            Core.Log.Msg("[Cargo] " + why + " - " + n + " loose item(s) back to the game's physics.");
        }

        // A pourable's reach point, in the RV's frame, keyed by the draggable. The task sets it in the world once, from a
        // point on the pot, and the game then pulls the bag back to it every frame.
        private sealed class Reach
        {
            internal Vector3 Local;
            internal Vector3 Written;
        }

        private static readonly Dictionary<IntPtr, Reach> _origins = new Dictionary<IntPtr, Reach>();

        internal static void CarryOrigin(Draggable d)
        {
            if (!d.LocationRestrictionEnabled) return;
            LandVehicle ch = RvMobile.Chassis;
            if (ch == null) return;
#if DEBUG
            if (DebugVanilla) return;
#endif
            Transform ct = ch.transform;
            if (!d.transform.IsChildOf(ct)) return;
            Vector3 origin = d.Origin;
            if (!_origins.TryGetValue(d.Pointer, out Reach r) || (origin - r.Written).sqrMagnitude > 1e-8f)
            {
                // New, or the task set it again.
                r = new Reach { Local = ct.InverseTransformPoint(origin) };
                _origins[d.Pointer] = r;
                Core.Log.Msg("[Cargo] '" + d.name + "' keeps its reach point on the RV at " + r.Local.ToString("F2") + ".");
            }
            r.Written = ct.TransformPoint(r.Local);
            d.Origin = r.Written;
        }

#if DEBUG
        // rvcargo probe [s]: per physics step, how the first loose body in the RV moves against the chassis.
        private static float _probeUntil;
        private static Rigidbody _probe;
        private static Vector3 _probePrevBody, _probePrevChassis;
        private static Quaternion _probePrevChassisRot;
        private static bool _probeHave;
        private static int _steps;
        private static Vector3 _visPrev;
        private static bool _visHave;
        private static int _frames;
        private static float _sumVis, _maxVis, _sumAcc, _maxAcc;
        private static float _sumAsWorld, _sumAsCarried, _sumGap, _maxAsWorld, _maxAsCarried, _maxGap, _sumChassis;

        internal static void Probe(float seconds)
        {
            LandVehicle ch = RvMobile.Chassis;
            if (ch == null) { Core.Log.Warning("[Cargo] probe: no chassis out."); return; }
            _probe = null;
            foreach (Rigidbody rb in ch.GetComponentsInChildren<Rigidbody>(false))
                if (rb != null && !rb.isKinematic && rb.Pointer != ch.Rb.Pointer) { _probe = rb; break; }
            if (_probe == null) { Core.Log.Warning("[Cargo] probe: no loose body in the RV - start a station task first."); return; }
            Draggable d = _probe.GetComponent<Draggable>();
            Rigidbody chRb = ch.Rb;
            Core.Log.Msg("[Cargo] probe on '" + _probe.name + "' (parent '" + (_probe.transform.parent != null ? _probe.transform.parent.name : "none")
                + "', interpolation=" + _probe.interpolation + ", draggable=" + (d != null) + (d != null ? ", held=" + d.IsHeld + ", restricted=" + d.LocationRestrictionEnabled : "")
                + ", joint=" + (_probe.GetComponent<Joint>() != null) + "). Chassis kinematic=" + chRb.isKinematic + " interpolation=" + chRb.interpolation
                + " autoSync=" + Physics.autoSyncTransforms + ", fix " + (DebugVanilla ? "off" : "on") + ".");
            _probeUntil = Time.time + seconds;
            _probeHave = false;
            _visHave = false; _frames = 0; _sumVis = _maxVis = _sumAcc = _maxAcc = 0f;
            _steps = 0; _sumAsWorld = _sumAsCarried = _sumGap = _maxAsWorld = _maxAsCarried = _maxGap = _sumChassis = 0f;
        }

        // rvcargo pin: holds the first loose body within reach of where it lies now, the way a pour task holds its bag.
        internal static void Pin()
        {
            LandVehicle ch = RvMobile.Chassis;
            if (ch == null) { Core.Log.Warning("[Cargo] pin: no chassis out."); return; }
            foreach (Draggable d in ch.GetComponentsInChildren<Draggable>(false))
            {
                if (d == null || d.Rb == null || d.Rb.isKinematic) continue;
                d.Origin = d.transform.position;
                d.LocationRestrictionEnabled = true;
                Core.Log.Msg("[Cargo] pin: '" + d.name + "' held within " + d.MaxDistanceFromOrigin.ToString("F2") + " m of " + d.Origin.ToString("F2") + ".");
                return;
            }
            Core.Log.Warning("[Cargo] pin: no loose draggable in the RV - start a station task first.");
        }

        internal static void SetVanilla(bool on)
        {
            DebugVanilla = on;
            Core.Log.Msg("[Cargo] fix " + (on ? "off - loose items move as in the unmodded game." : "on."));
        }

        private static void ProbeStep()
        {
            if (_probe == null) return;
            LandVehicle ch = RvMobile.Chassis;
            if (ch == null || _probe.WasCollected) { _probe = null; Core.Log.Warning("[Cargo] probe: chassis or body gone."); return; }
            Transform ct = ch.transform;
            Vector3 body = _probe.position, tr = _probe.transform.position, c = ct.position;
            Quaternion cr = ct.rotation;
            if (_probeHave)
            {
                float dt = Time.fixedDeltaTime;
                // The body's drift against the RV, once taken against the chassis as it stands now (near zero when the
                // chassis carried the body along) and once against the chassis' last pose (near zero when the body
                // stayed where it was in the world and the chassis drove off).
                Vector3 relPrev = Quaternion.Inverse(_probePrevChassisRot) * (_probePrevBody - _probePrevChassis);
                Vector3 asWorld = Quaternion.Inverse(cr) * (body - c) - relPrev;
                Vector3 asCarried = Quaternion.Inverse(_probePrevChassisRot) * (body - _probePrevChassis) - relPrev;
                float w = asWorld.magnitude / dt, k = asCarried.magnitude / dt, g = (tr - body).magnitude;
                _sumAsWorld += w; _sumAsCarried += k; _sumGap += g;
                _maxAsWorld = Mathf.Max(_maxAsWorld, w); _maxAsCarried = Mathf.Max(_maxAsCarried, k); _maxGap = Mathf.Max(_maxGap, g);
                _sumChassis += (c - _probePrevChassis).magnitude / dt;
                _steps++;
            }
            _probePrevBody = body; _probePrevChassis = c; _probePrevChassisRot = cr; _probeHave = true;
            if (Time.time < _probeUntil) return;
            float n = Mathf.Max(1, _steps), f = Mathf.Max(1, _frames);
            Core.Log.Msg("[Cargo] probe: " + _steps + " step(s), chassis " + (_sumChassis / n).ToString("F2") + " m/s. Body against the RV now: "
                + (_sumAsWorld / n).ToString("F3") + " m/s avg, " + _maxAsWorld.ToString("F3") + " max; against its last pose: "
                + (_sumAsCarried / n).ToString("F3") + " m/s avg, " + _maxAsCarried.ToString("F3") + " max. Transform off the body "
                + (_sumGap / n * 1000f).ToString("0.0") + " mm avg, " + (_maxGap * 1000f).ToString("0.0") + " mm max. On screen it moved "
                + (_sumVis / f * 1000f).ToString("0.0") + " mm per frame avg, " + (_maxVis * 1000f).ToString("0.0") + " mm max against the RV ("
                + _frames + " frame(s)). RV acceleration felt " + (_sumAcc / f).ToString("F2") + " m/s2 avg, " + _maxAcc.ToString("F2") + " max.");
            _probe = null;
        }

        // Before the frame renders: how far the body's shown pose moved against the RV's shown pose since the last frame.
        private static void ProbeFrame()
        {
            if (_probe == null) return;
            LandVehicle ch = RvMobile.Chassis;
            if (ch == null || _probe.WasCollected) return;
            Vector3 rel = ch.transform.InverseTransformPoint(_probe.transform.position);
            if (_visHave)
            {
                float d = (rel - _visPrev).magnitude, a = Acceleration.magnitude;
                _sumVis += d; _maxVis = Mathf.Max(_maxVis, d); _sumAcc += a; _maxAcc = Mathf.Max(_maxAcc, a); _frames++;
            }
            _visPrev = rel; _visHave = true;
        }
#endif
    }

    /// <summary>Keeps a pourable's reach point on the RV before the game pulls the bag back to it.</summary>
    [HarmonyPatch(typeof(Draggable), "LateUpdate")]
    internal static class RvCargoOriginPatch
    {
        private static void Prefix(Draggable __instance)
        {
            try { RvCargo.CarryOrigin(__instance); }
            catch (Exception e) { Core.Swallowed("[Cargo] carrying a pourable's reach point", e); }
        }
    }
}
