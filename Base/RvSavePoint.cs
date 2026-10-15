using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppScheduleOne.Persistence;
using MelonLoader;
using RVRepairVan.Managers;

namespace RVRepairVan.Base
{
    /// <summary>
    /// The save intercom every vanilla property has on a wall, cloned into the RV. The vanilla object carries its own
    /// collider, light, sounds and <see cref="SavePoint"/> logic (host only, 60 s cooldown), so the clone needs no
    /// wiring beyond a place on the wall.
    ///
    /// Its model is the exception. Every intercom in the scene is static-batched: it draws a slice of a combined mesh
    /// baked in world space at the source property, and no standalone intercom mesh exists at runtime. The clone's
    /// own renderers would draw a second intercom at that property, so they are switched off and the slice is drawn
    /// each frame at the clone instead.
    ///
    /// It sits under <c>rv/Main</c> (x along the RV, front +x; y across, door side +y; z up), so it travels with the
    /// model when the RV is driven, and it hangs off the intact model, so the wreck has none.
    /// </summary>
    internal static class RvSavePoint
    {
        private const string Name = "RVRepairVan_SavePoint";

        // Door-side wall, beside the light switch next to the door, under the window: free of furniture, gutted or not.
        private static Vector3 _local = new Vector3(0.98f, 1.563f, 0.12f);
        // Into the room from that wall.
        private static Vector3 _facing = new Vector3(0f, -1f, 0f);

        /// <summary>Place the intercom under the current RV model once; a second call finds it and returns.</summary>
        internal static void Ensure()
        {
            try
            {
                Transform model = RVManager.Model;
                if (model == null) { Core.LogDebug("[SavePoint] RV model not found yet - will retry on the next apply."); return; }
                Transform main = model.Find("rv/Main");
                if (main == null) { Core.Log.Warning("[SavePoint] 'rv/Main' not found under the RV model - the RV has no save intercom."); return; }
                if (main.Find(Name) != null) return;

                SavePoint template = FindTemplate();
                if (template == null) { Core.Log.Warning("[SavePoint] no vanilla save intercom in the scene - the RV has none; retrying on the next apply."); return; }

                GameObject go = UnityEngine.Object.Instantiate(template.gameObject, main, false);
                go.name = Name;
                go.SetActive(true);
                // The source hangs in another property's frame; match its size in the world, not its local scale.
                float scale = template.transform.lossyScale.x / Mathf.Max(0.0001f, main.lossyScale.x);
                go.transform.localScale = Vector3.one * scale;
                Place(go.transform);
                int redrawn = TakeModel(template.transform, go.transform);
                Core.Log.Msg("[SavePoint] save intercom placed in the RV (copied from '" + Path(template.transform) + "', scale "
                    + scale.ToString("0.###") + ", " + redrawn + " batched part(s) redrawn).");
            }
            catch (Exception e) { Core.Log.Error("[SavePoint] placing the save intercom failed: " + e); }
        }

        // Vanilla's intercom: local y is up, its face looks along local -x.
        private static void Place(Transform t)
        {
            Vector3 up = Vector3.forward;   // rv/Main is z-up
            t.localPosition = _local;
            t.localRotation = Quaternion.LookRotation(Vector3.Cross(-_facing, up), up);
        }

        private static SavePoint FindTemplate()
        {
            SavePoint fallback = null;
            foreach (SavePoint sp in UnityEngine.Object.FindObjectsOfType<SavePoint>(true))
            {
                if (sp == null || sp.gameObject.name == Name) continue;
                if (sp.gameObject.activeInHierarchy) return sp;
                if (fallback == null) fallback = sp;
            }
            return fallback;
        }

        // --- the model ------------------------------------------------------------------------------------

        private sealed class Part
        {
            public Transform Clone;
            public Mesh Mesh;
            public Material[] Materials;
            public int FirstSubMesh, Layer;
            public Matrix4x4 Bake;   // combined-mesh space -> the source renderer's local space
        }

        private static readonly List<Part> _parts = new List<Part>();
        private static object _draw;

