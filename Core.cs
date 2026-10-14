using System;
using System.Collections;
using MelonLoader;
using RVRepairVan.Config;
using RVRepairVan.Dialogue;
using RVRepairVan.Managers;
using RVRepairVan.Persistence;
using RVRepairVan.Quests;

[assembly: MelonInfo(typeof(RVRepairVan.Core), "RVRepairVan", DooDesch.ModVersion.Current, "DooDesch", "https://github.com/DooDesch-Mods/ScheduleOne-RVRepairVan")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace RVRepairVan
{
    /// <summary>
    /// MelonLoader entry point. Mirrors the Mimesis-InventoryExpansion template:
    /// initialize preferences, patch Harmony, log status. Per-scene setup runs the
    /// Marco dialogue injection and the post-load repair-state restore.
    /// </summary>
    public sealed class Core : MelonMod
    {
        public static Core Instance { get; private set; }
        public static MelonLogger.Instance Log { get; private set; }

        /// <summary>Debug-only trace log - compiled out of Release builds so the release log stays clean.</summary>
        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDebug(string msg) { Log?.Msg(msg); }

        // Keys already reported by Swallowed. Several callers sit in per-frame paths, so each site logs once per process.
        private static readonly System.Collections.Generic.HashSet<string> _swallowed = new System.Collections.Generic.HashSet<string>();

        /// <summary>Report a caught exception that deliberately stays without effect. Logs once per <paramref name="where"/> key.</summary>
        internal static void Swallowed(string where, Exception e)
        {
            lock (_swallowed)
            {
                if (!_swallowed.Add(where)) return;
            }
            Log?.Warning("[Swallowed] " + where + ": " + e.GetType().Name + ": " + e.Message);
        }

        public override void OnInitializeMelon()
        {
            Instance = this;
            Log = LoggerInstance;

            RVRepairVanPreferences.Initialize();
            Localization.German.Register();   // before any L10n.T use (quest title, dialogue, items)
            HarmonyInstance.PatchAll();

#if DEBUG
            // The debug words are answered in a SubmitCommand prefix and registered nowhere, so nothing that lists
            // commands can see them. This puts them in the game's own list for a terminal to complete.
            Patches.DebugConsolePatch.DeclareForTools();
#endif
            RVRepairVan.Net.NetworkBus.Init(HarmonyInstance);   // co-op sync (host-authoritative; no-op offline)

            Log.Msg($"RVRepairVan initialized. Enabled={RVRepairVanPreferences.Enabled}, Questline={RVRepairVanPreferences.QuestlineEnabled}, RepairPrice={RVRepairVanPreferences.RepairPrice}");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            // Back at the menu: clear the save-bound state so the next save starts from a clean slate. This has to
            // happen HERE and not on "Main" - S1API's OnLoaded runs before OnSceneWasLoaded when going menu ->
            // load, so wiping on "Main" would discard the values it had just read.
            if (sceneName == "Menu")
            {
                RepairSave.BeginLoad();
                return;
            }

            if (sceneName != "Main")
            {
                return;
            }

            RVManager.Reset();
            RVRepairVan.Effects.RepairCinematic.ForceReset();   // un-black + unlock input if a prior cinematic was interrupted

            // Wire the co-op bus dispatch regardless of quest mode - Simple mode also needs the host-authoritative
            // PayRepair round-trip (idempotent; no-op offline).
            Questline.InitNet();

            if (RVRepairVanPreferences.QuestlineEnabled)
            {
                Questline.Reset();
                Questline.Start();
            }
            else
            {
                MarcoRepairDialogue.Reset();
                MelonCoroutines.Start(MarcoRepairDialogue.SetupCoroutine());
                // The client mirror and the join snapshot used to be wired only by Questline.Start, so a Simple
                // mode client never asked for state: it kept a wrecked, un-upgraded RV until some later broadcast
                // happened to arrive, and the previous session's mirror leaked in. Both are quest-mode-independent.
                RepairStateStore.ResetClient();
                MelonCoroutines.Start(Questline.NetJoinCoroutineShared());
            }

            RVRepairVan.Base.RvUpgrades.Reset();   // drop last scene's cached transforms; the mask lives in the save
            RVRepairVan.Net.ClientPayment.Reset();  // a ticket from the last session can never be answered now

            MelonCoroutines.Start(RestoreRepairCoroutine());
            MelonCoroutines.Start(RVRepairVan.Base.RvRepairJob.InstantRepairCoroutine());
        }

        /// <summary>
        /// Fired when preferences are saved (incl. via the Mod Manager &amp; Phone App UI).
        /// Handles the one-shot debug "Destroy RV" toggle. Full live-settings integration
        /// follows in the Mod Manager phase.
        /// </summary>
        public override void OnPreferencesSaved()
        {
            try
            {
                // Live-update Marco's repair choice label to the current price (no restart).
                MarcoRepairDialogue.RefreshPrice();
                Questline.RefreshPrice();

#if DEBUG
                // Debug helpers are compiled into Debug builds only - never shipped to players.
                if (RVRepairVanPreferences.ConsumeDestroyRequest())
                {
                    Log.Msg("[Debug] Destroy RV toggle on - wrecking the RV now.");
                    if (RVManager.Destroy())
                    {
                        // Full reset so the questline re-runs from the top (debug re-test convenience).
                        RepairStateStore.SetRepaired(false);
                        RepairStateStore.SetStage(0);
                        RepairStateStore.SetSamples(0);
                        RepairStateStore.SetDiscountTotal(0);
                    }
                }

                if (RVRepairVanPreferences.ConsumeAddCashRequest())
                {
                    S1API.Money.Money.ChangeCashBalance(RVRepairVanPreferences.DebugCashAmount, true, true);
                    Log.Msg("[Debug] Added " + MoneyManager.FormatAmount(RVRepairVanPreferences.DebugCashAmount) + " cash.");
                }

                if (RVRepairVanPreferences.ConsumeDumpRequest())
                {
                    RVManager.LogState();
                    Questline.DumpNpcDiagnostics();
                }

                if (RVRepairVanPreferences.ConsumeTestCinematicRequest())
                {
                    Log.Msg("[Debug] Playing repair cinematic test.");
                    RVRepairVan.Effects.RepairCinematic.Play(null, () => Log.Msg("[Debug] cinematic test done."),
                        () => Questline.DebugGruntNearest());
                }

                if (RVRepairVanPreferences.ConsumeNetPingRequest())
                {
                    Log.Msg("[Debug] Net ping. Online=" + RVRepairVan.Net.NetworkBus.Online + " IsServer=" + RVRepairVan.Net.NetworkBus.IsServer);
                    if (RVRepairVan.Net.NetworkBus.IsServer) RVRepairVan.Net.NetworkBus.BroadcastToAll(RVRepairVan.Net.RvOp.Ping, 42);
                    RVRepairVan.Net.NetworkBus.SendToHost(RVRepairVan.Net.RvOp.Ping, 99);
                }
#endif
            }
            catch (Exception e)
            {
                Log.Warning("[Prefs] OnPreferencesSaved failed: " + e.Message);
            }
        }

        /// <summary>
        /// After a save loads, re-apply a previously paid-for repair (no charge) if the
        /// RV spawned back in its destroyed state.
        /// </summary>
        private static IEnumerator RestoreRepairCoroutine()
        {
            // Wait until our save-bound state has actually loaded (repaired flag is authoritative only then),
            // up to ~10s, then let the base-game RV settle a moment before reading/repairing.
            float waited = 0f;
            while (!RepairSave.Loaded && waited < 10f) { yield return new WaitForSeconds(0.5f); waited += 0.5f; }
            yield return new WaitForSeconds(2f);

            // On a co-op CLIENT the local save is not authoritative for the RV - the host owns RV.IsDestroyed and
            // replays it via OnSpawnServer / our RepairApplied snapshot. Restoring from our own (possibly stale)
            // local flag here could fight the host, so skip it; the host and offline both still restore normally.
            if (RVRepairVan.Net.NetworkBus.Online && !RVRepairVan.Net.NetworkBus.IsServer)
            {
                Log.Msg("[Restore] co-op client - deferring RV state to the host (no local restore).");
                yield break;
            }

            RVManager.LogState();   // diagnostic: see the real RV state on load

            bool restore = false;
            try
            {
                restore = RVManager.TryLocate()
                          && RepairStateStore.GetRepaired()
                          && RVManager.IsDestroyed();
            }
            catch (Exception e)
            {
                Log.Warning("[Restore] check failed: " + e.Message);
            }

            if (restore)
            {
                Log.Msg("[Restore] RV was previously repaired - restoring without charge.");
                RVManager.Repair();
            }

            // Marco's build-outs are runtime effects on scene objects, not saved world state - only the paid-for
            // mask is persisted, so re-apply it now that the intact RV exists. Retry a few times: the interior
            // and the grids only resolve once the repaired model is the active one.
            RVRepairVan.Base.RvRepairJob.ResumeIfRunning();
            for (int i = 0; i < 5; i++)
            {
                try { RVRepairVan.Base.RvUpgrades.ApplyOwned(); }
                catch (Exception e) { Log.Warning("[Restore] upgrade apply failed: " + e.Message); }
                yield return new WaitForSeconds(2f);
            }
        }
    }
}
