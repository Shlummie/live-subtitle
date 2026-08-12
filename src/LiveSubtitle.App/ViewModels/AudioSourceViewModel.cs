using LiveSubtitle.App.Audio;
using LiveSubtitle.Core;

namespace LiveSubtitle.App.ViewModels;

public sealed class AudioSourceViewModel : ObservableObject
{
    private string label;
    private string colorHex;
    private AudioSourceKind kind;
    private double gain;
    private bool enabled;
    private AudioEndpointInfo? selectedEndpoint;
    private ProcessAudioInfo? selectedProcess;
    private float level;
    private string state = "Ready";
    private string? detail;

    public AudioSourceViewModel(AudioSourceProfile profile)
    {
        Id = profile.Id;
        label = profile.Label;
        colorHex = profile.ColorHex;
        kind = profile.Kind;
        gain = profile.Gain;
        enabled = profile.Enabled;
        FollowDefaultDevice = profile.FollowDefaultDevice;
        DeviceId = profile.DeviceId;
        ProcessExecutablePath = profile.ProcessExecutablePath;
        ProcessName = profile.ProcessName;
    }

    public string Id { get; }
    public string Label { get => label; set => Set(ref label, value); }
    public string ColorHex { get => colorHex; set => Set(ref colorHex, value); }
    public AudioSourceKind Kind { get => kind; set { if (Set(ref kind, value)) Raise(nameof(IsProcess)); } }
    public double Gain { get => gain; set => Set(ref gain, value); }
    public bool Enabled { get => enabled; set => Set(ref enabled, value); }
    public bool FollowDefaultDevice { get; set; }
    public string? DeviceId { get; set; }
    public string? ProcessExecutablePath { get; set; }
    public string? ProcessName { get; set; }
    public bool IsProcess => Kind == AudioSourceKind.Process;
    public float Level { get => level; set { if (Set(ref level, value)) Raise(nameof(LevelPercent)); } }
    public double LevelPercent => Math.Clamp(Level * 550, 0, 100);
    public string State { get => state; set => Set(ref state, value); }
    public string? Detail { get => detail; set => Set(ref detail, value); }

    public AudioEndpointInfo? SelectedEndpoint
    {
        get => selectedEndpoint;
        set
        {
            if (!Set(ref selectedEndpoint, value) || value is null) return;
            DeviceId = value.Id;
            FollowDefaultDevice = value.IsDefault;
            Kind = value.Kind;
        }
    }

    public ProcessAudioInfo? SelectedProcess
    {
        get => selectedProcess;
        set
        {
            if (!Set(ref selectedProcess, value) || value is null) return;
            ProcessExecutablePath = value.ExecutablePath;
            ProcessName = value.Name;
            Kind = AudioSourceKind.Process;
        }
    }

    public AudioSourceProfile ToProfile() => new()
    {
        Id = Id,
        Label = string.IsNullOrWhiteSpace(Label) ? "Audio" : Label.Trim(),
        ColorHex = ColorHex,
        Kind = Kind,
        Gain = Gain,
        Enabled = Enabled,
        FollowDefaultDevice = FollowDefaultDevice,
        DeviceId = DeviceId,
        ProcessExecutablePath = ProcessExecutablePath,
        ProcessName = ProcessName
    };
}
