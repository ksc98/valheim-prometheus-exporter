using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using static ValheimPrometheusExporter.Sample;

namespace ValheimPrometheusExporter
{
    /// <summary>Reads live game state into a Snapshot. Each group is wrapped so a game update that
    /// renames one member drops that group's metrics for the run instead of killing the exporter.</summary>
    public static class Collectors
    {
        public static readonly Dictionary<string, MetricInfo> Info = new Dictionary<string, MetricInfo>();
        static void Def(string name, string type, string help) => Info[name] = new MetricInfo(name, type, help);

        static readonly Process Proc = Process.GetCurrentProcess();
        static readonly DateTime Started = DateTime.UtcNow;

        static Collectors()
        {
            Def("valheim_exporter_info", "gauge", "Exporter version (labels); value 1.");
            Def("valheim_exporter_collect_seconds", "gauge", "Time the last collection took.");
            Def("valheim_server_up", "gauge", "1 once the world is loaded and the server accepts players.");
            Def("valheim_server_uptime_seconds", "gauge", "Seconds since the exporter started (plugin load = server process start).");
            Def("valheim_server_frame_seconds", "gauge", "Server frame time, smoothed over the last collection interval. One core saturates near 0.05 s (20 fps).");
            Def("valheim_server_frame_max_seconds", "gauge", "Longest single frame since the last collection (a GC pause or save stall).");
            Def("valheim_server_frames_total", "counter", "Frames rendered by the server loop.");
            Def("valheim_process_memory_bytes", "gauge", "Resident memory of the server process.");
            Def("valheim_players", "gauge", "Connected players.");
            Def("valheim_player_info", "gauge", "One series per connected player (labels: player, character id); value 1.");
            Def("valheim_player_joined_timestamp_seconds", "gauge", "Unix time the player's connection completed its handshake (label player).");
            Def("valheim_player_ping_seconds", "gauge", "Per-player round-trip time as measured by Steam networking.");
            Def("valheim_player_connection_quality", "gauge", "Per-player Steam connection quality 0-1 (side=local|remote).");
            Def("valheim_player_bytes_per_second", "gauge", "Per-player throughput (direction=in|out) as measured by Steam networking.");
            Def("valheim_player_packets_per_second", "gauge", "Per-player packet rate (direction=in|out) as measured by Steam networking.");
            Def("valheim_player_send_queue_bytes", "gauge", "Bytes queued to send to the player (vanilla throttles at 10240).");
            Def("valheim_player_pending_bytes", "gauge", "Bytes in the Steam connection buffers (state=pending|unacked). Growing = the player's link can't keep up.");
            Def("valheim_player_queue_time_seconds", "gauge", "Estimated time a new packet waits in the send queue before it goes on the wire.");
            Def("valheim_player_send_rate_bytes_per_second", "gauge", "Steam's current send-rate estimate for the player's connection (what the networking mod tunes).");
            Def("valheim_player_position", "gauge", "Player world position (axis=x|y|z); only for players sharing position.");
            Def("valheim_player_distance_meters", "gauge", "Distance between two connected players (labels a, b; a < b). Below ~96 m they share an ownership area.");
            Def("valheim_zdos", "gauge", "Networked objects (ZDOs) in the loaded world.");
            Def("valheim_zdos_sent_per_second", "gauge", "ZDO updates sent to clients in the last second.");
            Def("valheim_zdos_received_per_second", "gauge", "ZDO updates received from clients in the last second.");
            Def("valheim_zdo_change_queue", "gauge", "ZDO changes waiting to be sent.");
            Def("valheim_world_day", "gauge", "In-game day.");
            Def("valheim_world_time_of_day", "gauge", "Fraction of the in-game day, 0 = midnight, 0.5 = noon.");
            Def("valheim_world_is_night", "gauge", "1 during in-game night.");
            Def("valheim_world_time_seconds", "gauge", "Total in-game seconds elapsed.");
            Def("valheim_world_environment_info", "gauge", "Current weather/environment name (label env); value 1.");
            Def("valheim_world_info", "gauge", "World name and seed name (labels); value 1.");
            Def("valheim_creatures", "gauge", "Loaded non-player characters by prefab (labels: prefab, tamed=0|1).");
            Def("valheim_creatures_total", "gauge", "All loaded non-player characters.");
            Def("valheim_event_active", "gauge", "1 while a random event (raid) is running; label name.");
            Def("valheim_event_started_timestamp_seconds", "gauge", "Unix time the active event started.");
            Def("valheim_event_remaining_seconds", "gauge", "Seconds until the active event ends.");
            Def("valheim_global_key_info", "gauge", "One series per global key/world modifier (label key); value 1.");
            Def("valheim_save_in_progress", "gauge", "1 while the world is being saved.");
            Def("valheim_save_last_duration_seconds", "gauge", "Duration of the last world save.");
            Def("valheim_save_last_timestamp_seconds", "gauge", "Unix time the last world save finished.");
            Def("valheim_saves_total", "counter", "World saves completed.");
            Def("valheim_player_joins_total", "counter", "Player connections that reached the world (label player).");
            Def("valheim_player_leaves_total", "counter", "Player disconnects (label player).");
            Def("valheim_player_deaths_total", "counter", "Player deaths (label player).");
            Def("valheim_events_total", "counter", "Random events started (label name).");
            Def("valheim_player_event_timestamp_seconds", "gauge", "Unix time of the player's latest join, leave or death (labels event=join|leave|death, player).");
            Def("valheim_raid_timestamp_seconds", "gauge", "Unix time the named random event last started (label name).");
            Def("valheim_connections_total", "counter", "Connection attempts by result (label result: accepted, wrong_password, banned, full, version, other).");
            Def("valheim_rpc_timeouts_total", "counter", "Peers dropped for not answering RPCs.");
            Def("valheim_player_send_rounds_total", "counter", "Send rounds the server ran for the player (one call of the ZDO send routine).");
            Def("valheim_player_send_rounds_with_data_total", "counter", "Send rounds that actually sent object updates to the player.");
            Def("valheim_player_send_rounds_starved_total", "counter", "Send rounds skipped because the player's in-flight window was full (nothing sent that round).");
            Def("valheim_player_sync_backlog", "gauge", "Object updates waiting for the player at the start of the latest send round.");
            Def("valheim_player_sync_backlog_max", "gauge", "Largest send-round backlog for the player since the last collection.");
            Def("valheim_creatures_near_players", "gauge", "Creatures (non-player characters) inside any connected player's ownership active area (~96 m from their zone centre), from the object store.");
            Def("valheim_creatures_owned", "gauge", "Creatures near players by who simulates them (label owner: player name, server, none).");
            Def("valheim_creature_owner_changes_total", "counter", "Creature ownership handoffs between players observed between collections.");
            Def("valheim_gc_collections_total", "counter", "Managed garbage collections by generation (label generation).");
            Def("valheim_nps_sends_per_second", "gauge", "NetworkPerformanceSystem: per-peer sends completed in the last second (absent without the mod).");
            Def("valheim_nps_budget_breaks_per_second", "gauge", "NetworkPerformanceSystem: send rounds cut short by the frame budget in the last second.");
            Def("valheim_nps_last_frame_peers_serviced", "gauge", "NetworkPerformanceSystem: peers served in the latest frame.");
        }

