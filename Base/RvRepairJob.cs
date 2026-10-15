using System;
using System.Collections;
using DooDesch.Localization;
using Il2CppScheduleOne.GameTime;
using MelonLoader;
using RVRepairVan.Config;
using RVRepairVan.Managers;
using RVRepairVan.Net;
using RVRepairVan.Persistence;

namespace RVRepairVan.Base
{
    /// <summary>
    /// The two comfort options around the repair itself.
    ///
    /// <b>InstantRepair</b> - repairs the RV free of charge as soon as it is wrecked. Unlike the competing mod,
    /// which blocks <c>set_IsDestroyed</c> outright, this lets the explosion happen normally and repairs
    /// afterwards: the blast, the sound and the tutorial quest beats that hang off it all still fire. The
    /// build-outs stay paid - only the repair is free.
    ///
    /// <b>RepairTakesADay</b> - Marco takes the job and the RV is ready 1440 in-game minutes later, with a text
    /// message when it is done. The START STAMP is persisted, so reloading mid-job keeps the remaining time
    /// instead of finishing instantly (which is what the competing mod does - it only stores a paid boolean).
    /// </summary>
    internal static class RvRepairJob
    {
        internal const int JobMinutes = 1440;   // one in-game day

        private static bool _watching;

        /// <summary>Current in-game minute counter, or -1 if the time manager is not up yet.</summary>
        internal static int NowMinutes()
        {
            try
            {
                TimeManager tm = NetworkSingleton<TimeManager>.Instance;
                if (tm == null) return -1;
                return tm.GetTotalMinSum();
            }
            catch (Exception e) { Core.Swallowed("RvRepairJob.NowMinutes", e); return -1; }
        }

        /// <summary>True while Marco has the RV and it is not finished yet.</summary>
        internal static bool JobRunning => RepairStateStore.GetRepairStartedAt() > 0 && !RepairStateStore.GetRepaired();

        /// <summary>
        /// Book the job instead of repairing on the spot. Returns false if the clock is unreadable, so the caller
        /// can fall back to repairing immediately rather than swallowing the player's money for nothing.
        /// </summary>
        internal static bool Begin()
        {
            int now = NowMinutes();
            if (now < 0) { Core.Log.Warning("[Job] game clock unavailable - repairing immediately instead."); return false; }
            RepairStateStore.SetRepairStartedAt(now);
            MelonCoroutines.Start(WatchJob());
            Core.Log.Msg("[Job] Marco took the RV in at minute " + now + "; ready in " + JobMinutes + ".");
            return true;
        }

        /// <summary>Resume an in-flight job after a load (host/offline only - a client mirrors the host's result).</summary>
        internal static void ResumeIfRunning()
        {
            if (NetworkBus.Online && !NetworkBus.IsServer) return;
            if (!JobRunning) return;
            Core.Log.Msg("[Job] resuming Marco's repair job (started at minute " + RepairStateStore.GetRepairStartedAt() + ").");
            MelonCoroutines.Start(WatchJob());
        }

        private static IEnumerator WatchJob()
        {
            if (_watching) yield break;
            _watching = true;
            try
            {
                while (JobRunning)
                {
                    yield return new WaitForSeconds(2f);
                    int now = NowMinutes();
                    // An unreadable clock is a reason to WAIT, not to finish early: the competing mod breaks out of
                    // its loop here and hands the player a free instant repair.
                    if (now < 0) continue;
                    int started = RepairStateStore.GetRepairStartedAt();
                    if (started <= 0) break;
                    if (now - started < JobMinutes) continue;
                    Finish();
                    break;
                }
            }
            finally { _watching = false; }
        }

        private static void Finish()
        {
            try
            {
                if (!RVManager.Repair()) { Core.Log.Warning("[Job] repair call failed at job completion."); return; }
                RepairStateStore.SetRepaired(true);
                RepairStateStore.SetRepairStartedAt(0);
                if (NetworkBus.Online && NetworkBus.IsServer) NetworkBus.BroadcastToAll(RvOp.RepairApplied);
                Quests.RepairQuest.CompleteIfActive();
                TextFromMarco(L10n.T("Your RV's all done, buddy. Good as new. Next time, better call Marco."));
                Core.Log.Msg("[Job] Marco finished the RV.");
            }
            catch (Exception e) { Core.Log.Warning("[Job] finish failed: " + e.Message); }
        }

        /// <summary>Marco texts the player. Falls back to a world line if the phone path fails.</summary>
        internal static void TextFromMarco(string text)
        {
            try
            {
                var marco = S1API.Entities.NPC.Get("marco_baron");
                if (marco != null)
                {
                    marco.SendTextMessage(text);
                    Core.Log.Msg("[Marco] texted the player: " + text);
                    return;
                }
                Core.Log.Warning("[Marco] NPC not found - saying the text in the world instead.");
            }
            catch (Exception e) { Core.Swallowed("RvRepairJob.TextFromMarco.text", e); }
            try { Quests.Questline.SayMarco(text); } catch (Exception e) { Core.Swallowed("RvRepairJob.TextFromMarco", e); }
        }

        // --- instant repair -------------------------------------------------------------------------------

        /// <summary>
        /// Watch for the RV being wrecked and repair it straight away. Host/offline only - on a client the host
        /// owns RV.IsDestroyed and pushes the result. Started per scene; exits when the setting is off.
        /// </summary>
        internal static IEnumerator InstantRepairCoroutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(3f);
                if (!RVRepairVanPreferences.Enabled || !RVRepairVanPreferences.InstantRepair) continue;
                if (NetworkBus.Online && !NetworkBus.IsServer) continue;
                bool wrecked;
                try { wrecked = RVManager.TryLocate() && RVManager.IsDestroyed(); }
                catch (Exception e) { Core.Swallowed("RvRepairJob.InstantRepairCoroutine", e); continue; }
                if (!wrecked) continue;

                Core.Log.Msg("[Instant] RV is wrecked and InstantRepair is on - repairing free of charge.");
                try
                {
                    if (RVManager.Repair())
                    {
                        RepairStateStore.SetRepaired(true);
                        RepairStateStore.SetRepairStartedAt(0);
                        if (NetworkBus.Online && NetworkBus.IsServer) NetworkBus.BroadcastToAll(RvOp.RepairApplied);
                        Quests.RepairQuest.CompleteIfActive();
                    }
                }
                catch (Exception e) { Core.Log.Warning("[Instant] repair failed: " + e.Message); }
            }
        }
    }
}
