using System;
using DooDesch.Localization;
using RVRepairVan.Config;
using RVRepairVan.Managers;
using RVRepairVan.Net;
using RVRepairVan.Persistence;
using RVRepairVan.Quests;

namespace RVRepairVan.Base
{
    /// <summary>Upgrades Marco sells once the RV is repaired. Stored as a bitmask, so the order is free.</summary>
    [Flags]
    internal enum RvUpgrade
    {
        None = 0,
        GutInterior = 1,     // retired: 3.x sold the whole interior as one; a save with it migrates to RvPart masks
        WorkshopFloor = 2,   // the build grid covers every floor space the furniture leaves free (bodyshop)
        CrewQuarters = 4,    // employee capacity + idle points + a walkable interior
        LoadingDock = 8,     // a delivery destination at the RV
        Engine = 16,         // the RV can be driven (RvMobile)
    }

    /// <summary>
    /// The RV build-out: four things Marco sells after the repair, each paid for, each applied through the same
    /// short cinematic. Nothing here is free - that is the point of the tier, and it is what separates this from
    /// simply switching the features on.
    ///
    /// The upgrades are runtime effects on scene objects (a grid activated, an array filled, a dock instantiated),
    /// not serialized world state. So the save holds only the bitmask and <see cref="ApplyOwned"/> re-applies
    /// everything on each scene load. Every module's Apply must therefore be idempotent.
    ///
    /// Co-op mirrors the questline: the buyer pays from its own cash, a client then sends
    /// <see cref="RvOp.BuyUpgrade"/>, the host validates prerequisites and the amount, and broadcasts the new mask.
    /// </summary>
    internal static class RvUpgrades
    {
        // Applied in dependency order - the crew needs the grid, which needs the room.
        private static readonly RvUpgrade[] Order =
        {
            RvUpgrade.WorkshopFloor, RvUpgrade.CrewQuarters, RvUpgrade.LoadingDock, RvUpgrade.Engine,
        };

        /// <summary>The full owned mask. Setting it on the host replicates, exactly like Questline.Stage.</summary>
        internal static int Mask
        {
            get => RepairStateStore.GetUpgrades();
            private set
            {
                RepairStateStore.SetUpgrades(value);
                if (NetworkBus.Online && NetworkBus.IsServer) NetworkBus.BroadcastToAll(RvOp.UpgradeSync, value);
            }
        }

        /// <summary>Every bit we actually support - used to sanitise anything that arrives over the wire.</summary>
        private const int AllBits = (int)(RvUpgrade.WorkshopFloor | RvUpgrade.CrewQuarters | RvUpgrade.LoadingDock | RvUpgrade.Engine);

        /// <summary>
        /// Exactly one known upgrade? A purchase arrives as an int over the network, and anything else - a
        /// composite like 15, a negative, an unknown bit - has no price and no prerequisite, so it would grant
        /// itself for nothing. Everything that can be bought goes through this gate first.
        /// </summary>
        internal static bool IsValid(RvUpgrade up) =>
            up == RvUpgrade.WorkshopFloor || up == RvUpgrade.CrewQuarters || up == RvUpgrade.LoadingDock || up == RvUpgrade.Engine;

        internal static bool Owned(RvUpgrade up) => IsValid(up) && (Mask & (int)up) != 0;

        internal static int Price(RvUpgrade up)
        {
            switch (up)
            {
                case RvUpgrade.WorkshopFloor: return RVRepairVanPreferences.PriceWorkshopFloor;
                case RvUpgrade.CrewQuarters: return RVRepairVanPreferences.PriceCrewQuarters;
                case RvUpgrade.LoadingDock: return RVRepairVanPreferences.PriceLoadingDock;
                case RvUpgrade.Engine: return RVRepairVanPreferences.PriceEngine;
                default: return 0;
            }
        }

        /// <summary>What has to be owned first. Only the crew builds on another upgrade: it works at the stations.</summary>
        private static RvUpgrade Prerequisite(RvUpgrade up)
        {
            switch (up)
            {
                case RvUpgrade.CrewQuarters: return RvUpgrade.WorkshopFloor;
                default: return RvUpgrade.None;
            }
        }

        /// <summary>The Marco choice text, with the live price - refreshed like the repair choice.</summary>
        internal static string ChoiceText(RvUpgrade up)
        {
            string money = MoneyManager.FormatAmount(Price(up));
            switch (up)
            {
                case RvUpgrade.WorkshopFloor: return L10n.T("Put in a work floor ({0})", money);
                case RvUpgrade.CrewQuarters: return L10n.T("Make room for a crew ({0})", money);
                case RvUpgrade.LoadingDock: return L10n.T("Build a loading dock ({0})", money);
                case RvUpgrade.Engine: return L10n.T("Get the engine running ({0})", money);
                default: return "";
            }
        }

