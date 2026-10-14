using System;
using System.Collections.Generic;
using DooDesch.Localization;
using HarmonyLib;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Vehicles;
using RVRepairVan.Managers;
using NpcBehaviour = Il2CppScheduleOne.NPCs.Behaviour.Behaviour;

namespace RVRepairVan.Base
{
    /// <summary>
    /// What the base does while the RV is driven away from its spot (<see cref="RvMobile"/>). Its stations
    /// travel with it, but the property itself - bounding box, idle points, the crew's NavMesh, the loading
    /// dock - stays at home. So the crew stops and waits next to the empty spot, and deliveries to the RV's
    /// dock wait until it is back. Host-side: employee AI and delivery arrival are both host-authoritative.
    /// </summary>
    internal static class RvAway
    {
        // Where the crew waits: a row on the ground beside the RV's spot, in RV-root space. The cabin idle
        // points hang in the air once the floor has left.
        private static readonly Vector3 WaitRowStart = new Vector3(1.2f, 0f, 4.8f);
        private static readonly Vector3 WaitRowStep = new Vector3(1.1f, 0f, 0f);

        private static readonly List<GameObject> _waitPoints = new List<GameObject>();
        private static LoadingDock _heldDock;

        /// <summary>The RV left its spot: park the crew outside and hold the dock. Host or offline only.</summary>
        internal static void Begin(Property prop, LandVehicle chassis)
        {
            try
            {
                SetCulling(prop, false);
                if (!Net.NetworkBus.IsServer && Net.NetworkBus.Online) return;
                HoldDock(prop, chassis);
                SendCrewOutside(prop);
            }
            catch (Exception e) { Core.Log.Error("[Away] begin failed: " + e); }
        }

        /// <summary>The RV is back on its spot: release the dock and send the crew back in.</summary>
        internal static void End(Property prop)
        {
            try
            {
                SetCulling(prop, true);
                if (!Net.NetworkBus.IsServer && Net.NetworkBus.Online) return;
                ReleaseDock();
                CallCrewBack(prop);
            }
            catch (Exception e) { Core.Log.Error("[Away] end failed: " + e); }
        }

        // Property.UpdateCulling measures the camera against the property ROOT, which stays home. Driven more than
        // 50 m away, every station on board would disappear while the player stands next to it.
        private static void SetCulling(Property prop, bool enabled)
        {
            if (prop == null) return;
            prop.ContentCullingEnabled = enabled;
            if (!enabled) prop.SetContentCulled(false);
            Core.Log.Msg("[Away] content culling " + (enabled ? "back on" : "off while the RV is away") + ".");
        }

        // --- deliveries -----------------------------------------------------------------------------------

        // DeliveryManager only lands a delivery on a free dock (LoadingDock.IsInUse is false); otherwise it sets
        // the delivery to Waiting and checks again every game minute. Naming the RV's chassis as the dock's
        // static occupant uses exactly that. The chassis stays IsVisible (its renderers are only forced off), so
        // LoadingDock.RefreshOccupant does not clear it.
        private static void HoldDock(Property prop, LandVehicle chassis)
        {
            LoadingDock dock = RvDeliveryDock.OurDock(prop);
            if (dock == null) { Core.Log.Msg("[Away] no loading dock on the RV - nothing to hold."); return; }
            if (dock.StaticOccupant != null && dock.StaticOccupant != chassis)
            {
                Core.Log.Msg("[Away] a delivery is unloading at the RV dock - it can finish, new ones wait from the next one.");
                return;
            }
            dock.SetStaticOccupant(chassis);
            _heldDock = dock;
            Core.Log.Msg("[Away] RV dock held - deliveries wait until the RV is back.");
        }

        private static void ReleaseDock()
        {
            if (_heldDock == null) return;
            try { _heldDock.SetStaticOccupant(null); Core.Log.Msg("[Away] RV dock free again."); }
            catch (Exception e) { Core.Log.Warning("[Away] releasing the dock failed: " + e.Message); }
            _heldDock = null;
        }

        /// <summary>Re-hold after a delivery that was already unloading has left the dock.</summary>
        internal static void KeepDockHeld(Property prop, LandVehicle chassis)
        {
            if (_heldDock != null || chassis == null) return;
            LoadingDock dock = RvDeliveryDock.OurDock(prop);
            if (dock != null && dock.StaticOccupant == null) HoldDock(prop, chassis);
        }

        // --- crew -----------------------------------------------------------------------------------------

