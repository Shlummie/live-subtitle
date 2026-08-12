using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime;
using System.Text;
using LiveSubtitle.Core;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace LiveSubtitle.App.Engine;

public sealed class EngineHost : IAsyncDisposable
{
    private const int HeapCompactionFinalInterval = 4;
    private readonly ConcurrentDictionary<string, SourceRecognitionState> sources = new();
    private readonly RecognitionScheduler scheduler = new();
    private readonly CancellationTokenSource cancellation = new();
    private NamedPipeClientStream? pipe;
    private PipeMessageCodec? codec;
    private WhisperFactory? factory;
    private WhisperProcessor? processor;
    private WhisperVadFactory? vadFactory;
    private WhisperVadProcessor? vadProcessor;
    private RecognitionProfile recognition = new();
    private string sessionId = Guid.NewGuid().ToString("N");
    private DateTimeOffset sessionStartedUtc;
    private long sequence;
    private int finalsSinceHeapCompaction;
    private Task? workerTask;
    private Task? tickerTask;

    public static async Task<int> RunAsync(string pipeName, CancellationToken cancellationToken = default)
    {
        await using var host = new EngineHost();
        try
        {
            await host.RunInternalAsync(pipeName, cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            try
            {
                if (host.codec is not null)
                {
                    await host.codec.WriteAsync(new EngineMessage
                    {
                        Kind = EngineMessageKind.Error,
                        Detail = $"{exception.GetType().Name}: {exception.Message}"
                    }, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch { }
            return 1;
        }
    }

    private async Task RunInternalAsync(string pipeName, CancellationToken externalCancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, externalCancellation);
        pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(15_000, linked.Token).ConfigureAwait(false);
        codec = new PipeMessageCodec(pipe);
        await codec.WriteAsync(new EngineMessage { Kind = EngineMessageKind.Hello, Detail = "Local engine" }, linked.Token)
            .ConfigureAwait(false);

        while (!linked.IsCancellationRequested)
        {
            var message = await codec.ReadAsync(linked.Token).ConfigureAwait(false);
            if (message is null || message.Kind == EngineMessageKind.Stop) break;
            switch (message.Kind)
            {
                case EngineMessageKind.LoadModel:
                    await LoadModelAsync(message, linked.Token).ConfigureAwait(false);
                    break;
                case EngineMessageKind.AudioFrame:
                    AcceptAudio(message);
                    break;
            }
        }
    }

    private async Task LoadModelAsync(EngineMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.ModelPath) || !File.Exists(message.ModelPath))
        {
            throw new FileNotFoundException("The selected speech model is not installed.", message.ModelPath);
        }

        recognition = message.Recognition ?? new RecognitionProfile();
        sessionId = message.RequestId ?? Guid.NewGuid().ToString("N");
        sessionStartedUtc = DateTimeOffset.UtcNow;
        RuntimeOptions.RuntimeLibraryOrder = message.Backend switch
        {
            InferenceBackend.Vulkan => [RuntimeLibrary.Vulkan],
            InferenceBackend.Cpu => [RuntimeLibrary.Cpu],
            _ => [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu]
        };

        factory = WhisperFactory.FromPath(message.ModelPath);
        var builder = factory.CreateBuilder()
            .WithLanguage("en")
            .WithThreads(Math.Clamp(Environment.ProcessorCount - 2, 4, 20))
            .WithSingleSegment()
            .WithNoContext()
            .WithTokenTimestamps()
            .WithProbabilities()
            .WithNoSpeechThreshold(Math.Clamp(recognition.VadThreshold, 0.05f, 0.95f));
        if (!string.IsNullOrWhiteSpace(recognition.CustomVocabulary))
        {
            builder.WithPrompt(recognition.CustomVocabulary.Trim()).WithCarryInitialPrompt(true);
        }
        processor = builder.Build();

        if (!string.IsNullOrWhiteSpace(message.VadModelPath) && File.Exists(message.VadModelPath))
        {
            vadFactory = WhisperVadFactory.FromPath(message.VadModelPath);
            vadProcessor = vadFactory.CreateBuilder()
                .WithThreads(4)
                .WithUseGpu(false)
                .WithThreshold(recognition.VadThreshold)
                .WithMinSpeechDuration(TimeSpan.FromMilliseconds(120))
                .WithMinSilenceDuration(TimeSpan.FromMilliseconds(recognition.FinalizeSilenceMs))
                .WithMaxSpeechDuration(TimeSpan.FromSeconds(recognition.MaxUtteranceSeconds))
                .WithSpeechPadding(TimeSpan.FromMilliseconds(80))
                .WithSamplesOverlap(TimeSpan.FromMilliseconds(recognition.WindowOverlapMs))
                .Build();
        }

        workerTask = Task.Run(() => WorkerLoopAsync(cancellation.Token));
        tickerTask = Task.Run(() => TickerLoopAsync(cancellation.Token));
        await codec!.WriteAsync(new EngineMessage
        {
            Kind = EngineMessageKind.Ready,
            Detail = $"{RuntimeOptions.LoadedLibrary ?? RuntimeLibrary.Cpu} backend ready"
        }, cancellationToken).ConfigureAwait(false);
    }

    private void AcceptAudio(EngineMessage message)
    {
        if (processor is null || message.Samples is not { Length: > 0 } || string.IsNullOrWhiteSpace(message.SourceId)) return;
        var state = sources.GetOrAdd(
            message.SourceId,
            sourceId => new SourceRecognitionState(
                sourceId,
                message.SourceLabel ?? "Audio",
                message.SourceColorHex ?? "#69A7FF",
                recognition));

        foreach (var job in state.Append(message.Samples, message.CapturedAtUtc == default ? DateTimeOffset.UtcNow : message.CapturedAtUtc))
        {
            scheduler.Enqueue(job);
        }
    }

    private async Task TickerLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var state in sources.Values)
            {
                var final = state.FinalizeIfIdle(now);
                if (final is not null) scheduler.Enqueue(final);
            }
        }
    }

    private async Task WorkerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var job = await scheduler.DequeueAsync(cancellationToken).ConfigureAwait(false);
            if (!job.IsFinal
                && sources.TryGetValue(job.SourceId, out var current)
                && job.Revision < current.LatestRevision)
            {
                continue;
            }

            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var samples = PrepareSamples(job.Samples, job.IsFinal);
            if (samples.Length == 0) continue;
            var text = await TranscribeAsync(samples, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (!sources.TryGetValue(job.SourceId, out var state)) continue;
            if (!job.IsFinal && job.Revision < state.LatestRevision)
            {
                continue;
            }
            var stabilized = state.Reconcile(text, job.IsFinal, job.EndsUtterance);
            var inferenceLatency = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var captureLatency = Math.Max(0, (DateTimeOffset.UtcNow - job.CapturedAtUtc).TotalMilliseconds);
            var latency = Math.Max(inferenceLatency, captureLatency);
            var caption = new CaptionEvent
            {
                SessionId = sessionId,
                SourceId = job.SourceId,
                SourceLabel = job.SourceLabel,
                SourceColorHex = job.SourceColorHex,
                Sequence = Interlocked.Increment(ref sequence),
                Start = job.StartedAtUtc - sessionStartedUtc,
                End = job.EndedAtUtc - sessionStartedUtc,
                Text = stabilized.FullText,
                StableText = stabilized.Stable,
                ProvisionalText = stabilized.Provisional,
                IsFinal = job.IsFinal,
                LatencyMs = latency,
                CapturedAtUtc = job.CapturedAtUtc
            };
            await codec!.WriteAsync(new EngineMessage { Kind = EngineMessageKind.Caption, Caption = caption }, cancellationToken)
                .ConfigureAwait(false);
            CompactReleasedAudioBuffersIfDue(job.IsFinal);
        }
    }

    private void CompactReleasedAudioBuffersIfDue(bool isFinal)
    {
        if (!isFinal || ++finalsSinceHeapCompaction < HeapCompactionFinalInterval) return;
        finalsSinceHeapCompaction = 0;
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private float[] PrepareSamples(float[] samples, bool isFinal)
    {
        if (isFinal && vadProcessor is not null)
        {
            try
            {
                var segments = vadProcessor.DetectSpeech(samples);
                if (segments.Count == 0) return [];
                var start = Math.Clamp((int)(segments[0].Start.TotalSeconds * AudioProcessing.TargetSampleRate), 0, samples.Length);
                var end = Math.Clamp((int)(segments[^1].End.TotalSeconds * AudioProcessing.TargetSampleRate), start, samples.Length);
                samples = samples[start..end];
            }
            catch
            {
                if (AudioProcessing.RootMeanSquare(samples) < recognition.NoiseFloor) return [];
            }
        }

        var minimumSamples = AudioProcessing.TargetSampleRate;
        if (samples.Length >= minimumSamples) return samples;
        var padded = new float[minimumSamples];
        samples.CopyTo(padded, 0);
        return padded;
    }

    private async Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        await foreach (var segment in processor!.ProcessAsync(samples, cancellationToken).ConfigureAwait(false))
        {
            text.Append(' ').Append(segment.Text);
        }
        return RecognizedTextFilter.Normalize(text.ToString());
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        if (tickerTask is not null) { try { await tickerTask.ConfigureAwait(false); } catch { } }
        if (workerTask is not null) { try { await workerTask.ConfigureAwait(false); } catch { } }
        if (vadProcessor is not null) await vadProcessor.DisposeAsync().ConfigureAwait(false);
        vadFactory?.Dispose();
        if (processor is not null) await processor.DisposeAsync().ConfigureAwait(false);
        factory?.Dispose();
        pipe?.Dispose();
        cancellation.Dispose();
    }

    private sealed class SourceRecognitionState
    {
        private readonly object gate = new();
        private readonly string sourceId;
        private readonly string sourceLabel;
        private readonly string sourceColor;
        private readonly RecognitionProfile profile;
        private readonly FloatRingBuffer buffer;
        private readonly AdaptiveVoiceActivityDetector vad;
        private readonly CaptionOverlapReconciler overlapReconciler = new();
        private readonly TranscriptStabilizer stabilizer = new();
        private DateTimeOffset windowStartedUtc;
        private DateTimeOffset lastAudioUtc;
        private DateTimeOffset lastDraftUtc;
        private bool hasUtterance;
        private long revision;

        public SourceRecognitionState(string sourceId, string sourceLabel, string sourceColor, RecognitionProfile profile)
        {
            this.sourceId = sourceId;
            this.sourceLabel = sourceLabel;
            this.sourceColor = sourceColor;
            this.profile = profile;
            buffer = new FloatRingBuffer(
                (profile.MaxUtteranceSeconds * 1000 + profile.WindowOverlapMs) *
                AudioProcessing.TargetSampleRate / 1000);
            vad = new AdaptiveVoiceActivityDetector(profile.FinalizeSilenceMs, profile.NoiseFloor);
        }

        public long LatestRevision { get { lock (gate) return revision; } }

        public StabilizedTranscript Reconcile(string text, bool isFinal, bool endsUtterance)
        {
            lock (gate)
            {
                var trimmed = overlapReconciler.TrimCommittedOverlap(text);
                var result = stabilizer.Update(trimmed, isFinal);
                if (isFinal) overlapReconciler.CommitFinal(result.FullText);
                if (endsUtterance) overlapReconciler.Reset();
                return result;
            }
        }

        public IReadOnlyList<RecognitionJob> Append(float[] samples, DateTimeOffset capturedAtUtc)
        {
            lock (gate)
            {
                var jobs = new List<RecognitionJob>(2);
                var durationMs = Math.Max(1, samples.Length * 1000 / AudioProcessing.TargetSampleRate);
                var wasSpeech = vad.IsSpeech;
                vad.Process(samples, durationMs);
                if (!hasUtterance && !wasSpeech && vad.IsSpeech)
                {
                    hasUtterance = true;
                    windowStartedUtc = capturedAtUtc - TimeSpan.FromMilliseconds(durationMs);
                    lastDraftUtc = DateTimeOffset.MinValue;
                    buffer.Clear();
                }

                if (hasUtterance)
                {
                    buffer.Write(samples);
                    lastAudioUtc = capturedAtUtc;
                    if (vad.EndpointReached)
                    {
                        jobs.Add(CreateJob(capturedAtUtc, isFinal: true, endsUtterance: true));
                        ResetUtterance();
                    }
                    else if ((capturedAtUtc - windowStartedUtc).TotalSeconds >= profile.MaxUtteranceSeconds)
                    {
                        jobs.Add(CreateJob(capturedAtUtc, isFinal: true, endsUtterance: false));
                        RetainOverlap(capturedAtUtc);
                    }
                    else if ((capturedAtUtc - lastDraftUtc).TotalMilliseconds >= profile.DraftIntervalMs)
                    {
                        lastDraftUtc = capturedAtUtc;
                        jobs.Add(CreateJob(capturedAtUtc, isFinal: false));
                    }
                }
                return jobs;
            }
        }

        public RecognitionJob? FinalizeIfIdle(DateTimeOffset now)
        {
            lock (gate)
            {
                if (!hasUtterance || (now - lastAudioUtc).TotalMilliseconds < profile.FinalizeSilenceMs) return null;
                var job = CreateJob(lastAudioUtc, isFinal: true, endsUtterance: true);
                ResetUtterance();
                return job;
            }
        }

        private RecognitionJob CreateJob(DateTimeOffset end, bool isFinal, bool endsUtterance = false) => new(
            sourceId,
            sourceLabel,
            sourceColor,
            buffer.Snapshot(),
            lastAudioUtc == default ? end : lastAudioUtc,
            windowStartedUtc,
            end,
            ++revision,
            isFinal,
            endsUtterance);

        private void RetainOverlap(DateTimeOffset capturedAtUtc)
        {
            var snapshot = buffer.Snapshot();
            var overlapSamples = Math.Min(
                snapshot.Length,
                profile.WindowOverlapMs * AudioProcessing.TargetSampleRate / 1000);
            buffer.Clear();
            if (overlapSamples > 0) buffer.Write(snapshot.AsSpan(snapshot.Length - overlapSamples));
            windowStartedUtc = capturedAtUtc - TimeSpan.FromMilliseconds(profile.WindowOverlapMs);
            lastDraftUtc = capturedAtUtc;
        }

        private void ResetUtterance()
        {
            hasUtterance = false;
            vad.Reset();
            buffer.Clear();
            stabilizer.Reset();
        }
    }
}
