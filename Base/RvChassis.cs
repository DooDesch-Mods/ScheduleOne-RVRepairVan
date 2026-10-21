using System;
using System.Collections.Generic;
using Chassis.Api;
using Il2CppScheduleOne.Vehicles;

namespace RVRepairVan.Base
{
    /// <summary>
    /// The RV's Chassis profile: the driver sits in the left cab chair with the hands on the turning steering wheel,
    /// the passenger in the right one, first person looks through their eyes, and Chassis runs the horn, the
    /// indicators and the automatic headlights on every peer.
    ///
    /// The chassis is a stock "veeper", so the profile is assigned to that one vehicle (<see cref="ChassisApi.Assign"/>)
    /// and does not follow the vehicle code to every veeper in town. The seats come from the RV's own cab chairs; the
    /// veeper's seats sit somewhere inside the RV's floor.
    ///
    /// The steering wheel gets a pivot at its centre, turned like the model, so Chassis can turn it about its column.
    /// The wheel is static-batched like most of the cab: moving its transform moves nothing, so the RV's static redraw
    /// asks <see cref="Offset"/> for the turn of each wheel renderer.
    /// </summary>
    internal static class RvChassis
    {
        internal const string Code = "rvrepairvan_rv";
        private const string Frame = "RV";
        internal const string PivotName = "RVRepairVan_SteeringPivot";

        /// <summary>A cab chair's cushion sits this far above the bottom of its bounds.</summary>
        private const float CushionAboveBase = 0.45f;
        /// <summary>The cushion's middle is this far ahead of the chair's bounds centre, which the backrest pulls back.</summary>
        private const float CushionForward = 0.08f;
        private const float HipHeight = 0.08f;
        /// <summary>
        /// The fitted eyes sit at the neck in the RV's high cab chairs, so first person looked into the rider's own
        /// forearms. Up and forward by this much the view clears the head and keeps the hands on the wheel in sight.
        /// </summary>
        private const float EyeLift = 0.045f, EyeForward = 0.05f;
        /// <summary>
        /// The RV wheel is 0.55 m across, so the fitted grip 0.17 m beside the hub put the hands on the spokes. The rim
        /// runs this far from the hub, and the hands hold it this many degrees above level (ten to two).
        /// </summary>
        private const float RimRadius = 0.25f, GripAngle = 30f;

        private static Transform _pivot;
        private static Quaternion _rest = Quaternion.identity;
        private static Vector3 _column = Vector3.forward;
        private static readonly HashSet<IntPtr> _wheelRenderers = new HashSet<IntPtr>();

        // ---- steering wheel ------------------------------------------------------------------------------------------

        /// <summary>
        /// Measures the cab at home, before the model is carried onto the chassis: static-batched renderers keep their
        /// home bounds wherever the model goes, and the static redraw turns the wheel against its pose at home.
        /// </summary>
        internal static void Prepare(Transform model)
        {
            PrepareSteering(model);
            MeasureChairs(model);
            CaptureHome();
        }

        /// <summary>Finds the steering wheel and hangs it under a pivot at its centre.</summary>
        private static void PrepareSteering(Transform model)
        {
            _pivot = null;
            _wheelRenderers.Clear();
            if (model == null) { Core.Log.Warning("[Chassis] no RV model - the steering wheel stays still."); return; }
            try
            {
                Transform pivot = Find(model, PivotName);
                if (pivot == null)
                {
                    Transform wheel = FindWheel(model);
                    if (wheel == null) { Core.Log.Warning("[Chassis] no steering wheel in the cab - the driver's hands rest on the lap."); return; }
                    Renderer wr = wheel.GetComponentInChildren<Renderer>(true);
                    if (wr == null) { Core.Log.Warning("[Chassis] steering wheel '" + wheel.name + "' has no renderer - it stays still."); return; }
                    // A static-batched renderer reports the combined mesh, so the wheel is measured by its world bounds at
                    // home: the centre is the middle of the ring, the column the wheel transform's own axis nearest the
                    // thinnest side of those bounds.
                    Bounds wb = wr.bounds;
                    Vector3 axisWorld = Column(wr.transform, wb.size);
                    pivot = new GameObject(PivotName).transform;
                    pivot.SetParent(wheel.parent, false);
                    pivot.position = wb.center;
                    pivot.rotation = model.rotation;
                    wheel.SetParent(pivot, true);
                    _column = pivot.InverseTransformDirection(axisWorld);
                    Core.Log.Msg("[Chassis] steering wheel '" + wheel.name + "' hung on a pivot at model-local " + model.InverseTransformPoint(pivot.position).ToString("F2")
                        + ", column " + _column.ToString("F2") + " (wheel bounds " + wb.size.ToString("F2") + " m).");
                }
                else
                {
                    // A scene reset forgets the reference, not the pivot; its column is the thinnest direction again.
                    Renderer wr = pivot.GetComponentInChildren<Renderer>(true);
                    if (wr != null) _column = pivot.InverseTransformDirection(Column(wr.transform, wr.bounds.size)).normalized;
                    Core.Log.Msg("[Chassis] steering pivot already on the model - reusing it, column " + _column.ToString("F2") + ".");
                }

                _pivot = pivot;
                // At rest the pivot is turned like the model; Chassis turns it from there.
                _rest = Quaternion.Inverse(pivot.parent.rotation) * model.rotation;
                int batched = 0;
                foreach (Renderer r in pivot.GetComponentsInChildren<Renderer>(true))
                    if (r.isPartOfStaticBatch) { _wheelRenderers.Add(r.Pointer); batched++; }
                Core.Log.Msg("[Chassis] steering wheel: " + batched + " static-batched renderer(s) turned in the redraw.");
            }
            catch (Exception e) { Core.Log.Error("[Chassis] preparing the steering wheel failed - it stays still: " + e); _pivot = null; }
        }

