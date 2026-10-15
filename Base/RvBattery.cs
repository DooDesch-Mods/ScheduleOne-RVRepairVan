using DooDesch.Localization;
using Il2CppScheduleOne.GameTime;
using RVRepairVan.Config;
using RVRepairVan.Net;
using RVRepairVan.Persistence;

namespace RVRepairVan.Base
{
    /// <summary>
    /// A flat battery after the RV has stood for a while (Engine upgrade). Once the RV has not been driven for
    /// <see cref="StandMinutes"/>, every start has a <see cref="Chance"/> to fail; the RV then stays dead until a
    /// player holds a battery (the mixing ingredient) at the wheel, which uses one up.
    ///
    /// The host decides and saves it: the last-driven stamp and the dead flag live in <see cref="RepairSave"/>, the
    /// roll runs where the chassis would spawn, and the clients mirror the flag through <see cref="RvOp.BatterySync"/>.
    /// A client swapping the battery consumes it from its own inventory and tells the host.
    /// </summary>
    internal static class RvBattery
    {
        private const int StandMinutes = 3 * 24 * 60;
        private const float Chance = 0.25f;
        private const string BatteryId = "battery";

        private static bool ClientMode => NetworkBus.Online && !NetworkBus.IsServer;
        private static bool _cDead;

        internal static bool Dead => ClientMode ? _cDead : RepairStateStore.GetBatteryDead();

        internal static void ResetClient() { _cDead = false; }

        private static int Now()
        {
            TimeManager tm = NetworkSingleton<TimeManager>.Instance;
            return tm != null ? tm.GetTotalMinSum() : 0;
        }

        /// <summary>Host/offline: the RV was driven just now, so the standing time starts again.</summary>
        internal static void HostStamp()
        {
            if (ClientMode) return;
            int now = Now();
            if (now > 0) RepairStateStore.SetLastDriven(now);
        }

        /// <summary>
        /// Host/offline, at a start attempt: true when the battery is flat and the RV must not start. Rolls once per
        /// start after the standing time; a start that passes resets the clock.
        /// </summary>
        internal static bool HostBlocksStart()
        {
            if (ClientMode) return false;
            if (!RVRepairVanPreferences.BatteryBreakdowns)
            {
                if (RepairStateStore.GetBatteryDead()) { SetDead(false); Core.Log.Msg("[Battery] breakdowns are off - the flat battery is cleared."); }
                return false;
            }
            if (RepairStateStore.GetBatteryDead()) { Core.Log.Msg("[Battery] start refused - the battery is still flat."); return true; }

            int now = Now();
            int last = RepairStateStore.GetLastDriven();
            if (now <= 0) { Core.Log.Warning("[Battery] no game clock - no breakdown roll."); return false; }
            if (last <= 0 || last > now) { RepairStateStore.SetLastDriven(now); Core.Log.Msg("[Battery] first start on record - the clock starts now."); return false; }

            int idle = now - last;
            if (idle < StandMinutes) { Core.LogDebug("[Battery] stood " + idle + " min, below " + StandMinutes + " - no roll."); return false; }
            float roll = UnityEngine.Random.value;
            if (roll >= Chance)
            {
                RepairStateStore.SetLastDriven(now);
                Core.Log.Msg("[Battery] stood " + (idle / 1440f).ToString("0.0") + " days, roll " + roll.ToString("0.00") + " - it starts.");
                return false;
            }
            SetDead(true);
            Core.Log.Msg("[Battery] stood " + (idle / 1440f).ToString("0.0") + " days, roll " + roll.ToString("0.00") + " - the battery is flat.");
            return true;
        }

        /// <summary>The local player failed to start the RV: Marco hints at the fix.</summary>
        internal static void TellLocalPlayer()
        {
            RvRepairJob.TextFromMarco(L10n.T("Left the keys in again? Battery's dead, bring a new one to the wheel."));
        }

