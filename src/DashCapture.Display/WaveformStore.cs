using DashCapture.Core.Models;

namespace DashCapture.Display;

public sealed class WaveformStore
{
    private readonly Dictionary<ChannelKey, WaveformRingBuffer> _buffers = new();
    private readonly Dictionary<ChannelKey, ChannelDescriptor> _channels = new();
    private readonly object _sync = new();
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
                return _channels.Values.ToArray();
            }
        }
    }

    public void SetCapacity(int capacity)
    {
        lock (_sync)
        {
            _capacityPerChannel = Math.Max(1, capacity);
            foreach (ChannelDescriptor channel in _channels.Values.ToArray())
            {
                _buffers[new ChannelKey(channel.DeviceId, channel.ChannelId)] = new WaveformRingBuffer(_capacityPerChannel);
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
                var key = new ChannelKey(channel.DeviceId, channel.ChannelId);
                _channels[key] = channel;
                if (!_buffers.ContainsKey(key))
                {
                    _buffers[key] = new WaveformRingBuffer(_capacityPerChannel);
                }
            }

            foreach (ChannelKey stale in _buffers.Keys.Except(_channels.Keys).ToArray())
            {
                _buffers.Remove(stale);
            }
        }
    }

    public void Append(ChannelDescriptor channel, ReadOnlySpan<float> samples)
    {
        lock (_sync)
        {
            var key = new ChannelKey(channel.DeviceId, channel.ChannelId);
            if (!_channels.ContainsKey(key))
            {
                return;
            }

            if (!_buffers.TryGetValue(key, out WaveformRingBuffer? buffer))
            {
                buffer = new WaveformRingBuffer(_capacityPerChannel);
                _buffers[key] = buffer;
            }

            buffer.Append(samples);
        }
    }

    public IReadOnlyDictionary<ChannelDescriptor, float[]> Snapshot()
    {
        lock (_sync)
        {
            return _channels.Values.ToDictionary(channel => channel, channel => _buffers[new ChannelKey(channel.DeviceId, channel.ChannelId)].Snapshot());
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            foreach (WaveformRingBuffer buffer in _buffers.Values)
            {
                buffer.Clear();
            }
        }
    }
}
