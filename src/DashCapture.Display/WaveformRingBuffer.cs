namespace DashCapture.Display;

public sealed class WaveformRingBuffer
{
    private readonly float[] _buffer;
    private long _writeIndex;
    private long _count;
    private readonly object _sync = new();

    public WaveformRingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new float[capacity];
    }

    public int Capacity => _buffer.Length;

    public void Append(ReadOnlySpan<float> values)
    {
        lock (_sync)
        {
            foreach (float value in values)
            {
                _buffer[_writeIndex % _buffer.Length] = value;
                _writeIndex++;
                if (_count < _buffer.Length)
                {
                    _count++;
                }
            }
        }
    }

    public float[] Snapshot()
    {
        lock (_sync)
        {
            int count = (int)Math.Min(_count, _buffer.Length);
            float[] snapshot = new float[count];
            long start = _writeIndex - count;
            for (int i = 0; i < count; i++)
            {
                snapshot[i] = _buffer[(start + i) % _buffer.Length];
            }

            return snapshot;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _writeIndex = 0;
            _count = 0;
        }
    }
}
