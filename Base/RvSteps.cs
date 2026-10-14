using System;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;

namespace RVRepairVan.Base
{
    /// <summary>
    /// The entry step under the RV door folds up and slides in under the floor while someone drives, and comes
    /// back out when the RV stands with nobody at the wheel, like a motorhome's retracting step.
    ///
    /// Folding squashes the step towards its top edge, the way a scissor step closes; sliding then carries it in
    /// behind the skirt. A step in the scene's static batch cannot be moved through its transform;
    /// <see cref="RvMobile"/> redraws it and asks <see cref="Offset"/> for the same motion. Any other part of it is
    /// moved through its transform and set back when the RV goes home.
    /// </summary>
    internal static class RvSteps
    {
        private const string StepsPath = "rv/Main/Steps";
        private const float Duration = 0.6f;       // seconds for the full fold and slide
        private const float Clearance = 0.05f;     // past the step's own depth, so its edge ends under the skirt
        private const float Folded = 0.2f;         // the step's height when folded, as a share of its full height

        private sealed class Part
        {
            public Transform T;
            public Vector3 LocalPos;
            public Vector3 LocalScale;
            public int UpAxis = -1;      // the local axis that points up at home, -1 = not folded
            public Vector3 SlideLocal;   // the full slide in the parent's space
            public Vector3 KeepTopLocal; // the shift, in the parent's space, that holds the top edge while folding fully
        }

        private static readonly List<Renderer> _static = new List<Renderer>();
        private static readonly List<Part> _moving = new List<Part>();
        private static Vector3 _slideWorldHome;       // the full slide in home world space, for the redraw
        private static float _topWorldHome;           // the step's top edge in home world space, the fold's hinge
        private static float _t;                      // 0 = out, 1 = in
        private static bool _in;
        private static object _loop;

        /// <summary>Find the step while the model still stands at home. <paramref name="homeRot"/> is the chassis rotation at home.</summary>
        internal static void Capture(Transform model, Quaternion homeRot)
        {
            Release();
            Transform steps = model.Find(StepsPath);
            if (steps == null) { Core.Log.Warning("[Steps] '" + StepsPath + "' not found - the step stays out while driving."); return; }

            Quaternion inv = Quaternion.Inverse(homeRot);
            bool any = false;
            Bounds b = default;
            foreach (Renderer r in steps.GetComponentsInChildren<Renderer>(false))
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                if (r.isPartOfStaticBatch) _static.Add(r);
            }
            if (!any) { Core.Log.Warning("[Steps] the step has no renderers - nothing to fold."); return; }

            // The chassis' right axis points across the RV; the step sits on one side of the middle and slides
            // towards it by its own depth.
            Vector3 c = inv * (b.center - model.position);
            Vector3 mid = inv * (BodyCentre(model) - model.position);
            float side = Mathf.Sign(c.x - mid.x);
            float depth = Mathf.Abs((inv * b.size).x);
            _slideWorldHome = homeRot * new Vector3(-side * (depth + Clearance), 0f, 0f);
            _topWorldHome = b.max.y;

            // Moving the step root carries every live part under it; the static ones are redrawn from home and
            // take the same motion through Offset.
            var part = new Part
            {
                T = steps,
                LocalPos = steps.localPosition,
                LocalScale = steps.localScale,
                UpAxis = UpAxis(steps),
                SlideLocal = steps.parent.InverseTransformVector(_slideWorldHome),
                KeepTopLocal = steps.parent.InverseTransformVector(Vector3.up * (b.max.y - steps.position.y) * (1f - Folded)),
            };
            if (part.UpAxis < 0) Core.Log.Warning("[Steps] the step is tilted at home - it slides in without folding.");
            _moving.Add(part);
            _t = 0f;
            _in = false;
            Core.Log.Msg("[Steps] step found (" + _static.Count + " redrawn, " + _moving.Count + " moved), folds to "
                + (b.size.y * Folded).ToString("0.00") + " m and slides " + (depth + Clearance).ToString("0.00") + " m in while driving.");
        }

