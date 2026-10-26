using System.Collections;
using DooDesch.Localization;
using MelonLoader;
using Il2CppScheduleOne.Interaction;
using RVRepairVan.Managers;
using RVRepairVan.Net;

namespace RVRepairVan.Base
{
    /// <summary>
    /// The roof hatch over the living area opens and closes from inside: look up at it and press the interact key.
    /// It shuts when someone starts to drive and stays shut until somebody opens it again. The stock model has the
    /// lid propped open, so that pose is "open" and a loaded game starts with it.
    ///
    /// The lid carries its own box collider, so turning its transform about the hinge moves the collider. Its mesh
    /// is in the scene's static batch, which draws from a combined mesh baked at home and ignores the transform. The
    /// lid's batched renderer is therefore switched off and its slice of the combined mesh is drawn each frame at the
    /// lid's transform, which takes the hinge and, while the RV is on its chassis, the drive along. The static redraw
    /// in <see cref="RvMobile"/> skips it, because it is already switched off when that one collects.
    ///
    /// Co-op: the host owns the state. A client sends the state it wants (<see cref="RvOp.HatchRequest"/>) and turns
    /// its own lid right away; the host applies it and sends the result to everyone (<see cref="RvOp.HatchSync"/>).
    /// </summary>
    internal static class RvHatch
    {
        private const string HatchPath = "rv/Main/RoofPanel/Hatch";
        private const string PromptName = "RVRepairVan_HatchPrompt";
        private const float SwingSeconds = 0.7f;
        private const float PromptBelowCeiling = 0.04f;   // the prompt box hangs this far under the ceiling, so the interaction ray meets it first

        private sealed class Part
        {
            public Renderer Renderer;
            public Transform T;
            public Mesh Mesh;
            public Material[] Materials;
            public int FirstSubMesh, Layer;
            public Matrix4x4 Bake;   // combined-mesh space -> the renderer's local space, taken at home in the stock pose
        }

        private static readonly List<Part> _parts = new List<Part>();
        private static Transform _hatch, _model;
        // In the lid parent's space: the stock (open) pose, the shut pose, the hinge point and the turn from shut to open.
        private static Vector3 _openPos, _closedPos, _pivot;
        private static Quaternion _openRot = Quaternion.identity, _closedRot = Quaternion.identity, _swing = Quaternion.identity;
        private static Bounds _shutInMain;   // the shut lid in rv/Main, for the prompt under it
        private static InteractableObject _prompt;
        private static object _loop;
        private static bool _open = true;
        private static float _t = 1f;   // 0 = shut, 1 = open

        /// <summary>True while the hatch stands open, or is still swinging open.</summary>
        internal static bool IsOpen => _open;

        /// <summary>Set by a view that hides the roof: the redrawn lid is not drawn while it is true.</summary>
        internal static bool Hidden { get; set; }

        // --- setup ----------------------------------------------------------------------------------------

        /// <summary>
        /// Find the hatch on the current intact model and take over its drawing. The batched mesh is measured against
        /// the lid's pose at home, so the first call must come while the model stands at home
        /// (<paramref name="modelAtHome"/>); later calls find the hatch taken and return.
        /// </summary>
        internal static void Ensure(bool modelAtHome)
        {
            try
            {
                Transform model = RVManager.Model;
                if (model == null) { Core.LogDebug("[Hatch] RV model not found yet - retrying on the next apply."); return; }
                if (_hatch != null && _model != null && _model.Pointer == model.Pointer) return;
                if (RVManager.IsDestroyed()) { Core.LogDebug("[Hatch] the RV is wrecked - no hatch to open."); return; }
                if (!modelAtHome) { Core.Log.Msg("[Hatch] the RV is away from home - the hatch is set up when it is back on its spot or on its chassis."); return; }
                Transform hatch = model.Find(HatchPath);
                if (hatch == null) { Core.Log.Warning("[Hatch] '" + HatchPath + "' not found on the RV model - the roof hatch cannot be opened or shut."); return; }
                Transform main = model.Find("rv/Main");
                if (main == null) { Core.Log.Warning("[Hatch] 'rv/Main' not found on the RV model - the roof hatch cannot be opened or shut."); return; }

                Release();
                if (!MeasureHinge(main, hatch)) return;
                _model = model;
                _hatch = hatch;
                _open = true;
                _t = 1f;
                int taken = TakeRenderers(hatch);
                _prompt = MakePrompt(main, hatch);
                if (_loop == null) _loop = MelonCoroutines.Start(Loop());
                Core.Log.Msg("[Hatch] roof hatch ready: " + taken + " batched renderer(s) redrawn, prompt " + (_prompt != null ? "built" : "missing") + ".");
            }
            catch (Exception e) { Core.Log.Error("[Hatch] setting up the roof hatch failed: " + e); }
        }

