using System.Text.Json;
using LiveSubtitle.App.Services;
using LiveSubtitle.Core;

namespace LiveSubtitle.App.Engine;

public sealed record EngineRestartSelfTestResult
{
    public bool Success { get; init; }
    public int? PrimaryProcessId { get; init; }
    public int? FallbackProcessId { get; init; }
    public string PrimaryBackend { get; init; } = string.Empty;
    public string FallbackBackend { get; init; } = string.Empty;
    public bool CrashObserved { get; init; }
    public bool NewChildCreated { get; init; }
    public string? Error { get; init; }
}

public static class EngineRestartSelfTest
{
    public static async Task<int> RunAsync(string resultPath, CancellationToken cancellationToken = default)
    {
        EngineRestartSelfTestResult result;
        try
        {
            var paths = new AppPaths();
            paths.EnsureCreated();
            var model = ModelCatalog.Get("base-en");
            var modelPath = Path.Combine(paths.Models, model.FileName);
            if (!await FileHashVerifier.MatchesSha256Async(modelPath, model.Sha256, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException("The verified base.en model is required for engine restart acceptance.");
            }
            var vadPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Vad", "ggml-silero-v6.2.0.bin");
            var crash = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            int? primaryProcessId;
            string primaryBackend;
            await using (var primary = new EngineClient())
            {
                primary.Faulted += (_, detail) => crash.TrySetResult(detail);
                primaryBackend = await primary.LoadModelAsync(
                    "restart-primary",
                    modelPath,
                    vadPath,
                    new RecognitionProfile { Backend = InferenceBackend.Auto },
                    cancellationToken).ConfigureAwait(false);
                primaryProcessId = primary.ChildProcessId;
                if (!primary.TerminateChildForDiagnostics())
                {
                    throw new InvalidOperationException("The primary engine child was not running.");
                }
                await crash.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            }

            int? fallbackProcessId;
            string fallbackBackend;
            await using (var fallback = new EngineClient())
            {
                fallbackBackend = await fallback.LoadModelAsync(
                    "restart-fallback",
                    modelPath,
                    vadPath,
                    new RecognitionProfile { Backend = InferenceBackend.Cpu },
                    cancellationToken).ConfigureAwait(false);
                fallbackProcessId = fallback.ChildProcessId;
            }

            result = new EngineRestartSelfTestResult
            {
                Success = primaryProcessId is not null
                          && fallbackProcessId is not null
                          && primaryProcessId != fallbackProcessId
                          && fallbackBackend.Contains("Cpu", StringComparison.OrdinalIgnoreCase),
                PrimaryProcessId = primaryProcessId,
                FallbackProcessId = fallbackProcessId,
                PrimaryBackend = primaryBackend,
                FallbackBackend = fallbackBackend,
                CrashObserved = true,
                NewChildCreated = primaryProcessId != fallbackProcessId
            };
        }
        catch (Exception exception)
        {
            result = new EngineRestartSelfTestResult
            {
                Success = false,
                Error = $"{exception.GetType().Name}: {exception.Message}"
            };
        }

        var fullPath = Path.GetFullPath(resultPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = fullPath + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            CancellationToken.None).ConfigureAwait(false);
        File.Move(temporary, fullPath, overwrite: true);
        return result.Success ? 0 : 1;
    }
}
