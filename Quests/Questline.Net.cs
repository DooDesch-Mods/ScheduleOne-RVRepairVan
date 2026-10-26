using System;
using System.Collections;
using DooDesch.Localization;
using MelonLoader;
using RVRepairVan.Config;
using RVRepairVan.Managers;
using RVRepairVan.Net;
using RVRepairVan.Persistence;

namespace RVRepairVan.Quests
{
    /// <summary>
    /// Co-op networking glue for the questline (host-authoritative, "1:1" with base-game quests).
    ///
    /// Model: the HOST owns the canonical state (Stage / DiscountTotal / RV repair). A client never mutates
    /// shared state directly - it sends an INTENT (RvOp, client -> host via NetworkBus.SendToHost) and the host
    /// validates it against its own Stage, applies the effect (advance Stage, repair the RV), then broadcasts the
    /// resulting STATE (StageSync / RepairApplied, host -> all via BroadcastToAll). Money is per player: a client
    /// pays from its own cash before sending a paid intent, and the host answers with PaymentSettled (ClientPayment).
    /// Clients render that state (objective text/POI, price, RV visual) but are otherwise dumb terminals.
    ///
    /// Offline (NetworkBus.Online == false) NONE of this runs - every handler takes its original local branch,
    /// so single-player is byte-for-byte unchanged.
    /// </summary>
    internal static partial class Questline
    {
        private static bool _netWired;

        /// <summary>Wire the bus dispatch once (idempotent). Called from Start().</summary>
        internal static void InitNet()
        {
            if (_netWired) return;
            _netWired = true;
            NetworkBus.OnServerIntent = OnServerIntent;   // host: a client (or host-loopback) intent arrived
            NetworkBus.OnClientState = OnClientState;      // any client: host state arrived
        }

        /// <summary>
        /// Intent router used at the top of every shared-state handler. On a co-op CLIENT it forwards the action
        /// to the host as an intent and returns true (the caller must return - the host will apply + replicate).
        /// Offline or on the host it returns false, so the caller runs the body locally (and the host's Stage/
        /// DiscountTotal writes auto-broadcast). This is the single branch that separates "ask the host" from
        /// "I am the authority" - keeping every handler's offline path byte-for-byte unchanged.
        /// </summary>
        private static bool RouteIntent(RvOp op, int a = 0, int b = 0)
        {
            if (NetworkBus.Online && !NetworkBus.IsServer) { NetworkBus.SendToHost(op, a, b); return true; }
            return false;
        }

        // On join, a client cannot trust its local save - ask the host for the current snapshot once the
        // network session is up. The host answers with StageSync (+ RepairApplied if already repaired).
        /// <summary>Same join handshake, reachable from Simple mode - which has no questline to start it.</summary>
        internal static IEnumerator NetJoinCoroutineShared() => NetJoinCoroutine();

        private static IEnumerator NetJoinCoroutine()
        {
            // Wait until we're actually in a networked session and know our role (up to ~30s).
            for (int i = 0; i < 60; i++)
            {
                yield return new WaitForSeconds(0.5f);
                if (NetworkBus.Online) break;
            }
            if (!NetworkBus.Online || NetworkBus.IsServer) yield break;   // offline or host: nothing to request

            // Ask twice, a beat apart, in case the first rides in before the host's QuestManager is ready.
            NetworkBus.SendToHost(RvOp.RequestSnapshot, NetworkBus.VersionCode);
            yield return new WaitForSeconds(2f);
            NetworkBus.SendToHost(RvOp.RequestSnapshot, NetworkBus.VersionCode);
        }