        /// <summary>Scene change: forget the hatch. The objects go with their scene.</summary>
        internal static void Reset()
        {
            if (_loop != null) { MelonCoroutines.Stop(_loop); _loop = null; }
            _parts.Clear();
            _hatch = null;
            _model = null;
            _prompt = null;
            _open = true;
            _t = 1f;
        }

        // Back to the stock lid: propped open, drawn by its own renderer. Used before taking it again.
        private static void Release()
        {
            if (_hatch != null) { _hatch.localPosition = _openPos; _hatch.localRotation = _openRot; }
            foreach (Part p in _parts) if (p.Renderer != null) p.Renderer.forceRenderingOff = false;
            _parts.Clear();
            if (_prompt != null) UnityEngine.Object.Destroy(_prompt.gameObject);
            _prompt = null;
            _hatch = null;
        }

        // The stock lid leans on its lower edge. Shutting it turns it about that edge until it lies flat. Measured in
        // rv/Main (x along the RV, front +x; y across; z up) from the lid's box collider, whose thinnest axis is the
        // lid's normal.
        private static bool MeasureHinge(Transform main, Transform hatch)
        {
            BoxCollider box = hatch.GetComponent<BoxCollider>();
            if (box == null) { Core.Log.Warning("[Hatch] the lid has no box collider - it cannot be measured and stays as it is."); return false; }
            Vector3 size = Vector3.Scale(box.size, hatch.lossyScale);
            int thin = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
            Vector3 up = Vector3.forward;   // rv/Main is z-up
            Vector3 normal = InMain(main, hatch, thin);
            if (normal.z < 0f) normal = -normal;
            float tilt = Vector3.Angle(normal, up);
            if (tilt < 2f) { Core.Log.Warning("[Hatch] the stock lid lies flat (" + tilt.ToString("0.0") + " deg) - there is no open pose to swing to."); return false; }
            Vector3 hinge = Vector3.Cross(normal, up).normalized;
            // Along the lid, from the hinge to the free edge, which stands higher.
            Vector3 along = Vector3.Cross(hinge, normal).normalized;
            if (along.z < 0f) along = -along;
            int alongAxis = -1;
            float best = -1f;
            for (int i = 0; i < 3; i++)
            {
                if (i == thin) continue;
                float d = Mathf.Abs(Vector3.Dot(InMain(main, hatch, i), along));
                if (d > best) { best = d; alongAxis = i; }
            }
            float length = size[alongAxis], width = size[3 - thin - alongAxis], thickness = size[thin];
            Vector3 centre = main.InverseTransformPoint(hatch.TransformPoint(box.center));
            Vector3 pivotMain = centre - along * (length * 0.5f) - normal * (thickness * 0.5f);

            // The lid stands open past upright: it leans away from the opening, over its hinge. Shutting turns it back
            // over the hinge by the rest of the half turn until its outer face, now the underside, faces up again. Of
            // the two turns about the hinge by that angle, the one that brings the normal to straight down is it.
            Quaternion a = Quaternion.AngleAxis(tilt - 180f, hinge), b = Quaternion.AngleAxis(180f - tilt, hinge);
            Quaternion shutMain = (a * normal).z < (b * normal).z ? a : b;
            Transform parent = hatch.parent;
            Quaternion mainToParent = Quaternion.Inverse(parent.rotation) * main.rotation;
            Quaternion shutParent = mainToParent * shutMain * Quaternion.Inverse(mainToParent);
            _pivot = parent.InverseTransformPoint(main.TransformPoint(pivotMain));
            _openPos = hatch.localPosition;
            _openRot = hatch.localRotation;
            _closedRot = shutParent * _openRot;
            _closedPos = _pivot + shutParent * (_openPos - _pivot);
            _swing = Quaternion.Inverse(shutParent);

            Vector3 alongShut = shutMain * along;
            Vector3 centreShut = pivotMain + shutMain * (centre - pivotMain);
            _shutInMain = new Bounds(centreShut, new Vector3(
                Mathf.Abs(alongShut.x) * length + Mathf.Abs(hinge.x) * width,
                Mathf.Abs(alongShut.y) * length + Mathf.Abs(hinge.y) * width, thickness));
            Core.Log.Msg("[Hatch] lid " + length.ToString("0.00") + " x " + width.ToString("0.00") + " m, propped open " + tilt.ToString("0")
                + " deg in the stock model; hinge in rv/Main at " + pivotMain.ToString("F2") + " along " + hinge.ToString("F2")
                + ", shut lid centre " + centreShut.ToString("F2") + ".");
            return true;
        }

