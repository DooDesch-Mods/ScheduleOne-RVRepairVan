using System;
using System.Collections.Generic;
using DooDesch.Localization;
using Il2CppScheduleOne.Vehicles;
using MarcosBodyshop.Api;
using RVRepairVan.Base;
using RVRepairVan.Config;
using RVRepairVan.Managers;
using RVRepairVan.Persistence;

namespace RVRepairVan.Garage
{
    /// <summary>
    /// The RV at Marco's bodyshop. The RV reaches the yard bay on its chassis (the Engine upgrade), so the bodyshop
    /// sees that chassis as the vehicle. Two categories of its own: the furniture part by part, and the workshop
    /// floor. The car categories that change the chassis' own body (paint, looks) are hidden for it: the chassis is
    /// never seen.
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
                // The chassis under the RV is never seen; painting it or changing its lights would change nothing.
                BodyshopApi.Gate("paint", v => IsRv(v) ? "" : null);
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
            ? L10n.T("Park her on the yellow spot out front, then ask me to modify your vehicle.")
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
                int roof = 0, wall = 0;
                foreach (var r in model.GetComponentsInChildren<Renderer>(false))
                {
                    if (r == null || !r.enabled) continue;
                    if (!LocalBox(model, r, out Bounds b)) continue;
                    float across = Vector3.Dot(b.size, side);
                    float along = side == Vector3.right ? b.size.z : b.size.x;
                    bool isRoof = b.min.y - floor > RoofGap;
                    bool isWall = !isRoof && across < WallThickness && along > WallLength && Vector3.Dot(b.center, side) > sideEdge;
                    if (!isRoof && !isWall) continue;
                    r.enabled = false;
                    _hidden.Add(r);
                    if (isRoof) roof++; else wall++;
                    Core.LogDebug("[Garage] hidden " + (isRoof ? "roof" : "wall") + " piece " + r.gameObject.name + " box " + b.min.ToString("F2") + " .. " + b.max.ToString("F2"));
                }
                Core.Log.Msg("[Garage] RV opened up for the camera: " + roof + " roof piece(s), " + wall + " wall piece(s) hidden.");
            }
            catch (Exception e) { Core.Log.Error("[Garage] opening the RV for the camera failed: " + e); ShowShell(); }
        }

        internal static void ShowShell()
        {
            if (_hidden.Count == 0) return;
            int n = 0;
            foreach (var r in _hidden)
                if (r != null) { r.enabled = true; n++; }
            _hidden.Clear();
            Core.LogDebug("[Garage] " + n + " roof and wall piece(s) shown again.");
        }

        private static bool LocalBox(Transform model, Renderer r, out Bounds box)
        {
            box = default;
            Bounds w = r.bounds;
            Vector3 c = w.center, e = w.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(c.x + ((i & 1) == 0 ? -e.x : e.x), c.y + ((i & 2) == 0 ? -e.y : e.y), c.z + ((i & 4) == 0 ? -e.z : e.z));
                Vector3 p = model.InverseTransformPoint(corner);
                if (i == 0) box = new Bounds(p, Vector3.zero); else box.Encapsulate(p);
            }
            return true;
        }
    }
}
