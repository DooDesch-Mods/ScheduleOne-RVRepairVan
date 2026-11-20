using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.StationFramework;
using UnityEngine;
#if DEBUG
using Il2CppScheduleOne.ObjectScripts;
using MelonLoader;
using RVRepairVan.Managers;
#endif

namespace RVRepairVan.Base
{
    /// <summary>
    /// Pouring at a station in an RV that stands tilted. A pourable (the beaker of a chemistry station, the bottles of a
    /// cauldron or lab oven) tips only as far as its joint lets it against the station, but the game starts the pour once
    /// the spout points far enough away from world up (<c>PourableModule.UpdatePouring</c>). On a level floor the
    /// beaker's joint limit lands within a degree of the pour angle. A station that leans with the parked RV moves that
    /// limit by the floor's tilt, and leaning the wrong way the beaker stops just short of pouring, so no cook can start.
    /// Here the pour angle is measured against the RV's up, as it would be with the RV at home.
    /// </summary>
    internal static class RvPour
    {
#if DEBUG
        /// <summary>rvpour vanilla: leave the game's pour angle alone, to show the pour it allows.</summary>
        internal static bool DebugVanilla;
#endif

        // Modules already reported, so the per-step path logs once per pourable.
        private static readonly HashSet<IntPtr> _noted = new HashSet<IntPtr>();
        private static bool _noPourPointNoted, _failedNoted;

        /// <summary>
        /// Before the game checks the pour: shift its pour angle by how much the RV's tilt changes the spout's angle from
        /// up, so the check reads as at home. Returns the angle to put back afterwards, or NaN when nothing was changed.
        /// </summary>
        internal static float Shift(PourableModule m)
        {
#if DEBUG
            if (DebugVanilla) return float.NaN;
#endif
            if (m == null) return float.NaN;
            // At home, off the RV, or with the RV standing as it does at home, the game's own check is right.
            if (!RvItemTilt.AwayFrame(m.transform, out Quaternion away)) return float.NaN;
            Transform pp = m.PourPoint;
            if (pp == null)
            {
                if (!_noPourPointNoted) { _noPourPointNoted = true; Core.Log.Warning("[Pour] '" + m.name + "' has no pour point - it pours as the game measures it."); }
                return float.NaN;
            }
            Vector3 f = pp.forward, rvUp = away * Vector3.up;
            float delta = Vector3.Angle(Vector3.up, f) - Vector3.Angle(rvUp, f);
            float original = m.AngleFromUpToPour;
            m.AngleFromUpToPour = original + delta;
            if (_noted.Add(m.Pointer))
                Core.Log.Msg("[Pour] '" + m.name + "' pours as at home on the tilted RV floor (floor tilt "
                    + Vector3.Angle(rvUp, Vector3.up).ToString("F1") + " deg, pour angle " + original.ToString("F1") + " deg against the RV).");
            return original;
        }

        internal static void Restore(PourableModule m, float original)
        {
            if (m != null && !float.IsNaN(original)) m.AngleFromUpToPour = original;
        }

        internal static void Failed(string what, Exception e)
        {
            if (_failedNoted) return;
            _failedNoted = true;
            Core.Log.Error("[Pour] " + what + " failed - a pour in the tilted RV may not start: " + e);
        }

#if DEBUG
        // --- rvpour tip: tips a chemistry station's beaker against its joint and records whether the game pours ---------

        private static Beaker _beaker;
        private static ChemistryStation _station;
        private static float _until, _flipAt, _level;
        private static float _maxWorld, _maxHome, _needHome, _limit;
        private static float _tiltMin, _tiltMax, _maxSpin, _minWorld, _minHome;
        private static int _steps, _pouring, _kinematic;

