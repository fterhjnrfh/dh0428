namespace DashCapture.Analysis;

public sealed class ChannelWindowAccumulator
{
    private readonly int _windowSampleCount;
    private readonly int _hopSampleCount;
    private readonly bool _keepWindowSamples;
    private readonly float[]? _ring;
    private int _writeIndex;
    private long _totalSamples;
    private long _nextWindowEnd;
    private long _completedWindows;

    public ChannelWindowAccumulator(int windowSampleCount, int hopSampleCount, bool keepWindowSamples)
    {
        _windowSampleCount = Math.Max(1, windowSampleCount);
        _hopSampleCount = Math.Max(1, hopSampleCount);
        _keepWindowSamples = keepWindowSamples;
        _ring = keepWindowSamples ? new float[_windowSampleCount] : null;
        _nextWindowEnd = _windowSampleCount;
    }

    public long TotalSamples => _totalSamples;
    public long CompletedWindows => _completedWindows;
    public int WindowSampleCount => _windowSampleCount;

    public long Append(ReadOnlySpan<float> samples, Action<ChannelWindowAccumulator>? windowCompleted = null)
    {
        if (samples.IsEmpty)
        {
            return 0;
        }

        long completed = 0;
        while (!samples.IsEmpty)
        {
            int chunk = (int)Math.Min(samples.Length, Math.Max(1, _nextWindowEnd - _totalSamples));
            ReadOnlySpan<float> current = samples.Slice(0, chunk);
            if (_keepWindowSamples)
            {
                CopyToRing(current);
            }

            _totalSamples += chunk;
            if (_totalSamples >= _nextWindowEnd)
            {
                completed += CompleteWindow(windowCompleted);
            }

            samples = samples.Slice(chunk);
        }

        return completed;
    }

    public unsafe long AppendInterleaved(
        float* source,
        int sampleCount,
        int channelCount,
        int dataIndex,
        Action<ChannelWindowAccumulator>? windowCompleted = null)
    {
        if (sampleCount <= 0)
        {
            return 0;
        }

        long completed = 0;
        if (_keepWindowSamples && _ring is not null)
        {
            for (int i = 0; i < sampleCount; i++)
            {
                _ring[_writeIndex] = source[i * channelCount + dataIndex];
                _writeIndex++;
                if (_writeIndex >= _windowSampleCount)
                {
                    _writeIndex = 0;
                }

                _totalSamples++;
                if (_totalSamples >= _nextWindowEnd)
                {
                    completed += CompleteWindow(windowCompleted);
                }
            }

            return completed;
        }

        for (int i = 0; i < sampleCount; i++)
        {
            _totalSamples++;
            if (_totalSamples >= _nextWindowEnd)
            {
                completed += CompleteWindow(windowCompleted);
            }
        }

        return completed;
    }

    public bool CopyCurrentWindowTo(Span<float> destination)
    {
        if (_ring is null || destination.Length < _windowSampleCount || _totalSamples < _windowSampleCount)
        {
            return false;
        }

        int tailCount = _windowSampleCount - _writeIndex;
        _ring.AsSpan(_writeIndex, tailCount).CopyTo(destination);
        _ring.AsSpan(0, _writeIndex).CopyTo(destination.Slice(tailCount));
        return true;
    }

    private void CopyToRing(ReadOnlySpan<float> samples)
    {
        if (_ring is null)
        {
            return;
        }

        while (!samples.IsEmpty)
        {
            int chunk = Math.Min(samples.Length, _windowSampleCount - _writeIndex);
            samples.Slice(0, chunk).CopyTo(_ring.AsSpan(_writeIndex, chunk));
            _writeIndex += chunk;
            if (_writeIndex >= _windowSampleCount)
            {
                _writeIndex = 0;
            }

            samples = samples.Slice(chunk);
        }
    }

    private long CompleteWindow(Action<ChannelWindowAccumulator>? windowCompleted)
    {
        _completedWindows++;
        _nextWindowEnd += _hopSampleCount;
        windowCompleted?.Invoke(this);
        return 1;
    }
}