        // Pairs each source renderer with its counterpart in the clone by path. Only the first LOD is drawn: the LOD
        // group cannot switch renderers that are drawn by hand, and two LODs drawn together fight on screen.
        private static int TakeModel(Transform source, Transform clone)
        {
            _parts.Clear();
            int plain = 0;
            foreach (Renderer src in source.GetComponentsInChildren<Renderer>(true))
            {
                Transform twin = clone.Find(RelativePath(source, src.transform));
                Renderer r = twin != null ? twin.GetComponent<Renderer>() : null;
                if (r == null) { Core.Log.Warning("[SavePoint] no clone of renderer '" + src.name + "' - that part stays invisible."); continue; }
                if (!src.isPartOfStaticBatch) { r.enabled = true; r.forceRenderingOff = false; plain++; continue; }
                r.forceRenderingOff = true;
                if (src.name.Contains("_LOD") && !src.name.EndsWith("_LOD0")) continue;

                MeshRenderer mr = src.TryCast<MeshRenderer>();
                MeshFilter mf = src.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : null;
                if (mr == null || mesh == null) { Core.Log.Warning("[SavePoint] batched part '" + src.name + "' has no mesh - it stays invisible."); continue; }
                Transform batchRoot = src.staticBatchRootTransform;
                var mats = src.sharedMaterials;
                var part = new Part
                {
                    Clone = twin,
                    Mesh = mesh,
                    Materials = new Material[mats.Length],
                    FirstSubMesh = mr.subMeshStartIndex,
                    Layer = twin.gameObject.layer,
                    Bake = src.transform.worldToLocalMatrix * (batchRoot != null ? batchRoot.localToWorldMatrix : Matrix4x4.identity),
                };
                for (int i = 0; i < mats.Length; i++) part.Materials[i] = mats[i];
                _parts.Add(part);
            }
            LODGroup lod = clone.GetComponentInChildren<LODGroup>(true);
            if (lod != null && _parts.Count > 0) lod.enabled = false;
            if (_draw != null) MelonCoroutines.Stop(_draw);
            _draw = _parts.Count > 0 ? MelonCoroutines.Start(Draw()) : null;
            if (plain > 0) Core.LogDebug("[SavePoint] " + plain + " unbatched renderer(s) draw themselves.");
            return _parts.Count;
        }

        private static IEnumerator Draw()
        {
            int failures = 0;
            while (_parts.Count > 0 && _parts[0].Clone != null)
            {
                foreach (Part p in _parts)
                {
                    if (p.Clone == null || !p.Clone.gameObject.activeInHierarchy) continue;
                    Matrix4x4 m = p.Clone.localToWorldMatrix * p.Bake;
                    for (int i = 0; i < p.Materials.Length; i++)
                    {
                        int sub = p.FirstSubMesh + i;
                        if (p.Materials[i] == null || sub >= p.Mesh.subMeshCount) continue;
                        try { Graphics.DrawMesh(p.Mesh, m, p.Materials[i], p.Layer, null, sub); }
                        catch (Exception e)
                        {
                            if (failures++ == 0) Core.Log.Error("[SavePoint] drawing the intercom failed: " + e.Message);
                        }
                    }
                }
                yield return null;
            }
            Core.LogDebug("[SavePoint] intercom gone with its scene - redraw stopped.");
            _parts.Clear();
            _draw = null;
        }

