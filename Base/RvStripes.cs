using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using RVRepairVan.Managers;
using UnityEngine;
using UnityEngine.Rendering;

namespace RVRepairVan.Base
{
    /// <summary>
    /// The RV's green stripes and accents, as their own layer for the second paint. The shell is one mesh with one
    /// texture: a grey body, the stripes and the accents in green. A copy of the shell's mesh sits on the shell, and its
    /// material shows only the green texels (a mask read from the shell's texture once, alpha-clipped), so a paint on it
    /// colours the stripes and keeps their edges sharp.
    ///
    /// Stock, the layer's colour has alpha 0 and nothing shows; Chassis' paint copy carries alpha 1.
    /// </summary>
    internal static class RvStripes
    {
        internal const string MaterialName = "RVRepairVan_Stripes";
        private const string LayerName = "RVRepairVan_Stripes";
        private const int MaxSize = 2048;

        // A texel is stripe when its green stands this far (0-255) above its red and blue. The body is grey (-4 to 2),
        // the accents 9 to 19, the stripes 27 to 35.
        internal static int Threshold = 8;

        private static readonly Dictionary<IntPtr, Texture2D> _masks = new Dictionary<IntPtr, Texture2D>();
        // The model the layer failed on; it is not tried again until the scene changes.
        private static IntPtr _failedFor;

        internal static void Reset() { _masks.Clear(); _failedFor = IntPtr.Zero; }

        /// <summary>The layer's renderer on the intact model, built on first use. Null when the shell is not found.</summary>
        internal static Renderer Ensure(Transform model)
        {
            try
            {
                Transform main = model != null ? model.Find("rv/Main") : null;
                if (main == null) { Core.LogDebug("[Stripes] 'rv/Main' not found - no stripe layer."); return null; }
                Transform have = main.Find(LayerName);
                if (have != null) return have.GetComponent<Renderer>();
                if (_failedFor == main.Pointer) return null;
                _failedFor = main.Pointer;

                var shell = main.GetComponent<MeshRenderer>();
                var mf = main.GetComponent<MeshFilter>();
                if (shell == null || mf == null || mf.sharedMesh == null) { Core.Log.Warning("[Stripes] the shell has no mesh - the second paint has no stripes to colour."); return null; }
                if (shell.isPartOfStaticBatch) { Core.Log.Warning("[Stripes] the shell is static-batched - its mesh cannot be copied, no stripe layer."); return null; }
                var mats = shell.sharedMaterials;
                Material src = mats.Length > 0 ? mats[0] : null;
                Texture tex = src != null && src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : null;
                if (tex == null) { Core.Log.Warning("[Stripes] the shell's material has no texture - no stripe layer."); return null; }
                Texture2D mask = Mask(tex);
                if (mask == null) return null;

                var go = new GameObject(LayerName);
                go.layer = main.gameObject.layer;
                go.transform.SetParent(main, false);
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = go.AddComponent<MeshRenderer>();
                // One material: only the shell's first submesh (the painted body) gets the layer.
                r.sharedMaterials = new Material[] { Layer(src, mask) };
                r.shadowCastingMode = ShadowCastingMode.Off;
                _failedFor = IntPtr.Zero;
                Core.Log.Msg("[Stripes] stripe layer built on the shell (" + mask.width + "x" + mask.height + " mask).");
                return r;
            }
            catch (Exception e) { Core.Log.Error("[Stripes] building the stripe layer failed - the second paint shows nowhere: " + e); return null; }
        }

        // The shell's material with the mask as its texture: white where the stripes are, clipped everywhere else.
        private static Material Layer(Material src, Texture2D mask)
        {
            var m = new Material(src) { name = MaterialName };
            m.SetTexture("_BaseMap", mask);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", mask);
            Color stock = new Color(1f, 1f, 1f, 0f);
            m.color = stock;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", stock);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            return m;
        }

        /// <summary>The stripe mask of a texture, read on the GPU (the game's textures are not CPU-readable).</summary>
        private static Texture2D Mask(Texture tex)
        {
            if (_masks.TryGetValue(tex.Pointer, out var have) && have != null) return have;
            int w = Math.Min(tex.width, MaxSize), h = Math.Min(tex.height, MaxSize);
            byte[] px = Read(tex, w, h);
            if (px == null) return null;
            var hit = new bool[w * h];
            for (int i = 0; i < hit.Length; i++)
            {
                int r = px[i * 4], g = px[i * 4 + 1], b = px[i * 4 + 2];
                hit[i] = g - Math.Max(r, b) >= Threshold;
            }
            // Single texels of the grey body's grooves reach the threshold: opening the mask (shrink, then grow by one
            // texel) drops them. The accents carry dark grooves under it: closing (grow, then shrink) fills those. Both
            // keep the outer edges where they are.
            hit = Grow(Grow(hit, w, h, false), w, h, true);
            hit = Grow(Grow(hit, w, h, true), w, h, false);
            var mask = new byte[px.Length];
            int on = 0;
            for (int i = 0; i < hit.Length; i++)
            {
                mask[i * 4] = mask[i * 4 + 1] = mask[i * 4 + 2] = 255;
                mask[i * 4 + 3] = hit[i] ? (byte)255 : (byte)0;
                if (hit[i]) on++;
            }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = MaterialName + "_Mask", wrapMode = tex.wrapMode, filterMode = FilterMode.Bilinear };
            // A texture with mipmaps takes every level in one block.
            t.LoadRawTextureData(new Il2CppStructArray<byte>(WithMips(mask, w, h, t.mipmapCount)));
            t.Apply(false, true);
            _masks[tex.Pointer] = t;
            Core.Log.Msg("[Stripes] mask of '" + tex.name + "': " + (100f * on / (px.Length / 4)).ToString("F1") + "% of the texture is stripe (threshold " + Threshold + ").");
            return t;
        }

