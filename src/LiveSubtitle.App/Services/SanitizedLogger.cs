using System.Globalization;

namespace LiveSubtitle.App.Services;

public sealed class SanitizedLogger
{
    private readonly AppPaths paths;
    private readonly SemaphoreSlim gate = new(1, 1);

    public SanitizedLogger(AppPaths paths)
    {
        this.paths = paths;
    }

    public Task InfoAsync(string eventName, string detail = "") =>
        WriteAsync("INFO", eventName, detail);

    public Task WarningAsync(string eventName, string detail = "") =>
        WriteAsync("WARN", eventName, detail);

    public Task ErrorAsync(string eventName, Exception exception) =>
        WriteAsync("ERROR", eventName, $"{exception.GetType().Name}: {exception.Message}");

    private async Task WriteAsync(string level, string eventName, string detail)
    {
        paths.EnsureCreated();
        var safeEvent = Sanitize(eventName, 64);
        var safeDetail = Sanitize(detail, 384);
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.UtcNow:O}\t{level}\t{safeEvent}\t{safeDetail}{Environment.NewLine}");
        var file = Path.Combine(paths.Logs, $"livesubtitle-{DateTime.UtcNow:yyyy-MM-dd}.log");

        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(file, line).ConfigureAwait(false);
            Rotate();
        }
        finally
        {
            gate.Release();
        }
    }

    private void Rotate()
    {
        foreach (var file in Directory.EnumerateFiles(paths.Logs, "livesubtitle-*.log")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(7))
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    private static string Sanitize(string value, int maxLength)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength];
    }
}
