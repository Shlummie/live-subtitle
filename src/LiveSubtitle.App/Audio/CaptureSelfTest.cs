using System.Collections.Concurrent;
using System.Text.Json;
using LiveSubtitle.App.Engine;
using LiveSubtitle.App.Services;
using LiveSubtitle.Core;

namespace LiveSubtitle.App.Audio;

public sealed record CaptureSelfTestResult
{
    public bool Success { get; init; }
    public AudioSourceKind SourceKind { get; init; }
    public string? ProcessPath { get; init; }
    public int CapturedChunks { get; init; }
    public int NonSilentChunks { get; init; }
    public long CapturedSamples { get; init; }
    public float MaximumLevel { get; init; }
    public int DraftCount { get; init; }
    public int FinalCount { get; init; }
    public int ListeningTransitions { get; init; }
    public int TranscriptWordCount { get; init; }
    public bool TranscriptPersisted { get; init; }
    public double? WordErrorRate { get; init; }
    public bool ForbiddenTextDetected { get; init; }
    public IReadOnlyList<string> Statuses { get; init; } = [];
    public int RawAudioFilesWritten { get; init; }
    public string? Error { get; init; }
}

public static class CaptureSelfTest
{
    public static async Task<int> RunAsync(
        AudioSourceKind sourceKind,
        string? processPath,
        string resultPath,
        string readyPath,
        TimeSpan duration,
        string? expectedText,
        string? forbiddenText,
        CancellationToken cancellationToken = default)
    {
        var statuses = new ConcurrentQueue<string>();
        var captions = new ConcurrentQueue<CaptionEvent>();
        CaptureSelfTestResult result;
        try
        {
            var paths = new AppPaths();
            paths.EnsureCreated();
            var model = ModelCatalog.Get("base-en");
            var modelPath = Path.Combine(paths.Models, model.FileName);
            if (!await FileHashVerifier.MatchesSha256Async(modelPath, model.Sha256, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException("The verified base.en model is required for capture acceptance.");
            }

            var profile = new AudioSourceProfile
            {
                Id = "capture-self-test",
                Kind = sourceKind,
                FollowDefaultDevice = sourceKind != AudioSourceKind.Process,
                ProcessExecutablePath = processPath,
                ProcessName = processPath is null ? null : Path.GetFileNameWithoutExtension(processPath),
                Label = sourceKind == AudioSourceKind.Process ? "Selected application" : sourceKind == AudioSourceKind.InputDevice ? "Default input" : "Computer audio",
                ColorHex = "#69A7FF",
                Enabled = true
            };

            await using var engine = new EngineClient();
            string? engineFault = null;
            engine.CaptionReceived += (_, caption) => captions.Enqueue(caption);
            engine.Faulted += (_, detail) => engineFault = detail;
            var recognition = new RecognitionProfile
            {
                ModelId = model.Id,
                Backend = InferenceBackend.Auto,
                DraftIntervalMs = 250,
                FinalizeSilenceMs = 450,
                WindowOverlapMs = 500,
                MaxUtteranceSeconds = 15,
                NoiseFloor = 0.004f
            };
            var vadPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Vad", "ggml-silero-v6.2.0.bin");
            await engine.LoadModelAsync(
                $"capture-self-test-{Guid.NewGuid():N}",
                modelPath,
                vadPath,
                recognition,
                cancellationToken).ConfigureAwait(false);

            var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var captureFault = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var chunks = 0;
            var nonSilent = 0;
            long sampleCount = 0;
            float maximumLevel = 0;
            await using var coordinator = new AudioCaptureCoordinator();
            coordinator.StatusChanged += (_, status) =>
            {
                statuses.Enqueue($"{status.State}: {status.Detail}".TrimEnd(' ', ':'));
                if (status.State == "Listening") listening.TrySetResult();
                if (status.State is "Waiting" or "Recovering" && !string.IsNullOrWhiteSpace(status.Detail))
                {
                    captureFault.TrySetResult(status.Detail);
                }
            };
            coordinator.MeterUpdated += (_, chunk) =>
            {
                Interlocked.Increment(ref chunks);
                Interlocked.Add(ref sampleCount, chunk.Samples.Length);
                if (chunk.Level > 0.003f) Interlocked.Increment(ref nonSilent);
                lock (statuses) maximumLevel = Math.Max(maximumLevel, chunk.Level);
            };

            using var readerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var readerTask = Task.Run(async () =>
            {
                await foreach (var chunk in coordinator.ReadAllAsync(readerCancellation.Token).ConfigureAwait(false))
                {
                    engine.SendAudio(chunk);
                }
            }, readerCancellation.Token);

            await coordinator.StartAsync([profile], cancellationToken).ConfigureAwait(false);
            var completed = await Task.WhenAny(
                listening.Task,
                captureFault.Task,
                Task.Delay(TimeSpan.FromSeconds(15), cancellationToken)).ConfigureAwait(false);
            if (completed == captureFault.Task) throw new InvalidOperationException(await captureFault.Task.ConfigureAwait(false));
            if (completed != listening.Task) throw new TimeoutException("The selected source did not enter Listening state.");

            await WriteAtomicJsonAsync(readyPath, new
            {
                sourceKind,
                processPath,
                readyAtUtc = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);

            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
            await coordinator.StopAsync().ConfigureAwait(false);
            await Task.Delay(750, cancellationToken).ConfigureAwait(false);
            readerCancellation.Cancel();
            try { await readerTask.ConfigureAwait(false); } catch (OperationCanceledException) { }

            var requiresSpeech = sourceKind != AudioSourceKind.InputDevice || !string.IsNullOrWhiteSpace(expectedText);
            if (requiresSpeech)
            {
                await WaitForCaptionsToSettleAsync(captions, () => engineFault, cancellationToken).ConfigureAwait(false);
            }
            var snapshot = captions.OrderBy(item => item.Sequence).ToArray();
            var finals = snapshot.Where(item => item.IsFinal).ToArray();
            var finalText = string.Join(' ', finals.Select(item => item.Text));
            var wordErrorRate = string.IsNullOrWhiteSpace(expectedText)
                ? null
                : (double?)ComputeWordErrorRate(expectedText, finalText);
            var forbiddenDetected = !string.IsNullOrWhiteSpace(forbiddenText)
                                    && ContainsDistinctiveWords(finalText, forbiddenText);
            result = new CaptureSelfTestResult
            {
                Success = chunks > 0
                          && (!requiresSpeech || nonSilent > 0)
                          && string.IsNullOrWhiteSpace(engineFault)
                          && (!string.IsNullOrWhiteSpace(expectedText) ? finals.Length > 0 && wordErrorRate <= 0.35 : true)
                          && !forbiddenDetected,
                SourceKind = sourceKind,
                ProcessPath = processPath,
                CapturedChunks = chunks,
                NonSilentChunks = nonSilent,
                CapturedSamples = sampleCount,
                MaximumLevel = maximumLevel,
                DraftCount = snapshot.Count(item => !item.IsFinal),
                FinalCount = finals.Length,
                ListeningTransitions = statuses.Count(status => status == "Listening"),
                TranscriptWordCount = NormalizeWords(finalText).Length,
                TranscriptPersisted = false,
                WordErrorRate = wordErrorRate,
                ForbiddenTextDetected = forbiddenDetected,
                Statuses = statuses.ToArray(),
                RawAudioFilesWritten = 0,
                Error = engineFault
            };
        }
        catch (Exception exception)
        {
            result = new CaptureSelfTestResult
            {
                Success = false,
                SourceKind = sourceKind,
                ProcessPath = processPath,
                Statuses = statuses.ToArray(),
                RawAudioFilesWritten = 0,
                Error = $"{exception.GetType().Name}: {exception.Message}"
            };
        }

        await WriteAtomicJsonAsync(resultPath, result, CancellationToken.None).ConfigureAwait(false);
        return result.Success ? 0 : 1;
    }

    private static async Task WaitForCaptionsToSettleAsync(
        ConcurrentQueue<CaptionEvent> captions,
        Func<string?> readFault,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(35);
        var stableSince = DateTimeOffset.UtcNow;
        var previousCount = -1;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var fault = readFault();
            if (!string.IsNullOrWhiteSpace(fault)) throw new InvalidOperationException(fault);
            var count = captions.Count;
            if (count != previousCount)
            {
                previousCount = count;
                stableSince = DateTimeOffset.UtcNow;
            }
            if (captions.Any(item => item.IsFinal)
                && DateTimeOffset.UtcNow - stableSince >= TimeSpan.FromMilliseconds(1500)) return;
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("Captured speech did not produce a final caption within 35 seconds.");
    }

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

    private static bool ContainsDistinctiveWords(string actual, string forbidden)
    {
        var actualWords = NormalizeWords(actual).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var forbiddenWords = NormalizeWords(forbidden).Where(word => word.Length >= 5).ToArray();
        return forbiddenWords.Length > 0 && forbiddenWords.Count(actualWords.Contains) >= Math.Min(2, forbiddenWords.Length);
    }

    private static double ComputeWordErrorRate(string expected, string actual)
    {
        var reference = NormalizeWords(expected);
        var hypothesis = NormalizeWords(actual);
        if (reference.Length == 0) return hypothesis.Length == 0 ? 0 : 1;
        var previous = Enumerable.Range(0, hypothesis.Length + 1).ToArray();
        for (var row = 1; row <= reference.Length; row++)
        {
            var current = new int[hypothesis.Length + 1];
            current[0] = row;
            for (var column = 1; column <= hypothesis.Length; column++)
            {
                var substitution = previous[column - 1] + (reference[row - 1] == hypothesis[column - 1] ? 0 : 1);
                current[column] = Math.Min(Math.Min(previous[column] + 1, current[column - 1] + 1), substitution);
            }
            previous = current;
        }
        return previous[^1] / (double)reference.Length;
    }

    private static string[] NormalizeWords(string value)
    {
        var normalized = new string(value.ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character == '\'' ? character : ' ')
            .ToArray());
        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
