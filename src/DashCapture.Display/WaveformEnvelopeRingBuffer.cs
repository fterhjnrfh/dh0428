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

    public EnvelopePoint[] Snapshot()
    {
        lock (_sync)
        {
            return CopySnapshot();
        }
    }

    public WaveformEnvelopeSnapshot SnapshotWithPosition()
    {
        lock (_sync)
        {
            return new WaveformEnvelopeSnapshot(CopySnapshot(), _writeIndex);
        }
    }

    public WaveformEnvelopeSnapshot SnapshotCurrentSweepDownsampled(int pointsPerSweep, int targetBuckets)
    {
        lock (_sync)
        {
            int sourceCount = CurrentSweepCount(pointsPerSweep);
            if (sourceCount <= 0)
            {
                return new WaveformEnvelopeSnapshot(Array.Empty<EnvelopePoint>(), _writeIndex, 0);
            }

            int buckets = Math.Min(sourceCount, Math.Max(1, targetBuckets));
            var output = new EnvelopePoint[buckets];
            long start = _writeIndex - sourceCount;
            int capacity = _buffer.Length;
            for (int pixel = 0; pixel < buckets; pixel++)
            {
                int offsetStart = (int)((long)pixel * sourceCount / buckets);
                int offsetEnd = (int)((long)(pixel + 1) * sourceCount / buckets);
                if (offsetEnd <= offsetStart)
                {
                    offsetEnd = offsetStart + 1;
                }

                bool hasValue = false;
                float first = float.NaN;
                float last = float.NaN;
                float min = float.MaxValue;
                float max = float.MinValue;
                for (int offset = offsetStart; offset < offsetEnd; offset++)
                {
                    EnvelopePoint point = _buffer[PositiveModulo(start + offset, capacity)];
                    if (float.IsNaN(point.Minimum) || float.IsInfinity(point.Minimum) ||
                        float.IsNaN(point.Maximum) || float.IsInfinity(point.Maximum))
                    {
                        continue;
                    }

                    if (!hasValue)
                    {
                        first = point.First;
                        hasValue = true;
                    }

                    last = point.Last;
                    if (point.Minimum < min) min = point.Minimum;
                    if (point.Maximum > max) max = point.Maximum;
                }

                output[pixel] = hasValue
                    ? new EnvelopePoint(pixel, first, last, min, max)
                    : new EnvelopePoint(pixel, float.NaN, float.NaN, float.NaN, float.NaN);
            }

            return new WaveformEnvelopeSnapshot(output, _writeIndex, sourceCount);
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

    private EnvelopePoint[] CopySnapshot()
    {
        int count = (int)Math.Min(_count, _buffer.Length);
        if (count == 0)
        {
            return Array.Empty<EnvelopePoint>();
        }

        var snapshot = new EnvelopePoint[count];
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

    private int CurrentSweepCount(int pointsPerSweep)
    {
        int count = (int)Math.Min(_count, _buffer.Length);
        if (count <= 0)
        {
            return 0;
        }

        pointsPerSweep = Math.Max(1, pointsPerSweep);
        int sweepCount;
        if (_writeIndex < pointsPerSweep)
        {
            sweepCount = (int)Math.Min(_writeIndex, count);
        }
        else
        {
            int phase = (int)(_writeIndex % pointsPerSweep);
            sweepCount = phase == 0 ? pointsPerSweep : phase;
            sweepCount = Math.Min(sweepCount, count);
        }

        return Math.Max(0, sweepCount);
    }

    private static int PositiveModulo(long value, int divisor)
    {
        int result = (int)(value % divisor);
        return result < 0 ? result + divisor : result;
    }
}

public readonly record struct WaveformEnvelopeSnapshot(EnvelopePoint[] Points, long TotalPointCount, int SourcePointCount = 0);
