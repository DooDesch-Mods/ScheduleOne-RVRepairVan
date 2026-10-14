#if DEBUG && IL2CPP
using System;
using System.Collections;
using MelonLoader;
using Il2CppFishNet;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Vehicles;

namespace RVRepairVan.Base
{
    /// <summary>Console helpers for the drivable RV (<see cref="RvMobile"/>): state dump, a screenshot camera, keyless driving.</summary>
    internal static class RvDrive
    {
        private static LandVehicle _chassis => RvMobile.Chassis;

        /// <summary>rvdrive exit: leave the driver seat like pressing the interact key.</summary>
        internal static void Exit()
        {
            if (_chassis == null) { Core.Log.Warning("[Drive] exit: no chassis out."); return; }
            if (!_chassis.LocalPlayerIsInVehicle) { Core.Log.Msg("[Drive] exit: not in the driver seat."); return; }
            _chassis.overrideControls = false;
            _chassis.ExitVehicle();
            Core.Log.Msg("[Drive] exit requested.");
        }

        /// <summary>rvdrive look [off]: a fixed camera above and beside the parked chassis, for screenshots.</summary>
        internal static void Look(bool off)
        {
            var pc = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerCamera>.Instance;
            if (pc == null) { Core.Log.Warning("[Drive] look: no PlayerCamera."); return; }
            if (off) { pc.StopTransformOverride(0f, true, true); Core.Log.Msg("[Drive] look: camera back on the player."); return; }
            if (_chassis == null) { Core.Log.Warning("[Drive] look: no chassis out."); return; }
            if (_chassis.LocalPlayerIsInVehicle) { Core.Log.Warning("[Drive] look: leave the driver seat first, the vehicle camera wins."); return; }
            Vector3 at = _chassis.transform.position + Vector3.up * 1.5f;
            Vector3 eye = at + _chassis.transform.right * 12f + Vector3.up * 9f;
            pc.OverrideTransform(eye, Quaternion.LookRotation(at - eye), 0f, false);
            Core.Log.Msg("[Drive] look: eye=" + eye + " at " + at);
        }

        /// <summary>rvdrive gas &lt;throttle&gt; [steer] | rvdrive gas off: drive without a keyboard, for console tests.</summary>
        internal static void Gas(string throttle, string steer)
        {
            if (_chassis == null) { Core.Log.Warning("[Drive] no chassis - 'rvdrive' first."); return; }
            if (throttle == "off")
            {
                _chassis.overrideControls = false;
                Core.Log.Msg("[Drive] controls back to the player.");
                return;
            }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            float th = float.TryParse(throttle, System.Globalization.NumberStyles.Float, inv, out float a) ? a : 0f;
            float st = float.TryParse(steer, System.Globalization.NumberStyles.Float, inv, out float s) ? s : 0f;
            _chassis.overrideControls = true;
            _chassis.throttleOverride = th;
            _chassis.steerOverride = st;
            if (_chassis.Rb != null) _chassis.Rb.WakeUp();
            Core.Log.Msg("[Drive] override throttle=" + th + " steer=" + st + " speed=" + _chassis.Speed_Kmh.ToString("0.0") + " km/h");
        }