        // ---- HOST: an intent arrived (from a client, or from the host itself via RPC loopback) ----
        private static void OnServerIntent(RvMsg m)
        {
            if (!NetworkBus.IsServer) return;   // only the host is authoritative
            try
            {
                // Each case runs the SAME handler body the host would run for its own action: on the host RouteIntent
                // returns false, so the body executes and its Stage/DiscountTotal writes auto-broadcast to clients.
                // Handlers with a per-player inventory effect dispatch to their Host* variant (no local inventory).
                switch (m.Op)
                {
                    case RvOp.AskDonna: OnAskDonna(); break;
                    case RvOp.AcceptErrand: OnAcceptErrand(); break;
                    case RvOp.DeliverCrate: HostDeliverCrate(); break;
                    case RvOp.MingPayLoss: Settle(m, HostMingPayLoss(PaidAmount(m))); break;
                    case RvOp.MarcoGreet: OnMarcoGreet(); break;
                    case RvOp.MarcoReferral: OnMarcoReferral(); break;
                    case RvOp.MarcoFavour: OnMarcoFavour(); break;
                    case RvOp.GotPackage: HostGotPackage(); break;
                    case RvOp.MarcoPayLoss: Settle(m, HostMarcoPayLoss(PaidAmount(m))); break;
                    case RvOp.GiveSample: HostGiveSample(m.A); break;   // A = discount the client computed
                    case RvOp.PayRepair: Settle(m, HostPayRepair(false, PaidAmount(m))); break;  // no cinematic on the host for a client's repair
                    case RvOp.CheckedRv: HostCheckedRv(); break;       // a client reached the post-repair RV
                    case RvOp.ErrandItemPicked: HostErrandItemPicked(); break;  // a client picked up the crate/package
                    case RvOp.BuyUpgrade: Settle(m, Base.RvUpgrades.HostBuy((Base.RvUpgrade)m.A, false, PaidAmount(m))); break;   // client is already playing its own cinematic
                    case RvOp.RequestSnapshot: NetworkBus.LogClientVersion(m.A); HostSendSnapshot(); break;
                    case RvOp.DriveRequest: Base.RvMobile.HostOnDriveRequest(); break;
                    case RvOp.MobileReport: Base.RvMobile.HostOnClientReport(m.A, m.B); break;
                    case RvOp.BatterySwap: Base.RvBattery.HostOnSwap(); break;
                    case RvOp.ArrestHome: Base.RvMobile.HostOnArrestHome(m.A); break;
#if DEBUG
                    case RvOp.Ping: Core.Log.Msg("[Net] host <- Ping " + m.A); break;
#endif
                }
            }
            catch (Exception e) { Core.Log.Warning("[Net] server intent " + m.Op + " failed: " + e.Message); }
        }

        // A paid intent carries B = the client's ticket and C = what it paid. Without a ticket nothing was paid, and
        // the host's own cash must not cover it - so it counts as paying 0, which every price check refuses.
        private static int PaidAmount(RvMsg m) => m.B != 0 ? Math.Max(0, m.C) : 0;

        /// <summary>Host: tell the paying client whether to keep its payment or take it back.</summary>
        private static void Settle(RvMsg m, bool applied)
        {
            if (m.B == 0) { Core.Log.Warning("[Net] paid intent " + m.Op + " arrived without a ticket - nothing to settle."); return; }
            NetworkBus.BroadcastToAll(RvOp.PaymentSettled, m.B, applied ? 0 : 1);
            Core.Log.Msg("[Net] " + m.Op + " ticket " + m.B + (applied ? " kept" : " refunded") + ".");
        }

        // ---- CLIENT: host state arrived ----
        private static void OnClientState(RvMsg m)
        {
            try
            {
                switch (m.Op)
                {
                    case RvOp.StageSync: ApplyStageSync(m.A, m.B); break;
                    case RvOp.TransientSync: ApplyTransient(m.A, m.B, m.C); break;
                    case RvOp.InteriorSync: Base.RvInterior.ApplySync(m.A, m.B); break;
                    case RvOp.UpgradeSync: Base.RvUpgrades.ApplySync(m.A); break;
                    case RvOp.MobileSync: Base.RvMobile.ApplySync(m.A); break;
                    case RvOp.BatterySync: Base.RvBattery.ApplySync(m.A); break;
                    case RvOp.RepairApplied: ApplyRepair(); break;
                    case RvOp.PaymentSettled: if (!NetworkBus.IsServer) ClientPayment.Settle(m.A, m.B != 0); break;
#if DEBUG
                    case RvOp.Ping: Core.Log.Msg("[Net] client <- Ping " + m.A); break;
                    case RvOp.DebugGiveItems: RVRepairVan.Patches.DebugConsolePatch.GiveTestProducts("jar", "ogkush"); break;
#endif
                }
            }
            catch (Exception e) { Core.Log.Warning("[Net] client state " + m.Op + " failed: " + e.Message); }
        }

