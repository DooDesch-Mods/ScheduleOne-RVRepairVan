using System;
using System.Collections;
using DooDesch.Localization;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.VoiceOver;   // EVOLineType / VOEmitter (Marco's grunt)
using MelonLoader;
using RVRepairVan.Config;
using RVRepairVan.Managers;
using RVRepairVan.Net;
using RVRepairVan.Persistence;
using S1API.Quests;
using UnityEngine.Events;

namespace RVRepairVan.Quests
{
    /// <summary>
    /// "Back on the Road" - the configurable RV-repair questline (QuestMode = Questline). See
    /// docs/QUESTLINE.md (player) and docs/QUEST-SCRIPT.md (exact trace).
    /// Donna -> Ming (crate dead-drop errand) -> Marco (referral price + optional trust pickup + packaged
    /// samples) -> pay -> check the RV. A single stage value drives the objective AND the price tier
    /// (ReferralUsed / Trusted are derived from Stage). Progress persists per save via RepairStateStore.
    /// </summary>
    internal static partial class Questline
    {
        // Persisted stages (RepairStateStore.GetStage/SetStage). The stage encodes ALL progression, so the
        // price tier is derived from it: the 10k referral price (ReferralUsed) at >= ReadyToPay, the sample
        // discount (Trusted_) at >= Trusted.
        private const int None = 0, Started = 1, AskedDonna = 2, MingErrand = 3, MingCrate = 4,
                          Referred = 5, MarcoMet = 6, ReadyToPay = 7, Trusted = 8, Paid = 9, Done = 10;

        /// <summary>
        /// The stage a freshly loaded save should run at. A repaired RV with the story stored mid-way (Simple mode
        /// repairs without moving the stage, a debug repair skips it) would otherwise re-open "Back on the road" with
        /// its marker on an intact RV, because the RV still reads as a wreck until the repair visual is swapped in.
        /// </summary>
        internal static int StageForLoad(bool repaired, int stage)
        {
            if (!repaired || stage < Started || stage >= Paid) return stage;
            Core.Log.Msg("[Questline] save has a repaired RV at stage " + stage + " - loading it as finished.");
            return Done;
        }

        // NPC ids in the game's registry (same ids S1API uses). Reliable lookup; see FindNpc().
        private const string DonnaId = "donna_martin", MingId = "ming", MarcoId = "marco_baron";

        // Custom carried items placed in the dead drops, and the fee if the player loses one.
        private const string CrateId = "rv_ming_crate", PackageId = "rv_marco_package";
        private const int LostPackageFee = 500;

        // Loss-fallback lines (per NPC). Player admits the loss -> NPC demands $LostPackageFee -> pay or defer.
        // English source text doubles as the L10n key - wrap in L10n.T(...) at the point of use.
        private const string MingAngry  = "You lose my crate? Not very good. Five hundred, then we are okay, yes?";
        private const string MingPaid   = "Very good. Now go see Marco at the docks, tell him Ming sends you.";
        private const string MingShort  = "Okay. You bring money, yes?";
        private const string MarcoAngry = "You lost it? Well, that's gonna cost ya. Five hundred and we're even.";
        private const string MarcoPaid  = "Pleasure doing business. Now, bring me packaged product in your hand, and every piece comes off your bill.";
        private const string MarcoShort = "I'll be here.";

        // Attempts (2s each during the fast burst) an NPC's dialogue containers get to build before we inject the
        // fallback version instead. ~30s covers a slow scene load without leaving the player waiting on a dead NPC.
        private const int DEGRADED_AFTER_ATTEMPTS = 15;

        // Transient per-scene state.
        private static bool _donnaDone, _mingDone, _marcoDone;   // dialogue injected per NPC (independent)
        private static bool _pickupActive;       // Marco's trust pickup started
        private static bool _hasPackage;         // Marco's package collected from the drop
        private static Vector3 _dropPoint;       // Marco's package dead drop
        private static S1API.DeadDrops.DeadDropInstance _drop;   // reserved dead drop for Marco's pickup
        private static Vector3 _cratePoint;      // Ming's crate dead drop
        private static S1API.DeadDrops.DeadDropInstance _crateDrop;  // reserved dead drop for Ming's crate
        private static bool _cratePlaced, _pkgPlaced;   // a real item was placed in that drop (else proximity fallback)
        private static bool _itemsRegistered;
        private static bool _clientCheckedRv;    // co-op client: reported reaching the post-repair RV once
        private static int _gen;                 // scene generation - stops stale coroutines after a reload

        private static Transform _donnaT, _mingT, _marcoT;
        private static DialogueController _donnaDC, _mingDC, _marcoDC;   // kept for the rvdiag dump
        private static int _injectedChoices, _skippedChoices;   // AddChoice bookkeeping, reported by rvdiag
#if DEBUG
        private static bool _injectedDegraded;         // an NPC had to be injected without its dialogue containers
        private static bool _debugForceNoContainers;   // rvreinject: pretend S1API handed us nothing
#endif
        private static DialogueController.DialogueChoice _marcoRepairChoice;

        // Sample choice: the packaged product in the player's hand; _sampleSlots is the live snapshot the choice
        // and its handler index into.
        // Marco's build-out shop. Order matters only for the display order of the choices - the prerequisites
        // themselves live in RvUpgrades.
        // The ones inside the RV (furniture, workshop floor) are sold at Marco's bodyshop (Garage.RvGarage).
        private static readonly Base.RvUpgrade[] UpgradeOrder =
        {
            Base.RvUpgrade.CrewQuarters, Base.RvUpgrade.LoadingDock, Base.RvUpgrade.Engine,
        };
        private static readonly DialogueController.DialogueChoice[] _upgradeChoices =
            new DialogueController.DialogueChoice[UpgradeOrder.Length];

        // Vanilla lists an NPC's shown choices by Priority, highest first (DialogueController.GetActiveChoices).
        // Marco's own choices sit at 0 and his customer choices run from 10 ([Complete Deal]) down to -20 (free
        // sample), so every RV choice stays below -20: number key 1 keeps the vanilla choice, never a purchase.
        internal const int MarcoPriorityTop = -100;

        private const int MAX_SAMPLE_CHOICES = 1;   // only the product in the player's hand
        private static readonly DialogueController.DialogueChoice[] _sampleChoices = new DialogueController.DialogueChoice[MAX_SAMPLE_CHOICES];
        private static readonly System.Collections.Generic.List<ItemSlot> _sampleSlots = new System.Collections.Generic.List<ItemSlot>();

        // The host is authoritative: any host-side write to Stage / DiscountTotal auto-replicates to all clients
        // (single StageSync broadcast). Offline and client writes don't broadcast (client writes only happen via
        // ApplyStageSync, which uses RepairStateStore directly to avoid echoing). This is the one choke point that
        // keeps every shared state change in sync without per-handler broadcast calls.
        private static int Stage
        {
            get => RepairStateStore.GetStage();
            set
            {
                RepairStateStore.SetStage(value);
                if (NetworkBus.Online && NetworkBus.IsServer)
                    NetworkBus.BroadcastToAll(RvOp.StageSync, value, DiscountTotal);
            }
        }
        private static int Samples { get => RepairStateStore.GetSamples(); set => RepairStateStore.SetSamples(value); }
        private static int DiscountTotal
        {
            get => RepairStateStore.GetDiscountTotal();
            set
            {
                RepairStateStore.SetDiscountTotal(value);
                if (NetworkBus.Online && NetworkBus.IsServer)
                    NetworkBus.BroadcastToAll(RvOp.StageSync, Stage, value);
            }
        }

        private static bool ReferralUsed => Stage >= ReadyToPay;  // you actually told Marco - price is 10k
        private static bool Trusted_ => Stage >= Trusted;         // did Marco's pickup - packaged samples unlocked
        // A wrecked RV needs repairing - availability is driven by IsDestroyed(). On a co-op CLIENT, RV.IsDestroyed
        // is host-owned and only pushed at join (OnSpawnServer), so a wreck that happens AFTER the client joined
        // never reaches it - the client would then hide every quest choice (e.g. Donna's) even though the host's
        // quest is live and its Stage synced. So a client also trusts the synced Stage: an active quest
        // (Started up to before Paid) means the host has a wrecked RV by definition. Host/offline keep the direct check.
        private static bool Active => RVRepairVanPreferences.Enabled
            && (RVManager.IsDestroyed()
                || (NetworkBus.Online && !NetworkBus.IsServer && Stage >= Started && Stage < Paid));

        internal static int CurrentPrice()
        {
            // Referral price (10k) only once you've told Marco "Mrs. Ming sent me" (Stage >= ReadyToPay); 50k before.
            int basePrice = ReferralUsed ? RVRepairVanPreferences.BasePriceWithReferral : RVRepairVanPreferences.BasePriceNoReferral;
            return Mathf.Max(RVRepairVanPreferences.RepairPrice, basePrice - DiscountTotal);
        }

        // The repair questline only begins once the player is past the explosion beat: the RV blew up
        // during "Welcome to Hyland Point", they reported it to Uncle Nelson by payphone, and "Getting
        // Started" (rent the motel room) is now active/completed. Reading the vanilla quest state avoids the
        // immersion break of the quest popping the instant the RV explodes.
        private static bool ExplosionBeatPassed()
        {
            try
            {
                var gs = Il2CppScheduleOne.Quests.Quest.GetQuest("Getting Started");
                if (gs != null && (gs.State == Il2CppScheduleOne.Quests.EQuestState.Active ||
                                   gs.State == Il2CppScheduleOne.Quests.EQuestState.Completed))
                    return true;
                var wh = Il2CppScheduleOne.Quests.Quest.GetQuest("Welcome to Hyland Point");
                if (wh != null && wh.State == Il2CppScheduleOne.Quests.EQuestState.Completed)
                    return true;
            }
            catch (Exception e) { Core.Swallowed("Questline.ExplosionBeatPassed", e); }
            return false;
        }

