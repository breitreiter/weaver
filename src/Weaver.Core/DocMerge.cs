using DiffPlex;

namespace Weaver.Core;

// 3-way merge for the co-edited document (see project/plans/co-edit-document.md).
//
// The substrate is version-checked diff/patch: a writer sends what it last saw
// (base) and its new text; if the server has moved on, we merge the writer's
// edits onto the current server text using `base` as the common ancestor.
//
// Conservative, line-level diff3: edits that touch DISJOINT base-line ranges
// compose cleanly; edits that touch the SAME range bounce as a conflict for the
// human to resolve. We never silently auto-resolve overlapping edits — a clean
// auto-merge of contested lines would be exactly the "plausible but wrong" move
// weaver refuses elsewhere. Markdown is line-oriented, so a line is the unit.
public static class DocMerge
{
    public readonly record struct Result(bool Clean, string Text);

    // one base-line range [Start, Start+Count) replaced by Lines.
    private readonly record struct Edit(int Start, int Count, string[] Lines);

    public static Result Merge(string baseText, string ours, string theirs)
    {
        // fast paths — the common cases, and they keep line-ending nuance exact.
        if (ours == theirs) return new(true, ours);
        if (baseText == ours) return new(true, theirs);   // only theirs moved
        if (baseText == theirs) return new(true, ours);   // only ours moved

        var od = Differ.Instance.CreateLineDiffs(baseText, ours, ignoreWhitespace: false);
        var td = Differ.Instance.CreateLineDiffs(baseText, theirs, ignoreWhitespace: false);

        var oursEdits = od.DiffBlocks.Select(b =>
            new Edit(b.DeleteStartA, b.DeleteCountA, Slice(od.PiecesNew, b.InsertStartB, b.InsertCountB))).ToList();
        var theirsEdits = td.DiffBlocks.Select(b =>
            new Edit(b.DeleteStartA, b.DeleteCountA, Slice(td.PiecesNew, b.InsertStartB, b.InsertCountB))).ToList();

        // an ours-edit identical to a theirs-edit is the same change made twice —
        // keep it once, never count it as a conflict.
        var merged = new List<Edit>(oursEdits);
        foreach (var t in theirsEdits)
            if (!oursEdits.Any(o => Same(o, t)))
                merged.Add(t);

        // any two edits whose base ranges collide → conflict (all-pairs; n is tiny).
        for (int i = 0; i < merged.Count; i++)
            for (int j = i + 1; j < merged.Count; j++)
                if (Conflicts(merged[i], merged[j]))
                    return new(false, "");

        // disjoint: splice every edit into the base lines, applied back-to-front so
        // earlier indices stay valid.
        var lines = od.PiecesOld.ToList();   // == td.PiecesOld (same ancestor)
        foreach (var e in merged.OrderByDescending(e => e.Start))
        {
            lines.RemoveRange(e.Start, e.Count);
            lines.InsertRange(e.Start, e.Lines);
        }
        return new(true, string.Join('\n', lines));
    }

    // half-open base ranges overlap, OR both are insertions at the same point with
    // differing content (ambiguous interleave), OR they start at the same line.
    private static bool Conflicts(Edit a, Edit b)
    {
        if (Same(a, b)) return false;
        int ae = a.Start + a.Count, be = b.Start + b.Count;
        if (a.Start < be && b.Start < ae) return true;
        return a.Start == b.Start;
    }

    private static bool Same(Edit a, Edit b) =>
        a.Start == b.Start && a.Count == b.Count && a.Lines.SequenceEqual(b.Lines);

    private static string[] Slice(IReadOnlyList<string> arr, int start, int count)
    {
        var r = new string[count];
        for (int i = 0; i < count; i++) r[i] = arr[start + i];
        return r;
    }
}
