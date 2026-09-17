using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimPrometheusExporter
{
    /// <summary>Relay responsiveness: how the server serves each player's send rounds, how much is
    /// waiting, who simulates the creatures near players, and what the networking mod's scheduler
    /// reports. Hooks are postfixes; nothing changes game behaviour.</summary>
    public static class Relay
    {
        // --- per-peer send rounds (ZDOMan.SendZDOs is one round for one peer) --------------------
        public sealed class PeerStats
        {
            public long Rounds, WithData, Starved; public int Backlog; public int BacklogMax;
            public volatile bool ListRebuilt;
        }
        public static readonly ConcurrentDictionary<long, PeerStats> Peers = new ConcurrentDictionary<long, PeerStats>();
        static PeerStats For(ZDOMan.ZDOPeer p) => Peers.GetOrAdd(p.m_peer.m_uid, _ => new PeerStats());

        [HarmonyPatch(typeof(ZDOMan), "SendZDOs")]
        static class SendRound
        {
            static void Prefix(ZDOMan.ZDOPeer peer) { if (peer?.m_peer != null) For(peer).ListRebuilt = false; }
            static void Postfix(ZDOMan.ZDOPeer peer, bool __result)
            {
                if (peer?.m_peer == null) return;
                var s = For(peer);
                s.Rounds++;
                if (__result) s.WithData++;
                // A round that never rebuilt the sync list bailed on the send-window check: the
                // player had a full queue and got nothing this round (vanilla: >10 KB in flight).
                else if (!s.ListRebuilt) s.Starved++;
            }
        }

        // CreateSyncList fills the list of ZDOs this peer still needs: the backlog at round start.
        [HarmonyPatch(typeof(ZDOMan), "CreateSyncList")]
        static class SyncList
        {
            static void Postfix(ZDOMan.ZDOPeer peer, List<ZDO> toSync)
            {
                if (peer?.m_peer == null || toSync == null) return;
                var s = For(peer);
                s.ListRebuilt = true;
                s.Backlog = toSync.Count;
                if (toSync.Count > s.BacklogMax) s.BacklogMax = toSync.Count;
            }
        }

        // --- creature ownership near players (the dedicated server instantiates nothing there;
        //     read the ZDOs in each player's active area instead) --------------------------------
        static readonly Dictionary<int, bool> IsCreaturePrefab = new Dictionary<int, bool>();
        static readonly Dictionary<ZDOID, long> LastOwner = new Dictionary<ZDOID, long>();
        static readonly List<ZDO> Scratch = new List<ZDO>();
        public static long OwnerChanges;

        static bool IsCreature(int prefabHash)
        {
            if (IsCreaturePrefab.TryGetValue(prefabHash, out var v)) return v;
            bool r = false;
            var go = ZNetScene.instance?.GetPrefab(prefabHash);
            if (go != null) r = go.GetComponent<Character>() != null && go.GetComponent<Player>() == null;
            IsCreaturePrefab[prefabHash] = r;
            return r;
        }

        static string OwnerName(long uid, ZNet znet)
        {
            if (uid == 0) return "none";
            if (uid == ZDOMan.GetSessionID()) return "server";
            var p = znet.GetPeer(uid);
            return p != null ? (p.m_playerName ?? "unknown") : "unknown";
        }

        /// <summary>Creatures in the active area of every connected player, grouped by who simulates
        /// them. Also counts owner handoffs between collections.</summary>
        public static void CollectOwnership(Snapshot s)
        {
            var znet = ZNet.instance; var zdoman = ZDOMan.instance;
            if (znet == null || zdoman == null || ZNetScene.instance == null) return;
            var sim = znet.GetSyncedSimulationDistance();
            var near = new SimulationDistance(sim.NearSimulationDistance, 0, sim.IsClassic);
            var seen = new HashSet<ZDOID>();
            var byOwner = new Dictionary<string, int>();
            int total = 0;
            foreach (var p in znet.GetPeers())
            {
                if (p == null || !p.IsReady()) continue;
                Scratch.Clear();
                var zone = ZoneSystem.GetZone(p.m_refPos);
                zdoman.FindSectorObjects(zone, near, Scratch);
                foreach (var z in Scratch)
                {
                    if (z == null || !IsCreature(z.GetPrefab())) continue;
                    // The sector query covers the 5×5 block (±160 m) but ownership is only assigned
                    // inside the game's active area (~1.5 zones, 96 m, from the player's zone centre);
                    // creatures in the outer ring are loaded yet legitimately unowned. Count only
                    // what someone should own, so "none" means something.
                    if (!ZNetScene.InActiveArea(z.GetPosition(), zone) || !seen.Add(z.m_uid)) continue;
                    total++;
                    long owner = z.GetOwner();
                    if (LastOwner.TryGetValue(z.m_uid, out var prev) && prev != 0 && owner != 0 && prev != owner) OwnerChanges++;
                    LastOwner[z.m_uid] = owner;
                    var name = OwnerName(owner, znet);
                    byOwner.TryGetValue(name, out var n); byOwner[name] = n + 1;
                }
            }
            // forget creatures no longer near anyone so the map cannot grow without bound
            if (LastOwner.Count > seen.Count * 4 + 1000)
            {
                var stale = new List<ZDOID>();
                foreach (var k in LastOwner.Keys) if (!seen.Contains(k)) stale.Add(k);
                foreach (var k in stale) LastOwner.Remove(k);
            }
            s.Gauge("valheim_creatures_near_players", total);
            foreach (var kv in byOwner) s.Gauge("valheim_creatures_owned", kv.Value, Sample.L("owner", kv.Key));
            s.Gauge("valheim_creature_owner_changes_total", OwnerChanges);
        }

        // --- NetworkPerformanceSystem scheduler stats, by reflection (absent when not loaded) --------
        static bool npsLooked; static FieldInfo npsServiced, npsBreaks, npsLastFrame;
        public static void CollectNps(Snapshot s)
        {
            if (!npsLooked)
            {
                npsLooked = true;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t;
                    try { t = asm.GetType("NetworkPerformanceSystem.Patches.SendSchedulerPatches"); } catch { continue; }
                    if (t == null) continue;
                    const BindingFlags F = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
                    npsServiced = t.GetField("ServicedLastSecond", F);
                    npsBreaks = t.GetField("BudgetBreaksLastSecond", F);
                    npsLastFrame = t.GetField("LastFrameServiced", F);
                    break;
                }
            }
            if (npsServiced == null) return;
            s.Gauge("valheim_nps_sends_per_second", Convert.ToDouble(npsServiced.GetValue(null)));
            if (npsBreaks != null) s.Gauge("valheim_nps_budget_breaks_per_second", Convert.ToDouble(npsBreaks.GetValue(null)));
            if (npsLastFrame != null) s.Gauge("valheim_nps_last_frame_peers_serviced", Convert.ToDouble(npsLastFrame.GetValue(null)));
        }
    }
}
