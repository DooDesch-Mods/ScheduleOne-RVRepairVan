#if SNITCH
using System;
using Snitch.Api;
using RVRepairVan.Persistence;    // RepairStateStore
using RVRepairVan.Patches;        // DebugConsolePatch (rv* command helpers - DEBUG-only, defined together with SNITCH)
using RVRepairVan.Quests;         // Questline.DebugResetErrandDrop

namespace RVRepairVan.Profiling
{
    /// <summary>
    /// DEBUG-only Snitch instrumentation for RVRepairVan. This is an event/coroutine-driven quest mod with no
    /// per-frame cost, so the profiler value is the quest-stage timeline (a gauge + a stage-name/drops readout)
    /// plus a few one-click test actions that mirror the rv* dev console commands. The console commands stay the
    /// canonical MCP/automation interface (DebugConsolePatch); this panel just surfaces the most useful ones in the
    /// in-game Snitch overlay + web dashboard. No-op when the Snitch host is absent. Compiled only when SNITCH is
    /// defined (Debug + EnableSnitch); excluded from Release. See Workspace/build/Snitch.props.
    /// </summary>
    internal static class SnitchProbe
    {
        // Mirrors the Questline stage constants (None..Done). Used only for the panel readout text.
        private static string StageName(int s)
        {
            switch (s)
            {
                case 0:  return "None";
                case 1:  return "Started";
                case 2:  return "AskedDonna";
                case 3:  return "MingErrand";
                case 4:  return "MingCrate";
                case 5:  return "Referred";
                case 6:  return "MarcoMet";
                case 7:  return "ReadyToPay";
                case 8:  return "Trusted";
                case 9:  return "Paid";
                case 10: return "Done";
                default: return "?";
            }
        }

        // Count dead drops that currently hold one of our quest items (Ming's crate / Marco's package). Mirrors the
        // diagnostic rvdrops/rvclear scan over DeadDropManager.All; cheap, polled by the host at a few Hz.
        private static int CountQuestItemDrops()
        {
            try
            {
                var all = S1API.DeadDrops.DeadDropManager.All;
                if (all == null) return 0;
                int n = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    var d = all[i];
                    if (d != null && !d.IsEmpty) n++;
                }
                return n;
            }
            catch (Exception e) { Core.Swallowed("Probe.CountQuestItemDrops", e); return 0; }
        }

        public static void Register()
        {
            Panel p = Profiler.RegisterPanel("RVRepairVan", "RV Repair Van");

            // Keep the existing gauge id (RVRepairVan.Stage) so prior config/history still matches.
            p.Counter("Stage", () => RepairStateStore.GetStage(), "stage");

            // Stage name + samples/discount + non-empty dead-drop count (the rvdrops scan), at a glance.
            p.Text(() =>
            {
                int s = RepairStateStore.GetStage();
                return "stage: " + s + " (" + StageName(s) + ")"
                     + "\nsamples: " + RepairStateStore.GetSamples()
                     + "  discount: " + RepairStateStore.GetDiscountTotal()
                     + "\nnon-empty drops: " + CountQuestItemDrops();
            });

            // One-click mirrors of the most useful rv* dev console commands. Actions run on the main thread.
            // rvtest: give one packaged jar OG Kush per quality to the local inventory.
            p.Action("Give Test Items", () => DebugConsolePatch.GiveTestProducts("jar", "ogkush"));
            // rvdrops: dump every dead drop (name/pos/empty) to the log.
            p.Action("Dump Drops", () => DebugConsolePatch.DumpDrops());
            // rvclear: wipe accumulated test crates/packages from all drops + re-reserve a fresh errand drop.
            p.Action("Clear Test Crates", () =>
            {
                DebugConsolePatch.ClearErrandItems();
                Questline.DebugResetErrandDrop();
            });
            // (rvstage <n> and rvgiveclient stay console-only: rvstage takes an arg; rvgiveclient is host-broadcast.)

            p.Log();
        }
    }
}
#endif