        // A local axis of the lid as a direction in rv/Main.
        private static Vector3 InMain(Transform main, Transform hatch, int axis) =>
            main.InverseTransformDirection(hatch.TransformDirection(axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward)).normalized;

        private static int TakeRenderers(Transform hatch)
        {
            int plain = 0;
            foreach (Renderer r in hatch.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.isPartOfStaticBatch) { plain++; continue; }
                MeshRenderer mr = r.TryCast<MeshRenderer>();
                MeshFilter mf = r.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : null;
                if (mr == null || mesh == null) { Core.Log.Warning("[Hatch] batched part '" + r.name + "' has no mesh - it stays drawn by the batch and does not swing."); continue; }
                Transform batchRoot = r.staticBatchRootTransform;
                var mats = r.sharedMaterials;
                var part = new Part
                {
                    Renderer = r,
                    T = r.transform,
                    Mesh = mesh,
                    Materials = new Material[mats.Length],
                    FirstSubMesh = mr.subMeshStartIndex,
                    Layer = r.gameObject.layer,
                    Bake = r.transform.worldToLocalMatrix * (batchRoot != null ? batchRoot.localToWorldMatrix : Matrix4x4.identity),
                };
                for (int i = 0; i < mats.Length; i++) part.Materials[i] = mats[i];
                r.forceRenderingOff = true;
                _parts.Add(part);
            }
            if (plain > 0) Core.LogDebug("[Hatch] " + plain + " unbatched renderer(s) follow the hatch on their own.");
            return _parts.Count;
        }

