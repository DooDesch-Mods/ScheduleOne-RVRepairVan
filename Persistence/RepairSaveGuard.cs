using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json;

namespace RVRepairVan.Persistence
{
    /// <summary>
    /// Checks the RV save files before S1API reads them, and repairs a broken one.
    ///
    /// S1API reads every BeforeBaseGame saveable inside its own prefix on LoadManager.QueueLoadRequest. One file
    /// that does not parse throws out of that loop: OnLoaded never runs, RepairSave keeps the defaults from
    /// BeginLoad, and the next game save writes those defaults over the real progress (Stage 1, no upgrades).
    /// The same throw also skips every other mod's BeforeBaseGame saveable that comes after ours.
    ///
    /// This prefix runs first. A file that does not parse is copied aside as *.broken-&lt;time&gt;.json, and
    /// replaced by what could be read from it before the break, so S1API then loads a valid file.
    /// </summary>
    [HarmonyPatch(typeof(LoadManager), nameof(LoadManager.QueueLoadRequest))]
    internal static class RepairSaveGuard
    {
        private const string Folder = "RepairSave";
        private const string StateFile = "rv_state.json";

        // The per-field files of 3.6.2 and older, with the type each one held.
        private static readonly Dictionary<string, Type> OldFiles = new Dictionary<string, Type>
        {
            { "rv_repaired", typeof(bool) }, { "rv_stage", typeof(int) }, { "rv_samples", typeof(int) },
            { "rv_discount", typeof(int) }, { "rv_upgrades", typeof(int) }, { "rv_repair_started", typeof(int) },
            { "rv_away", typeof(bool) }, { "rv_away_px", typeof(float) }, { "rv_away_py", typeof(float) },
            { "rv_away_pz", typeof(float) }, { "rv_away_rx", typeof(float) }, { "rv_away_ry", typeof(float) },
            { "rv_away_rz", typeof(float) }, { "rv_away_rw", typeof(float) }, { "rv_last_driven", typeof(int) },
            { "rv_battery_dead", typeof(bool) },
        };

        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            string dir = null;
            try
            {
                LoadManager lm = LoadManager.Instance;
                string game = lm != null ? lm.LoadedGameFolderPath : null;
                if (string.IsNullOrEmpty(game)) return;   // S1API skips the same case: nothing is loaded from disk
                dir = Path.Combine(game, "Modded", "Saveables", Folder);
                if (!Directory.Exists(dir)) return;      // fresh save: S1API calls OnCreated

                CheckState(Path.Combine(dir, StateFile));
                foreach (var old in OldFiles)
                    CheckOldFile(Path.Combine(dir, old.Key + ".json"), old.Value);
            }
            catch (Exception e)
            {
                Core.Log.Error("[State] save file check failed in " + (dir ?? "(no folder)") + ": " + e);
            }
        }

        private static void CheckState(string path)
        {
            if (!File.Exists(path)) return;
            string json = File.ReadAllText(path);
            try
            {
                if (JsonConvert.DeserializeObject<RepairSave.State>(json) != null) return;
                throw new JsonException("the file holds no object");
            }
            catch (JsonException e)
            {
                string backup = Backup(path);
                RepairSave.State salvaged = Salvage(json, out int found);
                salvaged.Format = RepairSave.CurrentFormat;
                File.WriteAllText(path, JsonConvert.SerializeObject(salvaged, Formatting.Indented));
                Core.Log.Error($"[State] {StateFile} is damaged ({e.Message}). Kept {found} value(s) from before the damage, "
                    + $"the rest starts from defaults. The damaged file is saved as {Path.GetFileName(backup)}.");
            }
        }