        // Where the camera really is, which wheels touch the ground, and what sits in the chassis' way.
        private static void Diagnose()
        {
            try
            {
                Camera main = Camera.main;
                VehicleCamera vc = _chassis.GetComponentInChildren<VehicleCamera>(true);
                var pc = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerCamera>.Instance;
                Core.Log.Msg("[Drive]   camera main=" + (main != null ? main.name + " " + main.transform.position : "none")
                    + " playerCam=" + (pc != null ? pc.name + " " + pc.transform.position + " overridden=" + pc.transformOverriden
                        + " parent=" + (pc.transform.parent != null ? pc.transform.parent.name : "none") : "none")
                    + " vehicleCam=" + (vc != null ? "enabled=" + vc.enabled + " active=" + vc.gameObject.activeInHierarchy
                        + " target=" + (vc.targetTransform != null ? vc.targetTransform.position.ToString() : "none")
                        + " sinceAdjust=" + vc.timeSinceCameraManuallyAdjusted.ToString("0.00") : "none"));

                Rigidbody rb = _chassis.Rb;
                if (rb != null)
                    Core.Log.Msg("[Drive]   rb velocity=" + rb.velocity + " kinematic=" + rb.isKinematic + " sleeping=" + rb.IsSleeping()
                        + " handbrake=" + _chassis.HandbrakeApplied);

                // GetGroundHit's out struct takes the IL2CPP process down; isGrounded is enough here.
                foreach (WheelCollider w in _chassis.GetComponentsInChildren<WheelCollider>(false))
                    Core.Log.Msg("[Drive]   wheel " + w.name + " grounded=" + w.isGrounded + " torque=" + w.motorTorque.ToString("0")
                        + " brake=" + w.brakeTorque.ToString("0") + " rpm=" + w.rpm.ToString("0"));

                var hitSeen = new System.Collections.Generic.HashSet<string>();
                foreach (Collider c in Physics.OverlapBox(_chassis.transform.position + Vector3.up * 2f, new Vector3(2.8f, 1.6f, 7f),
                    _chassis.transform.rotation, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (c.transform.IsChildOf(_chassis.transform)) continue;
                    string layer = LayerMask.LayerToName(c.gameObject.layer);
                    if (layer == "Player" || layer == "Terrain") continue;
                    string p = Path(c.transform);
                    if (hitSeen.Add(p)) Core.Log.Msg("[Drive]   overlaps RV: " + p + " layer=" + layer + " at " + c.bounds.center);
                }

                Vector3 fwd = _chassis.transform.forward;
                foreach (float h in new[] { 0.5f, 1.5f, 3f })
                {
                    Vector3 from = _chassis.transform.position + Vector3.up * h - fwd * 1f;
                    RaycastHit[] hits = Physics.RaycastAll(from, fwd, 12f, ~0, QueryTriggerInteraction.Ignore);
                    foreach (RaycastHit r in hits)
                    {
                        if (r.collider.transform.IsChildOf(_chassis.transform) || LayerMask.LayerToName(r.collider.gameObject.layer) == "Player") continue;
                        Core.Log.Msg("[Drive]   ahead h=" + h + " " + r.distance.ToString("0.0") + " m: " + Path(r.collider.transform)
                            + " layer=" + LayerMask.LayerToName(r.collider.gameObject.layer));
                    }
                }
            }
            catch (Exception e) { Core.Log.Warning("[Drive] diagnose failed: " + e.Message); }
        }

        // Static-batched renderers draw from a combined world-space mesh, so moving their transform moves nothing.
        private static void StaticReport(string label, Transform t)
        {
            int all = 0, stat = 0, listed = 0;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            {
                all++;
                if (!r.isPartOfStaticBatch) continue;
                stat++;
                if (listed++ < 12) Core.Log.Msg("[Drive]   " + label + " static: " + Path(r.transform) + " active=" + r.gameObject.activeInHierarchy);
            }
            Core.Log.Msg("[Drive]   " + label + ": " + stat + " of " + all + " renderer(s) static-batched.");
        }

        private static void DumpChildren(string label, Transform t)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                int rend = 0, col = 0;
                foreach (Renderer r in c.GetComponentsInChildren<Renderer>(false)) if (!r.forceRenderingOff) rend++;
                foreach (Collider k in c.GetComponentsInChildren<Collider>(false)) if (k.enabled && !k.isTrigger) col++;
                Core.Log.Msg("[Drive]   " + label + "/" + c.name + " active=" + c.gameObject.activeSelf + " renderers=" + rend + " colliders=" + col
                    + " pos=" + c.position);
            }
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        /// <summary>rvdrive probe: the cabin's inner surfaces in rv/Main space, from rays out of the middle.</summary>
        internal static void Probe()
        {
            Transform model = Managers.RVManager.Model;
            Transform main = model != null ? model.Find("rv/Main") : null;
            if (main == null) { Core.Log.Warning("[Drive] probe: rv/Main not found."); return; }
            void Ray(Vector3 from, Vector3 dir)
            {
                Vector3 o = main.TransformPoint(from), d = main.TransformDirection(dir);
                if (Physics.Raycast(o, d, out RaycastHit h, 6f, ~0, QueryTriggerInteraction.Ignore))
                {
                    Renderer r = h.collider.GetComponent<Renderer>();
                    Core.Log.Msg("[Drive]   ray " + from + " " + dir + " -> " + main.InverseTransformPoint(h.point).ToString("F2")
                        + " " + Path(h.collider.transform) + " " + h.collider.GetIl2CppType().Name
                        + (r != null && r.sharedMaterial != null ? " mat=" + r.sharedMaterial.name : ""));
                }
                else Core.Log.Msg("[Drive]   ray " + from + " " + dir + " -> nothing within 6 m");
            }
            for (float z = -5f; z <= 5.6f; z += 1f) { Ray(new Vector3(0, 2f, z), Vector3.right); Ray(new Vector3(0, 2f, z), Vector3.left); }
            foreach (float x in new[] { -1.3f, 0f, 1.3f }) { Ray(new Vector3(x, 2f, 0), Vector3.forward); Ray(new Vector3(x, 2f, -2f), Vector3.back); }
            foreach (float z in new[] { -4.6f, -3f, 0f, 3f, 5.4f })
                foreach (float x in new[] { -1.45f, 0f, 1.45f }) Ray(new Vector3(x, 2.2f, z), Vector3.down);
            foreach (string n in new[] { "rv/Main", "rv/Main/Interior", "rv/Main/Trim", "rv/Main/Floor", "rv/Main/Interior/Dash" })
            {
                Transform t = model.Find(n);
                Renderer r = t != null ? t.GetComponent<Renderer>() : null;
                Material m = r != null ? r.sharedMaterial : null;
                if (m == null) { Core.Log.Msg("[Drive]   mat " + n + ": none"); continue; }
                MeshFilter mf = t.GetComponent<MeshFilter>();
                string col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : m.HasProperty("_Color") ? m.color.ToString() : "-";
                Core.Log.Msg("[Drive]   mat " + n + ": " + m.name + " shader=" + m.shader.name + " tex=" + (m.mainTexture != null ? m.mainTexture.name + " " + m.mainTexture.width + "x" + m.mainTexture.height : "none")
                    + " color=" + col + " readable=" + (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable) + " mesh=" + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-")
                    + " lossyScale=" + t.lossyScale + " rot=" + t.localEulerAngles);
            }
            foreach (Renderer r in model.Find("rv").GetComponentsInChildren<Renderer>(false))
            {
                if (r.name.IndexOf("Window", StringComparison.Ordinal) >= 0) continue;
                Bounds b = r.bounds;
                Core.Log.Msg("[Drive]   rend " + Path(r.transform) + " c=" + main.InverseTransformPoint(b.center).ToString("F2") + " size=" + b.size.ToString("F2")
                    + (r.sharedMaterial != null ? " mat=" + r.sharedMaterial.name : ""));
            }
        }

