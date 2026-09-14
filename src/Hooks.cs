using System;
using System.Diagnostics;
using HarmonyLib;

namespace ValheimPrometheusExporter
{
    /// <summary>Harmony hooks for things that are events, not state: joins/leaves/deaths, raids,
    /// saves, connection results, RPC timeouts. Postfixes only; nothing changes game behaviour.</summary>
    public static class Hooks
    {
        // --- players -------------------------------------------------------------------------
        // A peer becomes "ready" when its PeerInfo handshake completes; that's the join.
        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        static class PeerInfo
        {
            static void Postfix(ZRpc rpc)
            {
                var peer = ZNet.instance?.GetPeer(rpc);
                if (peer != null && peer.IsReady())
                {
                    Counters.Inc("joins|" + (peer.m_playerName ?? ""));
                    Counters.Inc("conn|accepted");
                    Collectors.JoinedAt[peer.m_uid] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
        static class Disconnect
        {
            static void Prefix(ZNetPeer peer)
            {
                if (peer == null) return;
                if (peer.IsReady()) Counters.Inc("leaves|" + (peer.m_playerName ?? ""));
                Collectors.JoinedAt.TryRemove(peer.m_uid, out _);
            }
        }

        // Rejections: the server answers a failed handshake with RPC "Error" + a ConnectionStatus code.
        [HarmonyPatch(typeof(ZNet), "SendPeerInfo")]
        static class HandshakeStart { static void Postfix() { Counters.Inc("conn|handshake"); } }

        [HarmonyPatch(typeof(ZRpc), nameof(ZRpc.Invoke), typeof(string), typeof(object[]))]
        static class RpcInvoke
        {
            static void Prefix(string method, object[] parameters)
            {
                if (method != "Error" || parameters == null || parameters.Length == 0) return;
                var code = parameters[0] is int i ? (ZNet.ConnectionStatus)i : ZNet.ConnectionStatus.None;
                string result;
                switch (code)
                {
                    case ZNet.ConnectionStatus.ErrorPassword: result = "wrong_password"; break;
                    case ZNet.ConnectionStatus.ErrorBanned: result = "banned"; break;
                    case ZNet.ConnectionStatus.ErrorFull: result = "full"; break;
                    case ZNet.ConnectionStatus.ErrorVersion: result = "version"; break;
                    default: result = "other"; break;
                }
                Counters.Inc("conn|" + result);
            }
        }

        // --- deaths (server sees the routed OnDeath on the player's ZNetView) ------------------
        [HarmonyPatch(typeof(Player), "RPC_OnDeath")]
        static class Death
        {
            static void Postfix(Player __instance)
            {
                Counters.Inc("deaths|" + (__instance?.GetPlayerName() ?? ""));
            }
        }

        // --- random events (raids) ---------------------------------------------------------
        [HarmonyPatch(typeof(RandEventSystem), "SetActiveEvent")]
        static class ActiveEvent
        {
            static string last;
            static void Postfix(RandomEvent ev)
            {
                var name = ev?.m_name;
                if (!string.IsNullOrEmpty(name) && name != last) Counters.Inc("events|" + name);
                last = name;
            }
        }

        // --- world save ----------------------------------------------------------------------
        static readonly Stopwatch SaveTimer = new Stopwatch();

        [HarmonyPatch(typeof(ZNet), "SaveWorld")]
        static class SaveStart
        {
            static void Prefix()
            {
                Collectors.SaveInProgress = true;
                SaveTimer.Restart();
            }
        }

        // The write happens on a thread; the world thread waits for it here. Postfix = save done.
        [HarmonyPatch(typeof(ZNet), "SaveWorldThread")]
        static class SaveDone
        {
            static void Postfix()
            {
                SaveTimer.Stop();
                Collectors.SaveInProgress = false;
                Collectors.SaveLastSeconds = SaveTimer.Elapsed.TotalSeconds;
                Collectors.SaveLastTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                Counters.Inc("saves");
            }
        }

        // --- RPC timeouts ----------------------------------------------------------------------
        // UpdatePing logs "ZRpc timeout detected" and closes the socket once the ping gap exceeds
        // m_timeout; count it when the prefix sees the gap already past the limit.
        [HarmonyPatch(typeof(ZRpc), "UpdatePing")]
        static class RpcTimeout
        {
            static void Prefix(ZRpc __instance, float dt)
            {
                if (__instance.m_timeSinceLastPing + dt > ZRpc.m_timeout && __instance.IsConnected())
                    Counters.Inc("rpc_timeouts");
            }
        }
    }
}
