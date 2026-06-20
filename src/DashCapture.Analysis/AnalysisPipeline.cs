using DashCapture.Core.Acquisition;
using DashCapture.Core.Configuration;
using DashCapture.Core.Memory;
using DashCapture.Core.Models;
using System.Buffers;
using System.Diagnostics;
using System.Threading.Channels;

namespace DashCapture.Analysis;

public sealed class AnalysisPipeline : IAsyncDisposable
{
    private readonly AcquisitionService _acquisition;
    private readonly AnalysisSettings _settings;
    private readonly Func<IReadOnlyList<DeviceDescriptor>> _devicesProvider;
    private readonly Dictionary<ChannelKey, ChannelWindowAccumulator> _accumulators = new();
    private readonly HashSet<ChannelKey> _fftChannels = new();
    private readonly HashSet<ChannelKey> _fftRejectedChannels = new();
    private readonly CpuFftProcessor _cpuFft = new();
    private readonly GpuFftProcessor _gpuFft = new();
    private readonly object _fftSync = new();
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _fftWorkerCts;
    private Task? _worker;
    private Task? _fftWorker;
    private Channel<FftWindowWorkItem>? _fftWindowQueue;
    private FftResultWriter? _resultWriter;
    private long _blocksProcessed;
    private long _bytesProcessed;
    private long _channelSamplesProcessed;
    private long _completedWindows;
    private long _fftFramesProcessed;
    private long _fftBytesWritten;
    private long _fftWindowsSkippedByChannelLimit;
    private long _fftWindowsQueued;
    private long _fftWindowsDropped;
    private long _fftBatchesProcessed;
    private long _fftCopyTicks;
    private long _fftComputeTicks;
    private long _fftWriteTicks;
    private long _startedTicks;
    private int _activeChannels;
    private int _fftQueueDepth;
    private string _lastResultPath = string.Empty;
    private string _lastFftBackend = "FFT disabled";
    private string _lastFftError = string.Empty;

    public AnalysisPipeline(
        AcquisitionService acquisition,
        AnalysisSettings settings,
        Func<IReadOnlyList<DeviceDescriptor>> devicesProvider)
    {
        _acquisition = acquisition;
        _settings = settings;
        _devicesProvider = devicesProvider;
    }

