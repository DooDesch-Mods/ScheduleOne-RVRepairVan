#if DEBUG && IL2CPP
using System;
using System.Collections;
using MelonLoader;
using Il2CppFishNet;
using Il2CppScheduleOne.Vehicles;

namespace RVRepairVan.Base
{
    /// <summary>
    /// Proof of concept: drive the RV. Vanilla has no drivable RV - the property is scene-placed without
    /// physics, and the intro only animates a separate copy. So a stock vehicle is spawned as a hidden chassis,
    /// and the RV model plus the property's placed items are parented onto it. The property itself never
    /// moves: back within <see cref="HomeRadius"/> of the start, leaving the driver seat snaps everything home
    /// and despawns the chassis. Host only; clients see the chassis move but not the RV model.
    /// </summary>
    internal static class RvDrive
    {
        private const float HomeRadius = 8f;

        private static LandVehicle _chassis;
        private static Transform _model, _modelParent, _contents, _contentsParent;
        private static Vector3 _modelLocalPos, _contentsPos, _homeChassisPos;
        private static Quaternion _modelLocalRot, _contentsRot;
        private static object _watch;

        internal static bool Active => _chassis != null;

        /// <summary>rvdrive [code] [yaw]: spawn the chassis under the RV and take the driver seat, or re-enter it.</summary>
        internal static void Drive(string code, float yaw)
        {
            try
            {
                if (!InstanceFinder.IsServer) { Core.Log.Warning("[Drive] host only."); return; }
                if (_chassis != null) { Core.Log.Msg("[Drive] re-entering the chassis."); _chassis.EnterVehicle(); return; }

                Transform root = Managers.RVManager.Root;
                if (root == null) { Core.Log.Warning("[Drive] RV not found."); return; }
                if (Managers.RVManager.IsDestroyed()) { Core.Log.Warning("[Drive] the RV is wrecked - repair it first."); return; }
                RV rv = root.GetComponent<RV>();
                _model = root.Find("RV");
                if (_model == null) { Core.Log.Warning("[Drive] RV model child not found."); return; }
                _contents = rv != null && rv.Container != null ? rv.Container.transform : null;
                if (_contents == null) Core.Log.Warning("[Drive] no contents container - placed items stay home.");

                Bounds b = ModelBounds(_model);
                Vector3 pos = new Vector3(b.center.x, root.position.y + 0.3f, b.center.z);
                Quaternion rot = root.rotation * Quaternion.Euler(0f, yaw, 0f);
                Core.Log.Msg("[Drive] model bounds center=" + b.center + " size=" + b.size + " rootFwd=" + root.forward
                    + " -> chassis '" + code + "' at " + pos + " yaw+" + yaw);

                VehicleManager vm = NetworkSingleton<VehicleManager>.Instance;
                if (vm == null) { Core.Log.Warning("[Drive] no VehicleManager."); return; }
                _chassis = vm.SpawnAndReturnVehicle(code, pos, rot, true);
                if (_chassis == null) { Core.Log.Error("[Drive] spawn of '" + code + "' returned null."); return; }
                _homeChassisPos = pos;

                // forceRenderingOff, not enabled: LandVehicle.SetVisible re-activates its model when the player enters.
                int hidden = 0;
                foreach (Renderer r in _chassis.GetComponentsInChildren<Renderer>(true)) { r.forceRenderingOff = true; hidden++; }
                RaiseCamera(_chassis, -root.forward);

                _modelParent = _model.parent;
                _modelLocalPos = _model.localPosition;
                _modelLocalRot = _model.localRotation;
                _model.SetParent(_chassis.transform, true);
                if (_contents != null)
                {
                    _contentsParent = _contents.parent;
                    _contentsPos = _contents.position;
                    _contentsRot = _contents.rotation;
                    _contents.SetParent(_chassis.transform, true);
                }
                int baked = TakeStaticParts(_model);
                int muted = MuteColliders(_model) + (_contents != null ? MuteColliders(_contents) : 0);
                Rigidbody rb = _chassis.Rb;
                Core.Log.Msg("[Drive] chassis spawned, " + hidden + " renderer(s) hidden, model"
                    + (_contents != null ? " and " + _contents.childCount + " placed item(s)" : "") + " attached, "
                    + muted + " RV collider(s) off, " + baked + " static part(s) redrawn"
                    + (rb != null ? ", mass=" + rb.mass + " com=" + rb.centerOfMass + " topSpeed=" + _chassis.TopSpeed : ", no Rigidbody"));

                if (_watch != null) MelonCoroutines.Stop(_watch);
                _watch = MelonCoroutines.Start(Watch());
            }
            catch (Exception e)
            {
                Core.Log.Error("[Drive] start failed: " + e);
            }
        }

