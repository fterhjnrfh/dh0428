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
    private IAcquisitionSource? _source;
    private long _blocksReceived;
    private long _bytesReceived;
    private long _displayDrops;
    private int _storageDepth;
    private int _displayDepth;
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
    public IReadOnlyList<DeviceDescriptor> Devices => _source?.Devices ?? Array.Empty<DeviceDescriptor>();
    public ChannelReader<AcquisitionBlock> StorageReader => _storageQueue.Reader;
    public ChannelReader<AcquisitionBlock> DisplayReader => _displayQueue.Reader;
    public bool IsRunning { get; private set; }
    public bool IsConnected => _source?.IsConnected == true;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_source is not null)
        {
            return;
        }

        _source = new DashSdkAcquisitionSource(_settings.Sdk);
        _source.SampleReceived += OnSampleReceived;
        await _source.ConnectAsync(cancellationToken).ConfigureAwait(false);
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

        _storageQueue.Writer.TryComplete();
        _displayQueue.Writer.TryComplete();
        _pool.Dispose();
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
            Interlocked.Increment(ref _blocksReceived);
            Interlocked.Add(ref _bytesReceived, sample.BufferCount);

            if (_backpressureLevel < BackpressureLevel.PauseDisplay)
            {
                block.Retain();
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
        DeviceDescriptor? device = Devices.FirstOrDefault(d => d.DeviceId == sample.GroupId) ??
                                   Devices.FirstOrDefault(d => d.DeviceId == sample.MachineId);
        return Math.Max(1, device?.Channels.Count ?? 1);
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
