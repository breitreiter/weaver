namespace Weaver.Core;

/// <summary>
/// Characterizes a time series into a native-English shape — never a glyph
/// sparkline (a model has to decode glyphs; it reads words directly; see
/// project/plans/cli.md). Emits a compact <c>ShapeCode</c> plus a one-line
/// <c>Prose</c> summary.
///
/// Pipeline (per project/plans/cli.md): bucket → smooth → flatness gate →
/// classify level → run-length encode → collapse monotonic runs into ramps →
/// detect spike/dip → emit.
/// </summary>
public sealed record TrajectoryResult(string ShapeCode, string Prose, bool Flat,
    double Min, double Max, double Mean);

public static class Trajectory
{
    static readonly string[] Bands = ["baseline", "low", "normal", "elevated", "high", "peak"];

    // A series whose smoothed swing is under this fraction of its mean reads as
    // steady — keeps noise from being narrated as shape.
    const double FlatRelThreshold = 0.20;

    public static TrajectoryResult Encode(IReadOnlyList<double> values, double totalMinutes,
        string unit = "", int buckets = 10)
    {
        int n = values.Count;
        if (n == 0) return new("(no data)", "No samples in range.", true, 0, 0, 0);

        buckets = Math.Clamp(buckets, 3, n);
        double[] bmean = BucketMeans(values, buckets);   // real units
        double[] sm = Smooth(bmean);                     // de-noised for shape

        double mn = sm.Min(), mx = sm.Max(), mean = sm.Average();
        double rmn = bmean.Min(), rmx = bmean.Max(), rmean = bmean.Average();
        double rel = (mx - mn) / Math.Max(Math.Abs(mean), 1e-9);

        string U(double v) => Fmt(v) + unit;
        double MinAt(int b) => Math.Round(totalMinutes * b / buckets);

        // --- flat ---------------------------------------------------------
        if (rel < FlatRelThreshold)
        {
            double pct = rmean != 0 ? (rmx - rmn) / 2.0 / Math.Abs(rmean) * 100.0 : 0;
            return new($"steady(0-{Fmt(totalMinutes)}m)",
                $"Steady around {U(rmean)} (+/-{pct:0.#}%) across the window.",
                true, rmn, rmx, rmean);
        }

        // --- classify levels on the de-noised, self-normalized series -----
        int Level(double v)
        {
            double t = (v - mn) / (mx - mn);
            return Math.Clamp((int)(t * Bands.Length), 0, Bands.Length - 1);
        }
        int[] lv = [.. sm.Select(Level)];

        // RLE into (level, startBucket, endBucket)
        var segs = new List<(int level, int s, int e)>();
        int s = 0;
        for (int i = 1; i <= buckets; i++)
            if (i == buckets || lv[i] != lv[s]) { segs.Add((lv[s], s, i - 1)); s = i; }

        // --- emit shape_code: anchors (plateaus + first/last), transitions
        // between them as one word. Intermediate single-bucket bands (smoothing
        // blur) are absorbed into the transition rather than narrated.
        var anchors = new List<(int level, int s, int e)>();
        for (int k = 0; k < segs.Count; k++)
            if (segs[k].e > segs[k].s || k == 0 || k == segs.Count - 1) anchors.Add(segs[k]);

        string AnchorStr((int level, int s, int e) a) =>
            $"{Bands[a.level]}({Fmt(MinAt(a.s))}-{Fmt(MinAt(a.e + 1))}m)";

        var parts = new List<string> { AnchorStr(anchors[0]) };
        for (int k = 1; k < anchors.Count; k++)
        {
            var a = anchors[k];
            var p = anchors[k - 1];
            if (a.level == p.level) continue;
            int gap = a.s - p.e;                        // buckets spent transitioning
            string dir = a.level > p.level ? "up" : "down";
            parts.Add($"{(gap <= 2 ? "step" : "ramp")}-{dir}");
            parts.Add(AnchorStr(a));
        }

        // --- prose from the extremes (robust for ramp / step / spike / dip)
        double startVal = bmean[0], endVal = bmean[^1];
        double peak = bmean.Max(), trough = bmean.Min();
        bool up = (peak - startVal) >= (startVal - trough);
        double extreme = up ? peak : trough;
        double swing = Math.Max(Math.Abs(extreme - startVal), 1e-9);

        int onsetB = 0;
        for (int i = 0; i < buckets; i++)
            if (Math.Abs(bmean[i] - startVal) >= 0.3 * swing) { onsetB = i; break; }

        bool sustained = Math.Abs(endVal - extreme) <= 0.3 * swing;
        bool recovered = Math.Abs(endVal - startVal) <= 0.3 * swing;
        double mult = startVal != 0 ? extreme / startVal : 0;

        string move = up
            ? $"rises to ~{U(peak)}{(mult >= 1.5 ? $" (~{mult:0.#}x baseline)" : "")}"
            : $"drops to ~{U(trough)}";
        string tail = sustained ? ", sustained to the end."
                    : recovered ? ", then recovers."
                    : $", settling near {U(endVal)}.";
        string prose = $"Around {U(startVal)} until T+{Fmt(MinAt(onsetB))}m, then {move}{tail}";

        return new(string.Join(" ", parts), prose, false, rmn, rmx, rmean);
    }

    // --- helpers ----------------------------------------------------------
    static double[] BucketMeans(IReadOnlyList<double> v, int buckets)
    {
        var outp = new double[buckets];
        for (int b = 0; b < buckets; b++)
        {
            int lo = (int)((long)b * v.Count / buckets);
            int hi = (int)((long)(b + 1) * v.Count / buckets);
            if (hi <= lo) hi = lo + 1;
            double sum = 0; int c = 0;
            for (int i = lo; i < hi && i < v.Count; i++) { sum += v[i]; c++; }
            outp[b] = c > 0 ? sum / c : 0;
        }
        return outp;
    }

    static double[] Smooth(double[] x)
    {
        var outp = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            double sum = 0; int c = 0;
            for (int j = Math.Max(0, i - 1); j <= Math.Min(x.Length - 1, i + 1); j++) { sum += x[j]; c++; }
            outp[i] = sum / c;
        }
        return outp;
    }

    static string Fmt(double v)
    {
        double a = Math.Abs(v);
        if (a >= 100) return v.ToString("0");
        if (a >= 10) return v.ToString("0.#");
        if (a >= 1) return v.ToString("0.##");
        return v.ToString("0.####");
    }
}
