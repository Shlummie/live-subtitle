using LiveSubtitle.Core;

namespace LiveSubtitle.App.Audio;

public sealed record AudioEndpointInfo(
    string Id,
    string Name,
    AudioSourceKind Kind,
    bool IsDefault,
    string State);

public sealed record ProcessAudioInfo(
    int ProcessId,
    string Name,
    string? ExecutablePath,
    string DisplayName);

public sealed record AudioChunk(
    string SourceId,
    string SourceLabel,
    string SourceColorHex,
    float[] Samples,
    DateTimeOffset CapturedAtUtc,
    float Level);

public interface IAudioSourceCapture : IAsyncDisposable
{
    AudioSourceProfile Profile { get; }
    event EventHandler<AudioChunk>? AudioAvailable;
    event EventHandler<string>? Faulted;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
