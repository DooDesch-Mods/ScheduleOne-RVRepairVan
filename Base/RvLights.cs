using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Misc;
using Il2CppScheduleOne.Vehicles;

namespace RVRepairVan.Base
{
    /// <summary>
    /// The RV's own lamps while it drives. The hidden chassis keeps its <see cref="VehicleLights"/>: the headlight key,
    /// and the brake and reverse state it syncs to every client. Its lamps are emptied out of that component and
    /// switched off, so they do not glow inside the cabin; the RV's headlights, brake lights and reverse lights follow
    /// the same state instead. Vanilla never drives the RV's lamps, so at home they stay as the scene left them.
    /// </summary>
    internal static class RvLights
    {
        private static LandVehicle _chassis;
        private static VehicleLights _source;
        private static Material _lensOn;
        private static readonly List<ToggleableLight> _brake = new List<ToggleableLight>();
        private static readonly List<ToggleableLight> _reverse = new List<ToggleableLight>();
        private static readonly List<GameObject> _beams = new List<GameObject>();
        private static readonly List<Lens> _lenses = new List<Lens>();
        private static bool _bound, _head, _brakeOn, _reverseOn;

        private sealed class Lens
        {
            public Renderer R;
            public Material[] Off;
        }

        /// <summary>Switch the chassis' own lamps off for good. Call before the RV is parented onto it.</summary>
        internal static void MuteChassis(LandVehicle chassis)
        {
            _chassis = chassis;
            _source = null;
            _lensOn = null;
            try
            {
                VehicleLights vl = chassis.GetComponent<VehicleLights>();
                if (vl == null) Core.Log.Warning("[Lights] the chassis has no VehicleLights - the RV lamps stay dark.");
                else
                {
                    _source = vl;
                    _lensOn = vl.headlightMat_On;
                    // Emptied, the component still tracks the state but has nothing left to switch on.
                    vl.headLightMeshes = new Il2CppReferenceArray<MeshRenderer>(0);
                    vl.headLightSources = new Il2CppReferenceArray<OptimizedLight>(0);
                    vl.brakeLightMeshes = new Il2CppReferenceArray<MeshRenderer>(0);
                    vl.brakeLightSources = new Il2CppReferenceArray<Light>(0);
                    vl.reverseLightMeshes = new Il2CppReferenceArray<MeshRenderer>(0);
                    vl.reverseLightSources = new Il2CppReferenceArray<Light>(0);
                }
                int optimized = 0, lights = 0;
                foreach (OptimizedLight ol in chassis.GetComponentsInChildren<OptimizedLight>(true)) { ol.Enabled = false; optimized++; }
                foreach (Light l in chassis.GetComponentsInChildren<Light>(true)) { l.enabled = false; lights++; }
                Core.Log.Msg("[Lights] chassis lamps off: " + lights + " light(s), " + optimized + " managed.");
            }
            catch (Exception e) { Core.Log.Error("[Lights] muting the chassis lamps failed: " + e); }
        }

        /// <summary>Find the RV's lamps under its model and put them all off.</summary>
        internal static void Bind(Transform model)
        {
            Unbind();
            if (model == null) { Core.Log.Warning("[Lights] no RV model to bind the lamps to."); return; }
            try
            {
                foreach (ToggleableLight tl in model.GetComponentsInChildren<ToggleableLight>(true))
                {
                    string n = tl.name;
                    if (n.IndexOf("Brake", StringComparison.OrdinalIgnoreCase) >= 0) _brake.Add(tl);
                    else if (n.IndexOf("Reverse", StringComparison.OrdinalIgnoreCase) >= 0) _reverse.Add(tl);
                    else { Core.Log.Msg("[Lights] lamp '" + n + "' is neither brake nor reverse - left alone."); continue; }
                    Core.LogDebug("[Lights]   " + n + ": slot " + tl.MaterialIndex + " on=" + (tl.lightOnMat != null ? tl.lightOnMat.name : "none")
                        + " off=" + (tl.lightOffMat != null ? tl.lightOffMat.name : "none") + " " + Describe(tl.lightSurfacesMeshes));
                }
                foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.StartsWith("Headlight", StringComparison.OrdinalIgnoreCase)) continue;
                    Renderer r = t.GetComponent<Renderer>();
                    if (r != null && r.enabled && t.gameObject.activeInHierarchy)
                        _lenses.Add(new Lens { R = r, Off = r.sharedMaterials });
                    foreach (Light l in t.GetComponentsInChildren<Light>(true))
                        if (!_beams.Contains(l.gameObject)) _beams.Add(l.gameObject);
                }
                _bound = true;
                _head = _brakeOn = _reverseOn = true;   // differs from every state, so the first apply runs in full
                Apply(false, false, false);
                Core.Log.Msg("[Lights] RV lamps: " + _brake.Count + " brake, " + _reverse.Count + " reverse, " + _lenses.Count
                    + " headlight lens(es), " + _beams.Count + " beam(s)" + (_lensOn == null ? ", no lit lens material" : "") + ".");
            }
            catch (Exception e) { Core.Log.Error("[Lights] binding the RV lamps failed: " + e); }
        }