        private static string DoneLine(RvUpgrade up)
        {
            switch (up)
            {
                case RvUpgrade.WorkshopFloor:
                    return L10n.T("Floor's in. You can set up shop in there now.");
                case RvUpgrade.CrewQuarters:
                    return L10n.T("Room for {0} now. Hire 'em through the Fixer, and put a locker in there so they get paid. Yeah, science.", RVRepairVanPreferences.CrewSize);
                case RvUpgrade.LoadingDock:
                    return L10n.T("Dock's done. Deliveries can come straight to the RV now.");
                case RvUpgrade.Engine:
                    return L10n.T("She runs. Just don't leave the keys in.");
                default: return "";
            }
        }

        /// <summary>Should Marco offer this upgrade right now? Drives the dialogue choice's visibility check.</summary>
        internal static bool Available(RvUpgrade up)
        {
            if (!IsValid(up)) return false;
            if (!RVRepairVanPreferences.Enabled || !RVRepairVanPreferences.UpgradesEnabled) return false;
            if (!RepairStateStore.GetRepaired()) return false;   // Marco sells build-outs, not repairs
            if (Owned(up)) return false;
            RvUpgrade need = Prerequisite(up);
            return need == RvUpgrade.None || Owned(need);
        }

        // --- buying ---------------------------------------------------------------------------------------

        /// <summary>
        /// Player picked an upgrade at Marco. On a co-op client this player pays and sends an intent - the host
        /// applies and broadcasts; we still play the cinematic locally so it does not look like nothing happened.
        /// </summary>
        internal static void Buy(RvUpgrade up)
        {
            try
            {
                if (!Available(up)) return;

                if (NetworkBus.Online && !NetworkBus.IsServer)
                {
                    // The host owns the mask, this player owns the money. Play the black locally with no effect of
                    // our own - the host's UpgradeSync applies the actual change while we are looking at the veil.
                    int price = Price(up);
                    int ticket = ClientPayment.Charge(price, "upgrade " + up);
                    if (ticket == 0)
                    {
                        Questline.SayMarco(L10n.T("That's {0}, buddy. Come back when you've got it.", MoneyManager.FormatAmount(price)));
                        return;
                    }
                    NetworkBus.SendToHost(RvOp.BuyUpgrade, (int)up, ticket, price);
                    Effects.RepairCinematic.PlayUpgrade(null, null);
                    return;
                }

                HostBuy(up, withCinematic: true);
            }
            catch (Exception e) { Core.Log.Warning("[Upgrade] buy " + up + " failed: " + e.Message); }
        }

        /// <summary>
        /// Host (or offline): validate, charge once, apply, replicate. Charging happens BEFORE the cinematic for
        /// the same reason the repair does it - an interrupted fade must never be able to bill twice.
        /// <paramref name="clientPaid"/> &lt; 0 = the host player buys with its own cash; otherwise a client already
        /// paid that much, which must match the price. Returns false when nothing was bought.
        /// </summary>
        internal static bool HostBuy(RvUpgrade up, bool withCinematic, int clientPaid = -1)
        {
            try
            {
                if (!Available(up)) { Core.LogDebug("[Upgrade] " + up + " not available - ignored."); return false; }
                int price = Price(up);
                if (clientPaid < 0)
                {
                    if (S1API.Money.Money.GetCashBalance() < price)
                    {
                        Core.Log.Msg("[Upgrade] " + up + " rejected - short of " + MoneyManager.FormatAmount(price) + ".");
                        Questline.SayMarco(L10n.T("That's {0}, buddy. Come back when you've got it.", MoneyManager.FormatAmount(price)));
                        return false;
                    }
                    // Charge and record ownership in the SAME step. Deferring the mask to the cinematic's midpoint
                    // callback meant two purchases arriving before that callback both saw the upgrade as unowned and
                    // both charged, while ScreenTransition rejected the second fade - two charges, one upgrade. The
                    // cinematic is presentation; it must not carry the transaction.
                    S1API.Money.Money.ChangeCashBalance(-price, true, true);
                }
                else if (clientPaid != price)
                {
                    Core.Log.Warning("[Upgrade] " + up + " refused - client paid " + MoneyManager.FormatAmount(clientPaid)
                        + ", price is " + MoneyManager.FormatAmount(price) + ".");
                    return false;
                }
                Mask = Mask | (int)up;   // setter replicates to clients
                Core.Log.Msg("[Upgrade] " + up + " bought for " + MoneyManager.FormatAmount(price) + ".");

                // Apply is a retryable projection of that ownership - if it does not take now, the re-apply pass
                // after the next load runs it again.
                if (withCinematic)
                    Effects.RepairCinematic.PlayUpgrade(() => Apply(up), () => Questline.SayMarco(DoneLine(up)));
                else
                    Apply(up);   // a client acted - their own cinematic is already running
                return true;
            }
            catch (Exception e) { Core.Log.Warning("[Upgrade] host buy " + up + " failed: " + e.Message); return false; }
        }

