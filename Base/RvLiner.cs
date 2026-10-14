using System;
using RVRepairVan.Managers;

namespace RVRepairVan.Base
{
    /// <summary>
    /// Closes the holes in the RV's interior shell: the open front of the step up to the cab, the gaps between the
    /// dash and the cab walls (both show the outside), and the sunlight that leaks through the rear corner joints and
    /// the wall foot, which the bed and the cabinets used to cover. Visual only - plain boxes without colliders, so
    /// walking, building and the NavMesh are unchanged.
    ///
    /// The boxes sit under <c>rv/Main</c>, whose local frame is x along the RV (front +x), y across (door side +y)
    /// and z up, so they travel with the model when it is driven. Their colour is the dominant colour of the texture
    /// of the surface they cover, because a cube's UVs would smear a whole texture atlas over a 10 cm strip.
    /// </summary>
    internal static class RvLiner
    {
        private const string LinerName = "RVRepairVan_Liner";

        // Shadow: never drawn, only casts shadows. It may stick out of the body, which is what lets it close a joint
        // the sun gets through.
        private enum Surface { Wall, Floor, Shadow }

        // name, centre, size, surface - in rv/Main space.
        private static readonly (string name, Vector3 c, Vector3 s, Surface m)[] Pieces =
        {
            ("StepRiser",  new Vector3(3.46f, 0f, -1.30f),     new Vector3(0.10f, 3.10f, 0.60f), Surface.Floor),
            // Cab side walls up to the top of the dash: the gap between the dash's side panel and the wall shows the
            // outside of the body.
            ("CabPanel_N", new Vector3(4.40f, -1.56f, -0.625f), new Vector3(1.95f, 0.10f, 1.25f), Surface.Wall),
            ("CabPanel_P", new Vector3(4.40f, 1.56f, -0.625f),  new Vector3(1.95f, 0.10f, 1.25f), Surface.Wall),
            // Rear corners: the shell is closed outside, but the sun leaks through the corner joint onto the side
            // walls. A post thick enough to cast its own shadow over the joint.
            ("Post_N",     new Vector3(-5.60f, -1.465f, 0.15f), new Vector3(0.30f, 0.22f, 2.70f), Surface.Wall),
            ("Post_P",     new Vector3(-5.60f, 1.465f, 0.15f),  new Vector3(0.30f, 0.22f, 2.70f), Surface.Wall),
            // Skirting along the wall foot, reaching into the wall so it also shades the joint under it. The door
            // side leaves the doorway (x 1.45 to 2.60) open.
            ("Skirt_N",    new Vector3(-1.125f, -1.56f, -1.10f), new Vector3(9.15f, 0.12f, 0.30f), Surface.Wall),
            ("Skirt_P1",   new Vector3(-2.125f, 1.56f, -1.10f),  new Vector3(7.15f, 0.12f, 0.30f), Surface.Wall),
            ("Skirt_P2",   new Vector3(3.025f, 1.56f, -1.10f),   new Vector3(0.85f, 0.12f, 0.30f), Surface.Wall),
            // Shadow casters over the outside of the same joints, below the windows so no window light is lost.
            ("Shade_CornerN", new Vector3(-5.95f, -1.45f, 0.275f), new Vector3(0.90f, 0.90f, 2.95f), Surface.Shadow),
            ("Shade_CornerP", new Vector3(-5.95f, 1.45f, 0.275f),  new Vector3(0.90f, 0.90f, 2.95f), Surface.Shadow),
            ("Shade_FootN",   new Vector3(-1.125f, -1.70f, -1.05f), new Vector3(9.15f, 0.30f, 0.70f), Surface.Shadow),
            ("Shade_FootP1",  new Vector3(-2.125f, 1.70f, -1.05f),  new Vector3(7.15f, 0.30f, 0.70f), Surface.Shadow),
            ("Shade_FootP2",  new Vector3(3.025f, 1.70f, -1.05f),   new Vector3(0.85f, 0.30f, 0.70f), Surface.Shadow),
        };

