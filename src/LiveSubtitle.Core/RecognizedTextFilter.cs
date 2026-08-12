namespace LiveSubtitle.Core;

public static class RecognizedTextFilter
{
    private static readonly HashSet<string> BlankSentinels = new(StringComparer.OrdinalIgnoreCase)
    {
        "BLANKTEXT",
        "BLANKAUDIO",
        "NOSPEECH"
    };

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var sentinelKey = new string(collapsed.Where(char.IsLetterOrDigit).ToArray());
        return BlankSentinels.Contains(sentinelKey) ? string.Empty : collapsed;
    }
}