        /// <summary>rvdrive bare [code]: a stock vehicle beside the RV, nothing attached - the baseline for the drive tests.</summary>
        internal static void Bare(string code)
        {
            try
            {
                if (!InstanceFinder.IsServer) { Core.Log.Warning("[Drive] host only."); return; }
                if (_chassis != null) { Core.Log.Warning("[Drive] a chassis is out - 'rvdrive home' first."); return; }
                Transform root = Managers.RVManager.Root;
                if (root == null) { Core.Log.Warning("[Drive] RV not found."); return; }
                VehicleManager vm = NetworkSingleton<VehicleManager>.Instance;
                if (vm == null) { Core.Log.Warning("[Drive] no VehicleManager."); return; }
                Vector3 pos = root.position + root.right * 7f + Vector3.up * 0.3f;
                _model = null;
                _contents = null;
                _chassis = vm.SpawnAndReturnVehicle(code, pos, root.rotation * Quaternion.Euler(0f, 180f, 0f), true);
                if (_chassis == null) { Core.Log.Error("[Drive] spawn of '" + code + "' returned null."); return; }
                _homeChassisPos = pos;
                Core.Log.Msg("[Drive] bare '" + code + "' at " + pos + ", entering.");
                if (_watch != null) MelonCoroutines.Stop(_watch);
                _watch = MelonCoroutines.Start(Watch());
            }
            catch (Exception e)
            {
                Core.Log.Error("[Drive] bare spawn failed: " + e);
            }
        }

        /// <summary>Takes the driver seat, then polls it. Leaving it near home brings the RV back; elsewhere it stays parked.</summary>
        private static IEnumerator Watch()
        {
            // VehicleCamera subscribes to the enter event in its Start, a frame after the spawn. Entering
            // in the spawn frame leaves the camera on the player's head.
            yield return null;
            yield return null;
            if (_chassis == null) { Core.Log.Warning("[Drive] chassis gone before the player could enter."); _watch = null; yield break; }
            try { _chassis.EnterVehicle(); }
            catch (Exception e) { Core.Log.Error("[Drive] entering the chassis failed: " + e.Message); }

            // The spawn teleport reads as a big negative speed for a few frames, and VehicleCamera then flips to
            // the reverse view and stays there until the vehicle goes forward faster than 2 km/h.
            int resets = 0;
            for (int i = 0; i < 10 && _chassis != null; i++)
            {
                yield return null;
                VehicleCamera vc = _chassis != null ? _chassis.GetComponentInChildren<VehicleCamera>(true) : null;
                if (vc != null && vc.cameraReversed) { vc.cameraReversed = false; resets++; }
            }
            Core.Log.Msg("[Drive] camera reverse view reset " + resets + "x after entering.");

            bool wasIn = false;
            while (_chassis != null)
            {
                bool inside = false;
                try { inside = _chassis.LocalPlayerIsInVehicle; }
                catch (Exception e) { Core.Log.Warning("[Drive] chassis gone: " + e.Message); _chassis = null; break; }

                if (inside && !wasIn) Core.Log.Msg("[Drive] player in the driver seat.");
                if (!inside && wasIn)
                {
                    float d = Vector3.Distance(_chassis.transform.position, _homeChassisPos);
                    if (d <= HomeRadius) { Core.Log.Msg("[Drive] parked at home (" + d.ToString("0.0") + " m)."); ReturnHome(); yield break; }
                    Core.Log.Msg("[Drive] parked " + d.ToString("0") + " m from home. 'rvdrive' gets back in.");
                }
                wasIn = inside;
                yield return new WaitForSeconds(0.25f);
            }
            _watch = null;
        }