        private static void CheckOldFile(string path, Type type)
        {
            if (!File.Exists(path)) return;
            string json = File.ReadAllText(path);
            try
            {
                JsonConvert.DeserializeObject(json, type);
            }
            catch (JsonException e)
            {
                string backup = Backup(path);
                File.Delete(path);
                Core.Log.Error($"[State] {Path.GetFileName(path)} is damaged ({e.Message}) - that value starts from its default. "
                    + $"The damaged file is saved as {Path.GetFileName(backup)}.");
            }
        }

        private static string Backup(string path)
        {
            string backup = Path.Combine(Path.GetDirectoryName(path),
                Path.GetFileNameWithoutExtension(path) + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            File.Copy(path, backup, true);
            return backup;
        }

        /// <summary>
        /// Reads property/value pairs until the JSON breaks. A save cut off mid-write keeps everything written
        /// before the cut; a value of the wrong type is skipped.
        /// </summary>
        private static RepairSave.State Salvage(string json, out int found)
        {
            var state = new RepairSave.State();
            found = 0;
            string name = null;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonToken.PropertyName) { name = (string)reader.Value; continue; }
                        if (name == null || reader.Value == null) continue;
                        if (Apply(state, name, reader.Value)) found++;
                        name = null;
                    }
                }
            }
            catch (JsonException e) { Core.Log.Msg($"[State] {StateFile}: reading stopped at the damage ({e.Message})."); }
            return state;
        }

        private static bool Apply(RepairSave.State s, string name, object v)
        {
            try
            {
                switch (name)
                {
                    case nameof(RepairSave.State.Repaired): s.Repaired = Convert.ToBoolean(v); return true;
                    case nameof(RepairSave.State.Stage): s.Stage = Convert.ToInt32(v); return true;
                    case nameof(RepairSave.State.Samples): s.Samples = Convert.ToInt32(v); return true;
                    case nameof(RepairSave.State.Discount): s.Discount = Convert.ToInt32(v); return true;
                    case nameof(RepairSave.State.Upgrades): s.Upgrades = Convert.ToInt32(v); return true;
                    case nameof(RepairSave.State.RepairStartedAt): s.RepairStartedAt = Convert.ToInt32(v); return true;
                    case nameof(RepairSave.State.Away): s.Away = Convert.ToBoolean(v); return true;
                    case nameof(RepairSave.State.AwayPx): s.AwayPx = Convert.ToSingle(v); return true;
                    case nameof(RepairSave.State.AwayPy): s.AwayPy = Convert.ToSingle(v); return true;
                    case nameof(RepairSave.State.AwayPz): s.AwayPz = Convert.ToSingle(v); return true;
                    case nameof(RepairSave.State.AwayRx): s.AwayRx = Convert.ToSingle(v); return true;
                    case nameof(RepairSave.State.AwayRy): s.AwayRy = Convert.ToSingle(v); return true;
                    case nameof(RepairSave.State.AwayRz): s.AwayRz = Convert.ToSingle(v); return true;
                    case nameof(RepairSave.State.AwayRw): s.AwayRw = Convert.ToSingle(v); return true;
                    case nameof(RepairSave.State.LastDriven): s.LastDriven = Convert.ToInt32(v); return true;
                    case nameof(RepairSave.State.BatteryDead): s.BatteryDead = Convert.ToBoolean(v); return true;
                    case nameof(RepairSave.State.InteriorUnlocked): s.InteriorUnlocked = Convert.ToInt32(v); return true;
                    case nameof(RepairSave.State.InteriorRemoved): s.InteriorRemoved = Convert.ToInt32(v); return true;
                    case nameof(RepairSave.State.Paint): s.Paint = Convert.ToString(v) ?? ""; return true;
                    case nameof(RepairSave.State.Paint2): s.Paint2 = Convert.ToString(v) ?? ""; return true;
                    default: return false;
                }
            }
            catch (Exception e) when (e is FormatException || e is InvalidCastException || e is OverflowException)
            {
                Core.Log.Warning($"[State] {StateFile}: value of {name} ('{v}') does not fit - it starts from its default.");
                return false;
            }
        }
    }
}
