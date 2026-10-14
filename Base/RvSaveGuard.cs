using System;
using HarmonyLib;
using Il2CppScheduleOne.Property;
using RVRepairVan.Persistence;

namespace RVRepairVan.Base
{
    /// <summary>
    /// Keeps the RV's save file alive once it has been repaired. Without this, none of the build-out survives a
    /// reload - and neither would a hired employee.
    ///
    /// The chain: <c>RV.ShouldSave()</c> returns false while <c>IsDestroyed</c>.
    /// <c>PropertyManager.WriteData</c> then leaves "RV" out of the approved-file list, and
    /// <c>ISaveable.CompleteSave</c> deletes every file under <c>Properties/</c> that is not on it - taking
    /// <c>Properties/RV.json</c> with it. That single file is where a property's employees AND every buildable
    /// placed on its grids are stored (<c>Property.GetSaveString</c>), so losing it wipes the whole base.
    ///
    /// This is also why the competing mod needs its own employee JSON: it never fixes the deletion, so it has to
    /// re-create the employees from a private file on every load.
    ///
    /// The flag can come back at runtime even after a repair - <c>Quest_WelcomeToHylandPoint.SetRVDestroyed()</c>
    /// is wired to a UnityEvent that fires on quest-state restore - so the guard keys off our own persisted
    /// "repaired" flag rather than the live <c>IsDestroyed</c> value.
    /// </summary>
    [HarmonyPatch(typeof(RV), nameof(RV.ShouldSave))]
    internal static class Rv_ShouldSave_Patch
    {
        private static void Postfix(ref bool __result)
        {
            try
            {
                if (__result) return;
                if (RepairStateStore.GetRepaired()) __result = true;
            }
            catch (Exception e) { Core.Swallowed("RvSaveGuard", e); }
        }
    }
}