        /// <summary>Build the liner under the current RV model once; a second call finds it and returns.</summary>
        internal static void Ensure()
        {
            try
            {
                Transform model = RVManager.Model;
                if (model == null) { Core.LogDebug("[Liner] RV model not found yet - will retry on the next apply."); return; }
                Transform main = model.Find("rv/Main");
                if (main == null) { Core.Log.Warning("[Liner] 'rv/Main' not found under the RV model - interior gaps stay open."); return; }
                if (main.Find(LinerName) != null) return;

                Material wall = Flat(main.Find("Interior"), "wall");
                Material floor = Flat(main.Find("Floor"), "floor");
                if (wall == null || floor == null) { Core.Log.Warning("[Liner] no source material - interior gaps stay open."); return; }

                var root = new GameObject(LinerName);
                root.layer = main.gameObject.layer;
                root.transform.SetParent(main, false);
                foreach (var p in Pieces)
                {
                    GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    box.name = p.name;
                    box.layer = root.layer;
                    UnityEngine.Object.Destroy(box.GetComponent<Collider>());
                    box.transform.SetParent(root.transform, false);
                    box.transform.localPosition = p.c;
                    box.transform.localScale = p.s;
                    MeshRenderer mr = box.GetComponent<MeshRenderer>();
                    mr.sharedMaterial = p.m == Surface.Floor ? floor : wall;
                    if (p.m == Surface.Shadow) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                }
                Core.Log.Msg("[Liner] " + Pieces.Length + " filler(s) placed in the RV interior.");
            }
            catch (Exception e) { Core.Log.Error("[Liner] building the interior filler failed: " + e); }
        }

        // The source material without its textures, tinted with the texture's most common colour.
        private static Material Flat(Transform source, string label)
        {
            Renderer r = source != null ? source.GetComponent<Renderer>() : null;
            Material src = r != null ? r.sharedMaterial : null;
            if (src == null) { Core.Log.Warning("[Liner] " + label + ": no renderer/material on " + (source != null ? source.name : "(missing)") + "."); return null; }

            Color tint = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.color;
            Color dominant = DominantColour(src.mainTexture, label);
            var m = new Material(src) { name = "RVRepairVan_Liner_" + label };
            foreach (string tex in new[] { "_BaseMap", "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" })
                if (m.HasProperty(tex)) m.SetTexture(tex, null);
            m.DisableKeyword("_NORMALMAP");
            Color c = new Color(tint.r * dominant.r, tint.g * dominant.g, tint.b * dominant.b, 1f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            Core.Log.Msg("[Liner] " + label + " colour " + c + " (texture " + dominant + " x tint " + tint + ")");
            return m;
        }

        // Downsample the texture on the GPU (the game's textures are not CPU-readable) and take the biggest colour
        // bin. An average would mix in every other part sharing the atlas; the largest surface wins the vote.
        private static Color DominantColour(Texture tex, string label)
        {
            if (tex == null) return Color.white;
            const int N = 64;
            RenderTexture rt = RenderTexture.GetTemporary(N, N, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prev = RenderTexture.active;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var read = new Texture2D(N, N, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, N, N), 0, 0);
                read.Apply();
                var px = read.GetPixels32();
                UnityEngine.Object.Destroy(read);

                var count = new int[512];
                var sum = new Vector3[512];
                foreach (Color32 c in px)
                {
                    int k = (c.r >> 5) * 64 + (c.g >> 5) * 8 + (c.b >> 5);
                    count[k]++;
                    sum[k] += new Vector3(c.r, c.g, c.b);
                }
                int best = 0;
                for (int i = 1; i < count.Length; i++) if (count[i] > count[best]) best = i;
                Vector3 avg = sum[best] / (count[best] * 255f);
                Core.LogDebug("[Liner] " + label + " texture '" + tex.name + "': top bin holds " + count[best] + " of " + (N * N) + " samples");
                return new Color(avg.x, avg.y, avg.z, 1f);
            }
            catch (Exception e)
            {
                Core.Log.Warning("[Liner] " + label + ": could not sample '" + tex.name + "' (" + e.Message + ") - using the plain tint.");
                return Color.white;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
