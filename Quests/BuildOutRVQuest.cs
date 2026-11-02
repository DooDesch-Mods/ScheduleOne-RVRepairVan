using System;
using DooDesch.Localization;
using RVRepairVan.Managers;
using S1API.Quests;

namespace RVRepairVan.Quests
{
    /// <summary>
    /// "Home on Wheels": from the repaired RV to the workshop floor. It leads through Marco's engine and his lot, the
    /// two steps nobody can guess. <see cref="BuildOutQuest"/> starts it, ends it and sets its steps.
    /// </summary>
    public class BuildOutRVQuest : Quest
    {
        protected override string Title => BuildOutQuest.Title;

        protected override string Description =>
            L10n.T("Marco can do more to the RV.");

        protected override bool AutoBegin => true;

        protected override Sprite QuestIcon => RepairRVQuest.SharedIcon;

        protected override void OnCreated()
        {
            base.OnCreated();
            try
            {
                Vector3 rv = RVManager.TryGetPosition(out Vector3 p) ? p : Vector3.zero;
                // Same order as the BuildOutQuest.Step constants. Every step needs a valid marker from the start: the
                // game registers a compass element per step and dereferences a missing location every frame.
                AddEntry(L10n.T("Ask Marco about the build-outs")).POIPosition = rv;
                AddEntry(L10n.T("Have Marco put the engine in")).POIPosition = rv;
                AddEntry(L10n.T("Drive the RV onto Marco's lot")).POIPosition = rv;
                AddEntry(L10n.T("Have Marco put in the workshop floor there")).POIPosition = rv;
            }
            catch (Exception e)
            {
                Core.Log.Warning("[Quest] build-out OnCreated failed: " + e.Message);
            }
        }
    }
}
