using NAudio.Wave;

namespace LiveSubtitle.App.Audio;

internal static class WaveSampleConverter
{
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");

    public static float[] Convert(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        var bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        var sampleCount = bytesRecorded / bytesPerSample;
        var result = new float[sampleCount];
        var encoding = format.Encoding;
        if (format is WaveFormatExtensible extensible)
        {
            encoding = extensible.SubFormat == IeeeFloatSubFormat
                ? WaveFormatEncoding.IeeeFloat
                : extensible.SubFormat == PcmSubFormat
                    ? WaveFormatEncoding.Pcm
                    : encoding;
        }

        if (encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            Buffer.BlockCopy(buffer, 0, result, 0, sampleCount * sizeof(float));
            return result;
        }

        if (encoding != WaveFormatEncoding.Pcm)
        {
            throw new NotSupportedException($"Unsupported capture format: {format}");
        }

        for (var index = 0; index < sampleCount; index++)
        {
            var offset = index * bytesPerSample;
            result[index] = format.BitsPerSample switch
            {
                8 => (buffer[offset] - 128) / 128f,
                16 => BitConverter.ToInt16(buffer, offset) / 32768f,
                24 => ReadInt24(buffer, offset) / 8388608f,
                32 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
                _ => throw new NotSupportedException($"Unsupported PCM bit depth: {format.BitsPerSample}")
            };
        }

        return result;
    }

    private static int ReadInt24(byte[] buffer, int offset)
    {
        var value = buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16;
        return (value & 0x800000) == 0 ? value : value | unchecked((int)0xFF000000);
    }
}
