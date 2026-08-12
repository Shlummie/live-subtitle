using System.Diagnostics;
using System.Text.Json;
using LiveSubtitle.App.Audio;
using LiveSubtitle.App.Services;
using LiveSubtitle.Core;
using NAudio.Wave;

namespace LiveSubtitle.App.Engine;

public sealed record SoakSelfTestResult
{
    public bool Success { get; init; }
    public int WarmupCycles { get; init; }
    public double WarmupMinutes { get; init; }
    public double ElapsedMinutes { get; init; }
    public double DigitalSilenceMinutes { get; init; }
    public int SilenceCaptionCount { get; init; }
    public int SpeechCycles { get; init; }
    public int FinalCaptionCount { get; init; }
    public int LostFinalCaptionCount { get; init; }
    public long BaselineWorkingSetBytes { get; init; }
    public long PeakWorkingSetBytes { get; init; }
    public long FinalWorkingSetBytes { get; init; }
    public long PeakWorkingSetGrowthBytes { get; init; }
    public long FinalWorkingSetGrowthBytes { get; init; }
    public bool FinalWorkingSetGatePassed { get; init; }
    public bool PeakWorkingSetWithin50MiB { get; init; }
    public int RawAudioFilesWritten { get; init; }
    public bool TranscriptPersisted { get; init; }
    public int? EngineProcessId { get; init; }
    public string Backend { get; init; } = string.Empty;
    public string? Error { get; init; }
}