        internal static void Reset()
        {
            _gen++;
            ResetDiag();
            _donnaDone = _mingDone = _marcoDone = false;
            _pickupActive = false;
            _hasPackage = false;
            _clientCheckedRv = false;
            _drop = null;
            _crateDrop = null;
            _cratePlaced = _pkgPlaced = false;   // _itemsRegistered stays - registration persists across resets
            _dropPoint = Vector3.zero;
            _cratePoint = Vector3.zero;
            _donnaT = _mingT = _marcoT = null;
            _donnaDC = _mingDC = _marcoDC = null;
            _injectedChoices = _skippedChoices = 0;
#if DEBUG
            _injectedDegraded = false;
#endif
            _marcoRepairChoice = null;
            System.Array.Clear(_upgradeChoices, 0, _upgradeChoices.Length);
            System.Array.Clear(_sampleChoices, 0, _sampleChoices.Length);
            _sampleSlots.Clear();
            RepairStateStore.ResetClient();   // co-op client mirror starts clean each scene load (host re-sends snapshot)
            Base.RvBattery.ResetClient();
        }

        internal static void Start()
        {
            InitNet();
            MelonCoroutines.Start(EnsureItemsCoroutine());   // host AND client: register quest items so both can see them in drops
            MelonCoroutines.Start(SetupCoroutine());
            MelonCoroutines.Start(ProximityCoroutine());
            MelonCoroutines.Start(RestoreCoroutine());
            MelonCoroutines.Start(NetJoinCoroutine());
        }

#if DEBUG
        // Debug: skip the questline straight to a stage. Run ON THE HOST - the Stage setter broadcasts StageSync to
        // clients, so both jump together. Lets testing bypass the dead-drop errands. SyncEntry re-renders journal/POI.
        internal static void DebugSetStage(int n)
        {
            Stage = n;
            // Materialize the journal quest (+ its POI marker) for active stages, so jumping straight to a stage
            // is actually testable in one command. The quest entry is added on a later event (see RepairQuest.HasEntry),
            // so ensure-then-sync has to wait for it - plain SyncEntry on a just-created quest no-ops.
            if (n >= Started && n < Done) MelonCoroutines.Start(DebugEnsureQuestThenSync());
            else SyncEntry();
            Core.Log.Msg("[Debug] rvstage -> Stage=" + n + " (host=" + NetworkBus.IsServer + ")");
#if SNITCH
            Snitch.Api.Profiler.Log("RVRepairVan", "rvstage -> Stage=" + n + " (host=" + NetworkBus.IsServer + ").");
#endif
        }

        // Create the quest if needed, then wait for its entry to be added (OnCreated fires a frame or two later)
        // before syncing the objective + POI, so a single rvstage fully sets up the marker.
        private static IEnumerator DebugEnsureQuestThenSync()
        {
            RepairQuest.StartIfNeeded();
            for (int i = 0; i < 60 && !RepairQuest.HasEntry(); i++) yield return null;
            SyncEntry();
        }

        // Debug: forget the reserved errand drops so the host re-reserves a FRESH one (nearest empty to the NPC) on
        // the next tick - used by rvclear after wiping accumulated test crates, so the drop lands near Ming again.
        internal static void DebugResetErrandDrop()
        {
            _cratePoint = Vector3.zero; _crateDrop = null;
            _dropPoint = Vector3.zero; _drop = null;
            Core.Log.Msg("[Debug] errand drop reservation reset.");
        }

        // Debug: force a second injection pass, optionally in degraded mode, so the fallback path (the one that runs
        // when S1API never hands us a dialogue container) can be exercised on demand. The existing choices stay, so
        // the NPC ends up with duplicates - this is a test command, not a repair.
        internal static void DebugReinject(bool degraded)
        {
            _donnaDone = _mingDone = _marcoDone = false;
            // Degraded only means something when the containers are actually missing, which cannot be provoked from
            // outside - so suppress them for this one pass and let the fallback branches run for real.
            _debugForceNoContainers = degraded;
            try { TryInject(degraded); }
            finally { _debugForceNoContainers = false; }
            Core.Log.Msg("[Debug] rvreinject degraded=" + degraded + " -> Donna=" + _donnaDone
                + " Ming=" + _mingDone + " Marco=" + _marcoDone
                + " choices=" + _injectedChoices + " skippedDead=" + _skippedChoices);
        }

        // Debug: one-shot state dump. Answers the two questions player reports keep raising - "the quest never
        // starts" (the gate) and "clicking the choice does nothing" (a choice with neither a Conversation nor an
        // action is a dead click, which is exactly what a missing dialogue container used to produce).
        internal static void DebugDiag()
        {
            try
            {
                Core.Log.Msg("[Debug] rvdiag ---------------------------------------------");
                Core.Log.Msg("  questline=" + RVRepairVanPreferences.QuestlineEnabled + " enabled=" + RVRepairVanPreferences.Enabled
                    + " online=" + NetworkBus.Online + " host=" + NetworkBus.IsServer);
                Core.Log.Msg("  stage=" + Stage + " active=" + Active + " repaired=" + RepairStateStore.GetRepaired()
                    + " rvDestroyed=" + RVManager.IsDestroyed() + " explosionBeat=" + ExplosionBeatPassed());
                Core.Log.Msg("  price=" + CurrentPrice() + " samples=" + Samples + " discount=" + DiscountTotal);
                Core.Log.Msg("  saveLoaded=" + Persistence.RepairSave.Loaded + " itemsRegistered=" + _itemsRegistered);
                Core.Log.Msg("  crateDrop=" + (_crateDrop != null ? _crateDrop.Name : "<none>") + " placed=" + _cratePlaced
                    + " | pkgDrop=" + (_drop != null ? _drop.Name : "<none>") + " placed=" + _pkgPlaced);
                Core.Log.Msg("  injected: Donna=" + _donnaDone + " Ming=" + _mingDone + " Marco=" + _marcoDone
                    + " degraded=" + _injectedDegraded
                    + " choices=" + _injectedChoices + " skippedDead=" + _skippedChoices);
                DumpChoices("Donna", _donnaDC);
                DumpChoices("Ming", _mingDC);
                DumpChoices("Marco", _marcoDC);
                Core.Log.Msg("[Debug] rvdiag ---------------------------------------------");
            }
            catch (Exception e) { Core.Log.Warning("[Debug] rvdiag failed: " + e.Message); }
        }

        // Reads the choices back off the live DialogueController (not our own bookkeeping) so the dump shows what
        // the game actually has - including anything another mod added to the same NPC.
        private static void DumpChoices(string who, DialogueController dc)
        {
            if (dc == null) { Core.Log.Msg("  " + who + ": no DialogueController."); return; }
            var list = dc.Choices;
            if (list == null) { Core.Log.Msg("  " + who + ": Choices list null."); return; }
            Core.Log.Msg("  " + who + ": " + list.Count + " choice(s)");
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null) continue;
                // Runtime AddListener calls are invisible from here (only serialized listeners are countable), so this
                // dump shows structure only - whether a choice actually does something is enforced in AddChoice.
                bool shown = false;
                try { shown = c.shouldShowCheck == null || c.shouldShowCheck.Invoke(false); }
                catch (Exception e) { Core.Log.Warning("[Debug] shouldShowCheck threw for '" + c.ChoiceText + "': " + e.Message); }
                Core.Log.Msg("    [" + i + "] '" + c.ChoiceText + "' prio=" + c.Priority + " shown=" + shown + " enabled=" + c.Enabled
                    + " conv=" + (c.Conversation != null) + " onChoosen=" + (c.onChoosen != null));
            }
        }

        /// <summary>Debug: open an NPC's dialogue the way the talk key does, so the choice list shows in its real
        /// order. The bridge's interact command reaches Marco's pickpocket interaction instead.</summary>
        internal static void DebugOpen(string who)
        {
            DialogueController dc = who == "donna" ? _donnaDC : who == "ming" ? _mingDC : who == "marco" ? _marcoDC : null;
            if (dc == null) { Core.Log.Warning("[Debug] rvpick open: no dialogue for '" + who + "'."); return; }
            dc.StartGenericDialogue();
            Core.Log.Msg("[Debug] rvpick open: " + who + "'s dialogue started.");
        }

        /// <summary>Debug: fire the i-th choice of an NPC (as listed by rvdiag) the way a click would - its action
        /// runs, its reply container does not open. Covers the state flow behind each dialogue line.</summary>
        internal static void DebugPick(string who, int index)
        {
            DialogueController dc = who == "donna" ? _donnaDC : who == "ming" ? _mingDC : who == "marco" ? _marcoDC : null;
            if (dc == null || dc.Choices == null) { Core.Log.Warning("[Debug] rvpick: no dialogue for '" + who + "'."); return; }
            if (index < 0 || index >= dc.Choices.Count) { Core.Log.Warning("[Debug] rvpick: no choice " + index + " on " + who + "."); return; }
            var c = dc.Choices[index];
            Core.Log.Msg("[Debug] rvpick " + who + " [" + index + "] '" + c.ChoiceText + "'");
            if (c.onChoosen != null) c.onChoosen.Invoke();
        }