        public static Snapshot Collect()
        {
            var sw = Stopwatch.StartNew();
            var snap = new Snapshot(Info, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            snap.Gauge("valheim_exporter_info", 1, L("version", Plugin.Version));
            snap.Gauge("valheim_server_uptime_seconds", (DateTime.UtcNow - Started).TotalSeconds);
            Try("frame", () => FrameStats(snap));
            Try("process", () => { Proc.Refresh(); snap.Gauge("valheim_process_memory_bytes", Proc.WorkingSet64); });
            Try("server", () => Server(snap));
            Try("players", () => Players(snap));
            Try("zdo", () => Zdo(snap));
            Try("world", () => World(snap));
            Try("creatures", () => Creatures(snap));
            Try("event", () => Event(snap));
            Try("keys", () => Keys(snap));
            Try("save", () => Save(snap));
            Try("counters", () => CounterSamples(snap));
            Try("relay", () => RelayStats(snap));
            Try("ownership", () => Relay.CollectOwnership(snap));
            Try("gc", () => { for (int g = 0; g <= GC.MaxGeneration; g++) snap.Gauge("valheim_gc_collections_total", GC.CollectionCount(g), L("generation", g.ToString())); });
            Try("nps", () => Relay.CollectNps(snap));
            snap.Gauge("valheim_exporter_collect_seconds", sw.Elapsed.TotalSeconds);
            return snap;
        }

        static readonly HashSet<string> Failed = new HashSet<string>();
        static void Try(string group, Action a)
        {
            try { a(); }
            catch (Exception e)
            {
                if (Failed.Add(group)) Plugin.Log.LogWarning($"collector '{group}' failed (game API changed?): {e.GetType().Name}: {e.Message}");
            }
        }

        // ---- frame timing (fed by Plugin.Update) ----------------------------------------
        public static double FrameAccum, FrameMax; public static long FrameCount, FramesTotal;
        static void FrameStats(Snapshot s)
        {
            if (FrameCount > 0) s.Gauge("valheim_server_frame_seconds", FrameAccum / FrameCount);
            s.Gauge("valheim_server_frame_max_seconds", FrameMax);
            s.Gauge("valheim_server_frames_total", FramesTotal);
            FrameAccum = 0; FrameMax = 0; FrameCount = 0;
        }

        static void Server(Snapshot s)
        {
            var znet = ZNet.instance;
            s.Gauge("valheim_server_up", znet != null && ZNet.m_world != null ? 1 : 0);
            if (znet == null) return;
            s.Gauge("valheim_world_info", 1, L("world", ZNet.m_world?.m_name), L("seed", ZNet.m_world?.m_seedName));
        }

        static void Players(Snapshot s)
        {
            var znet = ZNet.instance; if (znet == null) return;
            int n = 0;
            foreach (var p in znet.GetPeers())
            {
                if (p == null || !p.IsReady()) continue;
                n++;
                var name = p.m_playerName ?? "";
                s.Gauge("valheim_player_info", 1, L("player", name), L("character", p.m_characterID.ToString()));
                if (JoinedAt.TryGetValue(p.m_uid, out var joined))
                    s.Gauge("valheim_player_joined_timestamp_seconds", joined, L("player", name));
                try { PlayerSocket(s, p, name); }
                catch (Exception e) { if (Failed.Add("player-socket")) Plugin.Log.LogWarning($"per-player socket stats unavailable: {e.GetType().Name}: {e.Message}"); }
                if (p.m_publicRefPos)
                {
                    s.Gauge("valheim_player_position", p.m_refPos.x, L("player", name), L("axis", "x"));
                    s.Gauge("valheim_player_position", p.m_refPos.y, L("player", name), L("axis", "y"));
                    s.Gauge("valheim_player_position", p.m_refPos.z, L("player", name), L("axis", "z"));
                }
            }
            s.Gauge("valheim_players", n);
            // Pairwise distance: the server always knows every peer's reference position (it needs it to
            // decide what to send), so this needs no position sharing. Distance only, no coordinates.
            var ready = new List<ZNetPeer>();
            foreach (var p in znet.GetPeers()) if (p != null && p.IsReady()) ready.Add(p);
            for (int i = 0; i < ready.Count; i++)
                for (int j = i + 1; j < ready.Count; j++)
                {
                    string na = ready[i].m_playerName ?? "", nb = ready[j].m_playerName ?? "";
                    if (string.CompareOrdinal(na, nb) > 0) { var t = na; na = nb; nb = t; }
                    var d = Vector3.Distance(ready[i].m_refPos, ready[j].m_refPos);
                    s.Gauge("valheim_player_distance_meters", d, L("a", na), L("b", nb));
                }
        }

        // The game's own GetConnectionQuality() calls the *client* Steam API, which is not
        // initialised on a dedicated server; the server-side socket code uses the game-server
        // interface with the same connection handle. Mirror that.
        static void PlayerSocket(Snapshot s, ZNetPeer p, string name)
        {
            if (!(p.m_socket is ZSteamSocket ss)) return;
            var st = default(Steamworks.SteamNetConnectionRealTimeStatus_t);
            var lane = default(Steamworks.SteamNetConnectionRealTimeLaneStatus_t);
            var res = Steamworks.SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(ss.m_con, ref st, 0, ref lane);
            if (res != Steamworks.EResult.k_EResultOK) return;
            s.Gauge("valheim_player_ping_seconds", st.m_nPing / 1000.0, L("player", name));
            s.Gauge("valheim_player_connection_quality", st.m_flConnectionQualityLocal, L("player", name), L("side", "local"));
            s.Gauge("valheim_player_connection_quality", st.m_flConnectionQualityRemote, L("player", name), L("side", "remote"));
            s.Gauge("valheim_player_bytes_per_second", st.m_flOutBytesPerSec, L("player", name), L("direction", "out"));
            s.Gauge("valheim_player_bytes_per_second", st.m_flInBytesPerSec, L("player", name), L("direction", "in"));
            s.Gauge("valheim_player_packets_per_second", st.m_flOutPacketsPerSec, L("player", name), L("direction", "out"));
            s.Gauge("valheim_player_packets_per_second", st.m_flInPacketsPerSec, L("player", name), L("direction", "in"));
            s.Gauge("valheim_player_send_queue_bytes", ss.GetSendQueueSize(), L("player", name));
            s.Gauge("valheim_player_pending_bytes", st.m_cbPendingReliable + st.m_cbPendingUnreliable, L("player", name), L("state", "pending"));
            s.Gauge("valheim_player_pending_bytes", st.m_cbSentUnackedReliable, L("player", name), L("state", "unacked"));
            s.Gauge("valheim_player_queue_time_seconds", (long)st.m_usecQueueTime / 1e6, L("player", name));
            s.Gauge("valheim_player_send_rate_bytes_per_second", st.m_nSendRateBytesPerSecond, L("player", name));
        }

        static void Zdo(Snapshot s)
        {
            var z = ZDOMan.instance; if (z == null) return;
            s.Gauge("valheim_zdos", z.NrOfObjects());
            s.Gauge("valheim_zdos_sent_per_second", z.GetSentZDOs());
            s.Gauge("valheim_zdos_received_per_second", z.GetRecvZDOs());
            s.Gauge("valheim_zdo_change_queue", z.GetClientChangeQueue());
        }

        static void World(Snapshot s)
        {
            var e = EnvMan.instance; if (e == null) return;
            s.Gauge("valheim_world_day", e.GetDay());
            s.Gauge("valheim_world_time_of_day", e.GetDayFraction());
            s.Gauge("valheim_world_is_night", EnvMan.IsNight() ? 1 : 0);
            s.Gauge("valheim_world_time_seconds", ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0);
            var env = e.GetCurrentEnvironment();
            if (env != null) s.Gauge("valheim_world_environment_info", 1, L("env", env.m_name));
        }

        static void RelayStats(Snapshot s)
        {
            var znet = ZNet.instance; if (znet == null) return;
            foreach (var kv in Relay.Peers)
            {
                var peer = znet.GetPeer(kv.Key);
                if (peer == null) { Relay.Peers.TryRemove(kv.Key, out _); continue; }
                var name = peer.m_playerName ?? ""; var st = kv.Value;
                s.Gauge("valheim_player_send_rounds_total", st.Rounds, L("player", name));
                s.Gauge("valheim_player_send_rounds_with_data_total", st.WithData, L("player", name));
                s.Gauge("valheim_player_send_rounds_starved_total", st.Starved, L("player", name));
                s.Gauge("valheim_player_sync_backlog", st.Backlog, L("player", name));
                s.Gauge("valheim_player_sync_backlog_max", st.BacklogMax, L("player", name));
                st.BacklogMax = st.Backlog;
            }
        }

        static void Creatures(Snapshot s)
        {
            var counts = new Dictionary<string, int>();
            int total = 0;
            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null || c.IsPlayer()) continue;
                total++;
                var key = Prefab(c.gameObject.name) + "|" + (c.IsTamed() ? "1" : "0");
                counts.TryGetValue(key, out var n); counts[key] = n + 1;
            }
            s.Gauge("valheim_creatures_total", total);
            foreach (var kv in counts)
            {
                var parts = kv.Key.Split('|');
                s.Gauge("valheim_creatures", kv.Value, L("prefab", parts[0]), L("tamed", parts[1]));
            }
        }

