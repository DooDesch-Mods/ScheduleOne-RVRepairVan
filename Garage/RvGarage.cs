using System;
using System.Collections;
using System.Collections.Generic;
using DooDesch.Localization;
using Il2CppScheduleOne.Vehicles;
using MarcosBodyshop.Api;
using MelonLoader;
using RVRepairVan.Base;
using RVRepairVan.Config;
using RVRepairVan.Managers;
using RVRepairVan.Persistence;
using UnityEngine.Rendering;

namespace RVRepairVan.Garage
{
    /// <summary>
    /// The RV at Marco's bodyshop. The RV reaches the yard bay on its chassis (the Engine upgrade), so the bodyshop
    /// sees that chassis as the vehicle. Two categories of its own: the furniture part by part, and the workshop
    /// floor. Paint goes on the RV's body through <see cref="RvPaint"/>; the looks of the chassis are hidden for it,
    /// the chassis is never seen.
    ///
    /// While the interior is open the roof and the wall facing the camera are hidden, and the camera looks down into
    /// the RV from the side.
    /// </summary>
    internal static class RvGarage
    {
        internal const string InteriorCategory = "rvrepairvan.interior";
        internal const string WorkshopCategory = "rvrepairvan.workshop";

        // Anything whose bottom is this far above the cabin floor is roof (panels, hatch, air conditioner).
        private const float RoofGap = 1.9f;
        // A wall piece: thinner than this across the RV, and at least this long.
        private const float WallThickness = 0.5f, WallLength = 2f;

        private static readonly List<Renderer> _hidden = new List<Renderer>();

        internal static void Register()
        {
            try
            {
                BodyshopApi.RegisterCategory(new BodyshopCategory
                {
                    Id = InteriorCategory,
                    Label = L10n.T("Interior"),
                    Icon = "interior",
                    Order = 150,
                    Garage = BodyshopApi.MarcoGarage,
                    AppliesTo = IsRv,
                    Note = _ => L10n.T("{0} of {1} fitted", Fitted(), RvInterior.Parts.Length),
                    Camera = _ => Overview(),
                    Entered = _ => HideShell(),
                    Left = _ => { RvInterior.EndPreview(); ShowShell(); },
                    Entries = InteriorEntries,
                });
                BodyshopApi.RegisterCategory(new BodyshopCategory
                {
                    Id = WorkshopCategory,
                    Label = L10n.T("Workshop"),
                    Icon = "workshop",
                    Order = 160,
                    Garage = BodyshopApi.MarcoGarage,
                    AppliesTo = IsRv,
                    Camera = _ => Overview(),
                    Entered = _ => HideShell(),
                    Left = _ => ShowShell(),
                    Entries = WorkshopEntries,
                });
                // Paint goes on the RV's body (RvPaint names it as the chassis' paint targets). The chassis' own lights,
                // glass and wheels are never seen, so its looks would change nothing.
                BodyshopApi.Gate("looks", v => IsRv(v) ? "" : null);
                Core.Log.Msg("[Garage] RV categories registered at Marco's bodyshop.");
            }
            catch (Exception e) { Core.Log.Error("[Garage] registering the RV at the bodyshop failed - the RV has no garage menu: " + e); }
        }

        internal static bool IsRv(LandVehicle v)
        {
            LandVehicle c = RvMobile.Chassis;
            return v != null && c != null && v.Pointer == c.Pointer;
        }

        /// <summary>Is the RV standing in one of Marco's bays right now?</summary>
        internal static bool InGarage()
        {
            try { return IsRv(BodyshopApi.VehicleIn(BodyshopApi.MarcoGarage, out _)); }
            catch (Exception e) { Core.Swallowed("RvGarage.InGarage", e); return false; }
        }

        // ---- Marco's dialogue ------------------------------------------------------------------------------------

        /// <summary>The "more for the RV" choice: repaired, build-outs on, and the RV not already in his yard.</summary>
        internal static bool AskMoreVisible()
        {
            if (!RVRepairVanPreferences.Enabled || !RVRepairVanPreferences.UpgradesEnabled) return false;
            if (!RepairStateStore.GetRepaired()) return false;
            return !InGarage();
        }