#endif

        /// <summary>After a save loads, re-create + re-sync the journal quest at the stored stage.</summary>
        private static IEnumerator RestoreCoroutine()
        {
            yield return new WaitForSeconds(4f);
            // On a co-op CLIENT the local save is not authoritative - the host's snapshot (RequestSnapshot ->
            // StageSync/RepairApplied) drives our journal instead, so don't restore from stale local state.
            if (NetworkBus.Online && !NetworkBus.IsServer) yield break;
            try { if (Active && Stage >= Started && Stage < Done) EnsureQuest(); }
            catch (Exception e) { Core.Log.Warning("[Questline] restore failed: " + e.Message); }
        }

        private static IEnumerator SetupCoroutine()
        {
            int myGen = _gen;
            int attempt = 0;
            bool reported = false;
            // Fast burst, then keep checking slowly the whole scene - NPCs like Donna spawn late.
            while (myGen == _gen && !(_donnaDone && _mingDone && _marcoDone))
            {
                yield return new WaitForSeconds(attempt < 20 ? 2f : 10f);
                attempt++;
                // Past the fast burst an NPC that is still missing has a real problem, not a timing one - inject in
                // degraded mode so the player gets a working (if plainer) questline instead of choices that do nothing.
                TryInject(degraded: attempt >= DEGRADED_AFTER_ATTEMPTS);
                if (!reported && attempt >= 20)
                {
                    reported = true;
                    Core.LogDebug($"[Questline] initial setup: Donna={_donnaDone} Ming={_mingDone} Marco={_marcoDone} (keeps retrying any missing)");
#if DEBUG
                    DumpNpcDiagnostics();
#endif
                }
            }
            if (myGen == _gen)
            {
                Core.LogDebug($"[Questline] all NPC dialogue injected: Donna={_donnaDone} Ming={_mingDone} Marco={_marcoDone}");
                // The NPC transforms are only resolved now. On a fresh load RestoreCoroutine's first SyncEntry runs on
                // a fixed ~4s timer - often BEFORE Marco/Ming exist - so MarcoPos() falls back to RvPos()/zero and the
                // quest POI (the wrench marker) is left pointing nowhere, never re-pointed. Re-sync once here with the
                // real positions (host/offline only; a co-op client's POI is driven by the host snapshot).
                try
                {
                    if (!(NetworkBus.Online && !NetworkBus.IsServer) && Active && Stage >= Started && Stage < Done)
                    {
                        EnsureQuest();
                        Core.LogDebug("[Questline] post-inject POI re-sync (Marco resolved=" + (_marcoT != null) + ").");
                    }
                }
                catch (Exception e) { Core.Log.Warning("[Questline] post-inject POI sync failed: " + e.Message); }
            }
        }

        // Inject each NPC independently, looked up by id in the game's NPC registry. `degraded` is the last resort
        // after the retry budget is spent: inject even though a dialogue container is missing, using direct actions
        // instead of the sub-menus, so the questline stays playable rather than showing choices that do nothing.
        private static void TryInject(bool degraded = false)
        {
            try
            {
                if (!_donnaDone)  _donnaDone  = TryOne(DonnaId,  out _donnaT,  ref _donnaDC, InjectDonna, degraded);
                if (!_mingDone)   _mingDone   = TryOne(MingId,   out _mingT,   ref _mingDC,  InjectMing,  degraded);
                if (!_marcoDone)  _marcoDone  = TryOne(MarcoId,  out _marcoT,  ref _marcoDC, InjectMarco, degraded);
            }
            catch (Exception e)
            {
                Core.Log.Warning("[Questline] inject attempt failed: " + e.Message);
            }
        }

        private static bool TryOne(string id, out Transform t, ref DialogueController held,
                                   Func<DialogueController, NPC, bool, bool> inject, bool degraded)
        {
            NPC n = FindNpc(id);
            DialogueController dc = ControllerOf(n, out t);
            // The controller can exist a moment before S1API's dialogue layer is ready, in which case the containers
            // come back null. Returning false here keeps the NPC in the retry loop instead of injecting dead choices.
            if (dc != null && inject(dc, n, degraded))
            {
                held = dc;
                if (degraded)
                {
                    // Worth a warning in release logs too: it is the single line that explains why a player's
                    // dialogue looks plainer than the wiki describes.
                    Core.Log.Warning("[Questline] '" + id + "' injected WITHOUT its dialogue containers - the questline "
                        + "works but replies show as floating lines instead of in-conversation menus.");
#if DEBUG
                    _injectedDegraded = true;
#endif
                }
                return true;
            }
#if DEBUG
            DiagOnce(id, n);
#endif
            return false;
        }

        private static bool InjectDonna(DialogueController donna, NPC npc, bool degraded)
        {
            var reply = S1Container(npc, "rv_donna", b => b
                .AddNode("ENTRY", L10n.T("I run a motel, not a garage. Try Mrs. Ming at the Chinese restaurant, ok? She knows people.")));
            if (reply == null && !degraded) return false;   // retry rather than inject a choice without its reply

            AddChoice(donna, L10n.T("My RV got blown up. Know anyone who can fix it?"), 90,
                () => Active && Stage == Started, OnAskDonna, reply);
            Core.LogDebug("[Questline] Donna dialogue injected" + (degraded ? " (degraded)." : "."));
            return true;
        }

        private static bool InjectMing(DialogueController ming, NPC npc, bool degraded)
        {
            // The errand offer is an in-dialogue sub-menu (accept / not now), no cash involved.
            var offer = S1Container(npc, "rv_ming_offer", b => b
                .AddNode("ENTRY",
                    L10n.T("Oh yes! Marco, at the docks. Very good mechanic. But first you do small favor for me, yes? Crate in a dead drop. You bring it here, I call Marco."),
                    c => c.Add("MING_ACCEPT", L10n.T("I'll get it."), "MING_ACCEPTED")
                          .Add("MING_DEFER", L10n.T("Not now."), "MING_DEFERRED"))
                .AddNode("MING_ACCEPTED", L10n.T("Very good! And do not open, yes?"))
                .AddNode("MING_DEFERRED", L10n.T("Okay. You come back when ready.")));

            var deliver = S1Container(npc, "rv_ming_deliver", b => b
                .AddNode("ENTRY", L10n.T("Very good! You are careful. I like careful. I call Marco now. Go see him at the docks, tell him Ming sends you.")));

            // Loss fallback: collected the drop but no longer holding the crate -> admit it, pay the fee or defer.
            var lost = S1Container(npc, "rv_ming_lost", b => b
                .AddNode("ENTRY", L10n.T(MingAngry),
                    c => c.Add("MING_PAY", L10n.T("Pay ${0}", LostPackageFee), null)
                          .Add("MING_DEFER_LOSS", L10n.T("I'll come back with the money."), "MING_LOSS_DEFER"))
                .AddNode("MING_LOSS_DEFER", L10n.T(MingShort)));

            if (!degraded && (offer == null || deliver == null || lost == null)) return false;

            OnPick(npc, "MING_ACCEPT", OnAcceptErrand);
            // Without its sub-menu this choice carries neither a Conversation nor an action, so picking it does
            // nothing at all. Accept the errand straight away instead and say Ming's line in the world.
            AddChoice(ming, L10n.T("Donna says you know someone who can fix my RV."), 92,
                () => Active && Stage == AskedDonna,
                offer != null ? null : (Action)(() =>
                {
                    WorldSay(_mingT, L10n.T("Oh yes! Marco, at the docks. Very good mechanic. But first you do small favor for me, yes? Crate in a dead drop. You bring it here, I call Marco."));
                    OnAcceptErrand();
                }),
                offer);

            AddChoice(ming, L10n.T("Here's your crate."), 91,
                () => Active && Stage == MingCrate && PlayerHasItem(CrateId), OnDeliverCrate, deliver);

            OnPick(npc, "MING_PAY", OnMingPayLoss);
            // Same dead-click risk as the offer: the fee is named in the label so paying directly is not a surprise.
            AddChoice(ming,
                lost != null ? L10n.T("I lost your crate.") : L10n.T("I lost your crate. (Pay ${0})", LostPackageFee), 90,
                () => Active && Stage == MingCrate && !PlayerHasItem(CrateId),
                lost != null ? null : (Action)(() => { WorldSay(_mingT, L10n.T(MingAngry)); OnMingPayLoss(); }),
                lost);
            Core.LogDebug("[Questline] Ming dialogue injected" + (degraded ? " (degraded)." : "."));
            return true;
        }

        private static bool InjectMarco(DialogueController marco, NPC npc, bool degraded)
        {
            // Build every container first: a choice whose only action is its Conversation is a dead click if the
            // container is missing, so we would rather retry the whole injection than inject half of it.
            var favourC = S1Container(npc, "rv_marco_favour", b => b
                .AddNode("ENTRY", L10n.T("Tell ya what. There's a package of mine in a dead drop. Bring it here and we'll talk.")));
            var gotpkgC = S1Container(npc, "rv_marco_gotpkg", b => b
                .AddNode("ENTRY", L10n.T("Much obliged. Here's the deal: bring me packaged product in your hand, and every piece comes off your bill.")));
            var lostpkgC = S1Container(npc, "rv_marco_lost", b => b
                .AddNode("ENTRY", L10n.T(MarcoAngry),
                    c => c.Add("MARCO_PAY", L10n.T("Pay ${0}", LostPackageFee), null)
                          .Add("MARCO_DEFER_LOSS", L10n.T("I'll come back with the money."), "MARCO_LOSS_DEFER"))
                .AddNode("MARCO_LOSS_DEFER", L10n.T(MarcoShort)));
            var bringInfoC = S1Container(npc, "rv_marco_bring", b => b
                .AddNode("ENTRY", BringInfoLine()));

            if (!degraded && (favourC == null || gotpkgC == null || lostpkgC == null || bringInfoC == null)) return false;

            // Mrs. Ming's name is in the first line the player can say, so the quote they hear is already the
            // referral price. Saves made before this change can sit at MarcoMet; they get the same choice.
            AddChoice(marco, L10n.T("Mrs. Ming sent me. Can you fix my RV?"), MarcoPriorityTop,
                () => Active && (Stage == Referred || Stage == MarcoMet), OnMarcoReferral);
            _marcoRepairChoice = AddChoice(marco, RepairChoiceText(), MarcoPriorityTop - 3,
                () => Active && ReferralUsed && Stage < Paid, OnMarcoRepair);

            // "Can you go any lower?" is one question with two answers: before the package run Marco names the
            // package (and starts the run), after it he names what knocks the bill down. Only one is ever visible.
            AddChoice(marco, L10n.T("Can you go any lower?"), MarcoPriorityTop - 4,
                () => Active && Stage == ReadyToPay && !_pickupActive, OnMarcoFavour, favourC);
            AddChoice(marco, L10n.T("Can you go any lower?"), MarcoPriorityTop - 4,
                () => Active && Trusted_ && Stage < Paid && HeldPackagedSlot() == null && CurrentPrice() > RVRepairVanPreferences.RepairPrice,
                bringInfoC != null ? null : (Action)(() => WorldSay(_marcoT, BringInfoLine())),
                bringInfoC);

            AddChoice(marco, L10n.T("Here's your package."), MarcoPriorityTop - 4,
                () => Active && _pickupActive && _hasPackage && PlayerHasItem(PackageId), OnGotPackage, gotpkgC);

            // Loss fallback: collected the drop but no longer holding the package -> admit it, pay the fee or defer.
            OnPick(npc, "MARCO_PAY", OnMarcoPayLoss);
            // Without the sub-menu this choice would be a dead click - name the fee in the label and pay directly.
            AddChoice(marco,
                lostpkgC != null ? L10n.T("I lost your package.") : L10n.T("I lost your package. (Pay ${0})", LostPackageFee), MarcoPriorityTop - 4,
                () => Active && _pickupActive && _hasPackage && !PlayerHasItem(PackageId),
                lostpkgC != null ? null : (Action)(() => { WorldSay(_marcoT, L10n.T(MarcoAngry)); OnMarcoPayLoss(); }),
                lostpkgC);

            // The product the player held when the conversation opened, as one choice. Shown live
            // (SampleChoiceVisible) only while trust is earned and the price is above the floor; its label is set to
            // that product's name + per-unit value right before it is drawn.
            for (int s = 0; s < MAX_SAMPLE_CHOICES; s++)
            {
                int idx = s;
                _sampleChoices[idx] = AddChoice(marco, L10n.T("Give Marco a packaged sample"), MarcoPriorityTop - 5 - idx,
                    () => SampleChoiceVisible(idx), () => OnGiveSample(idx));
            }

            // Marco's build-out shop: offered once the RV is standing again, in both quest modes. Each entry keeps
            // a live price label like the repair choice, so a settings change updates it without a restart.
            for (int u = 0; u < UpgradeOrder.Length; u++)
            {
                Base.RvUpgrade up = UpgradeOrder[u];
                _upgradeChoices[u] = AddChoice(marco, Base.RvUpgrades.ChoiceText(up), MarcoPriorityTop - 7 - u,
                    () => Base.RvUpgrades.Available(up), () => Base.RvUpgrades.Buy(up));
            }
            AddChoice(marco, L10n.T("Can you do more to the RV?"), MarcoPriorityTop - 11,
                () => Garage.RvGarage.AskMoreVisible(), () => SayMarco(Garage.RvGarage.AskMoreLine()));
            Core.LogDebug("[Questline] Marco dialogue injected" + (degraded ? " (degraded)." : "."));
            return true;
        }

        // The game's own NPC registry, matched by id - the lookup S1API itself uses. Reliable where
        // FindObjectsOfType<ConcreteNpc> is not (region streaming / concrete il2cpp subclasses).
        private static NPC FindNpc(string id)
        {
            try
            {
                var reg = NPCManager.NPCRegistry;
                if (reg == null) return null;
                for (int i = 0; i < reg.Count; i++)
                {
                    NPC n = reg[i];
                    if (n != null && string.Equals(n.ID, id, StringComparison.OrdinalIgnoreCase))
                        return n;
                }
            }
            catch (Exception e) { Core.Swallowed("Questline.FindNpc", e); }
            return null;
        }

        // Play an NPC's "hurt" voice line (his real VO) - used mid repair-cinematic so Marco grunts as if he hurt
        // himself. Positional at the NPC, so the player (standing at Marco) hears it.
        internal static void GruntNpc(NPC npc)
        {
            try { if (npc != null && npc.VoiceOverEmitter != null) npc.VoiceOverEmitter.Play(EVOLineType.Annoyed); }
            catch (Exception e) { Core.Log.Warning("[Questline] grunt failed: " + e.Message); }
        }

