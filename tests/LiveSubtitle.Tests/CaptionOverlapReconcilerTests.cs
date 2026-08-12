using LiveSubtitle.Core;

namespace LiveSubtitle.Tests;

public sealed class CaptionOverlapReconcilerTests
{
    [Fact]
    public void RemovesRepeatedWordsFromOverlappingWindow()
    {
        var reconciler = new CaptionOverlapReconciler();
        reconciler.CommitFinal("Live subtitles should appear quickly");

        var result = reconciler.TrimCommittedOverlap("appear quickly while every word stays private.");

        Assert.Equal("while every word stays private.", result);
    }

    [Fact]
    public void PreservesUnrelatedAndShortRepeatedWords()
    {
        var reconciler = new CaptionOverlapReconciler();
        reconciler.CommitFinal("Turn it on");

        Assert.Equal("on every display", reconciler.TrimCommittedOverlap("on every display"));
        Assert.Equal("A new sentence starts", reconciler.TrimCommittedOverlap("A new sentence starts"));
    }
}
