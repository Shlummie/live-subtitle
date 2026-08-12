using LiveSubtitle.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LiveSubtitle.App.Audio;

public sealed class EndpointAudioCapture : IAudioSourceCapture
{
    private readonly object gate = new();
    private MMDevice? device;
    private IWaveIn? capture;

    public EndpointAudioCapture(AudioSourceProfile profile)
    {
        Profile = profile;
    }

    public AudioSourceProfile Profile { get; }
    public string? ActiveDeviceId { get; private set; }
    public event EventHandler<AudioChunk>? AudioAvailable;
    public event EventHandler<string>? Faulted;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (capture is not null) return Task.CompletedTask;
            device = AudioDeviceService.ResolveDevice(Profile);
            ActiveDeviceId = device.ID;
            capture = Profile.Kind == AudioSourceKind.OutputDevice
                ? new WasapiLoopbackCapture(device)
                : new WasapiCapture(device, useEventSync: true, audioBufferMillisecondsLength: 40);
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            capture.StartRecording();
        }
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        lock (gate)
        {
            capture?.StopRecording();
        }
        return Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        var current = capture;
        if (current is null || args.BytesRecorded == 0) return;
        try
        {
            var input = WaveSampleConverter.Convert(args.Buffer, args.BytesRecorded, current.WaveFormat);
            var normalized = AudioProcessing.ToMono16Khz(
                input,
                current.WaveFormat.SampleRate,
                current.WaveFormat.Channels,
                Profile.Gain);
            AudioAvailable?.Invoke(this, new AudioChunk(
                Profile.Id,
                Profile.Label,
                Profile.ColorHex,
                normalized,
                DateTimeOffset.UtcNow,
                AudioProcessing.RootMeanSquare(normalized)));
        }
        catch (Exception exception)
        {
            Faulted?.Invoke(this, $"{Profile.Label}: {exception.Message}");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is not null)
        {
            Faulted?.Invoke(this, $"{Profile.Label} stopped: {args.Exception.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        lock (gate)
        {
            if (capture is not null)
            {
                capture.DataAvailable -= OnDataAvailable;
                capture.RecordingStopped -= OnRecordingStopped;
                capture.Dispose();
                capture = null;
            }
            device?.Dispose();
            device = null;
            ActiveDeviceId = null;
        }
    }
}
