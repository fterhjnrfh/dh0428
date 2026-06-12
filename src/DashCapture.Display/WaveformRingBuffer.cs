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
        if (values.IsEmpty)
        {
            return;
        }

        lock (_sync)
        {
            int capacity = _buffer.Length;
            if (values.Length >= capacity)
            {
                values[^capacity..].CopyTo(_buffer);
                _writeIndex += values.Length;
                _count = capacity;
                return;
            }

            int position = (int)(_writeIndex % capacity);
            int firstCopy = Math.Min(values.Length, capacity - position);
            values[..firstCopy].CopyTo(_buffer.AsSpan(position, firstCopy));
            if (firstCopy < values.Length)
            {
                values[firstCopy..].CopyTo(_buffer.AsSpan(0, values.Length - firstCopy));
            }

            _writeIndex += values.Length;
            _count = Math.Min(capacity, _count + values.Length);
        }
    }

    public float[] Snapshot()
    {
        lock (_sync)
        {
            int count = (int)Math.Min(_count, _buffer.Length);
            if (count == 0)
            {
                return Array.Empty<float>();
            }

            float[] snapshot = new float[count];
            long start = _writeIndex - count;
            int position = (int)(start % _buffer.Length);
            if (position < 0)
            {
                position += _buffer.Length;
            }

            int firstCopy = Math.Min(count, _buffer.Length - position);
            Array.Copy(_buffer, position, snapshot, 0, firstCopy);
            if (firstCopy < count)
            {
                Array.Copy(_buffer, 0, snapshot, firstCopy, count - firstCopy);
            }

            return snapshot;
        }
    }

    public WaveformRingBuffer Resize(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

        float[] snapshot = Snapshot();
        if (snapshot.Length > capacity)
        {
            snapshot = snapshot.AsSpan(snapshot.Length - capacity, capacity).ToArray();
        }

        var resized = new WaveformRingBuffer(capacity);
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
