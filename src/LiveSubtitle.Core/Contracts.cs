using System.Text.Json.Serialization;

namespace LiveSubtitle.Core;

public enum AudioSourceKind
{
    OutputDevice,
    InputDevice,
    Process
}

public enum InferenceBackend
{
    Auto,
    Vulkan,
    Cpu
}

public enum RecognitionPreset
{
    Aggressive,
    Balanced,
    Stable,
    Custom
}

public enum ThemeMode
{
    Dark,
    Light,
    System,
    HighContrast
}

public enum OverlayAnchor
{
    BottomCenter,
    BottomLeft,
    BottomRight,
    TopCenter,
    Custom
}

public enum OverlayCaptureMode
{
    Include,
    Exclude
}

public sealed record AudioSourceProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public AudioSourceKind Kind { get; init; } = AudioSourceKind.OutputDevice;
    public string? DeviceId { get; init; }
    public bool FollowDefaultDevice { get; init; } = true;
    public string? ProcessExecutablePath { get; init; }
    public string? ProcessName { get; init; }
    public string Label { get; init; } = "Computer audio";
    public string ColorHex { get; init; } = "#69A7FF";
    public double Gain { get; init; } = 1.0;
    public bool Enabled { get; init; } = true;
}

public sealed record RecognitionProfile
{
    public string ModelId { get; init; } = "base-en";
    public InferenceBackend Backend { get; init; } = InferenceBackend.Auto;
    public RecognitionPreset Preset { get; init; } = RecognitionPreset.Aggressive;
    public int DraftIntervalMs { get; init; } = 250;
    public int FinalizeSilenceMs { get; init; } = 450;
    public int WindowOverlapMs { get; init; } = 500;
    public int MaxUtteranceSeconds { get; init; } = 15;
    public float VadThreshold { get; init; } = 0.50f;
    public float NoiseFloor { get; init; } = 0.008f;
    public string CustomVocabulary { get; init; } = string.Empty;
    public bool IncludePunctuation { get; init; } = true;
    public bool MaskProfanity { get; init; }
}

public sealed record OverlayProfile
{
    public string? MonitorId { get; init; }
    public OverlayAnchor Anchor { get; init; } = OverlayAnchor.BottomCenter;
    public double X { get; init; } = 0.5;
    public double Y { get; init; } = 0.84;
    public double Width { get; init; } = 900;
    public int MaxLines { get; init; } = 3;
    public string FontFamily { get; init; } = "Segoe UI Variable Display";
    public double FontSize { get; init; } = 34;
    public int FontWeight { get; init; } = 600;
    public string TextColorHex { get; init; } = "#FFFFFFFF";
    public string DraftColorHex { get; init; } = "#B8D5FFFF";
    public string BackgroundColorHex { get; init; } = "#D9161A20";
    public string OutlineColorHex { get; init; } = "#E6000000";
    public double BackgroundOpacity { get; init; } = 0.82;
    public double CornerRadius { get; init; } = 12;
    public double OutlineThickness { get; init; } = 1.5;
    public double ShadowOpacity { get; init; } = 0.72;
    public double ShadowBlurRadius { get; init; } = 18;
    public double ShadowDepth { get; init; } = 2;
    public bool ShowSourceLabels { get; init; } = true;
    public bool ClickThroughWhenLocked { get; init; } = true;
    public bool Locked { get; init; } = true;
    public OverlayCaptureMode CaptureMode { get; init; } = OverlayCaptureMode.Include;
}

public sealed record HotkeyProfile
{
    public string ToggleListening { get; init; } = "Ctrl+Alt+Space";
    public string ToggleOverlay { get; init; } = "Ctrl+Alt+O";
    public string ToggleOverlayLock { get; init; } = "Ctrl+Alt+L";
    public string NextProfile { get; init; } = "Ctrl+Alt+P";
    public string SaveSession { get; init; } = "Ctrl+Alt+S";
}