        /// <summary>
        /// The local player used the wheel while the battery is flat. A held battery is used up and the RV is good
        /// again (returns true, the start goes on); empty hands leave it dead (false).
        /// </summary>
        internal static bool TrySwap()
        {
            if (!HoldsBattery(out ItemSlot slot)) { Core.Log.Msg("[Battery] wheel used with a flat battery and no battery in hand."); return false; }
            RemoveOne(slot);
            if (ClientMode)
            {
                _cDead = false;
                NetworkBus.SendToHost(RvOp.BatterySwap);
                Core.Log.Msg("[Battery] battery swapped - told the host.");
            }
            else
            {
                SetDead(false);
                Core.Log.Msg("[Battery] battery swapped.");
            }
            return true;
        }

        /// <summary>Host: a client swapped the battery from its own inventory.</summary>
        internal static void HostOnSwap()
        {
            if (!RepairStateStore.GetBatteryDead()) { Core.Log.Msg("[Battery] client swap - the battery was not flat."); Broadcast(); return; }
            SetDead(false);
            Core.Log.Msg("[Battery] a client swapped the battery.");
        }

        /// <summary>Client: the host's flag arrived.</summary>
        internal static void ApplySync(int dead)
        {
            bool was = _cDead;
            _cDead = dead != 0;
            if (_cDead && !was && RvMobile.CancelPendingEnter())
            {
                Core.Log.Msg("[Battery] the host says the battery is flat - the start failed.");
                TellLocalPlayer();
            }
            else Core.LogDebug("[Battery] sync: dead=" + _cDead + ".");
        }

        internal static void HostSnapshot() { if (!ClientMode) Broadcast(); }

        private static void SetDead(bool dead)
        {
            RepairStateStore.SetBatteryDead(dead);
            if (!dead) HostStamp();
            Broadcast();
        }

        private static void Broadcast()
        {
            if (!NetworkBus.Online || !NetworkBus.IsServer) return;
            NetworkBus.BroadcastToAll(RvOp.BatterySync, RepairStateStore.GetBatteryDead() ? 1 : 0);
        }

        internal static bool HoldsBattery(out ItemSlot slot)
        {
            slot = null;
            try
            {
                PlayerInventory inv = PlayerSingleton<PlayerInventory>.Instance;
                if (inv == null) return false;
                ItemSlot s = inv.equippedSlot;
                ItemInstance item = s != null ? s.ItemInstance : null;
                if (item == null || !string.Equals(((BaseItemInstance)item).ID, BatteryId, StringComparison.OrdinalIgnoreCase)) return false;
                slot = s;
                return true;
            }
            catch (Exception e) { Core.Swallowed("RvBattery.HoldsBattery", e); return false; }
        }

        // ChangeQuantity(-1) on the last unit leaves an empty item in the slot, so the last one is cleared instead.
        private static void RemoveOne(ItemSlot slot)
        {
            ItemInstance item = slot.ItemInstance;
            int qty = item != null ? ((BaseItemInstance)item).Quantity : 0;
            if (qty > 1) slot.ChangeQuantity(-1);
            else slot.ClearStoredInstance(false);
        }

#if DEBUG
        /// <summary>Debug (rvbattery): report; 'dead' / 'fix' set the flag; 'age N' makes the RV stand N days.</summary>
        internal static void DebugCmd(string[] args)
        {
            string a = args.Length > 0 ? args[0].ToLower() : "";
            if (a == "dead") SetDead(true);
            else if (a == "fix") SetDead(false);
            else if (a == "age" && args.Length > 1 && float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float days))
                RepairStateStore.SetLastDriven(Math.Max(1, Now() - (int)(days * 1440f)));
            int last = RepairStateStore.GetLastDriven();
            Core.Log.Msg("[Debug] rvbattery: dead=" + Dead + " lastDriven=" + last + " now=" + Now() + " stood="
                + (last > 0 ? ((Now() - last) / 1440f).ToString("0.00") : "-") + " days, enabled=" + RVRepairVanPreferences.BatteryBreakdowns
                + ", holding battery=" + HoldsBattery(out _) + ".");
        }
#endif
    }
}