#if DEBUG
        // Debug: grunt the NPC nearest the player (so the cinematic debug toggle can demo the grunt anywhere).
        internal static void DebugGruntNearest()
        {
            try
            {
                var reg = NPCManager.NPCRegistry;
                if (reg == null) return;
                Vector3 p = PlayerPos();
                NPC best = null; float bd = float.MaxValue;
                for (int i = 0; i < reg.Count; i++)
                {
                    NPC n = reg[i];
                    if (n == null) continue;
                    try { float d = Dist(p, n.transform.position); if (d < bd) { bd = d; best = n; } } catch (Exception e) { Core.Swallowed("Questline.DebugGruntNearest.distance", e); }
                }
                GruntNpc(best);
            }
            catch (Exception e) { Core.Swallowed("Questline.DebugGruntNearest", e); }
        }
#endif

        // --- S1API native dialogue (works since S1API 3.0.5 fixed the NPC-loader crash) -------------------

        // Builds a real Conversation on the NPC via S1API and hands it back (found again in the handler)
        // so it can be attached to an injected choice's Conversation - the NPC's reply (and any sub-choices)
        // then plays IN the dialogue UI, the conversation stays open. Null -> graceful fallback to a plain choice.
        private static Conversation S1Container(NPC npc, string name, Action<S1API.Entities.Dialogue.DialogueContainerBuilder> build)
        {
            try
            {
#if DEBUG
                if (_debugForceNoContainers) return null;
#endif
                if (npc == null) { Core.LogDebug("[Questline] container '" + name + "': npc null."); return null; }
                var s1 = S1API.Entities.NPC.Get(npc.ID);
                if (s1 == null) { Core.LogDebug("[Questline] container '" + name + "': S1API NPC '" + npc.ID + "' not ready."); return null; }
                s1.Dialogue.BuildAndRegisterContainer(name, build);
                DialogueHandler h = npc.DialogueHandler;
                var list = h != null ? h.dialogueContainers : null;
                if (list == null) { Core.LogDebug("[Questline] container '" + name + "': DialogueHandler not ready."); return null; }
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && ((UnityEngine.Object)list[i]).name == name) return list[i];
                Core.LogDebug("[Questline] container '" + name + "': built but not found among " + list.Count + " containers.");
            }
            catch (Exception e) { Core.Log.Warning("[Questline] S1 container '" + name + "' failed: " + e.Message); }
            return null;
        }

        private static void OnPick(NPC npc, string label, Action cb)
        {
            try
            {
                if (npc == null) return;
                var s1 = S1API.Entities.NPC.Get(npc.ID);
                s1?.Dialogue.OnChoiceSelected(label, cb);
            }
            catch (Exception e) { Core.Log.Warning("[Questline] OnPick '" + label + "' failed: " + e.Message); }
        }

        /// <summary>Debug dump: registry size, every NPC id, and the detail for our quest NPCs.</summary>
        internal static void DumpNpcDiagnostics()
        {
            DumpState();
            try
            {
                var reg = NPCManager.NPCRegistry;
                if (reg == null) { Core.Log.Msg("[NPC-DIAG] NPCRegistry == null"); return; }
                string ids = "";
                for (int i = 0; i < reg.Count; i++)
                {
                    NPC n = reg[i];
                    if (n == null) continue;
                    try { ids += n.ID + " "; } catch (Exception e) { Core.Swallowed("Questline.DumpNpcDiagnostics.ID", e); ids += "(err) "; }
                }
                Core.Log.Msg("[NPC-DIAG] registry(" + reg.Count + "): " + ids);
                DiagTarget(DonnaId); DiagTarget(MingId); DiagTarget(MarcoId);
            }
            catch (Exception e) { Core.Log.Warning("[NPC-DIAG] dump failed: " + e.Message); }
        }

        /// <summary>Logs the exact runtime questline state - the evidence for "why doesn't it start / show".</summary>
        internal static void DumpState()
        {
            try
            {
                Core.Log.Msg("[STATE] Enabled=" + RVRepairVanPreferences.Enabled
                    + " Questline=" + RVRepairVanPreferences.QuestlineEnabled
                    + " Stage=" + Stage + " Samples=" + Samples + " Discount=" + DiscountTotal
                    + " Active(IsDestroyed)=" + Active + " ExplosionBeatPassed=" + ExplosionBeatPassed());
                LogQuest("Getting Started");
                LogQuest("Welcome to Hyland Point");
                try { Core.Log.Msg("[STATE] S1API NPC.All count = " + S1API.Entities.NPC.All.Count); }
                catch (Exception e) { Core.Log.Msg("[STATE] S1API NPC.All threw: " + e.Message); }
            }
            catch (Exception e) { Core.Log.Warning("[STATE] dump failed: " + e.Message); }
        }

        private static void LogQuest(string name)
        {
            try
            {
                var q = Il2CppScheduleOne.Quests.Quest.GetQuest(name);
                Core.Log.Msg("[STATE] Quest '" + name + "': " + (q == null ? "NOT FOUND" : ("state=" + q.State)));
            }
            catch (Exception e) { Core.Log.Msg("[STATE] Quest '" + name + "' lookup threw: " + e.Message); }
        }

        private static void DiagTarget(string id)
        {
            NPC n = FindNpc(id);
            if (n == null) { Core.Log.Msg("[NPC-DIAG] " + id + ": NOT in registry"); return; }
            string go = "?"; bool active = false; bool ctrl = false;
            try { var c = (Component)n; go = c.gameObject.name; active = c.gameObject.activeInHierarchy; } catch (Exception e) { Core.Swallowed("Questline.DiagTarget.gameObject", e); }
            try { ctrl = ControllerOf(n, out _) != null; } catch (Exception e) { Core.Swallowed("Questline.DiagTarget.controller", e); }
            Core.Log.Msg("[NPC-DIAG] " + id + ": IN registry, go='" + go + "' active=" + active + " controllerFound=" + ctrl);
        }

#if DEBUG
        private static readonly System.Collections.Generic.HashSet<string> _diagged = new System.Collections.Generic.HashSet<string>();
        private static void DiagOnce(string id, NPC n)
        {
            string key = id + "|" + (n == null ? "absent" : "noctrl");
            if (!_diagged.Add(key)) return;
            Core.Log.Msg("[NPC-DIAG] " + id + (n == null
                ? ": not in registry yet"
                : ": in registry but no DialogueController found"));
        }