        /// <summary>
        /// rvpour tip [level] [seconds]: makes a beaker on the first chemistry station in the RV, fills it to the level
        /// (0..1), holds it lifted and tips it with a steady torque, half the time each way, the way a player pulls it
        /// against its joint. The game's own pour check runs on it; the result line says whether it poured.
        /// </summary>
        internal static void Tip(float level, float seconds)
        {
            Transform root = RVManager.Root;
            RV rv = root != null ? root.GetComponent<RV>() : null;
            if (rv == null || rv.Container == null) { Core.Log.Warning("[Pour] tip: RV contents not found."); return; }
            ChemistryStation st = rv.Container.GetComponentInChildren<ChemistryStation>();
            if (st == null) { Core.Log.Warning("[Pour] tip: no chemistry station in the RV - 'rvplace chemistrystation' first."); return; }
            if (_beaker != null) { UnityEngine.Object.Destroy(_beaker.gameObject); _beaker = null; Core.Log.Msg("[Pour] tip: the last test beaker removed."); }
            _station = st;
            _level = Mathf.Clamp01(level);
            Beaker b = st.CreateBeaker();
            if (b == null) { Core.Log.Warning("[Pour] tip: the station made no beaker."); return; }
            st.StaticBeaker.gameObject.SetActive(false);
            MelonCoroutines.Start(StartTip(b, seconds));
        }

        // The beaker's joint and constraint connect in Start, one frame after it is made.
        private static IEnumerator StartTip(Beaker b, float seconds)
        {
            yield return null;
            yield return null;
            if (b == null || b.WasCollected) { Core.Log.Warning("[Pour] tip: the beaker was gone before the tip."); yield break; }
            PourableModule p = b.Pourable;
            if (p == null) { Core.Log.Warning("[Pour] tip: the beaker has no pourable module."); yield break; }
            // What the cook's pour step does to the beaker.
            b.SetStatic(false);
            p.ActivateModule(b);
            b.Fillable.enabled = false;
            p.SetLiquidLevel(p.LiquidCapacity_L * _level);
            b.Draggable.Rb.useGravity = false;
            // The station's own colliders stop a beaker tipped in place; the measure is the joint against the pour angle.
            b.Draggable.Rb.detectCollisions = false;
            _beaker = b;
            _steps = _pouring = 0;
            _maxWorld = _maxHome = _maxSpin = 0f;
            _tiltMin = float.MaxValue; _tiltMax = float.MinValue; _kinematic = 0;
            _minWorld = _minHome = 180f;
            _until = Time.time + seconds;
            _flipAt = Time.time + seconds * 0.5f;
            Rigidbody rb = b.Draggable.Rb;
            ConfigurableJoint j = b.Joint;
            Core.Log.Msg("[Pour] tip: beaker on '" + _station.name + "' at level " + _level.ToString("F2") + ", tipping for " + seconds.ToString("F1") + " s. Body kinematic "
                + rb.isKinematic + ", constraints " + rb.constraints + ", angular drag " + rb.angularDrag.ToString("F1") + ", max spin " + rb.maxAngularVelocity.ToString("F1")
                + "; joint to '" + (j.connectedBody != null ? j.connectedBody.name : "<world>") + "', angular motion " + j.angularXMotion + "/" + j.angularYMotion + "/" + j.angularZMotion + ".");
        }