public sealed record UserProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Default";
    public IReadOnlyList<AudioSourceProfile> AudioSources { get; init; } =
    [
        new AudioSourceProfile()
    ];
    public RecognitionProfile Recognition { get; init; } = new();
    public OverlayProfile Overlay { get; init; } = new();
    public HotkeyProfile Hotkeys { get; init; } = new();
}

public sealed record GeneralSettings
{
    public ThemeMode Theme { get; init; } = ThemeMode.Dark;
    public bool StartWithWindows { get; init; }
    public bool MinimizeToTrayWhileListening { get; init; } = true;
    public bool ShowOverlayOnStart { get; init; } = true;
    public bool ShowFirstRun { get; init; } = true;
    public bool ReduceMotion { get; init; }
}

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string ActiveProfileId { get; init; } = "default";
    public IReadOnlyList<UserProfile> Profiles { get; init; } = CreateDefaultProfiles();
    public GeneralSettings General { get; init; } = new();

    public static AppSettings CreateDefault() => new();

    private static IReadOnlyList<UserProfile> CreateDefaultProfiles() =>
    [
        new UserProfile
        {
            Id = "default",
            Name = "Default"
        },
        new UserProfile
        {
            Id = "gaming",
            Name = "Gaming",
            Overlay = new OverlayProfile { Width = 820, FontSize = 32, MaxLines = 2 }
        },
        new UserProfile
        {
            Id = "videos",
            Name = "Videos",
            Overlay = new OverlayProfile { Width = 940, FontSize = 36, MaxLines = 3 }
        },
        new UserProfile
        {
            Id = "calls",
            Name = "Calls",
            AudioSources =
            [
                new AudioSourceProfile { Label = "Call", ColorHex = "#69A7FF" },
                new AudioSourceProfile
                {
                    Kind = AudioSourceKind.InputDevice,
                    Label = "You",
                    ColorHex = "#72D7A0"
                }
            ]
        }
    ];
}

public sealed record CaptionEvent
{
    public required string SessionId { get; init; }
    public required string SourceId { get; init; }
    public required string SourceLabel { get; init; }
    public required string SourceColorHex { get; init; }
    public long Sequence { get; init; }
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public required string Text { get; init; }
    public string StableText { get; init; } = string.Empty;
    public string ProvisionalText { get; init; } = string.Empty;
    public bool IsFinal { get; init; }
    public double LatencyMs { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record TranscriptSegment
{
    public long Id { get; init; }
    public required string SessionId { get; init; }
    public required string SourceId { get; init; }
    public required string SourceLabel { get; init; }
    public required string SourceColorHex { get; init; }
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public required string Text { get; init; }
}

public sealed record TranscriptSession
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? EndedAtUtc { get; init; }
    public bool Saved { get; init; }
    public IReadOnlyList<TranscriptSegment> Segments { get; init; } = [];
}

public sealed record ModelDefinition(
    string Id,
    string DisplayName,
    string Description,
    string FileName,
    Uri DownloadUri,
    long ApproximateBytes,
    string Sha256,
    bool Recommended = false);

public enum EngineMessageKind
{
    Hello,
    LoadModel,
    StartSource,
    AudioFrame,
    EndSource,
    Stop,
    Ready,
    Caption,
    Metrics,
    Error
}

public sealed record EngineMessage
{
    public EngineMessageKind Kind { get; init; }
    public string? RequestId { get; init; }
    public string? SourceId { get; init; }
    public string? SourceLabel { get; init; }
    public string? SourceColorHex { get; init; }
    public string? ModelPath { get; init; }
    public string? VadModelPath { get; init; }
    public InferenceBackend Backend { get; init; }
    public RecognitionProfile? Recognition { get; init; }
    public float[]? Samples { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; }
    public CaptionEvent? Caption { get; init; }
    public string? Detail { get; init; }
    public double Value { get; init; }
}