        internal static string AskMoreLine() => RvUpgrades.Owned(RvUpgrade.Engine)
            ? L10n.T("Park her on the lot out front, then ask me to modify your vehicle.")
            : L10n.T("I can't work on her where she sits. Get the engine in first, then drive her over.");

        // ---- interior --------------------------------------------------------------------------------------------

        private static int Fitted()
        {
            int n = 0;
            foreach (var p in RvInterior.Parts) if (!RvInterior.IsRemoved(p)) n++;
            return n;
        }

        private static IEnumerable<BodyshopEntry> InteriorEntries(LandVehicle v)
        {
            foreach (var part in RvInterior.Parts)
            {
                RvPart p = part;
                yield return new BodyshopEntry
                {
                    Id = p.ToString().ToLowerInvariant(),
                    Label = RvInterior.Label(p),
                    Text = RvInterior.IsUnlocked(p) ? L10n.T("Paid for. Taking it out or putting it back is free.") : L10n.T("Pay once, then take it out or put it back for free."),
                    Price = RvInterior.Price(p),
                    Rule = PriceRule.UnlockOnce,
                    Toggle = true,
                    Installed = _ => !RvInterior.IsRemoved(p),
                    Unlocked = _ => RvInterior.IsUnlocked(p),
                    Blocked = _ => RvBuildGrid.RefitBlocked(p),
                    Preview = _ => RvInterior.Preview(p),
                    EndPreview = _ => RvInterior.EndPreview(),
                    Apply = _ => ApplyPart(p),
                    Camera = _ => PartShot(p),
                };
            }
        }

        /// <summary>Host: flip the part and mark it paid for.</summary>
        private static string ApplyPart(RvPart p)
        {
            if (!RepairStateStore.GetRepaired()) return L10n.T("Repair the RV first.");
            string blocked = RvBuildGrid.RefitBlocked(p);
            if (blocked != null) return blocked;
            int unlocked = RvInterior.Unlocked | (int)p;
            int removed = RvInterior.Removed ^ (int)p;
            RvInterior.HostSet(unlocked, removed, p + (RvInterior.IsRemoved(p) ? " put back" : " taken out") + " at the bodyshop");
            return null;
        }

        // ---- workshop ---------------------------------------------------------------------------------------------

        private static IEnumerable<BodyshopEntry> WorkshopEntries(LandVehicle v)
        {
            yield return new BodyshopEntry
            {
                Id = "floor",
                Label = L10n.T("Workshop Floor"),
                Text = L10n.T("Build on every bit of floor the furniture leaves free."),
                Price = RVRepairVanPreferences.PriceWorkshopFloor,
                Installed = _ => RvUpgrades.Owned(RvUpgrade.WorkshopFloor),
                Apply = _ => RvUpgrades.HostGrant(RvUpgrade.WorkshopFloor),
            };
        }

        // ---- camera and shell ---------------------------------------------------------------------------------

        // The RV's long axis and its side, in the model frame, read from the furniture boxes.
        private static bool Frame(out Transform model, out Bounds all, out Vector3 side)
        {
            model = RVManager.Model;
            all = default;
            side = Vector3.right;
            if (model == null) return false;
            bool any = false;
            foreach (var p in RvInterior.Parts)
                if (RvInterior.Box(p, out Bounds b)) { if (!any) { all = b; any = true; } else all.Encapsulate(b); }
            if (!any) return false;
            side = all.size.x < all.size.z ? Vector3.right : Vector3.forward;
            return true;
        }

        private static CameraShot? Overview()
        {
            if (!Frame(out Transform model, out Bounds all, out Vector3 side)) return null;
            Vector3 target = all.center;
            Vector3 eye = target + Vector3.up * 6.5f + side * 3.5f;
            return new CameraShot(model.TransformPoint(eye), model.TransformPoint(target), 55f);
        }

        private static CameraShot? PartShot(RvPart p)
        {
            if (!Frame(out Transform model, out _, out Vector3 side)) return null;
            if (!RvInterior.Box(p, out Bounds b)) return Overview();
            Vector3 target = b.center;
            Vector3 eye = target + Vector3.up * 4f + side * 2.5f;
            return new CameraShot(model.TransformPoint(eye), model.TransformPoint(target), 50f);
        }

