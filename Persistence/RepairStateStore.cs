using RVRepairVan.Net;

namespace RVRepairVan.Persistence
{
    /// <summary>
    /// Accessor for the questline's persisted state.
    ///
    /// HOST / OFFLINE: backs onto <see cref="RepairSave"/>, which S1API serialises into the game save (written on
    /// game-save, read on load) - so reads/writes are in-memory during play and only hit disk when the player saves.
    /// This keeps our state in lockstep with the game's own RV/quest state.
    ///
    /// CO-OP CLIENT: the state is host-driven and lives in memory here, NOT in <see cref="RepairSave"/>. A joining
    /// client never runs S1API's load pipeline for its own save (it joins the host's world instead of loading a save),
    /// so RepairSave stays unloaded and <see cref="RepairSave.BeginLoad"/> wipes its fields - writing the host's synced
    /// Stage there does not stick (the client's quest state would fall back to 0 and desync from the host). Keeping the
    /// client's copy in plain static fields makes the host's StageSync the single source of truth on the client.
    /// </summary>
    internal static class RepairStateStore
    {
        private static RepairSave S => RepairSave.Instance;

        // A co-op client mirrors host state here instead of the save-bound RepairSave (see class summary).
        private static bool ClientMode => NetworkBus.Online && !NetworkBus.IsServer;
        private static bool _cRepaired;
        private static int _cStage, _cSamples, _cDiscount, _cUpgrades, _cInteriorUnlocked, _cInteriorRemoved;

        /// <summary>Clear the client mirror (called on scene load so a previous co-op session never leaks in).</summary>
        internal static void ResetClient() { _cRepaired = false; _cStage = 0; _cSamples = 0; _cDiscount = 0; _cUpgrades = 0; _cInteriorUnlocked = 0; _cInteriorRemoved = 0; }

        internal static bool GetRepaired() => ClientMode ? _cRepaired : (S != null && S.Repaired);
        internal static void SetRepaired(bool repaired) { if (ClientMode) _cRepaired = repaired; else if (S != null) S.Repaired = repaired; }

        internal static int GetStage() => ClientMode ? _cStage : (S != null ? S.Stage : 0);
        internal static void SetStage(int stage) { if (ClientMode) _cStage = stage; else if (S != null) S.Stage = stage; }

        internal static int GetSamples() => ClientMode ? _cSamples : (S != null ? S.Samples : 0);
        internal static void SetSamples(int samples) { if (ClientMode) _cSamples = samples; else if (S != null) S.Samples = samples; }

        internal static int GetDiscountTotal() => ClientMode ? _cDiscount : (S != null ? S.Discount : 0);
        internal static void SetDiscountTotal(int discount) { if (ClientMode) _cDiscount = discount; else if (S != null) S.Discount = discount; }

        internal static int GetUpgrades() => ClientMode ? _cUpgrades : (S != null ? S.Upgrades : 0);
        internal static void SetUpgrades(int mask) { if (ClientMode) _cUpgrades = mask; else if (S != null) S.Upgrades = mask; }

        internal static int GetInteriorUnlocked() => ClientMode ? _cInteriorUnlocked : (S != null ? S.InteriorUnlocked : 0);
        internal static void SetInteriorUnlocked(int mask) { if (ClientMode) _cInteriorUnlocked = mask; else if (S != null) S.InteriorUnlocked = mask; }
        internal static int GetInteriorRemoved() => ClientMode ? _cInteriorRemoved : (S != null ? S.InteriorRemoved : 0);
        internal static void SetInteriorRemoved(int mask) { if (ClientMode) _cInteriorRemoved = mask; else if (S != null) S.InteriorRemoved = mask; }

        // Only meaningful on the host/offline: the "repair takes a day" job clock. A client never runs the timer.
        internal static int GetRepairStartedAt() => S != null ? S.RepairStartedAt : 0;
        internal static void SetRepairStartedAt(int minSum) { if (S != null) S.RepairStartedAt = minSum; }

        // Host/offline only: the flat-battery state (RvBattery). A client mirrors the flag in RvBattery itself.
        internal static int GetLastDriven() => S != null ? S.LastDriven : 0;
        internal static void SetLastDriven(int minSum) { if (!ClientMode && S != null) S.LastDriven = minSum; }
        internal static bool GetBatteryDead() => S != null && S.BatteryDead;
        internal static void SetBatteryDead(bool dead) { if (!ClientMode && S != null) S.BatteryDead = dead; }

        // Host/offline only: where the RV was driven to (RvMobile). A client never saves the world.
        internal static bool TryGetAwayPose(out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            if (S == null || !S.Away) return false;
            pos = S.AwayPos;
            rot = S.AwayRot;
            return true;
        }

        internal static void SetAwayPose(Vector3 pos, Quaternion rot) { if (!ClientMode && S != null) S.SetAway(pos, rot); }
        internal static void ClearAwayPose() { if (!ClientMode && S != null) S.ClearAway(); }
    }
}