        /// <summary>
        /// Host: an upgrade bought at the bodyshop. The bodyshop has taken the money and plays its own workshop
        /// effect, so this only checks, records, replicates and applies. Returns null when done, else the reason.
        /// </summary>
        internal static string HostGrant(RvUpgrade up)
        {
            if (!IsValid(up)) return "Not available.";
            if (!RVRepairVanPreferences.Enabled || !RVRepairVanPreferences.UpgradesEnabled) { Core.Log.Warning("[Upgrade] " + up + " refused - upgrades are switched off."); return "Not available."; }
            if (!RepairStateStore.GetRepaired()) { Core.Log.Warning("[Upgrade] " + up + " refused - the RV is not repaired."); return "Repair the RV first."; }
            if (Owned(up)) return "Already installed.";
            RvUpgrade need = Prerequisite(up);
            if (need != RvUpgrade.None && !Owned(need)) { Core.Log.Warning("[Upgrade] " + up + " refused - needs " + need + "."); return "Not available."; }
            Mask = Mask | (int)up;   // setter replicates to clients
            Core.Log.Msg("[Upgrade] " + up + " fitted at the bodyshop.");
            Apply(up);
            return null;
        }

        /// <summary>Client: adopt the host's mask and apply whatever is newly owned.</summary>
        internal static void ApplySync(int mask)
        {
            if (NetworkBus.IsServer) return;
            RepairStateStore.SetUpgrades(mask & AllBits);   // never adopt bits we do not know
            ApplyOwned();
            Core.LogDebug("[Upgrade] client applied UpgradeSync mask=" + mask);
        }

        // --- applying -------------------------------------------------------------------------------------

        /// <summary>
        /// Re-apply every owned upgrade, in dependency order. Called after a load and after a sync; each module's
        /// Apply is idempotent, so running this repeatedly is harmless.
        /// </summary>
        internal static void ApplyOwned()
        {
            RvLiner.Ensure();   // not an upgrade: the stock model has holes whether or not anything was bought
            RvSavePoint.Ensure();
            RvMobile.EnsureWheel();
            if (RepairStateStore.GetRepaired()) RvInterior.Sync();   // the furniture before the grid that lays around it
            RvPaint.Show();
            if (!RVRepairVanPreferences.UpgradesEnabled) return;
            foreach (RvUpgrade up in Order)
                if (Owned(up)) Apply(up);
            RvEasterEggs.Ensure();   // after the furniture, so the cook gear only shows where nothing stands
        }

        private static void Apply(RvUpgrade up)
        {
            try
            {
                switch (up)
                {
                    case RvUpgrade.WorkshopFloor: RvBuildGrid.Apply(); break;
                    case RvUpgrade.CrewQuarters: RvCrew.Apply(); break;
                    case RvUpgrade.LoadingDock: RvDeliveryDock.Apply(); break;
                    case RvUpgrade.Engine: RvMobile.Apply(); break;
                }
            }
            catch (Exception e) { Core.Log.Warning("[Upgrade] apply " + up + " failed: " + e.Message); }
        }

        /// <summary>Per-scene reset of the modules' cached transforms. The mask itself lives in the save.</summary>
        internal static void Reset()
        {
            RvInterior.Reset();
            RvBuildGrid.Reset();
            RvCrew.Reset();
            RvDeliveryDock.Reset();
            RvMobile.Reset();
            RvEasterEggs.Reset();
            RvStripes.Reset();
        }

#if DEBUG
        /// <summary>Debug: grant or revoke upgrades directly (rvupg). Host-side; the setter replicates.</summary>
        internal static void DebugSetMask(int mask)
        {
            Mask = mask;
            RvInterior.Reset(); RvBuildGrid.Reset(); RvCrew.Reset(); RvDeliveryDock.Reset();
            ApplyOwned();
            Core.Log.Msg("[Debug] rvupg -> mask=" + mask + " (" + (RvUpgrade)mask + ")");
        }

        internal static void DebugDump()
        {
            Core.Log.Msg("[Debug] upgrades mask=" + Mask + " enabled=" + RVRepairVanPreferences.UpgradesEnabled
                + " repaired=" + RepairStateStore.GetRepaired());
            foreach (RvUpgrade up in Order)
                Core.Log.Msg("[Debug]   " + up + " owned=" + Owned(up) + " available=" + Available(up)
                    + " price=" + Price(up));
        }
#endif
    }
}
