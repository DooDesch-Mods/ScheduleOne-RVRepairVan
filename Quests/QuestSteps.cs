using System;
using System.Collections.Generic;
using S1API.Quests;
using S1API.Quests.Constants;
using VanillaEntryState = Il2CppScheduleOne.Quests.EQuestState;
using VanillaQuest = Il2CppScheduleOne.Quests.Quest;

namespace RVRepairVan.Quests
{
    /// <summary>
    /// Writes the journal steps of a quest whose progress lives elsewhere (the stage and the upgrade mask). Host and
    /// clients derive the same states from the replicated values, so a step is set without a network message. The
    /// journal hides Inactive steps and the HUD lists only Active ones; the game hides the marker of any step that is
    /// not Active, so only Active steps get their marker pointed.
    /// </summary>
    internal readonly struct QuestSteps
    {
        // Where each step's marker points right now, so a repeated sync does not rebind it.
        private static readonly Dictionary<string, string> _markers = new Dictionary<string, string>();

        private readonly Quest _quest;
        private readonly VanillaQuest _vanilla;
        private readonly string _title;

        private QuestSteps(Quest quest, VanillaQuest vanilla, string title)
        {
            _quest = quest;
            _vanilla = vanilla;
            _title = title;
        }

        /// <summary>Forget every marker: a new scene builds new step objects.</summary>
        internal static void ResetScene() => _markers.Clear();

        /// <summary>Forget one quest's markers: it is about to be created again.</summary>
        internal static void Forget(string questTitle)
        {
            var keys = new List<string>();
            foreach (string key in _markers.Keys)
                if (key.StartsWith(questTitle + "|", StringComparison.Ordinal)) keys.Add(key);
            foreach (string key in keys) _markers.Remove(key);
        }

        /// <summary>False while the quest is not in the journal or its steps are not built yet (OnCreated runs a
        /// frame or two after CreateQuest).</summary>
        internal static bool TryOpen(string questTitle, int stepCount, out QuestSteps steps)
        {
            steps = default;
            Quest quest = QuestManager.GetQuestByName(questTitle);
            if (quest?.QuestEntries == null) return false;
            if (quest.QuestEntries.Count < stepCount)
            {
                Core.LogDebug("[Quest] '" + questTitle + "' has " + quest.QuestEntries.Count + " of " + stepCount + " steps yet.");
                return false;
            }
            VanillaQuest vanilla = VanillaQuest.GetQuest(questTitle);
            if (vanilla == null || vanilla.Entries == null || vanilla.Entries.Count < stepCount)
            {
                Core.Log.Warning("[Quest] '" + questTitle + "' has no matching game quest with " + stepCount + " steps.");
                return false;
            }
            steps = new QuestSteps(quest, vanilla, questTitle);
            return true;
        }

        internal QuestState State(int index) => _quest.QuestEntries[index].State;

        /// <summary>
        /// Set one step. <paramref name="title"/> null keeps the title. The marker follows the NPC when
        /// <paramref name="npcId"/> resolves, and otherwise stands at <paramref name="point"/>.
        /// </summary>
        internal void Set(int index, QuestState want, string title, string npcId, Vector3 point)
        {
            QuestEntry entry = _quest.QuestEntries[index];
            if (title != null && entry.Title != title) entry.Title = title;
            if (entry.State != want)
            {
                Core.Log.Msg("[Quest] '" + _title + "' step " + index + " '" + entry.Title + "': " + entry.State + " -> " + want + ".");
                _vanilla.Entries[index].SetState((VanillaEntryState)(int)want, false);
            }
            if (want == QuestState.Active) PointMarker(entry, index, npcId, point);
        }

        private void PointMarker(QuestEntry entry, int index, string npcId, Vector3 point)
        {
            string key = _title + "|" + index;
            if (npcId != null)
            {
                string target = "npc:" + npcId;
                if (_markers.TryGetValue(key, out string bound) && bound == target) return;
                S1API.Entities.NPC npc = S1API.Entities.NPC.Get(npcId);
                if (npc != null && entry.SetPOIToNPC(npc))
                {
                    _markers[key] = target;
                    Core.LogDebug("[Quest] '" + _title + "' step " + index + " marker follows '" + npcId + "'.");
                    return;
                }
                // Not recorded, so the next sync tries the NPC again.
                Core.LogDebug("[Quest] '" + _title + "' step " + index + " marker at " + point + " until '" + npcId + "' resolves.");
            }
            if (point == Vector3.zero)
            {
                Core.LogDebug("[Quest] '" + _title + "' step " + index + " has no marker position yet.");
                return;
            }
            string spot = "pos:" + Mathf.RoundToInt(point.x) + "," + Mathf.RoundToInt(point.z);
            if (_markers.TryGetValue(key, out string stands) && stands == spot) return;
            entry.POIPosition = point;
            if (npcId == null) _markers[key] = spot;
        }

        /// <summary>
        /// Complete every step that is not complete yet. The quest ends with the last one, its journal and HUD entries
        /// go with it, so the skipped optional steps never show as done.
        /// </summary>
        internal static void CompleteAll(string questTitle)
        {
            Quest quest = QuestManager.GetQuestByName(questTitle);
            if (quest == null)
            {
                Core.LogDebug("[Quest] '" + questTitle + "' not in the journal - nothing to complete.");
                return;
            }
            // The steps are added in OnCreated, a frame or two after CreateQuest. Completing in that window completes
            // nothing and leaves the quest open, so say so instead of reporting success.
            if (quest.QuestEntries == null || quest.QuestEntries.Count == 0)
            {
                Core.Log.Warning("[Quest] '" + questTitle + "' has no steps yet - left open.");
                return;
            }
            int completed = 0;
            foreach (QuestEntry entry in quest.QuestEntries)
            {
                if (entry.State == QuestState.Completed) continue;
                try { entry.Complete(); completed++; }
                catch (Exception e) { Core.Swallowed("QuestSteps.CompleteAll", e); /* keep completing the rest */ }
            }
            Core.Log.Msg("[Quest] '" + questTitle + "' completed (" + completed + " step(s) closed).");
        }

#if DEBUG
        /// <summary>Debug (rvquest): every step of a quest as the game holds it.</summary>
        internal static void Dump(string questTitle)
        {
            Quest quest = QuestManager.GetQuestByName(questTitle);
            if (quest == null) { Core.Log.Msg("[rvquest] '" + questTitle + "': not in the journal."); return; }
            int count = quest.QuestEntries != null ? quest.QuestEntries.Count : 0;
            Core.Log.Msg("[rvquest] '" + questTitle + "': " + count + " step(s).");
            for (int i = 0; i < count; i++)
            {
                QuestEntry e = quest.QuestEntries[i];
                Core.Log.Msg("[rvquest]   [" + i + "] " + e.State + " '" + e.Title + "'.");
            }
        }
#endif
    }
}
