using System.Collections.Concurrent;
using System.Threading.Channels;
using LiveSubtitle.Core;

namespace LiveSubtitle.App.Audio;

public sealed record SourceStatus(string SourceId, string Label, string State, string? Detail = null);

public sealed class AudioCaptureCoordinator : IAsyncDisposable
{
    private readonly Channel<AudioChunk> chunks = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(64)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly ConcurrentDictionary<string, SourceRunner> runners = new();
    private CancellationTokenSource? cancellation;

    public event EventHandler<AudioChunk>? MeterUpdated;
    public event EventHandler<SourceStatus>? StatusChanged;

    public IAsyncEnumerable<AudioChunk> ReadAllAsync(CancellationToken cancellationToken) =>
        chunks.Reader.ReadAllAsync(cancellationToken);

    public Task StartAsync(IEnumerable<AudioSourceProfile> profiles, CancellationToken cancellationToken = default)
    {
        if (cancellation is not null) throw new InvalidOperationException("Audio capture is already running.");
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        foreach (var profile in profiles.Where(profile => profile.Enabled))
        {
            var runner = new SourceRunner(profile, OnAudio, OnStatus);
            if (runners.TryAdd(profile.Id, runner))
            {
                runner.Start(cancellation.Token);
            }
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var activeCancellation = cancellation;
        cancellation = null;
        activeCancellation?.Cancel();
        await Task.WhenAll(runners.Values.Select(runner => runner.Completion)).ConfigureAwait(false);
        foreach (var runner in runners.Values)
        {
            await runner.DisposeAsync().ConfigureAwait(false);
        }
        runners.Clear();
        activeCancellation?.Dispose();
    }

    private void OnAudio(AudioChunk chunk)
    {
        MeterUpdated?.Invoke(this, chunk);
        chunks.Writer.TryWrite(chunk);
    }

    private void OnStatus(SourceStatus status) => StatusChanged?.Invoke(this, status);

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        chunks.Writer.TryComplete();
    }

    private sealed class SourceRunner : IAsyncDisposable
    {
        private readonly AudioSourceProfile profile;
        private readonly Action<AudioChunk> onAudio;
        private readonly Action<SourceStatus> onStatus;
        private IAudioSourceCapture? capture;
        private Task? runTask;
        private TaskCompletionSource<string>? faultSignal;

        public SourceRunner(
            AudioSourceProfile profile,
            Action<AudioChunk> onAudio,
            Action<SourceStatus> onStatus)
        {
            this.profile = profile;
            this.onAudio = onAudio;
            this.onStatus = onStatus;
        }

        public Task Completion => runTask ?? Task.CompletedTask;

        public void Start(CancellationToken cancellationToken)
        {
            if (runTask is not null) throw new InvalidOperationException("The source runner is already active.");
            runTask = RunAsync(cancellationToken);
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    faultSignal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    capture = profile.Kind == AudioSourceKind.Process
                        ? new ProcessLoopbackCapture(profile)
                        : new EndpointAudioCapture(profile);
                    capture.AudioAvailable += OnAudioAvailable;
                    capture.Faulted += OnFaulted;
                    await capture.StartAsync(cancellationToken).ConfigureAwait(false);
                    onStatus(new SourceStatus(profile.Id, profile.Label, "Listening"));

                    if (profile.Kind == AudioSourceKind.Process)
                    {
                        var process = AudioDeviceService.ResolveProcess(profile);
                        if (process is not null)
                        {
                            using var processWaitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            var processExit = process.WaitForExitAsync(processWaitCancellation.Token);
                            try
                            {
                                var completed = await Task.WhenAny(processExit, faultSignal.Task).ConfigureAwait(false);
                                if (completed == faultSignal.Task)
                                {
                                    processWaitCancellation.Cancel();
                                    try { await processExit.ConfigureAwait(false); } catch (OperationCanceledException) { }
                                    onStatus(new SourceStatus(profile.Id, profile.Label, "Recovering", await faultSignal.Task.ConfigureAwait(false)));
                                }
                                else
                                {
                                    await processExit.ConfigureAwait(false);
                                    onStatus(new SourceStatus(profile.Id, profile.Label, "Waiting", "The app closed; reattaching when it restarts."));
                                }
                            }
                            finally { process.Dispose(); }
                        }
                        else
                        {
                            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        while (!cancellationToken.IsCancellationRequested)
                        {
                            var tick = Task.Delay(1000, cancellationToken);
                            var completed = await Task.WhenAny(faultSignal.Task, tick).ConfigureAwait(false);
                            if (completed == faultSignal.Task)
                            {
                                onStatus(new SourceStatus(profile.Id, profile.Label, "Recovering", await faultSignal.Task.ConfigureAwait(false)));
                                break;
                            }
                            await tick.ConfigureAwait(false);
                            if (profile.FollowDefaultDevice
                                && capture is EndpointAudioCapture endpoint
                                && !string.Equals(
                                    endpoint.ActiveDeviceId,
                                    AudioDeviceService.ResolveDefaultDeviceId(profile.Kind),
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                onStatus(new SourceStatus(profile.Id, profile.Label, "Recovering", "The default audio device changed; reconnecting."));
                                break;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    onStatus(new SourceStatus(profile.Id, profile.Label, "Waiting", exception.Message));
                }
                finally
                {
                    if (capture is not null)
                    {
                        capture.AudioAvailable -= OnAudioAvailable;
                        capture.Faulted -= OnFaulted;
                        await capture.DisposeAsync().ConfigureAwait(false);
                        capture = null;
                    }
                    faultSignal = null;
                }

                try { await Task.Delay(1500, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
            onStatus(new SourceStatus(profile.Id, profile.Label, "Stopped"));
        }

        private void OnAudioAvailable(object? sender, AudioChunk chunk) => onAudio(chunk);
        private void OnFaulted(object? sender, string detail) =>
            faultSignal?.TrySetResult(detail);

        public async ValueTask DisposeAsync()
        {
            if (capture is not null)
            {
                await capture.DisposeAsync().ConfigureAwait(false);
                capture = null;
            }
        }
    }
}
