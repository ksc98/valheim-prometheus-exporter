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
            Def("valheim_player_ping_seconds", "gauge", "Per-player round-trip time as measured by Steam networking.");
            Def("valheim_player_connection_quality", "gauge", "Per-player Steam connection quality 0-1 (side=local|remote).");
            Def("valheim_player_bytes_per_second", "gauge", "Per-player throughput (direction=in|out) as measured by Steam networking.");
            Def("valheim_player_send_queue_bytes", "gauge", "Bytes queued to send to the player (vanilla throttles at 10240).");
            Def("valheim_player_position", "gauge", "Player world position (axis=x|y|z); only for players sharing position.");
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
            Def("valheim_connections_total", "counter", "Connection attempts by result (label result: accepted, wrong_password, banned, full, version, other).");
            Def("valheim_rpc_timeouts_total", "counter", "Peers dropped for not answering RPCs.");
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
            var peers = znet.GetPeers();
            int n = 0;
            foreach (var p in peers)
            {
                if (p == null || !p.IsReady()) continue;
                n++;
                var name = p.m_playerName ?? "";
                s.Gauge("valheim_player_info", 1, L("player", name), L("character", p.m_characterID.ToString()));
                if (p.m_socket is ZSteamSocket ss)
                {
                    ss.GetConnectionQuality(out float lq, out float rq, out int ping, out float outB, out float inB);
                    s.Gauge("valheim_player_ping_seconds", ping / 1000.0, L("player", name));
                    s.Gauge("valheim_player_connection_quality", lq, L("player", name), L("side", "local"));
                    s.Gauge("valheim_player_connection_quality", rq, L("player", name), L("side", "remote"));
                    s.Gauge("valheim_player_bytes_per_second", outB, L("player", name), L("direction", "out"));
                    s.Gauge("valheim_player_bytes_per_second", inB, L("player", name), L("direction", "in"));
                    s.Gauge("valheim_player_send_queue_bytes", ss.GetSendQueueSize(), L("player", name));
                }
                if (p.m_publicRefPos)
                {
                    s.Gauge("valheim_player_position", p.m_refPos.x, L("player", name), L("axis", "x"));
                    s.Gauge("valheim_player_position", p.m_refPos.y, L("player", name), L("axis", "y"));
                    s.Gauge("valheim_player_position", p.m_refPos.z, L("player", name), L("axis", "z"));
                }
            }
            s.Gauge("valheim_players", n);
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
        }
    }
}