        internal static void Info()
        {
            Transform root = Managers.RVManager.Root;
            Core.Log.Msg("[Drive] " + RvMobile.DebugState() + (root != null ? " rootPos=" + root.position + " rootFwd=" + root.forward : " (no RV)"));
            if (_chassis != null) Diagnose();
            if (root != null)
            {
                DumpChildren("root", root);
                Transform model = root.Find("RV") ?? (_chassis != null ? _chassis.transform.Find("RV") : null);
                if (model != null)
                {
                    DumpChildren("model", model);
                    StaticReport("model", model);
                    foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                    {
                        string n = t.name;
                        if (n.IndexOf("Steer", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("Wheel", StringComparison.OrdinalIgnoreCase) < 0
                            && n.IndexOf("Seat", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("Door", StringComparison.OrdinalIgnoreCase) < 0
                            && n.IndexOf("Step", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        Renderer r = t.GetComponent<Renderer>();
                        Core.Log.Msg("[Drive]   part " + Path(t) + " active=" + t.gameObject.activeInHierarchy
                            + " local=" + model.InverseTransformPoint(t.position) + " static=" + (r != null && r.isPartOfStaticBatch)
                            + (r != null ? " size=" + r.bounds.size : ""));
                    }
                }
                Property prop = root.GetComponent<Property>();
                if (prop != null && prop.Grids != null)
                    foreach (var g in prop.Grids)
                        Core.Log.Msg("[Drive]   grid " + Path(g.transform) + " underModel=" + (model != null && g.transform.IsChildOf(model))
                            + " active=" + g.gameObject.activeInHierarchy);
            }
            Transform interior = root != null ? root.Find("RV/rv/Main/Interior") : null;
            if (interior != null)
                for (int i = 0; i < interior.childCount; i++)
                {
                    Transform c = interior.GetChild(i);
                    Core.Log.Msg("[Drive]   interior " + c.name + " local=" + root.InverseTransformPoint(c.position));
                }
        }
    }
}
#endif
