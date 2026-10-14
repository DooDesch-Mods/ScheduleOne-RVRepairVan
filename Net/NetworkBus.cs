using System;
using HarmonyLib;
using RVRepairVan.Net.Patches;
#if IL2CPP
using Il2CppFishNet;
using Il2CppFishNet.Managing;
using GameQuestManager = Il2CppScheduleOne.Quests.QuestManager;
using EQuestState = Il2CppScheduleOne.Quests.EQuestState;
#else
using FishNet;
using FishNet.Managing;
using GameQuestManager = ScheduleOne.Quests.QuestManager;
using EQuestState = ScheduleOne.Quests.EQuestState;
#endif

namespace RVRepairVan.Net
{
    /// <summary>
    /// Host-authoritative network bus for the questline. Intents (client -> host) ride the game's SendQuestState
    /// ServerRpc; state (host -> all clients) rides its ReceiveQuestState ObserversRpc - both carrying a private
    /// "RVRV:" guid that the vanilla handler ignores and our Harmony patches intercept (QuestManagerNetPatches).
    /// No custom FishNet serializer is involved. When not in a networked session (Online == false), the mod runs
    /// fully local exactly as in single-player - callers must take their offline branch in that case.
    /// </summary>
    internal static class NetworkBus
    {
        // Set by Questline: how received messages are applied.
        internal static Action<RvMsg> OnServerIntent;   // host: a client intent arrived
        internal static Action<RvMsg> OnClientState;    // any client: host state arrived

        private static NetworkManager Nm
        {
            get { try { return InstanceFinder.NetworkManager; } catch (Exception e) { Core.Swallowed("NetworkBus.Nm", e); return null; } }
        }

        /// <summary>True while in a real networked session (host or client). False = offline single-player.</summary>
        internal static bool Online
        {
            get { var nm = Nm; try { return nm != null && (nm.IsServer || nm.IsClient); } catch (Exception e) { Core.Swallowed("NetworkBus.Online", e); return false; } }
        }

        /// <summary>True on the host/server (authoritative for the shared questline state).</summary>
        internal static bool IsServer
        {
            get { var nm = Nm; try { return nm != null && nm.IsServer; } catch (Exception e) { Core.Swallowed("NetworkBus.IsServer", e); return false; } }
        }

        private static GameQuestManager Qm
        {
            get { try { return GameQuestManager.Instance; } catch (Exception e) { Core.Swallowed("NetworkBus.Qm", e); return null; } }
        }

        /// <summary>Client -> host intent (rides SendQuestState, a ServerRpc). Harmless to call on the host too.</summary>
        internal static void SendToHost(RvOp op, int a = 0, int b = 0, int c = 0)
        {
            try
            {
                var qm = Qm;
                if (qm == null) { Core.Log.Warning("[Net] no QuestManager - intent " + op + " dropped."); return; }
                qm.SendQuestState(RvMsg.Encode(op, a, b, c), EQuestState.Inactive);
                Core.LogDebug("[Net] -> host " + op + "(" + a + "," + b + "," + c + ")");
            }
            catch (Exception e) { Core.Log.Warning("[Net] SendToHost failed: " + e.Message); }
        }

        /// <summary>Host -> all clients state (rides ReceiveQuestState, an ObserversRpc). Server only.</summary>
        internal static void BroadcastToAll(RvOp op, int a = 0, int b = 0, int c = 0)
        {
            try
            {
                var qm = Qm;
                if (qm == null) return;
                qm.ReceiveQuestState(null, RvMsg.Encode(op, a, b, c), EQuestState.Inactive);
                Core.LogDebug("[Net] broadcast " + op + "(" + a + "," + b + "," + c + ")");
            }
            catch (Exception e) { Core.Log.Warning("[Net] BroadcastToAll failed: " + e.Message); }
        }

        // Called by the Harmony patches when our private "RVRV:" guid is seen on the wire.
        internal static void DispatchServerIntent(RvMsg m)
        {
            Core.LogDebug("[Net] host <- " + m);
            try { OnServerIntent?.Invoke(m); } catch (Exception e) { Core.Log.Warning("[Net] server dispatch failed: " + e.Message); }
        }

        internal static void DispatchClientState(RvMsg m)
        {
            Core.LogDebug("[Net] " + (IsServer ? "host <- (relayed) " : "client <- ") + m);
            try
            {
                // On a listen-server the game relays a client's SendQuestState (our intents) to ALL observers via
                // ReceiveQuestState, so the HOST actually receives client intents through THIS path - not through the
                // SendQuestState RpcLogic prefix (which does not fire for a remote client's call on this build). Route
                // by role: the host treats an incoming message as an intent (OnServerIntent), clients as host state
                // (OnClientState). The two delivery paths stay mutually exclusive - if the SendQuestState prefix ever
                // does intercept, it skips the relay - so an intent is never processed twice; and each op switch
                // ignores any message meant for the other role.
                if (IsServer) OnServerIntent?.Invoke(m);
                else OnClientState?.Invoke(m);
            }
            catch (Exception e) { Core.Log.Warning("[Net] dispatch failed: " + e.Message); }
        }

        internal static void Init(HarmonyLib.Harmony h)
        {
            QuestManagerNetPatches.Apply(h);
            Core.LogDebug("[Net] bus ready.");
        }
    }
}