        /// <summary>
        /// Hide the roof and the wall on the camera's side. Every renderer of the model is measured in its frame:
        /// above <see cref="RoofGap"/> is roof, a long thin piece on the camera's side is the wall.
        /// </summary>
        private static void HideShell()
        {
            ShowShell();
            try
            {
                if (!Frame(out Transform model, out Bounds all, out Vector3 side)) { Core.Log.Warning("[Garage] the RV's furniture is not measured - the roof stays."); return; }
                float floor = RvInterior.FloorY(model);
                float sideEdge = Vector3.Dot(all.max, side) - 0.2f;
                int roof = 0, wall = 0, shell = 0;
                float length = side == Vector3.right ? all.size.z : all.size.x;
                foreach (var r in model.GetComponentsInChildren<Renderer>(false))
                {
                    if (r == null || !r.enabled) continue;
                    if (!LocalBox(model, r, out Bounds b)) continue;
                    float across = Vector3.Dot(b.size, side);
                    float along = side == Vector3.right ? b.size.z : b.size.x;
                    bool isRoof = b.min.y - floor > RoofGap;
                    bool isWall = !isRoof && across < WallThickness && along > WallLength && Vector3.Dot(b.center, side) > sideEdge;
                    // The body is one mesh from the floor to the roof: it cannot be cut open, so it turns see-through.
                    // It runs most of the RV's length; furniture and partitions (parts the player buys) do not.
                    bool isShell = !isRoof && !isWall && b.min.y - floor < RoofGap && b.max.y - floor > RoofGap
                        && along > ShellShare * length && across > WallThickness;
                    if (isShell) { if (Ghost(r)) shell++; continue; }
                    if (!isRoof && !isWall) continue;
                    r.enabled = false;
                    _hidden.Add(r);
                    if (isRoof) roof++; else wall++;
                    Core.LogDebug("[Garage] hidden " + (isRoof ? "roof" : "wall") + " piece " + r.gameObject.name + " box " + b.min.ToString("F2") + " .. " + b.max.ToString("F2"));
                }
                // The roof hatch is drawn by hand each frame, not by its renderer.
                RvHatch.Hidden = true;
                Core.Log.Msg("[Garage] RV opened up for the camera: " + roof + " roof piece(s), " + wall + " wall piece(s) and the hatch hidden, "
                    + shell + " shell piece(s) see-through.");
            }
            catch (Exception e) { Core.Log.Error("[Garage] opening the RV for the camera failed: " + e); ShowShell(); }
        }

        internal static void ShowShell()
        {
            RvHatch.Hidden = false;
            int solid = 0;
            foreach (var g in _ghosts)
            {
                try
                {
                    if (g.R == null) continue;
                    if (g.Mesh != null) g.R.enabled = true;
                    else
                    {
                        g.R.sharedMaterials = g.Original;
                        g.R.shadowCastingMode = g.Shadows;
                        RvMobile.RefreshStatic(g.R);
                    }
                    solid++;
                }
                catch (Exception e) { Core.Log.Warning("[Garage] giving a shell piece its own materials back failed: " + e.Message); }
                finally { foreach (var m in g.Mine) if (m != null) UnityEngine.Object.Destroy(m); }
            }
            _ghosts.Clear();
            if (_hidden.Count == 0 && solid == 0) return;
            int n = 0;
            foreach (var r in _hidden)
                if (r != null) { r.enabled = true; n++; }
            _hidden.Clear();
            Core.LogDebug("[Garage] " + n + " roof and wall piece(s) shown again, " + solid + " shell piece(s) solid again.");
        }

        // ---- see-through shell ------------------------------------------------------------------------------------

        private const float ShellAlpha = 0.14f;
        // A shell piece is at least this share of the furniture's length.
        private const float ShellShare = 0.6f;

        private sealed class GhostedShell
        {
            internal Renderer R;
            internal Material[] Original, Mine;
            internal ShadowCastingMode Shadows;
            internal Mesh Mesh;     // set when the piece is hidden and drawn here; null when its materials are swapped
            internal int Layer;
        }

        private static readonly List<GhostedShell> _ghosts = new List<GhostedShell>();
        private static Shader _lit;
        private static object _drawGhosts;