        private static string RelativePath(Transform root, Transform t)
        {
            if (t == root) return "";
            string p = t.name;
            for (Transform up = t.parent; up != null && up != root; up = up.parent) p = up.name + "/" + p;
            return p;
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            for (Transform up = t.parent; up != null; up = up.parent) p = up.name + "/" + p;
            return p;
        }

#if DEBUG
        /// <summary>Debug (rvsave): report the intercom, or move it with 'at x y z [fx fy fz]' in rv/Main space.</summary>
        internal static void DebugCmd(string[] args)
        {
            Transform model = RVManager.Model;
            Transform t = model != null ? model.Find("rv/Main/" + Name) : null;
            if (args.Length >= 4 && args[0].ToLower() == "at")
            {
                if (t == null) { Core.Log.Warning("[Debug] rvsave: no intercom in the RV."); return; }
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                float F(int i) => float.Parse(args[i], ci);
                _local = new Vector3(F(1), F(2), F(3));
                if (args.Length >= 7) _facing = new Vector3(F(4), F(5), F(6));
                Place(t);
            }
            if (t == null) { Core.Log.Warning("[Debug] rvsave: no intercom in the RV (model " + (model != null) + ")."); return; }
            if (args.Length >= 1 && args[0].ToLower() == "probe")
            {
                // How far the back of each intercom sits from the wall behind it: vanilla's against its property wall,
                // ours against the RV's.
                SavePoint template = FindTemplate();
                if (template != null) ProbeWall("vanilla", template.transform, template.transform.right);
                ProbeWall("rv", t, t.parent.TransformDirection(-_facing));
                return;
            }
            if (args.Length >= 1 && args[0].ToLower() == "look")
            {
                var pc = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerCamera>.Instance;
                if (pc == null) { Core.Log.Warning("[Debug] rvsave look: no PlayerCamera."); return; }
                if (args.Length >= 2 && args[1].ToLower() == "off") { pc.StopTransformOverride(0f, true, true); Core.Log.Msg("[Debug] rvsave look: camera back on the player."); return; }
                // From inside the room and off to one side, so a gap between intercom and wall shows as a shadow.
                // 'look <into> <along>' sets the offsets in metres; a small 'into' looks along the wall.
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                float off = args.Length >= 2 ? float.Parse(args[1], inv) : 0.55f;
                float side = args.Length >= 3 ? float.Parse(args[2], inv) : 0.45f;
                Vector3 into = t.parent.TransformDirection(_facing).normalized;
                Vector3 along = t.parent.TransformDirection(Vector3.right).normalized;
                Vector3 eye = t.position + into * off + along * side + Vector3.up * 0.1f;
                pc.OverrideTransform(eye, Quaternion.LookRotation(t.position - eye), 0f, false);
                Core.Log.Msg("[Debug] rvsave look: eye=" + eye + " at " + t.position);
                return;
            }
            var sp = t.GetComponent<SavePoint>();
            if (args.Length >= 1 && args[0].ToLower() == "use")
            {
                // What pressing E on it does: InteractableObject.onInteractStart -> SavePoint.Interacted.
                var io = t.GetComponent<Il2CppScheduleOne.Interaction.InteractableObject>();
                if (io == null) { Core.Log.Warning("[Debug] rvsave use: no InteractableObject on the intercom."); return; }
                io.onInteractStart.Invoke();
                Core.Log.Msg("[Debug] rvsave use: interaction fired, saving=" + Il2CppScheduleOne.DevUtilities.Singleton<SaveManager>.Instance.IsSaving);
            }
            Core.Log.Msg("[Debug] rvsave local=" + t.localPosition + " facing=" + _facing + " inModel=" + model.InverseTransformPoint(t.position)
                + " active=" + t.gameObject.activeInHierarchy + " savepoint=" + (sp != null)
                + " colliders=" + t.GetComponentsInChildren<Collider>(true).Length
                + " sinceSave=" + Il2CppScheduleOne.DevUtilities.Singleton<SaveManager>.Instance.SecondsSinceLastSave.ToString("0.0") + " redrawn=" + _parts.Count + " drawing=" + (_draw != null));
            foreach (Part p in _parts)
                Core.Log.Msg("[Debug]   " + p.Clone.name + " mesh='" + p.Mesh.name + "' sub " + p.FirstSubMesh + "+" + p.Materials.Length);
        }

        // Distances along 'back' (out of the face, into the wall), measured from the pivot: how far the intercom's
        // colliders and visible parts reach, and where the first surface behind it is.
        internal static void ProbeWall(string label, Transform t, Vector3 back)
        {
            back = back.normalized;
            float colMax = float.NegativeInfinity, colMin = float.PositiveInfinity;
            foreach (Collider c in t.GetComponentsInChildren<Collider>(true))
                Reach(c.bounds, t.position, back, ref colMin, ref colMax);
            float renMax = float.NegativeInfinity, renMin = float.PositiveInfinity;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                if (!r.isPartOfStaticBatch) Reach(r.bounds, t.position, back, ref renMin, ref renMax);
            Core.Log.Msg("[Debug] rvsave probe " + label + ": pivot=" + t.position + " back=" + back
                + " colliders " + colMin.ToString("0.000") + ".." + colMax.ToString("0.000")
                + " renderers " + renMin.ToString("0.000") + ".." + renMax.ToString("0.000"));
            const float behind = 0.3f;
            var hits = new List<RaycastHit>(Physics.RaycastAll(t.position - back * behind, back, 1.5f, ~0, QueryTriggerInteraction.Ignore));
            hits.Sort((a, b) => a.distance.CompareTo(b.distance));
            int shown = 0;
            foreach (RaycastHit h in hits)
            {
                if (h.collider.transform.IsChildOf(t)) continue;
                Core.Log.Msg("[Debug]   surface at " + (h.distance - behind).ToString("0.000") + ": " + Path(h.collider.transform)
                    + " normal=" + h.normal);
                if (++shown == 3) break;
            }
            if (shown == 0) Core.Log.Warning("[Debug]   no surface within 1.2 m behind " + label + ".");
        }

        private static void Reach(Bounds b, Vector3 pivot, Vector3 dir, ref float min, ref float max)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                float d = Vector3.Dot(corner - pivot, dir);
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }
#endif
    }
}
