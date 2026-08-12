using LiveSubtitle.Core;

namespace LiveSubtitle.App.ViewModels;

public sealed class CaptionLineViewModel : ObservableObject
{
    private string stableText;
    private string provisionalText;
    private bool isFinal;
    private double latencyMs;

    public CaptionLineViewModel(CaptionEvent caption)
    {
        SessionId = caption.SessionId;
        SourceId = caption.SourceId;
        SourceLabel = caption.SourceLabel;
        SourceColorHex = caption.SourceColorHex;
        Sequence = caption.Sequence;
        Start = caption.Start;
        End = caption.End;
        stableText = caption.StableText;
        provisionalText = caption.ProvisionalText;
        isFinal = caption.IsFinal;
        latencyMs = caption.LatencyMs;
    }

    public string SessionId { get; }
    public string SourceId { get; }
    public string SourceLabel { get; }
    public string SourceColorHex { get; }
    public long Sequence { get; private set; }
    public TimeSpan Start { get; private set; }
    public TimeSpan End { get; private set; }
    public string StableText { get => stableText; private set => Set(ref stableText, value); }
    public string ProvisionalText { get => provisionalText; private set => Set(ref provisionalText, value); }
    public string FullText => string.Join(' ', new[] { StableText, ProvisionalText }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public bool IsFinal { get => isFinal; private set => Set(ref isFinal, value); }
    public double LatencyMs { get => latencyMs; private set => Set(ref latencyMs, value); }
    public string TimeLabel => Start.ToString(@"mm\:ss");

    public void Update(CaptionEvent caption)
    {
        Sequence = caption.Sequence;
        Start = caption.Start;
        End = caption.End;
        StableText = caption.StableText;
        ProvisionalText = caption.ProvisionalText;
        IsFinal = caption.IsFinal;
        LatencyMs = caption.LatencyMs;
        Raise(nameof(FullText));
        Raise(nameof(TimeLabel));
    }
}
