using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ValheimPrometheusExporter
{
    /// <summary>One sample: metric name, sorted labels, value. Built fresh on every collection.</summary>
    public readonly struct Sample
    {
        public readonly string Name;
        public readonly KeyValuePair<string, string>[] Labels;
        public readonly double Value;

        public Sample(string name, double value, params KeyValuePair<string, string>[] labels)
        {
            Name = name;
            Value = value;
            Labels = labels ?? new KeyValuePair<string, string>[0];
        }

        public static KeyValuePair<string, string> L(string k, string v) => new KeyValuePair<string, string>(k, v ?? "");
    }

    /// <summary>Metric metadata for the text exposition (# HELP / # TYPE).</summary>
    public sealed class MetricInfo
    {
        public readonly string Name, Type, Help;
        public MetricInfo(string name, string type, string help) { Name = name; Type = type; Help = help; }
    }

    /// <summary>A snapshot of every sample from every collector, plus the metadata table.</summary>
    public sealed class Snapshot
    {
        public readonly List<Sample> Samples = new List<Sample>(256);
        public readonly Dictionary<string, MetricInfo> Info;
        public readonly long TimestampMs;

        public Snapshot(Dictionary<string, MetricInfo> info, long timestampMs)
        {
            Info = info;
            TimestampMs = timestampMs;
        }

        public void Gauge(string name, double value, params KeyValuePair<string, string>[] labels)
            => Samples.Add(new Sample(name, value, labels));

        /// <summary>Prometheus text exposition format.</summary>
        public string ToText()
        {
            var sb = new StringBuilder(16 * 1024);
            string last = null;
            foreach (var s in Samples)
            {
                if (s.Name != last && Info.TryGetValue(s.Name, out var info))
                {
                    sb.Append("# HELP ").Append(info.Name).Append(' ').Append(info.Help).Append('\n');
                    sb.Append("# TYPE ").Append(info.Name).Append(' ').Append(info.Type).Append('\n');
                }
                last = s.Name;
                sb.Append(s.Name);
                if (s.Labels.Length > 0)
                {
                    sb.Append('{');
                    for (int i = 0; i < s.Labels.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(s.Labels[i].Key).Append("=\"").Append(Escape(s.Labels[i].Value)).Append('"');
                    }
                    sb.Append('}');
                }
                sb.Append(' ').Append(s.Value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            }
            return sb.ToString();
        }

        static string Escape(string v) => v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }
}
