using LiveSubtitle.Core;

namespace LiveSubtitle.App.Services;

public sealed class SettingsService
{
    private readonly AppPaths paths;

    public SettingsService(AppPaths paths)
    {
        this.paths = paths;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureCreated();
        if (!File.Exists(paths.Settings))
        {
            return AppSettings.CreateDefault();
        }

        try
        {
            var json = await File.ReadAllTextAsync(paths.Settings, cancellationToken).ConfigureAwait(false);
            return AppSettingsSerializer.Deserialize(json);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            var backup = $"{paths.Settings}.invalid-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            File.Copy(paths.Settings, backup, overwrite: false);
            return AppSettings.CreateDefault();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        paths.EnsureCreated();
        var temporary = $"{paths.Settings}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(
            temporary,
            AppSettingsSerializer.Serialize(settings),
            cancellationToken).ConfigureAwait(false);
        File.Move(temporary, paths.Settings, overwrite: true);
    }

    public async Task ExportAsync(AppSettings settings, string destination, CancellationToken cancellationToken = default) =>
        await File.WriteAllTextAsync(destination, AppSettingsSerializer.Serialize(settings), cancellationToken)
            .ConfigureAwait(false);

    public async Task<AppSettings> ImportAsync(string source, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(source, cancellationToken).ConfigureAwait(false);
        return AppSettingsSerializer.Deserialize(json);
    }
}
