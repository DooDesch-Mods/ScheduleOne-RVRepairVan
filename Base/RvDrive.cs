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
        internal static void Look(bool off, bool rear = false)
        {
            var pc = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerCamera>.Instance;
            if (pc == null) { Core.Log.Warning("[Drive] look: no PlayerCamera."); return; }
            if (off) { pc.StopTransformOverride(0f, true, true); Core.Log.Msg("[Drive] look: camera back on the player."); return; }
            if (_chassis == null) { Core.Log.Warning("[Drive] look: no chassis out."); return; }
            if (_chassis.LocalPlayerIsInVehicle) { Core.Log.Warning("[Drive] look: leave the driver seat first, the vehicle camera wins."); return; }
            Vector3 at = _chassis.transform.position + Vector3.up * 1.5f;
            Vector3 eye = rear ? at - _chassis.transform.forward * 13f + Vector3.up * 1.5f
                : at + _chassis.transform.right * 12f + Vector3.up * 9f;
            pc.OverrideTransform(eye, Quaternion.LookRotation(at - eye), 0f, false);
            Core.Log.Msg("[Drive] look: eye=" + eye + " at " + at);
        }

        private static object _rec;

        /// <summary>rvdrive rec [seconds] [fps]: write every frame to &lt;game&gt;/rvrec as PNG. Game time runs at a fixed step,
        /// so the clip plays smoothly however long each frame takes to save.</summary>
        internal static void Rec(string[] args)
        {
            if (_rec != null) { Core.Log.Warning("[Drive] rec: already recording."); return; }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            float seconds = args.Length > 0 && float.TryParse(args[0], System.Globalization.NumberStyles.Float, inv, out float a) ? a : 6f;
            int fps = args.Length > 1 && int.TryParse(args[1], out int f) ? f : 30;
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "rvrec"));
            try
            {
                if (System.IO.Directory.Exists(dir))
                    foreach (string old in System.IO.Directory.GetFiles(dir, "f_*.png")) System.IO.File.Delete(old);
                System.IO.Directory.CreateDirectory(dir);
            }
            catch (Exception e) { Core.Log.Error("[Drive] rec: cannot prepare " + dir + ": " + e.Message); return; }
            _rec = MelonCoroutines.Start(Record(dir, Mathf.Max(1, Mathf.RoundToInt(seconds * fps)), fps));
        }

        private static IEnumerator Record(string dir, int frames, int fps)
        {
            Core.Log.Msg("[Drive] rec: " + frames + " frame(s) at " + fps + " fps to " + dir);
            Time.captureFramerate = fps;
            int written = 0;
            for (int i = 0; i < frames; i++)
            {
                try { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "f_" + i.ToString("0000") + ".png")); written++; }
                catch (Exception e) { Core.Log.Error("[Drive] rec: frame " + i + " failed: " + e.Message); break; }
                yield return null;
            }
            yield return null;
            Time.captureFramerate = 0;
            _rec = null;
            Core.Log.Msg("[Drive] rec: done, " + written + " frame(s).");
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

        /// <summary>rvdrive stat: one line of driving telemetry - position, speed, roll and pitch, wheels on the ground.</summary>
        internal static void Stat(string tag)
        {
            if (_chassis == null) { Core.Log.Warning("[Drive] stat: no chassis out."); return; }
            Transform t = _chassis.transform;
            float roll = Vector3.SignedAngle(Vector3.ProjectOnPlane(Vector3.up, t.forward), t.up, t.forward);
            float pitch = Vector3.SignedAngle(Vector3.ProjectOnPlane(Vector3.up, t.right), t.up, t.right);
            int grounded = 0, wheels = 0;
            foreach (WheelCollider w in _chassis.GetComponentsInChildren<WheelCollider>(false)) { wheels++; if (w.isGrounded) grounded++; }
            Core.Log.Msg("[Drive] stat " + tag + " pos=" + t.position.ToString("F1") + " speed=" + _chassis.Speed_Kmh.ToString("0.0")
                + " km/h roll=" + roll.ToString("0.0") + " pitch=" + pitch.ToString("0.0") + " wheels=" + grounded + "/" + wheels
                + " throttle=" + _chassis.currentThrottle.ToString("0.00")
                + (_chassis.Rb != null ? " com=" + _chassis.Rb.centerOfMass.ToString("F2") + " inertia=" + _chassis.Rb.inertiaTensor.ToString("F0") : ""));
        }

        private static Il2CppScheduleOne.NPCs.NPC _target;

        private static Vector3 _pinAt;

        // Holds the NPC on its spot until something knocks it over or 20 s pass, so it does not walk off the road.
        private static IEnumerator Pin(Il2CppScheduleOne.NPCs.NPC npc)
        {
            float until = Time.time + 20f;
            while (npc != null && Time.time < until && !npc.Avatar.Ragdolled && _target == npc)
            {
                npc.Movement.Warp(_pinAt);
                yield return null;
            }
            Core.Log.Msg("[Drive] npc pin released (" + (npc != null && npc.Avatar.Ragdolled ? "knocked over" : "timeout") + ").");
        }

        /// <summary>rvdrive npc [metres] puts the nearest free NPC that far ahead of the chassis; rvdrive npc stat reports on it.</summary>
        internal static void Npc(string arg)
        {
            if (_chassis == null) { Core.Log.Warning("[Drive] npc: no chassis out."); return; }
            if (arg == "stat")
            {
                if (_target == null) { Core.Log.Warning("[Drive] npc stat: no NPC placed - 'rvdrive npc' first."); return; }
                CapsuleCollider cap = _target.Movement._capsuleCollider;
                BoxCollider box = RvMobile.HitBox;
                string capInfo = cap == null ? "no capsule" : "capsule active=" + cap.gameObject.activeInHierarchy + " enabled=" + cap.enabled
                    + " trigger=" + cap.isTrigger + " layer=" + LayerMask.LayerToName(cap.gameObject.layer)
                    + " rb=" + (cap.attachedRigidbody != null ? (cap.attachedRigidbody.isKinematic ? "kinematic" : "dynamic") : "none")
                    + " ignoresIR=" + Physics.GetIgnoreLayerCollision(cap.gameObject.layer, LayerMask.NameToLayer("Ignore Raycast"));
                string boxInfo = box == null ? "no box" : "box enabled=" + box.enabled + " at " + box.bounds.center.ToString("F1")
                    + " size " + box.bounds.size.ToString("F1") + " holdsNpc=" + box.bounds.Contains(_target.transform.position + Vector3.up);
                Core.Log.Msg("[Drive] npc " + _target.ID + " at " + _target.transform.position.ToString("F1") + " ragdolled=" + _target.Avatar.Ragdolled
                    + " sinceHit=" + _target.Movement.TimeSinceHitByCar.ToString("0.0") + " s | " + capInfo + " | " + boxInfo);
                return;
            }
            float ahead = float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float m) ? m : 20f;
            var reg = Il2CppScheduleOne.NPCs.NPCManager.NPCRegistry;
            if (reg == null) { Core.Log.Warning("[Drive] npc: no NPC registry."); return; }
            Il2CppScheduleOne.NPCs.NPC best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < reg.Count; i++)
            {
                var n = reg[i];
                if (n == null || !n.isActiveAndEnabled || !n.isVisible || n.CurrentVehicle != null || !n.IsConscious) continue;
                float d = Vector3.Distance(n.transform.position, _chassis.transform.position);
                if (d < bestD) { bestD = d; best = n; }
            }
            if (best == null) { Core.Log.Warning("[Drive] npc: no free NPC found."); return; }
            Vector3 at = _chassis.transform.position + _chassis.transform.forward * ahead;
            best.Movement.Stop();
            best.Movement.Warp(at);
            _target = best;
            _pinAt = at;
            MelonCoroutines.Start(Pin(best));
            Core.Log.Msg("[Drive] npc " + best.ID + " placed " + ahead.ToString("0") + " m ahead at " + at.ToString("F1") + " (was " + bestD.ToString("0") + " m away).");
        }

        /// <summary>rvdrive tp [x z heading]: set the parked chassis down 10 m ahead of the player, or at x/z facing a compass heading (0 = +z).</summary>
        internal static void Tp(string[] args)
        {
            if (_chassis == null) { Core.Log.Warning("[Drive] tp: no chassis out - 'rvdrive' first, then 'rvdrive exit'."); return; }
            if (_chassis.LocalPlayerIsInVehicle) { Core.Log.Warning("[Drive] tp: leave the driver seat first."); return; }
            var player = Il2CppScheduleOne.PlayerScripts.Player.Local;
            if (player == null) { Core.Log.Warning("[Drive] tp: no local player."); return; }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var num = System.Globalization.NumberStyles.Float;
            Vector3 fwd, at;
            if (args.Length >= 3 && float.TryParse(args[0], num, inv, out float x) && float.TryParse(args[1], num, inv, out float z)
                && float.TryParse(args[2], num, inv, out float heading))
            {
                fwd = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
                at = new Vector3(x, 0f, z);
            }
            else
            {
                fwd = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
                at = player.transform.position + fwd * 10f;
            }
            float ground = float.NegativeInfinity;
            string groundName = "-";
            foreach (RaycastHit h in Physics.RaycastAll(at + Vector3.up * 60f, Vector3.down, 120f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(_chassis.transform)) continue;
                string layer = LayerMask.LayerToName(h.collider.gameObject.layer);
                if (layer == "Player" || layer == "TransparentFX") continue;
                if (h.point.y > ground) { ground = h.point.y; groundName = Path(h.collider.transform) + " layer=" + layer; }
            }
            if (float.IsNegativeInfinity(ground)) { Core.Log.Warning("[Drive] tp: no ground under " + at + "."); return; }
            Core.Log.Msg("[Drive] tp: ground " + groundName);
            at.y = ground + 1f;
            Rigidbody rb = _chassis.Rb;
            Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
            _chassis.transform.SetPositionAndRotation(at, rot);
            if (rb != null) { rb.position = at; rb.rotation = rot; rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            Physics.SyncTransforms();
            Core.Log.Msg("[Drive] tp: chassis set down at " + at.ToString("F1") + " facing " + fwd.ToString("F2") + ".");
        }

        /// <summary>rvdrive lights: toggle the headlights like the vehicle's light key does.</summary>
        internal static void Lights()
        {
            if (_chassis == null) { Core.Log.Warning("[Drive] lights: no chassis out."); return; }
            VehicleLights vl = _chassis.GetComponent<VehicleLights>();
            if (vl == null) { Core.Log.Warning("[Drive] lights: the chassis has no VehicleLights."); return; }
            vl.HeadlightsOn = !vl.HeadlightsOn;
            Core.Log.Msg("[Drive] headlights " + (vl.HeadlightsOn ? "on" : "off") + ".");
        }

        /// <summary>rvdrive hull on|off: switch the driving hull box, and list what it touches.</summary>
        internal static void HullToggle(string arg)
        {
            BoxCollider hull = RvMobile.Hull;
            if (hull == null) { Core.Log.Warning("[Drive] hull: none built."); return; }
            if (arg == "on" || arg == "off") hull.enabled = arg == "on";
            Transform t = hull.transform;
            Vector3 half = Vector3.Scale(hull.size * 0.5f, t.lossyScale) + Vector3.one * 0.3f;   // contacts sit on the surface, not inside
            int n = 0;
            foreach (Collider c in Physics.OverlapBox(t.TransformPoint(hull.center), half, t.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (_chassis != null && c.transform.IsChildOf(_chassis.transform)) continue;
                Core.Log.Msg("[Drive]   hull touches " + Path(c.transform) + " layer=" + LayerMask.LayerToName(c.gameObject.layer));
                n++;
            }
            int own = 0;
            if (_chassis != null)
                foreach (Collider c in _chassis.GetComponentsInChildren<Collider>(false))
                    if (c.enabled && !c.isTrigger && c.TryCast<WheelCollider>() == null)
                    {
                        own++;
                        Core.Log.Msg("[Drive]   chassis collider " + Path(c.transform) + " " + c.GetIl2CppType().Name + " layer="
                            + LayerMask.LayerToName(c.gameObject.layer) + " bounds " + c.bounds.min.ToString("F2") + ".." + c.bounds.max.ToString("F2"));
                    }
            Core.Log.Msg("[Drive] hull enabled=" + hull.enabled + " layer=" + LayerMask.LayerToName(hull.gameObject.layer) + ", " + n
                + " foreign collider(s) inside, " + own + " solid chassis collider(s).");
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
                foreach (float h in new[] { 0.5f, 1.5f, 3f, 4.3f })
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
