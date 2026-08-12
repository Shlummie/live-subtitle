using LiveSubtitle.Core;

namespace LiveSubtitle.Tests;

public sealed class RecognitionInputTests
{
    [Theory]
    [InlineData("BLANK_TEXT")]
    [InlineData("[BLANK_TEXT]")]
    [InlineData("[BLANK_AUDIO]")]
    [InlineData("<|nospeech|>")]
    [InlineData("  blank_text  ")]
    public void BlankRecognitionSentinelsProduceNoCaption(string text) =>
        Assert.Equal(string.Empty, RecognizedTextFilter.Normalize(text));

    [Theory]
    [InlineData("Please leave the blank text field empty.")]
    [InlineData("Dialogue before BLANK_TEXT dialogue after")]
    public void SpokenTextContainingSentinelWordsIsPreserved(string text) =>
        Assert.Equal(text, RecognizedTextFilter.Normalize(text));

    [Fact]
    public void RecognitionWhitespaceIsCollapsed() =>
        Assert.Equal("quiet dialogue", RecognizedTextFilter.Normalize("  quiet   dialogue  "));

    [Fact]
    public void HigherSensitivityUsesALowerNoiseFloor()
    {
        var lowSensitivity = AudioSensitivityScale.ToNoiseFloor(10);
        var highSensitivity = AudioSensitivityScale.ToNoiseFloor(90);

        Assert.True(highSensitivity < lowSensitivity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(65)]
    [InlineData(100)]
    public void SensitivityRoundTripsThroughPersistedNoiseFloor(double sensitivity)
    {
        var noiseFloor = AudioSensitivityScale.ToNoiseFloor(sensitivity);
        var restored = AudioSensitivityScale.FromNoiseFloor(noiseFloor);

        Assert.InRange(restored, sensitivity - 0.001, sensitivity + 0.001);
    }

    [Fact]
    public void ProfileJsonPreservesAudioSensitivity()
    {
        var settings = AppSettings.CreateDefault();
        var profile = settings.Profiles[0] with
        {
            Recognition = settings.Profiles[0].Recognition with
            {
                NoiseFloor = AudioSensitivityScale.ToNoiseFloor(82)
            }
        };
        var json = AppSettingsSerializer.Serialize(settings with { Profiles = [profile] });
        var restored = AppSettingsSerializer.Deserialize(json).Profiles.Single().Recognition;

        Assert.InRange(AudioSensitivityScale.FromNoiseFloor(restored.NoiseFloor), 81.999, 82.001);
    }
}