        // A trigger box just under the cabin ceiling, below the shut lid, on the interaction layer. The ceiling collider
        // has no hole at the hatch, so a prompt at the lid itself is never reached from inside. It hangs off rv/Main and
        // not off the lid, so it stays where it is while the lid swings.
        private static InteractableObject MakePrompt(Transform main, Transform hatch)
        {
            try
            {
                Transform old = main.Find(PromptName);
                if (old != null) UnityEngine.Object.Destroy(old.gameObject);
                float ceiling = CeilingUnderLid(main, hatch);
                var go = new GameObject(PromptName);
                go.transform.SetParent(main, false);
                go.layer = RvMobile.InteractionLayer();
                go.transform.localPosition = new Vector3(_shutInMain.center.x, _shutInMain.center.y, ceiling - PromptBelowCeiling);
                go.transform.localRotation = Quaternion.identity;
                var col = go.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.center = Vector3.zero;
                col.size = new Vector3(_shutInMain.size.x, _shutInMain.size.y, 0.06f);
                var io = go.AddComponent<InteractableObject>();
                io.SetMessage(L10n.T("Close hatch"));
                io.MaxInteractionRange = 3f;
                io.RequiresUniqueClick = true;
                io.onHovered.AddListener((Action)OnHovered);
                io.onInteractStart.AddListener((Action)OnUsed);
                Core.LogDebug("[Hatch] prompt under the ceiling at rv/Main z " + (ceiling - PromptBelowCeiling).ToString("F2") + ".");
                return io;
            }
            catch (Exception e) { Core.Log.Error("[Hatch] the hatch prompt failed - the hatch cannot be opened: " + e); return null; }
        }

        // rv/Main height of the first solid surface above standing height under the shut lid: the cabin ceiling.
        private static float CeilingUnderLid(Transform main, Transform hatch)
        {
            Vector3 from = main.TransformPoint(new Vector3(_shutInMain.center.x, _shutInMain.center.y, -0.5f));
            Vector3 dir = main.TransformDirection(Vector3.forward);
            float best = float.MaxValue, z = float.NaN;
            foreach (RaycastHit h in Physics.RaycastAll(from, dir, 4f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == null || h.collider.transform.IsChildOf(hatch) || h.distance >= best) continue;
                best = h.distance;
                z = main.InverseTransformPoint(h.point).z;
            }
            if (!float.IsNaN(z)) return z;
            float guess = _shutInMain.min.z - 0.1f;
            Core.Log.Warning("[Hatch] no ceiling found under the lid - the prompt goes to rv/Main z " + guess.ToString("F2") + " and may not be reachable.");
            return guess;
        }

        // --- state ----------------------------------------------------------------------------------------

        private static void OnHovered()
        {
            if (_prompt == null) return;
            _prompt.SetMessage(_open ? L10n.T("Close hatch") : L10n.T("Open hatch"));
            _prompt.SetInteractableState(InteractableObject.EInteractableState.Default);
        }

        private static void OnUsed()
        {
            try
            {
                bool want = !_open;
                if (NetworkBus.Online && !NetworkBus.IsServer)
                {
                    SetLocal(want, "the local player");
                    NetworkBus.SendToHost(RvOp.HatchRequest, want ? 1 : 0);
                    Core.Log.Msg("[Hatch] asked the host to " + (want ? "open" : "close") + " the hatch.");
                    return;
                }
                HostSet(want, "the local player");
            }
            catch (Exception e) { Core.Log.Error("[Hatch] using the hatch failed: " + e); }
        }

        /// <summary>Host: a client wants the hatch open (1) or shut (0).</summary>
        internal static void HostOnRequest(int open)
        {
            if (_hatch == null) Ensure(!RvMobile.Away);
            HostSet(open != 0, "a client");
        }

        /// <summary>The RV started driving: the hatch shuts. Host and offline send it; a client shuts its own copy at once.</summary>
        internal static void OnDriveStart()
        {
            if (!_open) return;
            if (NetworkBus.Online && !NetworkBus.IsServer) { SetLocal(false, "the RV started driving"); return; }
            HostSet(false, "the RV started driving");
        }

        private static void HostSet(bool open, string who)
        {
            SetLocal(open, who);
            Broadcast();
        }

        private static void Broadcast()
        {
            if (!NetworkBus.Online || !NetworkBus.IsServer) return;
            NetworkBus.BroadcastToAll(RvOp.HatchSync, _open ? 1 : 0);
        }

        /// <summary>Host: part of the join snapshot.</summary>
        internal static void HostSnapshot() => Broadcast();

