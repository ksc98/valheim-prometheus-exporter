# valheim-prometheus-exporter

BepInEx plugin for a Valheim **dedicated server** that serves Prometheus metrics from live game state.
Server-side only; vanilla clients connect as usual.

`GET http://127.0.0.1:9200/metrics` (configurable). State is sampled every 5 s on the game thread;
scrapes read the last sample, so they never touch the game.

## Metrics

| Metric | Labels | Source |
|---|---|---|
| `valheim_server_up` | | world loaded |
| `valheim_server_frame_seconds`, `valheim_server_frame_max_seconds`, `valheim_server_frames_total` | | Unity frame timing (max frame = GC pause / save stall) |
| `valheim_process_memory_bytes` | | process working set |
| `valheim_players` | | ready peers |
| `valheim_player_info` | `player`, `character` | one per connected player |
| `valheim_player_ping_seconds` | `player` | Steam networking RTT |
| `valheim_player_connection_quality` | `player`, `side=local\|remote` | Steam connection quality 0–1 |
| `valheim_player_bytes_per_second` | `player`, `direction=in\|out` | Steam per-connection throughput |
| `valheim_player_packets_per_second` | `player`, `direction=in\|out` | Steam per-connection packet rate |
| `valheim_player_send_queue_bytes` | `player` | bytes queued to the player |
| `valheim_player_pending_bytes` | `player`, `state=pending\|unacked` | Steam connection buffer occupancy |
| `valheim_player_queue_time_seconds` | `player` | estimated send-queue wait |
| `valheim_player_send_rate_bytes_per_second` | `player` | Steam's send-rate estimate for the connection |
| `valheim_player_position` | `player`, `axis` | only for players sharing their position |
| `valheim_zdos`, `valheim_zdos_sent_per_second`, `valheim_zdos_received_per_second`, `valheim_zdo_change_queue` | | ZDOMan |
| `valheim_world_day`, `valheim_world_time_of_day`, `valheim_world_is_night`, `valheim_world_time_seconds` | | EnvMan |
| `valheim_world_environment_info` | `env` | current weather |
| `valheim_world_info` | `world`, `seed` | |
| `valheim_creatures`, `valheim_creatures_total` | `prefab`, `tamed` | loaded non-player characters |
| `valheim_event_active`, `valheim_event_remaining_seconds`, `valheim_event_started_timestamp_seconds` | `name` | active raid |
| `valheim_global_key_info` | `key` | global keys / world modifiers |
| `valheim_save_in_progress`, `valheim_save_last_duration_seconds`, `valheim_save_last_timestamp_seconds`, `valheim_saves_total` | | world save hooks |
| `valheim_player_joins_total`, `valheim_player_leaves_total`, `valheim_player_deaths_total` | `player` | |
| `valheim_events_total` | `name` | raids started |
| `valheim_connections_total` | `result` | accepted / wrong_password / banned / full / version / other |
| `valheim_rpc_timeouts_total` | | |

Counters are per process lifetime (they reset on server restart; use `increase()`).

## Install

### Any BepInEx server

Drop `ValheimPrometheusExporter.dll` into `BepInEx/plugins/`, or install from the release zip.
On first run it writes `BepInEx/config/dev.ksc98.valheim-prometheus-exporter.cfg`:

```ini
[Listen]
Host = 127.0.0.1   # 0.0.0.0 to scrape from another host or container
Port = 9200

[Collect]
IntervalSeconds = 5
```

Metrics are at `http://<host>:9200/metrics`. The default binds to loopback, so only a scraper on
the same machine (Grafana Alloy, a local Prometheus) can read it.

### Docker / Kubernetes with the `mbround18/valheim` image (Odin)

Odin installs mods from the `MODS` env var and accepts release-zip URLs, so nothing is copied by hand:

```yaml
MODS: |
  https://github.com/ksc98/valheim-prometheus-exporter/releases/download/v0.2.2/ValheimPrometheusExporter-0.2.2.zip
```

Pin the version; a floating URL would upgrade silently on every boot.

### Kubernetes with a Helm chart around that image (how the author runs it)

The exporter is scraped straight off the pod by a `PodMonitor`; no Service is needed. The config
file is seeded from values on every boot, so the listen address lives in git, not on the volume:

```yaml
server:
  type: BepInEx
  mods: |
    https://github.com/ksc98/valheim-prometheus-exporter/releases/download/v0.2.2/ValheimPrometheusExporter-0.2.2.zip
  bepinexConfig:
    dev.ksc98.valheim-prometheus-exporter.cfg: |
      [Listen]
      Host = 0.0.0.0
      Port = 9200

exporter:
  enabled: true     # container port + PodMonitor + NetworkPolicy rule
  port: 9200
  interval: 5s      # match [Collect] IntervalSeconds; a scrape only reads the last sample
```

The PodMonitor's job label becomes `<namespace>/<release>-exporter`; a NetworkPolicy admits only the
metrics namespace to port 9200 while the game ports stay public.

## Dashboard

[`dashboard/valheim-exporter.json`](dashboard/valheim-exporter.json) is a Grafana dashboard built only
from this exporter's metrics (37 panels: server, players, live game, relay responsiveness, ownership).
Import it in Grafana (Dashboards → New → Import), pick your Prometheus datasource, and select the
scrape `job` at the top. It is derived from the author's full dashboard, whose generator lives in the
[homelab repo](https://github.com/ksc98/helm/blob/main/valheim/dashboard-src/); a public read-only copy
of that one runs at https://dashboard.valheim.men.

## Build

Copy `assembly_valheim.dll`, `assembly_utils.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`,
`com.rlabrecque.steamworks.net.dll` from the server's `valheim_server_Data/Managed/` and
`BepInEx.dll`, `0Harmony.dll` from `BepInEx/core/` into `lib/`, then:

```
dotnet build src/ValheimPrometheusExporter.csproj -c Release
```

Private game members are accessed via [Krafs.Publicizer](https://github.com/krafs/Publicizer) at build time.
Built against Valheim 1.0.12.
