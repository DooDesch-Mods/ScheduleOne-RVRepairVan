using System;
using Il2CppScheduleOne.Vehicles;
using RVRepairVan.Base;
using RVRepairVan.Config;
using RVRepairVan.Managers;
using RVRepairVan.Persistence;
using UnityEngine;

namespace RVRepairVan.Api
{
    /// <summary>
    /// The RV's state for other mods: whether Marco repaired it, which upgrades it has, and where it is while someone
    /// drives it. Read it after the world has loaded; before that every value is the empty state.
    ///
    /// Co-op: the host owns the state and syncs it, so a client reads the host's values. The events fire on every
    /// machine where the value changes, the host and each client. A save that loads already repaired raises no event;
    /// read <see cref="IsRepaired"/> once the world is up. A client that joins a host with a repaired RV gets the events
    /// when the host's state arrives.
    /// </summary>
    public static class RvRepairVanApi
    {
        /// <summary>Raised by additions to this API. 1: state, events, vehicle, send home.</summary>
        public const int ApiVersion = 1;

        /// <summary>Marco has repaired the RV in this save.</summary>
        public static bool IsRepaired => RepairStateStore.GetRepaired();

        /// <summary>The engine upgrade is bought: the RV can be driven.</summary>
        public static bool HasEngine => RvUpgrades.Owned(RvUpgrade.Engine);

        /// <summary>What Marco asks for the engine upgrade, from this player's settings.</summary>
        public static int EnginePrice => RvUpgrades.Price(RvUpgrade.Engine);

        /// <summary>The mod is switched on in its settings.</summary>
        public static bool IsEnabled => RVRepairVanPreferences.Enabled;

        /// <summary>Raised when the RV becomes repaired during play.</summary>
        public static event Action Repaired;

        /// <summary>Raised when the engine upgrade is bought during play.</summary>
        public static event Action EngineBought;

        /// <summary>
        /// The vehicle the RV rides on while it is off its spot or someone drives it, or null while it stands at home.
        /// Its transform is the RV's position on the road.
        /// </summary>
        public static LandVehicle Vehicle => RvMobile.Chassis;

        /// <summary>True while someone sits at the RV's wheel.</summary>
        public static bool IsDriving => RvMobile.Driving;

        /// <summary>The RV stands on its spot at the RV property.</summary>
        public static bool IsHome => RvMobile.Chassis == null;

        /// <summary>The RV's position: on the road while it is out, else its spot. False before the world has loaded.</summary>
        public static bool TryGetPosition(out Vector3 position)
        {
            LandVehicle v = RvMobile.Chassis;
            if (v != null)
            {
                position = v.transform.position;
                return true;
            }
            return RVManager.TryGetPosition(out position);
        }

        /// <summary>
        /// A world point lies in the RV's body, or within <paramref name="margin"/> metres of its sides, while the RV
        /// is on the road. Use it to tell who rides along. Always false while the RV stands at home.
        /// </summary>
        public static bool Contains(Vector3 world, float margin = 0.3f) => RvMobile.Inside(world, margin);

        /// <summary>
        /// Host: put the RV back on its spot at the RV property now, wherever it is. A driver at the wheel is taken
        /// out first. False on a co-op client. <paramref name="reason"/> goes into the log.
        /// </summary>
        public static bool SendHome(string reason) => RvMobile.HostSendHome(string.IsNullOrEmpty(reason) ? "a mod" : reason);

        internal static void RaiseRepaired()
        {
            Core.Log.Msg("[Api] the RV is repaired - raising Repaired.");
            if (Repaired == null) return;
            foreach (Delegate d in Repaired.GetInvocationList())
            {
                try { ((Action)d)(); }
                catch (Exception e) { Core.Log.Error("[Api] a Repaired handler failed: " + e); }
            }
        }

        internal static void RaiseUpgradesChanged(int was, int now)
        {
            int engine = (int)RvUpgrade.Engine;
            if ((was & engine) != 0 || (now & engine) == 0) return;
            Core.Log.Msg("[Api] the engine is bought - raising EngineBought.");
            if (EngineBought == null) return;
            foreach (Delegate d in EngineBought.GetInvocationList())
            {
                try { ((Action)d)(); }
                catch (Exception e) { Core.Log.Error("[Api] an EngineBought handler failed: " + e); }
            }
        }
    }
}
