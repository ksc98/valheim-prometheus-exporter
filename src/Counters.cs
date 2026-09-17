using System.Collections.Generic;

namespace ValheimPrometheusExporter
{
    /// <summary>Monotonic counters kept for the life of the process (events observed by hooks).</summary>
    public static class Counters
    {
        static readonly object Lock = new object();
        static readonly Dictionary<string, double> Values = new Dictionary<string, double>();

        public static void Inc(string key, double by = 1)
        {
            lock (Lock) { Values.TryGetValue(key, out var v); Values[key] = v + by; }
        }

        public static double Get(string key)
        {
            lock (Lock) { return Values.TryGetValue(key, out var v) ? v : 0; }
        }

        /// <summary>All keys sharing a prefix, e.g. "deaths|" + player name.</summary>
        public static List<KeyValuePair<string, double>> WithPrefix(string prefix)
        {
            var outp = new List<KeyValuePair<string, double>>();
            lock (Lock)
            {
                foreach (var kv in Values)
                    if (kv.Key.StartsWith(prefix)) outp.Add(new KeyValuePair<string, double>(kv.Key.Substring(prefix.Length), kv.Value));
            }
            return outp;
        }
    }
}