        static string Prefab(string goName)
        {
            int i = goName.IndexOf('(');
            return (i > 0 ? goName.Substring(0, i) : goName).Trim();
        }

        static void Event(Snapshot s)
        {
            var r = RandEventSystem.instance; if (r == null) return;
            var ev = r.GetActiveEvent();
            if (ev == null) { s.Gauge("valheim_event_active", 0, L("name", "")); return; }
            s.Gauge("valheim_event_active", 1, L("name", ev.m_name));
            s.Gauge("valheim_event_remaining_seconds", Math.Max(0, ev.m_duration - ev.m_time), L("name", ev.m_name));
            s.Gauge("valheim_event_started_timestamp_seconds", DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ev.m_time, L("name", ev.m_name));
        }

        static void Keys(Snapshot s)
        {
            var z = ZoneSystem.instance; if (z == null) return;
            foreach (var k in z.GetGlobalKeys()) s.Gauge("valheim_global_key_info", 1, L("key", k));
        }

        // peer uid → join time; written by the PeerInfo/Disconnect hooks on the main thread, read by the collector
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<long, long> JoinedAt = new System.Collections.Concurrent.ConcurrentDictionary<long, long>();
        // "<kind>|<who>" → unix time of the latest such event. A timestamp gauge shows an event from
        // its first sample; a counter's first increment is invisible to increase()/changes().
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> LastEvent = new System.Collections.Concurrent.ConcurrentDictionary<string, long>();
        public static void Stamp(string key) => LastEvent[key] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public static volatile bool SaveInProgress; public static double SaveLastSeconds; public static long SaveLastTs;
        static void Save(Snapshot s)
        {
            s.Gauge("valheim_save_in_progress", SaveInProgress ? 1 : 0);
            if (SaveLastTs > 0)
            {
                s.Gauge("valheim_save_last_duration_seconds", SaveLastSeconds);
                s.Gauge("valheim_save_last_timestamp_seconds", SaveLastTs);
            }
        }

