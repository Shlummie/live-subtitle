namespace LiveSubtitle.Core;

public static class AudioProcessing
{
    public const int TargetSampleRate = 16_000;

    public static float[] ToMono16Khz(
        ReadOnlySpan<float> interleavedSamples,
        int inputSampleRate,
        int channels,
        double gain = 1.0)
    {
        if (inputSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputSampleRate));
        }

        if (channels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }

        if (interleavedSamples.Length < channels)
        {
            return [];
        }

        var frameCount = interleavedSamples.Length / channels;
        var mono = new float[frameCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            double sum = 0;
            var offset = frame * channels;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += interleavedSamples[offset + channel];
            }

            mono[frame] = Math.Clamp((float)(sum / channels * gain), -1f, 1f);
        }

        if (inputSampleRate == TargetSampleRate)
        {
            return mono;
        }

        var outputLength = Math.Max(1, (int)Math.Round(frameCount * (double)TargetSampleRate / inputSampleRate));
        var output = new float[outputLength];
        var sourceStep = (double)inputSampleRate / TargetSampleRate;
        for (var index = 0; index < outputLength; index++)
        {
            var sourcePosition = index * sourceStep;
            var left = Math.Min((int)sourcePosition, frameCount - 1);
            var right = Math.Min(left + 1, frameCount - 1);
            var fraction = sourcePosition - left;
            output[index] = (float)(mono[left] + (mono[right] - mono[left]) * fraction);
        }

        return output;
    }

    public static float RootMeanSquare(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return 0;
        }

        double sum = 0;
        foreach (var sample in samples)
        {
            sum += sample * sample;
        }

        return (float)Math.Sqrt(sum / samples.Length);
    }
}

public sealed class AdaptiveVoiceActivityDetector
{
    private readonly int _finalizeSilenceMs;
    private readonly float _configuredFloor;
    private double _noiseEstimate;
    private int _silenceMs;

    public AdaptiveVoiceActivityDetector(int finalizeSilenceMs, float configuredFloor)
    {
        _finalizeSilenceMs = Math.Clamp(finalizeSilenceMs, 100, 5000);
        _configuredFloor = Math.Clamp(configuredFloor, 0.0001f, 0.5f);
        _noiseEstimate = _configuredFloor;
    }

    public bool IsSpeech { get; private set; }
    public bool EndpointReached { get; private set; }
    public float LastLevel { get; private set; }

    public void Process(ReadOnlySpan<float> samples, int durationMs)
    {
        LastLevel = AudioProcessing.RootMeanSquare(samples);
        var threshold = Math.Max(_configuredFloor, (float)_noiseEstimate * 2.8f);
        var speechNow = LastLevel >= threshold;

        if (!speechNow && !IsSpeech)
        {
            _noiseEstimate = _noiseEstimate * 0.98 + LastLevel * 0.02;
        }

        EndpointReached = false;
        if (speechNow)
        {
            IsSpeech = true;
            _silenceMs = 0;
            return;
        }

        if (!IsSpeech)
        {
            return;
        }

        _silenceMs += Math.Max(0, durationMs);
        if (_silenceMs >= _finalizeSilenceMs)
        {
            EndpointReached = true;
            IsSpeech = false;
            _silenceMs = 0;
        }
    }

    public void Reset()
    {
        IsSpeech = false;
        EndpointReached = false;
        _silenceMs = 0;
    }
}
