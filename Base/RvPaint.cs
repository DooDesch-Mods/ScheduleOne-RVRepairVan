using System;
using System.Collections.Generic;
using Chassis.Api;
using Il2CppScheduleOne.Vehicles;
using RVRepairVan.Garage;
using RVRepairVan.Managers;
using RVRepairVan.Net;
using RVRepairVan.Persistence;

namespace RVRepairVan.Base
{
    /// <summary>
    /// The RV's outside paint. The bodyshop paints the chassis the RV rides on, and Chassis paints the slots this class
    /// names as that chassis' paint targets: the RV's own body. The chassis is gone at home, so the value lives in the
    /// RV's save and is painted on the RV's renderers directly whenever the RV is home or loads.
    ///
    /// Paint values are Chassis "paint" values (RRGGBB,finish). The stripes and accents take the second paint
    /// ("paint2"). The host stores and replicates both with <see cref="RvOp.PaintSync"/>.
    /// </summary>
    internal static class RvPaint
    {
        // The materials of the RV's painted shell, by name. Every slot of the intact model that carries one of them
        // takes the paint. Trim, glass, wheels, lamps and the interior keep their own materials.
        private static readonly HashSet<string> BodyMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "rv_Main mat",           // the shell: rv/Main, slot 0
            "rv_Door mat",           // the side door
            "rv_StorageHatch mat",   // the luggage hatches along the sides
        };

