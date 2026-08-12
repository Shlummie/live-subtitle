using LiveSubtitle.Core;

namespace LiveSubtitle.Tests;

public sealed class SubtitleFormatterTests
{
    private static readonly TranscriptSegment[] Segments =
    [
        new TranscriptSegment
        {
            Id = 2,
            SessionId = "session",
            SourceId = "mic",
            SourceLabel = "You",
            SourceColorHex = "#fff",
            Start = TimeSpan.FromSeconds(2),
            End = TimeSpan.FromSeconds(3.25),
            Text = "Second line"
        },
        new TranscriptSegment
        {
            Id = 1,
            SessionId = "session",
            SourceId = "system",
            SourceLabel = "Video",
            SourceColorHex = "#fff",
            Start = TimeSpan.FromMilliseconds(500),
            End = TimeSpan.FromSeconds(1.5),
            Text = "First line"
        }
    ];

    [Fact]
    public void Srt_OrdersSegmentsAndFormatsTimestamps()
    {
        var output = SubtitleFormatter.ToSrt(Segments);

        Assert.Contains("00:00:00,500 --> 00:00:01,500", output);
        Assert.True(output.IndexOf("First line", StringComparison.Ordinal) <
                    output.IndexOf("Second line", StringComparison.Ordinal));
    }

    [Fact]
    public void Vtt_HasHeaderAndSourceLabels()
    {
        var output = SubtitleFormatter.ToVtt(Segments);

        Assert.StartsWith("WEBVTT", output);
        Assert.Contains("[Video] First line", output);
        Assert.Contains("00:00:02.000 --> 00:00:03.250", output);
    }
}
