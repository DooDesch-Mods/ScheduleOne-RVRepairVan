using System;
using System.Collections;
using MelonLoader;
using RVRepairVan.Managers;

namespace RVRepairVan.Compat
{
    /// <summary>
    /// Schedule Maintenance (BeagleBango, MixingStationMkIII.dll) parks a repainted copy of the RV, the "Krystal Ship",
    /// on the RV spot once the intro is done, and puts its own "Knock" prompt over the door. It assumes the RV stays
    /// wrecked. While our RV stands repaired, its copy and its door are switched off, so the player sees and opens ours.
    /// Switched off, not destroyed: that mod rebuilds the copy every second once its object is gone.
    /// </summary>
    internal static class ScheduleMaintenance
    {
        private const string ShipName = "SM Krystal Ship (Cap'n Cook)";
        private const string DoorName = "SM RV Door";
        private const float CheckInterval = 2f;

        private static bool? _loaded;
        private static GameObject _ship, _door;
        private static bool _hidden;

        internal static bool Loaded
        {
            get
            {
                if (_loaded.HasValue) return _loaded.Value;
                _loaded = false;
                try
                {
                    foreach (MelonMod m in MelonMod.RegisteredMelons)
                    {
                        string asm = m.MelonAssembly?.Assembly?.GetName().Name ?? "";
                        if (asm.Equals("MixingStationMkIII", StringComparison.OrdinalIgnoreCase)
                            || (m.Info?.Name ?? "").Equals("Schedule Maintenance", StringComparison.OrdinalIgnoreCase))
                        {
                            _loaded = true;
                            Core.Log.Msg("[Compat] Schedule Maintenance " + m.Info?.Version + " is loaded - its Krystal Ship gives way to a repaired RV.");
                            break;
                        }
                    }
                }
                catch (Exception e) { Core.Log.Warning("[Compat] could not list the loaded mods: " + e.Message); }
                return _loaded.Value;
            }
        }

        /// <summary>Runs for one loaded save; stops when the next scene load bumps the generation.</summary>
        internal static IEnumerator Watch(int generation)
        {
            _ship = _door = null;
            _hidden = false;
            if (!Loaded) yield break;
            while (generation == Core.SceneGeneration)
            {
                yield return new WaitForSeconds(CheckInterval);
                if (generation != Core.SceneGeneration) break;
                try { Check(); }
                catch (Exception e) { Core.Log.Error("[Compat] Schedule Maintenance check failed: " + e.Message); }
            }
        }

        private static void Check()
        {
            // GameObject.Find only sees active objects, so the references are kept once found.
            if (_ship == null) _ship = GameObject.Find(ShipName);
            if (_door == null) _door = GameObject.Find(DoorName);
            if (_ship == null && _door == null) return;

            bool repaired = RVManager.Model != null && !RVManager.IsDestroyed();
            if (repaired == _hidden) return;

            if (_ship != null) _ship.SetActive(!repaired);
            if (_door != null) _door.SetActive(!repaired);
            _hidden = repaired;
            Core.Log.Msg(repaired
                ? "[Compat] the RV is repaired: Schedule Maintenance's Krystal Ship and its Knock prompt are switched off."
                : "[Compat] the RV is wrecked: Schedule Maintenance's Krystal Ship is back on the spot.");
        }
    }
}