        /// <summary>Host: replay the full current state to all clients (answer to RequestSnapshot / on change).</summary>
        private static void HostSendSnapshot()
        {
            NetworkBus.BroadcastToAll(RvOp.StageSync, Stage, DiscountTotal);
            HostBroadcastTransient();
            if (RepairStateStore.GetRepaired()) NetworkBus.BroadcastToAll(RvOp.RepairApplied);
            // Send the build-out mask AFTER RepairApplied: the client applies the upgrades against the repaired
            // RV, and the interior/grid modules only resolve once the intact model is the active one.
            // The furniture before the upgrades: the workshop grid lays its floor around the parts that are in.
            NetworkBus.BroadcastToAll(RvOp.InteriorSync, Base.RvInterior.Unlocked, Base.RvInterior.Removed);
            NetworkBus.BroadcastToAll(RvOp.UpgradeSync, Base.RvUpgrades.Mask);
            Base.RvMobile.HostSnapshot();   // after the upgrades: the client attaches the model the Engine upgrade set up
            Base.RvBattery.HostSnapshot();
            HostResyncErrandItem();
            Core.LogDebug("[Net] host snapshot sent: Stage=" + Stage + " Discount=" + DiscountTotal
                + " Repaired=" + RepairStateStore.GetRepaired() + " Upgrades=" + Base.RvUpgrades.Mask);
        }

        /// <summary>
        /// Re-insert the active errand item (Ming's crate / Marco's package) when a client requests a snapshot - i.e.
        /// just joined. A storage slot set BEFORE a client became an observer may not reach it, so the item placed at
        /// the host's load is invisible to a late joiner. Clearing + re-inserting now (with the client observing) sends
        /// a live slot update that replicates. Host-only; the drop is still the same one, so no duplicate persists.
        /// </summary>
        private static void HostResyncErrandItem()
        {
            try
            {
                if (Stage == MingErrand && _crateDrop != null && _cratePlaced)
                {
                    _crateDrop.Storage.RemoveAllOfDefinition(CrateId);
                    PlaceItem(_crateDrop, CrateId);
                    Core.LogDebug("[Net] host re-synced Ming's crate to a joining client.");
                }
                if (_pickupActive && !_hasPackage && _drop != null && _pkgPlaced)
                {
                    _drop.Storage.RemoveAllOfDefinition(PackageId);
                    PlaceItem(_drop, PackageId);
                    Core.LogDebug("[Net] host re-synced Marco's package to a joining client.");
                }
            }
            catch (Exception e) { Core.Log.Warning("[Net] errand item re-sync failed: " + e.Message); }
        }

        /// <summary>Client: adopt the host's authoritative stage + discount and re-render the journal/price.</summary>
        private static void ApplyStageSync(int stage, int discount)
        {
            if (NetworkBus.IsServer) return;   // host is the source of truth; never echo back onto itself
            RepairStateStore.SetStage(stage);
            RepairStateStore.SetDiscountTotal(discount);
            if (stage >= Done) { RepairQuest.CompleteIfActive(); }   // closing beat reached host-side
            else if (Active && stage >= Started) EnsureQuest();      // make sure the client has the journal
            SyncEntry();
            RefreshRepairChoice();
            Core.LogDebug("[Net] client applied StageSync: Stage=" + stage + " Discount=" + discount);
        }

        // --- transient pickup/drop state (host -> clients) -------------------------------------------------
        // The errand pickups use non-persisted transient state (_pickupActive/_hasPackage + which dead drop).
        // The host owns it and replicates it as a single compact message so a client's SyncEntry renders the exact
        // same objective text + dead-drop marker. Dead-drop items themselves already replicate (StorageEntity),
        // so the client just needs to know WHICH drop is the current target (index into DeadDropManager.All).

        /// <summary>Host: broadcast the current transient pickup state + active dead-drop index to all clients.</summary>
        private static void HostBroadcastTransient()
        {
            int flags = (_pickupActive ? 1 : 0) | (_hasPackage ? 2 : 0);
            // Sync the active dead drop by WORLD POSITION (rounded), NOT an index into DeadDropManager.All - that
            // collection is not identically ordered on host and client, so an index pointed the client's marker at
            // the wrong drop (or nowhere). Position is order-independent; the client matches the nearest drop to it.
            Vector3 dp = Vector3.zero;
            if (_pickupActive) dp = _drop != null ? _drop.Position : _dropPoint;
            else if (Stage == MingErrand) dp = _crateDrop != null ? _crateDrop.Position : _cratePoint;
            NetworkBus.BroadcastToAll(RvOp.TransientSync, flags, Mathf.RoundToInt(dp.x), Mathf.RoundToInt(dp.z));
        }

