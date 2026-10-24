using S1API.Internal.Abstraction;
using S1API.Saveables;

namespace RVRepairVan.Persistence
{
    /// <summary>
    /// Quest state persisted via S1API's Saveable pipeline. S1API writes these fields ONLY when the game saves
    /// (SaveInternal) and reads them back on load (LoadInternal -> OnLoaded), so our state lives INSIDE the game
    /// save and moves in lockstep with it. That removes the old desync: previously the state was written to its
    /// own json immediately, so a completed-but-not-saved run (or a crash) left "repaired/Done" persisted forever
    /// while the game reverted the RV to wrecked - the quest then looked gone with no way back. Now "didn't save"
    /// reverts our state exactly like the game reverts the RV.
    ///
    /// S1API auto-discovers this type (non-abstract, parameterless ctor) and keeps a single instance. It writes
    /// one file per [SaveableField] into Modded/Saveables/RepairSave/, so the whole state is one field and the
    /// save folder holds one file, rv_state.json.
    /// </summary>
    public class RepairSave : Saveable
    {
        internal static RepairSave Instance { get; private set; }

        // True once the game's load pipeline has populated our fields (or left them at defaults on a fresh save).
        // The questline waits for this so it never acts on stale/unloaded state right after a scene load.
        internal static bool Loaded { get; private set; }

        /// <summary>
        /// Load BEFORE the base game's own loaders. The default (AfterBaseGame) is too late for the RV crew: the
        /// game restores a property's employees through EmployeeManager.CreateEmployee_Server, which refuses while
        /// Employees.Count >= EmployeeCapacity - so by the time we learned the crew quarters were paid for, the
        /// employees had already been dropped. Loading early means the upgrade mask is known before the first
        /// vanilla load request runs, which is what lets RvCrew provision the RV in time.
        /// </summary>
        public override SaveableLoadOrder LoadOrder => SaveableLoadOrder.BeforeBaseGame;

        /// <summary>
        /// The persisted state. A plain managed class with primitive fields, serialised by Newtonsoft as one
        /// JSON object. Format is 0 only on an instance that no rv_state.json was read into.
        /// </summary>
        internal sealed class State
        {
            public int Format;
            public bool Repaired;
            public int Stage;
            public int Samples;
            public int Discount;
            // Bitmask of the upgrades Marco has been paid for (see RvUpgrade). The upgrades themselves are runtime
            // effects on scene objects, so this mask is what gets re-applied on every load.
            public int Upgrades;
            // In-game minute stamp (TimeManager.GetTotalMinSum) at which Marco took the job, for the
            // "repair takes a day" option. 0 = no job running. Persisted so a reload keeps the remaining time.
            public int RepairStartedAt;
            // Where the RV stands when it was driven off its spot (Engine upgrade): the chassis pose. Not away = home.
            public bool Away;
            public float AwayPx, AwayPy, AwayPz;
            public float AwayRx, AwayRy, AwayRz, AwayRw = 1f;
            // In-game minute stamp of the last drive (Engine upgrade), and whether the battery went flat after standing.
            public int LastDriven;
            public bool BatteryDead;
        }

        internal const int CurrentFormat = 1;

        // Names must not be "QuestData" (reserved by the API).
        [SaveableField("rv_state")] private State _state = new State();

        // The per-field files of 3.6.2 and older. S1API still reads them into these fields when they exist; OnLoaded
        // moves them into _state and sets them to null, and S1API deletes the file of a null field on the next save.
        [SaveableField("rv_repaired")] private bool? _oldRepaired;
        [SaveableField("rv_stage")] private int? _oldStage;
        [SaveableField("rv_samples")] private int? _oldSamples;
        [SaveableField("rv_discount")] private int? _oldDiscount;
        [SaveableField("rv_upgrades")] private int? _oldUpgrades;
        [SaveableField("rv_repair_started")] private int? _oldRepairStarted;
        [SaveableField("rv_away")] private bool? _oldAway;
        [SaveableField("rv_away_px")] private float? _oldAwayPx;
        [SaveableField("rv_away_py")] private float? _oldAwayPy;
        [SaveableField("rv_away_pz")] private float? _oldAwayPz;
        [SaveableField("rv_away_rx")] private float? _oldAwayRx;
        [SaveableField("rv_away_ry")] private float? _oldAwayRy;
        [SaveableField("rv_away_rz")] private float? _oldAwayRz;
        [SaveableField("rv_away_rw")] private float? _oldAwayRw;
        [SaveableField("rv_last_driven")] private int? _oldLastDriven;
        [SaveableField("rv_battery_dead")] private bool? _oldBatteryDead;

