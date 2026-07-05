using DiffPlex;

namespace Weaver.Core;

// Line-level diff for "what changed in the co-edited document since I last saw it"
// (weaver doc changes). Built on the same DiffPlex line engine as DocMerge, so the
// two agree on what a change is. Purely presentational: it classifies each diff
// block as an addition, a removal, or a change (both sides present), carrying the
// new-doc line anchor so a reader can point at it. Like the rest of weaver it
// enumerates edits — it never says which one matters.
public static class DocDiff
{
    public enum Kind { Added, Removed, Changed }

    // AtLine is 1-based into the NEW document — where the change landed, or where the
    // removed lines used to sit. Removed/Added hold the affected lines on each side
    // (Changed carries both; Added has empty Removed; Removed has empty Added).
    public readonly record struct Hunk(Kind Change, int AtLine, string[] Removed, string[] Added);

    public static IReadOnlyList<Hunk> Compute(string oldText, string newText)
    {
        if (oldText == newText) return [];
        var d = Differ.Instance.CreateLineDiffs(oldText, newText, ignoreWhitespace: false);
        var hunks = new List<Hunk>(d.DiffBlocks.Count);
        foreach (var b in d.DiffBlocks)
        {
            var removed = Slice(d.PiecesOld, b.DeleteStartA, b.DeleteCountA);
            var added = Slice(d.PiecesNew, b.InsertStartB, b.InsertCountB);
            var kind = (removed.Length, added.Length) switch
            {
                (0, _) => Kind.Added,
                (_, 0) => Kind.Removed,
                _ => Kind.Changed,
            };
            hunks.Add(new Hunk(kind, b.InsertStartB + 1, removed, added));
        }
        return hunks;
    }

    private static string[] Slice(IReadOnlyList<string> arr, int start, int count)
    {
        var r = new string[count];
        for (int i = 0; i < count; i++) r[i] = arr[start + i];
        return r;
    }
}
