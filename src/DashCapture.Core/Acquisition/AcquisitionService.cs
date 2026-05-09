using System;
using System.Threading.Channels;
using DashCapture.Core.Configuration;
using DashCapture.Core.Memory;
using DashCapture.Core.Models;
using DashCapture.Core.Sources;

namespace DashCapture.Core.Acquisition;

public sealed class AcquisitionService : IAsyncDisposable
{
    private readonly CaptureSettings _settings;
    private readonly NativeSlabPool _pool;
    private readonly Channel<AcquisitionBlock> _storageQueue;
    private readonly Channel<AcquisitionBlock> _displayQueue;
    private readonly ContinuityTracker _continuity = new();
    private readonly object _sync = new();
    private readonly object _devicesSync = new();
    private IReadOnlyList<DeviceDescriptor> _devices = Array.Empty<DeviceDescriptor>();
    private IAcquisitionSource? _source;
    private long _blocksReceived;
    private long _bytesReceived;
    private long _displayDrops;
    private int _storageDepth;
    private int _displayDepth;
    private int _storageEnabled = 1;
    private BackpressureLevel _backpressureLevel;
    private string _status = "Idle";

    public AcquisitionService(CaptureSettings settings)
    {
        _settings = settings;
        int slabSize = Math.Max(1, settings.Queues.SlabSizeMb) * 1024 * 1024;
        _pool = new NativeSlabPool(slabSize, Math.Max(1, settings.Queues.SlabCount));
        _storageQueue = Channel.CreateBounded<AcquisitionBlock>(new BoundedChannelOptions(settings.Queues.StorageCapacityBlocks)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        _displayQueue = Channel.CreateBounded<AcquisitionBlock>(new BoundedChannelOptions(settings.Queues.DisplayCapacityBlocks)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public event Action<AcquisitionFault>? Faulted;
    public event Action<CaptureTelemetry>? TelemetryUpdated;
    public IReadOnlyList<DeviceDescriptor> Devices
    {
        get
        {
            lock (_devicesSync)
            {
                return _devices;
            }
        }
    }
    public ChannelReader<AcquisitionBlock> StorageReader => _storageQueue.Reader;
    public ChannelReader<AcquisitionBlock> DisplayReader => _displayQueue.Reader;
    public bool IsRunning { get; private set; }
    public bool IsConnected => _source?.IsConnected == true;
    public bool StorageEnabled => Volatile.Read(ref _storageEnabled) != 0;

    public void SetStorageEnabled(bool enabled)
    {
        Volatile.Write(ref _storageEnabled, enabled ? 1 : 0);
        if (!enabled)
        {
            ReleaseQueuedBlocks(_storageQueue.Reader, ref _storageDepth);
            UpdateBackpressure();
            PublishTelemetry();
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_source is not null)
        {
            return;
        }

        _source = new DashSdkAcquisitionSource(_settings.Sdk);
        _source.SampleReceived += OnSampleReceived;
        await _source.ConnectAsync(cancellationToken).ConfigureAwait(false);
        SetDevices(_source.Devices);
        _status = _source.Devices.Count == 0 ? "Connected: no devices" : $"Connected: {_source.Devices.Count} device(s)";

        PublishTelemetry();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_source is null)
        {
            await ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_source is null)
        {
            throw new InvalidOperationException("No acquisition source is available.");
        }

        _continuity.Reset();
        _blocksReceived = 0;
        _bytesReceived = 0;
        _displayDrops = 0;
        _storageDepth = 0;
        _displayDepth = 0;
        _backpressureLevel = BackpressureLevel.Normal;
        IsRunning = true;
        _status = "Sampling";
        await _source.StartAsync(cancellationToken).ConfigureAwait(false);
        PublishTelemetry();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        IsRunning = false;
        if (_source is not null)
        {
            await _source.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        _status = "Stopped";
        PublishTelemetry();
    }

    public CaptureTelemetry GetTelemetry() => new(
        DateTimeOffset.UtcNow,
        Interlocked.Read(ref _blocksReceived),
        Interlocked.Read(ref _bytesReceived),
        Interlocked.Read(ref _displayDrops),
        Volatile.Read(ref _storageDepth),
        Volatile.Read(ref _displayDepth),
        _backpressureLevel,
        _status);

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        if (_source is not null)
        {
            _source.SampleReceived -= OnSampleReceived;
            await _source.DisposeAsync().ConfigureAwait(false);
        }

        ReleaseQueuedBlocks();
        _storageQueue.Writer.TryComplete();
        _displayQueue.Writer.TryComplete();
        _pool.Dispose();
    }

    public void ReleaseQueuedBlocks()
    {
        int releasedStorage = ReleaseQueuedBlocks(_storageQueue.Reader, ref _storageDepth);
        int releasedDisplay = ReleaseQueuedBlocks(_displayQueue.Reader, ref _displayDepth);
        if (releasedStorage > 0 || releasedDisplay > 0)
        {
            PublishTelemetry();
        }
    }

    public void MarkStorageBlockConsumed()
    {
        Interlocked.Decrement(ref _storageDepth);
        UpdateBackpressure();
    }

    public void MarkDisplayBlockConsumed()
    {
        Interlocked.Decrement(ref _displayDepth);
        UpdateBackpressure();
    }

    private static int ReleaseQueuedBlocks(ChannelReader<AcquisitionBlock> reader, ref int depth)
    {
        int released = 0;
        while (reader.TryRead(out AcquisitionBlock? block))
        {
            block.Release();
            released++;
        }

        if (released > 0)
        {
            int remaining = Interlocked.Add(ref depth, -released);
            if (remaining < 0)
            {
                Volatile.Write(ref depth, 0);
            }
        }

        return released;
    }

    private void OnSampleReceived(SdkSampleData sample)
    {
        try
        {
            if (!IsRunning || sample.BufferCount <= 0 || sample.DataPointer == IntPtr.Zero)
            {
                return;
            }

            AcquisitionFault? fault = _continuity.Validate(sample);
            if (fault is not null)
            {
                PublishFault(fault);
                _ = StopAsync(CancellationToken.None);
                return;
            }

            int channelCount = ResolveChannelCount(sample);
            var rented = _pool.Rent(sample.BufferCount);
            NativeMemoryCopy.Copy(sample.DataPointer, rented.Pointer, sample.BufferCount);
            var block = new AcquisitionBlock(rented, sample, channelCount);
            bool storageEnabled = StorageEnabled;

            if (storageEnabled)
            {
                if (!_storageQueue.Writer.TryWrite(block))
                {
                    block.Release();
                    PublishFault(new AcquisitionFault(
                        DateTimeOffset.UtcNow,
                        "STORAGE_QUEUE_FULL",
                        "Storage queue is full. Sampling will stop to protect lossless capture.",
                        sample.MachineId));
                    _ = StopAsync(CancellationToken.None);
                    return;
                }

                Interlocked.Increment(ref _storageDepth);
            }

            Interlocked.Increment(ref _blocksReceived);
            Interlocked.Add(ref _bytesReceived, sample.BufferCount);

            if (_backpressureLevel < BackpressureLevel.PauseDisplay)
            {
                if (storageEnabled)
                {
                    block.Retain();
                }

                if (_displayQueue.Writer.TryWrite(block))
                {
                    Interlocked.Increment(ref _displayDepth);
                }
                else
                {
                    block.Release();
                    Interlocked.Increment(ref _displayDrops);
                }
            }
            else
            {
                if (!storageEnabled)
                {
                    block.Release();
                }

                Interlocked.Increment(ref _displayDrops);
            }

            UpdateBackpressure();
            if ((Interlocked.Read(ref _blocksReceived) & 0x1F) == 0)
            {
                PublishTelemetry();
            }
        }
        catch (Exception ex)
        {
            PublishFault(new AcquisitionFault(DateTimeOffset.UtcNow, "CALLBACK_ERROR", ex.Message, sample.MachineId));
        }
    }

    private int ResolveChannelCount(SdkSampleData sample)
    {
        int inferred = InferFloatChannelCount(sample);
        if (inferred > 0)
        {
            return inferred;
        }

        if (_settings.Sdk.GetDataType == GetDataType.MultiMachine)
        {
            int totalChannels = Devices.Sum(d => d.Channels.Count);
            if (totalChannels > 0)
            {
                return totalChannels;
            }
        }

        DeviceDescriptor? device = Devices.FirstOrDefault(d => d.DeviceId == sample.GroupId) ??
                                   Devices.FirstOrDefault(d => d.DeviceId == sample.MachineId);
        return Math.Max(1, device?.Channels.Count ?? 1);
    }

    private void SetDevices(IReadOnlyList<DeviceDescriptor> devices)
    {
        IReadOnlyList<DeviceDescriptor> snapshot = devices.ToList();
        lock (_devicesSync)
        {
            _devices = snapshot;
        }
    }

    private static int InferFloatChannelCount(SdkSampleData sample)
    {
        if (sample.DataCountPerChannel <= 0 || sample.BufferCount <= 0)
        {
            return 0;
        }

        int bytesPerChannel = sample.DataCountPerChannel * sizeof(float);
        if (bytesPerChannel <= 0 || sample.BufferCount % bytesPerChannel != 0)
        {
            return 0;
        }

        return sample.BufferCount / bytesPerChannel;
    }

    private void UpdateBackpressure()
    {
        int capacity = Math.Max(1, _settings.Queues.StorageCapacityBlocks);
        double storageLoad = Math.Clamp((double)Volatile.Read(ref _storageDepth) / capacity, 0, 1);
        BackpressureLevel next =
            storageLoad >= 0.90 ? BackpressureLevel.StopRequired :
            storageLoad >= 0.75 ? BackpressureLevel.PauseDisplay :
            storageLoad >= 0.50 ? BackpressureLevel.ReduceDisplay :
            BackpressureLevel.Normal;

        if (next != _backpressureLevel)
        {
            _backpressureLevel = next;
            PublishTelemetry();
            if (next == BackpressureLevel.StopRequired)
            {
                PublishFault(new AcquisitionFault(DateTimeOffset.UtcNow, "BACKPRESSURE_STOP", "Storage queue pressure exceeded 90%."));
                _ = StopAsync(CancellationToken.None);
            }
        }
    }

    private void PublishFault(AcquisitionFault fault)
    {
        lock (_sync)
        {
            _status = fault.Message;
        }

        Faulted?.Invoke(fault);
        PublishTelemetry();
    }

    private void PublishTelemetry()
    {
        TelemetryUpdated?.Invoke(GetTelemetry());
    }
}