        /// <summary>rvdrive home: snap the RV back to its spot and despawn the chassis.</summary>
        internal static void ReturnHome()
        {
            try
            {
                if (_chassis == null) { Core.Log.Msg("[Drive] nothing to return - the RV is home."); return; }
                if (_chassis.LocalPlayerIsInVehicle) _chassis.ExitVehicle();

                if (_model != null)
                {
                    _model.SetParent(_modelParent, false);
                    _model.localPosition = _modelLocalPos;
                    _model.localRotation = _modelLocalRot;
                }
                RestoreColliders();
                ReleaseStaticParts();
                if (_contents != null)
                {
                    _contents.SetParent(_contentsParent, true);
                    _contents.SetPositionAndRotation(_contentsPos, _contentsRot);
                }

                VehicleManager vm = NetworkSingleton<VehicleManager>.Instance;
                if (vm != null) vm.PlayerOwnedVehicles.Remove(_chassis);
                LandVehicle chassis = _chassis;
                _chassis = null;
                MelonCoroutines.Start(DespawnNextFrame(chassis));
                Core.Log.Msg("[Drive] RV back home.");
            }
            catch (Exception e)
            {
                Core.Log.Error("[Drive] return home failed: " + e);
            }
        }

        // ExitVehicle frees the seat over an RPC; DestroyVehicle refuses an occupied vehicle, so wait a frame.
        private static IEnumerator DespawnNextFrame(LandVehicle chassis)
        {
            yield return null;
            yield return null;
            try { chassis.DestroyVehicle(); Core.Log.Msg("[Drive] chassis despawned."); }
            catch (Exception e) { Core.Log.Error("[Drive] chassis despawn failed: " + e.Message); }
        }

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

        // The vehicle camera orbits the stock van's cameraOrigin and pulls in wherever a ray from there hits a wall,
        // which inside the RV body is right away. Lift the origin above the roof and back the camera off.
        private const float CameraLift = 3.5f, CameraLateral = 11f, CameraVertical = 4f;

        private static void RaiseCamera(LandVehicle chassis, Vector3 rvFront)
        {
            try
            {
                VehicleCamera cam = chassis.GetComponentInChildren<VehicleCamera>(true);
                if (cam == null) { Core.Log.Warning("[Drive] chassis has no VehicleCamera - the camera stays inside the RV."); return; }
                Transform origin = cam.cameraOrigin;
                if (origin != null) origin.localPosition += Vector3.up * CameraLift;
                // The camera sits at -forward of the VehicleCamera's own transform, which can face the other way
                // than the RV's cab. Flip the offset so the camera always trails the cab.
                float lateral = Vector3.Dot(cam.transform.forward, rvFront) >= 0f ? CameraLateral : -CameraLateral;
                cam.lateralOffset = lateral;
                cam.verticalOffset = CameraVertical;
                Core.Log.Msg("[Drive] camera raised: origin " + (origin != null ? origin.position.ToString() : "missing")
                    + " camFwd=" + cam.transform.forward + " chassisFwd=" + chassis.transform.forward + " rvFront=" + rvFront
                    + " lateral=" + lateral + " vertical=" + CameraVertical);
            }
            catch (Exception e) { Core.Log.Warning("[Drive] camera adjust failed: " + e.Message); }
        }

        // Parented under the chassis, every RV collider joins its Rigidbody: the floor drags on the ground and the
        // walls stop the vehicle camera's ray. While driving, the chassis' own colliders carry the collisions.
        private static readonly System.Collections.Generic.List<Collider> _muted = new System.Collections.Generic.List<Collider>();

        private static int MuteColliders(Transform t)
        {
            int n = 0;
            foreach (Collider c in t.GetComponentsInChildren<Collider>(false))
            {
                if (!c.enabled || c.isTrigger) continue;
                c.enabled = false;
                _muted.Add(c);
                n++;
            }
            return n;
        }

        private static void RestoreColliders()
        {
            int back = 0;
            foreach (Collider c in _muted) if (c != null) { c.enabled = true; back++; }
            if (back != _muted.Count) Core.Log.Warning("[Drive] " + (_muted.Count - back) + " RV collider(s) gone before restore.");
            _muted.Clear();
        }

        // 77 of the RV's renderers (wheels, windows, side panels) are static-batched: they draw from one combined
        // mesh baked in world space at the home spot, so moving their transform moves nothing. While driving, the
        // originals are hidden and their slice of the combined mesh is drawn each frame with the drive offset.
        private sealed class StaticPart
        {
            public Renderer Renderer;
            public Mesh Mesh;
            public Material[] Materials;
            public int FirstSubMesh, Layer;
            public Matrix4x4 HomeBase;
        }

