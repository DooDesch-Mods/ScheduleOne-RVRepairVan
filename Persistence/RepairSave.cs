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
    /// S1API auto-discovers this type (non-abstract, parameterless ctor) and keeps a single instance.
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

        // One file per field (S1API serialises each [SaveableField] separately) - primitives keep IL2CPP JSON
        // reflection trivial and risk-free. Names must not be "QuestData" (reserved by the API).
        [SaveableField("rv_repaired")] private bool _repaired;
        [SaveableField("rv_stage")] private int _stage;
        [SaveableField("rv_samples")] private int _samples;
        [SaveableField("rv_discount")] private int _discount;
        // Bitmask of the upgrades Marco has been paid for (see RvUpgrade). The upgrades themselves are runtime
        // effects on scene objects, so this mask is what gets re-applied on every load.
        [SaveableField("rv_upgrades")] private int _upgrades;
        // In-game minute stamp (TimeManager.GetTotalMinSum) at which Marco took the job, for the
        // "repair takes a day" option. 0 = no job running. Persisted so a reload keeps the remaining time.
        [SaveableField("rv_repair_started")] private int _repairStartedAt;
        // Where the RV stands when it was driven off its spot (Engine upgrade): the chassis pose. Not away = home.
        [SaveableField("rv_away")] private bool _away;
        [SaveableField("rv_away_px")] private float _awayPx;
        [SaveableField("rv_away_py")] private float _awayPy;
        [SaveableField("rv_away_pz")] private float _awayPz;
        [SaveableField("rv_away_rx")] private float _awayRx;
        [SaveableField("rv_away_ry")] private float _awayRy;
        [SaveableField("rv_away_rz")] private float _awayRz;
        [SaveableField("rv_away_rw")] private float _awayRw;
        // In-game minute stamp of the last drive (Engine upgrade), and whether the battery went flat after standing.
        [SaveableField("rv_last_driven")] private int _lastDriven;
        [SaveableField("rv_battery_dead")] private bool _batteryDead;

        public RepairSave() { Instance = this; }

        internal bool Repaired { get => _repaired; set => _repaired = value; }
        internal int Stage { get => _stage; set => _stage = value; }
        internal int Samples { get => _samples; set => _samples = value; }
        internal int Discount { get => _discount; set => _discount = value; }
        internal int Upgrades { get => _upgrades; set => _upgrades = value; }
        internal int RepairStartedAt { get => _repairStartedAt; set => _repairStartedAt = value; }
        internal int LastDriven { get => _lastDriven; set => _lastDriven = value; }
        internal bool BatteryDead { get => _batteryDead; set => _batteryDead = value; }

        internal bool Away => _away;
        internal Vector3 AwayPos => new Vector3(_awayPx, _awayPy, _awayPz);
        internal Quaternion AwayRot => new Quaternion(_awayRx, _awayRy, _awayRz, _awayRw);

        internal void SetAway(Vector3 pos, Quaternion rot)
        {
            _away = true;
            _awayPx = pos.x; _awayPy = pos.y; _awayPz = pos.z;
            _awayRx = rot.x; _awayRy = rot.y; _awayRz = rot.z; _awayRw = rot.w;
        }

        internal void ClearAway() => SetAwayFields(false);

        private void SetAwayFields(bool away)
        {
            _away = away;
            _awayPx = _awayPy = _awayPz = 0f;
            _awayRx = _awayRy = _awayRz = 0f;
            _awayRw = 1f;
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
                Instance._repaired = false;
                Instance._stage = 0;
                Instance._samples = 0;
                Instance._discount = 0;
                Instance._upgrades = 0;
                Instance._repairStartedAt = 0;
                Instance._lastDriven = 0;
                Instance._batteryDead = false;
                Instance.SetAwayFields(false);
            }
        }

        // S1API found existing save data for this save, so OnLoaded ran (not OnCreated). Fields were just deserialised.
        protected override void OnLoaded()
        {
            Instance = this;
            Loaded = true;
            _stage = Quests.Questline.StageForLoad(_repaired, _stage);
            Core.Log.Msg($"[State] loaded: repaired={_repaired} stage={_stage} samples={_samples} discount={_discount} upgrades={_upgrades} away={_away} lastDriven={_lastDriven} batteryDead={_batteryDead}");
        }

        // No mod data for this save yet (fresh save / first time / a save from before the mod) -> clean defaults.
        // S1API calls this when our save folder does not exist, so it is the "fresh save" signal. It is NOT exclusive
        // with OnLoaded: S1API serializes each [SaveableField] separately, and when only some of them are on disk (a
        // save written before rv_battery_dead or the rv_away_* fields existed) it fires OnLoaded for the present ones
        // and then OnCreated for the missing ones. A blind reset there wipes the questline progress just read.
        protected override void OnCreated()
        {
            Instance = this;

            if (Loaded)
            {
                Core.Log.Msg("[State] OnCreated fired after OnLoaded (partial save data) - keeping what was loaded.");
                return;
            }

            _repaired = false;
            _stage = 0;
            _samples = 0;
            _discount = 0;
            _upgrades = 0;
            _repairStartedAt = 0;
            _lastDriven = 0;
            _batteryDead = false;
            SetAwayFields(false);
            Loaded = true;
            Core.Log.Msg("[State] created (fresh save) - defaults applied.");
        }

        protected override void OnSaved()
        {
            Core.Log.Msg($"[State] saved: repaired={_repaired} stage={_stage} samples={_samples} discount={_discount} upgrades={_upgrades}");
        }
    }
}