#endif

        private static void ResetDiag()
        {
#if DEBUG
            _diagged.Clear();
#endif
        }

        private static DialogueController ControllerOf(NPC npc, out Transform t)
        {
            t = null;
            if (npc == null) return null;
            try
            {
                t = ((Component)npc).transform;
                DialogueHandler dh = npc.DialogueHandler;
                DialogueController dc = null;
                if (dh != null) dc = ((Component)dh).GetComponentInChildren<DialogueController>(true);
                if (dc == null) dc = ((Component)npc).GetComponentInChildren<DialogueController>(true);
                return dc;
            }
            catch (Exception e) { Core.Swallowed("Questline.ControllerOf", e); return null; }
        }

        // --- handlers -------------------------------------------------------

        private static void OnAskDonna()
        {
            if (RouteIntent(RvOp.AskDonna)) return;   // co-op client: let the host advance + replicate
            if (Stage != Started) return;             // host re-validates (rejects stale/out-of-order intents)
            // reply = rv_donna node; advance.
            Stage = AskedDonna;
            SyncEntry();
        }

        // Fired by the in-dialogue "MING_ACCEPT" sub-choice (reply = MING_ACCEPTED node). No cash - the crate
        // is already paid for. Marks a real dead drop near Ming as the pickup so there's always a marker.
        private static void OnAcceptErrand()
        {
            if (RouteIntent(RvOp.AcceptErrand)) return;   // co-op client: host reserves the drop + places the crate
            if (Stage != AskedDonna) return;
            Stage = MingErrand;
            _crateDrop = ReserveDeadDrop(_mingT != null ? _mingT.position : RvPos());
            _cratePoint = _crateDrop != null ? _crateDrop.Position
                : (_mingT != null ? _mingT.position + new Vector3(8f, 0f, 8f) : RvPos());
            _cratePlaced = PlaceItem(_crateDrop, CrateId);   // a real "Ming's Crate" to carry back
            SyncEntry();
        }

        private static void OnDeliverCrate()
        {
            // The crate leaves the ACTING player's own inventory (PlayerInventory is per-player) - do that locally
            // on whoever picked the choice, then ask the host to advance the shared stage. Unconditional (no-op if the
            // player is not holding it): _cratePlaced is reset to false on a client once the stage advances past the
            // drop, so gating the removal on it skipped it on the client (the crate stayed in the client's hotbar).
            RemovePlayerItem(CrateId);   // hand the crate over (acting player's hotbar)
            if (RouteIntent(RvOp.DeliverCrate)) return;
            HostDeliverCrate();   // host or offline
        }

        // Host-only (or offline) shared-state advance for the crate delivery - no inventory touch, so it is safe to
        // run when the host is merely processing a client's intent (the client already removed its own crate).
        private static void HostDeliverCrate()
        {
            // reply = rv_ming_deliver node. Ming refers you to Marco (price still 50k until you say her name).
            if (Stage != MingCrate) return;
            Stage = Referred;
            SyncEntry();
        }

        // Ming - you admitted losing her crate. Pay the fee to move on, or come back later.
        private static void OnMingPayLoss()
        {
            if (NetworkBus.Online && !NetworkBus.IsServer)
            {
                // Co-op client: the fee comes out of this player's cash, the host advances the stage.
                int ticket = ClientPayment.Charge(LostPackageFee, "Ming's fee");
                if (ticket == 0) { WorldSay(_mingT, L10n.T(MingShort)); return; }
                NetworkBus.SendToHost(RvOp.MingPayLoss, 0, ticket, LostPackageFee);
                return;
            }
            HostMingPayLoss(-1);
        }

        /// <summary>
        /// Host or offline. <paramref name="clientPaid"/> &lt; 0: the acting player is this one, so charge this
        /// player's cash. Otherwise a client already paid that much and only the stage moves. False = nothing happened.
        /// </summary>
        private static bool HostMingPayLoss(int clientPaid)
        {
            if (Stage != MingCrate) { Core.LogDebug("[Questline] Ming fee ignored - stage " + Stage + "."); return false; }
            if (clientPaid < 0)
            {
                if (S1API.Money.Money.GetCashBalance() < LostPackageFee) { WorldSay(_mingT, L10n.T(MingShort)); return false; }
                S1API.Money.Money.ChangeCashBalance(-LostPackageFee, true, true);
            }
            else if (clientPaid != LostPackageFee)
            {
                Core.Log.Warning("[Questline] Ming fee refused - client paid " + clientPaid + ", fee is " + LostPackageFee + ".");
                return false;
            }
            Stage = Referred;
            WorldSay(_mingT, L10n.T(MingPaid));
            SyncEntry();
            return true;
        }

        // Kept for a co-op client on an older build that still sends the greet intent: it now means the same as
        // naming Mrs. Ming, since there is no separate greeting step any more.
        private static void OnMarcoGreet() => OnMarcoReferral();

        private static void OnMarcoReferral()
        {
            if (RouteIntent(RvOp.MarcoReferral)) return;
            if (Stage != Referred && Stage != MarcoMet)
            {
                Core.LogDebug("[Questline] referral ignored - stage " + Stage + ".");
                return;
            }
            Stage = ReadyToPay;          // ReferralUsed -> the referral price applies
            RefreshRepairChoice();
            WorldSay(_marcoT, L10n.T("Ming sent ya? Well, any friend of hers. That RV's a burnt-out shell, buddy - another week and she'd have gone to the crusher. Normally that's {0}. For you, {1}.",
                MoneyManager.FormatAmount(RVRepairVanPreferences.BasePriceNoReferral), MoneyManager.FormatAmount(CurrentPrice())));
            SyncEntry();
        }

        private static string ShortLine(int price) =>
            L10n.T("That's {0}, buddy. Come back when you've got it.", MoneyManager.FormatAmount(price));

        // The answer to "Can you go any lower?" once Marco trusts you.
        private static string BringInfoLine() =>
            L10n.T("Packaged product, buddy. Have it in your hand when you talk to me - every piece knocks up to {0} off the bill.",
                MoneyManager.FormatAmount(RVRepairVanPreferences.MaxSampleDiscount));

        private static void OnMarcoRepair()
        {
            try
            {
                if (!RVManager.IsDestroyed()) { WorldSay(_marcoT, L10n.T("Your RV looks fine to me.")); return; }
                int price = CurrentPrice();
                if (S1API.Money.Money.GetCashBalance() < price)
                {
                    WorldSay(_marcoT, ShortLine(price));
                    return;
                }
                WorldSay(_marcoT, L10n.T("Alright, let me grab my tools."));

                // Co-op CLIENT: the repair (RV state + persistence) runs on the HOST, since RV.IsDestroyed is
                // host-authoritative. The money is this player's own cash, so it is taken here and the host refunds
                // it if it refuses. Play the cinematic locally; the host's RepairApplied swaps the RV during the black.
                if (NetworkBus.Online && !NetworkBus.IsServer)
                {
                    int ticket = ClientPayment.Charge(price, "repair");
                    if (ticket == 0) { WorldSay(_marcoT, ShortLine(price)); return; }
                    NetworkBus.SendToHost(RvOp.PayRepair, 0, ticket, price);
                    RVRepairVan.Effects.RepairCinematic.Play(
                        null,   // no local field write - the host drives the visual via RepairApplied
                        () => WorldSay(_marcoT, L10n.T("There she is, good as new. Try not to blow her up again, buddy.")),
                        () => GruntNpc(FindNpc(MarcoId)));
                    return;
                }

                // Co-op HOST: authoritative repair WITH the cinematic (the host player is the one acting).
                if (NetworkBus.IsServer) { HostPayRepair(true); return; }

                // Offline single-player: unchanged. Commit payment synchronously (so an interrupted cinematic can
                // never double-charge), then play the repair cinematic and swap the RV while hidden.
                S1API.Money.Money.ChangeCashBalance(-price, true, true);
                int paid = price;
                RVRepairVan.Effects.RepairCinematic.Play(
                    () =>   // at the darkest point, with the swap hidden
                    {
                        if (RVManager.Repair())
                        {
                            RepairStateStore.SetRepaired(true);
                            Stage = Paid;
                            SyncEntry();   // objective -> Check on the RV
                            Core.Log.Msg("[Questline] RV repaired for " + MoneyManager.FormatAmount(paid) + ".");
#if SNITCH
                            Snitch.Api.Profiler.Log("RVRepairVan", "RV repaired for " + MoneyManager.FormatAmount(paid) + " (stage -> Paid).");
#endif
                        }
                    },
                    () => WorldSay(_marcoT, L10n.T("There she is, good as new. Try not to blow her up again, buddy.")),
                    () => GruntNpc(FindNpc(MarcoId)));   // mid-repair: Marco hurts himself
            }
            catch (Exception e) { Core.Log.Error("[Questline] repair failed: " + e); }
        }

        private static void OnMarcoFavour()
        {
            if (RouteIntent(RvOp.MarcoFavour)) return;   // host reserves the drop + places the package
            if (Stage != ReadyToPay || _pickupActive) return;
            _pickupActive = true;
            _hasPackage = false;
            // Point the player at a REAL game dead drop near Marco (its own world marker). Reply = rv_marco_favour.
            _drop = ReserveDeadDrop(_marcoT != null ? _marcoT.position : RvPos());
            _dropPoint = _drop != null ? _drop.Position
                : (_marcoT != null ? _marcoT.position + new Vector3(10f, 0f, 10f) : RvPos());
            _pkgPlaced = PlaceItem(_drop, PackageId);   // a real "Marco's Package" to carry back
            SyncEntry();
        }

        private static void OnGotPackage()
        {
            // Package leaves the ACTING player's own inventory locally, then the host advances the shared stage.
            // Unconditional (no-op if not held) - _pkgPlaced is unreliable on a client once the stage moves on.
            RemovePlayerItem(PackageId);   // hand the package over (acting player's hotbar)
            if (RouteIntent(RvOp.GotPackage)) return;
            HostGotPackage();   // host or offline
        }

        private static void HostGotPackage()
        {
            if (!(_pickupActive && _hasPackage)) return;
            _pickupActive = false;
            _hasPackage = false;
            _drop = null;
            Stage = Trusted;   // packaged samples unlocked. reply = rv_marco_gotpkg node.
            SyncEntry();
        }

        // Marco - you admitted losing his package. Pay the fee to move on, or come back later.
        private static void OnMarcoPayLoss()
        {
            if (NetworkBus.Online && !NetworkBus.IsServer)
            {
                // Co-op client: the fee comes out of this player's cash, the host advances the stage.
                int ticket = ClientPayment.Charge(LostPackageFee, "Marco's fee");
                if (ticket == 0) { WorldSay(_marcoT, L10n.T(MarcoShort)); return; }
                NetworkBus.SendToHost(RvOp.MarcoPayLoss, 0, ticket, LostPackageFee);
                return;
            }
            HostMarcoPayLoss(-1);
        }

        /// <summary>Host or offline; same contract as <see cref="HostMingPayLoss"/>.</summary>
        private static bool HostMarcoPayLoss(int clientPaid)
        {
            if (!(_pickupActive && _hasPackage)) { Core.LogDebug("[Questline] Marco fee ignored - no lost package."); return false; }
            if (clientPaid < 0)
            {
                if (S1API.Money.Money.GetCashBalance() < LostPackageFee) { WorldSay(_marcoT, L10n.T(MarcoShort)); return false; }
                S1API.Money.Money.ChangeCashBalance(-LostPackageFee, true, true);
            }
            else if (clientPaid != LostPackageFee)
            {
                Core.Log.Warning("[Questline] Marco fee refused - client paid " + clientPaid + ", fee is " + LostPackageFee + ".");
                return false;
            }
            _pickupActive = false;
            _hasPackage = false;
            _drop = null;
            Stage = Trusted;
            WorldSay(_marcoT, L10n.T(MarcoPaid));
            SyncEntry();
            return true;
        }

        // Give Marco one packaged product. i indexes the snapshot the choice list was built from (RefreshSampleSlots),
        // so the player hands over exactly the product they picked; falls back to the first packaged product if the
        // index is stale. Reads from inventory (not the equipped item, which is null during a conversation).
        private static void OnGiveSample(int i)
        {
            try
            {
                RefreshSampleSlots();
                ItemSlot slot = (i >= 0 && i < _sampleSlots.Count) ? _sampleSlots[i] : HeldPackagedSlot();
                ProductItemInstance product = slot?.ItemInstance?.TryCast<ProductItemInstance>();
                if (product == null || product.AppliedPackaging == null)
                {
                    WorldSay(_marcoT, L10n.T("That ain't packaged, buddy. Bring it sealed."));
                    return;
                }
                int discount = SampleUnitDiscount(product);
                string thanks = ThanksLine(product, discount);

                // Marco actually consumes it: NPCBehaviour.ConsumeProduct_Server (the ServerRpc the vanilla sample flow uses)
                // both sets the product AND enables the consume behaviour, so he plays the smoke/snort/eat animation,
                // sound and particles and the product's effects apply to him - and it replicates in co-op. Earlier we
                // only called SendProduct (sets the product, never starts the behaviour), so nothing happened visibly.
                // removeFromInventory:FALSE - that flag only touches the NPC's own inventory; we remove exactly one
                // unit from the player ourselves below (deterministic; protects a stack > 1).
                NPC marco = FindNpc(MarcoId);
                int before = slot != null ? slot.Quantity : -1;
                if (marco != null && marco.Behaviour != null) marco.Behaviour.ConsumeProduct_Server(product, false);
                RemoveOneFromSlot(slot);   // exactly one from the slot we found (clears it if it was the last)
                int after = slot != null ? slot.Quantity : -1;
                Core.LogDebug("[Questline] sample given: hotbar qty " + before + " -> " + after + " (expected -1).");

                // The consume above is the ACTING player's local action (per-player inventory; ConsumeProduct is a
                // ServerRpc so Marco's eating replicates). The DISCOUNT is shared state - the host owns it. Client:
                // send the discount it computed and let the host apply + replicate the new price.
                if (RouteIntent(RvOp.GiveSample, discount))
                {
                    WorldSay(_marcoT, thanks);
                    return;
                }
                HostGiveSample(discount, thanks);   // host or offline
            }
            catch (Exception e) { Core.Log.Warning("[Questline] give sample failed: " + e.Message); }
        }

        // Per-sample discount = the value of the ONE package handed over, NOT the whole stack. GetMonetaryValue() is
        // MarketValue * Quantity * Amount (scales with stack size), so divide by Quantity to get a single unit's
        // worth, then clamp to the configured min/max.
        private static int SampleUnitDiscount(ProductItemInstance p)
        {
            int qty = Mathf.Max(1, ((BaseItemInstance)p).Quantity);
            // Per-package value (GetMonetaryValue already folds in product type + effects via MarketValue, and the
            // packaging size via Amount) times a Marco-specific quality bonus/penalty, finally clamped to min/max.
            float unit = (p.GetMonetaryValue() / qty) * QualityMultiplier(p.Quality);
            return Mathf.Clamp(Mathf.RoundToInt(unit), RVRepairVanPreferences.MinSampleDiscount, RVRepairVanPreferences.MaxSampleDiscount);
        }

        // Marco pays more for cleaner product, less for junk. The game's own monetary value is quality-independent,
        // so this is a Marco-only sweetener. EQuality order: Trash, Poor, Standard, Premium, Heavenly.
        private static float QualityMultiplier(EQuality q)
        {
            switch (q)
            {
                case EQuality.Trash: return 0.6f;
                case EQuality.Poor: return 0.8f;
                case EQuality.Premium: return 1.5f;
                case EQuality.Heavenly: return 2.0f;
                default: return 1.0f;   // Standard
            }
        }

        // Snapshot the slot the player held when the conversation opened, if it is packaged product. Rebuilt each
        // time the choice is evaluated/picked so it always reflects what the player holds right now.
        private static void RefreshSampleSlots()
        {
            _sampleSlots.Clear();
            ItemSlot held = HeldPackagedSlot();
            if (held != null) _sampleSlots.Add(held);
        }

        // Live visibility + label for the i-th sample choice. Shown only with trust earned and room above the floor;
        // refreshes the snapshot and (the game calls this right before drawing) sets this entry's label to the i-th
        // packaged product's name + per-unit value.
        private static bool SampleChoiceVisible(int i)
        {
            if (!(Active && Trusted_ && Stage < Paid && CurrentPrice() > RVRepairVanPreferences.RepairPrice)) return false;
            RefreshSampleSlots();
            if (i >= _sampleSlots.Count) return false;
            ProductItemInstance p = _sampleSlots[i]?.ItemInstance?.TryCast<ProductItemInstance>();
            if (p == null) return false;
            if (_sampleChoices[i] != null) _sampleChoices[i].ChoiceText = SampleChoiceText(p);
            return true;
        }

        private static string SampleChoiceText(ProductItemInstance p)
        {
            string name = L10n.T("product");
            try { ItemDefinition def = p.Definition; if (def != null) name = def.Name; } catch (Exception e) { Core.Swallowed("Questline.SampleChoiceText", e); }
            return L10n.T("Give Marco: {0} (-{1})", name, MoneyManager.FormatAmount(SampleUnitDiscount(p)));
        }

        // Host-only (or offline): apply a sample's discount to the shared price. Safe when the host is processing a
        // client's GiveSample intent (the client already consumed its own product) - no inventory touch here.
        private static void HostGiveSample(int discount, string thanks = null)
        {
            Samples = Samples + 1;
            DiscountTotal = DiscountTotal + discount;   // setter broadcasts the new price (StageSync) to all clients
            RefreshRepairChoice();
            WorldSay(_marcoT, thanks ?? L10n.T("Thanks, buddy. That's {0} off.", MoneyManager.FormatAmount(discount)));
        }

        // Premium or better meth gets Marco's chemistry line instead of the plain thanks.
        private static string ThanksLine(ProductItemInstance p, int discount)
        {
            string off = MoneyManager.FormatAmount(discount);
            try
            {
                ProductDefinition def = p.Definition != null ? p.Definition.TryCast<ProductDefinition>() : null;
                if (def != null && def.DrugType == EDrugType.Methamphetamine && p.Quality >= EQuality.Premium)
                {
                    Core.LogDebug("[Questline] Marco got " + p.Quality + " meth - chemistry line.");
                    return L10n.T("Ninety-nine percent pure? Where'd you learn chemistry, buddy? That's {0} off.", off);
                }
            }
            catch (Exception e) { Core.Swallowed("Questline.ThanksLine", e); }
            return L10n.T("Thanks, buddy. That's {0} off.", off);
        }

