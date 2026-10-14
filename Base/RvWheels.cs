using System;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using Il2CppScheduleOne.Vehicles;

namespace RVRepairVan.Base
{
    /// <summary>
    /// Makes the RV's own tyres the wheels of the hidden chassis, so the RV reads as the vehicle and not as a box on
    /// a smaller one.
    ///
    /// The chassis is shorter than the RV. Its wheel colliders are moved under the RV's tyres, with the tyre radius,
    /// so the RV rests where its tyres touch the road - on a slope and over a kerb too. Each RV tyre then follows
    /// the pose of the chassis wheel under it: spin, steering and suspension travel. Where that pose is not
    /// simulated here (a client watching someone else drive), the tyres roll by the distance covered and steer
    /// with the wheel's steer angle.
    ///
    /// A tyre in the scene's static batch cannot be moved through its transform; <see cref="RvMobile"/> redraws
    /// those, and asks <see cref="Spin"/> for the extra motion. Any other tyre is moved through its transform and
    /// set back when the RV goes home.
    /// </summary>
    internal static class RvWheels
    {
        private sealed class Tyre
        {
            public Renderer R;
            public bool Static;
            public Vector3 Centre;            // chassis space, at rest
            public float Radius;
            public Vector3 RestPos;           // transform pose in chassis space, at rest
            public Quaternion RestRot;
            public Vector3 LocalPos;          // transform pose under its parent, to set back
            public Quaternion LocalRot;
            public Axle Axle;
            public Matrix4x4 Motion = Matrix4x4.identity;   // chassis space, about the chassis origin
        }

        private sealed class Axle
        {
            public WheelCollider W;
            public Vector3 Rest;              // wheel centre at rest, chassis space
            public float Radius, Roll;        // Roll: the fallback spin, degrees
        }

        private static readonly List<Tyre> _tyres = new List<Tyre>();
        private static readonly List<Axle> _axles = new List<Axle>();
        private static Vector3 _homePos;
        private static Quaternion _homeRot = Quaternion.identity;
        private static LandVehicle _chassis;
        private static object _loop;
        private static Vector3 _last;
        private static bool _hasLast;

        /// <summary>
        /// Find the tyres while the model still stands at home, and put the chassis wheels under them.
        /// <paramref name="homePos"/>/<paramref name="homeRot"/> are the chassis pose at home, already levelled.
        /// </summary>
        internal static void Capture(Transform model, LandVehicle chassis, Vector3 homePos, Quaternion homeRot)
        {
            Release();
            _chassis = chassis;
            _homePos = homePos;
            _homeRot = homeRot;
            _hasLast = false;
            Quaternion inv = Quaternion.Inverse(homeRot);

            Transform body = model.Find("rv") ?? model;
            int stat = 0;
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(false))
            {
                string n = r.name;
                if (!n.StartsWith("Wheel", StringComparison.OrdinalIgnoreCase) || n.StartsWith("WheelBracket", StringComparison.OrdinalIgnoreCase)) continue;
                Bounds b = r.bounds;
                if (b.extents.y < 0.05f) { Core.Log.Warning("[Wheels] '" + n + "' has no size - it does not turn."); continue; }
                Transform t = r.transform;
                var tyre = new Tyre
                {
                    R = r,
                    Static = r.isPartOfStaticBatch,
                    Centre = inv * (b.center - homePos),
                    Radius = b.extents.y,
                    RestPos = inv * (t.position - homePos),
                    RestRot = inv * t.rotation,
                    LocalPos = t.localPosition,
                    LocalRot = t.localRotation,
                };
                if (tyre.Static) stat++;
                _tyres.Add(tyre);
            }
            if (_tyres.Count == 0) { Core.Log.Warning("[Wheels] no RV tyres found - the chassis keeps its own wheels."); return; }

            int moved = PlaceAxles(chassis);
            _loop = MelonCoroutines.Start(Loop());
            Core.Log.Msg("[Wheels] " + _tyres.Count + " RV tyre(s) on " + moved + " chassis wheel(s) (" + stat + " redrawn, radius "
                + _tyres[0].Radius.ToString("0.00") + " m).");
        }

        // Each chassis wheel goes under the RV tyres on its side and end (a twin rear tyre counts once, at the pair's
        // middle), at the tyre radius, with its contact patch kept at the height the chassis already rests on.
        private static int PlaceAxles(LandVehicle chassis)
        {
            Transform ct = chassis.transform;
            var wheels = new List<WheelCollider>();
            foreach (WheelCollider w in chassis.GetComponentsInChildren<WheelCollider>(true)) wheels.Add(w);
            if (wheels.Count == 0) { Core.Log.Warning("[Wheels] the chassis has no wheel colliders."); return 0; }

            float wheelMid = 0f, tyreMid = 0f;
            foreach (WheelCollider w in wheels) wheelMid += ct.InverseTransformPoint(w.transform.position).z;
            wheelMid /= wheels.Count;
            foreach (Tyre t in _tyres) tyreMid += t.Centre.z;
            tyreMid /= _tyres.Count;

            int moved = 0;
            foreach (WheelCollider w in wheels)
            {
                Vector3 wl = ct.InverseTransformPoint(w.transform.position);
                bool left = wl.x < 0f, front = wl.z > wheelMid;
                Vector3 sum = Vector3.zero;
                float radius = 0f;
                int n = 0;
                var mine = new List<Tyre>();
                foreach (Tyre t in _tyres)
                {
                    if ((t.Centre.x < 0f) != left || (t.Centre.z > tyreMid) != front) continue;
                    sum += t.Centre;
                    radius = Mathf.Max(radius, t.Radius);
                    mine.Add(t);
                    n++;
                }
                if (n == 0) { Core.Log.Warning("[Wheels] no RV tyre over the chassis wheel '" + w.name + "' - it stays where it is."); continue; }
                Vector3 target = sum / n;

                // Contact height before the move: the chassis was levelled so the RV tyres stand on it.
                float rest = w.suspensionDistance * w.suspensionSpring.targetPosition;
                float ground = ct.InverseTransformPoint(w.transform.TransformPoint(w.center)).y - rest - w.radius;
                Vector3 centreAtRest = new Vector3(target.x, ground + radius, target.z);
                // The collider's own centre offset is kept, so its transform goes where that offset lands on the tyre.
                Vector3 offset = ct.InverseTransformVector(w.transform.TransformVector(w.center));
                w.transform.position = ct.TransformPoint(centreAtRest - offset + Vector3.up * rest);
                w.radius = radius;
                var axle = new Axle { W = w, Rest = centreAtRest, Radius = radius };
                _axles.Add(axle);
                foreach (Tyre t in mine) t.Axle = axle;
                moved++;
                Core.LogDebug("[Wheels]   " + w.name + " -> " + centreAtRest.ToString("F2") + " r " + radius.ToString("0.00") + " (" + n + " tyre(s)).");
            }
            return moved;
        }

