namespace DashCapture.Display;

public sealed class WaveformEnvelopeRingBuffer
{
    private readonly EnvelopePoint[] _buffer;
    private long _writeIndex;
    private long _count;
    private readonly object _sync = new();

    public WaveformEnvelopeRingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new EnvelopePoint[capacity];
    }

    public int Capacity => _buffer.Length;

    public void Append(ReadOnlySpan<EnvelopePoint> values)
    {
        lock (_sync)
        {
            foreach (EnvelopePoint value in values)
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

    public EnvelopePoint[] Snapshot()
    {
        lock (_sync)
        {
            int count = (int)Math.Min(_count, _buffer.Length);
            var snapshot = new EnvelopePoint[count];
            long start = _writeIndex - count;
            for (int i = 0; i < count; i++)
            {
                snapshot[i] = _buffer[(start + i) % _buffer.Length];
            }

            return snapshot;
        }
    }

    public WaveformEnvelopeRingBuffer Resize(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

        EnvelopePoint[] snapshot = Snapshot();
        if (snapshot.Length > capacity)
        {
            snapshot = snapshot.AsSpan(snapshot.Length - capacity, capacity).ToArray();
        }

        var resized = new WaveformEnvelopeRingBuffer(capacity);
        resized.Append(snapshot);
        return resized;
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
