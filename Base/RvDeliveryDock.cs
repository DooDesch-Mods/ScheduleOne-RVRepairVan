using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.Property;
using RVRepairVan.Managers;

namespace RVRepairVan.Base
{
    /// <summary>
    /// Upgrade 4 - "loading dock". Lets suppliers deliver to the RV.
    ///
    /// Two independent gates, both of which have to open:
    ///
    /// 1. <c>RV.CanDeliverToProperty()</c> is overridden to false. That is the real filter - the destination list
    ///    is <c>OwnedProperties.Where(x => x.CanDeliverToProperty())</c> in DeliveryShop. (The competing mod
    ///    instead scans a whole assembly for anything named GetPotentialDestinations, which is both the wrong
    ///    hook and a process-killer on IL2CPP.)
    /// 2. <c>LoadingDockCount == 0</c> blocks the order with "Selected destination has no loading docks", so an
    ///    actual dock has to exist. A LoadingDock needs a ParkingLot, a VehicleDetector and access points, which
    ///    is far more than can sensibly be built by hand - so clone one of the Manor's.
    /// </summary>
    internal static class RvDeliveryDock
    {
        private const string DockName = "RVRepairVan_LoadingDock";

        // Where the delivery van should park, relative to the RV.
        private static readonly Vector3 DockLocal = new Vector3(1.0f, 0.0f, 11.5f);
        private static readonly Quaternion DockRot = Quaternion.Euler(0f, 180f, 0f);

        private static readonly string[] TemplatePaths =
        {
            "@Properties/Manor/Loading Docks/Loading Dock",
            "@Properties/Manor/Loading Docks/Loading Dock (1)",
            "@Properties/Manor/Loading Docks/Loading Dock (2)",
        };

        /// <summary>True once the dock is attached - read by the CanDeliverToProperty patch below.</summary>
        internal static bool DockReady { get; private set; }

        private static bool _applied;

        internal static void Reset() { _applied = false; DockReady = false; Deferred.Clear(); }

#if DEBUG
        /// <summary>Debug (rvdeliver guard off): lets the vanilla activation run unguarded, to reproduce the original failure.</summary>
        internal static bool DebugGuardOff;
#endif

        // Deliveries to the RV whose activation had to wait for the dock (a save that is loaded with one open).
        private static readonly List<DeliveryInstance> Deferred = new List<DeliveryInstance>();

        /// <summary>
        /// True when the delivery targets the RV but the RV has no dock at its index yet. Tries to attach the
        /// dock right now; if that is not possible the delivery is parked and activated by <see cref="Apply"/>.
        /// </summary>
        internal static bool MustDefer(DeliveryInstance inst)
        {
            try
            {
#if DEBUG
                if (DebugGuardOff) { Core.Log.Msg("[Debug] dock guard is off - vanilla activation runs unguarded."); return false; }
#endif
                Transform root = RVManager.Root;
                Property rv = root != null ? root.GetComponent<Property>() : null;
                if (rv == null || inst.DestinationCode != rv.PropertyCode) return false;
                if (HasDockAt(rv, inst.LoadingDockIndex)) { Core.Log.Msg("[Dock] delivery " + inst.DeliveryID + " activates at RV dock " + inst.LoadingDockIndex + "."); return false; }

                if (RvUpgrades.Owned(RvUpgrade.LoadingDock)) Apply();
                if (HasDockAt(rv, inst.LoadingDockIndex)) { Core.Log.Msg("[Dock] dock attached before delivery " + inst.DeliveryID + " activated."); return false; }

                if (!Deferred.Contains(inst)) Deferred.Add(inst);
                Core.Log.Warning("[Dock] delivery " + inst.DeliveryID + " to the RV has no loading dock yet - activation deferred until the dock is attached.");
                return true;
            }
            catch (Exception e) { Core.Log.Error("[Dock] MustDefer failed: " + e.Message); return false; }
        }

        private static bool HasDockAt(Property rv, int index)
        {
            return rv.LoadingDocks != null && index >= 0 && index < rv.LoadingDocks.Length && rv.LoadingDocks[index] != null;
        }

        private static void ActivateDeferred()
        {
            if (Deferred.Count == 0) return;
            DeliveryInstance[] pending = Deferred.ToArray();
            Deferred.Clear();
            foreach (DeliveryInstance inst in pending)
            {
                try
                {
                    inst.SetStatus(inst.Status);   // runs the vanilla activation now that the dock exists
                    Core.Log.Msg("[Dock] activated deferred delivery " + inst.DeliveryID + " (" + inst.Status + ").");
                }
                catch (Exception e) { Core.Log.Error("[Dock] deferred activation of " + inst.DeliveryID + " failed: " + e.Message); }
            }
        }