        /// <summary>Client: adopt the host's transient pickup state + resolve the active dead drop, then re-render.</summary>
        private static void ApplyTransient(int flags, int x, int z)
        {
            if (NetworkBus.IsServer) return;
            _pickupActive = (flags & 1) != 0;
            _hasPackage = (flags & 2) != 0;
            var drop = ResolveDropByPos(x, z);
            Vector3 pos = drop != null ? drop.Position : (x == 0 && z == 0 ? Vector3.zero : new Vector3(x, 0f, z));
            if (_pickupActive)
            {
                _drop = drop; _pkgPlaced = drop != null; _dropPoint = pos;
            }
            else
            {
                _crateDrop = drop; _cratePlaced = drop != null; _cratePoint = pos;
            }
            SyncEntry();
            Core.LogDebug("[Net] client applied TransientSync: pickup=" + _pickupActive + " hasPkg=" + _hasPackage
                + " dropPos=(" + x + "," + z + ") resolved=" + (drop != null));
        }

        // Resolve the synced dead drop by WORLD POSITION (rounded x/z), not by index: DeadDropManager.All is NOT
        // guaranteed identically ordered on host and client, so an index could resolve to the wrong drop or none
        // (the client's marker pointed into nothing). Matching the nearest drop to the broadcast position is
        // order-independent - drops sit metres apart, so the ~1m rounding error is unambiguous. Returns null if
        // nothing is within tolerance (e.g. the drops are not registered on the client yet; the host re-broadcasts).
        private static S1API.DeadDrops.DeadDropInstance ResolveDropByPos(int x, int z)
        {
            try
            {
                if (x == 0 && z == 0) return null;
                var all = S1API.DeadDrops.DeadDropManager.All;
                if (all == null) return null;
                S1API.DeadDrops.DeadDropInstance best = null;
                float bestSq = float.MaxValue;
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    float dx = all[i].Position.x - x, dz = all[i].Position.z - z;
                    float sq = dx * dx + dz * dz;
                    if (sq < bestSq) { bestSq = sq; best = all[i]; }
                }
                return bestSq <= 9f ? best : null;   // accept only a real match (within ~3 m)
            }
            catch (Exception e) { Core.Swallowed("Questline.Net.ResolveDropByPos", e); return null; }
        }

        /// <summary>
        /// Host-only (or offline): the closing "check on the RV" beat - advance Paid -> Done + complete the journal.
        /// Called by the host's own proximity loop AND by a client's CheckedRv intent, so either player finishing it
        /// completes the quest for everyone (Stage = Done auto-broadcasts StageSync, which completes it on clients).
        /// </summary>
        /// <summary>Host: a client reported it picked up the current errand item - advance the matching step for all.
        /// Re-validated against the host's own Stage so a stale/duplicate intent is a harmless no-op.</summary>
        private static void HostErrandItemPicked()
        {
            if (Stage == MingErrand) { Stage = MingCrate; SyncEntry(); }                 // Ming's crate collected
            else if (_pickupActive && !_hasPackage) { _hasPackage = true; SyncEntry(); } // Marco's package collected
        }

        private static void HostCheckedRv()
        {
            if (Stage != Paid) return;
            // With "repair takes a day" the stage flips to Paid the moment Marco takes the job, while the RV is
            // still a wreck. Standing near it then would close the quest with "There she is. Standing again."
            // in front of the wreck - so require the RV to actually be back.
            if (!RepairStateStore.GetRepaired() || Base.RvRepairJob.JobRunning) return;
            Stage = Done;
            RepairQuest.CompleteIfActive();
            // Marco stays at his shop, so a speech bubble here would never be seen next to the RV - he texts instead.
            Base.RvRepairJob.TextFromMarco(RVRepairVanPreferences.UpgradesEnabled
                ? L10n.T("Hope she runs fine. If you want more out of her, come see me - I do build-outs too.")
                : L10n.T("Hope she runs fine, buddy."));
            Core.Log.Msg("[Questline] quest complete (RV checked).");
        }