    public event Action<AcquisitionFault>? Faulted;
    public bool IsRunning =>
        (_worker is not null && !_worker.IsCompleted) ||
        (_fftWorker is not null && !_fftWorker.IsCompleted);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_settings.Enabled || IsRunning)
        {
            return Task.CompletedTask;
        }

        ResetStatistics();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_settings.ComputeFft && _settings.PersistResults)
        {
            _fftWorkerCts = new CancellationTokenSource();
            _fftWindowQueue = Channel.CreateBounded<FftWindowWorkItem>(new BoundedChannelOptions(Math.Max(1, _settings.FftWindowQueueCapacity))
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });
            _fftWorker = Task.Run(
                () => ConsumeFftWindowsAsync(_fftWindowQueue.Reader, _fftWorkerCts.Token),
                CancellationToken.None);
        }

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
        if (_fftWindowQueue is not null)
        {
            _fftWindowQueue.Writer.TryComplete();
        }

        if (_fftWorker is not null)
        {
            Task completed = await Task.WhenAny(
                _fftWorker,
                Task.Delay(Math.Max(0, _settings.FftDrainTimeoutMs))).ConfigureAwait(false);
            if (!ReferenceEquals(completed, _fftWorker))
            {
                _fftWorkerCts?.Cancel();
            }

            try
            {
                await _fftWorker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _fftWorkerCts?.Dispose();
        _fftWorkerCts = null;
        _fftWorker = null;
        DrainAndReleaseFftQueue();
        _fftWindowQueue = null;
        DisposeResultWriter();
    }

    public AnalysisPipelineStatistics GetStatistics()
    {
        long elapsedMs = Math.Max(1, Environment.TickCount64 - Volatile.Read(ref _startedTicks));
        double elapsedSeconds = elapsedMs / 1000.0;
        long bytes = Interlocked.Read(ref _bytesProcessed);
        long windows = Interlocked.Read(ref _completedWindows);
        int fftChannelCount;
        int fftRejectedChannelCount;
        lock (_fftSync)
        {
            fftChannelCount = _fftChannels.Count;
            fftRejectedChannelCount = _fftRejectedChannels.Count;
        }

        return new AnalysisPipelineStatistics(
            Interlocked.Read(ref _blocksProcessed),
            bytes,
            Interlocked.Read(ref _channelSamplesProcessed),
            windows,
            Volatile.Read(ref _activeChannels),
            bytes / 1024.0 / 1024.0 / elapsedSeconds,
            windows / elapsedSeconds,
            Interlocked.Read(ref _fftFramesProcessed),
            Interlocked.Read(ref _fftBytesWritten),
            fftChannelCount,
            fftRejectedChannelCount,
            Interlocked.Read(ref _fftWindowsSkippedByChannelLimit),
            Volatile.Read(ref _fftQueueDepth),
            Math.Max(1, _settings.FftWindowQueueCapacity),
            Interlocked.Read(ref _fftWindowsQueued),
            Interlocked.Read(ref _fftWindowsDropped),
            Interlocked.Read(ref _fftBatchesProcessed),
            TicksToMillisecondsPerSecond(Interlocked.Read(ref _fftCopyTicks), elapsedSeconds),
            TicksToMillisecondsPerSecond(Interlocked.Read(ref _fftComputeTicks), elapsedSeconds),
            TicksToMillisecondsPerSecond(Interlocked.Read(ref _fftWriteTicks), elapsedSeconds),
            _resultWriter?.CurrentPath ?? _lastResultPath,
            _lastFftBackend,
            _lastFftError);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gpuFft.Dispose();
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        await foreach (AcquisitionBlock block in _acquisition.AnalysisReader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                Process(block);
            }
            catch (Exception ex)
            {
                Faulted?.Invoke(new AcquisitionFault(DateTimeOffset.UtcNow, "ANALYSIS_PROCESS_FAILED", ex.Message, block.Header.MachineId));
            }
            finally
            {
                block.Release();
                _acquisition.MarkAnalysisBlockConsumed();
            }
        }
    }

    private unsafe void Process(AcquisitionBlock block)
    {
        IReadOnlyList<DeviceDescriptor> devices = _devicesProvider();
        if (devices.Count == 0 || block.Header.DataCountPerChannel <= 0)
        {
            return;
        }

        int sampleCount = block.Header.DataCountPerChannel;
        int channelCount = Math.Max(1, block.ChannelCount);
        int maxChannels = Math.Max(1, _settings.MaxChannels);
        bool globalBlock = IsGlobalMultiDeviceBlock(block, devices);
        DeviceDescriptor? device = globalBlock
            ? null
            : ResolveDevice(devices, block.Header.GroupId, block.Header.MachineId);
        bool singleChannelBlock = !globalBlock && IsExplicitSingleChannelBlock(block);

        if (!globalBlock && device is null)
        {
            return;
        }

        int activeChannels = 0;
        long channelSamples = 0;
        long completedWindows = 0;
        if (globalBlock)
        {
            for (int deviceIndex = 0; deviceIndex < devices.Count && activeChannels < maxChannels; deviceIndex++)
            {
                DeviceDescriptor current = devices[deviceIndex];
                for (int channelIndex = 0; channelIndex < current.Channels.Count && activeChannels < maxChannels; channelIndex++)
                {
                    ChannelDescriptor channel = current.Channels[channelIndex];
                    if (!channel.Online)
                    {
                        continue;
                    }

                    int dataIndex = channel.DataIndex;
                    if (!IsValidDataIndex(dataIndex, channelCount))
                    {
                        continue;
                    }

                    completedWindows += ProcessChannel(block, channel, sampleCount, channelCount, dataIndex);
                    channelSamples += sampleCount;
                    activeChannels++;
                }
            }
        }
        else
        {
            foreach (ChannelDescriptor channel in device!.Channels)
            {
                if (activeChannels >= maxChannels)
                {
                    break;
                }

                if (!channel.Online)
                {
                    continue;
                }

                if (singleChannelBlock && channel.ChannelId != block.Header.ChannelId)
                {
                    continue;
                }

                int dataIndex = singleChannelBlock
                    ? 0
                    : block.Header.Layout == SampleDataLayout.ChannelContiguousFloat32 ? channel.LocalDataIndex : channel.DataIndex;
                if (!IsValidDataIndex(dataIndex, channelCount))
                {
                    continue;
                }

                completedWindows += ProcessChannel(block, channel, sampleCount, channelCount, dataIndex);
                channelSamples += sampleCount;
                activeChannels++;
            }
        }

        Interlocked.Increment(ref _blocksProcessed);
        Interlocked.Add(ref _bytesProcessed, block.Length);
        Interlocked.Add(ref _channelSamplesProcessed, channelSamples);
        Interlocked.Add(ref _completedWindows, completedWindows);
        Volatile.Write(ref _activeChannels, _accumulators.Count);
    }

    private unsafe long ProcessChannel(
        AcquisitionBlock block,
        ChannelDescriptor channel,
        int sampleCount,
        int channelCount,
        int dataIndex)
    {
        bool wantsFft = _settings.ComputeFft && _settings.PersistResults;
        ChannelWindowAccumulator accumulator = GetAccumulator(channel, wantsFft);
        Action<ChannelWindowAccumulator>? windowCompleted = wantsFft
            ? completedAccumulator => ProcessFftWindow(block, channel, completedAccumulator)
            : null;
        float* source = (float*)block.DataPointer.ToPointer();
        if (block.Header.Layout == SampleDataLayout.ChannelContiguousFloat32)
        {
            return accumulator.Append(
                new ReadOnlySpan<float>(source + (dataIndex * sampleCount), sampleCount),
                windowCompleted);
        }

        return accumulator.AppendInterleaved(source, sampleCount, channelCount, dataIndex, windowCompleted);
    }

    private ChannelWindowAccumulator GetAccumulator(ChannelDescriptor channel, bool keepWindowSamples)
    {
        var key = new ChannelKey(channel);
        if (_accumulators.TryGetValue(key, out ChannelWindowAccumulator? accumulator))
        {
            return accumulator;
        }

        FftWindowParameters windowParameters = FftWindowPlanner.Resolve(_settings, channel);
        accumulator = new ChannelWindowAccumulator(
            windowParameters.WindowSampleCount,
            windowParameters.HopSampleCount,
            _settings.KeepWindowSamples || keepWindowSamples);
        _accumulators[key] = accumulator;
        return accumulator;
    }

    private bool ShouldComputeFft(ChannelDescriptor channel)
    {
        if (!_settings.ComputeFft || !_settings.PersistResults)
        {
            return false;
        }

        var key = new ChannelKey(channel);
        lock (_fftSync)
        {
            if (_fftChannels.Contains(key))
            {
                return true;
            }

            int maxFftChannels = _settings.MaxFftChannels;
            if (maxFftChannels > 0 && _fftChannels.Count >= maxFftChannels)
            {
                _fftRejectedChannels.Add(key);
                Interlocked.Increment(ref _fftWindowsSkippedByChannelLimit);
                return false;
            }

            _fftChannels.Add(key);
            return true;
        }
    }

    private void ProcessFftWindow(
        AcquisitionBlock block,
        ChannelDescriptor channel,
        ChannelWindowAccumulator accumulator)
    {
        int fftSize = accumulator.WindowSampleCount;
        float[] window = ArrayPool<float>.Shared.Rent(fftSize);
        bool queued = false;
        try
        {
            if (!ShouldComputeFft(channel))
            {
                return;
            }

            long copyStarted = Stopwatch.GetTimestamp();
            if (!accumulator.CopyCurrentWindowTo(window.AsSpan(0, fftSize)))
            {
                return;
            }

            AddTicks(ref _fftCopyTicks, Stopwatch.GetTimestamp() - copyStarted);

            Channel<FftWindowWorkItem>? queue = _fftWindowQueue;
            if (queue is null)
            {
                Interlocked.Increment(ref _fftWindowsDropped);
                return;
            }

            var item = new FftWindowWorkItem(
                channel,
                block.Header.SampleTime,
                accumulator.CompletedWindows,
                accumulator.TotalSamples,
                fftSize,
                window);
            if (!queue.Writer.TryWrite(item))
            {
                Interlocked.Increment(ref _fftWindowsDropped);
                return;
            }

            queued = true;
            Interlocked.Increment(ref _fftWindowsQueued);
            Interlocked.Increment(ref _fftQueueDepth);
        }
        finally
        {
            if (!queued)
            {
                ArrayPool<float>.Shared.Return(window);
            }
        }
    }

    private async Task ConsumeFftWindowsAsync(ChannelReader<FftWindowWorkItem> reader, CancellationToken cancellationToken)
    {
        int batchSize = Math.Max(1, _settings.FftBatchSize);
        var batch = new List<FftWindowWorkItem>(batchSize);
        try
        {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                batch.Clear();
                while (batch.Count < batchSize && reader.TryRead(out FftWindowWorkItem? item))
                {
                    Interlocked.Decrement(ref _fftQueueDepth);
                    batch.Add(item);
                }

                if (batch.Count == 0)
                {
                    continue;
                }

                ProcessFftBatch(batch);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            while (reader.TryRead(out FftWindowWorkItem? item))
            {
                Interlocked.Decrement(ref _fftQueueDepth);
                item.Release();
            }
        }
    }

    private void ProcessFftBatch(List<FftWindowWorkItem> batch)
    {
        batch.Sort(static (left, right) => left.FftSize.CompareTo(right.FftSize));
        Interlocked.Increment(ref _fftBatchesProcessed);
        int start = 0;
        while (start < batch.Count)
        {
            int fftSize = batch[start].FftSize;
            int end = start + 1;
            while (end < batch.Count && batch[end].FftSize == fftSize)
            {
                end++;
            }

            int count = end - start;
            if (!TryProcessGpuFftBatchGroup(batch, start, count))
            {
                for (int i = start; i < end; i++)
                {
                    ProcessQueuedFftWindow(batch[i]);
                }
            }

            start = end;
        }
    }

    private bool TryProcessGpuFftBatchGroup(List<FftWindowWorkItem> batch, int start, int count)
    {
        if (_settings.FftBackend == FftComputeBackend.Cpu || count <= 0)
        {
            return false;
        }

        if (!_gpuFft.IsAvailable)
        {
            _lastFftError = _gpuFft.AvailabilityError;
            if (_settings.FftBackend == FftComputeBackend.Gpu)
            {
                FaultAndReleaseFftGroup(batch, start, count, $"GPU FFT requested but CUDA/cuFFT is not available: {_gpuFft.AvailabilityError}");
                return true;
            }

            return false;
        }

        int fftSize = batch[start].FftSize;
        int binCount = fftSize / 2 + 1;
        int sampleCount = checked(fftSize * count);
        int magnitudeCount = checked(binCount * count);
        float[] sampleBuffer = ArrayPool<float>.Shared.Rent(sampleCount);
        float[] magnitudeBuffer = ArrayPool<float>.Shared.Rent(magnitudeCount);
        try
        {
            long computeStarted = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                FftWindowWorkItem item = batch[start + i];
                item.Window.AsSpan(0, fftSize).CopyTo(sampleBuffer.AsSpan(i * fftSize, fftSize));
            }

            if (!_gpuFft.TryComputeMagnitudeBatch(
                    sampleBuffer.AsSpan(0, sampleCount),
                    fftSize,
                    count,
                    magnitudeBuffer.AsSpan(0, magnitudeCount),
                    out string error))
            {
                AddTicks(ref _fftComputeTicks, Stopwatch.GetTimestamp() - computeStarted);
                _lastFftError = error;
                if (_settings.FftBackend == FftComputeBackend.Gpu)
                {
                    FaultAndReleaseFftGroup(batch, start, count, $"GPU FFT batch failed: {error}");
                    return true;
                }

                return false;
            }

            AddTicks(ref _fftComputeTicks, Stopwatch.GetTimestamp() - computeStarted);
            string device = string.IsNullOrWhiteSpace(_gpuFft.DeviceName) ? "CUDA" : _gpuFft.DeviceName;
            var execution = new FftComputeExecution("CUDA/cuFFT batch", device);
            _lastFftBackend = execution.DisplayName;
            _lastFftError = string.Empty;

            long writeStarted = Stopwatch.GetTimestamp();
            FftResultWriter writer = EnsureResultWriter(execution);
            for (int i = 0; i < count; i++)
            {
                FftWindowWorkItem item = batch[start + i];
                writer.WriteFrame(
                    item.Channel,
                    item.SourceSampleTime,
                    item.WindowIndex,
                    item.WindowEndSample,
                    item.FftSize,
                    execution.Backend,
                    execution.Device,
                    magnitudeBuffer.AsSpan(i * binCount, binCount));
                Interlocked.Increment(ref _fftFramesProcessed);
                item.Release();
            }

            AddTicks(ref _fftWriteTicks, Stopwatch.GetTimestamp() - writeStarted);
            Interlocked.Exchange(ref _fftBytesWritten, writer.BytesWritten);
            return true;
        }
        catch (Exception ex)
        {
            FaultAndReleaseFftGroup(batch, start, count, ex.Message);
            return true;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(sampleBuffer);
            ArrayPool<float>.Shared.Return(magnitudeBuffer);
        }
    }

    private void FaultAndReleaseFftGroup(List<FftWindowWorkItem> batch, int start, int count, string message)
    {
        FftWindowWorkItem first = batch[start];
        Faulted?.Invoke(new AcquisitionFault(DateTimeOffset.UtcNow, "ANALYSIS_FFT_BATCH_FAILED", message, first.Channel.DeviceId));
        for (int i = 0; i < count; i++)
        {
            batch[start + i].Release();
        }
    }

    private void ProcessQueuedFftWindow(FftWindowWorkItem item)
    {
        int binCount = item.FftSize / 2 + 1;
        float[] magnitudes = ArrayPool<float>.Shared.Rent(binCount);
        try
        {
            long computeStarted = Stopwatch.GetTimestamp();
            FftComputeExecution execution = ComputeMagnitude(
                item.Window.AsSpan(0, item.FftSize),
                magnitudes.AsSpan(0, binCount));
            AddTicks(ref _fftComputeTicks, Stopwatch.GetTimestamp() - computeStarted);

            long writeStarted = Stopwatch.GetTimestamp();
            FftResultWriter writer = EnsureResultWriter(execution);
            writer.WriteFrame(
                item.Channel,
                item.SourceSampleTime,
                item.WindowIndex,
                item.WindowEndSample,
                item.FftSize,
                execution.Backend,
                execution.Device,
                magnitudes.AsSpan(0, binCount));
            AddTicks(ref _fftWriteTicks, Stopwatch.GetTimestamp() - writeStarted);
            Interlocked.Increment(ref _fftFramesProcessed);
            Interlocked.Exchange(ref _fftBytesWritten, writer.BytesWritten);
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(new AcquisitionFault(DateTimeOffset.UtcNow, "ANALYSIS_FFT_FAILED", ex.Message, item.Channel.DeviceId));
        }
        finally
        {
            ArrayPool<float>.Shared.Return(magnitudes);
            item.Release();
        }
    }

    private FftComputeExecution ComputeMagnitude(ReadOnlySpan<float> window, Span<float> magnitudes)
    {
        if (_settings.FftBackend == FftComputeBackend.Cpu)
        {
            _cpuFft.ComputeMagnitude(window, magnitudes);
            var execution = new FftComputeExecution("CPU FFT", "CPU");
            _lastFftBackend = execution.DisplayName;
            _lastFftError = string.Empty;
            return execution;
        }

        if (_gpuFft.TryComputeMagnitude(window, magnitudes, out string error))
        {
            string device = string.IsNullOrWhiteSpace(_gpuFft.DeviceName) ? "CUDA" : _gpuFft.DeviceName;
            var execution = new FftComputeExecution("CUDA/cuFFT", device);
            _lastFftBackend = execution.DisplayName;
            _lastFftError = string.Empty;
            return execution;
        }

        _lastFftError = error;
        if (_settings.FftBackend == FftComputeBackend.Gpu)
        {
            throw new InvalidOperationException($"GPU FFT requested but CUDA/cuFFT is not available: {error}");
        }

        _cpuFft.ComputeMagnitude(window, magnitudes);
        var fallback = new FftComputeExecution("CPU FFT fallback", "CPU");
        _lastFftBackend = fallback.DisplayName;
        return fallback;
    }

    private FftResultWriter EnsureResultWriter(FftComputeExecution execution)
    {
        if (_resultWriter is not null)
        {
            return _resultWriter;
        }

        _resultWriter = new FftResultWriter(_settings, execution.Backend, execution.Device);
        _lastResultPath = _resultWriter.CurrentPath;
        return _resultWriter;
    }

    private void ResetStatistics()
    {
        DisposeResultWriter();
        _accumulators.Clear();
        lock (_fftSync)
        {
            _fftChannels.Clear();
            _fftRejectedChannels.Clear();
        }

        Interlocked.Exchange(ref _blocksProcessed, 0);
        Interlocked.Exchange(ref _bytesProcessed, 0);
        Interlocked.Exchange(ref _channelSamplesProcessed, 0);
        Interlocked.Exchange(ref _completedWindows, 0);
        Interlocked.Exchange(ref _fftFramesProcessed, 0);
        Interlocked.Exchange(ref _fftBytesWritten, 0);
        Interlocked.Exchange(ref _fftWindowsSkippedByChannelLimit, 0);
        Interlocked.Exchange(ref _fftWindowsQueued, 0);
        Interlocked.Exchange(ref _fftWindowsDropped, 0);
        Interlocked.Exchange(ref _fftBatchesProcessed, 0);
        Interlocked.Exchange(ref _fftCopyTicks, 0);
        Interlocked.Exchange(ref _fftComputeTicks, 0);
        Interlocked.Exchange(ref _fftWriteTicks, 0);
        Volatile.Write(ref _activeChannels, 0);
        Volatile.Write(ref _fftQueueDepth, 0);
        Volatile.Write(ref _startedTicks, Environment.TickCount64);
        _lastResultPath = string.Empty;
        _lastFftBackend = _settings.ComputeFft ? "FFT waiting" : "FFT disabled";
        _lastFftError = string.Empty;
    }

    private void DisposeResultWriter()
    {
        if (_resultWriter is null)
        {
            return;
        }

        _resultWriter.Dispose();
        _resultWriter = null;
    }

    private void DrainAndReleaseFftQueue()
    {
        Channel<FftWindowWorkItem>? queue = _fftWindowQueue;
        if (queue is null)
        {
            return;
        }

        while (queue.Reader.TryRead(out FftWindowWorkItem? item))
        {
            Interlocked.Decrement(ref _fftQueueDepth);
            item.Release();
        }
    }

    private static bool IsGlobalMultiDeviceBlock(AcquisitionBlock block, IReadOnlyList<DeviceDescriptor> devices)
    {
        int totalChannelCount = 0;
        for (int i = 0; i < devices.Count; i++)
        {
            totalChannelCount += devices[i].Channels.Count;
        }

        return block.Header.MessageType == DashSampleMessageType.AnalogMultiChannelData ||
               block.Header.GroupId < 0 ||
               block.Header.MachineId < 0 ||
               (devices.Count > 1 &&
                totalChannelCount > 0 &&
                block.ChannelCount == totalChannelCount);
    }

    private static DeviceDescriptor? ResolveDevice(IReadOnlyList<DeviceDescriptor> devices, int groupId, int machineId)
    {
        DeviceDescriptor? machineMatch = null;
        for (int i = 0; i < devices.Count; i++)
        {
            DeviceDescriptor device = devices[i];
            if (device.DeviceId == groupId)
            {
                return device;
            }

            if (device.DeviceId == machineId)
            {
                machineMatch = device;
            }
        }

        return machineMatch;
    }

    private static bool IsExplicitSingleChannelBlock(AcquisitionBlock block)
    {
        return block.ChannelCount == 1 && block.Header.ChannelId >= 0;
    }

    private static bool IsValidDataIndex(int dataIndex, int channelCount)
    {
        return dataIndex >= 0 && dataIndex < channelCount;
    }

    private static void AddTicks(ref long target, long ticks)
    {
        if (ticks > 0)
        {
            Interlocked.Add(ref target, ticks);
        }
    }

    private static double TicksToMillisecondsPerSecond(long ticks, double elapsedSeconds)
    {
        if (ticks <= 0 || elapsedSeconds <= 0)
        {
            return 0;
        }

        return ticks * 1000.0 / Stopwatch.Frequency / elapsedSeconds;
    }
}

internal readonly record struct FftComputeExecution(string Backend, string Device)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Device) || string.Equals(Device, Backend, StringComparison.OrdinalIgnoreCase)
        ? Backend
        : $"{Backend} ({Device})";
}

internal sealed class FftWindowWorkItem
{
    private float[]? _window;

    public FftWindowWorkItem(
        ChannelDescriptor channel,
        long sourceSampleTime,
        long windowIndex,
        long windowEndSample,
        int fftSize,
        float[] window)
    {
        Channel = channel;
        SourceSampleTime = sourceSampleTime;
        WindowIndex = windowIndex;
        WindowEndSample = windowEndSample;
        FftSize = fftSize;
        _window = window;
    }

    public ChannelDescriptor Channel { get; }
    public long SourceSampleTime { get; }
    public long WindowIndex { get; }
    public long WindowEndSample { get; }
    public int FftSize { get; }
    public float[] Window => _window ?? throw new ObjectDisposedException(nameof(FftWindowWorkItem));

    public void Release()
    {
        float[]? window = Interlocked.Exchange(ref _window, null);
        if (window is not null)
        {
            ArrayPool<float>.Shared.Return(window);
        }
    }
}
