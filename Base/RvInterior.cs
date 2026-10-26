using System;
using System.Collections.Generic;
using DooDesch.Localization;
using RVRepairVan.Config;
using RVRepairVan.Managers;
using RVRepairVan.Net;
using RVRepairVan.Persistence;

namespace RVRepairVan.Base
{
    /// <summary>The RV's fixed furniture, one bit per part the bodyshop sells.</summary>
    [Flags]
    internal enum RvPart
    {
        None = 0,
        Bed = 1,
        Kitchen = 2,          // the bench with the sink
        UpperCabinets = 4,
        LowerCabinets = 8,
        BedroomWall = 16,
        CabWall = 32,         // the partition behind the driver
        BedsideTable = 64,
        Decor = 128,          // the loose props: tarp, radio, vase, plant, the leftovers of somebody's lunch
    }

    /// <summary>
    /// The RV's furniture, part by part. Each part is unlocked once (paid at Marco's bodyshop); after that it can be
    /// taken out and put back for free. Taking a part out frees its floor for the workshop grid
    /// (<see cref="RvBuildGrid"/>); putting it back needs that floor empty.
    ///
    /// The object list is exact, taken from a live measurement (see
    /// Workspace/docs/RVRepairVan/VANILLA-RV-PROPERTY.md): the children <c>Bed</c>, <c>Bench</c> and <c>Cabinets</c>
    /// of <c>rv/Main/Interior</c>, the cabinets and the two partitions directly under <c>rv/Main</c>, and loose props
    /// on the model root. A curated list on purpose - name heuristics over the whole hierarchy are how the competing
    /// mod ends up hiding things it meant to keep. Which cabinet is upper and which partition is behind the driver is
    /// read from the model: a cabinet whose bottom is above <see cref="UpperCabinetFloorGap"/> hangs on the wall, and
    /// the partition nearer the dashboard is the cab wall.
    ///
    /// The original active state is snapshotted before anything is switched off, so putting a part back cannot turn
    /// on an object the game had deliberately disabled.
    ///
    /// State: the host owns both masks (<see cref="RepairStateStore"/>), sets them through <see cref="HostSet"/>, and
    /// replicates them with <see cref="RvOp.InteriorSync"/>.
    /// </summary>
    internal static class RvInterior
    {
        internal const int AllParts = 255;

        // The order the bodyshop lists them in.
        internal static readonly RvPart[] Parts =
        {
            RvPart.Bed, RvPart.Kitchen, RvPart.UpperCabinets, RvPart.LowerCabinets,
            RvPart.BedroomWall, RvPart.CabWall, RvPart.BedsideTable, RvPart.Decor,
        };

        // Under RV/rv/Main/Interior. "Dash", "Chair" and "Chair (1)" are the driver's cab and stay: with them hidden
        // the front of the RV is a hole the player can see straight through to the outside.
        private const string InteriorPath = "rv/Main/Interior";
        private const string MainPath = "rv/Main";

        private static readonly string[] Cabinets =
        {
            "Cabinet.001", "Cabinet.001 (1)", "Cabinet.002", "Cabinet.002 (1)",
            "Cabinet.003", "Cabinet.003 (1)", "Cabinet.004", "Cabinet.004 (1)",
        };
        private static readonly string[] Walls = { "Wall.001", "Wall.001 (1)" };

        // Loose props on the model root (RV/RV). NOT touched: 'rv' (the vehicle itself), 'Container' (where placed
        // buildables live), 'Pot location', the triggers, the audio zone and the occlusion area.
        private static readonly string[] DecorProps =
        {
            "SM_Prop_Tarp_Generic_01", "SM_Item_Radio_01", "Vase", "Ashtray",
            "SM_Prop_Plant_02", "Can_Cuke", "Cuke_Crushed_Junk", "Water Bottle",
        };

        // A cabinet whose lowest point is this far above the floor hangs on the wall.
        private const float UpperCabinetFloorGap = 0.6f;

        private sealed class Piece
        {
            internal Transform T;
            internal bool WasActive;
        }

        private static readonly Dictionary<RvPart, List<Piece>> _pieces = new Dictionary<RvPart, List<Piece>>();
        // Each part's box in the model's local frame, measured while the part was standing.
        private static readonly Dictionary<RvPart, Bounds> _boxes = new Dictionary<RvPart, Bounds>();
        private static IntPtr _resolvedFor;
        private static int _shownRemoved = -1;
        // Parts the bodyshop shows the other way round while the player looks at them. Only the look; the masks and
        // the grid stay as they are.
        private static int _previewFlip;

