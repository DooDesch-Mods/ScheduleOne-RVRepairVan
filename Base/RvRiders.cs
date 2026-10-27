using System;
using System.Collections.Generic;
using Il2CppScheduleOne.Vehicles;
using RVRepairVan.Net;

namespace RVRepairVan.Base
{
    /// <summary>
    /// Other players standing in the moving RV, drawn where they stand in it.
    ///
    /// The network places a player by their world position, which their own client measured against its view of the
    /// chassis. That view trails the driver's, and the position arrives later again, so on every other screen a rider
    /// stood behind or ahead of their place by the RV's speed times the delay: they swayed back and forth as it sped up
    /// and slowed down. Each rider sends where they stand in the RV instead (<see cref="RvOp.RiderPose"/>, eight times a
    /// second while on board), and every other peer draws them there against its own chassis, after the network moved
    /// them, so the body and the RV move as one.
    /// </summary>
    internal static class RvRiders
    {
        private const float SendEvery = 0.125f;
        private const float StaleAfter = 0.5f;   // no pose for this long: the network places the player again
        private const float Follow = 15f;        // how fast the drawn pose eases to a new one, per second

        // Packing: millimetres with an offset, so each fits its bits. x and z +-16.3 m in 15 bits each, y +-4 m in 13
        // bits next to the yaw in whole degrees.
        private const int XzOffset = 16384, XzBits = 15, YOffset = 4096, YawBits = 9;
        private const int Clear = -1;

        private sealed class Rider
        {
            internal int Id;
            internal string Name = "?";
            internal Vector3 Target, Drawn;
            internal float TargetYaw, DrawnYaw;
            internal float At;
            internal bool Have, Drawing;
        }

        private static readonly Dictionary<int, Rider> _riders = new Dictionary<int, Rider>();
        private static float _nextSend;
        private static bool _sending;
        private static int _failures;

        internal static void Reset()
        {
            _riders.Clear();
            _sending = false;
            _nextSend = 0f;
        }

        // ---- the rider: send where they stand ------------------------------------------------------------------------

        /// <summary>After the local player was carried: on board, send their place in the RV now and then.</summary>
        internal static void Send(bool onBoard, Transform chassis)
        {
            if (!NetworkBus.Online) return;
            Player local = Player.Local;
            int id = local != null && local.NetworkObject != null ? local.NetworkObject.ObjectId : -1;
            if (!onBoard || chassis == null || id < 0)
            {
                if (_sending)
                {
                    _sending = false;
                    Post(id, Clear, 0);
                    Core.Log.Msg("[Riders] the player left the RV - the others place them by the network again.");
                }
                return;
            }
            if (Time.time < _nextSend) return;
            _nextSend = Time.time + SendEvery;
            Transform pt = local.transform;
            Vector3 p = chassis.InverseTransformPoint(pt.position);
            float yaw = Mathf.Repeat(Mathf.DeltaAngle(0f, (Quaternion.Inverse(chassis.rotation) * pt.rotation).eulerAngles.y), 360f);
            int x = Mathf.Clamp(Mathf.RoundToInt(p.x * 1000f) + XzOffset, 0, (1 << XzBits) - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt(p.z * 1000f) + XzOffset, 0, (1 << XzBits) - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(p.y * 1000f) + YOffset, 0, (1 << 13) - 1);
            Post(id, (x << XzBits) | z, (y << YawBits) | (Mathf.RoundToInt(yaw) % 360));
            if (!_sending)
            {
                _sending = true;
                Core.Log.Msg("[Riders] the player stands in the RV - sending their place in it to the others.");
            }
        }

        private static void Post(int id, int b, int c)
        {
            if (id < 0) return;
            if (NetworkBus.IsServer) NetworkBus.BroadcastToAll(RvOp.RiderPose, id, b, c);
            else NetworkBus.SendToHost(RvOp.RiderPose, id, b, c);
        }

        // ---- every other peer: draw them there ----------------------------------------------------------------------

        /// <summary>A rider's place in the RV arrived.</summary>
        internal static void OnPose(RvMsg m)
        {
            Player local = Player.Local;
            if (local != null && local.NetworkObject != null && local.NetworkObject.ObjectId == m.A) return;
            if (!_riders.TryGetValue(m.A, out Rider r)) { r = new Rider { Id = m.A }; _riders[m.A] = r; }
            if (m.B == Clear)
            {
                r.Have = false;
                Stop(r, "they left the RV");
                return;
            }
            r.Target = new Vector3(((m.B >> XzBits) - XzOffset) / 1000f, ((m.C >> YawBits) - YOffset) / 1000f, ((m.B & ((1 << XzBits) - 1)) - XzOffset) / 1000f);
            r.TargetYaw = m.C & ((1 << YawBits) - 1);
            if (!r.Have) { r.Drawn = r.Target; r.DrawnYaw = r.TargetYaw; }
            r.Have = true;
            r.At = Time.time;
        }

        /// <summary>Late in the frame, after the network placed the players: put each rider at their place in the RV.</summary>
        internal static void Draw()
        {
            if (_riders.Count == 0) return;
#if DEBUG
            if (DebugOff) return;
#endif
            LandVehicle chassis = RvMobile.Chassis;
            var players = Player.PlayerList;
            try
            {
                foreach (Rider r in _riders.Values)
                {
                    if (!r.Have) continue;
                    if (chassis == null) { r.Have = false; Stop(r, "the RV is home"); continue; }
                    if (Time.time - r.At > StaleAfter) { r.Have = false; Stop(r, "no place from them for " + StaleAfter.ToString("0.0") + " s"); continue; }
                    Player p = null;
                    if (players != null)
                        foreach (Player q in players)
                            if (q != null && q.NetworkObject != null && q.NetworkObject.ObjectId == r.Id) { p = q; break; }
                    if (p == null) continue;
                    r.Name = p.PlayerName;
                    float k = 1f - Mathf.Exp(-Follow * Time.deltaTime);
                    r.Drawn = Vector3.Lerp(r.Drawn, r.Target, k);
                    r.DrawnYaw = Mathf.LerpAngle(r.DrawnYaw, r.TargetYaw, k);
                    Transform ct = chassis.transform;
                    p.transform.SetPositionAndRotation(ct.TransformPoint(r.Drawn), ct.rotation * Quaternion.Euler(0f, r.DrawnYaw, 0f));
                    if (!r.Drawing)
                    {
                        r.Drawing = true;
                        Core.Log.Msg("[Riders] drawing " + r.Name + " at their place in the RV (" + r.Drawn.ToString("F2") + ").");
                    }
                }
                _failures = 0;
            }
            catch (Exception e)
            {
                if (_failures++ == 0) Core.Log.Error("[Riders] drawing the other riders in the RV failed - they may sway while it moves: " + e);
            }
        }

        private static void Stop(Rider r, string why)
        {
            if (!r.Drawing) return;
            r.Drawing = false;
            Core.Log.Msg("[Riders] " + r.Name + " is placed by the network again: " + why + ".");
        }

#if DEBUG
        /// <summary>rvdrive ride oldothers: other riders are placed by the network alone, as before.</summary>
        internal static bool DebugOff;
#endif
    }
}
