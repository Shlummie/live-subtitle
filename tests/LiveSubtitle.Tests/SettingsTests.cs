using LiveSubtitle.Core;

namespace LiveSubtitle.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void RoundTrip_PreservesProfilesAndSchema()
    {
        var original = AppSettings.CreateDefault();
        var json = AppSettingsSerializer.Serialize(original);

        var restored = AppSettingsSerializer.Deserialize(json);

        Assert.Equal(AppSettings.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Equal(original.Profiles.Count, restored.Profiles.Count);
        Assert.Equal("default", restored.ActiveProfileId);
    }

    [Fact]
    public void Validation_ClampsUnsafeValues()
    {
        var settings = AppSettings.CreateDefault();
        var profile = settings.Profiles[0] with
        {
            Recognition = settings.Profiles[0].Recognition with
            {
                DraftIntervalMs = 1,
                MaxUtteranceSeconds = 1000
            },
            Overlay = settings.Profiles[0].Overlay with
            {
                X = -2,
                ShadowOpacity = 4,
                ShadowBlurRadius = 900
            }
        };

        var validated = AppSettingsValidator.Validate(settings with { Profiles = [profile] });

        Assert.Equal(150, validated.Profiles[0].Recognition.DraftIntervalMs);
        Assert.Equal(30, validated.Profiles[0].Recognition.MaxUtteranceSeconds);
        Assert.Equal(0, validated.Profiles[0].Overlay.X);
        Assert.Equal(1, validated.Profiles[0].Overlay.ShadowOpacity);
        Assert.Equal(80, validated.Profiles[0].Overlay.ShadowBlurRadius);
    }

    [Fact]
    public void FutureSchema_IsRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            AppSettingsSerializer.Deserialize("{\"schemaVersion\":999}"));
    }

    [Fact]
    public void LegacySettingsWithoutVersion_AreMigrated()
    {
        const string legacy = """
            {
              "activeProfileId": "legacy",
              "profiles": [
                { "id": "legacy", "name": "Legacy profile" }
              ]
            }
            """;

        var migrated = AppSettingsSerializer.Deserialize(legacy);

        Assert.Equal(AppSettings.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Equal("legacy", migrated.ActiveProfileId);
        Assert.Equal("Legacy profile", migrated.Profiles.Single().Name);
    }

    [Fact]
    public void ProfileExportRoundTrip_PreservesCustomRoutingAndAppearance()
    {
        var custom = new UserProfile
        {
            Id = "custom",
            Name = "My games",
            AudioSources =
            [
                new AudioSourceProfile
                {
                    Id = "process",
                    Kind = AudioSourceKind.Process,
                    ProcessExecutablePath = @"C:\Games\Example.exe",
                    Label = "Game",
                    ColorHex = "#FF8844"
                }
            ],
            Overlay = new OverlayProfile
            {
                MonitorId = @"\\.\DISPLAY2",
                Anchor = OverlayAnchor.Custom,
                X = 0.72,
                Y = 0.81,
                Width = 1110,
                MaxLines = 4,
                FontSize = 38,
                ShadowOpacity = 0.4,
                ShadowBlurRadius = 27,
                ShadowDepth = 6
            },
            Hotkeys = new HotkeyProfile { NextProfile = "Ctrl+Shift+P" }
        };
        var exported = AppSettingsSerializer.Serialize(new AppSettings
        {
            ActiveProfileId = custom.Id,
            Profiles = [custom]
        });

        var imported = AppSettingsSerializer.Deserialize(exported).Profiles.Single();

        Assert.Equal(@"C:\Games\Example.exe", imported.AudioSources.Single().ProcessExecutablePath);
        Assert.Equal("#FF8844", imported.AudioSources.Single().ColorHex);
        Assert.Equal(1110, imported.Overlay.Width);
        Assert.Equal(4, imported.Overlay.MaxLines);
        Assert.Equal(@"\\.\DISPLAY2", imported.Overlay.MonitorId);
        Assert.Equal(OverlayAnchor.Custom, imported.Overlay.Anchor);
        Assert.Equal(0.72, imported.Overlay.X);
        Assert.Equal(0.81, imported.Overlay.Y);
        Assert.Equal(0.4, imported.Overlay.ShadowOpacity);
        Assert.Equal(27, imported.Overlay.ShadowBlurRadius);
        Assert.Equal(6, imported.Overlay.ShadowDepth);
        Assert.Equal("Ctrl+Shift+P", imported.Hotkeys.NextProfile);
    }
}
