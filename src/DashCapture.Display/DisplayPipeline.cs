using DashCapture.Core.Acquisition;
using DashCapture.Core.Memory;
using DashCapture.Core.Models;

namespace DashCapture.Display;

public sealed class DisplayPipeline : IAsyncDisposable
{
    private readonly AcquisitionService _acquisition;
    private readonly Func<IReadOnlyList<DeviceDescriptor>> _devicesProvider;
    private CancellationTokenSource? _cts;
    private Task? _worker;

    public DisplayPipeline(AcquisitionService acquisition, WaveformStore store, Func<IReadOnlyList<DeviceDescriptor>> devicesProvider)
    {
        _acquisition = acquisition;
        Store = store;
        _devicesProvider = devicesProvider;
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
        DeviceDescriptor? device = devices.FirstOrDefault(d => d.DeviceId == block.Header.GroupId) ??
                                   devices.FirstOrDefault(d => d.DeviceId == block.Header.MachineId);
        if (device is null)
        {
            return;
        }

        IReadOnlyList<ChannelDescriptor> visible = Store.VisibleChannels;
        if (visible.Count == 0)
        {
            return;
        }

        int sampleCount = block.Header.DataCountPerChannel;
        int channelCount = Math.Max(1, block.ChannelCount);
        float[] scratch = new float[sampleCount];

        foreach (ChannelDescriptor channel in visible)
        {
            if (channel.DeviceId != device.DeviceId || channel.DataIndex < 0 || channel.DataIndex >= channelCount)
            {
                continue;
            }

            CopyFloatChannel(block.DataPointer, sampleCount, channelCount, channel.DataIndex, scratch);
            Store.Append(channel, scratch.AsSpan(0, sampleCount));
        }
    }

    private static unsafe void CopyFloatChannel(IntPtr source, int sampleCount, int channelCount, int dataIndex, Span<float> destination)
    {
        float* src = (float*)source.ToPointer();
        for (int i = 0; i < sampleCount; i++)
        {
            destination[i] = src[i * channelCount + dataIndex];
        }
    }
}