        // Transparent URP Lit copies of the piece's materials, its colour and texture kept; no shadow, so the sun reaches
        // the cabin. A static-batched piece is redrawn by RvMobile, which takes the new materials. Any other piece is
        // hidden and drawn here each frame: Chassis puts its paint back on painted slots, so a swap would not hold.
        private static bool Ghost(Renderer r)
        {
            if (_lit == null) _lit = Shader.Find("Universal Render Pipeline/Lit");
            if (_lit == null) { Core.Log.Warning("[Garage] no URP Lit shader - the RV's body stays solid in the interior view."); return false; }
            var g = new GhostedShell { R = r, Original = r.sharedMaterials, Shadows = r.shadowCastingMode, Layer = r.gameObject.layer };
            if (!r.isPartOfStaticBatch)
            {
                var mf = r.GetComponent<MeshFilter>();
                g.Mesh = mf != null ? mf.sharedMesh : null;
                if (g.Mesh == null) { Core.Log.Warning("[Garage] shell piece " + r.gameObject.name + " has no mesh to draw - it stays solid."); return false; }
            }
            g.Mine = new Material[g.Original.Length];
            for (int i = 0; i < g.Mine.Length; i++) g.Mine[i] = SeeThrough(g.Original[i]);
            if (g.Mesh != null) r.enabled = false;
            else
            {
                r.sharedMaterials = g.Mine;
                r.shadowCastingMode = ShadowCastingMode.Off;
                RvMobile.RefreshStatic(r);
            }
            _ghosts.Add(g);
            if (g.Mesh != null && _drawGhosts == null) _drawGhosts = MelonCoroutines.Start(DrawGhosts());
            Core.LogDebug("[Garage] shell piece " + r.gameObject.name + " see-through (" + (g.Mesh != null ? "drawn here" : "materials swapped") + ").");
            return true;
        }

        private static IEnumerator DrawGhosts()
        {
            int failures = 0;
            while (_ghosts.Count > 0)
            {
                foreach (var g in _ghosts)
                {
                    if (g.Mesh == null || g.R == null) continue;
                    Matrix4x4 m = g.R.transform.localToWorldMatrix;
                    for (int i = 0; i < g.Mine.Length && i < g.Mesh.subMeshCount; i++)
                    {
                        try { Graphics.DrawMesh(g.Mesh, m, g.Mine[i], g.Layer, null, i, null, false, true); }
                        catch (Exception e) { if (failures++ == 0) Core.Log.Error("[Garage] drawing the see-through shell failed: " + e.Message); }
                    }
                }
                yield return null;
            }
            _drawGhosts = null;
        }

        private static Material SeeThrough(Material src)
        {
            var m = new Material(_lit) { name = "RVRepairVan_Shell" };
            if (src != null)
            {
                if (src.HasProperty("_BaseMap") && src.GetTexture("_BaseMap") != null) m.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
                else if (src.HasProperty("_MainTex") && src.GetTexture("_MainTex") != null) m.SetTexture("_BaseMap", src.GetTexture("_MainTex"));
                Color c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
                c.a *= ShellAlpha;
                m.SetColor("_BaseColor", c);
            }
            else m.SetColor("_BaseColor", new Color(1f, 1f, 1f, ShellAlpha));
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        private static bool LocalBox(Transform model, Renderer r, out Bounds box)
        {
            box = default;
            Bounds w = r.bounds;
            // A renderer without a mesh reports an empty box at the world origin.
            if (w.size.sqrMagnitude < 1e-6f) { Core.LogDebug("[Garage] " + r.gameObject.name + " has an empty box - neither roof nor wall."); return false; }
            Vector3 c = w.center, e = w.extents;
            Matrix4x4 toModel = RvMobile.WorldToModel(r, model);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(c.x + ((i & 1) == 0 ? -e.x : e.x), c.y + ((i & 2) == 0 ? -e.y : e.y), c.z + ((i & 4) == 0 ? -e.z : e.z));
                Vector3 p = toModel.MultiplyPoint3x4(corner);
                if (i == 0) box = new Bounds(p, Vector3.zero); else box.Encapsulate(p);
            }
            return true;
        }
    }
}
