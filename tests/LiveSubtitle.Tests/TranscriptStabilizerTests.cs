using LiveSubtitle.Core;

namespace LiveSubtitle.Tests;

public sealed class TranscriptStabilizerTests
{
    [Fact]
    public void RepeatedDraft_CommitsSharedWordsAndLeavesTailProvisional()
    {
        var stabilizer = new TranscriptStabilizer();

        stabilizer.Update("the quick brown", false);
        var result = stabilizer.Update("the quick brown fox", false);

        Assert.Equal("the quick", result.Stable);
        Assert.Equal("brown fox", result.Provisional);
        Assert.False(result.IsFinal);
    }

    [Fact]
    public void FinalResult_ResetsState()
    {
        var stabilizer = new TranscriptStabilizer();
        stabilizer.Update("hello there", false);

        var final = stabilizer.Update("hello there", true);
        var next = stabilizer.Update("new phrase", false);

        Assert.Equal("hello there", final.Stable);
        Assert.Equal(string.Empty, next.Stable);
    }
}