        private static void SendCrewOutside(Property prop)
        {
            if (prop == null || prop.Employees == null || prop.Employees.Count == 0) return;
            Transform root = prop.transform;
            int moved = 0;
            for (int i = 0; i < prop.Employees.Count; i++)
            {
                Employee emp = prop.Employees[i];
                if (emp == null) continue;
                Transform spot = WaitPoint(root, i);
                try
                {
                    StopWork(emp);
                    emp.WaitOutside.IdlePoint = spot;
                    emp.SetWaitOutside(true);
                    emp.Movement.Warp(spot.position);
                    emp.SubmitNoWorkReason(L10n.T("The RV is away."), L10n.T("Drive it back to its spot."), 10);
                    moved++;
                }
                catch (Exception e) { Core.Log.Warning("[Away] crew member " + i + " could not be parked: " + e.Message); }
            }
            Core.Log.Msg("[Away] " + moved + " crew member(s) wait beside the RV's spot.");
        }

        private static void CallCrewBack(Property prop)
        {
            if (prop == null || prop.Employees == null) return;
            int back = 0;
            for (int i = 0; i < prop.Employees.Count; i++)
            {
                Employee emp = prop.Employees[i];
                if (emp == null) continue;
                try
                {
                    int idx = emp.EmployeeIndex;
                    var points = prop.EmployeeIdlePoints;
                    if (points != null && idx >= 0 && idx < points.Length) emp.WaitOutside.IdlePoint = points[idx];
                    emp.SetWaitOutside(false);
                    back++;
                }
                catch (Exception e) { Core.Log.Warning("[Away] crew member " + i + " could not be called back: " + e.Message); }
            }
            Core.Log.Msg("[Away] " + back + " crew member(s) back to work.");
        }

        private static Transform WaitPoint(Transform root, int i)
        {
            while (_waitPoints.Count <= i) _waitPoints.Add(null);
            GameObject go = _waitPoints[i];
            if (go == null)
            {
                go = new GameObject("RVRepairVan_WaitPoint_" + i);
                go.transform.SetParent(root, false);
                _waitPoints[i] = go;
            }
            go.transform.localPosition = WaitRowStart + WaitRowStep * i;
            go.transform.localRotation = Quaternion.identity;
            return go.transform;
        }

        /// <summary>
        /// Stop whatever task the employee is doing. WaitOutside has the lowest priority, so enabling it alone never
        /// interrupts a running task - the work behaviours have to be switched off themselves.
        /// </summary>
        internal static int StopWork(Employee emp)
        {
            int stopped = 0;
            foreach (NpcBehaviour b in WorkBehaviours(emp))
            {
                if (b == null || !b.Enabled) continue;
                b.Disable_Networked(null);
                stopped++;
            }
            return stopped;
        }

        private static IEnumerable<NpcBehaviour> WorkBehaviours(Employee emp)
        {
            yield return emp.MoveItemBehaviour;
            Botanist bot = emp.TryCast<Botanist>();
            if (bot != null && bot._workBehaviours != null)
                foreach (NpcBehaviour b in bot._workBehaviours) yield return b;
            Chemist chem = emp.TryCast<Chemist>();
            if (chem != null)
            {
                yield return chem.StartChemistryStationBehaviour;
                yield return chem.StartLabOvenBehaviour;
                yield return chem.FinishLabOvenBehaviour;
                yield return chem.StartCauldronBehaviour;
                yield return chem.StartMixingStationBehaviour;
            }
            Packager pack = emp.TryCast<Packager>();
            if (pack != null)
            {
                yield return pack.PackagingBehaviour;
                yield return pack.BrickPressBehaviour;
            }
            Cleaner clean = emp.TryCast<Cleaner>();
            if (clean != null)
            {
                yield return clean.PickUpTrashBehaviour;
                yield return clean.EmptyTrashGrabberBehaviour;
                yield return clean.BagTrashCanBehaviour;
                yield return clean.DisposeTrashBagBehaviour;
            }
        }

        internal static void Reset()
        {
            _waitPoints.Clear();
            _heldDock = null;
        }
    }

    /// <summary>
    /// Employee.OnTick is the one entry all four employee types share (virtual, so it survives IL2CPP inlining).
    /// The subclass starts a task inside it when CanWork() allows; this postfix cancels that in the same tick
    /// while the RV is away, and keeps the employee waiting.
    /// </summary>
    [HarmonyPatch(typeof(Employee), nameof(Employee.OnTick))]
    internal static class Rv_EmployeeOnTick_Patch
    {
        private static void Postfix(Employee __instance)
        {
            try
            {
                if (!RvMobile.Away || !Net.NetworkBus.IsServer && Net.NetworkBus.Online) return;
                Property prop = __instance.AssignedProperty;
                if (prop == null || RVManager.Root == null || prop.transform != RVManager.Root) return;
                if (RvAway.StopWork(__instance) > 0)
                    Core.LogDebug("[Away] " + __instance.FirstName + " started a task while the RV is away - cancelled.");
                if (!__instance.WaitOutside.Enabled) __instance.SetWaitOutside(true);
            }
            catch (Exception e) { Core.Swallowed("Rv_EmployeeOnTick_Patch", e); }
        }
    }
}
