using DashCapture.Core.Models;

namespace DashCapture.Display;

public sealed class WaveformStore
{
    private readonly Dictionary<ChannelKey, WaveformEnvelopeRingBuffer> _buffers = new();
    private readonly Dictionary<ChannelKey, ChannelDescriptor> _channels = new();
    private readonly Dictionary<ChannelKey, float> _displaySampleRates = new();
    private readonly object _sync = new();
    private ChannelDescriptor[] _visibleChannels = Array.Empty<ChannelDescriptor>();
    private int _capacityPerChannel;

    public WaveformStore(int capacityPerChannel)
    {
        _capacityPerChannel = Math.Max(1, capacityPerChannel);
    }

    public IReadOnlyList<ChannelDescriptor> VisibleChannels
    {
        get
        {
            lock (_sync)
            {
                return _visibleChannels;
            }
        }
    }

    public void SetCapacity(int capacity)
    {
        lock (_sync)
        {
            int newCapacity = Math.Max(1, capacity);
            if (newCapacity == _capacityPerChannel)
            {
                return;
            }

            _capacityPerChannel = newCapacity;
            foreach (ChannelKey key in _buffers.Keys.ToArray())
            {
                _buffers[key] = _buffers[key].Resize(_capacityPerChannel);
            }
        }
    }

    public void SetVisibleChannels(IEnumerable<ChannelDescriptor> channels)
    {
        lock (_sync)
        {
            _channels.Clear();
            foreach (ChannelDescriptor channel in channels)
            {
                var key = new ChannelKey(channel);
                _channels[key] = channel;
                if (!_buffers.ContainsKey(key))
                {
                    _buffers[key] = new WaveformEnvelopeRingBuffer(_capacityPerChannel);
                }

                _displaySampleRates[key] = channel.SampleRate;
            }

            foreach (ChannelKey stale in _buffers.Keys.Except(_channels.Keys).ToArray())
            {
                _buffers.Remove(stale);
                _displaySampleRates.Remove(stale);
            }

            _visibleChannels = _channels.Values.ToArray();
        }
    }

    public void Append(ChannelDescriptor channel, ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
        {
            return;
        }

        var envelopes = new EnvelopePoint[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            float value = samples[i];
            envelopes[i] = new EnvelopePoint(i, value, value, value, value);
        }

        AppendEnvelope(channel, envelopes, channel.SampleRate);
    }

    public void AppendEnvelope(ChannelDescriptor channel, ReadOnlySpan<EnvelopePoint> points, float displaySampleRate)
    {
        lock (_sync)
        {
            var key = new ChannelKey(channel);
            if (!_channels.ContainsKey(key))
            {
                return;
            }

            if (IsValidSampleRate(displaySampleRate))
            {
                _displaySampleRates[key] = displaySampleRate;
            }

            if (!_buffers.TryGetValue(key, out WaveformEnvelopeRingBuffer? buffer))
            {
                buffer = new WaveformEnvelopeRingBuffer(_capacityPerChannel);
                _buffers[key] = buffer;
            }

            buffer.Append(points);
        }
    }

    public IReadOnlyDictionary<ChannelDescriptor, EnvelopePoint[]> Snapshot()
    {
        return Snapshot(null);
    }

    public IReadOnlyDictionary<ChannelDescriptor, EnvelopePoint[]> Snapshot(IReadOnlyList<ChannelDescriptor>? channels)
    {
        lock (_sync)
        {
            return ResolveChannels(channels).ToDictionary(
                channel => channel,
                channel => _buffers[new ChannelKey(channel)].Snapshot());
        }
    }

    public IReadOnlyList<WaveformSnapshot> SnapshotSeries(IReadOnlyList<ChannelDescriptor>? channels)
    {
        lock (_sync)
        {
            IReadOnlyList<ChannelDescriptor> resolved = ResolveChannels(channels);
            var snapshots = new WaveformSnapshot[resolved.Count];
            for (int i = 0; i < resolved.Count; i++)
            {
                ChannelDescriptor channel = resolved[i];
                var key = new ChannelKey(channel);
                float displaySampleRate = _displaySampleRates.TryGetValue(key, out float rate) && IsValidSampleRate(rate)
                    ? rate
                    : channel.SampleRate;
                WaveformEnvelopeSnapshot bufferSnapshot = _buffers[key].SnapshotWithPosition();
                snapshots[i] = new WaveformSnapshot(
                    channel,
                    displaySampleRate,
                    bufferSnapshot.Points,
                    bufferSnapshot.TotalPointCount);
            }

            return snapshots;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            foreach (WaveformEnvelopeRingBuffer buffer in _buffers.Values)
            {
                buffer.Clear();
            }
        }
    }

    private static bool IsValidSampleRate(float sampleRate)
    {
        return sampleRate > 0 && !float.IsNaN(sampleRate) && !float.IsInfinity(sampleRate);
    }

    private IReadOnlyList<ChannelDescriptor> ResolveChannels(IReadOnlyList<ChannelDescriptor>? channels)
    {
        if (channels is null)
        {
            return _visibleChannels;
        }

        var resolved = new List<ChannelDescriptor>(channels.Count);
        for (int i = 0; i < channels.Count; i++)
        {
            var key = new ChannelKey(channels[i]);
            if (_channels.TryGetValue(key, out ChannelDescriptor? channel))
            {
                resolved.Add(channel);
            }
        }

        return resolved;
    }
}

public sealed record WaveformSnapshot(
    ChannelDescriptor Channel,
    float DisplaySampleRate,
    EnvelopePoint[] Points,
    long TotalPointCount);
