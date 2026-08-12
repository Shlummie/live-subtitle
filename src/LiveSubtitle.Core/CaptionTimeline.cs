namespace LiveSubtitle.Core;

public sealed class CaptionTimeline
{
    private readonly object _gate = new();
    private readonly List<CaptionEvent> _captions = [];

    public void Add(CaptionEvent caption)
    {
        lock (_gate)
        {
            if (!caption.IsFinal)
            {
                _captions.RemoveAll(existing =>
                    !existing.IsFinal &&
                    existing.SourceId == caption.SourceId &&
                    existing.Sequence <= caption.Sequence);
            }

            _captions.Add(caption);
            _captions.Sort(Compare);
        }
    }

    public IReadOnlyList<CaptionEvent> Snapshot(int maximum = 100)
    {
        lock (_gate)
        {
            return _captions.TakeLast(Math.Max(1, maximum)).ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _captions.Clear();
        }
    }

    private static int Compare(CaptionEvent left, CaptionEvent right)
    {
        var time = left.CapturedAtUtc.CompareTo(right.CapturedAtUtc);
        if (time != 0)
        {
            return time;
        }

        time = string.Compare(left.SourceId, right.SourceId, StringComparison.Ordinal);
        return time != 0 ? time : left.Sequence.CompareTo(right.Sequence);
    }
}
