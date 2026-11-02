using HarmonyLib;
using Il2CppScheduleOne.NPCs.CharacterClasses;
using RVRepairVan.Quests;

namespace RVRepairVan.Patches
{
    /// <summary>The game calls SetGreeted through a listener on Marco's conversation start, only while his first-contact
    /// greeting is pending, so this is the one place that tells the greeting apart from any other talk.</summary>
    [HarmonyPatch(typeof(Marco), nameof(Marco.SetGreeted))]
    internal static class MarcoGreetingPatch
    {
        private static void Postfix() => Questline.OnMarcoGreeted();
    }
}
