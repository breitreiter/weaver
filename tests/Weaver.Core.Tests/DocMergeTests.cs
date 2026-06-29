using Weaver.Core;
using Xunit;

namespace Weaver.Core.Tests;

public class DocMergeTests
{
    [Fact]
    public void Identical_sides_return_that_text()
    {
        var r = DocMerge.Merge("a\nb\nc", "a\nX\nc", "a\nX\nc");
        Assert.True(r.Clean);
        Assert.Equal("a\nX\nc", r.Text);
    }

    [Fact]
    public void Only_one_side_changed_takes_that_side()
    {
        var baseT = "a\nb\nc";
        Assert.Equal("a\nB\nc", DocMerge.Merge(baseT, "a\nB\nc", baseT).Text); // ours
        Assert.Equal("a\nb\nC", DocMerge.Merge(baseT, baseT, "a\nb\nC").Text); // theirs
    }

    [Fact]
    public void Disjoint_edits_merge_cleanly()
    {
        // ours edits line 0, theirs edits line 2 — different base ranges.
        var baseT = "one\ntwo\nthree";
        var r = DocMerge.Merge(baseT, "ONE\ntwo\nthree", "one\ntwo\nTHREE");
        Assert.True(r.Clean);
        Assert.Equal("ONE\ntwo\nTHREE", r.Text);
    }

    [Fact]
    public void Disjoint_insertions_merge_cleanly()
    {
        var baseT = "intro\n\nbody";
        // ours appends to the body region; theirs adds a line after intro.
        var ours = "intro\n\nbody\nours-tail";
        var theirs = "intro\ntheirs-note\n\nbody";
        var r = DocMerge.Merge(baseT, ours, theirs);
        Assert.True(r.Clean);
        Assert.Equal("intro\ntheirs-note\n\nbody\nours-tail", r.Text);
    }

    [Fact]
    public void Overlapping_edits_conflict()
    {
        // both rewrite the same base line, differently.
        var r = DocMerge.Merge("a\nb\nc", "a\nOURS\nc", "a\nTHEIRS\nc");
        Assert.False(r.Clean);
    }

    [Fact]
    public void Trailing_newline_round_trips_on_clean_merge()
    {
        var baseT = "a\nb\n";
        var r = DocMerge.Merge(baseT, "A\nb\n", baseT);
        Assert.True(r.Clean);
        Assert.Equal("A\nb\n", r.Text);
    }

    [Fact]
    public void Empty_base_first_write_is_clean()
    {
        var r = DocMerge.Merge("", "", "first draft");
        Assert.True(r.Clean);
        Assert.Equal("first draft", r.Text);
    }
}
