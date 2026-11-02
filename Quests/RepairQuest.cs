using System;
using DooDesch.Localization;
using S1API.Quests;
using S1API.Quests.Constants;

namespace RVRepairVan.Quests
{
    /// <summary>
    /// Drives the "Back on the Road" quest lifecycle. Start is triggered by the explosion-beat
    /// auto-start in Questline.ProximityCoroutine; completion is triggered by reaching the
    /// repaired RV. S1API persists the active quest per save, so we look it up by name rather
    /// than holding a stale reference across reloads. Questline.SyncEntry sets its steps.
    /// </summary>
    internal static class RepairQuest
    {
        // Localized once at first use; every lookup (GetQuestByName) goes through this same field,
        // so create/find/complete stay consistent within a session regardless of language.
        internal static readonly string Title = L10n.T("Back on the Road");

        internal static bool IsActive()
        {
            try
            {
                return QuestManager.GetQuestByName(Title) != null;
            }
            catch (Exception e)
            {
                Core.Swallowed("RepairQuest.IsActive", e);
                return false;
            }
        }

        /// <summary>
        /// True once the quest exists AND its steps are populated. They are added in OnCreated, which S1API
        /// fires on the game's Quest.Start event a frame or two AFTER CreateQuest - so right after StartIfNeeded
        /// the quest exists but has no steps yet, and a sync would no-op.
        /// </summary>
        internal static bool HasEntry()
        {
            try
            {
                Quest quest = QuestManager.GetQuestByName(Title);
                return quest?.QuestEntries != null && quest.QuestEntries.Count > 0;
            }
            catch (Exception e)
            {
                Core.Swallowed("RepairQuest.HasEntry", e);
                return false;
            }
        }

        /// <summary>True while the quest has an entry that is neither completed nor failed.</summary>
        internal static bool HasOpenEntry()
        {
            try
            {
                Quest quest = QuestManager.GetQuestByName(Title);
                if (quest?.QuestEntries == null) return false;
                foreach (QuestEntry entry in quest.QuestEntries)
                    if (entry.State == QuestState.Active || entry.State == QuestState.Inactive) return true;
                return false;
            }
            catch (Exception e)
            {
                Core.Swallowed("RepairQuest.HasOpenEntry", e);
                return false;
            }
        }

        internal static void StartIfNeeded()
        {
            try
            {
                if (IsActive())
                {
                    Core.LogDebug("[Quest] '" + Title + "' already active.");
                    return;
                }

                QuestSteps.Forget(Title);
                QuestManager.CreateQuest<RepairRVQuest>();
                Core.Log.Msg("[Quest] '" + Title + "' started.");
            }
            catch (Exception e)
            {
                Core.Log.Warning("[Quest] start failed: " + e.Message);
            }
        }

        internal static void CompleteIfActive()
        {
            try { QuestSteps.CompleteAll(Title); }
            catch (Exception e) { Core.Log.Warning("[Quest] complete failed: " + e.Message); }
        }
    }
}