        /// <summary>The axis of <paramref name="t"/> that lies closest to the thinnest side of world bounds of this size.</summary>
        private static Vector3 Column(Transform t, Vector3 size)
        {
            Vector3 thin = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
            Vector3 best = t.forward;
            foreach (Vector3 axis in new[] { t.right, t.up, t.forward })
                if (Mathf.Abs(Vector3.Dot(axis, thin)) > Mathf.Abs(Vector3.Dot(best, thin))) best = axis;
            return best.normalized;
        }

        private static Transform FindWheel(Transform model)
        {
            Transform found = null;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("RVRepairVan_", StringComparison.Ordinal)) continue;
                if (t.name == "SteeringWheel") return t;
                if (found == null && t.name.IndexOf("Steering", StringComparison.OrdinalIgnoreCase) >= 0) found = t;
            }
            return found;
        }

        private static Matrix4x4 _homePivot, _homePivotInverse;

        /// <summary>The wheel's pose at home, at rest; the static redraw turns about it.</summary>
        private static void CaptureHome()
        {
            if (_pivot == null) return;
            _homePivot = _pivot.parent.localToWorldMatrix * Matrix4x4.TRS(_pivot.localPosition, _rest, _pivot.localScale);
            _homePivotInverse = _homePivot.inverse;
        }

        /// <summary>Turn of a static-batched wheel renderer in home space, identity for every other renderer.</summary>
        internal static Matrix4x4 Offset(Renderer r)
        {
            if (_pivot == null || _wheelRenderers.Count == 0 || r == null || !_wheelRenderers.Contains(r.Pointer)) return Matrix4x4.identity;
            Quaternion turn = Quaternion.Inverse(_rest) * _pivot.localRotation;
            return _homePivot * Matrix4x4.Rotate(turn) * _homePivotInverse;
        }

        // ---- cab chairs ------------------------------------------------------------------------------------------------

        private static bool _chairs;
        private static Vector3 _driverSeat, _passengerSeat;   // model space, on the cushion

        private static void MeasureChairs(Transform model)
        {
            _chairs = false;
            if (model == null) return;
            Transform interior = model.Find("rv/Main/Interior");
            Transform a = interior != null ? interior.Find("Chair") : null, b = interior != null ? interior.Find("Chair (1)") : null;
            if (a == null || b == null) { Core.Log.Warning("[Chassis] cab chairs not found under rv/Main/Interior - the RV keeps vanilla seats."); return; }
            Vector3 wheelAt = _pivot != null ? _pivot.position : model.TransformPoint(new Vector3(-4.74f, 1.92f, -0.84f));
            Bounds ba = ChairBounds(a), bb = ChairBounds(b);
            bool aDrives = Vector3.Distance(ba.center, wheelAt) <= Vector3.Distance(bb.center, wheelAt);
            _driverSeat = model.InverseTransformPoint(Cushion(aDrives ? ba : bb));
            _passengerSeat = model.InverseTransformPoint(Cushion(aDrives ? bb : ba));
            _chairs = true;
            Core.Log.Msg("[Chassis] cab chairs: driver '" + (aDrives ? a : b).name + "' cushion at model-local " + _driverSeat.ToString("F2")
                + ", passenger at " + _passengerSeat.ToString("F2") + ".");
        }

        private static Vector3 Cushion(Bounds chair) => new Vector3(chair.center.x, chair.min.y + CushionAboveBase, chair.center.z);

        // ---- profile ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// Registers the profile and gives it to this chassis. Every peer, after the model sits on the chassis, so the
        /// chairs and the chassis' own directions agree.
        /// </summary>
        internal static void Attach(LandVehicle chassis, Transform model)
        {
            if (chassis == null || model == null) { Core.Log.Warning("[Chassis] attach: no chassis or no model - the RV keeps vanilla seats."); return; }
            try
            {
                if (!_chairs) { Core.Log.Warning("[Chassis] the cab chairs were not measured - the RV keeps vanilla seats."); return; }
                Vector3 wheelAt = _pivot != null ? _pivot.position : model.TransformPoint(new Vector3(-4.74f, 1.92f, -0.84f));
                Transform root = chassis.transform;
                var profile = new ChassisProfile
                {
                    Code = Code,
                    Frame = Frame,
                    Steering = _pivot != null ? new SteeringProfile { Path = PivotName, Axis = _column, Ratio = 8f, Max = 300f } : null,
                    // No lamps of its own: the RV's rear lenses blink through RvLights; the empty set still takes the keys.
                    Lamps = new LampProfile(),
                    Horn = new HornProfile { Position = root.InverseTransformPoint(wheelAt + root.forward * 1.2f), Volume = 0.9f },
                    AutoLights = true,
                };

                bool passengerTaken = false;
                var seats = chassis.Seats;
                for (int i = 0; seats != null && i < seats.Length; i++)
                {
                    if (seats[i] == null) continue;
                    bool driver = seats[i].isDriverSeat;
                    if (!driver && passengerTaken) { Core.Log.Msg("[Chassis] chassis seat " + i + " has no chair in the cab - it stays a vanilla seat."); continue; }
                    if (!driver) passengerTaken = true;
                    Vector3 point = model.TransformPoint(driver ? _driverSeat : _passengerSeat) + root.forward * CushionForward;
                    SeatProfile seat = ChassisApi.FitSeat(chassis, i, point, model, driver ? _pivot : null, HipHeight);
                    seat.Eyes += Vector3.up * EyeLift + Quaternion.Euler(0f, seat.Yaw, 0f) * Vector3.forward * EyeForward;
                    if (driver && _pivot != null)
                    {
                        seat.Hands.GripStart = model.InverseTransformPoint(RimGrip(root));
                        Core.Log.Msg("[Chassis] driver's hands on the rim at model-local " + seat.Hands.GripStart.ToString("F3") + ".");
                    }
                    profile.Seats.Add(seat);
                }

                bool used = ChassisApi.Register(profile);
                ChassisApi.Assign(chassis, Code);
                Core.Log.Msg("[Chassis] RV profile " + (used ? "registered" : "overridden by a JSON file") + ": " + profile.Seats.Count + " seat(s), steering "
                    + (profile.Steering != null ? "on" : "off") + ".");
            }
            catch (Exception e) { Core.Log.Error("[Chassis] registering the RV profile failed - the RV keeps vanilla seats: " + e); }
        }

        /// <summary>Right hand on the rim, in world space: out to the right and up the wheel's face towards the windscreen.</summary>
        private static Vector3 RimGrip(Transform root)
        {
            Vector3 column = _pivot.TransformDirection(_column).normalized;
            Vector3 right = Vector3.ProjectOnPlane(root.right, column).normalized;
            Vector3 top = Vector3.ProjectOnPlane(root.forward, column).normalized;
            float a = GripAngle * Mathf.Deg2Rad;
            return _pivot.position + (right * Mathf.Cos(a) + top * Mathf.Sin(a)) * RimRadius;
        }

        /// <summary>The chassis goes back to being a plain veeper.</summary>
        internal static void Detach(LandVehicle chassis)
        {
            if (chassis == null) return;
            try { ChassisApi.Unassign(chassis); }
            catch (Exception e) { Core.Log.Warning("[Chassis] unassigning the RV profile failed: " + e.Message); }
        }

        internal static void Reset()
        {
            _pivot = null;
            _chairs = false;
            _wheelRenderers.Clear();
        }

        private static Bounds ChairBounds(Transform chair)
        {
            bool any = false;
            Bounds b = new Bounds(chair.position, Vector3.zero);
            foreach (Renderer r in chair.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!any) Core.Log.Warning("[Chassis] chair '" + chair.name + "' has no renderer - its seat is fitted at its origin.");
            return b;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