        /// <summary>
        /// Per-scene reset. The pieces stay cached while their model lives: a part that is out is inactive, and
        /// resolving it again would remember it as switched off by the game and never show it again.
        /// </summary>
        internal static void Reset()
        {
            _shownRemoved = -1;
            _previewFlip = 0;
            if (!Alive()) { _pieces.Clear(); _boxes.Clear(); _resolvedFor = IntPtr.Zero; }
        }

        private static bool Alive()
        {
            if (_pieces.Count == 0) return false;
            foreach (var kv in _pieces)
                foreach (var piece in kv.Value)
                    if (piece.T == null) return false;
            return true;
        }

        // ---- state ----------------------------------------------------------------------------------------------

        internal static int Unlocked => RepairStateStore.GetInteriorUnlocked();
        internal static int Removed => RepairStateStore.GetInteriorRemoved();
        internal static bool IsUnlocked(RvPart p) => (Unlocked & (int)p) != 0;
        internal static bool IsRemoved(RvPart p) => (Removed & (int)p) != 0;
        internal static bool AnyRemoved => (Removed & AllParts) != 0;

        internal static bool IsValid(RvPart p)
        {
            foreach (var q in Parts) if (q == p) return true;
            return false;
        }

        internal static int Price(RvPart p) => RVRepairVanPreferences.PartPrice(p);

        internal static string Label(RvPart p)
        {
            switch (p)
            {
                case RvPart.Bed: return L10n.T("Bed");
                case RvPart.Kitchen: return L10n.T("Kitchen Bench");
                case RvPart.UpperCabinets: return L10n.T("Upper Cabinets");
                case RvPart.LowerCabinets: return L10n.T("Lower Cabinets");
                case RvPart.BedroomWall: return L10n.T("Bedroom Wall");
                case RvPart.CabWall: return L10n.T("Wall Behind Driver");
                case RvPart.BedsideTable: return L10n.T("Bedside Table");
                case RvPart.Decor: return L10n.T("Decor");
                default: return p.ToString();
            }
        }

        /// <summary>
        /// Host: write both masks and replicate them. The bodyshop has already checked the price and the floor;
        /// this only refuses what makes no sense.
        /// </summary>
        internal static void HostSet(int unlocked, int removed, string why)
        {
            unlocked &= AllParts;
            removed &= AllParts & unlocked;   // a part that was never unlocked stays in
            RepairStateStore.SetInteriorUnlocked(unlocked);
            RepairStateStore.SetInteriorRemoved(removed);
            if (NetworkBus.Online && NetworkBus.IsServer) NetworkBus.BroadcastToAll(RvOp.InteriorSync, unlocked, removed);
            Core.Log.Msg("[Interior] " + why + " - unlocked " + (RvPart)unlocked + ", out " + (RvPart)removed + ".");
            Sync();
            RvBuildGrid.PartsChanged();
        }

        /// <summary>Client: adopt the host's masks.</summary>
        internal static void ApplySync(int unlocked, int removed)
        {
            if (NetworkBus.IsServer) return;
            RepairStateStore.SetInteriorUnlocked(unlocked & AllParts);
            RepairStateStore.SetInteriorRemoved(removed & AllParts);
            Core.LogDebug("[Interior] client adopted unlocked=" + unlocked + " out=" + removed);
            Sync();
            RvBuildGrid.PartsChanged();
        }

        // ---- the model -------------------------------------------------------------------------------------------

        /// <summary>Show every part that is in and hide every part that is out. Idempotent; retries until the model exists.</summary>
        internal static void Sync()
        {
            try
            {
                if (!Resolve()) return;
                int removed = Removed ^ _previewFlip;
                foreach (var kv in _pieces)
                {
                    bool show = (removed & (int)kv.Key) == 0;
                    foreach (var piece in kv.Value)
                    {
                        if (piece.T == null) continue;
                        bool want = show && piece.WasActive;
                        if (piece.T.gameObject.activeSelf != want) piece.T.gameObject.SetActive(want);
                    }
                }
                if (removed != _shownRemoved)
                {
                    _shownRemoved = removed;
                    Core.Log.Msg("[Interior] in place: " + (RvPart)(AllParts & ~removed) + "; out: " + (RvPart)removed + ".");
                }
                RvEasterEggs.Ensure();
            }
            catch (Exception e) { Core.Log.Error("[Interior] showing the parts failed: " + e); }
        }

