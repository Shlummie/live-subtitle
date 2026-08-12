using LiveSubtitle.Core;

namespace LiveSubtitle.Tests;

public sealed class AudioProcessingTests
{
    [Fact]
    public void ToMono16Khz_DownmixesStereoAndResamples()
    {
        var stereo = new float[48_000 * 2];
        for (var index = 0; index < 48_000; index++)
        {
            stereo[index * 2] = 0.5f;
            stereo[index * 2 + 1] = -0.25f;
        }

        var mono = AudioProcessing.ToMono16Khz(stereo, 48_000, 2);

        Assert.Equal(16_000, mono.Length);
        Assert.All(mono, sample => Assert.InRange(sample, 0.124f, 0.126f));
    }

    [Fact]
    public void AdaptiveVad_EndsUtteranceAfterConfiguredSilence()
    {
        var detector = new AdaptiveVoiceActivityDetector(450, 0.01f);
        detector.Process(Enumerable.Repeat(0.2f, 1600).ToArray(), 100);
        Assert.True(detector.IsSpeech);

        for (var index = 0; index < 4; index++)
        {
            detector.Process(new float[1600], 100);
            Assert.False(detector.EndpointReached);
        }

        detector.Process(new float[800], 50);
        Assert.True(detector.EndpointReached);
        Assert.False(detector.IsSpeech);
    }
}
