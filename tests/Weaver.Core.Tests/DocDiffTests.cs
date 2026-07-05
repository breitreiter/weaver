using Weaver.Core;
using Xunit;

namespace Weaver.Core.Tests;

public class DocDiffTests
{
    [Fact]
    public void Identical_text_has_no_hunks()
    {
        Assert.Empty(DocDiff.Compute("a\nb\nc", "a\nb\nc"));
        Assert.Empty(DocDiff.Compute("", ""));
    }

    [Fact]
    public void Pure_insertion_is_an_Added_hunk()
    {
        var h = Assert.Single(DocDiff.Compute("a\nb", "a\nNEW\nb"));
        Assert.Equal(DocDiff.Kind.Added, h.Change);
        Assert.Equal(new[] { "NEW" }, h.Added);
        Assert.Empty(h.Removed);
        Assert.Equal(2, h.AtLine);   // 1-based into the new doc
    }

    [Fact]
    public void Pure_deletion_is_a_Removed_hunk()
    {
        var h = Assert.Single(DocDiff.Compute("a\ngone\nb", "a\nb"));
        Assert.Equal(DocDiff.Kind.Removed, h.Change);
        Assert.Equal(new[] { "gone" }, h.Removed);
        Assert.Empty(h.Added);
    }

    [Fact]
    public void Replacement_is_a_Changed_hunk_with_both_sides()
    {
        // the gold case: the human sharpens a tentative claim into a confirmed one.
        var h = Assert.Single(DocDiff.Compute("likely DB saturation", "DB saturation, CONFIRMED"));
        Assert.Equal(DocDiff.Kind.Changed, h.Change);
        Assert.Equal(new[] { "likely DB saturation" }, h.Removed);
        Assert.Equal(new[] { "DB saturation, CONFIRMED" }, h.Added);
    }

    [Fact]
    public void Multiple_disjoint_edits_yield_multiple_hunks()
    {
        var hunks = DocDiff.Compute("h1\nbody\nh2\ntail", "h1\nbody EDITED\nh2\ntail\nappended");
        Assert.Equal(2, hunks.Count);
        Assert.Contains(hunks, x => x.Change == DocDiff.Kind.Changed);
        Assert.Contains(hunks, x => x.Change == DocDiff.Kind.Added && x.Added.Length == 1 && x.Added[0] == "appended");
    }
}