        // One texel of growth (any neighbour set) or shrink (all neighbours set) over the 3x3 block.
        private static bool[] Grow(bool[] src, int w, int h, bool grow)
        {
            var dst = new bool[src.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool v = !grow;
                    for (int dy = -1; dy <= 1 && v != grow; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = Math.Min(w - 1, Math.Max(0, x + dx)), ny = Math.Min(h - 1, Math.Max(0, y + dy));
                            if (src[ny * w + nx] == grow) { v = grow; break; }
                        }
                    dst[y * w + x] = v;
                }
            return dst;
        }

        /// <summary>The level-0 RGBA bytes followed by each smaller level, every texel the average of the four above it.</summary>
        private static byte[] WithMips(byte[] level0, int w, int h, int levels)
        {
            var all = new List<byte[]> { level0 };
            int total = level0.Length;
            byte[] src = level0;
            int sw = w, sh = h;
            for (int l = 1; l < levels; l++)
            {
                int dw = Math.Max(1, sw / 2), dh = Math.Max(1, sh / 2);
                var dst = new byte[dw * dh * 4];
                for (int y = 0; y < dh; y++)
                    for (int x = 0; x < dw; x++)
                    {
                        int x0 = Math.Min(sw - 1, x * 2), x1 = Math.Min(sw - 1, x * 2 + 1);
                        int y0 = Math.Min(sh - 1, y * 2), y1 = Math.Min(sh - 1, y * 2 + 1);
                        for (int c = 0; c < 4; c++)
                            dst[(y * dw + x) * 4 + c] = (byte)((src[(y0 * sw + x0) * 4 + c] + src[(y0 * sw + x1) * 4 + c]
                                + src[(y1 * sw + x0) * 4 + c] + src[(y1 * sw + x1) * 4 + c] + 2) / 4);
                    }
                all.Add(dst);
                total += dst.Length;
                src = dst; sw = dw; sh = dh;
            }
            var block = new byte[total];
            int at = 0;
            foreach (var b in all) { Buffer.BlockCopy(b, 0, block, at, b.Length); at += b.Length; }
            return block;
        }

        /// <summary>The texture's RGBA bytes at the given size, sRGB as stored.</summary>
        internal static byte[] Read(Texture tex, int w, int h)
        {
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prev = RenderTexture.active;
            Texture2D read = null;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                read = new Texture2D(w, h, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                read.Apply();
                var native = read.GetPixels32();
                var bytes = new byte[native.Length * 4];
                // An IL2CPP array's elements follow its header: class, monitor, bounds and length, one pointer each.
                Marshal.Copy(IntPtr.Add(native.Pointer, 4 * IntPtr.Size), bytes, 0, bytes.Length);
                return bytes;
            }
            catch (Exception e)
            {
                Core.Log.Error("[Stripes] reading '" + tex.name + "' failed - no stripe mask: " + e.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (read != null) UnityEngine.Object.Destroy(read);
            }
        }

#if DEBUG
        /// <summary>Rebuild the mask with another threshold; the next paint takes it.</summary>
        internal static void DebugRebuild(int threshold)
        {
            Threshold = threshold;
            Transform model = RVManager.Model;
            Transform layer = model != null ? model.Find("rv/Main/" + LayerName) : null;
            foreach (var kv in _masks) if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            _masks.Clear();
            _failedFor = IntPtr.Zero;
            if (layer != null) UnityEngine.Object.Destroy(layer.gameObject);
            Core.Log.Msg("[Stripes] threshold " + threshold + " - the layer is rebuilt on the next paint.");
        }

        /// <summary>The shell's texture as a PPM file, to measure the stripe colours outside the game.</summary>
        internal static void DebugDumpTexture(string path)
        {
            Transform model = RVManager.Model;
            var shell = model != null ? model.Find("rv/Main")?.GetComponent<Renderer>() : null;
            Material src = shell != null && shell.sharedMaterials.Length > 0 ? shell.sharedMaterials[0] : null;
            Texture tex = src != null ? src.GetTexture("_BaseMap") : null;
            if (tex == null) { Core.Log.Warning("[Stripes] dump: no shell texture."); return; }
            int w = Math.Min(tex.width, MaxSize), h = Math.Min(tex.height, MaxSize);
            byte[] px = Read(tex, w, h);
            if (px == null) return;
            var head = System.Text.Encoding.ASCII.GetBytes("P6\n" + w + " " + h + "\n255\n");
            var file = new byte[head.Length + w * h * 3];
            Buffer.BlockCopy(head, 0, file, 0, head.Length);
            // PPM runs top to bottom, the read bottom to top.
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int s = ((h - 1 - y) * w + x) * 4, d = head.Length + (y * w + x) * 3;
                    file[d] = px[s]; file[d + 1] = px[s + 1]; file[d + 2] = px[s + 2];
                }
            System.IO.File.WriteAllBytes(path, file);
            Core.Log.Msg("[Stripes] '" + tex.name + "' (" + tex.width + "x" + tex.height + ") written to " + path);
        }
#endif
    }
}