        private static readonly System.Collections.Generic.List<StaticPart> _static = new System.Collections.Generic.List<StaticPart>();
        private static Matrix4x4 _modelHomeInverse;
        private static object _draw;

        private static int TakeStaticParts(Transform model)
        {
            _static.Clear();
            _modelHomeInverse = model.worldToLocalMatrix;
            int skipped = 0;
            string meshName = null;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || r.forceRenderingOff || !r.isPartOfStaticBatch) continue;
                MeshRenderer mr = r.TryCast<MeshRenderer>();
                MeshFilter mf = r.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : null;
                if (mr == null || mesh == null) { skipped++; continue; }
                Transform batchRoot = r.staticBatchRootTransform;
                var mats = r.sharedMaterials;
                var part = new StaticPart
                {
                    Renderer = r,
                    Mesh = mesh,
                    Materials = new Material[mats.Length],
                    FirstSubMesh = mr.subMeshStartIndex,
                    Layer = r.gameObject.layer,
                    HomeBase = batchRoot != null ? batchRoot.localToWorldMatrix : Matrix4x4.identity,
                };
                for (int i = 0; i < mats.Length; i++) part.Materials[i] = mats[i];
                r.forceRenderingOff = true;
                _static.Add(part);
                meshName ??= mesh.name + " subMeshes=" + mesh.subMeshCount;
            }
            if (skipped > 0) Core.Log.Warning("[Drive] " + skipped + " static part(s) without a MeshRenderer or mesh stay at home.");
            if (meshName != null) Core.Log.Msg("[Drive] static parts draw from '" + meshName + "'.");
            if (_draw != null) MelonCoroutines.Stop(_draw);
            _draw = _static.Count > 0 ? MelonCoroutines.Start(DrawStaticParts()) : null;
            return _static.Count;
        }

        private static IEnumerator DrawStaticParts()
        {
            int failures = 0;
            while (_static.Count > 0 && _model != null)
            {
                Matrix4x4 delta = _model.localToWorldMatrix * _modelHomeInverse;
                foreach (StaticPart p in _static)
                {
                    Matrix4x4 m = delta * p.HomeBase;
                    for (int i = 0; i < p.Materials.Length; i++)
                    {
                        int sub = p.FirstSubMesh + i;
                        if (p.Materials[i] == null || sub >= p.Mesh.subMeshCount) continue;
                        try { Graphics.DrawMesh(p.Mesh, m, p.Materials[i], p.Layer, null, sub); }
                        catch (Exception e)
                        {
                            if (failures++ == 0) Core.Log.Error("[Drive] drawing a static part failed: " + e.Message);
                        }
                    }
                }
                yield return null;
            }
            _draw = null;
        }

        private static void ReleaseStaticParts()
        {
            if (_draw != null) { MelonCoroutines.Stop(_draw); _draw = null; }
            int back = 0;
            foreach (StaticPart p in _static) if (p.Renderer != null) { p.Renderer.forceRenderingOff = false; back++; }
            if (back != _static.Count) Core.Log.Warning("[Drive] " + (_static.Count - back) + " static part(s) gone before restore.");
            _static.Clear();
        }

        private static Bounds ModelBounds(Transform model)
        {
            bool any = false;
            Bounds b = new Bounds(model.position, Vector3.zero);
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(false))
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) Core.Log.Warning("[Drive] RV model has no renderer - using its pivot.");
            return b;
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

        internal static void Info()
        {
            Transform root = Managers.RVManager.Root;
            Core.Log.Msg("[Drive] active=" + Active + (root != null ? " rootPos=" + root.position + " rootFwd=" + root.forward : " (no RV)")
                + (_chassis != null ? " chassisPos=" + _chassis.transform.position + " fromHome=" + Vector3.Distance(_chassis.transform.position, _homeChassisPos).ToString("0.0")
                    + " speed=" + _chassis.Speed_Kmh.ToString("0.0") + " km/h" : ""));
            if (_chassis != null) Diagnose();
            if (root != null)
            {
                DumpChildren("root", root);
                Transform model = _model != null ? _model : root.Find("RV");
                if (model != null) { DumpChildren("model", model); StaticReport("model", model); }
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
