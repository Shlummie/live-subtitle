using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveSubtitle.Core;

public static class AppSettingsSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(AppSettings settings) =>
        JsonSerializer.Serialize(AppSettingsValidator.Validate(settings), Options);

    public static AppSettings Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return AppSettings.CreateDefault();
        }

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var version = document.RootElement.TryGetProperty("schemaVersion", out var schemaElement)
            ? schemaElement.GetInt32()
            : 0;

        if (version > AppSettings.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Settings schema {version} is newer than this version of LiveSubtitle supports.");
        }

        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options)
            ?? AppSettings.CreateDefault();

        return AppSettingsValidator.Validate(Migrate(settings, version));
    }

    private static AppSettings Migrate(AppSettings settings, int sourceVersion) => sourceVersion switch
    {
        <= 0 => settings with { SchemaVersion = AppSettings.CurrentSchemaVersion },
        _ => settings
    };
}

public static class AppSettingsValidator
{
    public static AppSettings Validate(AppSettings settings)
    {
        var profiles = settings.Profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Id))
            .GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => Validate(group.First()))
            .ToList();

        if (profiles.Count == 0)
        {
            profiles.AddRange(AppSettings.CreateDefault().Profiles);
        }

        var activeId = profiles.Any(profile => profile.Id == settings.ActiveProfileId)
            ? settings.ActiveProfileId
            : profiles[0].Id;

        return settings with
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            ActiveProfileId = activeId,
            Profiles = profiles
        };
    }

    private static UserProfile Validate(UserProfile profile)
    {
        var recognition = profile.Recognition with
        {
            DraftIntervalMs = Math.Clamp(profile.Recognition.DraftIntervalMs, 150, 3000),
            FinalizeSilenceMs = Math.Clamp(profile.Recognition.FinalizeSilenceMs, 250, 3000),
            WindowOverlapMs = Math.Clamp(profile.Recognition.WindowOverlapMs, 0, 2500),
            MaxUtteranceSeconds = Math.Clamp(profile.Recognition.MaxUtteranceSeconds, 3, 30),
            VadThreshold = Math.Clamp(profile.Recognition.VadThreshold, 0.05f, 0.95f),
            NoiseFloor = Math.Clamp(profile.Recognition.NoiseFloor, 0.0005f, 0.25f)
        };

        var overlay = profile.Overlay with
        {
            Width = Math.Clamp(profile.Overlay.Width, 320, 2400),
            MaxLines = Math.Clamp(profile.Overlay.MaxLines, 1, 8),
            FontSize = Math.Clamp(profile.Overlay.FontSize, 12, 96),
            FontWeight = Math.Clamp(profile.Overlay.FontWeight, 100, 900),
            BackgroundOpacity = Math.Clamp(profile.Overlay.BackgroundOpacity, 0, 1),
            CornerRadius = Math.Clamp(profile.Overlay.CornerRadius, 0, 40),
            OutlineThickness = Math.Clamp(profile.Overlay.OutlineThickness, 0, 8),
            ShadowOpacity = Math.Clamp(profile.Overlay.ShadowOpacity, 0, 1),
            ShadowBlurRadius = Math.Clamp(profile.Overlay.ShadowBlurRadius, 0, 80),
            ShadowDepth = Math.Clamp(profile.Overlay.ShadowDepth, 0, 30),
            X = Math.Clamp(profile.Overlay.X, 0, 1),
            Y = Math.Clamp(profile.Overlay.Y, 0, 1)
        };

        var sources = profile.AudioSources
            .Where(source => !string.IsNullOrWhiteSpace(source.Id))
            .Select(source => source with
            {
                Label = string.IsNullOrWhiteSpace(source.Label) ? "Audio" : source.Label.Trim(),
                Gain = Math.Clamp(source.Gain, 0, 4)
            })
            .ToList();

        return profile with
        {
            Name = string.IsNullOrWhiteSpace(profile.Name) ? "Profile" : profile.Name.Trim(),
            AudioSources = sources,
            Recognition = recognition,
            Overlay = overlay
        };
    }
}
