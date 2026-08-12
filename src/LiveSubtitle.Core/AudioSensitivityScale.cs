namespace LiveSubtitle.Core;

public static class AudioSensitivityScale
{
    public const float MostSensitiveNoiseFloor = 0.001f;
    public const float LeastSensitiveNoiseFloor = 0.021f;

    public static float ToNoiseFloor(double sensitivityPercent)
    {
        var sensitivity = Math.Clamp(sensitivityPercent, 0, 100) / 100.0;
        return (float)(LeastSensitiveNoiseFloor
                       - ((LeastSensitiveNoiseFloor - MostSensitiveNoiseFloor) * sensitivity));
    }

    public static double FromNoiseFloor(float noiseFloor)
    {
        var clamped = Math.Clamp(noiseFloor, MostSensitiveNoiseFloor, LeastSensitiveNoiseFloor);
        return 100.0 * (LeastSensitiveNoiseFloor - clamped)
               / (LeastSensitiveNoiseFloor - MostSensitiveNoiseFloor);
    }
}
