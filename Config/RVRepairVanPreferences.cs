using MelonLoader;
using UnityEngine;

namespace RVRepairVan.Config
{
    /// <summary>
    /// MelonPreferences wrapper. The category identifier is prefixed with the mod name
    /// ("RVRepairVan_...") so it is auto-detected by the "Mod Manager &amp; Phone App"
    /// settings UI (Prowiler).
    /// </summary>
    internal static class RVRepairVanPreferences
    {
        private const string CategoryId = "RVRepairVan_01_Main";

        private static MelonPreferences_Category _category;
        private static MelonPreferences_Entry<bool> _enabled;
        private static MelonPreferences_Entry<int> _repairPrice;
        private static MelonPreferences_Entry<string> _questMode;
        private static MelonPreferences_Entry<int> _basePriceNoReferral;
        private static MelonPreferences_Entry<int> _basePriceWithReferral;
        private static MelonPreferences_Entry<int> _minSampleDiscount;
        private static MelonPreferences_Entry<int> _maxSampleDiscount;
        private static MelonPreferences_Entry<bool> _upgradesEnabled;
        private static MelonPreferences_Entry<int> _priceGutInterior;
        private static MelonPreferences_Entry<int> _priceWorkshopFloor;
        private static MelonPreferences_Entry<int> _priceCrewQuarters;
        private static MelonPreferences_Entry<int> _priceLoadingDock;
        private static MelonPreferences_Entry<int> _priceEngine;
        private static MelonPreferences_Entry<int> _crewSize;
        private static MelonPreferences_Entry<int> _buildGridExtraTiles;
        private static MelonPreferences_Entry<bool> _instantRepair;
        private static MelonPreferences_Entry<bool> _repairTakesADay;
        private static MelonPreferences_Entry<bool> _batteryBreakdowns;
#if DEBUG
        private static MelonPreferences_Entry<bool> _destroyRvDebug;
        private static MelonPreferences_Entry<bool> _addCashDebug;
        private static MelonPreferences_Entry<bool> _dumpStateDebug;
        private static MelonPreferences_Entry<bool> _testCinematicDebug;
        private static MelonPreferences_Entry<bool> _netPingDebug;
#endif

        internal static void Initialize()
        {
            if (_category != null)
            {
                return;
            }

            _category = MelonPreferences.CreateCategory(CategoryId, "RV Repair Van");
            _enabled = CreateEntry("Enabled", true, "Enabled", "Enable the RV repair feature.");
            _repairPrice = CreateEntry("RepairPrice", 1500, "Repair Price", "Cash required to repair the RV (Simple mode, and the price floor).");
            _questMode = CreateEntry("QuestMode", "Questline", "Quest Mode",
                "Questline (default) = the full Donna -> Ming -> Marco story (see docs/QUESTLINE.md). Simple = just talk to Marco and pay.");
            _basePriceNoReferral = CreateEntry("BasePriceNoReferral", 50000, "Questline: Price without referral",
                "Marco's price in Questline mode if you go straight to him.");
            _basePriceWithReferral = CreateEntry("BasePriceWithReferral", 10000, "Questline: Price with Ming's referral",
                "Marco's price in Questline mode after Mrs. Ming puts in a word.");
            _minSampleDiscount = CreateEntry("MinSampleDiscount", 100, "Questline: Min sample discount",
                "Smallest price cut a single free sample can give Marco (cheap product floors here).");
            _maxSampleDiscount = CreateEntry("MaxSampleDiscount", 500, "Questline: Max sample discount",
                "Largest price cut a single free sample can give. Each packaged sample cuts the price by its value, clamped between min and max, down to the Repair Price floor.");

            _upgradesEnabled = CreateEntry("UpgradesEnabled", true, "Marco's RV upgrades",
                "After the repair, Marco sells five build-outs that turn the RV into a working base you can drive. Turn off to keep the RV as a plain shell.");
            _priceGutInterior = CreateEntry("PriceGutInterior", 2500, "Upgrade price: gut the interior",
                "Rips out the bed, bench, cabinets, chairs and the partition wall to make room. Prerequisite for the workshop floor.");
            _priceWorkshopFloor = CreateEntry("PriceWorkshopFloor", 7500, "Upgrade price: workshop floor",
                "Extends the RV's build grid by Extra Build Tiles. The RV's own 32 tiles come free with the repair. Prerequisite for the crew quarters.");
            _priceCrewQuarters = CreateEntry("PriceCrewQuarters", 15000, "Upgrade price: crew quarters",
                "Lets you hire and station employees at the RV. They still need a locker placed inside before they work.");
            _priceLoadingDock = CreateEntry("PriceLoadingDock", 25000, "Upgrade price: loading dock",
                "Adds a delivery dock so suppliers can drop orders at the RV.");
            _priceEngine = CreateEntry("PriceEngine", 10000, "Upgrade price: engine",
                "Makes the RV drivable. While it is away from its spot, the crew and deliveries wait for it.");
            _crewSize = CreateEntry("CrewSize", 3, "Crew size",
                "How many employees the RV can hold once the crew quarters are built.");
            _buildGridExtraTiles = CreateEntry("BuildGridExtraTiles", 96, "Extra build tiles (cap)",
                "Upper limit for the tiles the workshop upgrade lays. It fills the whole cabin floor and stops here; raise it if any floor is left bare.");

            _instantRepair = CreateEntry("InstantRepair", false, "Repair the RV instantly",
                "Repairs the RV free of charge as soon as it is wrecked - no Marco, no questline. The upgrades still cost money.");
            _repairTakesADay = CreateEntry("RepairTakesADay", false, "Repair takes a day",
                "Marco takes the job and the RV is ready 24 in-game hours later; he texts you when it is done. Off = repaired on the spot.");
            _batteryBreakdowns = CreateEntry("BatteryBreakdowns", true, "Flat battery after standing",
                "After 3 in-game days without a drive, the RV has a 1 in 4 chance not to start. Hold a battery at the wheel to fix it.");
#if DEBUG
            _destroyRvDebug = CreateEntry("DestroyRvDebug", false, "Destroy RV (debug, one-shot)",
                "Toggle ON to manually wreck the RV so you can test the repair. Auto-resets to OFF.");
            _addCashDebug = CreateEntry("AddCashDebug", false, "Add $10,000 (debug, one-shot)",
                "Toggle ON to give yourself $10,000 cash for testing. Auto-resets to OFF.");
            _dumpStateDebug = CreateEntry("DumpRvStateDebug", false, "Dump RV state (debug, one-shot)",
                "Toggle ON (ideally while standing at the RV) to log the current RV state to the MelonLoader console. Auto-resets.");
            _testCinematicDebug = CreateEntry("TestRepairCinematicDebug", false, "Play repair cinematic (debug, one-shot)",
                "Toggle ON to play the repair fade-to-black + sound right where you stand, without going to Marco. Auto-resets.");
            _netPingDebug = CreateEntry("TestNetPingDebug", false, "Net ping (debug, one-shot)",
                "Toggle ON in a co-op session to send a network ping (host->clients + client->host). Watch the log for '[Net] host <- Ping' / '[Net] client <- Ping' on the other machine. Auto-resets.");
#endif
        }

#if DEBUG
        internal const float DebugCashAmount = 10000f;
#endif

