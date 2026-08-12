namespace LiveSubtitle.Core;

public sealed record StabilizedTranscript(string Stable, string Provisional, string FullText, bool IsFinal);

public sealed class TranscriptStabilizer
{
    private string[] _previousWords = [];
    private int _stableWordCount;

    public StabilizedTranscript Update(string text, bool isFinal)
    {
        var words = SplitWords(text);
        if (isFinal)
        {
            var finalText = Join(words);
            Reset();
            return new StabilizedTranscript(finalText, string.Empty, finalText, true);
        }

        var commonPrefix = CommonPrefixLength(_previousWords, words);
        _stableWordCount = Math.Min(words.Length, Math.Max(_stableWordCount, Math.Max(0, commonPrefix - 1)));

        var stable = Join(words.AsSpan(0, _stableWordCount));
        var provisional = Join(words.AsSpan(_stableWordCount));
        var full = Join(words);
        _previousWords = words;

        return new StabilizedTranscript(stable, provisional, full, false);
    }

    public void Reset()
    {
        _previousWords = [];
        _stableWordCount = 0;
    }

    private static string[] SplitWords(string value) => value
        .Split((char[]?)null, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static int CommonPrefixLength(ReadOnlySpan<string> first, ReadOnlySpan<string> second)
    {
        var length = Math.Min(first.Length, second.Length);
        var index = 0;
        while (index < length && string.Equals(first[index], second[index], StringComparison.Ordinal))
        {
            index++;
        }

        return index;
    }

    private static string Join(ReadOnlySpan<string> words) =>
        words.Length == 0 ? string.Empty : string.Join(' ', words.ToArray());
}