        /// <summary>Show a part the other way round (in when it is out, out when it is in) until <see cref="EndPreview"/>.</summary>
        internal static void Preview(RvPart p)
        {
            _previewFlip = (int)p;
            Sync();
        }

        internal static void EndPreview()
        {
            if (_previewFlip == 0) return;
            _previewFlip = 0;
            Sync();
        }

        /// <summary>
        /// The part's box in the model's local frame, measured while it stood. False before the model was resolved
        /// or for a part without renderers.
        /// </summary>
        internal static bool Box(RvPart p, out Bounds box)
        {
            Resolve();
            return _boxes.TryGetValue(p, out box);
        }

        /// <summary>Is a world point inside a part that is fitted? Used for props that would stand in furniture.</summary>
        internal static bool InFittedPart(Vector3 world, float margin)
        {
            Transform model = RVManager.Model;
            if (model == null || !Resolve()) return false;
            Vector3 local = model.InverseTransformPoint(world);
            int removed = Removed;
            foreach (var kv in _boxes)
            {
                if ((removed & (int)kv.Key) != 0) continue;
                Bounds b = kv.Value;
                b.Expand(margin * 2f);
                if (b.Contains(local)) return true;
            }
            return false;
        }

        /// <summary>
        /// Find the pieces of every part on the current model, and measure each part while all of it still stands.
        /// The furniture hangs off the INTACT model ("RV"), not the wreck - so this only resolves once repaired.
        /// </summary>
        private static bool Resolve()
        {
            Transform model = RVManager.Model;
            if (model == null) { Core.LogDebug("[Interior] RV model not found yet - will retry on the next apply."); return false; }
            if (_resolvedFor == model.Pointer && Alive()) return true;
            if (!model.gameObject.activeInHierarchy) { Core.LogDebug("[Interior] the intact RV model is not shown yet - will retry."); return false; }
            _pieces.Clear();
            _boxes.Clear();
            _shownRemoved = -1;

            Transform interior = model.Find(InteriorPath);
            Transform main = model.Find(MainPath);
            if (interior == null) Core.Log.Warning("[Interior] '" + InteriorPath + "' not found under the RV model.");
            if (main == null) Core.Log.Warning("[Interior] '" + MainPath + "' not found under the RV model.");

            Add(RvPart.Bed, interior?.Find("Bed"));
            Add(RvPart.Kitchen, interior?.Find("Bench"));

            // The cabinets: one group under Interior, eight pieces under Main. Up or down is read from the model.
            float floor = FloorY(model);
            var cabinets = new List<Transform> { interior?.Find("Cabinets") };
            foreach (string n in Cabinets) cabinets.Add(main?.Find(n));
            foreach (var c in cabinets)
            {
                if (c == null) continue;
                bool upper = LocalBox(model, c, out Bounds b) && b.min.y - floor > UpperCabinetFloorGap;
                Add(upper ? RvPart.UpperCabinets : RvPart.LowerCabinets, c);
            }

            // The partitions: the one nearer the dashboard stands behind the driver.
            Transform dash = interior?.Find("Dash");
            Transform w0 = main?.Find(Walls[0]), w1 = main?.Find(Walls[1]);
            if (w0 != null && w1 != null && dash != null)
            {
                Vector3 d = model.InverseTransformPoint(dash.position);
                float d0 = (model.InverseTransformPoint(w0.position) - d).sqrMagnitude;
                float d1 = (model.InverseTransformPoint(w1.position) - d).sqrMagnitude;
                Add(RvPart.CabWall, d0 <= d1 ? w0 : w1);
                Add(RvPart.BedroomWall, d0 <= d1 ? w1 : w0);
            }
            else
            {
                Core.Log.Warning("[Interior] partitions or dashboard missing - the walls are assigned by name.");
                Add(RvPart.CabWall, w0);
                Add(RvPart.BedroomWall, w1);
            }

            Add(RvPart.BedsideTable, model.Find("Bedside_Table"));
            foreach (string n in DecorProps) Add(RvPart.Decor, model.Find(n));

            foreach (var kv in _pieces)
            {
                bool any = false;
                Bounds all = default;
                foreach (var piece in kv.Value)
                    if (LocalBox(model, piece.T, out Bounds b)) { if (!any) { all = b; any = true; } else all.Encapsulate(b); }
                if (any) _boxes[kv.Key] = all;
                Core.Log.Msg("[Interior] " + kv.Key + ": " + kv.Value.Count + " piece(s)"
                    + (any ? ", box " + all.min.ToString("F2") + " .. " + all.max.ToString("F2") : ", no box") + ".");
            }
            foreach (var p in Parts)
                if (!_pieces.ContainsKey(p)) Core.Log.Warning("[Interior] no piece of " + p + " found on the model.");

            _resolvedFor = model.Pointer;
            return _pieces.Count > 0;
        }