        /// <summary>Client: the host repaired the shared RV - swap the visual + mirror the repaired flag.</summary>
        private static void ApplyRepair()
        {
            if (NetworkBus.IsServer) return;   // the host already did the real repair
            RVManager.MarkRepairedByHost();    // first: the visual swap builds the wheel, which asks whether the RV is wrecked
            RVManager.RepairVisualOnly();
            RepairStateStore.SetRepaired(true);
            RepairQuest.CompleteIfActive();
            Core.LogDebug("[Net] client applied RepairApplied (visual swap).");
        }

        /// <summary>
        /// Host-authoritative repair (used by BOTH quest modes): repair the RV (state + visual + persistence) and
        /// broadcast RepairApplied to all clients. In Questline mode it also advances to Paid (which auto-broadcasts
        /// StageSync); in Simple mode it just completes.
        /// <paramref name="withCinematic"/> = the host player is the one acting (play the local fade); false when
        /// it's a client's PayRepair intent (commit immediately, no black on the host's screen).
        /// <paramref name="clientPaid"/> &lt; 0 = the host player pays from its own cash; otherwise a client already
        /// paid that much, which must match the current price. Returns false when nothing was done.
        /// </summary>
        internal static bool HostPayRepair(bool withCinematic, int clientPaid = -1)
        {
            try
            {
                if (!RVManager.IsDestroyed()) { Core.LogDebug("[Net] host PayRepair ignored - RV not destroyed."); return false; }
                int price = RVRepairVanPreferences.QuestlineEnabled ? CurrentPrice() : RVRepairVanPreferences.RepairPrice;
                if (clientPaid < 0)
                {
                    if (S1API.Money.Money.GetCashBalance() < price)
                    {
                        Core.Log.Msg("[Net] host PayRepair rejected - host short of " + MoneyManager.FormatAmount(price) + ".");
                        return false;
                    }
                    // Charge synchronously, before the cinematic, so an interrupted fade cannot bill twice.
                    S1API.Money.Money.ChangeCashBalance(-price, true, true);
                }
                else if (clientPaid != price)
                {
                    Core.Log.Warning("[Net] host PayRepair refused - client paid " + MoneyManager.FormatAmount(clientPaid)
                        + ", price is " + MoneyManager.FormatAmount(price) + ".");
                    return false;
                }
                int paid = price;

                // "Repair takes a day": Marco takes the job now and hands the RV back 24 in-game hours later.
                // The money is already gone (charged above), which is the point - he has been paid for the work.
                // Begin() returns false if the game clock is unreadable; then fall through to repairing on the spot
                // rather than leaving the player out of pocket with nothing happening.
                if (RVRepairVanPreferences.RepairTakesADay && Base.RvRepairJob.Begin())
                {
                    if (RVRepairVanPreferences.QuestlineEnabled) { Stage = Paid; SyncEntry(); }
                    WorldSay(_marcoT, L10n.T("Alright, I'll send the boys over to your RV. Give me a day - I'll text ya when she's done."));
                    Core.Log.Msg("[Net] host booked Marco's repair job for " + MoneyManager.FormatAmount(paid) + ".");
                    return true;
                }

                Action commit = () =>
                {
                    if (RVManager.Repair())   // field write (persists + correct for joiners) + visual + FX
                    {
                        RepairStateStore.SetRepaired(true);
                        NetworkBus.BroadcastToAll(RvOp.RepairApplied);
                        if (RVRepairVanPreferences.QuestlineEnabled) { Stage = Paid; SyncEntry(); }   // setter broadcasts StageSync
                        else RepairQuest.CompleteIfActive();
                        Core.Log.Msg("[Net] host repaired the RV for " + MoneyManager.FormatAmount(paid) + " (broadcast to clients).");
                    }
                };

                if (withCinematic)
                    RVRepairVan.Effects.RepairCinematic.Play(
                        () => commit(),
                        () => WorldSay(_marcoT, L10n.T("There she is, good as new. Try not to blow her up again, buddy.")),
                        () => GruntNpc(FindNpc(MarcoId)));
                else
                    commit();   // a client acted; do it now and let our broadcast swap their RV during their cinematic
                return true;
            }
            catch (Exception e) { Core.Log.Error("[Net] host repair failed: " + e); return false; }
        }
    }
}
