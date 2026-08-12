using LiveSubtitle.Core;

namespace LiveSubtitle.Tests;

public sealed class CaptionTimelineTests
{
    [Fact]
    public void NewDraft_ReplacesOlderDraftForSameSource()
    {
        var timeline = new CaptionTimeline();
        timeline.Add(Caption("source", 1, "hello", false));
        timeline.Add(Caption("source", 2, "hello world", false));

        var snapshot = timeline.Snapshot();

        Assert.Single(snapshot);
        Assert.Equal("hello world", snapshot[0].Text);
    }

    [Fact]
    public void MultipleSources_AreMergedByCaptureTimeThenSource()
    {
        var timeline = new CaptionTimeline();
        timeline.Add(Caption("call", 3, "third", true) with
        {
            CapturedAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(3)
        });
        timeline.Add(Caption("game", 1, "first", true) with
        {
            CapturedAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(1)
        });
        timeline.Add(Caption("microphone", 2, "second", true) with
        {
            CapturedAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(2)
        });

        Assert.Equal(["first", "second", "third"], timeline.Snapshot().Select(item => item.Text));
    }

    private static CaptionEvent Caption(string source, long sequence, string text, bool final) => new()
    {
        SessionId = "session",
        SourceId = source,
        SourceLabel = source,
        SourceColorHex = "#fff",
        Sequence = sequence,
        Text = text,
        IsFinal = final,
        CapturedAtUtc = DateTimeOffset.UnixEpoch.AddMilliseconds(sequence)
    };
}
