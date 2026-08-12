using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using LiveSubtitle.Core;

namespace LiveSubtitle.App.Services;

public sealed record ModelDownloadProgress(long DownloadedBytes, long? TotalBytes, string Stage);

public sealed class ModelDownloadService
{
    private static readonly HttpClient Http = CreateClient();
    private readonly AppPaths paths;

    public ModelDownloadService(AppPaths paths)
    {
        this.paths = paths;
    }

    public string GetInstalledPath(ModelDefinition model) => Path.Combine(paths.Models, model.FileName);

    public bool IsInstalled(ModelDefinition model) => File.Exists(GetInstalledPath(model));

    public async Task<bool> IsVerifiedAsync(ModelDefinition model, CancellationToken cancellationToken = default) =>
        await FileHashVerifier.MatchesSha256Async(GetInstalledPath(model), model.Sha256, cancellationToken)
            .ConfigureAwait(false);

    public async Task<string> DownloadAsync(
        ModelDefinition model,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        paths.EnsureCreated();
        var finalPath = GetInstalledPath(model);
        if (await FileHashVerifier.MatchesSha256Async(finalPath, model.Sha256, cancellationToken).ConfigureAwait(false))
        {
            progress?.Report(new ModelDownloadProgress(model.ApproximateBytes, model.ApproximateBytes, "Ready"));
            return finalPath;
        }

        if (File.Exists(finalPath))
        {
            File.Move(finalPath, $"{finalPath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: true);
        }

        var partialPath = finalPath + ".partial";
        var existing = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0L;
        using var request = new HttpRequestMessage(HttpMethod.Get, model.DownloadUri);
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }

        using var response = await Http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.OK && existing > 0)
        {
            existing = 0;
        }
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentRange?.Length
                    ?? response.Content.Headers.ContentLength + existing;
        var fileMode = existing > 0 ? FileMode.Append : FileMode.Create;
        await using var destination = new FileStream(
            partialPath,
            fileMode,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new byte[1024 * 1024];
        var downloaded = existing;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            downloaded += read;
            progress?.Report(new ModelDownloadProgress(downloaded, total, "Downloading"));
        }
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        destination.Close();

        progress?.Report(new ModelDownloadProgress(downloaded, total, "Verifying SHA-256"));
        if (!await FileHashVerifier.MatchesSha256Async(partialPath, model.Sha256, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("The downloaded model did not match its pinned SHA-256 hash.");
        }

        File.Move(partialPath, finalPath, overwrite: true);
        progress?.Report(new ModelDownloadProgress(downloaded, total, "Ready"));
        return finalPath;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LiveSubtitle/0.1");
        return client;
    }
}
