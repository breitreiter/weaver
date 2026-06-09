using Weaver.Contracts;

namespace Weaver.Core;

/// <summary>
/// The analysis primitives (see project/plans/analysis-architecture.md). Each
/// one ENUMERATES — it lists everything reachable / everything that moved /
/// every onset — and never DISCRIMINATES cause from collateral. There is
/// deliberately no method here that takes symptoms and returns a cause.
/// Pure functions over observed data; the API wires the DB to them.
/// </summary>
public static class Analysis
{
    public sealed record SeriesInput(string Kind, string Id, string Metric,
        IReadOnlyList<(DateTimeOffset Ts, double Val)> Points);

    /// <summary>
    /// Transitive dependents of <paramref name="node"/> — everyone whose
    /// requests route through it, so everyone affected if it degrades. Requires
    /// the caller to supply the hypothesis (the node); it tests a guess, it
    /// does not generate one. Edges are (from depends-on to).
    /// </summary>
    public static BlastRadiusDto BlastRadius(IReadOnlyList<(string From, string To)> edges, string node)
    {
        var dependents = new Dictionary<string, List<string>>();
        foreach (var (from, to) in edges)
        {
            if (!dependents.TryGetValue(to, out var list)) dependents[to] = list = [];
            list.Add(from);
        }

        var dist = new Dictionary<string, int>();
        var seen = new HashSet<string> { node };
        var q = new Queue<(string id, int hop)>();
        q.Enqueue((node, 0));
        while (q.Count > 0)
        {
            var (id, hop) = q.Dequeue();
            if (!dependents.TryGetValue(id, out var ups)) continue;
            foreach (var f in ups)
                if (seen.Add(f)) { dist[f] = hop + 1; q.Enqueue((f, hop + 1)); }
        }

        var nodes = dist.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key)
            .Select(kv => new BlastNodeDto(kv.Key, kv.Value)).ToList();
        return new BlastRadiusDto(node, nodes.Count, nodes);
    }

    /// <summary>
    /// Signals that deviated in the subject window (ts >= split) vs the base
    /// window (ts &lt; split). Flags on both statistical (z) and material
    /// (percent) significance so diurnal wobble doesn't read as an incident.
    /// Returns ALL movers, unranked by causality.
    /// </summary>
    public static List<AnomalyDto> Anomalies(IEnumerable<SeriesInput> series,
        DateTimeOffset split, double z, double minPct)
    {
        var outp = new List<AnomalyDto>();
        foreach (var s in series)
        {
            var baseVals = s.Points.Where(p => p.Ts < split).Select(p => p.Val).ToList();
            var subjPts = s.Points.Where(p => p.Ts >= split).ToList();
            if (baseVals.Count < 3 || subjPts.Count < 3) continue;

            double bm = baseVals.Average();
            double effSd = Math.Max(Std(baseVals, bm), Math.Max(0.02 * Math.Abs(bm), 1e-9));
            double sm = subjPts.Average(p => p.Val);
            double zsc = (sm - bm) / effSd;
            double deltaPct = bm != 0 ? (sm - bm) / Math.Abs(bm) * 100 : 0;
            if (Math.Abs(zsc) < z || Math.Abs(deltaPct) < minPct) continue;

            bool up = zsc > 0;
            double thr = up ? bm + z * effSd : bm - z * effSd;
            string? onset = null;
            foreach (var p in subjPts)
                if ((up && p.Val >= thr) || (!up && p.Val <= thr)) { onset = Iso(p.Ts); break; }

            outp.Add(new AnomalyDto(s.Kind, s.Id, s.Metric,
                Math.Round(bm, 3), Math.Round(sm, 3), Math.Round(deltaPct, 1),
                Math.Round(zsc, 1), up ? "up" : "down", onset));
        }
        return outp.OrderByDescending(a => Math.Abs(a.Z)).ToList();
    }

    /// <summary>
    /// Earliest onset per subject, ordered ascending — reveals precedence.
    /// Reading precedence as causation is the investigator's call.
    /// </summary>
    public static List<TimelineEntryDto> Timeline(IEnumerable<SeriesInput> series,
        DateTimeOffset split, double z, double minPct)
    {
        return Anomalies(series, split, z, minPct)
            .Where(a => a.OnsetTs is not null)
            .GroupBy(a => (a.SubjectKind, a.SubjectId))
            .Select(g => g.OrderBy(a => a.OnsetTs, StringComparer.Ordinal).First())
            .Select(a => new TimelineEntryDto(a.SubjectId, a.SubjectKind, a.Metric, a.OnsetTs!, a.Z))
            .OrderBy(t => t.OnsetTs, StringComparer.Ordinal)
            .ToList();
    }

    static double Std(IReadOnlyList<double> v, double mean)
    {
        double s = 0;
        foreach (var x in v) s += (x - mean) * (x - mean);
        return Math.Sqrt(s / v.Count);
    }

    static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
}