        private static MelonPreferences_Entry<T> CreateEntry<T>(string identifier, T defaultValue, string displayName, string description = null)
        {
            return _category.CreateEntry(identifier, defaultValue, displayName, description);
        }

        internal static bool Enabled => _enabled?.Value ?? true;
        internal static int RepairPrice => Mathf.Max(0, _repairPrice?.Value ?? 1500);
        internal static bool QuestlineEnabled =>
            string.Equals(_questMode?.Value, "Questline", System.StringComparison.OrdinalIgnoreCase);
        internal static int BasePriceNoReferral => Mathf.Max(0, _basePriceNoReferral?.Value ?? 50000);
        internal static int BasePriceWithReferral => Mathf.Max(0, _basePriceWithReferral?.Value ?? 10000);
        internal static int MinSampleDiscount => Mathf.Max(0, _minSampleDiscount?.Value ?? 100);
        internal static int MaxSampleDiscount => Mathf.Max(MinSampleDiscount, _maxSampleDiscount?.Value ?? 500);

        internal static bool UpgradesEnabled => _upgradesEnabled?.Value ?? true;
        internal static int PriceGutInterior => Mathf.Max(0, _priceGutInterior?.Value ?? 2500);
        internal static int PriceWorkshopFloor => Mathf.Max(0, _priceWorkshopFloor?.Value ?? 7500);
        internal static int PriceCrewQuarters => Mathf.Max(0, _priceCrewQuarters?.Value ?? 15000);
        internal static int PriceLoadingDock => Mathf.Max(0, _priceLoadingDock?.Value ?? 25000);
        internal static int PriceEngine => Mathf.Max(0, _priceEngine?.Value ?? 10000);
        /// <summary>Employee capacity the crew upgrade grants. Clamped: 0 would leave the upgrade inert, and every
        /// slot needs its own idle point, so a silly number just wastes transforms.</summary>
        internal static int CrewSize => Mathf.Clamp(_crewSize?.Value ?? 3, 1, 10);
        internal static int BuildGridExtraTiles => Mathf.Clamp(_buildGridExtraTiles?.Value ?? 96, 0, 400);

        internal static bool InstantRepair => _instantRepair?.Value ?? false;
        internal static bool RepairTakesADay => _repairTakesADay?.Value ?? false;
        internal static bool BatteryBreakdowns => _batteryBreakdowns?.Value ?? true;

#if DEBUG
        /// <summary>
        /// Returns true once if the debug "Destroy RV" toggle is on, resetting it back to off
        /// (in-memory, to avoid a save -> OnPreferencesSaved recursion). One-shot.
        /// </summary>
        internal static bool ConsumeDestroyRequest()
        {
            if (_destroyRvDebug != null && _destroyRvDebug.Value)
            {
                _destroyRvDebug.Value = false;
                return true;
            }
            return false;
        }

        /// <summary>One-shot: true once if the debug "Add cash" toggle is on, then resets it.</summary>
        internal static bool ConsumeAddCashRequest()
        {
            if (_addCashDebug != null && _addCashDebug.Value)
            {
                _addCashDebug.Value = false;
                return true;
            }
            return false;
        }

        /// <summary>One-shot: true once if the debug "Dump RV state" toggle is on, then resets it.</summary>
        internal static bool ConsumeDumpRequest()
        {
            if (_dumpStateDebug != null && _dumpStateDebug.Value)
            {
                _dumpStateDebug.Value = false;
                return true;
            }
            return false;
        }

        /// <summary>One-shot: true once if the debug "Play repair cinematic" toggle is on, then resets it.</summary>
        internal static bool ConsumeTestCinematicRequest()
        {
            if (_testCinematicDebug != null && _testCinematicDebug.Value)
            {
                _testCinematicDebug.Value = false;
                return true;
            }
            return false;
        }

        /// <summary>One-shot: true once if the debug "Net ping" toggle is on, then resets it.</summary>
        internal static bool ConsumeNetPingRequest()
        {
            if (_netPingDebug != null && _netPingDebug.Value)
            {
                _netPingDebug.Value = false;
                return true;
            }
            return false;
        }
#endif
    }
}
