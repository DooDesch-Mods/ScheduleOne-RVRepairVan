using System;

namespace RVRepairVan.Net
{
    /// <summary>
    /// Opcodes for the RVRepairVan network bus. Intents flow client -> host; state flows host -> all clients.
    /// They travel as a private guid string on the game's own quest RPC channel (see NetworkBus), so no custom
    /// FishNet serializer is needed - the payload is a plain string the game already knows how to replicate.
    /// </summary>
    internal enum RvOp
    {
        // client -> host intents (the host validates + applies the shared effect)
        AcceptErrand = 0,
        DeliverCrate = 1,
        MingPayLoss = 2,
        MarcoGreet = 3,
        MarcoReferral = 4,
        MarcoFavour = 5,
        GotPackage = 6,
        MarcoPayLoss = 7,
        GiveSample = 8,   // A = discount the client computed from its consumed sample
        PayRepair = 9,
        RequestSnapshot = 10,
        AskDonna = 11,
        CheckedRv = 12,   // client reached the post-repair "check on the RV" spot
        ErrandItemPicked = 13,   // client now HOLDS the current errand item (Ming's crate / Marco's package)
        BuyUpgrade = 14,   // A = RvUpgrade bit the client wants to buy (host validates prerequisites)
        DriveRequest = 15,   // client used the RV's wheel while the RV is home - the host spawns the chassis
        BatterySwap = 16,    // client used up a battery at the wheel - the RV's flat battery is fixed
        MobileReport = 17,   // A = chassis the RV rides on at the client; -1 = attach failed, -2 = not spawned there yet (B = the named chassis)
        ArrestHome = 18,     // client was arrested after driving the RV - A = chassis it drove; the host sends the RV home instead of the impound
        HatchRequest = 19,   // client wants the roof hatch open (A = 1) or shut (A = 0)
        RiderPose = 20,      // a rider's place in the moving RV: A = their player ObjectId, B/C = packed pose, B = -1 left (RvRiders); relayed to everyone
        // RequestSnapshot carries A = the client's mod version (NetworkBus.VersionCode), 0 from builds before it existed.
        // Paid intents (PayRepair, MingPayLoss, MarcoPayLoss, BuyUpgrade) also carry B = ticket, C = amount paid.

        // host -> all clients state (absolute values, idempotent on re-receive)
        StageSync = 100,      // A = stage, B = discount total
        RepairApplied = 101,
        TransientSync = 102,  // A = flags (1=pickupActive, 2=hasPackage); B = drop world X, C = drop world Z (0,0 = none)
        UpgradeSync = 103,    // A = the full owned-upgrade bitmask (absolute, so a re-send just re-applies)
        PaymentSettled = 104, // A = ticket, B = 1 refund / 0 keep - only the client holding the ticket acts
        MobileSync = 105,     // A = ObjectId of the chassis the RV rides on, -1 = the RV is home
        BatterySync = 106,    // A = 1 the RV's battery is flat, 0 it starts
        HatchSync = 107,      // A = 1 the roof hatch is open, 0 it is shut
        InteriorSync = 108,   // A = unlocked RvPart mask, B = the parts taken out (absolute)
        PaintSync = 109,      // A = the RV's paint 0xRRGGBB, -1 = stock; B = finish index; C = 0 body, 1 trim (RvPaint)
        Ping = 200,   // connectivity / latency ping
#if DEBUG
        DebugGiveItems = 201,   // DEBUG: host -> clients, spawn packaged test products into each client's inventory
#endif
    }

    /// <summary>A decoded bus message. Carried as the guid string "RVRV:&lt;op&gt;:&lt;a&gt;:&lt;b&gt;:&lt;c&gt;".</summary>
    internal struct RvMsg
    {
        internal const string Prefix = "RVRV:";

        internal RvOp Op;
        internal int A;   // primary arg (stage, discount, flags, ping value...)
        internal int B;   // secondary arg (discount, drop X...)
        internal int C;   // tertiary arg (drop Z...)

        internal static string Encode(RvOp op, int a = 0, int b = 0, int c = 0) => Prefix + (int)op + ":" + a + ":" + b + ":" + c;

        internal static bool TryDecode(string guid, out RvMsg msg)
        {
            msg = default;
            if (string.IsNullOrEmpty(guid) || !guid.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            try
            {
                string[] p = guid.Substring(Prefix.Length).Split(':');
                if (p.Length < 1 || !int.TryParse(p[0], out int op)) return false;
                msg.Op = (RvOp)op;
                if (p.Length > 1) int.TryParse(p[1], out msg.A);
                if (p.Length > 2) int.TryParse(p[2], out msg.B);
                if (p.Length > 3) int.TryParse(p[3], out msg.C);
                return true;
            }
            catch (Exception e) { Core.Swallowed("RvMsg.TryDecode", e); return false; }
        }

        public override string ToString() => Op + "(" + A + "," + B + "," + C + ")";
    }
}
