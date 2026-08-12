using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiveSubtitle.App.Audio;
using LiveSubtitle.App.Services;
using LiveSubtitle.Core;
using NAudio.Wave;

namespace LiveSubtitle.App.Engine;

public sealed record EngineSelfTestResult
{
    public bool Success { get; init; }
    public string Backend { get; init; } = string.Empty;
    public string ModelPath { get; init; } = string.Empty;
    public string AudioPath { get; init; } = string.Empty;
    public string ExpectedText { get; init; } = string.Empty;
    public string FinalText { get; init; } = string.Empty;
    public int DraftCount { get; init; }
    public int FinalCount { get; init; }
    public double? MedianDraftLatencyMs { get; init; }
    public double? DraftP95LatencyMs { get; init; }
    public double? FinalLatencyMs { get; init; }
    public double? WordErrorRate { get; init; }
    public bool DraftLatencyGatePassed { get; init; }
    public bool FinalLatencyGatePassed { get; init; }
    public bool AccuracyGatePassed { get; init; }
    public bool AcceptancePassed { get; init; }
    public double ElapsedMs { get; init; }
    public string? Error { get; init; }
}

public static class EngineSelfTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<int> RunAsync(
        string audioPath,
        string resultPath,
        string? expectedText,
        string? modelPath,
        InferenceBackend backend,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        EngineSelfTestResult result;
        try
        {
            var paths = new AppPaths();
            paths.EnsureCreated();
            var model = ModelCatalog.Get("base-en");
            modelPath ??= Path.Combine(paths.Models, model.FileName);
            if (!await FileHashVerifier.MatchesSha256Async(modelPath, model.Sha256, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException("The base.en model is missing or does not match its pinned SHA-256.");
            }

            var samples = ReadMono16Khz(audioPath);
            if (samples.Length == 0) throw new InvalidDataException("The self-test audio file contains no samples.");
            samples = AppendSilence(samples, TimeSpan.FromMilliseconds(900));

            var captions = new ConcurrentQueue<CaptionEvent>();
            string? engineFault = null;
            await using var client = new EngineClient();
            client.CaptionReceived += (_, caption) => captions.Enqueue(caption);
            client.Faulted += (_, fault) =>
            {
                engineFault = fault;
            };

            var profile = new RecognitionProfile
            {
                ModelId = model.Id,
                Backend = backend,
                Preset = RecognitionPreset.Aggressive,
                DraftIntervalMs = 250,
                FinalizeSilenceMs = 450,
                WindowOverlapMs = 500,
                MaxUtteranceSeconds = 15,
                VadThreshold = 0.50f,
                NoiseFloor = 0.004f
            };
            var vadPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Vad", "ggml-silero-v6.2.0.bin");
            var backendDetail = await client.LoadModelAsync(
                $"self-test-{Guid.NewGuid():N}",
                modelPath,
                vadPath,
                profile,
                cancellationToken).ConfigureAwait(false);

            await FeedAsync(client, samples, "warmup", cancellationToken).ConfigureAwait(false);
            await WaitForSettledAsync(captions, "warmup", () => engineFault, cancellationToken).ConfigureAwait(false);
            while (captions.TryDequeue(out _)) { }

            var measuredAt = Stopwatch.GetTimestamp();
            await FeedAsync(client, samples, "self-test", cancellationToken).ConfigureAwait(false);
            await WaitForSettledAsync(captions, "self-test", () => engineFault, cancellationToken).ConfigureAwait(false);
            var snapshot = captions
                .Where(item => item.SourceId == "self-test")
                .OrderBy(item => item.Sequence)
                .ToArray();
            var finals = snapshot.Where(item => item.IsFinal).ToArray();
            var finalText = string.Join(' ', finals.Select(item => item.Text));
            var drafts = snapshot.Where(item => !item.IsFinal).Select(item => item.LatencyMs).Order().ToArray();
            var median = drafts.Length == 0 ? null : (double?)drafts[drafts.Length / 2];
            var draftP95 = Percentile(drafts, 0.95);
            var finalP95 = Percentile(finals.Select(item => item.LatencyMs).Order().ToArray(), 0.95);
            var wer = string.IsNullOrWhiteSpace(expectedText)
                ? null
                : (double?)ComputeWordErrorRate(expectedText, finalText);
            var draftGate = median <= 500 && draftP95 <= 900;
            var finalGate = finalP95 <= 1_200;
            var accuracyGate = wer is null || wer <= 0.15;
            result = new EngineSelfTestResult
            {
                Success = string.IsNullOrWhiteSpace(engineFault) && !string.IsNullOrWhiteSpace(finalText),
                Backend = backendDetail,
                ModelPath = modelPath,
                AudioPath = Path.GetFullPath(audioPath),
                ExpectedText = expectedText ?? string.Empty,
                FinalText = finalText,
                DraftCount = snapshot.Count(item => !item.IsFinal),
                FinalCount = finals.Length,
                MedianDraftLatencyMs = median,
                DraftP95LatencyMs = draftP95,
                FinalLatencyMs = finalP95,
                WordErrorRate = wer,
                DraftLatencyGatePassed = draftGate,
                FinalLatencyGatePassed = finalGate,
                AccuracyGatePassed = accuracyGate,
                AcceptancePassed = draftGate && finalGate && accuracyGate,
                ElapsedMs = Stopwatch.GetElapsedTime(measuredAt).TotalMilliseconds,
                Error = engineFault
            };
        }
        catch (Exception exception)
        {
            result = new EngineSelfTestResult
            {
                Success = false,
                ModelPath = modelPath ?? string.Empty,
                AudioPath = Path.GetFullPath(audioPath),
                ExpectedText = expectedText ?? string.Empty,
                ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
                Error = $"{exception.GetType().Name}: {exception.Message}"
            };
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(resultPath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = resultPath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(result, JsonOptions), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, resultPath, overwrite: true);
        return result.Success ? 0 : 1;
    }

    private static float[] ReadMono16Khz(string path)
    {
        using var reader = new AudioFileReader(path);
        var samples = new List<float>((int)Math.Min(int.MaxValue, reader.Length / sizeof(float)));
        var buffer = new float[Math.Max(4096, reader.WaveFormat.SampleRate * reader.WaveFormat.Channels)];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            samples.AddRange(buffer.AsSpan(0, read).ToArray());
        }
        return AudioProcessing.ToMono16Khz(
            samples.ToArray(),
            reader.WaveFormat.SampleRate,
            reader.WaveFormat.Channels);
    }

    private static float[] AppendSilence(float[] samples, TimeSpan duration)
    {
        var silenceSamples = (int)Math.Round(duration.TotalSeconds * AudioProcessing.TargetSampleRate);
        var combined = new float[samples.Length + silenceSamples];
        samples.CopyTo(combined, 0);
        return combined;
    }

    private static async Task FeedAsync(
        EngineClient client,
        float[] samples,
        string sourceId,
        CancellationToken cancellationToken)
    {
        const int frameSamples = AudioProcessing.TargetSampleRate / 50;
        for (var offset = 0; offset < samples.Length; offset += frameSamples)
        {
            var length = Math.Min(frameSamples, samples.Length - offset);
            var frame = samples.AsSpan(offset, length).ToArray();
            client.SendAudio(new AudioChunk(
                sourceId,
                sourceId == "warmup" ? "Warm-up" : "Fixture",
                "#69A7FF",
                frame,
                DateTimeOffset.UtcNow,
                AudioProcessing.RootMeanSquare(frame)));
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WaitForSettledAsync(
        ConcurrentQueue<CaptionEvent> captions,
        string sourceId,
        Func<string?> readFault,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(45);
        var stableSince = DateTimeOffset.UtcNow;
        var previousCount = -1;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var fault = readFault();
            if (!string.IsNullOrWhiteSpace(fault)) throw new InvalidOperationException(fault);
            var snapshot = captions.Where(item => item.SourceId == sourceId).ToArray();
            if (snapshot.Length != previousCount)
            {
                previousCount = snapshot.Length;
                stableSince = DateTimeOffset.UtcNow;
            }
            if (snapshot.Any(item => item.IsFinal)
                && DateTimeOffset.UtcNow - stableSince >= TimeSpan.FromMilliseconds(1500))
            {
                return;
            }
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException($"The {sourceId} caption stream did not settle within 45 seconds.");
    }

    private static double? Percentile(double[] ordered, double percentile)
    {
        if (ordered.Length == 0) return null;
        var index = Math.Clamp((int)Math.Ceiling(ordered.Length * percentile) - 1, 0, ordered.Length - 1);
        return ordered[index];
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
