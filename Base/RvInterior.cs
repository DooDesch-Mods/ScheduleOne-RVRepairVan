using System;
using System.Collections.Generic;
using RVRepairVan.Managers;

namespace RVRepairVan.Base
{
    /// <summary>
    /// Upgrade 1 - "gut the interior". Clears the RV's fixed furniture so there is floor to build on.
    ///
    /// The object list is exact, taken from a live measurement (see
    /// Workspace/docs/RVRepairVan/VANILLA-RV-PROPERTY.md): six children under
    /// <c>RV/rv/Main/Interior</c> plus the partition <c>RV/rv/Main/Wall.001</c>. A curated list on purpose -
    /// name heuristics over the whole hierarchy are how the competing mod ends up hiding things it meant to keep.
    ///
    /// The original active state is snapshotted before anything is switched off, so a revert cannot turn on an
    /// object the game had deliberately disabled.
    /// </summary>
    internal static class RvInterior
    {
        // Under RV/rv/Main/Interior. "Dash", "Chair" and "Chair (1)" are the driver's cab (dashboard at z -5.25, both
        // seats at z -4.22 beside the steering column) and stay: with them hidden the front of the RV is a hole the
        // player can see straight through to the outside.
        private static readonly string[] InteriorChildren =
        {
            "Bed", "Bench", "Cabinets",
        };

        // Under RV/rv/Main directly - and this is the half that was missed at first. The furniture is NOT all
        // parented to Interior: the wall cabinets and the second partition sit one level up, so gutting only the
        // Interior children left the cabinets and one wall standing. Measured with rvprobe, which dumps the whole
        // model tree; "Main" has 55 children and only 6 of them are the Interior group.
        // Structure stays: Floor, Steps, Walls .002, windows, mirrors, lights, bumpers and the driver's controls.
        private static readonly string[] MainChildren =
        {
            "Wall.001", "Wall.001 (1)",
            "Cabinet.001", "Cabinet.001 (1)",
            "Cabinet.002", "Cabinet.002 (1)",
            "Cabinet.003", "Cabinet.003 (1)",
            "Cabinet.004", "Cabinet.004 (1)",
        };

        // Loose props parented straight to the model root (RV/RV), a level above even Main. This is the clutter
        // that is actually visible standing inside: the tarp on the floor, the bedside table, the radio, the vase,
        // the plant and the leftovers of somebody's lunch.
        // NOT touched: 'rv' (the vehicle itself), 'Container' (where placed buildables live), 'Pot location',
        // the triggers, the audio zone and the occlusion area.
        private static readonly string[] RootProps =
        {
            "SM_Prop_Tarp_Generic_01", "Bedside_Table", "SM_Item_Radio_01", "Vase", "Ashtray",
            "SM_Prop_Plant_02", "Can_Cuke", "Cuke_Crushed_Junk", "Water Bottle",
        };

        private const string MainPath = "rv/Main";
        private const string InteriorPath = "rv/Main/Interior";

        // name -> was it active before we touched it
        private static readonly Dictionary<string, bool> _original = new Dictionary<string, bool>();
        private static bool _applied;

        internal static bool Applied => _applied;

        internal static void Reset() { _original.Clear(); _applied = false; }

        internal static void Apply()
        {
            if (_applied) return;
            try
            {
                Transform model = ModelRoot();
                if (model == null) { Core.LogDebug("[Interior] RV model not found yet - will retry on the next apply."); return; }

                int hidden = 0;
                Transform interior = model.Find(InteriorPath);
                if (interior != null)
                {
                    foreach (string name in InteriorChildren)
                    {
                        Transform t = interior.Find(name);
                        if (t == null) continue;
                        Remember(name, t.gameObject.activeSelf);
                        if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); hidden++; }
                    }
                }
                else Core.Log.Warning("[Interior] '" + InteriorPath + "' not found under the RV model.");

                Transform main = model.Find(MainPath);
                if (main != null)
                {
                    foreach (string name in MainChildren)
                    {
                        Transform t = main.Find(name);
                        if (t == null) continue;
                        Remember("Main/" + name, t.gameObject.activeSelf);
                        if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); hidden++; }
                    }
                }
                else Core.Log.Warning("[Interior] '" + MainPath + "' not found under the RV model.");

                foreach (string name in RootProps)
                {
                    Transform t = model.Find(name);
                    if (t == null) continue;
                    Remember("Root/" + name, t.gameObject.activeSelf);
                    if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); hidden++; }
                }

                _applied = true;
                Core.Log.Msg("[Interior] gutted - " + hidden + " object(s) hidden.");
                RvEasterEggs.Ensure();
            }
            catch (Exception e) { Core.Log.Warning("[Interior] apply failed: " + e.Message); }
        }

        /// <summary>Put the interior back the way it was found. Only used by the debug revoke path.</summary>
        internal static void Revert()
        {
            try
            {
                Transform model = ModelRoot();
                if (model == null) return;
                Transform interior = model.Find(InteriorPath);
                foreach (string name in InteriorChildren)
                {
                    Transform t = interior != null ? interior.Find(name) : null;
                    if (t != null && _original.TryGetValue(name, out bool was)) t.gameObject.SetActive(was);
                }
                Transform main = model.Find(MainPath);
                foreach (string name in MainChildren)
                {
                    Transform t = main != null ? main.Find(name) : null;
                    if (t != null && _original.TryGetValue("Main/" + name, out bool was)) t.gameObject.SetActive(was);
                }
                foreach (string name in RootProps)
                {
                    Transform t = model.Find(name);
                    if (t != null && _original.TryGetValue("Root/" + name, out bool was)) t.gameObject.SetActive(was);
                }
                _applied = false;
                Core.Log.Msg("[Interior] restored to its original state.");
                RvEasterEggs.Ensure();
            }
            catch (Exception e) { Core.Log.Warning("[Interior] revert failed: " + e.Message); }
        }

        private static void Remember(string key, bool active)
        {
            if (!_original.ContainsKey(key)) _original[key] = active;
        }

        // The furniture hangs off the INTACT model ("RV"), not the wreck - so this only resolves once repaired.
        private static Transform ModelRoot()
        {
            return RVManager.Model;
        }
    }
}
