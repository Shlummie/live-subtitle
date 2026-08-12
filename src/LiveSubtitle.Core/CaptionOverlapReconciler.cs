namespace LiveSubtitle.Core;

public sealed class CaptionOverlapReconciler
{
    private const int MaximumTailWords = 12;
    private string[] previousFinalTail = [];

    public string TrimCommittedOverlap(string text)
    {
        var words = SplitWords(text);
        if (words.Length == 0 || previousFinalTail.Length == 0) return Join(words);

        var maximum = Math.Min(previousFinalTail.Length, words.Length);
        for (var length = maximum; length >= 1; length--)
        {
            var previousStart = previousFinalTail.Length - length;
            var matches = true;
            for (var index = 0; index < length; index++)
            {
                if (Normalize(previousFinalTail[previousStart + index]) == Normalize(words[index])) continue;
                matches = false;
                break;
            }

            if (!matches) continue;
            if (length == 1 && Normalize(words[0]).Length < 4) continue;
            return Join(words.AsSpan(length));
        }

        return Join(words);
    }

    public void CommitFinal(string emittedText)
    {
        var words = SplitWords(emittedText);
        if (words.Length == 0) return;
        previousFinalTail = words[^Math.Min(MaximumTailWords, words.Length)..];
    }

    public void Reset() => previousFinalTail = [];

    private static string[] SplitWords(string value) => value.Split(
        (char[]?)null,
        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string Normalize(string value) => new(
        value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string Join(ReadOnlySpan<string> words) =>
        words.Length == 0 ? string.Empty : string.Join(' ', words.ToArray());
}
