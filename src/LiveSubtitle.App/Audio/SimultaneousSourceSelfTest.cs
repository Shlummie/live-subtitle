using System.Collections.Concurrent;
using System.Text.Json;
using LiveSubtitle.App.Engine;
using LiveSubtitle.App.Services;
using LiveSubtitle.Core;

namespace LiveSubtitle.App.Audio;

public sealed record SimultaneousSourceSelfTestResult
{
    public bool Success { get; init; }
    public string ProcessA { get; init; } = string.Empty;
    public string ProcessB { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, int> CapturedChunksBySource { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> NonSilentChunksBySource { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> FinalCaptionsBySource { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> FinalWordsBySource { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<string> CaptionLabels { get; init; } = [];
    public int RawAudioFilesWritten { get; init; }
    public bool TranscriptPersisted { get; init; }
    public string Backend { get; init; } = string.Empty;
    public string? Error { get; init; }
}

public static class SimultaneousSourceSelfTest
{
    public static async Task<int> RunAsync(
        string processA,
        string processB,
        string resultPath,
        string readyPath,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        SimultaneousSourceSelfTestResult result;
        var chunkCounts = new ConcurrentDictionary<string, int>();
        var nonSilentCounts = new ConcurrentDictionary<string, int>();
        var captions = new ConcurrentQueue<CaptionEvent>();
        try
        {
            if (!File.Exists(processA) || !File.Exists(processB))
            {
                throw new FileNotFoundException("Both fixture-player executables are required.");
            }

            var paths = new AppPaths();
            paths.EnsureCreated();
            var model = ModelCatalog.Get("base-en");
            var modelPath = Path.Combine(paths.Models, model.FileName);
            if (!await FileHashVerifier.MatchesSha256Async(modelPath, model.Sha256, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException("The verified base.en model is required for simultaneous-source acceptance.");
            }

            var rawAudioBefore = CountRawAudioFiles(paths.Root);
            var historyBefore = FileStamp.Read(paths.History);
            string? engineFault = null;
            await using var engine = new EngineClient();
            engine.CaptionReceived += (_, caption) => captions.Enqueue(caption);
            engine.Faulted += (_, detail) => engineFault = detail;
            var recognition = new RecognitionProfile
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
                $"simultaneous-{Guid.NewGuid():N}",
                modelPath,
                vadPath,
                recognition,
                cancellationToken).ConfigureAwait(false);

            var profiles = new[]
            {
                ProcessProfile("source-a", "Application A", "#69A7FF", processA),
                ProcessProfile("source-b", "Application B", "#72D7A0", processB)
            };
            var listening = new ConcurrentDictionary<string, byte>();
            await using var coordinator = new AudioCaptureCoordinator();
            coordinator.StatusChanged += (_, status) =>
            {
                if (status.State == "Listening") listening[status.SourceId] = 0;
            };
            coordinator.MeterUpdated += (_, chunk) =>
            {
                chunkCounts.AddOrUpdate(chunk.SourceId, 1, (_, count) => count + 1);
                if (chunk.Level > 0.003f) nonSilentCounts.AddOrUpdate(chunk.SourceId, 1, (_, count) => count + 1);
            };

            using var readerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var readerTask = Task.Run(async () =>
            {
                await foreach (var chunk in coordinator.ReadAllAsync(readerCancellation.Token).ConfigureAwait(false))
                {
                    engine.SendAudio(chunk);
                }
            }, readerCancellation.Token);

            await coordinator.StartAsync(profiles, cancellationToken).ConfigureAwait(false);
            await WriteAtomicJsonAsync(readyPath, new
            {
                processA,
                processB,
                readyAtUtc = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);

            var listeningDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(25);
            while (listening.Count < profiles.Length && DateTimeOffset.UtcNow < listeningDeadline)
            {
                ThrowIfFaulted(engineFault);
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            if (listening.Count < profiles.Length) throw new TimeoutException("Both application sources did not enter Listening state.");

            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
            await coordinator.StopAsync().ConfigureAwait(false);
            var finalDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(45);
            while (captions.Where(caption => caption.IsFinal).Select(caption => caption.SourceId).Distinct().Count() < 2
                   && DateTimeOffset.UtcNow < finalDeadline)
            {
                ThrowIfFaulted(engineFault);
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            readerCancellation.Cancel();
            try { await readerTask.ConfigureAwait(false); } catch (OperationCanceledException) { }

            var finalCaptions = captions.Where(caption => caption.IsFinal).OrderBy(caption => caption.Sequence).ToArray();
            var finalsBySource = finalCaptions
                .GroupBy(caption => caption.SourceId)
                .ToDictionary(group => group.Key, group => group.Count());
            var wordsBySource = finalCaptions
                .GroupBy(caption => caption.SourceId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(caption => caption.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length));
            var labels = finalCaptions.Select(caption => caption.SourceLabel).Distinct().Order().ToArray();
            var rawAudioAfter = CountRawAudioFiles(paths.Root);
            var transcriptPersisted = historyBefore != FileStamp.Read(paths.History);
            result = new SimultaneousSourceSelfTestResult
            {
                Success = profiles.All(profile => chunkCounts.GetValueOrDefault(profile.Id) > 0)
                          && profiles.All(profile => nonSilentCounts.GetValueOrDefault(profile.Id) > 0)
                          && profiles.All(profile => finalsBySource.GetValueOrDefault(profile.Id) > 0)
                          && labels.SequenceEqual(["Application A", "Application B"])
                          && rawAudioAfter == rawAudioBefore
                          && !transcriptPersisted
                          && string.IsNullOrWhiteSpace(engineFault),
                ProcessA = processA,
                ProcessB = processB,
                CapturedChunksBySource = chunkCounts.ToDictionary(),
                NonSilentChunksBySource = nonSilentCounts.ToDictionary(),
                FinalCaptionsBySource = finalsBySource,
                FinalWordsBySource = wordsBySource,
                CaptionLabels = labels,
                RawAudioFilesWritten = Math.Max(0, rawAudioAfter - rawAudioBefore),
                TranscriptPersisted = transcriptPersisted,
                Backend = backend,
                Error = engineFault
            };
        }
        catch (Exception exception)
        {
            result = new SimultaneousSourceSelfTestResult
            {
                ProcessA = processA,
                ProcessB = processB,
                CapturedChunksBySource = chunkCounts.ToDictionary(),
                NonSilentChunksBySource = nonSilentCounts.ToDictionary(),
                Error = $"{exception.GetType().Name}: {exception.Message}"
            };
        }

        await WriteAtomicJsonAsync(resultPath, result, CancellationToken.None).ConfigureAwait(false);
        return result.Success ? 0 : 1;
    }

    private static AudioSourceProfile ProcessProfile(string id, string label, string color, string path) => new()
    {
        Id = id,
        Kind = AudioSourceKind.Process,
        FollowDefaultDevice = false,
        ProcessExecutablePath = Path.GetFullPath(path),
        ProcessName = Path.GetFileNameWithoutExtension(path),
        Label = label,
        ColorHex = color
    };

    private static void ThrowIfFaulted(string? detail)
    {
        if (!string.IsNullOrWhiteSpace(detail)) throw new InvalidOperationException(detail);
    }

    private static int CountRawAudioFiles(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Count(path => Path.GetExtension(path).ToLowerInvariant() is ".wav" or ".pcm" or ".raw" or ".flac" or ".mp3");

    private static async Task WriteAtomicJsonAsync(string path, object value, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken).ConfigureAwait(false);
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
