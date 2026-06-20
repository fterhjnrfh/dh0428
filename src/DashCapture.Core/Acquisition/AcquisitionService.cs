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
    private readonly Channel<AcquisitionBlock> _analysisQueue;
    private readonly ContinuityTracker _continuity = new();
    private readonly object _sync = new();
    private DeviceDescriptor[] _devices = Array.Empty<DeviceDescriptor>();
    private Dictionary<int, DeviceDescriptor> _devicesById = new();
    private int _totalChannelCount;
    private IAcquisitionSource? _source;
    private long _blocksReceived;
    private long _bytesReceived;
    private long _displayDrops;
    private long _analysisDrops;
    private long _captureStartedTicks;
    private int _storageDepth;
    private int _displayDepth;
    private int _analysisDepth;
    private int _storageEnabled = 1;
    private int _analysisEnabled;
    private BackpressureLevel _backpressureLevel;
    private Func<SdkSampleData, int, bool>? _storageBlockFilter;
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
        _analysisQueue = Channel.CreateBounded<AcquisitionBlock>(new BoundedChannelOptions(Math.Max(1, settings.Queues.AnalysisCapacityBlocks))
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        _analysisEnabled = settings.Analysis.Enabled ? 1 : 0;
    }

    public event Action<AcquisitionFault>? Faulted;
    public event Action<CaptureTelemetry>? TelemetryUpdated;
    public IReadOnlyList<DeviceDescriptor> Devices
    {
        get
        {
            return Volatile.Read(ref _devices);
        }
    }
    public ChannelReader<AcquisitionBlock> StorageReader => _storageQueue.Reader;
    public ChannelReader<AcquisitionBlock> DisplayReader => _displayQueue.Reader;
    public ChannelReader<AcquisitionBlock> AnalysisReader => _analysisQueue.Reader;
    public bool IsRunning { get; private set; }
    public bool IsConnected => _source?.IsConnected == true;
    public bool StorageEnabled => Volatile.Read(ref _storageEnabled) != 0;
    public bool AnalysisEnabled => Volatile.Read(ref _analysisEnabled) != 0;

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

    public void SetStorageBlockFilter(Func<SdkSampleData, int, bool>? filter)
    {
        Interlocked.Exchange(ref _storageBlockFilter, filter);
    }

    public void SetAnalysisEnabled(bool enabled)
    {
        Volatile.Write(ref _analysisEnabled, enabled ? 1 : 0);
        if (!enabled)
        {
            ReleaseQueuedBlocks(_analysisQueue.Reader, ref _analysisDepth);
            PublishTelemetry();
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_source is not null)
        {
            return;
        }

        _source = CreateSource();
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
        _analysisDrops = 0;
        _captureStartedTicks = Environment.TickCount64;
        _storageDepth = 0;
        _displayDepth = 0;
        _analysisDepth = 0;
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

        if (string.Equals(_status, "Sampling", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_status, "Idle", StringComparison.OrdinalIgnoreCase) ||
            _status.StartsWith("Connected", StringComparison.OrdinalIgnoreCase))
        {
            _status = "Stopped";
        }

        PublishTelemetry();
    }

    public CaptureTelemetry GetTelemetry() => new(
        DateTimeOffset.UtcNow,
        Interlocked.Read(ref _blocksReceived),
        Interlocked.Read(ref _bytesReceived),
        Interlocked.Read(ref _displayDrops),
        Interlocked.Read(ref _analysisDrops),
        Volatile.Read(ref _storageDepth),
        Volatile.Read(ref _displayDepth),
        Volatile.Read(ref _analysisDepth),
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
        _analysisQueue.Writer.TryComplete();
        _pool.Dispose();
    }

    public void ReleaseQueuedBlocks()
    {
        int releasedStorage = ReleaseQueuedBlocks(_storageQueue.Reader, ref _storageDepth);
        int releasedDisplay = ReleaseQueuedBlocks(_displayQueue.Reader, ref _displayDepth);
        int releasedAnalysis = ReleaseQueuedBlocks(_analysisQueue.Reader, ref _analysisDepth);
        if (releasedStorage > 0 || releasedDisplay > 0 || releasedAnalysis > 0)
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

    public void MarkAnalysisBlockConsumed()
    {
        Interlocked.Decrement(ref _analysisDepth);
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
            bool storageEnabled = StorageEnabled;
            bool queueForStorage = storageEnabled && ShouldQueueForStorage(sample, channelCount);
            bool queueForAnalysis = AnalysisEnabled;
            bool canQueueDisplay = _backpressureLevel < BackpressureLevel.PauseDisplay;

            if (!queueForStorage && !canQueueDisplay && !queueForAnalysis)
            {
                Interlocked.Increment(ref _blocksReceived);
                Interlocked.Add(ref _bytesReceived, sample.BufferCount);
                Interlocked.Increment(ref _displayDrops);
                UpdateBackpressure();
                return;
            }

            var rented = _pool.Rent(sample.BufferCount);
            NativeMemoryCopy.Copy(sample.DataPointer, rented.Pointer, sample.BufferCount);
            var block = new AcquisitionBlock(rented, sample, channelCount);
            bool retainedForDisplay = queueForStorage && canQueueDisplay;
            bool retainedForAnalysis = queueForAnalysis && (queueForStorage || canQueueDisplay);
            if (retainedForDisplay)
            {
                block.Retain();
            }

            if (retainedForAnalysis)
            {
                block.Retain();
            }

            if (queueForStorage)
            {
                if (!TryWriteStorageBlock(block))
                {
                    if (retainedForDisplay)
                    {
                        block.Release();
                    }

                    if (retainedForAnalysis)
                    {
                        block.Release();
                    }

                    block.Release();
                    _backpressureLevel = BackpressureLevel.StopRequired;
                    PublishFault(CreateStorageQueueFullFault(sample, channelCount));
                    _ = StopAsync(CancellationToken.None);
                    return;
                }
            }

            Interlocked.Increment(ref _blocksReceived);
            Interlocked.Add(ref _bytesReceived, sample.BufferCount);

            if (canQueueDisplay)
            {
                if (TryWriteDisplayBlock(block))
                {
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

            if (queueForAnalysis)
            {
                if (!TryWriteAnalysisBlock(block))
                {
                    block.Release();
                    Interlocked.Increment(ref _analysisDrops);
                }
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

    private bool ShouldQueueForStorage(SdkSampleData sample, int channelCount)
    {
        Func<SdkSampleData, int, bool>? filter = Volatile.Read(ref _storageBlockFilter);
        return filter is null || filter(sample, channelCount);
    }

    private bool TryWriteStorageBlock(AcquisitionBlock block)
    {
        if (TryWriteStorageBlockOnce(block))
        {
            return true;
        }

        int timeoutMs = Math.Clamp(_settings.Queues.StorageWriteTimeoutMs, 0, 30000);
        if (timeoutMs <= 0)
        {
            return false;
        }

        long started = Environment.TickCount64;
        var spin = new SpinWait();
        while (IsRunning && ElapsedMilliseconds(started) < timeoutMs)
        {
            if (TryWriteStorageBlockOnce(block))
            {
                return true;
            }

            if (spin.Count < 10)
            {
                spin.SpinOnce();
            }
            else
            {
                Thread.Sleep(1);
            }
        }

        return false;
    }

    private bool TryWriteDisplayBlock(AcquisitionBlock block)
    {
        Interlocked.Increment(ref _displayDepth);
        if (_displayQueue.Writer.TryWrite(block))
        {
            return true;
        }

        Interlocked.Decrement(ref _displayDepth);
        return false;
    }

    private bool TryWriteAnalysisBlock(AcquisitionBlock block)
    {
        Interlocked.Increment(ref _analysisDepth);
        if (_analysisQueue.Writer.TryWrite(block))
        {
            return true;
        }

        Interlocked.Decrement(ref _analysisDepth);
        return false;
    }

    private bool TryWriteStorageBlockOnce(AcquisitionBlock block)
    {
        Interlocked.Increment(ref _storageDepth);
        if (_storageQueue.Writer.TryWrite(block))
        {
            return true;
        }

        Interlocked.Decrement(ref _storageDepth);
        return false;
    }

    private AcquisitionFault CreateStorageQueueFullFault(SdkSampleData sample, int channelCount)
    {
        int storageDepth = Volatile.Read(ref _storageDepth);
        int displayDepth = Volatile.Read(ref _displayDepth);
        int analysisDepth = Volatile.Read(ref _analysisDepth);
        int storageCapacity = Math.Max(1, _settings.Queues.StorageCapacityBlocks);
        int displayCapacity = Math.Max(1, _settings.Queues.DisplayCapacityBlocks);
        int analysisCapacity = Math.Max(1, _settings.Queues.AnalysisCapacityBlocks);
        int timeoutMs = Math.Clamp(_settings.Queues.StorageWriteTimeoutMs, 0, 30000);
        double blockMb = sample.BufferCount / 1024.0 / 1024.0;
        double elapsedSeconds = Math.Max(0.001, ElapsedMilliseconds(Volatile.Read(ref _captureStartedTicks)) / 1000.0);
        double ingressMbPerSecond = Interlocked.Read(ref _bytesReceived) / 1024.0 / 1024.0 / elapsedSeconds;

        string message =
            $"Storage queue is full after waiting {timeoutMs} ms. Sampling will stop to protect lossless storage. " +
            $"StorageQ {storageDepth}/{storageCapacity}, DisplayQ {displayDepth}/{displayCapacity}, AnalysisQ {analysisDepth}/{analysisCapacity}, " +
            $"Block {blockMb:0.0} MB, Channels {channelCount}, SamplesPerChannel {sample.DataCountPerChannel}, " +
            $"Ingress {ingressMbPerSecond:0.0} MB/s.";

        return new AcquisitionFault(
            DateTimeOffset.UtcNow,
            "STORAGE_QUEUE_FULL",
            message,
            sample.MachineId);
    }

    private static long ElapsedMilliseconds(long startedTicks)
    {
        return Math.Max(0, Environment.TickCount64 - startedTicks);
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
            int totalChannels = Volatile.Read(ref _totalChannelCount);
            if (totalChannels > 0)
            {
                return totalChannels;
            }
        }

        DeviceDescriptor? device = ResolveDevice(sample.GroupId, sample.MachineId);
        return Math.Max(1, device?.Channels.Count ?? 1);
    }

    private IAcquisitionSource CreateSource()
    {
        return _settings.Acquisition.Source switch
        {
            AcquisitionSourceMode.RemoteDataSource => new RemoteDataSourceAcquisitionSource(_settings.DataSource),
            _ => new DashSdkAcquisitionSource(_settings.Sdk)
        };
    }

    private void SetDevices(IReadOnlyList<DeviceDescriptor> devices)
    {
        DeviceDescriptor[] snapshot = devices.ToArray();
        var devicesById = new Dictionary<int, DeviceDescriptor>(snapshot.Length * 2);
        int totalChannels = 0;
        foreach (DeviceDescriptor device in snapshot)
        {
            devicesById[device.DeviceId] = device;
            totalChannels += device.Channels.Count;
        }

        Volatile.Write(ref _devicesById, devicesById);
        Volatile.Write(ref _totalChannelCount, totalChannels);
        Volatile.Write(ref _devices, snapshot);
    }

    private DeviceDescriptor? ResolveDevice(int groupId, int machineId)
    {
        Dictionary<int, DeviceDescriptor> devicesById = Volatile.Read(ref _devicesById);
        if (devicesById.TryGetValue(groupId, out DeviceDescriptor? device))
        {
            return device;
        }

        return devicesById.TryGetValue(machineId, out device) ? device : null;
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
        int capacity = Math.Max(1, _settings.Queues.DisplayCapacityBlocks);
        double displayLoad = Math.Clamp((double)Volatile.Read(ref _displayDepth) / capacity, 0, 1);
        BackpressureLevel next =
            displayLoad >= 0.90 ? BackpressureLevel.PauseDisplay :
            displayLoad >= 0.60 ? BackpressureLevel.ReduceDisplay :
            BackpressureLevel.Normal;

        if (next != _backpressureLevel)
        {
            _backpressureLevel = next;
            PublishTelemetry();
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