        // The second paint goes on the green stripes and accents of the shell: their own layer (RvStripes), whose
        // texture is the stripe mask, so it is painted with the texture kept.
        private static readonly HashSet<string> TrimMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            RvStripes.MaterialName,
        };

        // Painted slots all carry Chassis' copy named "Chassis_Paint", so the role of a slot is remembered from its
        // original material the first time it is seen. Key: renderer instance id and material index.
        private static readonly Dictionary<long, bool> _trimSlot = new Dictionary<long, bool>();

        private static readonly HashSet<IntPtr> _targetPtrs = new HashSet<IntPtr>();
        private static bool _hooked;
        private static bool _storing;

        internal static string Saved => RepairStateStore.GetPaint() ?? "";
        internal static string Saved2 => RepairStateStore.GetPaint2() ?? "";

        private static string KeyOf(bool trim) => trim ? "paint2" : "paint";
        private static string Stored(bool trim) => trim ? Saved2 : Saved;

        internal static void Init()
        {
            if (_hooked) return;
            _hooked = true;
            ChassisApi.TuningChanged += OnTuningChanged;
            Core.Log.Msg("[Paint] listening for paint on the RV's chassis.");
        }

        /// <summary>Every material slot of the intact RV that shows a paint; the stripes' are <see cref="PaintTarget.Secondary"/>.</summary>
        internal static List<PaintTarget> Targets()
        {
            var list = new List<PaintTarget>();
            _targetPtrs.Clear();
            Transform model = RVManager.Model;
            if (model == null) return list;
            RvStripes.Ensure(model);
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!Role(r, i, mats[i], out bool trim)) continue;
                    list.Add(new PaintTarget(r, i, trim));
                    _targetPtrs.Add(r.Pointer);
                }
            }
            return list;
        }

        private static List<PaintTarget> Of(List<PaintTarget> all, bool trim) => all.FindAll(t => t.Secondary == trim);

        // Whether a slot takes a paint, and which. A painted slot carries Chassis' copy, named "Chassis_Paint"; its role
        // was remembered when it still showed its original.
        private static bool Role(Renderer r, int index, Material m, out bool trim)
        {
            trim = false;
            if (m == null) return false;
            long key = ((long)r.GetInstanceID() << 8) ^ index;
            string n = Clean(m.name);
            if (n == "Chassis_Paint") return _trimSlot.TryGetValue(key, out trim);
            if (BodyMaterials.Contains(n)) { _trimSlot[key] = false; return true; }
            if (TrimMaterials.Contains(n)) { _trimSlot[key] = trim = true; return true; }
            return false;
        }

        private static bool _paintedOnce, _painted2Once;

        private static string Clean(string name)
        {
            if (name == null) return "";
            int cut = name.IndexOf(" (Instance)", StringComparison.Ordinal);
            return cut >= 0 ? name.Substring(0, cut) : name;
        }

        internal static bool IsTarget(Renderer r) => r != null && _targetPtrs.Contains(r.Pointer);

        // ---- the chassis -------------------------------------------------------------------------------------

        /// <summary>Every peer, when the RV goes on its chassis: the chassis' paint goes on the RV. Host: the saved paint.</summary>
        internal static void OnAttach(LandVehicle chassis)
        {
            try
            {
                var targets = Targets();
                if (targets.Count == 0) { Core.Log.Warning("[Paint] the RV shows no body material - the bodyshop paints nothing on it."); return; }
                ChassisApi.SetPaintTargets(chassis, targets);
                if (NetworkBus.Online && !NetworkBus.IsServer) return;
                foreach (bool trim in new[] { false, true })
                {
                    string saved = Stored(trim);
                    if (saved == (ChassisApi.GetTuning(chassis, KeyOf(trim)) ?? "")) continue;
                    _storing = true;
                    try { ChassisApi.SetTuning(chassis, KeyOf(trim), saved.Length == 0 ? null : saved); }
                    finally { _storing = false; }
                    Core.Log.Msg("[Paint] the RV's " + KeyOf(trim) + " '" + (saved.Length == 0 ? "stock" : saved) + "' put on its chassis.");
                }
            }
            catch (Exception e) { Core.Log.Error("[Paint] giving the chassis the RV's body failed - the bodyshop paint misses the RV: " + e); }
        }

        private static void OnTuningChanged(LandVehicle v, string key)
        {
            try
            {
                if (_storing || !RvGarage.IsRv(v)) return;
                if (key != "paint" && key != "paint2" && key != "*") return;
                if (NetworkBus.Online && !NetworkBus.IsServer) return;   // the host stores and replicates
                foreach (bool trim in new[] { false, true })
                {
                    string value = ChassisApi.GetTuning(v, KeyOf(trim)) ?? "";
                    if (value != Stored(trim)) HostSet(value, "bodyshop", trim);
                }
            }
            catch (Exception e) { Core.Log.Error("[Paint] storing the RV's paint failed - it is lost after the trip home: " + e); }
        }

        // ---- state -------------------------------------------------------------------------------------------

        /// <summary>Host: store, replicate and show a paint ("" = stock), on the shell or with <paramref name="trim"/> on the trim.</summary>
        internal static void HostSet(string value, string why, bool trim = false)
        {
            value = value ?? "";
            if (trim) RepairStateStore.SetPaint2(value); else RepairStateStore.SetPaint(value);
            if (NetworkBus.Online && NetworkBus.IsServer) Broadcast();
            Core.Log.Msg("[Paint] RV " + (trim ? "trim" : "paint") + " " + (value.Length == 0 ? "back to stock" : value) + " (" + why + ").");
            // On the chassis Chassis repaints its targets from its own state, so that state follows.
            LandVehicle chassis = RvMobile.Chassis;
            if (chassis != null && (ChassisApi.GetTuning(chassis, KeyOf(trim)) ?? "") != value)
            {
                _storing = true;
                try { ChassisApi.SetTuning(chassis, KeyOf(trim), value.Length == 0 ? null : value); }
                finally { _storing = false; }
            }
            Show();
        }

        /// <summary>"ff0000,Metallic" -> "FF0000,metallic", the way Chassis writes a paint value.</summary>
        internal static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Equals("stock", StringComparison.OrdinalIgnoreCase)) return "";
            string[] p = value.Split(',');
            return p[0].ToUpperInvariant() + "," + (p.Length > 1 ? p[1].ToLowerInvariant() : TuningFinish.Classic);
        }

        internal static void Broadcast()
        {
            foreach (bool trim in new[] { false, true })
            {
                Encode(Stored(trim), out int rgb, out int finish);
                NetworkBus.BroadcastToAll(RvOp.PaintSync, rgb, finish, trim ? 1 : 0);
            }
        }

        /// <summary>Client: the host's paint of the shell or the trim arrived.</summary>
        internal static void ApplySync(int rgb, int finish, bool trim)
        {
            if (NetworkBus.IsServer) return;
            string value = Decode(rgb, finish);
            if (trim) RepairStateStore.SetPaint2(value); else RepairStateStore.SetPaint(value);
            Core.LogDebug("[Paint] client got PaintSync " + (trim ? "trim " : "") + (value.Length == 0 ? "stock" : value));
            Show();
        }

        /// <summary>
        /// Paint the RV's renderers with the stored value. On the chassis Chassis keeps the same slots painted; at
        /// home nobody else does.
        /// </summary>
        internal static void Show()
        {
            try
            {
                if (RVManager.Model == null) { Core.LogDebug("[Paint] no RV model yet - the paint waits for the next load."); return; }
                if (!RepairStateStore.GetRepaired()) { Core.LogDebug("[Paint] the RV is a wreck - no paint shown."); return; }
                var all = Targets();
                if (all.Count == 0) { Core.Log.Warning("[Paint] the RV shows no body material - paint '" + Saved + "' not shown."); return; }
                ShowLayer(Of(all, false), Saved, ref _paintedOnce, "paint");
                ShowLayer(Of(all, true), Saved2, ref _painted2Once, "trim");
                foreach (var t in all) RvMobile.RefreshStatic(t.Renderer);
            }
            catch (Exception e) { Core.Log.Error("[Paint] painting the RV failed: " + e); }
        }

        private static void ShowLayer(List<PaintTarget> targets, string value, ref bool paintedOnce, string what)
        {
            if (value.Length == 0 && !paintedOnce) return;   // stock and never painted: nothing to put back
            if (targets.Count == 0) { Core.Log.Warning("[Paint] the RV shows no " + what + " material - '" + value + "' not shown."); return; }
            if (!ChassisApi.PaintRenderers(targets, value)) Core.Log.Warning("[Paint] the " + what + " '" + value + "' did not go on the RV.");
            paintedOnce = value.Length > 0 || paintedOnce;
        }

        // ---- wire format: A = 0xRRGGBB (-1 = stock), B = the finish index ------------------------------------

        private static readonly string[] Finishes = { TuningFinish.Classic, TuningFinish.Metallic, TuningFinish.Matte, TuningFinish.Metal, TuningFinish.Chrome };

        private static void Encode(string value, out int rgb, out int finish)
        {
            rgb = -1; finish = 0;
            if (string.IsNullOrEmpty(value)) return;
            string[] p = value.Split(',');
            if (p[0].Length != 6 || !int.TryParse(p[0], System.Globalization.NumberStyles.HexNumber, null, out rgb)) { rgb = -1; Core.Log.Warning("[Paint] '" + value + "' is no paint - sent as stock."); return; }
            if (p.Length > 1) finish = Math.Max(0, Array.FindIndex(Finishes, f => string.Equals(f, p[1], StringComparison.OrdinalIgnoreCase)));
        }

        private static string Decode(int rgb, int finish)
        {
            if (rgb < 0) return "";
            string f = finish >= 0 && finish < Finishes.Length ? Finishes[finish] : Finishes[0];
            return rgb.ToString("X6") + "," + f;
        }

        // ---- debug --------------------------------------------------------------------------------------------

        /// <summary>Every renderer of the intact RV with its materials, to name the body materials.</summary>
        internal static void DebugDump()
        {
            Transform model = RVManager.Model;
            if (model == null) { Core.Log.Warning("[Paint] dump: no RV model."); return; }
            var count = new Dictionary<string, int>();
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                var names = new List<string>();
                foreach (var m in mats)
                {
                    string n = m == null ? "-" : Clean(m.name);
                    names.Add(n);
                    count[n] = (count.TryGetValue(n, out int c) ? c : 0) + 1;
                }
                Bounds b = r.bounds;
                Core.Log.Msg("[Paint] " + Path(model, r.transform) + " static=" + r.isPartOfStaticBatch + " on=" + (r.enabled && r.gameObject.activeInHierarchy)
                    + " size=" + b.size.ToString("F1") + " mats=[" + string.Join(", ", names) + "]");
            }
            foreach (var kv in count) Core.Log.Msg("[Paint] material " + kv.Key + " x" + kv.Value + (BodyMaterials.Contains(kv.Key) ? " (body)" : TrimMaterials.Contains(kv.Key) ? " (trim)" : ""));
            var all = Targets();
            Core.Log.Msg("[Paint] saved '" + Saved + "', trim '" + Saved2 + "', " + Of(all, false).Count + " body and " + Of(all, true).Count + " trim slot(s).");
        }

        private static string Path(Transform root, Transform t)
        {
            string p = t.name;
            while (t.parent != null && t.parent != root) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