        /// <summary>Client: the host's hatch state.</summary>
        internal static void ApplySync(int open)
        {
            if (NetworkBus.IsServer) return;
            if (_hatch == null) Ensure(!RvMobile.Away);
            SetLocal(open != 0, "the host");
        }

        private static void SetLocal(bool open, string who)
        {
            if (_hatch == null) { Core.Log.Warning("[Hatch] " + who + ": " + (open ? "open" : "close") + " asked, but the hatch is not set up - nothing moves."); return; }
            if (open == _open) { Core.LogDebug("[Hatch] " + who + ": the hatch is " + (open ? "open" : "shut") + " already."); return; }
            _open = open;
            Core.Log.Msg("[Hatch] " + (open ? "opening" : "closing") + " the roof hatch (" + who + ").");
            if (_loop == null) _loop = MelonCoroutines.Start(Loop());
        }

        // --- motion and drawing ---------------------------------------------------------------------------

        private static IEnumerator Loop()
        {
            int failures = 0;
            while (_hatch != null)
            {
                try
                {
                    Step();
                    Draw();
                    failures = 0;
                }
                catch (Exception e)
                {
                    if (failures++ == 0) Core.Log.Error("[Hatch] moving or drawing the hatch failed: " + e);
                }
                yield return null;
            }
            Core.LogDebug("[Hatch] hatch gone with its scene - redraw stopped.");
            _loop = null;
        }

        private static void Step()
        {
            float target = _open ? 1f : 0f;
            if (Mathf.Approximately(_t, target)) return;
            _t = Mathf.MoveTowards(_t, target, Time.deltaTime / SwingSeconds);
            float e = _t * _t * (3f - 2f * _t);
            Quaternion q = Quaternion.Slerp(Quaternion.identity, _swing, e);
            _hatch.localRotation = q * _closedRot;
            _hatch.localPosition = _pivot + q * (_closedPos - _pivot);
        }

        private static void Draw()
        {
            if (Hidden || _parts.Count == 0 || !_hatch.gameObject.activeInHierarchy) return;
            foreach (Part p in _parts)
            {
                if (p.T == null) continue;
                Matrix4x4 m = p.T.localToWorldMatrix * p.Bake;
                for (int i = 0; i < p.Materials.Length; i++)
                {
                    int sub = p.FirstSubMesh + i;
                    if (p.Materials[i] == null || sub >= p.Mesh.subMeshCount) continue;
                    Graphics.DrawMesh(p.Mesh, m, p.Materials[i], p.Layer, null, sub);
                }
            }
        }

#if DEBUG
        // --- console --------------------------------------------------------------------------------------

        /// <summary>rvhatch [open|close|look|use]: report, set, view or press the hatch.</summary>
        internal static void DebugCmd(string[] args)
        {
            string a = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (_hatch == null) Ensure(!RvMobile.Away);
            if (a == "open" || a == "close")
            {
                if (NetworkBus.Online && !NetworkBus.IsServer) { SetLocal(a == "open", "the console"); NetworkBus.SendToHost(RvOp.HatchRequest, a == "open" ? 1 : 0); }
                else HostSet(a == "open", "the console");
            }
            else if (a == "use") { if (_prompt == null) Core.Log.Warning("[Hatch] use: no prompt."); else { _prompt.onInteractStart.Invoke(); Core.Log.Msg("[Hatch] use: prompt pressed."); } }
            else if (a == "look") { Look(); return; }
            Report();
        }