        private static Vector3 BodyCentre(Transform model)
        {
            Transform body = model.Find("rv/Main");
            Renderer r = body != null ? body.GetComponent<Renderer>() : null;
            if (r != null) return r.bounds.center;
            Core.Log.Warning("[Steps] no renderer on 'rv/Main' - the step slides towards the model's origin.");
            return model.position;
        }

        // The local axis of t that points (almost) straight up, so scaling it squashes the step towards its top.
        private static int UpAxis(Transform t)
        {
            Vector3 u = t.InverseTransformDirection(Vector3.up);
            float x = Mathf.Abs(u.x), y = Mathf.Abs(u.y), z = Mathf.Abs(u.z);
            if (x >= 0.95f) return 0;
            if (y >= 0.95f) return 1;
            if (z >= 0.95f) return 2;
            return -1;
        }

        /// <summary>Fold in (someone drives) or out (nobody at the wheel).</summary>
        internal static void SetIn(bool slideIn)
        {
            if (_static.Count == 0 && _moving.Count == 0) return;
            if (_in == slideIn) return;
            _in = slideIn;
            Core.LogDebug("[Steps] step " + (slideIn ? "in" : "out") + ".");
            if (_loop == null) _loop = MelonCoroutines.Start(Animate());
        }

        /// <summary>The step's fold and slide for a redrawn static part, in home world space; identity for anything else.</summary>
        internal static Matrix4x4 Offset(Renderer r)
        {
            if (_t <= 0f || r == null) return Matrix4x4.identity;
            foreach (Renderer s in _static)
                if (s != null && s.Pointer == r.Pointer)
                {
                    float e = Ease(_t);
                    Vector3 hinge = new Vector3(0f, _topWorldHome, 0f);
                    return Matrix4x4.Translate(_slideWorldHome * e) * Matrix4x4.Translate(hinge)
                        * Matrix4x4.Scale(new Vector3(1f, Mathf.Lerp(1f, Folded, e), 1f)) * Matrix4x4.Translate(-hinge);
                }
            return Matrix4x4.identity;
        }

        private static IEnumerator Animate()
        {
            int failures = 0;
            while (true)
            {
                float target = _in ? 1f : 0f;
                if (Mathf.Approximately(_t, target)) break;
                _t = Mathf.MoveTowards(_t, target, Time.deltaTime / Duration);
                try { ApplyMoving(); }
                catch (Exception e) { if (failures++ == 0) Core.Log.Error("[Steps] moving the step failed: " + e); }
                yield return null;
            }
            _loop = null;
        }

        private static void ApplyMoving()
        {
            float e = Ease(_t);
            foreach (Part p in _moving)
            {
                if (p.T == null) continue;
                if (p.UpAxis < 0) { p.T.localPosition = p.LocalPos + p.SlideLocal * e; continue; }
                Vector3 s = p.LocalScale;
                s[p.UpAxis] *= Mathf.Lerp(1f, Folded, e);
                p.T.localScale = s;
                p.T.localPosition = p.LocalPos + p.SlideLocal * e + p.KeepTopLocal * e;
            }
        }

        private static float Ease(float t) => t * t * (3f - 2f * t);

        /// <summary>The step back out where it belongs; the RV goes home.</summary>
        internal static void Release()
        {
            if (_loop != null) { MelonCoroutines.Stop(_loop); _loop = null; }
            int back = 0;
            foreach (Part p in _moving)
            {
                if (p.T == null) continue;
                try { p.T.localPosition = p.LocalPos; p.T.localScale = p.LocalScale; back++; }
                catch (Exception e) { Core.Log.Warning("[Steps] setting the step back failed: " + e.Message); }
            }
            if (_static.Count + _moving.Count > 0) Core.LogDebug("[Steps] released, " + back + " transform(s) set back.");
            _static.Clear();
            _moving.Clear();
            _t = 0f;
            _in = false;
        }

        // The scene is going away with the step in it; only the bookkeeping is cleared.
        internal static void Reset()
        {
            if (_loop != null) { MelonCoroutines.Stop(_loop); _loop = null; }
            _static.Clear();
            _moving.Clear();
            _t = 0f;
            _in = false;
        }
    }
}
