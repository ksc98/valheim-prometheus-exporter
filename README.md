# valheim-prometheus-exporter

BepInEx plugin for a Valheim dedicated server that serves Prometheus metrics from live game state.

- [Install](#install)
- [Metrics](#metrics)
- [Dashboard](#dashboard)
- [Build](#build)

`GET http://127.0.0.1:9200/metrics` (configurable). State is sampled every 5 seconds on the game thread;
scrapes read the last sample, so they never touch the game.

## Install

### Any BepInEx server

Drop `ValheimPrometheusExporter.dll` into `BepInEx/plugins/`, or install from the release zip.
On first run it writes `BepInEx/config/dev.ksc98.valheim-prometheus-exporter.cfg`:

```ini
[Listen]
Host = 127.0.0.1   # 0.0.0.0 binds all interfaces. Caution: you may expose this to the internet
Port = 9200

[Collect]
IntervalSeconds = 5
```

Metrics are at `http://<host>:9200/metrics`. The default binds to the loopback interface, so
only processes on the same machine can reach it, not even the rest of your LAN.

## Metrics

| Metric | Type | Description |
|---|---|---|
| `valheim_exporter_info` | gauge | Exporter version (labels); value 1. |
| `valheim_exporter_collect_seconds` | gauge | Time the last collection took. |
| `valheim_server_up` | gauge | 1 once the world is loaded and the server accepts players. |
| `valheim_server_uptime_seconds` | gauge | Seconds since the exporter started (plugin load = server process start). |
| `valheim_server_frame_seconds` | gauge | Server frame time, smoothed over the last collection interval. One core saturates near 0.05 s (20 fps). |
| `valheim_server_frame_max_seconds` | gauge | Longest single frame since the last collection (a GC pause or save stall). |
| `valheim_server_frames_total` | counter | Frames rendered by the server loop. |
| `valheim_process_memory_bytes` | gauge | Resident memory of the server process. |
| `valheim_players` | gauge | Connected players. |
| `valheim_player_info` | gauge | One series per connected player (labels: player, character id); value 1. |
| `valheim_player_joined_timestamp_seconds` | gauge | Unix time the player's connection completed its handshake (label player). |
| `valheim_player_ping_seconds` | gauge | Per-player round-trip time as measured by Steam networking. |
| `valheim_player_connection_quality` | gauge | Per-player Steam connection quality 0-1 (side=local\|remote). |
| `valheim_player_bytes_per_second` | gauge | Per-player throughput (direction=in\|out) as measured by Steam networking. |
| `valheim_player_packets_per_second` | gauge | Per-player packet rate (direction=in\|out) as measured by Steam networking. |
| `valheim_player_send_queue_bytes` | gauge | Bytes queued to send to the player (vanilla throttles at 10240). |
| `valheim_player_pending_bytes` | gauge | Bytes in the Steam connection buffers (state=pending\|unacked). Growing = the player's link can't keep up. |
| `valheim_player_queue_time_seconds` | gauge | Estimated time a new packet waits in the send queue before it goes on the wire. |
| `valheim_player_send_rate_bytes_per_second` | gauge | Steam's current send-rate estimate for the player's connection (what the networking mod tunes). |
| `valheim_player_position` | gauge | Player world position (axis=x\|y\|z); only for players sharing position. |
| `valheim_player_distance_meters` | gauge | Distance between two connected players (labels a, b; a < b). Below ~96 m they share an ownership area. |
| `valheim_zdos` | gauge | Networked objects (ZDOs) in the loaded world. |
| `valheim_zdos_sent_per_second` | gauge | ZDO updates sent to clients in the last second. |
| `valheim_zdos_received_per_second` | gauge | ZDO updates received from clients in the last second. |
| `valheim_zdo_change_queue` | gauge | ZDO changes waiting to be sent. |
| `valheim_world_day` | gauge | In-game day. |
| `valheim_world_time_of_day` | gauge | Fraction of the in-game day, 0 = midnight, 0.5 = noon. |
| `valheim_world_is_night` | gauge | 1 during in-game night. |
| `valheim_world_time_seconds` | gauge | Total in-game seconds elapsed. |
| `valheim_world_environment_info` | gauge | Current weather/environment name (label env); value 1. |
| `valheim_world_info` | gauge | World name and seed name (labels); value 1. |
| `valheim_creatures` | gauge | Loaded non-player characters by prefab (labels: prefab, tamed=0\|1). |
| `valheim_creatures_total` | gauge | All loaded non-player characters. |
| `valheim_event_active` | gauge | 1 while a random event (raid) is running; label name. |
| `valheim_event_started_timestamp_seconds` | gauge | Unix time the active event started. |
| `valheim_event_remaining_seconds` | gauge | Seconds until the active event ends. |
| `valheim_global_key_info` | gauge | One series per global key/world modifier (label key); value 1. |
| `valheim_save_in_progress` | gauge | 1 while the world is being saved. |
| `valheim_save_last_duration_seconds` | gauge | Duration of the last world save. |
| `valheim_save_last_timestamp_seconds` | gauge | Unix time the last world save finished. |
| `valheim_saves_total` | counter | World saves completed. |
| `valheim_player_joins_total` | counter | Player connections that reached the world (label player). |
| `valheim_player_leaves_total` | counter | Player disconnects (label player). |
| `valheim_player_deaths_total` | counter | Player deaths (label player). |
| `valheim_events_total` | counter | Random events started (label name). |
| `valheim_player_event_timestamp_seconds` | gauge | Unix time of the player's latest join, leave or death (labels event=join\|leave\|death, player). |
| `valheim_raid_timestamp_seconds` | gauge | Unix time the named random event last started (label name). |
| `valheim_connections_total` | counter | Connection attempts by result (label result: accepted, wrong_password, banned, full, version, other). |
| `valheim_rpc_timeouts_total` | counter | Peers dropped for not answering RPCs. |
| `valheim_player_send_rounds_total` | counter | Send rounds the server ran for the player (one call of the ZDO send routine). |
| `valheim_player_send_rounds_with_data_total` | counter | Send rounds that actually sent object updates to the player. |
| `valheim_player_send_rounds_starved_total` | counter | Send rounds skipped because the player's in-flight window was full (nothing sent that round). |
| `valheim_player_sync_backlog` | gauge | Object updates waiting for the player at the start of the latest send round. |
| `valheim_player_sync_backlog_max` | gauge | Largest send-round backlog for the player since the last collection. |
| `valheim_creatures_near_players` | gauge | Creatures (non-player characters) inside any connected player's ownership active area (~96 m from their zone centre), from the object store. |
| `valheim_creatures_owned` | gauge | Creatures near players by who simulates them (label owner: player name, server, none). |
| `valheim_creature_owner_changes_total` | counter | Creature ownership handoffs between players observed between collections. |
| `valheim_gc_collections_total` | counter | Managed garbage collections by generation (label generation). |
| `valheim_nps_sends_per_second` | gauge | NetworkPerformanceSystem: per-peer sends completed in the last second (absent without the mod). |
| `valheim_nps_budget_breaks_per_second` | gauge | NetworkPerformanceSystem: send rounds cut short by the frame budget in the last second. |
| `valheim_nps_last_frame_peers_serviced` | gauge | NetworkPerformanceSystem: peers served in the latest frame. |

Counters are per process lifetime (they reset on server restart; use `increase()`).

## Dashboard

[`dashboard/valheim-exporter.json`](dashboard/valheim-exporter.json) is a Grafana dashboard built only
from this exporter's metrics: server, players, live game, relay responsiveness, ownership. Import it
(Dashboards → New → Import), pick your Prometheus datasource, and select the scrape `job` at the top.

## Build

Copy `assembly_valheim.dll`, `assembly_utils.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`,
`com.rlabrecque.steamworks.net.dll` from the server's `valheim_server_Data/Managed/` and
`BepInEx.dll`, `0Harmony.dll` from `BepInEx/core/` into `lib/`, then:

```
dotnet build src/ValheimPrometheusExporter.csproj -c Release
```

`just package` builds and assembles the release zip (`dist/ValheimPrometheusExporter-<version>.zip`: the
DLL, this README and `package/manifest.json`); the version comes from `Plugin.cs` and must match the manifest.

Private game members are accessed via [Krafs.Publicizer](https://github.com/krafs/Publicizer) at build time.
Built against Valheim 1.0.12 and BepInExPack_Valheim 5.4.2350.
