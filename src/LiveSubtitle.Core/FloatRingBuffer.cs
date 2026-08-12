namespace LiveSubtitle.Core;

public sealed class FloatRingBuffer
{
    private readonly float[] _buffer;
    private readonly object _gate = new();
    private int _writeIndex;
    private int _count;

    public FloatRingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _buffer = new float[capacity];
    }

    public int Capacity => _buffer.Length;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    public void Write(ReadOnlySpan<float> samples)
    {
        lock (_gate)
        {
            if (samples.Length >= _buffer.Length)
            {
                samples[^_buffer.Length..].CopyTo(_buffer);
                _writeIndex = 0;
                _count = _buffer.Length;
                return;
            }

            foreach (var sample in samples)
            {
                _buffer[_writeIndex] = sample;
                _writeIndex = (_writeIndex + 1) % _buffer.Length;
                _count = Math.Min(_count + 1, _buffer.Length);
            }
        }
    }

    public float[] Snapshot()
    {
        lock (_gate)
        {
            var result = new float[_count];
            var start = (_writeIndex - _count + _buffer.Length) % _buffer.Length;
            var firstLength = Math.Min(_count, _buffer.Length - start);
            Array.Copy(_buffer, start, result, 0, firstLength);
            if (firstLength < _count)
            {
                Array.Copy(_buffer, 0, result, firstLength, _count - firstLength);
            }

            return result;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _writeIndex = 0;
            _count = 0;
            Array.Clear(_buffer);
        }
    }
}
