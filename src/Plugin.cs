using System;
using System.Net;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ValheimPrometheusExporter
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dev.ksc98.valheim-prometheus-exporter";
        public const string Name = "ValheimPrometheusExporter";
        public const string Version = "0.1.0";

        public static ManualLogSource Log;

        ConfigEntry<string> listenHost;
        ConfigEntry<int> listenPort;
        ConfigEntry<float> collectInterval;

        HttpListener listener;
        Thread serveThread;
        volatile string latestText = "# exporter starting\n";
        float sinceCollect;

        void Awake()
        {
            Log = Logger;
            if (!IsDedicatedServer())
            {
                Log.LogInfo("not a dedicated server; exporter disabled");
                return;
            }
            listenHost = Config.Bind("Listen", "Host", "127.0.0.1",
                "Address for the /metrics HTTP endpoint. Keep it on localhost and scrape via a sidecar or "
                + "reverse proxy; 0.0.0.0 exposes player names and positions to anyone who can reach the port.");
            listenPort = Config.Bind("Listen", "Port", 9200, "Port for /metrics.");
            collectInterval = Config.Bind("Collect", "IntervalSeconds", 5f,
                "How often game state is sampled. /metrics serves the latest sample, so scrapes never touch the game thread.");

            new Harmony(Guid).PatchAll(typeof(Hooks));
            StartListener();
            Log.LogInfo($"{Name} {Version}: /metrics on http://{listenHost.Value}:{listenPort.Value}/metrics every {collectInterval.Value:0.#}s");
        }

        static bool IsDedicatedServer()
        {
            // the dedicated build has no graphics device; also true under BepInEx's server pack
            return SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
        }

        void Update()
        {
            if (listener == null) return;
            float dt = Time.unscaledDeltaTime;
            Collectors.FrameAccum += dt;
            Collectors.FrameCount++;
            Collectors.FramesTotal++;
            if (dt > Collectors.FrameMax) Collectors.FrameMax = dt;

            sinceCollect += dt;
            if (sinceCollect < collectInterval.Value) return;
            sinceCollect = 0;
            try { latestText = Collectors.Collect().ToText(); }
            catch (Exception e) { Log.LogError("collect failed: " + e); }
        }

        void StartListener()
        {
            listener = new HttpListener();
            listener.Prefixes.Add($"http://{listenHost.Value}:{listenPort.Value}/");
            listener.Start();
            serveThread = new Thread(Serve) { IsBackground = true, Name = "valheim-prometheus-exporter" };
            serveThread.Start();
        }

        void Serve()
        {
            while (listener != null && listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); } catch { break; }
                try
                {
                    var path = ctx.Request.Url.AbsolutePath;
                    byte[] body;
                    if (path == "/metrics")
                    {
                        body = Encoding.UTF8.GetBytes(latestText);
                        ctx.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
                    }
                    else if (path == "/healthz")
                    {
                        body = Encoding.UTF8.GetBytes("ok\n");
                        ctx.Response.ContentType = "text/plain";
                    }
                    else
                    {
                        ctx.Response.StatusCode = 404;
                        body = Encoding.UTF8.GetBytes("not found\n");
                    }
                    ctx.Response.ContentLength64 = body.Length;
                    ctx.Response.OutputStream.Write(body, 0, body.Length);
                }
                catch (Exception e) { Log.LogWarning("serve: " + e.Message); }
                finally { try { ctx.Response.Close(); } catch { } }
            }
        }

        void OnDestroy()
        {
            try { listener?.Stop(); } catch { }
            listener = null;
        }
    }
}