public static class SoakSelfTest
{
    public static async Task<int> RunAsync(
        string audioPath,
        string resultPath,
        string readyPath,
        TimeSpan duration,
        TimeSpan silenceDuration,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        SoakSelfTestResult result;
        try
        {
            var paths = new AppPaths();
            paths.EnsureCreated();
            var model = ModelCatalog.Get("base-en");
            var modelPath = Path.Combine(paths.Models, model.FileName);
            if (!await FileHashVerifier.MatchesSha256Async(modelPath, model.Sha256, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException("The verified base.en model is required for soak acceptance.");
            }

            var fixture = AppendSilence(ReadMono16Khz(audioPath), TimeSpan.FromMilliseconds(900));
            if (fixture.Length == 0) throw new InvalidDataException("The soak fixture contains no samples.");

            var rawAudioBefore = CountRawAudioFiles(paths.Root);
            var historyBefore = FileStamp.Read(paths.History);
            var captionCount = 0;
            var finalCount = 0;
            await using var engine = new EngineClient();
            string? engineFault = null;
            engine.CaptionReceived += (_, caption) =>
            {
                Interlocked.Increment(ref captionCount);
                if (caption.IsFinal) Interlocked.Increment(ref finalCount);
            };
            engine.Faulted += (_, detail) => engineFault = detail;
            var profile = new RecognitionProfile
            {
                Backend = InferenceBackend.Auto,
                DraftIntervalMs = 250,
                FinalizeSilenceMs = 450,
                WindowOverlapMs = 500,
                MaxUtteranceSeconds = 15,
                NoiseFloor = 0.004f
            };
            var vadPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Vad", "ggml-silero-v6.2.0.bin");
            var backend = await engine.LoadModelAsync(
                $"soak-{Guid.NewGuid():N}",
                modelPath,
                vadPath,
                profile,
                cancellationToken).ConfigureAwait(false);

            const int warmupCycles = 10;
            var warmupStarted = Stopwatch.StartNew();
            for (var warmup = 0; warmup < warmupCycles; warmup++)
            {
                await FeedRealtimeAsync(engine, fixture, "soak-warmup", cancellationToken).ConfigureAwait(false);
            }
            await WaitForFinalsAsync(() => Volatile.Read(ref finalCount), warmupCycles, () => engineFault, cancellationToken)
                .ConfigureAwait(false);
            await Task.Delay(3_000, cancellationToken).ConfigureAwait(false);
            var warmupMinutes = warmupStarted.Elapsed.TotalMinutes;
            Interlocked.Exchange(ref captionCount, 0);
            Interlocked.Exchange(ref finalCount, 0);

            var parent = Process.GetCurrentProcess();
            using var child = engine.ChildProcessId is int childId ? Process.GetProcessById(childId) : null;
            var baseline = ReadCombinedWorkingSet(parent, child);
            var peak = baseline;
            await WriteAtomicJsonAsync(readyPath, new
            {
                parentProcessId = Environment.ProcessId,
                engineProcessId = engine.ChildProcessId,
                backend,
                baselineWorkingSetBytes = baseline,
                startedAtUtc = DateTimeOffset.UtcNow,
                warmupCycles,
                warmupMinutes,
                durationSeconds = duration.TotalSeconds,
                silenceSeconds = silenceDuration.TotalSeconds
            }, cancellationToken).ConfigureAwait(false);

            var runStarted = Stopwatch.StartNew();
            var silenceFrames = (long)Math.Ceiling(silenceDuration.TotalMilliseconds / 20.0);
            var silenceFrame = new float[AudioProcessing.TargetSampleRate / 50];
            for (long index = 0; index < silenceFrames; index++)
            {
                engine.SendAudio(new AudioChunk(
                    "soak",
                    "Digital silence",
                    "#69A7FF",
                    silenceFrame,
                    DateTimeOffset.UtcNow,
                    0));
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                if (index % 50 == 0) peak = Math.Max(peak, ReadCombinedWorkingSet(parent, child));
                ThrowIfFaulted(engineFault);
            }
            var silenceCaptions = Volatile.Read(ref captionCount);

            var cycles = 0;
            while (runStarted.Elapsed < duration)
            {
                cycles++;
                await FeedRealtimeAsync(engine, fixture, "soak", cancellationToken, () =>
                {
                    peak = Math.Max(peak, ReadCombinedWorkingSet(parent, child));
                    ThrowIfFaulted(engineFault);
                }).ConfigureAwait(false);
            }

            await WaitForFinalsAsync(() => Volatile.Read(ref finalCount), cycles, () => engineFault, cancellationToken)
                .ConfigureAwait(false);
            await Task.Delay(1_500, cancellationToken).ConfigureAwait(false);
            peak = Math.Max(peak, ReadCombinedWorkingSet(parent, child));
            var finalWorkingSet = ReadCombinedWorkingSet(parent, child);
            var finals = Volatile.Read(ref finalCount);
            var rawAudioAfter = CountRawAudioFiles(paths.Root);
            var transcriptPersisted = historyBefore != FileStamp.Read(paths.History);
            var peakGrowth = Math.Max(0, peak - baseline);
            var finalGrowth = Math.Max(0, finalWorkingSet - baseline);
            var finalWorkingSetGatePassed = finalGrowth <= 50L * 1024 * 1024;
            var peakWorkingSetWithin50MiB = peakGrowth <= 50L * 1024 * 1024;
            result = new SoakSelfTestResult
            {
                Success = silenceCaptions == 0
                          && finals == cycles
                          && finalWorkingSetGatePassed
                          && rawAudioAfter == rawAudioBefore
                          && !transcriptPersisted
                          && string.IsNullOrWhiteSpace(engineFault),
                WarmupCycles = warmupCycles,
                WarmupMinutes = warmupMinutes,
                ElapsedMinutes = runStarted.Elapsed.TotalMinutes,
                DigitalSilenceMinutes = silenceDuration.TotalMinutes,
                SilenceCaptionCount = silenceCaptions,
                SpeechCycles = cycles,
                FinalCaptionCount = finals,
                LostFinalCaptionCount = Math.Max(0, cycles - finals),
                BaselineWorkingSetBytes = baseline,
                PeakWorkingSetBytes = peak,
                FinalWorkingSetBytes = finalWorkingSet,
                PeakWorkingSetGrowthBytes = peakGrowth,
                FinalWorkingSetGrowthBytes = finalGrowth,
                FinalWorkingSetGatePassed = finalWorkingSetGatePassed,
                PeakWorkingSetWithin50MiB = peakWorkingSetWithin50MiB,
                RawAudioFilesWritten = Math.Max(0, rawAudioAfter - rawAudioBefore),
                TranscriptPersisted = transcriptPersisted,
                EngineProcessId = engine.ChildProcessId,
                Backend = backend,
                Error = engineFault
            };
        }
        catch (Exception exception)
        {
            result = new SoakSelfTestResult
            {
                Success = false,
                ElapsedMinutes = started.Elapsed.TotalMinutes,
                Error = $"{exception.GetType().Name}: {exception.Message}"
            };
        }

        await WriteAtomicJsonAsync(resultPath, result, CancellationToken.None).ConfigureAwait(false);
        return result.Success ? 0 : 1;
    }

    private static async Task FeedRealtimeAsync(
        EngineClient engine,
        float[] samples,
        string sourceId,
        CancellationToken cancellationToken,
        Action? onSecond = null)
    {
        const int frameSamples = AudioProcessing.TargetSampleRate / 50;
        var frameIndex = 0;
        for (var offset = 0; offset < samples.Length; offset += frameSamples)
        {
            var length = Math.Min(frameSamples, samples.Length - offset);
            var frame = samples.AsSpan(offset, length).ToArray();
            engine.SendAudio(new AudioChunk(
                sourceId,
                "Soak fixture",
                "#69A7FF",
                frame,
                DateTimeOffset.UtcNow,
                AudioProcessing.RootMeanSquare(frame)));
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            if (++frameIndex % 50 == 0) onSecond?.Invoke();
        }
    }

    private static async Task WaitForFinalsAsync(
        Func<int> readFinals,
        int expected,
        Func<string?> readFault,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ThrowIfFaulted(readFault());
            if (readFinals() >= expected) return;
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException($"Only {readFinals()} of {expected} final captions arrived.");
    }

    private static void ThrowIfFaulted(string? detail)
    {
        if (!string.IsNullOrWhiteSpace(detail)) throw new InvalidOperationException(detail);
    }

    private static float[] ReadMono16Khz(string path)
    {
        using var reader = new AudioFileReader(path);
        var samples = new List<float>();
        var buffer = new float[Math.Max(4096, reader.WaveFormat.SampleRate * reader.WaveFormat.Channels)];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            samples.AddRange(buffer.AsSpan(0, read).ToArray());
        }
        return AudioProcessing.ToMono16Khz(samples.ToArray(), reader.WaveFormat.SampleRate, reader.WaveFormat.Channels);
    }

    private static float[] AppendSilence(float[] samples, TimeSpan duration)
    {
        var combined = new float[samples.Length + (int)(duration.TotalSeconds * AudioProcessing.TargetSampleRate)];
        samples.CopyTo(combined, 0);
        return combined;
    }

    private static long ReadCombinedWorkingSet(Process parent, Process? child)
    {
        parent.Refresh();
        var value = parent.WorkingSet64;
        if (child is null || child.HasExited) return value;
        child.Refresh();
        return value + child.WorkingSet64;
    }

    private static int CountRawAudioFiles(string root) => Directory.Exists(root)
        ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count(path =>
            Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".pcm", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".raw", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".flac", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
        : 0;

    private static async Task WriteAtomicJsonAsync(string path, object value, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = fullPath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), cancellationToken)
            .ConfigureAwait(false);
        File.Move(temporary, fullPath, overwrite: true);
    }

    private readonly record struct FileStamp(bool Exists, long Length, DateTime LastWriteUtc)
    {
        public static FileStamp Read(string path)
        {
            if (!File.Exists(path)) return default;
            var file = new FileInfo(path);
            return new FileStamp(true, file.Length, file.LastWriteTimeUtc);
        }
    }
}