        public RepairSave() { Instance = this; }

        internal bool Repaired { get => _state.Repaired; set => _state.Repaired = value; }
        internal int Stage { get => _state.Stage; set => _state.Stage = value; }
        internal int Samples { get => _state.Samples; set => _state.Samples = value; }
        internal int Discount { get => _state.Discount; set => _state.Discount = value; }
        internal int Upgrades { get => _state.Upgrades; set => _state.Upgrades = value; }
        internal int RepairStartedAt { get => _state.RepairStartedAt; set => _state.RepairStartedAt = value; }
        internal int LastDriven { get => _state.LastDriven; set => _state.LastDriven = value; }
        internal bool BatteryDead { get => _state.BatteryDead; set => _state.BatteryDead = value; }

        internal bool Away => _state.Away;
        internal Vector3 AwayPos => new Vector3(_state.AwayPx, _state.AwayPy, _state.AwayPz);
        internal Quaternion AwayRot => new Quaternion(_state.AwayRx, _state.AwayRy, _state.AwayRz, _state.AwayRw);

        internal void SetAway(Vector3 pos, Quaternion rot)
        {
            _state.Away = true;
            _state.AwayPx = pos.x; _state.AwayPy = pos.y; _state.AwayPz = pos.z;
            _state.AwayRx = rot.x; _state.AwayRy = rot.y; _state.AwayRz = rot.z; _state.AwayRw = rot.w;
        }

        internal void ClearAway()
        {
            _state.Away = false;
            _state.AwayPx = _state.AwayPy = _state.AwayPz = 0f;
            _state.AwayRx = _state.AwayRy = _state.AwayRz = 0f;
            _state.AwayRw = 1f;
        }

        /// <summary>
        /// Reset to defaults between saves. The wipe matters because S1API keeps ONE instance per type for the
        /// whole process: loading save A then save B reuses the same object, and for a save with no prior mod data
        /// the pipeline calls OnCreated (not OnLoaded) - without this reset, save A's values would leak into save B.
        ///
        /// Call this on the MENU scene, not on "Main". S1API's OnLoaded does not have a fixed order relative to
        /// MelonLoader's OnSceneWasLoaded: going menu -> load, OnLoaded fires FIRST, so wiping on "Main" threw away
        /// the values that had just been read (measured: "[State] loaded: repaired=True" followed by the questline
        /// restarting from scratch because everything read back as zero). The menu is the one point that is always
        /// between two saves and always before the load.
        /// </summary>
        internal static void BeginLoad()
        {
            Loaded = false;
            if (Instance != null)
            {
                Instance._state = new State();
                Instance.ClearOldFields();
            }
        }

        // S1API found existing save data for this save, so OnLoaded ran (not OnCreated). Fields were just deserialised.
        protected override void OnLoaded()
        {
            Instance = this;
            Loaded = true;
            if (_state == null)
            {
                Core.Log.Warning("[State] rv_state.json held null - starting from defaults.");
                _state = new State();
            }
            if (_state.Format == 0)
                MigrateOldFields();
            else if (ClearOldFields() > 0)
                Core.Log.Warning("[State] old per-field files lie beside rv_state.json - rv_state.json wins, the old files go on the next save.");
            _state.Format = CurrentFormat;
            _state.Stage = Quests.Questline.StageForLoad(_state.Repaired, _state.Stage);
            Core.Log.Msg($"[State] loaded: repaired={_state.Repaired} stage={_state.Stage} samples={_state.Samples} discount={_state.Discount} upgrades={_state.Upgrades} away={_state.Away} lastDriven={_state.LastDriven} batteryDead={_state.BatteryDead}");
        }

