using System;
using System.Collections;
using DooDesch.Localization;
using Il2CppScheduleOne.Money;
using RVRepairVan.Base;
using RVRepairVan.Config;
using RVRepairVan.Garage;
using RVRepairVan.Net;
using RVRepairVan.Persistence;
using S1API.Quests;
using S1API.Quests.Constants;

namespace RVRepairVan.Quests
{
    /// <summary>
    /// Drives "Home on Wheels". Everything in it is derived once a second from values that are saved and replicated
    /// (the repair, the upgrade mask, the stage), so a save from before this quest existed gets it on load with its
    /// purchases ticked, and host and clients show the same steps. The crew and the loading dock stay plain offers.
    /// </summary>
    internal static class BuildOutQuest
    {
        internal static readonly string Title = L10n.T("Home on Wheels");

        internal const int StepAsk = 0, StepEngine = 1, StepDrive = 2, StepFloor = 3, StepCount = 4;

        /// <summary>
        /// One tick. <paramref name="storyOver"/>: the repair story is not running (finished, or never played).
        /// <paramref name="asked"/>: the player has asked Marco about the build-outs.
        /// </summary>
        internal static void Tick(bool storyOver, bool asked)
        {
            Quest quest = QuestManager.GetQuestByName(Title);
            bool engine = RvUpgrades.Owned(RvUpgrade.Engine);
            bool floor = RvUpgrades.Owned(RvUpgrade.WorkshopFloor);

            if (engine && floor)
            {
                if (quest != null) Finish();
                return;
            }

            bool wanted = RVRepairVanPreferences.Enabled && RVRepairVanPreferences.QuestlineEnabled
                && RVRepairVanPreferences.UpgradesEnabled && RepairStateStore.GetRepaired() && storyOver;
            if (!wanted)
            {
                if (quest != null)
                {
                    Core.Log.Msg("[Quest] '" + Title + "' cancelled - repaired=" + RepairStateStore.GetRepaired()
                        + " storyOver=" + storyOver + " upgrades=" + RVRepairVanPreferences.UpgradesEnabled + ".");
                    QuestSteps.Forget(Title);
                    quest.Cancel();
                }
                return;
            }

            if (quest == null)
            {
                QuestSteps.Forget(Title);
                QuestManager.CreateQuest<BuildOutRVQuest>();
                Core.Log.Msg("[Quest] '" + Title + "' started (engine=" + engine + " floor=" + floor + " asked=" + asked + ").");
                return;   // its steps exist a frame or two later; the next tick sets them
            }

            Sync(asked || RvUpgrades.Mask != 0, engine, floor);
        }

        private static void Sync(bool asked, bool engine, bool floor)
        {
            if (!QuestSteps.TryOpen(Title, StepCount, out QuestSteps s)) return;
            bool onLot = engine && !floor && RvGarage.InGarage();
            Vector3 marco = Questline.MarcoPosition();

            s.Set(StepAsk, asked ? QuestState.Completed : QuestState.Active, null, Questline.MarcoId, marco);

            s.Set(StepEngine,
                engine ? QuestState.Completed : asked ? QuestState.Active : QuestState.Inactive,
                L10n.T("Have Marco put the engine in ({0})", MoneyManager.FormatAmount(RvUpgrades.Price(RvUpgrade.Engine))),
                Questline.MarcoId, marco);

            s.Set(StepDrive,
                !engine ? QuestState.Inactive : floor || onLot ? QuestState.Completed : QuestState.Active,
                null, null, RvGarage.LotPoint(marco));

            s.Set(StepFloor,
                floor ? QuestState.Completed : onLot ? QuestState.Active : QuestState.Inactive,
                L10n.T("Have Marco put in the workshop floor there ({0})", MoneyManager.FormatAmount(RvUpgrades.Price(RvUpgrade.WorkshopFloor))),
                Questline.MarcoId, marco);
        }

        private static void Finish()
        {
            QuestSteps.CompleteAll(Title);
            // The bodyshop's own effect has just played, so Marco names what else he sells by text.
            if (NetworkBus.Online && !NetworkBus.IsServer) return;
            RvRepairJob.TextFromMarco(L10n.T("Floor's in. You can set up shop in there now. Crew room and a loading dock I can do too, just ask."));
        }

        /// <summary>
        /// Simple mode has no quests, but a save can come from Questline mode. Started from the scene wiring there,
        /// after the save and its quests have loaded.
        /// </summary>
        internal static IEnumerator CancelLeftoverCoroutine()
        {
            for (int i = 0; i < 15 && !RepairSave.Loaded; i++) yield return new WaitForSeconds(1f);
            yield return new WaitForSeconds(3f);
            try
            {
                Quest quest = QuestManager.GetQuestByName(Title);
                if (quest == null) yield break;
                Core.Log.Msg("[Quest] '" + Title + "' cancelled - QuestMode is Simple.");
                quest.Cancel();
            }
            catch (Exception e) { Core.Log.Warning("[Quest] cancelling the leftover build-out quest failed: " + e.Message); }
        }
    }
}