        /// <summary>Each physics step of a tip: hold the beaker lifted, push it over, record the game's pour state.</summary>
        internal static void FixedStep()
        {
            Beaker b = _beaker;
            if (b == null) return;
            if (b.WasCollected || _station == null) { _beaker = null; Core.Log.Warning("[Pour] tip: beaker or station gone mid-tip."); return; }
            Rigidbody rb = b.Draggable.Rb;
            PourableModule p = b.Pourable;
            Vector3 rvUp = RvMobile.AwayRotation * Vector3.up;
            Vector3 hold = _station.BeakerAlignmentTransform.position + rvUp * 0.3f;
            rb.velocity = (hold - rb.position) * 10f;
            float sign = Time.time < _flipAt ? 1f : -1f;
            rb.AddTorque(b.transform.forward * (40f * sign), ForceMode.Acceleration);
            Vector3 f = p.PourPoint.forward;
            _maxWorld = Mathf.Max(_maxWorld, Vector3.Angle(Vector3.up, f));
            _maxHome = Mathf.Max(_maxHome, Vector3.Angle(rvUp, f));
            _minWorld = Mathf.Min(_minWorld, Vector3.Angle(Vector3.up, f));
            _minHome = Mathf.Min(_minHome, Vector3.Angle(rvUp, f));
            _needHome = p.AngleFromUpToPour;
            _limit = b.Joint.angularZLimit.limit;
            // Tilt of the beaker against its station, about the axis the joint lets it tip around.
            float tilt = Vector3.SignedAngle(_station.ItemContainer.up, Vector3.ProjectOnPlane(b.transform.up, b.transform.forward), b.transform.forward);
            _tiltMin = Mathf.Min(_tiltMin, tilt);
            _tiltMax = Mathf.Max(_tiltMax, tilt);
            _maxSpin = Mathf.Max(_maxSpin, rb.angularVelocity.magnitude);
            if (rb.isKinematic) _kinematic++;
            _steps++;
            if (p.IsPouring) _pouring++;
            if (Time.time < _until) return;

            Vector3 axis = b.transform.forward;
            // How far world up stands from the station's up within the plane the beaker tips in: the part of the
            // floor's slope that moves the pour.
            float lean = Vector3.SignedAngle(rvUp, Vector3.ProjectOnPlane(Vector3.up, axis), axis);
            Core.Log.Msg("[Pour] tip result: " + (_pouring > 0 ? "POURED" : "NO POUR") + " in " + _pouring + " of " + _steps + " step(s). Level "
                + _level.ToString("F2") + ", joint limit " + _limit.ToString("F1") + " deg, pour angle " + _needHome.ToString("F1")
                + " deg. Spout reached " + _maxWorld.ToString("F1") + " deg from world up, " + _maxHome.ToString("F1") + " deg from the RV's up. Floor tilt "
                + Vector3.Angle(rvUp, Vector3.up).ToString("F1") + " deg, " + lean.ToString("F1") + " deg of it in the beaker's tip plane. Fix "
                + (DebugVanilla ? "off" : "on") + ", chassis " + (RvMobile.Chassis != null ? "out" : "home") + ". Beaker tilted "
                + _tiltMin.ToString("F1") + ".." + _tiltMax.ToString("F1") + " deg against the station, spin up to " + _maxSpin.ToString("F1")
                + " rad/s, kinematic in " + _kinematic + " step(s). Spout low " + _minWorld.ToString("F1") + " deg from world up, "
                + _minHome.ToString("F1") + " deg from the RV's up. Station up " + Vector3.Angle(Vector3.up, _station.ItemContainer.up).ToString("F1")
                + " deg from world up, " + Vector3.Angle(rvUp, _station.ItemContainer.up).ToString("F1") + " from the RV's; anchor up "
                + (b.Joint.connectedBody != null ? Vector3.Angle(Vector3.up, b.Joint.connectedBody.transform.up).ToString("F1") + "/" + Vector3.Angle(rvUp, b.Joint.connectedBody.transform.up).ToString("F1") : "-")
                + "; chassis up " + (RvMobile.Chassis != null ? Vector3.Angle(Vector3.up, RvMobile.Chassis.transform.up).ToString("F1") : "-") + " deg.");
            UnityEngine.Object.Destroy(b.gameObject);
            _beaker = null;
            _station.StaticBeaker.gameObject.SetActive(true);
        }

        internal static void SetVanilla(bool on)
        {
            DebugVanilla = on;
            Core.Log.Msg("[Pour] fix " + (on ? "off - pours are measured against world up as in the unmodded game." : "on."));
        }
#endif
    }

    [HarmonyPatch(typeof(PourableModule), "UpdatePouring")]
    internal static class RvPourPatch
    {
        private static void Prefix(PourableModule __instance, out float __state)
        {
            __state = float.NaN;
            try { __state = RvPour.Shift(__instance); }
            catch (Exception e) { RvPour.Failed("measuring a pour against the RV floor", e); }
        }

        private static void Postfix(PourableModule __instance, float __state)
        {
            try { RvPour.Restore(__instance, __state); }
            catch (Exception e) { RvPour.Failed("putting back a pour angle", e); }
        }
    }
}