        internal static void Apply()
        {
            if (_applied) return;
            LoadingDock dock = null;
            try
            {
                Transform root = RVManager.Root;
                Property prop = root != null ? root.GetComponent<Property>() : null;
                if (prop == null) { Core.LogDebug("[Dock] RV property not ready - will retry on the next apply."); return; }

                // Already attached (e.g. a second apply in the same session)?
                if (HasOurDock(prop)) { _applied = true; DockReady = true; return; }

                LoadingDock template = FindTemplate();
                if (template == null)
                {
                    // Deliberately do NOT set _applied here: without the template this is retryable, and the
                    // competing mod's bug is exactly that it latches the "created" flag before it can fail.
                    Core.Log.Warning("[Dock] no LoadingDock template found - retrying on the next apply.");
                    return;
                }

                dock = UnityEngine.Object.Instantiate(template, root.TransformPoint(DockLocal), root.rotation * DockRot);
                if (dock == null) { Core.Log.Warning("[Dock] instantiate failed - retrying on the next apply."); return; }

                // Cloning a live scene entity copies its baked GUID, and Awake has already run by the time we get
                // the reference back - so the clone has taken over the Manor dock's entry in GUIDManager
                // (RegisterObject replaces an existing mapping). Left alone, anything resolving the Manor's dock
                // or parking lot would get the RV's copy instead. Give the clone fresh GUIDs and hand the
                // originals their registrations back.
                RepairClonedGuids(dock, template);

                dock.name = DockName;
                dock.ParentProperty = prop;
                dock.transform.SetParent(root, true);

                var list = new List<LoadingDock>();
                if (prop.LoadingDocks != null)
                    foreach (LoadingDock d in prop.LoadingDocks)
                        if (d != null) list.Add(d);
                list.Add(dock);
                prop.LoadingDocks = list.ToArray();   // implicit conversion to Il2CppReferenceArray

                _applied = true;
                DockReady = true;
                dock = null;   // handed over to the property - no longer ours to clean up
                Core.Log.Msg("[Dock] loading dock attached (" + list.Count + " total on the RV).");
                ActivateDeferred();
            }
            catch (Exception e) { Core.Log.Warning("[Dock] apply failed: " + e.Message); }
            finally
            {
                // A clone that never made it onto the property would otherwise linger in the scene - and each
                // retry would add another one, with another set of GUID registrations to fight over.
                if (dock != null)
                {
                    try { UnityEngine.Object.Destroy(dock.gameObject); } catch (Exception e) { Core.Swallowed("RvDeliveryDock.Apply", e); }
                    Core.LogDebug("[Dock] discarded an unattached dock clone.");
                }
            }
        }

        /// <summary>
        /// Undo the GUID theft an Instantiate of a live scene entity causes: give the clone's registerables fresh
        /// GUIDs, then re-register the originals so their mappings point back at the real Manor objects.
        /// Covers the dock itself and its parking lot - the two things under a LoadingDock that register a GUID.
        /// </summary>
        private static void RepairClonedGuids(LoadingDock clone, LoadingDock original)
        {
            try
            {
                ReassignGuid(clone, original);

                var cloneLots = clone.GetComponentsInChildren<Il2CppScheduleOne.Map.ParkingLot>(true);
                var origLots = original.GetComponentsInChildren<Il2CppScheduleOne.Map.ParkingLot>(true);
                if (cloneLots != null)
                    for (int i = 0; i < cloneLots.Length; i++)
                    {
                        var origLot = (origLots != null && i < origLots.Length) ? origLots[i] : null;
                        ReassignGuid(cloneLots[i], origLot);
                    }
            }
            catch (Exception e) { Core.Log.Warning("[Dock] GUID repair failed: " + e.Message); }
        }

        private static void ReassignGuid(Component cloneComp, Component originalComp)
        {
            try
            {
                var clone = cloneComp != null ? cloneComp.TryCast<Il2CppScheduleOne.IGUIDRegisterable>() : null;
                if (clone == null) return;

                Il2Cpp.GUIDManager.DeregisterObject(clone);
                clone.SetGUID(Il2Cpp.GUIDManager.GenerateUniqueGUID());
                Il2Cpp.GUIDManager.RegisterObject(clone, null);

                // The original lost its entry when the clone's Awake registered the copied GUID - put it back.
                var orig = originalComp != null ? originalComp.TryCast<Il2CppScheduleOne.IGUIDRegisterable>() : null;
                if (orig != null) Il2Cpp.GUIDManager.RegisterObject(orig, null);
            }
            catch (Exception e) { Core.Swallowed("RvDeliveryDock.ReassignGuid", e); }
        }

        /// <summary>The dock this mod attached to the RV, or null.</summary>
        internal static LoadingDock OurDock(Property prop)
        {
            try
            {
                if (prop == null || prop.LoadingDocks == null) return null;
                foreach (LoadingDock d in prop.LoadingDocks)
                    if (d != null && d.name == DockName) return d;
            }
            catch (Exception e) { Core.Log.Warning("[Dock] dock lookup failed: " + e.Message); }
            return null;
        }

