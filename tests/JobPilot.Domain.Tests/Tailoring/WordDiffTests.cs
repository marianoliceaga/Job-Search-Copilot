using JobPilot.Domain.Tailoring;

namespace JobPilot.Domain.Tests.Tailoring;

public class WordDiffTests
{
    [Fact]
    public void Marks_added_and_removed_words()
    {
        var diff = WordDiff.Compute("Cut payment latency by 40%.", "Reduced payment latency by 40% on Azure.");

        Assert.Equal(
        [
            new DiffSegment(DiffKind.Added, "Reduced"),
            new DiffSegment(DiffKind.Removed, "Cut"),
            new DiffSegment(DiffKind.Same, "payment latency by 40%"),
            new DiffSegment(DiffKind.Added, "on Azure."),
        ], diff);
    }

    [Fact]
    public void Identical_texts_are_one_segment()
    {
        Assert.Equal([new DiffSegment(DiffKind.Same, "Same text here")], WordDiff.Compute("Same text here", "Same  text here"));
        Assert.Empty(WordDiff.Compute(null, ""));
    }
}