#if DEBUG
        /// <summary>Debug (rvthanks): the line Marco would say for the product in hand.</summary>
        internal static void DebugThanks()
        {
            PlayerInventory inv = PlayerSingleton<PlayerInventory>.Instance;
            ProductItemInstance p = inv?.equippedSlot?.ItemInstance?.TryCast<ProductItemInstance>();
            if (p == null) { Core.Log.Warning("[Debug] rvthanks: no product in hand."); return; }
            Core.Log.Msg("[Debug] rvthanks " + ((BaseItemInstance)p).ID + " " + p.Quality + ": " + ThanksLine(p, 250));
        }
#endif

        // Reserve a real (preferably empty) dead drop for a pickup step. Normally the FARTHEST drop from the
        // origin (a proper errand); in DEBUG the CLOSEST (fast testing).
        private static S1API.DeadDrops.DeadDropInstance ReserveDeadDrop(Vector3 origin)
        {
            try
            {
                var pool = S1API.DeadDrops.DeadDropManager.Empty;
                if (pool == null || pool.Length == 0) pool = S1API.DeadDrops.DeadDropManager.All;
                if (pool == null || pool.Length == 0) return null;

                S1API.DeadDrops.DeadDropInstance pick = null;
#if DEBUG
                float best = float.MaxValue;   // closest
#else
                float best = -1f;              // farthest
#endif
                for (int i = 0; i < pool.Length; i++)
                {
                    var d = pool[i];
                    if (d == null) continue;
                    float dist = Vector3.Distance(origin, d.Position);
#if DEBUG
                    if (dist < best) { best = dist; pick = d; }
#else
                    if (dist > best) { best = dist; pick = d; }
#endif
                }
                if (pick != null)
                    Core.LogDebug("[Questline] dead drop: '" + pick.Name + "' at " + pick.Position + " (dist " + best.ToString("F0") + ")");
                return pick;
            }
            catch (Exception e) { Core.Log.Warning("[Questline] reserve dead drop failed: " + e.Message); return null; }
        }

        // --- quest items (real carried packages placed in the dead drops) ---

        private static void EnsureItems()
        {
            if (_itemsRegistered) return;
            try
            {
                // Register on BOTH host AND client (called from a startup coroutine, not only the host's PlaceItem).
                // The CLIENT must register these too or it cannot DESERIALISE the crate/package the host placed into
                // the networked dead-drop storage - the drop then looks empty to the client (vanilla items work
                // precisely because they are registered everywhere). Wait until the game's own items are loaded so
                // CloneFrom has a real base (registering too early would lock in a model-less fallback).
                if (S1API.Items.ItemManager.GetDefinition("grainbag") == null) return;   // game items not ready yet
                DumpItemsOnce();   // DEBUG only: list every item id/name (clone-base reference)
                // Carry-only bases (a sack / a bag), NOT furniture - inherits a real icon + in-hand model.
                // Tried in order; first that clones wins. grainbag/trashbag are StorableItemDefinition subclasses.
                RegisterItem(CrateId, L10n.T("Mrs. Ming's Crate"), L10n.T("A sealed crate for Mrs. Ming, stamped 'Golden Moth Chemical'. Don't open it."),
                    new[] { "grainbag", "trashbag", "flashlight" });
                RegisterItem(PackageId, L10n.T("Marco's Package"), L10n.T("Marco's package. Smells like fried chicken. Bring it to him at the docks."),
                    new[] { "trashbag", "grainbag", "flashlight" });
                _itemsRegistered = true;
                Core.LogDebug("[Questline] quest items registered (host+client).");
            }
            catch (Exception e) { Core.Log.Warning("[Questline] item register failed: " + e.Message); }
        }

        // Register the quest items as soon as the game's items are loaded - on the CLIENT too, so it can reconstruct
        // them from the dead-drop storage the host fills. Retries because game items load a moment after scene load.
        private static IEnumerator EnsureItemsCoroutine()
        {
            for (int i = 0; i < 30 && !_itemsRegistered; i++)
            {
                EnsureItems();
                if (_itemsRegistered) yield break;
                yield return new WaitForSeconds(1f);
            }
        }

        // Register a quest item. We CLONE an existing storable item (inherits a real inventory icon + in-hand
        // model + world mesh via CopyPropertiesFrom) and just override id/name/desc - a code-built item with no
        // model shows a blank icon, nothing in-hand, and risks an NRE on equip. baseIds = candidate source items
        // (carry-only), tried in order; CloneFrom throws if a base isn't a StorableItemDefinition, so we skip to
        // the next. Fallback = a model-less item so registration never fails.
        private static void RegisterItem(string id, string name, string desc, string[] baseIds)
        {
            if (S1API.Items.ItemManager.GetDefinition(id) != null) return;

            S1API.Items.Storable.StorableItemDefinition def = null;
            foreach (var baseId in baseIds)
            {
                try
                {
                    def = S1API.Items.Storable.ItemCreator.CloneFrom(baseId)
                        .WithBasicInfo(id, name, desc, S1API.Items.ItemCategory.Tools)
                        .WithStackLimit(1)
                        .Build();   // Build() already registers (AddToRegistry) - no separate RegisterItem
                    Core.LogDebug("[Questline] quest item '" + id + "' cloned from base '" + baseId + "'.");
                    break;
                }
                catch (Exception e) { Core.Log?.Warning("[Questline] CloneFrom('" + baseId + "') failed: " + e.Message); }
            }

            if (def == null)
            {
                // No candidate base cloned - register a plain item (works, but blank icon / no in-hand model).
                def = S1API.Items.Storable.ItemCreator.CreateItem(id, name, desc, S1API.Items.ItemCategory.Tools, 1);
                Core.Log.Warning("[Questline] quest item '" + id + "' registered WITHOUT a model/icon (no clone base usable).");
            }

            try { S1API.Items.ItemManager.PreserveRuntimeItem(def); } catch (Exception e) { Core.Swallowed("Questline.RegisterItem", e); }
        }

        // One-time dump of every registered item (DEBUG builds only) - so we can read real ids/names from the
        // log and confirm which clone base got picked. Compiles out entirely in Release.
        private static bool _itemsDumped;
        [System.Diagnostics.Conditional("DEBUG")]
        private static void DumpItemsOnce()
        {
            if (_itemsDumped) return;
            _itemsDumped = true;
            try
            {
                var all = S1API.Items.ItemManager.GetAllItemDefinitions();
                Core.Log.Msg("[ITEMDUMP] " + (all != null ? all.Count : 0) + " registered items:");
                if (all != null)
                    for (int i = 0; i < all.Count; i++)
                    {
                        var d = all[i];
                        if (d == null) continue;
                        try { Core.Log.Msg("[ITEMDUMP] id='" + d.ID + "' name='" + d.Name + "' cat=" + d.Category + " icon=" + (d.Icon != null)); }
                        catch (Exception e) { Core.Swallowed("Questline.DumpItemsOnce", e); }
                    }
            }
            catch (Exception e) { Core.Log.Warning("[ITEMDUMP] failed: " + e.Message); }
        }

        // Place one unit of the item into the drop's storage. Returns true on success.
        private static bool PlaceItem(S1API.DeadDrops.DeadDropInstance drop, string id)
        {
            try
            {
                if (drop == null) return false;
                EnsureItems();
                var def = S1API.Items.ItemManager.GetDefinition(id);
                if (def == null) return false;
                drop.Storage.AddItem(def.CreateInstance(1));
                Core.LogDebug("[Questline] placed '" + id + "' in drop '" + drop.Name + "'.");
                return true;
            }
            catch (Exception e) { Core.Log.Warning("[Questline] place item failed: " + e.Message); return false; }
        }

        // Does the player currently hold the item (any hotbar slot)?
        private static bool PlayerHasItem(string id)
        {
            try
            {
                PlayerInventory inv = PlayerSingleton<PlayerInventory>.Instance;
                var slots = inv != null ? inv.hotbarSlots : null;
                if (slots == null) return false;
                for (int i = 0; i < slots.Count; i++)
                {
                    ItemSlot slot = slots[i];
                    ItemInstance item = slot != null ? slot.ItemInstance : null;
                    if (item != null && string.Equals(((BaseItemInstance)item).ID, id, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (Exception e) { Core.Swallowed("Questline.PlayerHasItem", e); }
            return false;
        }

        private static void RemovePlayerItem(string id)
        {
            try
            {
                PlayerInventory inv = PlayerSingleton<PlayerInventory>.Instance;
                var slots = inv != null ? inv.hotbarSlots : null;
                if (slots == null) { Core.Log.Warning("[Questline] remove '" + id + "': no hotbar slots."); return; }
                for (int i = 0; i < slots.Count; i++)
                {
                    ItemSlot slot = slots[i];
                    ItemInstance item = slot != null ? slot.ItemInstance : null;
                    if (item != null && string.Equals(((BaseItemInstance)item).ID, id, StringComparison.OrdinalIgnoreCase))
                    {
                        RemoveOneFromSlot(slot);
                        Core.LogDebug("[Questline] removed one '" + id + "' from hotbar slot " + i + ".");
                        return;
                    }
                }
                Core.Log.Warning("[Questline] remove '" + id + "': not found in hotbar.");
            }
            catch (Exception e) { Core.Log.Warning("[Questline] remove item failed: " + e.Message); }
        }

        // Remove exactly one unit from a slot. ChangeQuantity(-1) on a quantity-1 item leaves an empty ghost
        // slot (the item never disappears) - so for the last unit we must ClearStoredInstance to fully empty it.
        private static void RemoveOneFromSlot(ItemSlot slot)
        {
            if (slot == null) return;
            ItemInstance item = slot.ItemInstance;
            int qty = item != null ? ((BaseItemInstance)item).Quantity : 0;
            if (qty > 1) slot.ChangeQuantity(-1);
            else slot.ClearStoredInstance(false);
        }

        // --- proximity (errand pickups + the closing "check on the RV") ------

        private static IEnumerator ProximityCoroutine()
        {
            int myGen = _gen;
            int waitedForLoad = 0;
            while (myGen == _gen)
            {
                yield return new WaitForSeconds(1f);

                // Wait for our save state to load before doing anything stateful, so we never read stale/default
                // values right after a scene load (which could e.g. auto-start a quest the save already completed).
                // Our state is now part of the game save (RepairSave), so it is always consistent with the actual
                // RV/quest world - no desync reconciliation needed. Fallback: proceed after ~10s so a missing/late
                // load never hangs the questline forever.
                if (!RepairSave.Loaded)
                {
                    if (++waitedForLoad < 10) continue;
                    if (waitedForLoad == 10) Core.Log.Warning("[Questline] save state not loaded after 10s - proceeding with current values.");
                }

                // Co-op CLIENT: the HOST owns all stage advancement (auto-start, dead-drop pickups, the closing
                // beat) and replicates it via StageSync/TransientSync. A client only renders - it must never write
                // Stage/transient here, or it would fight the host's authoritative state. The ONE exception is the
                // post-repair "check on the RV" beat: the client REPORTS reaching it (once) so either player can
                // finish the quest, and the host validates + completes for everyone. Offline + host run it all.
                if (NetworkBus.Online && !NetworkBus.IsServer)
                {
                    if (Stage == Paid && !_clientCheckedRv
                        && RVManager.TryGetPosition(out Vector3 crvp) && Dist(PlayerPos(), crvp) < 14f)
                    {
                        _clientCheckedRv = true;
                        NetworkBus.SendToHost(RvOp.CheckedRv);
                    }
                    // The client picked up the current errand item -> tell the host (it can't see the client's
                    // inventory) so it advances for everyone. Stops once the host's StageSync/TransientSync clears the
                    // condition (~1-2 sends); the host re-validates and ignores it if the stage already moved on.
                    else if ((Stage == MingErrand && PlayerHasItem(CrateId))
                          || (_pickupActive && !_hasPackage && PlayerHasItem(PackageId)))
                    {
                        NetworkBus.SendToHost(RvOp.ErrandItemPicked);
                    }
                    continue;
                }

                // Auto-start: an ACTUAL wrecked RV must exist (IsDestroyed) AND the story must be past the explosion
                // beat ("Getting Started" active / "Welcome to Hyland Point" completed). Requiring IsDestroyed means
                // an intact/repaired RV never spawns a flickering quest, while a fresh install on a post-explosion
                // save (wrecked RV, our quest never played) still picks it up. Because the state is save-bound, a
                // completed-but-not-saved run reloads with repaired=false/Stage=None here and correctly restarts.
                if (RVRepairVanPreferences.Enabled && Stage == None && !RepairStateStore.GetRepaired()
                    && RVManager.IsDestroyed() && ExplosionBeatPassed())
                {
                    Stage = Started;
                    EnsureQuest();
                    Core.Log.Msg("[Questline] quest started (wrecked RV + explosion beat passed).");
                }

                // Closing beat: after paying, the RV shell is restored (so Active is now false) - go look at it.
                // Checked BEFORE the Active guard for exactly that reason. Also fired by a client's CheckedRv intent.
                if (Stage == Paid && RVManager.TryGetPosition(out Vector3 rvp) && Dist(PlayerPos(), rvp) < 14f)
                {
                    HostCheckedRv();
                }

                // A repaired RV is the end of the story. A stage still mid-way (a debug repair) or a journal entry still
                // open at Done (a save from a build that left it open) shows "Back on the road" with its marker on an
                // intact RV, so close both.
                if (RepairStateStore.GetRepaired() && !Base.RvRepairJob.JobRunning)
                {
                    if (Stage >= Started && Stage < Paid)
                    {
                        Core.Log.Msg("[Questline] RV is already repaired at stage " + Stage + " - finishing the story.");
                        Stage = Done;
                    }
                    if (Stage == Done && RepairQuest.HasOpenEntry())
                    {
                        Core.Log.Msg("[Questline] story is finished but the journal entry is still open - closing it.");
                        RepairQuest.CompleteIfActive();
                    }
                }

                if (!Active) continue;
                try
                {
                    Vector3 p = PlayerPos();

                    // Ming's crate: collected once the player empties the marked drop (takes the real item).
                    // Fallback to proximity when no item could be placed. Re-reserve + re-place after a reload - but
                    // WAIT for Ming's transform: on load this loop runs before NPC injection finishes, and reserving
                    // with the RV-position fallback picked a drop across the map instead of the one nearest Ming.
                    if (Stage == MingErrand)
                    {
                        if (_cratePoint == Vector3.zero && _mingT != null)
                        {
                            _crateDrop = ReserveDeadDrop(_mingT.position);
                            _cratePoint = _crateDrop != null ? _crateDrop.Position : _mingT.position + new Vector3(8f, 0f, 8f);
                            _cratePlaced = PlaceItem(_crateDrop, CrateId);
                            SyncEntry();
                        }
                        // "Collected" = a player now HOLDS the crate (robust + works for either player), NOT the drop's
                        // IsEmpty (which proved fragile across reloads / co-op - stale wrappers, re-sync, leftovers).
                        // The host detects its own pickup here; a client reports its pickup via the ErrandItemPicked
                        // intent (handled host-side). Proximity stays the no-item fallback.
                        bool got = _cratePlaced ? PlayerHasItem(CrateId)
                                                : (_cratePoint != Vector3.zero && Dist(p, _cratePoint) < 5f);
                        if (got) { Stage = MingCrate; SyncEntry(); }
                    }

                    // Marco's package: collected once the player empties the drop (real item), else proximity.
                    if (_pickupActive && !_hasPackage)
                    {
                        bool got = _pkgPlaced ? PlayerHasItem(PackageId)
                                              : (Dist(p, _dropPoint) < 5f);
                        if (got) { _hasPackage = true; SyncEntry(); }
                    }
                }
                catch (Exception e) { Core.Swallowed("Questline.ProximityCoroutine", e); /* never break the loop */ }
            }
        }

        // --- quest journal entry --------------------------------------------

        private static void EnsureQuest()
        {
            RepairQuest.StartIfNeeded();
            SyncEntry();
        }

        private static void SyncEntry()
        {
            string title;
            string npcId = null;   // set when the marker should follow an NPC live instead of a fixed point
            Vector3 poi;

            if (_pickupActive)
            {
                title = _hasPackage ? L10n.T("Bring the package to Marco") : L10n.T("Pick up Marco's package from the dead drop");
                if (_hasPackage) { npcId = MarcoId; poi = MarcoPos(); } else poi = _dropPoint;
            }
            else
            {
                switch (Stage)
                {
                    case Started:    title = L10n.T("Ask Donna at the motel about your RV"); npcId = DonnaId; poi = DonnaPos(); break;
                    case AskedDonna: title = L10n.T("Talk to Mrs. Ming at the Chinese restaurant"); npcId = MingId; poi = MingPos(); break;
                    case MingErrand: title = L10n.T("Pick up Mrs. Ming's crate from the dead drop"); poi = _cratePoint; break;
                    case MingCrate:  title = L10n.T("Bring the crate to Mrs. Ming"); npcId = MingId; poi = MingPos(); break;
                    case Referred:
                    case MarcoMet:   title = L10n.T("Tell Marco at the docks that Mrs. Ming sent you"); npcId = MarcoId; poi = MarcoPos(); break;
                    case ReadyToPay:
                    case Trusted:    title = L10n.T("Pay Marco for the repair"); npcId = MarcoId; poi = MarcoPos(); break;
                    case Paid:       title = L10n.T("Go see your RV"); poi = RvPos(); break;
                    default:         title = L10n.T("Find a way to repair your RV"); poi = RvPos(); break;
                }
            }

            // NPC stages: bind the marker to the NPC's live transform so it tracks them while they roam,
            // instead of freezing wherever they happened to be when the stage advanced. poi is the fallback
            // if the NPC can't be resolved yet.
            if (npcId != null) RepairQuest.UpdateEntry(title, FindNpc(npcId), poi);
            else RepairQuest.UpdateEntry(title, poi);

            // Host: re-broadcast the transient pickup/drop state on every entry change so clients render the same
            // objective text + dead-drop marker (clients reuse this exact SyncEntry, driven by the synced flags).
            if (NetworkBus.Online && NetworkBus.IsServer) HostBroadcastTransient();
        }

        // --- helpers --------------------------------------------------------

        // Adds a stage-conditional top-level choice (shouldShowCheck = the only way to gate visibility per
        // quest stage). If a Conversation container is given, picking it plays that conversation IN the dialogue
        // (NPC reply + any sub-choices, UI stays open). onChosen is the optional state action.
        private static DialogueController.DialogueChoice AddChoice(DialogueController dc, string text, int prio, Func<bool> show, Action onChosen, Conversation conv = null)
        {
            // A choice with neither a reply container nor an action does nothing when picked - the player clicks and
            // the conversation just sits there. That is what a missing S1API container used to produce, so refuse it
            // outright rather than shipping a dead option.
            if (conv == null && onChosen == null)
            {
                _skippedChoices++;
                Core.Log.Warning("[Questline] skipped a choice with neither reply nor action: '" + text + "'");
                return null;
            }
            _injectedChoices++;

            var choice = new DialogueController.DialogueChoice
            {
                Enabled = true,
                ChoiceText = text,
                Conversation = conv,
                Priority = prio
            };
            Func<bool, bool> pred = (_) => { try { return show(); } catch (Exception e) { Core.Swallowed("Questline.AddChoice.shouldShowCheck", e); return false; } };
            choice.shouldShowCheck = DelegateSupport.ConvertDelegate<DialogueController.DialogueChoice.ShouldShowCheck>(pred);
            choice.onChoosen = new UnityEvent();
            if (onChosen != null) choice.onChoosen.AddListener((Action)onChosen);
            dc.AddDialogueChoice(choice, prio);
            return choice;
        }

        /// <summary>The hotbar slot the player is holding, if it carries PACKAGED product, else null. A conversation
        /// unequips the hand (the dialogue state disables equipping), and PlayerInventory.SetEquippingEnabled(false)
        /// keeps that slot in PriorEquippedSlotIndex to re-equip it afterwards - so that index is "the hand" while
        /// Marco is talking. EquippedSlotIndex covers a call made outside a conversation.</summary>
        private static ItemSlot HeldPackagedSlot()
        {
            try
            {
                PlayerInventory inv = PlayerSingleton<PlayerInventory>.Instance;
                if (inv == null) return null;
                int idx = inv.EquippedSlotIndex >= 0 ? inv.EquippedSlotIndex : inv.PriorEquippedSlotIndex;
                if (idx < 0) return null;
                ItemSlot slot = inv.IndexAllSlots(idx);
                ProductItemInstance p = slot?.ItemInstance?.TryCast<ProductItemInstance>();
                return p != null && p.AppliedPackaging != null ? slot : null;
            }
            catch (Exception e) { Core.Swallowed("Questline.HeldPackagedSlot", e); return null; }
        }

        /// <summary>Say a line as Marco in the world. Used by the upgrade shop, which lives outside this class but
        /// speaks with Marco's voice and needs his transform, resolved here during NPC injection.</summary>
        internal static void SayMarco(string line) => WorldSay(_marcoT, line);

        private static void RefreshRepairChoice()
        {
            try { if (_marcoRepairChoice != null) _marcoRepairChoice.ChoiceText = RepairChoiceText(); }
            catch (Exception e) { Core.Swallowed("Questline.RefreshRepairChoice.repair", e); }
            // The build-out prices are settings too - keep their labels in step with the repair price.
            try
            {
                for (int i = 0; i < _upgradeChoices.Length && i < UpgradeOrder.Length; i++)
                    if (_upgradeChoices[i] != null) _upgradeChoices[i].ChoiceText = Base.RvUpgrades.ChoiceText(UpgradeOrder[i]);
            }
            catch (Exception e) { Core.Swallowed("Questline.RefreshRepairChoice.upgrades", e); }
        }

        internal static void RefreshPrice() => RefreshRepairChoice();

        private static string RepairChoiceText() => L10n.T("Repair my RV ({0})", MoneyManager.FormatAmount(CurrentPrice()));

        private static void WorldSay(Transform npc, string line)
        {
            try
            {
                if (npc == null) { Core.Log.Warning("[Say] no NPC to speak: " + line); return; }
                NPC n = npc.GetComponentInParent<NPC>();
                if (n == null) { Core.Log.Warning("[Say] '" + npc.name + "' has no NPC component: " + line); return; }
                n.SendWorldSpaceDialogue(line, 5f);
                Core.Log.Msg("[Say] " + n.FirstName + ": " + line);
            }
            catch (Exception e) { Core.Swallowed("Questline.WorldSay", e); /* worldspace lines are nice-to-have */ }
        }

        private static Vector3 PlayerPos()
        {
            try { var pl = Player.Local; if (pl != null) return pl.transform.position; } catch (Exception e) { Core.Swallowed("Questline.PlayerPos", e); }
            return Vector3.zero;
        }

        private static float Dist(Vector3 a, Vector3 b) => Vector3.Distance(a, b);
        private static Vector3 RvPos() { return RVManager.TryGetPosition(out Vector3 p) ? p : Vector3.zero; }
        private static Vector3 DonnaPos() => _donnaT != null ? _donnaT.position : RvPos();
        private static Vector3 MingPos() => _mingT != null ? _mingT.position : RvPos();
        private static Vector3 MarcoPos() => _marcoT != null ? _marcoT.position : RvPos();
    }
}
