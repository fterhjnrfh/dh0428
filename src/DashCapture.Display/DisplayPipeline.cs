using DashCapture.Core.Acquisition;
using DashCapture.Core.Memory;
using DashCapture.Core.Models;

namespace DashCapture.Display;

public sealed class DisplayPipeline : IAsyncDisposable
{
    private readonly AcquisitionService _acquisition;
    private readonly Func<IReadOnlyList<DeviceDescriptor>> _devicesProvider;
    private readonly int _maxDisplayPointsPerSecond;
    private readonly Dictionary<ChannelKey, ChannelEnvelopeDecimator> _decimators = new();
    private CancellationTokenSource? _cts;
    private Task? _worker;

    public DisplayPipeline(
        AcquisitionService acquisition,
        WaveformStore store,
        Func<IReadOnlyList<DeviceDescriptor>> devicesProvider,
        int maxDisplayPointsPerSecond)
    {
        _acquisition = acquisition;
        Store = store;
        _devicesProvider = devicesProvider;
        _maxDisplayPointsPerSecond = Math.Max(1, maxDisplayPointsPerSecond);
    }

    public WaveformStore Store { get; }
    public bool IsRunning => _worker is not null && !_worker.IsCompleted;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _worker = Task.Run(() => ConsumeAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts?.Dispose();
        _cts = null;
        _worker = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        await foreach (AcquisitionBlock block in _acquisition.DisplayReader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                Process(block);
            }
            finally
            {
                block.Release();
                _acquisition.MarkDisplayBlockConsumed();
            }
        }
    }

    private void Process(AcquisitionBlock block)
    {
        IReadOnlyList<DeviceDescriptor> devices = _devicesProvider();
        IReadOnlyList<ChannelDescriptor> visible = Store.VisibleChannels;
        if (visible.Count == 0)
        {
            return;
        }

        int sampleCount = block.Header.DataCountPerChannel;
        if (sampleCount <= 0)
        {
            return;
        }

        int channelCount = Math.Max(1, block.ChannelCount);
        bool globalBlock = IsGlobalMultiDeviceBlock(block, devices);
        DeviceDescriptor? device = globalBlock
            ? null
            : devices.FirstOrDefault(d => d.DeviceId == block.Header.GroupId) ??
              devices.FirstOrDefault(d => d.DeviceId == block.Header.MachineId);

        if (!globalBlock && device is null)
        {
            return;
        }

        foreach (ChannelDescriptor channel in visible)
        {
            if (!globalBlock && channel.DeviceId != device!.DeviceId)
            {
                continue;
            }

            int dataIndex = globalBlock
                ? channel.DataIndex
                : block.Header.Layout == SampleDataLayout.ChannelContiguousFloat32 ? channel.LocalDataIndex : channel.DataIndex;
            if (dataIndex < 0 || dataIndex >= channelCount)
            {
                continue;
            }

            ChannelEnvelopeDecimator decimator = GetDecimator(channel);
            EnvelopePoint[] points = decimator.Process(
                block.DataPointer,
                sampleCount,
                channelCount,
                dataIndex,
                block.Header.Layout);

            if (points.Length > 0)
            {
                Store.AppendEnvelope(channel, points, decimator.OutputSampleRate);
            }
        }
    }

    private ChannelEnvelopeDecimator GetDecimator(ChannelDescriptor channel)
    {
        var key = new ChannelKey(channel);
        float rawSampleRate = IsValidSampleRate(channel.SampleRate) ? channel.SampleRate : _maxDisplayPointsPerSecond;
        int bucketSize = Math.Max(1, (int)Math.Round(rawSampleRate / Math.Min(rawSampleRate, _maxDisplayPointsPerSecond)));
        float outputSampleRate = rawSampleRate / bucketSize;

        if (_decimators.TryGetValue(key, out ChannelEnvelopeDecimator? decimator))
        {
            decimator.Configure(bucketSize, outputSampleRate);
            return decimator;
        }

        decimator = new ChannelEnvelopeDecimator(bucketSize, outputSampleRate);
        _decimators[key] = decimator;
        return decimator;
    }

    private static bool IsGlobalMultiDeviceBlock(AcquisitionBlock block, IReadOnlyList<DeviceDescriptor> devices)
    {
        int totalChannelCount = devices.Sum(d => d.Channels.Count);
        return block.Header.MessageType == DashSampleMessageType.AnalogMultiChannelData ||
               block.Header.GroupId < 0 ||
               block.Header.MachineId < 0 ||
               (devices.Count > 1 &&
                totalChannelCount > 0 &&
                block.ChannelCount == totalChannelCount);
    }

    private static bool IsValidSampleRate(float sampleRate)
    {
        return sampleRate > 0 && !float.IsNaN(sampleRate) && !float.IsInfinity(sampleRate);
    }

    private sealed class ChannelEnvelopeDecimator
    {
        private int _bucketSize;
        private int _bucketCount;
        private float _first;
        private float _last;
        private float _min;
        private float _max;
        private bool _hasValue;

        public ChannelEnvelopeDecimator(int bucketSize, float outputSampleRate)
        {
            _bucketSize = Math.Max(1, bucketSize);
            OutputSampleRate = outputSampleRate;
        }

        public float OutputSampleRate { get; private set; }

        public void Configure(int bucketSize, float outputSampleRate)
        {
            bucketSize = Math.Max(1, bucketSize);
            if (bucketSize != _bucketSize)
            {
                ResetBucket();
                _bucketSize = bucketSize;
            }

            OutputSampleRate = outputSampleRate;
        }

        public unsafe EnvelopePoint[] Process(
            IntPtr source,
            int sampleCount,
            int channelCount,
            int dataIndex,
            SampleDataLayout layout)
        {
            int estimated = Math.Max(1, sampleCount / _bucketSize + 2);
            var output = new EnvelopePoint[estimated];
            int outputCount = 0;
            float* src = (float*)source.ToPointer();

            if (layout == SampleDataLayout.ChannelContiguousFloat32)
            {
                float* channelStart = src + (dataIndex * sampleCount);
                for (int i = 0; i < sampleCount; i++)
                {
                    AddValue(channelStart[i], ref output, ref outputCount);
                }
            }
            else
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    AddValue(src[i * channelCount + dataIndex], ref output, ref outputCount);
                }
            }

            if (outputCount == output.Length)
            {
                return output;
            }

            Array.Resize(ref output, outputCount);
            return output;
        }

        private void AddValue(float value, ref EnvelopePoint[] output, ref int outputCount)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return;
            }

            if (!_hasValue)
            {
                _first = value;
                _min = value;
                _max = value;
                _hasValue = true;
            }

            _last = value;
            if (value < _min) _min = value;
            if (value > _max) _max = value;
            _bucketCount++;

            if (_bucketCount < _bucketSize)
            {
                return;
            }

            if (outputCount >= output.Length)
            {
                Array.Resize(ref output, output.Length * 2);
            }

            output[outputCount] = new EnvelopePoint(outputCount, _first, _last, _min, _max);
            outputCount++;
            ResetBucket();
        }

        private void ResetBucket()
        {
            _bucketCount = 0;
            _first = 0;
            _last = 0;
            _min = 0;
            _max = 0;
            _hasValue = false;
        }
    }
}