        /// <summary>The extra motion of a redrawn static tyre, in home world space; identity for anything else.</summary>
        internal static Matrix4x4 Spin(Renderer r)
        {
            if (r == null) return Matrix4x4.identity;
            foreach (Tyre t in _tyres)
            {
                if (!t.Static || t.R == null || t.R.Pointer != r.Pointer) continue;
                Matrix4x4 home = Matrix4x4.TRS(_homePos, _homeRot, Vector3.one);
                return home * t.Motion * home.inverse;
            }
            return Matrix4x4.identity;
        }

        /// <summary>Tyres back to their own pose; the RV goes home or the scene unloads. The chassis is despawned.</summary>
        internal static void Release()
        {
            if (_loop != null) { MelonCoroutines.Stop(_loop); _loop = null; }
            int back = 0;
            foreach (Tyre t in _tyres)
            {
                if (t.Static || t.R == null) continue;
                try { t.R.transform.localPosition = t.LocalPos; t.R.transform.localRotation = t.LocalRot; back++; }
                catch (Exception e) { Core.Log.Warning("[Wheels] setting a tyre back failed: " + e.Message); }
            }
            if (_tyres.Count > 0) Core.LogDebug("[Wheels] released, " + back + " tyre transform(s) set back.");
            _tyres.Clear();
            _axles.Clear();
            _chassis = null;
        }

        // The scene is going away with every tyre in it; only the bookkeeping is cleared.
        internal static void Reset()
        {
            if (_loop != null) { MelonCoroutines.Stop(_loop); _loop = null; }
            _tyres.Clear();
            _axles.Clear();
            _chassis = null;
        }

        private static IEnumerator Loop()
        {
            int failures = 0;
            while (_tyres.Count > 0)
            {
                try { Step(Time.deltaTime); }
                catch (Exception e)
                {
                    if (failures++ == 0) Core.Log.Error("[Wheels] moving the tyres failed: " + e);
                }
                yield return null;
            }
            _loop = null;
        }

        private static void Step(float dt)
        {
            if (_chassis == null || dt <= 0f) return;
            Transform ct = _chassis.transform;

            // Fallback roll from the distance covered: on a client the chassis is moved by its network transform,
            // and its wheel colliders report a wheel at rest.
            Vector3 pos = ct.position;
            float forward = 0f;
            if (_hasLast)
            {
                Vector3 moved = pos - _last;
                if (moved.sqrMagnitude < 100f) forward = Vector3.Dot(moved, ct.forward);   // more is a teleport
            }
            _last = pos;
            _hasLast = true;
            bool simulated = _chassis.LocalPlayerIsDriver;

            Quaternion invChassis = Quaternion.Inverse(ct.rotation);
            foreach (Axle a in _axles)
            {
                if (a.W == null) continue;
                Matrix4x4 motion;
                if (simulated && a.W.enabled)
                {
                    a.W.GetWorldPose(out Vector3 wp, out Quaternion wr);
                    Vector3 travel = ct.InverseTransformPoint(wp) - a.Rest;
                    Quaternion turn = invChassis * wr;
                    motion = Matrix4x4.Translate(a.Rest + travel) * Matrix4x4.Rotate(turn) * Matrix4x4.Translate(-a.Rest);
                }
                else
                {
                    a.Roll = Mathf.Repeat(a.Roll + forward / a.Radius * Mathf.Rad2Deg, 360f);
                    Quaternion turn = Quaternion.AngleAxis(a.W.steerAngle, Vector3.up) * Quaternion.AngleAxis(a.Roll, Vector3.right);
                    motion = Matrix4x4.Translate(a.Rest) * Matrix4x4.Rotate(turn) * Matrix4x4.Translate(-a.Rest);
                }
                foreach (Tyre t in _tyres)
                {
                    if (t.Axle != a) continue;
                    // A twin tyre turns about its own centre, moved like the wheel under the pair.
                    Vector3 shift = t.Centre - a.Rest;
                    t.Motion = Matrix4x4.Translate(shift) * motion * Matrix4x4.Translate(-shift);
                    if (t.Static || t.R == null) continue;
                    Vector3 p = t.Motion.MultiplyPoint3x4(t.RestPos);
                    Quaternion q = t.Motion.rotation * t.RestRot;
                    t.R.transform.SetPositionAndRotation(ct.TransformPoint(p), ct.rotation * q);
                }
            }
        }
    }
}