        private static void Report()
        {
            if (_hatch == null) { Core.Log.Msg("[Hatch] no hatch set up (model " + (RVManager.Model != null) + ", away " + RvMobile.Away + ")."); return; }
            Transform main = _model != null ? _model.Find("rv/Main") : null;
            Core.Log.Msg("[Hatch] open=" + _open + " t=" + _t.ToString("0.00") + " hidden=" + Hidden + " parts=" + _parts.Count
                + " local=" + _hatch.localPosition.ToString("F3") + " rot=" + _hatch.localEulerAngles.ToString("F1")
                + " layer=" + LayerMask.LayerToName(_hatch.gameObject.layer) + " children=" + _hatch.childCount
                + (main != null ? " inMain=" + main.InverseTransformPoint(_hatch.position).ToString("F2") : ""));
            foreach (Collider c in _hatch.GetComponentsInChildren<Collider>(true))
                Core.Log.Msg("[Hatch]   collider " + c.name + " " + c.GetIl2CppType().Name + " enabled=" + c.enabled + " trigger=" + c.isTrigger
                    + " bounds " + c.bounds.center.ToString("F2") + " size " + c.bounds.size.ToString("F2"));
            foreach (Renderer r in _hatch.GetComponentsInChildren<Renderer>(true))
                Core.Log.Msg("[Hatch]   renderer " + r.name + " static=" + r.isPartOfStaticBatch + " off=" + r.forceRenderingOff
                    + " mat=" + (r.sharedMaterial != null ? r.sharedMaterial.name : "-"));
            if (_prompt != null && main != null)
                Core.Log.Msg("[Hatch]   prompt inMain=" + main.InverseTransformPoint(_prompt.transform.position).ToString("F2") + " layer="
                    + LayerMask.LayerToName(_prompt.gameObject.layer) + " msg='" + _prompt.message + "'");
            if (main == null) return;
            // What the cabin's ceiling is under the hatch: the first surfaces up from standing height.
            Vector3 from = main.TransformPoint(new Vector3(_shutInMain.center.x, _shutInMain.center.y, -0.5f));
            var hits = new List<RaycastHit>(Physics.RaycastAll(from, main.TransformDirection(Vector3.forward), 3f, ~0, QueryTriggerInteraction.Collide));
            hits.Sort((x, y) => x.distance.CompareTo(y.distance));
            foreach (RaycastHit h in hits)
                Core.Log.Msg("[Hatch]   up " + h.distance.ToString("0.00") + " m: " + h.collider.name + " (" + (h.collider.transform.parent != null ? h.collider.transform.parent.name : "-")
                    + ") layer=" + LayerMask.LayerToName(h.collider.gameObject.layer) + " trigger=" + h.collider.isTrigger + " inMain z=" + main.InverseTransformPoint(h.point).z.ToString("F2"));
            var im = Singleton<InteractionManager>.Instance;
            if (im != null) Core.Log.Msg("[Hatch]   interaction mask=" + im.Interaction_SearchMask.value);
        }

        // Stand in the cabin under the hatch and look up at it, so the real hover prompt shows.
        private static void Look()
        {
            var pc = PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerCamera>.Instance;
            var pm = PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerMovement>.Instance;
            Transform main = _model != null ? _model.Find("rv/Main") : null;
            if (pc == null || pm == null || main == null || _prompt == null) { Core.Log.Warning("[Hatch] look: no player, model or prompt."); return; }
            Vector3 below = main.TransformPoint(new Vector3(_shutInMain.center.x - 0.9f, _shutInMain.center.y, -1.1f));
            pm.Teleport(below, true);
            MelonCoroutines.Start(LookLater(pc));
        }

        private static IEnumerator LookLater(Il2CppScheduleOne.PlayerScripts.PlayerCamera pc)
        {
            yield return new WaitForSeconds(0.5f);
            if (_prompt == null || pc == null) { Core.Log.Warning("[Hatch] look: prompt or camera gone before the look."); yield break; }
            pc.LookAt(_prompt.transform.position, 0.1f);
            yield return new WaitForSeconds(0.3f);
            var im = Singleton<InteractionManager>.Instance;
            InteractableObject h = im != null ? im.HoveredInteractableObject : null;
            Core.Log.Msg("[Hatch] look: hovered " + (h == null ? "nothing" : "'" + h.gameObject.name + "' message='" + h.message + "'") + ".");
        }
#endif
    }
}