        static void CounterSamples(Snapshot s)
        {
            s.Gauge("valheim_saves_total", Counters.Get("saves"));
            s.Gauge("valheim_rpc_timeouts_total", Counters.Get("rpc_timeouts"));
            foreach (var kv in Counters.WithPrefix("joins|")) s.Gauge("valheim_player_joins_total", kv.Value, L("player", kv.Key));
            foreach (var kv in Counters.WithPrefix("leaves|")) s.Gauge("valheim_player_leaves_total", kv.Value, L("player", kv.Key));
            foreach (var kv in Counters.WithPrefix("deaths|")) s.Gauge("valheim_player_deaths_total", kv.Value, L("player", kv.Key));
            foreach (var kv in Counters.WithPrefix("events|")) s.Gauge("valheim_events_total", kv.Value, L("name", kv.Key));
            foreach (var kv in Counters.WithPrefix("conn|")) s.Gauge("valheim_connections_total", kv.Value, L("result", kv.Key));
            foreach (var kv in LastEvent)
            {
                int i = kv.Key.IndexOf('|'); if (i < 0) continue;
                string kind = kv.Key.Substring(0, i), who = kv.Key.Substring(i + 1);
                if (kind == "raid") s.Gauge("valheim_raid_timestamp_seconds", kv.Value, L("name", who));
                else s.Gauge("valheim_player_event_timestamp_seconds", kv.Value, L("event", kind), L("player", who));
            }
        }
    }
}