        private static bool HasOurDock(Property prop)
        {
            try
            {
                if (prop.LoadingDocks == null) return false;
                foreach (LoadingDock d in prop.LoadingDocks)
                    if (d != null && d.name == DockName) return true;
            }
            catch (Exception e) { Core.Swallowed("RvDeliveryDock.HasOurDock", e); }
            return false;
        }

        private static LoadingDock FindTemplate()
        {
            foreach (string path in TemplatePaths)
            {
                try
                {
                    GameObject go = GameObject.Find(path);
                    LoadingDock d = go != null ? go.GetComponent<LoadingDock>() : null;
                    if (d != null) return d;
                }
                catch (Exception e) { Core.Swallowed("RvDeliveryDock.FindTemplate.path", e); }
            }
            // Fall back to any dock in the scene: the Manor paths are the likely ones, but a renamed hierarchy
            // should degrade to "some working dock" rather than to no delivery at all.
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<LoadingDock>();
                if (all != null)
                    foreach (LoadingDock d in all)
                        if (d != null && d.name != DockName) return d;
            }
            catch (Exception e) { Core.Swallowed("RvDeliveryDock.FindTemplate.anyDock", e); }
            return null;
        }
    }

    /// <summary>
    /// Put the RV into the delivery destination list.
    ///
    /// The obvious hook is <c>RV.CanDeliverToProperty()</c>, which returns false - but that override has no managed
    /// body in IL2CPP, it is a generated interop stub. Harmony rebuilds it into a dynamic method that then throws
    /// a NullReferenceException on EVERY call, and the game calls it constantly: a first attempt flooded the log
    /// with thousands of "During invoking native->managed trampoline" errors and broke loading a save.
    ///
    /// <c>DeliveryShop.GetPotentialDestinations()</c> is a real method with a real body and is the single place
    /// the list is built, so append to its result instead. Targeted at this one method on this one type - not the
    /// competing mod's assembly-wide name scan, which also runs <c>Assembly.GetTypes()</c> over the interop
    /// assembly and kills the process.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppScheduleOne.UI.Phone.Delivery.DeliveryShop), "GetPotentialDestinations")]
    internal static class Rv_GetPotentialDestinations_Patch
    {
        private static void Postfix(Il2CppSystem.Collections.Generic.List<Property> __result)
        {
            try
            {
                if (!RvDeliveryDock.DockReady || __result == null) return;

                Transform root = RVManager.Root;
                Property rv = root != null ? root.GetComponent<Property>() : null;
                if (rv == null || !rv.IsOwned || rv.LoadingDockCount <= 0) return;

                for (int i = 0; i < __result.Count; i++)
                    if (__result[i] != null && __result[i].PropertyCode == rv.PropertyCode) return;   // already there

                __result.Add(rv);
            }
            catch (Exception e) { Core.Swallowed("RvDeliveryDock.DestinationPatch", e); }
        }
    }

    /// <summary>
    /// A delivery restored from a save goes through <c>SetStatus(Arrived)</c> and vanilla indexes the destination's
    /// <c>LoadingDocks</c> with it. The RV only gets its dock a few seconds later, so that throws
    /// IndexOutOfRangeException and leaves the van half activated. Attach the dock first or hold the activation
    /// back until it exists.
    /// </summary>
    [HarmonyPatch(typeof(DeliveryInstance), nameof(DeliveryInstance.SetStatus))]
    internal static class Rv_DeliverySetStatus_Patch
    {
        private static bool Prefix(DeliveryInstance __instance, EDeliveryStatus status)
        {
            try
            {
                if (status != EDeliveryStatus.Arrived || !RvDeliveryDock.MustDefer(__instance)) return true;
                __instance.Status = status;
                return false;
            }
            catch (Exception e) { Core.Log.Error("[Dock] SetStatus guard failed: " + e.Message); return true; }
        }
    }

    /// <summary>The bay check indexes LoadingDocks too; with no dock yet the bay counts as busy instead of throwing.</summary>
    [HarmonyPatch(typeof(DeliveryManager), nameof(DeliveryManager.IsLoadingBayFree))]
    internal static class Rv_IsLoadingBayFree_Patch
    {
        private static bool Prefix(Property destination, int loadingDockIndex, ref bool __result)
        {
            try
            {
                if (destination == null || (destination.LoadingDocks != null && loadingDockIndex >= 0 && loadingDockIndex < destination.LoadingDocks.Length)) return true;
                Core.Log.Warning("[Dock] bay " + loadingDockIndex + " of " + destination.PropertyCode + " does not exist yet - treated as busy.");
                __result = false;
                return false;
            }
            catch (Exception e) { Core.Log.Error("[Dock] IsLoadingBayFree guard failed: " + e.Message); return true; }
        }
    }
}