        /// <summary>
        /// A save from 3.6.2 or older has no rv_state.json but up to 16 single files. Copy what is there into
        /// _state; a missing file keeps its default, as it did when each field was its own file.
        /// </summary>
        private void MigrateOldFields()
        {
            _state.Repaired = _oldRepaired ?? false;
            _state.Stage = _oldStage ?? 0;
            _state.Samples = _oldSamples ?? 0;
            _state.Discount = _oldDiscount ?? 0;
            _state.Upgrades = _oldUpgrades ?? 0;
            _state.RepairStartedAt = _oldRepairStarted ?? 0;
            _state.Away = _oldAway ?? false;
            _state.AwayPx = _oldAwayPx ?? 0f;
            _state.AwayPy = _oldAwayPy ?? 0f;
            _state.AwayPz = _oldAwayPz ?? 0f;
            _state.AwayRx = _oldAwayRx ?? 0f;
            _state.AwayRy = _oldAwayRy ?? 0f;
            _state.AwayRz = _oldAwayRz ?? 0f;
            _state.AwayRw = _oldAwayRw ?? 1f;
            _state.LastDriven = _oldLastDriven ?? 0;
            _state.BatteryDead = _oldBatteryDead ?? false;
            int found = ClearOldFields();
            if (found > 0)
                Core.Log.Msg($"[State] read {found} old per-field file(s) - the next save writes rv_state.json and removes them.");
            else
                Core.Log.Msg("[State] save folder exists but holds no RV state - defaults applied.");
        }

        // Sets every old per-field value to null and returns how many were set.
        private int ClearOldFields()
        {
            int n = 0;
            if (_oldRepaired != null) { _oldRepaired = null; n++; }
            if (_oldStage != null) { _oldStage = null; n++; }
            if (_oldSamples != null) { _oldSamples = null; n++; }
            if (_oldDiscount != null) { _oldDiscount = null; n++; }
            if (_oldUpgrades != null) { _oldUpgrades = null; n++; }
            if (_oldRepairStarted != null) { _oldRepairStarted = null; n++; }
            if (_oldAway != null) { _oldAway = null; n++; }
            if (_oldAwayPx != null) { _oldAwayPx = null; n++; }
            if (_oldAwayPy != null) { _oldAwayPy = null; n++; }
            if (_oldAwayPz != null) { _oldAwayPz = null; n++; }
            if (_oldAwayRx != null) { _oldAwayRx = null; n++; }
            if (_oldAwayRy != null) { _oldAwayRy = null; n++; }
            if (_oldAwayRz != null) { _oldAwayRz = null; n++; }
            if (_oldAwayRw != null) { _oldAwayRw = null; n++; }
            if (_oldLastDriven != null) { _oldLastDriven = null; n++; }
            if (_oldBatteryDead != null) { _oldBatteryDead = null; n++; }
            return n;
        }

        // No mod data for this save yet (fresh save / first time / a save from before the mod) -> clean defaults.
        // S1API calls this when our save folder does not exist, so it is the "fresh save" signal. The Loaded guard
        // keeps a value that OnLoaded already read if both ever fire for the same load.
        protected override void OnCreated()
        {
            Instance = this;

            if (Loaded)
            {
                Core.Log.Msg("[State] OnCreated fired after OnLoaded - keeping what was loaded.");
                return;
            }

            _state = new State { Format = CurrentFormat };
            ClearOldFields();
            Loaded = true;
            Core.Log.Msg("[State] created (fresh save) - defaults applied.");
        }

        protected override void OnSaved()
        {
            Core.Log.Msg($"[State] saved rv_state.json: repaired={_state.Repaired} stage={_state.Stage} samples={_state.Samples} discount={_state.Discount} upgrades={_state.Upgrades}");
        }
    }
}