        private static void Add(RvPart part, Transform t)
        {
            if (t == null) return;
            if (!_pieces.TryGetValue(part, out var list)) _pieces[part] = list = new List<Piece>();
            // The active state as the game left it - a part is resolved before it is ever hidden, and an older
            // version's "gut" never runs before this.
            list.Add(new Piece { T = t, WasActive = t.gameObject.activeSelf });
        }

        /// <summary>The bounds of a piece's renderers in the model's local frame. Measured only while it is shown.</summary>
        private static bool LocalBox(Transform model, Transform t, out Bounds box)
        {
            box = default;
            if (t == null || !t.gameObject.activeInHierarchy) return false;
            bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.enabled) continue;
                Bounds w = r.bounds;
                Vector3 c = w.center, e = w.extents;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(c.x + ((i & 1) == 0 ? -e.x : e.x), c.y + ((i & 2) == 0 ? -e.y : e.y), c.z + ((i & 4) == 0 ? -e.z : e.z));
                    Vector3 p = model.InverseTransformPoint(corner);
                    if (!any) { box = new Bounds(p, Vector3.zero); any = true; } else box.Encapsulate(p);
                }
            }
            return any;
        }

        /// <summary>The cabin floor's height in the model frame: the lowest vanilla grid tile, else the model origin.</summary>
        internal static float FloorY(Transform model)
        {
            float y = float.MaxValue;
            try
            {
                var prop = RVManager.Root != null ? RVManager.Root.GetComponent<Il2CppScheduleOne.Property.Property>() : null;
                var grids = prop != null ? prop.Grids : null;
                for (int i = 0; grids != null && i < grids.Count; i++)
                {
                    var g = grids[i];
                    if (g == null || g.Tiles == null) continue;
                    for (int k = 0; k < g.Tiles.Count; k++)
                        if (g.Tiles[k] != null) y = Mathf.Min(y, model.InverseTransformPoint(g.Tiles[k].transform.position).y);
                }
            }
            catch (Exception e) { Core.Log.Warning("[Interior] reading the floor height failed - the model origin is used: " + e.Message); }
            return y == float.MaxValue ? 0f : y;
        }

        // ---- old saves ---------------------------------------------------------------------------------------------

        /// <summary>
        /// A save from 3.x bought "gut the interior" as one upgrade. It becomes every part unlocked and every part out,
        /// so the RV looks as it did.
        /// </summary>
        internal static void MigrateGut(RepairSave.State s)
        {
            const int gut = 1;
            if ((s.Upgrades & gut) == 0) return;
            s.Upgrades &= ~gut;
            s.InteriorUnlocked = AllParts;
            s.InteriorRemoved = AllParts;
            Core.Log.Msg("[Interior] this save gutted the RV with an older version - every part is unlocked and out.");
        }

#if DEBUG
        internal static void DebugDump()
        {
            Resolve();
            Core.Log.Msg("[Debug] interior unlocked=" + (RvPart)Unlocked + " out=" + (RvPart)Removed);
            foreach (var p in Parts)
            {
                int n = _pieces.TryGetValue(p, out var list) ? list.Count : 0;
                string box = _boxes.TryGetValue(p, out Bounds b) ? b.min.ToString("F2") + " .. " + b.max.ToString("F2") : "none";
                Core.Log.Msg("[Debug]   " + p + " pieces=" + n + " box=" + box + " price=" + Price(p) + " blocked=" + (RvBuildGrid.RefitBlocked(p) ?? "no"));
            }
        }
#endif
    }
}