#if DEBUG
        private static bool _forced;

        /// <summary>rvdrive lamps head|brake|reverse|all|off|auto: hold the lamps in one state for screenshots.</summary>
        internal static void Force(string what)
        {
            if (!_bound) { Core.Log.Warning("[Lights] force: no RV lamps bound - the RV has to be away from home."); return; }
            if (what == "auto") { _forced = false; Core.Log.Msg("[Lights] lamps follow the chassis again."); return; }
            _forced = true;
            bool all = what == "all";
            Apply(all || what == "head", all || what == "brake", all || what == "reverse");
            Core.Log.Msg("[Lights] lamps held: " + what + ".");
        }
#endif

        /// <summary>Follow the chassis: headlight toggle, brake and reverse.</summary>
        internal static void Update()
        {
            if (!_bound || _chassis == null) return;
#if DEBUG
            if (_forced) return;
#endif
            bool head = _source != null && _source.HeadlightsOn;
            Apply(head, _chassis.BrakesApplied, _chassis.IsReversing);
        }

        /// <summary>All lamps off and the lenses back to their own material; the RV goes home or the scene unloads.</summary>
        internal static void Unbind()
        {
            if (_bound)
            {
                try { Apply(false, false, false); }
                catch (Exception e) { Core.Log.Warning("[Lights] switching the RV lamps off failed: " + e.Message); }
                foreach (Lens l in _lenses)
                    try { if (l.R != null) { l.R.sharedMaterials = l.Off; RvMobile.RefreshStatic(l.R); } }
                    catch (Exception e) { Core.Log.Warning("[Lights] restoring a headlight lens failed: " + e.Message); }
                Core.Log.Msg("[Lights] RV lamps released.");
            }
            _bound = false;
#if DEBUG
            _forced = false;
#endif
            _brake.Clear();
            _reverse.Clear();
            _beams.Clear();
            _lenses.Clear();
        }

        // The scene is going away with every lamp in it; only the bookkeeping is cleared.
        internal static void Reset()
        {
            _bound = false;
            Unbind();
            _chassis = null;
            _source = null;
            _lensOn = null;
        }

        private static void Apply(bool head, bool brake, bool reverse)
        {
            if (head != _head)
            {
                _head = head;
                foreach (GameObject b in _beams) if (b != null) b.SetActive(head);
                if (_lensOn != null)
                    foreach (Lens l in _lenses)
                    {
                        if (l.R == null) continue;
                        Material[] mats = (Material[])l.Off.Clone();
                        if (head && mats.Length > 0) mats[0] = _lensOn;
                        l.R.sharedMaterials = mats;
                        RvMobile.RefreshStatic(l.R);
                    }
                Core.LogDebug("[Lights] headlights " + (head ? "on" : "off") + ".");
            }
            if (brake != _brakeOn) { _brakeOn = brake; Switch(_brake, brake); Core.LogDebug("[Lights] brake lights " + (brake ? "on" : "off") + "."); }
            if (reverse != _reverseOn) { _reverseOn = reverse; Switch(_reverse, reverse); Core.LogDebug("[Lights] reverse lights " + (reverse ? "on" : "off") + "."); }
        }

        private static string Describe(Il2CppReferenceArray<MeshRenderer> meshes)
        {
            if (meshes == null) return "no meshes";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < meshes.Length; i++)
            {
                MeshRenderer m = meshes[i];
                if (m == null) continue;
                sb.Append(m.name).Append('[');
                foreach (Material mat in m.sharedMaterials) sb.Append(mat != null ? mat.name : "null").Append(',');
                sb.Append("] ");
            }
            return sb.ToString();
        }

        private static void Switch(List<ToggleableLight> lamps, bool on)
        {
            foreach (ToggleableLight tl in lamps)
            {
                if (tl == null) continue;
                tl.isOn = on;
                var meshes = tl.lightSurfacesMeshes;
                if (meshes != null)
                    for (int i = 0; i < meshes.Length; i++)
                        if (meshes[i] != null) RvMobile.RefreshStatic(meshes[i]);
            }
        }
    }
}
