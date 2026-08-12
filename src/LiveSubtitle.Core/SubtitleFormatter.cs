using System.Text;

namespace LiveSubtitle.Core;

public static class SubtitleFormatter
{
    public static string ToPlainText(IEnumerable<TranscriptSegment> segments) => string.Join(
        Environment.NewLine,
        Ordered(segments).Select(segment => $"[{segment.SourceLabel}] {segment.Text}"));

    public static string ToSrt(IEnumerable<TranscriptSegment> segments)
    {
        var builder = new StringBuilder();
        var index = 1;
        foreach (var segment in Ordered(segments))
        {
            builder.AppendLine(index++.ToString());
            builder.Append(FormatSrtTime(segment.Start));
            builder.Append(" --> ");
            builder.AppendLine(FormatSrtTime(NormalizedEnd(segment)));
            builder.Append('[').Append(segment.SourceLabel).Append("] ").AppendLine(segment.Text.Trim());
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static string ToVtt(IEnumerable<TranscriptSegment> segments)
    {
        var builder = new StringBuilder("WEBVTT\n\n");
        foreach (var segment in Ordered(segments))
        {
            builder.Append(FormatVttTime(segment.Start));
            builder.Append(" --> ");
            builder.AppendLine(FormatVttTime(NormalizedEnd(segment)));
            builder.Append('[').Append(segment.SourceLabel).Append("] ").AppendLine(segment.Text.Trim());
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static IEnumerable<TranscriptSegment> Ordered(IEnumerable<TranscriptSegment> segments) =>
        segments.OrderBy(segment => segment.Start).ThenBy(segment => segment.Id);

    private static TimeSpan NormalizedEnd(TranscriptSegment segment) =>
        segment.End > segment.Start ? segment.End : segment.Start.Add(TimeSpan.FromMilliseconds(500));

    private static string FormatSrtTime(TimeSpan value) =>
        $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00},{value.Milliseconds:000}";

    private static string FormatVttTime(TimeSpan value) =>
        $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
}
