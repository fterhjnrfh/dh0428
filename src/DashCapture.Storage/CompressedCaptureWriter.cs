using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DashCapture.Core.Configuration;
using DashCapture.Core.Memory;
using DashCapture.Core.Models;

namespace DashCapture.Storage;

public sealed class CompressedCaptureWriter : ICaptureStorageWriter
{
    private readonly StorageSettings _settings;
    private readonly CompressionSettings _compressionSettings;
    private readonly IReadOnlyList<DeviceDescriptor> _devices;
    private readonly Dictionary<ChannelKey, CompressedCaptureChannelManifest> _channels = new();
    private readonly Dictionary<ChannelKey, ChannelWriteBuffer> _pendingBuffers = new();
    private readonly Dictionary<ChannelKey, ulong> _sampleCounts = new();
    private readonly HashSet<ChannelKey> _initializedChannels = new();
    private readonly List<CompressedCaptureRecordIndex> _recordIndex = new();
    private readonly Dictionary<int, float> _latestSampleRates = new();
    private readonly object _statsSync = new();
    private readonly object _writeSync = new();
    private readonly object _faultSync = new();
    private readonly BlockingCollection<ChannelCompressionJob> _compressionQueue;
    private readonly BlockingCollection<CompressedChannelChunk> _writeQueue;
    private readonly CancellationTokenSource _pipelineCts = new();
    private readonly CancellationTokenSource _checkpointCts = new();
    private readonly Task[] _compressionWorkers;
    private readonly Task _writeWorker;
    private readonly Task _checkpointWorker;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private readonly int _totalChannelCount;
    private BinaryWriter? _writer;
    private FileStream? _stream;
    private RawBlockAuditWriter? _auditWriter;
    private int _fileIndex;
    private string _currentPath = string.Empty;
    private readonly string _runFolder;
    private readonly string _runStem;
    private long _segmentPayloadBytes;
    private long _segmentStoredBytes;
    private long _completedFileBytes;
    private long _totalRawBytes;
    private long _writtenRawBytes;
    private long _totalPayloadBytes;
    private long _totalBlocks;
    private long _compressedBlocks;
    private long _storedBlocks;
    private long _rawStoredBlocks;
    private long _queuedCompressionJobs;
    private long _writtenCompressionJobs;
    private long _lastDurableRawBytes;
    private long _lastDurableWrittenBytes;
    private long _lastDurableCheckpointTicks;
    private int _compressionQueueDepth;
    private int _writeQueueDepth;
    private Exception? _pipelineFault;
    private bool _footerWritten;
    private bool _rollPending;
    private bool _disposed;

    public CompressedCaptureWriter(StorageSettings settings, IReadOnlyList<DeviceDescriptor> devices)
    {
        _settings = settings;
        _compressionSettings = CloneCompressionSettings(settings.Compression);
        _devices = devices;
        _totalChannelCount = devices.Sum(device => device.Channels.Count);
        _compressionQueue = new BlockingCollection<ChannelCompressionJob>(QueueCapacity(settings.CompressionQueueCapacityBlocks));
        _writeQueue = new BlockingCollection<CompressedChannelChunk>(QueueCapacity(settings.WriteQueueCapacityBlocks));
        Directory.CreateDirectory(settings.RootPath);
        _runStem = CreateRunStem();
        _runFolder = CreateUniqueDirectory(settings.RootPath, _runStem);
        OpenNextFile();
        _compressionWorkers = StartCompressionWorkers();
        _writeWorker = Task.Run(WriteLoop);
        _checkpointWorker = Task.Run(CheckpointLoop);
    }

    public string CurrentPath => _currentPath;
    public string CurrentFolder => _runFolder;
    public string CurrentAuditPath => _auditWriter?.CurrentPath ?? string.Empty;
    public CaptureStorageStatistics Statistics => CreateStatistics();
    public event Action<Exception, string>? Faulted;

    public void AppendBlock(AcquisitionBlock block)
    {
        ThrowIfPipelineFault();
        if (_rollPending)
        {
            OpenNextFile();
            _rollPending = false;
        }

        if (_writer is null)
        {
            return;
        }

        _auditWriter?.Write(block);
        int sampleCount = block.Header.DataCountPerChannel;
        if (sampleCount <= 0)
        {
            return;
        }

        if (IsGlobalBlock(block))
        {
            AppendGlobalBlock(block, sampleCount);
            return;
        }

        DeviceDescriptor? device = ResolveDevice(block.Header);
        if (device is null || device.Channels.Count == 0)
        {
            return;
        }

        AppendDeviceBlock(block, device, sampleCount);
        RollFileIfNeeded();
    }

    public void UpdateDeviceRates(IReadOnlyList<DeviceDescriptor> devices)
    {
        foreach (DeviceDescriptor device in devices)
        {
            if (IsValidSampleRate(device.SampleRate))
            {
                _latestSampleRates[device.DeviceId] = device.SampleRate;
            }
        }
    }

    public void Save()
    {
        ThrowIfPipelineFault();
        FlushAllChannelBuffers();
        long checkpointTarget = Volatile.Read(ref _queuedCompressionJobs);
        long checkpointRawBytes = ReadTotalRawBytes();
        _auditWriter?.Flush();
        DrainPipelineTo(checkpointTarget);
        lock (_writeSync)
        {
            _writer?.Flush();
            _stream?.Flush(flushToDisk: true);
            UpdateDurableCheckpoint(checkpointRawBytes);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            FinalizeCurrentFile();
        }
        finally
        {
            StopPipeline();
            _pipelineCts.Dispose();
            _checkpointCts.Dispose();
        }
    }

    private unsafe void AppendGlobalBlock(AcquisitionBlock block, int sampleCount)
    {
        int channelCount = Math.Max(1, block.ChannelCount);
        if (block.Header.Layout == SampleDataLayout.ChannelContiguousFloat32)
        {
            float* source = (float*)block.DataPointer.ToPointer();
            foreach (DeviceDescriptor device in _devices)
            {
                foreach (ChannelDescriptor channel in device.Channels)
                {
                    int dataIndex = channel.DataIndex;
                    if (dataIndex < 0 || dataIndex >= channelCount)
                    {
                        continue;
                    }

                    AppendChannel(channel, new ReadOnlySpan<float>(source + (dataIndex * sampleCount), sampleCount), block);
                }
            }

            RollFileIfNeeded();
            return;
        }

        float[] scratch = new float[sampleCount];

        foreach (DeviceDescriptor device in _devices)
        {
            foreach (ChannelDescriptor channel in device.Channels)
            {
                int dataIndex = channel.DataIndex;
                if (dataIndex < 0 || dataIndex >= channelCount)
                {
                    continue;
                }

                NativeDeinterleaver.CopyFloatChannel(block.DataPointer, sampleCount, channelCount, dataIndex, scratch, block.Header.Layout);
                AppendChannel(channel, scratch.AsSpan(0, sampleCount), block);
            }
        }

        RollFileIfNeeded();
    }

    private unsafe void AppendDeviceBlock(AcquisitionBlock block, DeviceDescriptor device, int sampleCount)
    {
        int channelCount = Math.Max(1, block.ChannelCount);
        if (block.Header.Layout == SampleDataLayout.ChannelContiguousFloat32)
        {
            float* source = (float*)block.DataPointer.ToPointer();
            foreach (ChannelDescriptor channel in device.Channels)
            {
                int dataIndex = ResolveDataIndex(block, channel);
                if (dataIndex < 0 || dataIndex >= channelCount)
                {
                    continue;
                }

                AppendChannel(channel, new ReadOnlySpan<float>(source + (dataIndex * sampleCount), sampleCount), block);
            }

            return;
        }

        float[] scratch = new float[sampleCount];

        foreach (ChannelDescriptor channel in device.Channels)
        {
            int dataIndex = ResolveDataIndex(block, channel);
            if (dataIndex < 0 || dataIndex >= channelCount)
            {
                continue;
            }

            NativeDeinterleaver.CopyFloatChannel(block.DataPointer, sampleCount, channelCount, dataIndex, scratch, block.Header.Layout);
            AppendChannel(channel, scratch.AsSpan(0, sampleCount), block);
        }
    }

    private void AppendChannel(ChannelDescriptor channel, ReadOnlySpan<float> values, AcquisitionBlock block)
    {
        if (_writer is null || values.IsEmpty)
        {
            return;
        }

        ChannelKey key = new(channel);
        if (!_channels.TryGetValue(key, out CompressedCaptureChannelManifest? channelInfo) || channelInfo is null)
        {
            return;
        }

        ulong sampleStart = _sampleCounts.TryGetValue(key, out ulong current) ? current : 0;
        ChannelWriteBuffer buffer = GetOrCreateBuffer(key);
        buffer.Append(values, sampleStart, CreateCallbackBlockInfo(block));

        _sampleCounts[key] = sampleStart + (ulong)values.Length;
        _initializedChannels.Add(key);
        _segmentPayloadBytes += values.Length * sizeof(float);

        if (buffer.ByteCount >= ChunkTargetBytes())
        {
            FlushChannelBuffer(channelInfo, buffer);
        }
    }

    private void OpenNextFile()
    {
        if (_writer is not null || _stream is not null)
        {
            FinalizeCurrentFile();
        }

        _channels.Clear();
        _pendingBuffers.Clear();
        _sampleCounts.Clear();
        _initializedChannels.Clear();
        _recordIndex.Clear();
        _segmentPayloadBytes = 0;
        _segmentStoredBytes = 0;
        _footerWritten = false;

        _fileIndex++;
        _currentPath = CreateNextFilePath();
        _auditWriter = _settings.EnableRawBlockAudit ? new RawBlockAuditWriter(_currentPath) : null;
        _stream = new FileStream(_currentPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1024 * 1024);
        _writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);

        CompressedCaptureManifest manifest = CreateManifest();
        byte[] manifestJson = JsonSerializer.SerializeToUtf8Bytes(manifest, CompressedCaptureFormat.JsonOptions);
        _writer.Write(CompressedCaptureFormat.Magic);
        _writer.Write(CompressedCaptureFormat.Version);
        _writer.Write(manifestJson.Length);
        _writer.Write(manifestJson);
        _writer.Flush();
        _stream.Flush(flushToDisk: true);
        UpdateDurableCheckpoint(ReadTotalRawBytes());
    }

    private CompressedCaptureManifest CreateManifest()
    {
        var channels = new List<CompressedCaptureChannelManifest>();
        int ordinal = 0;
        foreach (DeviceDescriptor device in _devices)
        {
            float sampleRate = SampleRateFor(device);
            string groupName = $"Device_{device.DeviceId + 1:0000}_{Sanitize(device.IpAddress)}";
            string groupDescription = $"DeviceId={device.DeviceId}; Ip={device.IpAddress}; SampleRate={sampleRate}";

            foreach (ChannelDescriptor channel in device.Channels)
            {
                string channelName = $"AI{channel.ChannelId + 1:000}";
                string channelDescription = $"DeviceId={channel.DeviceId}; ChannelId={channel.ChannelId}; DataIndex={channel.DataIndex}; LocalDataIndex={channel.LocalDataIndex}; RawType=float32; ByteOrder={(BitConverter.IsLittleEndian ? "LittleEndian" : "BigEndian")}";
                var item = new CompressedCaptureChannelManifest(
                    ordinal,
                    device.DeviceId,
                    groupName,
                    groupDescription,
                    sampleRate,
                    "float32",
                    BitConverter.IsLittleEndian ? "LittleEndian" : "BigEndian",
                    channelName,
                    channelDescription,
                    string.IsNullOrWhiteSpace(channel.Unit) ? "raw" : channel.Unit,
                    channel.ChannelId,
                    channel.DataIndex,
                    channel.LocalDataIndex);
                channels.Add(item);
                _channels[new ChannelKey(channel)] = item;
                ordinal++;
            }
        }

        return new CompressedCaptureManifest(
            CompressedCaptureFormat.Version,
            _runStem,
            _runFolder,
            _fileIndex,
            _startedAt,
            DateTimeOffset.Now,
            _compressionSettings.Enabled,
            EffectiveCodec(),
            EffectiveCodec().ToString(),
            EffectivePreprocessor(),
            "float32",
            BitConverter.IsLittleEndian ? "LittleEndian" : "BigEndian",
            "SdkSampleData: BlockIndex, MessageType, GroupId, MachineId, TotalDataCount, DataCountPerChannel, BufferCount, ChannelCount, Layout",
            _compressionSettings.Algorithm,
            _compressionSettings.Preprocessor,
            Math.Clamp(_compressionSettings.ChunkSizeMb, 1, 256),
            Math.Clamp(_compressionSettings.ZstdLevel, -5, 22),
            Math.Clamp(_compressionSettings.ZstdWindowLog, 0, 31),
            Math.Clamp(_compressionSettings.Lz4Level, 0, 12),
            Math.Clamp(_compressionSettings.Lz4HcLevel, 3, 12),
            Math.Clamp(_compressionSettings.ZlibLevel, 0, 9),
            Math.Clamp(_compressionSettings.BZip2BlockSize, 1, 9),
            Math.Clamp(_compressionSettings.LpcOrder, 1, 4),
            channels);
    }

    private void RollFileIfNeeded()
    {
        if (!AllExpectedChannelsInitialized)
        {
            return;
        }

        if (_segmentPayloadBytes >= SplitBytes())
        {
            FlushAllChannelBuffers();
            FinalizeCurrentFile();
            _rollPending = true;
        }
    }

    private void FinalizeCurrentFile()
    {
        if (_writer is null && _stream is null)
        {
            return;
        }

        try
        {
            FlushAllChannelBuffers();
            DrainPipeline();
            lock (_writeSync)
            {
                WriteFooter();
                _writer?.Flush();
                _stream?.Flush(flushToDisk: true);
                UpdateDurableCheckpoint(ReadTotalRawBytes());
                if (_stream is not null)
                {
                    lock (_statsSync)
                    {
                        _completedFileBytes += _stream.Length;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex, _currentPath);
        }
        finally
        {
            lock (_writeSync)
            {
                _writer?.Dispose();
                _writer = null;
                _stream?.Dispose();
                _stream = null;
                _auditWriter?.Dispose();
                _auditWriter = null;
            }
        }
    }

    private ChannelWriteBuffer GetOrCreateBuffer(ChannelKey key)
    {
        if (!_pendingBuffers.TryGetValue(key, out ChannelWriteBuffer? buffer))
        {
            buffer = new ChannelWriteBuffer();
            _pendingBuffers[key] = buffer;
        }

        return buffer;
    }

    private void FlushAllChannelBuffers()
    {
        if (_writer is null)
        {
            return;
        }

        foreach (KeyValuePair<ChannelKey, ChannelWriteBuffer> item in _pendingBuffers)
        {
            if (item.Value.Count == 0)
            {
                continue;
            }

            if (_channels.TryGetValue(item.Key, out CompressedCaptureChannelManifest? channelInfo))
            {
                FlushChannelBuffer(channelInfo, item.Value);
            }
        }
    }

    private void FlushChannelBuffer(CompressedCaptureChannelManifest channelInfo, ChannelWriteBuffer buffer)
    {
        if (_writer is null || _stream is null || buffer.Count == 0)
        {
            return;
        }

        ReadOnlySpan<float> values = buffer.Values.AsSpan(0, buffer.Count);
        byte[] raw = MemoryMarshal.AsBytes(values).ToArray();
        var job = new ChannelCompressionJob(
            channelInfo.Ordinal,
            buffer.SampleStart,
            buffer.Count,
            raw.Length,
            EffectiveCodec(),
            EffectivePreprocessor(),
            CloneCompressionSettings(_compressionSettings),
            raw,
            buffer.CallbackBlocks.ToArray());
        EnqueueCompressionJob(job);
        AddRawBytes(raw.Length);
        buffer.Clear();
    }

    private Task[] StartCompressionWorkers()
    {
        int workerCount = ResolveCompressionWorkerCount();
        var workers = new Task[workerCount];
        for (int index = 0; index < workers.Length; index++)
        {
            workers[index] = Task.Run(CompressionLoop);
        }

        return workers;
    }

    private void CompressionLoop()
    {
        try
        {
            foreach (ChannelCompressionJob job in _compressionQueue.GetConsumingEnumerable(_pipelineCts.Token))
            {
                Interlocked.Decrement(ref _compressionQueueDepth);
                CompressedChannelChunk chunk = CompressJob(job);
                EnqueueCompressedChunk(chunk);
            }
        }
        catch (OperationCanceledException) when (_pipelineCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            RecordPipelineFault(ex);
        }
    }

    private void WriteLoop()
    {
        try
        {
            foreach (CompressedChannelChunk chunk in _writeQueue.GetConsumingEnumerable(_pipelineCts.Token))
            {
                Interlocked.Decrement(ref _writeQueueDepth);
                WriteCompressedChunk(chunk);
                Interlocked.Increment(ref _writtenCompressionJobs);
            }
        }
        catch (OperationCanceledException) when (_pipelineCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            RecordPipelineFault(ex);
        }
    }

    private void EnqueueCompressionJob(ChannelCompressionJob job)
    {
        ThrowIfPipelineFault();
        Interlocked.Increment(ref _queuedCompressionJobs);
        Interlocked.Increment(ref _compressionQueueDepth);
        try
        {
            _compressionQueue.Add(job, _pipelineCts.Token);
        }
        catch
        {
            Interlocked.Decrement(ref _compressionQueueDepth);
            Interlocked.Decrement(ref _queuedCompressionJobs);
            throw;
        }
    }

    private void EnqueueCompressedChunk(CompressedChannelChunk chunk)
    {
        Interlocked.Increment(ref _writeQueueDepth);
        try
        {
            _writeQueue.Add(chunk, _pipelineCts.Token);
        }
        catch
        {
            Interlocked.Decrement(ref _writeQueueDepth);
            throw;
        }
    }

    private CompressedChannelChunk CompressJob(ChannelCompressionJob job)
    {
        ReadOnlySpan<float> values = MemoryMarshal.Cast<byte, float>(job.Raw);
        CaptureBlockSummary summary = CaptureBlockSummary.From(values);
        uint rawCrc32 = Crc32.Compute(job.Raw);
        byte[] payload = CompressedTdmsCodec.EncodePayloadInPlace(job.Raw, job.Settings, out int transformedLength, out byte flags);
        uint payloadCrc32 = Crc32.Compute(payload);

        return new CompressedChannelChunk(
            job.ChannelOrdinal,
            job.SampleStart,
            job.SampleCount,
            job.OriginalLength,
            transformedLength,
            payload,
            flags,
            job.Codec,
            job.Preprocessor,
            rawCrc32,
            payloadCrc32,
            summary,
            job.CallbackBlocks);
    }

    private void WriteCompressedChunk(CompressedChannelChunk chunk)
    {
        lock (_writeSync)
        {
            if (_writer is null || _stream is null)
            {
                throw new ObjectDisposedException(nameof(CompressedCaptureWriter));
            }

            _writer.Write(chunk.ChannelOrdinal);
            _writer.Write(chunk.SampleStart);
            _writer.Write(chunk.SampleCount);
            _writer.Write(chunk.OriginalLength);
            _writer.Write((byte)chunk.Preprocessor);
            _writer.Write((byte)chunk.Codec);
            _writer.Write(chunk.TransformedLength);
            _writer.Write(chunk.Payload.Length);
            _writer.Write(chunk.Flags);
            _writer.Write(chunk.RawCrc32);
            _writer.Write(chunk.PayloadCrc32);
            _writer.Write(chunk.Summary.First);
            _writer.Write(chunk.Summary.Last);
            _writer.Write(chunk.Summary.Minimum);
            _writer.Write(chunk.Summary.Maximum);
            long payloadOffset = _stream.Position;
            _writer.Write(chunk.Payload);

            _segmentStoredBytes += chunk.Payload.Length;
            _recordIndex.Add(new CompressedCaptureRecordIndex(
                chunk.ChannelOrdinal,
                chunk.SampleStart,
                chunk.SampleCount,
                chunk.OriginalLength,
                chunk.TransformedLength,
                chunk.Payload.Length,
                chunk.Flags,
                payloadOffset,
                chunk.Codec,
                chunk.Preprocessor,
                chunk.RawCrc32,
                chunk.PayloadCrc32,
                chunk.Summary.First,
                chunk.Summary.Last,
                chunk.Summary.Minimum,
                chunk.Summary.Maximum,
                chunk.CallbackBlocks));
            UpdateStatistics(chunk.OriginalLength, chunk.Payload.Length, chunk.Flags);
        }
    }

    private async Task CheckpointLoop()
    {
        CancellationToken token = _checkpointCts.Token;
        TimeSpan interval = TimeSpan.FromMilliseconds(DurabilityCheckpointIntervalMs());
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(interval, token).ConfigureAwait(false);
                FlushDurabilityCheckpoint(flushToDisk: true);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            RecordPipelineFault(ex);
        }
    }

    private void FlushDurabilityCheckpoint(bool flushToDisk)
    {
        if (_disposed)
        {
            return;
        }

        ThrowIfPipelineFault();
        lock (_writeSync)
        {
            if (_writer is null || _stream is null)
            {
                return;
            }

            _writer.Flush();
            _stream.Flush(flushToDisk);
            UpdateDurableCheckpoint(ReadWrittenRawBytes());
        }
    }

    private void DrainPipeline()
    {
        long target = Volatile.Read(ref _queuedCompressionJobs);
        DrainPipelineTo(target);
    }

    private void DrainPipelineTo(long target)
    {
        while (Volatile.Read(ref _writtenCompressionJobs) < target)
        {
            ThrowIfPipelineFault();
            Thread.Sleep(5);
        }

        ThrowIfPipelineFault();
    }

    private void StopPipeline()
    {
        _checkpointCts.Cancel();
        try
        {
            _checkpointWorker.Wait();
        }
        catch (AggregateException ex)
        {
            RecordPipelineFault(ex.Flatten().InnerExceptions.FirstOrDefault() ?? ex);
        }

        try
        {
            _compressionQueue.CompleteAdding();
            Task.WaitAll(_compressionWorkers);
        }
        catch (AggregateException ex)
        {
            RecordPipelineFault(ex.Flatten().InnerExceptions.FirstOrDefault() ?? ex);
        }
        finally
        {
            _writeQueue.CompleteAdding();
        }

        try
        {
            _writeWorker.Wait();
        }
        catch (AggregateException ex)
        {
            RecordPipelineFault(ex.Flatten().InnerExceptions.FirstOrDefault() ?? ex);
        }
        finally
        {
            _compressionQueue.Dispose();
            _writeQueue.Dispose();
        }
    }

    private void ThrowIfPipelineFault()
    {
        Exception? fault;
        lock (_faultSync)
        {
            fault = _pipelineFault;
        }

        if (fault is not null)
        {
            throw new InvalidOperationException("Storage pipeline failed.", fault);
        }
    }

    private void RecordPipelineFault(Exception exception)
    {
        lock (_faultSync)
        {
            if (_pipelineFault is not null)
            {
                return;
            }

            _pipelineFault = exception;
        }

        try
        {
            _pipelineCts.Cancel();
            _checkpointCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        Faulted?.Invoke(exception, _currentPath);
    }

    private void WriteFooter()
    {
        if (_writer is null || _footerWritten)
        {
            return;
        }

        var index = new CompressedCaptureIndex(
            CompressedCaptureFormat.Version,
            _recordIndex.Count,
            _segmentPayloadBytes,
            _segmentStoredBytes,
            _recordIndex.ToArray());
        byte[] indexJson = JsonSerializer.SerializeToUtf8Bytes(index, CompressedCaptureFormat.JsonOptions);
        _writer.Write(indexJson);
        _writer.Write(indexJson.Length);
        _writer.Write(CompressedCaptureFormat.FooterMagic);
        _footerWritten = true;
    }

    private void UpdateStatistics(int rawLength, int payloadLength, byte flags)
    {
        lock (_statsSync)
        {
            _writtenRawBytes += rawLength;
            _totalPayloadBytes += payloadLength;
            _totalBlocks++;
            if ((flags & CompressedCaptureFormat.RawStoredChunkFlag) != 0)
            {
                _rawStoredBlocks++;
                _storedBlocks++;
            }
            else if ((flags & CompressedCaptureFormat.StoredChunkFlag) != 0)
            {
                _storedBlocks++;
            }
            else
            {
                _compressedBlocks++;
            }
        }
    }

    private void AddRawBytes(int rawLength)
    {
        lock (_statsSync)
        {
            _totalRawBytes += rawLength;
        }
    }

    private long ReadTotalRawBytes()
    {
        lock (_statsSync)
        {
            return _totalRawBytes;
        }
    }

    private long ReadWrittenRawBytes()
    {
        lock (_statsSync)
        {
            return _writtenRawBytes;
        }
    }

    private void UpdateDurableCheckpoint(long durableRawBytes)
    {
        long activeFileBytes = _stream?.Position ?? 0;
        lock (_statsSync)
        {
            _lastDurableRawBytes = Math.Max(_lastDurableRawBytes, durableRawBytes);
            _lastDurableWrittenBytes = Math.Max(_lastDurableWrittenBytes, _completedFileBytes + activeFileBytes);
            _lastDurableCheckpointTicks = DateTimeOffset.UtcNow.UtcTicks;
        }
    }

    private CaptureStorageStatistics CreateStatistics()
    {
        long activeFileBytes = 0;
        try
        {
            lock (_writeSync)
            {
                activeFileBytes = _stream?.Position ?? 0;
            }
        }
        catch (ObjectDisposedException)
        {
            activeFileBytes = 0;
        }

        lock (_statsSync)
        {
            double elapsedSeconds = Math.Max(0.001, (DateTimeOffset.UtcNow - _startedAt).TotalSeconds);
            long writtenBytes = _completedFileBytes + activeFileBytes;
            long checkpointTicks = _lastDurableCheckpointTicks;
            double durableLagSeconds = checkpointTicks > 0
                ? Math.Max(0, (DateTimeOffset.UtcNow - new DateTimeOffset(checkpointTicks, TimeSpan.Zero)).TotalSeconds)
                : elapsedSeconds;
            return new CaptureStorageStatistics(
                _totalRawBytes,
                writtenBytes,
                _totalPayloadBytes,
                _totalBlocks,
                _compressedBlocks,
                _storedBlocks,
                _rawStoredBlocks,
                EffectiveCodec().ToString(),
                EffectivePreprocessor().ToString(),
                writtenBytes / 1024.0 / 1024.0 / elapsedSeconds,
                Math.Max(0, Volatile.Read(ref _compressionQueueDepth)),
                Math.Max(0, Volatile.Read(ref _writeQueueDepth)),
                _compressionWorkers.Length,
                _lastDurableRawBytes,
                _lastDurableWrittenBytes,
                durableLagSeconds);
        }
    }

    private CompressionAlgorithm EffectiveCodec()
    {
        return _compressionSettings.Enabled ? _compressionSettings.Algorithm : CompressionAlgorithm.None;
    }

    private CompressionPreprocessor EffectivePreprocessor()
    {
        return EffectiveCodec() == CompressionAlgorithm.None
            ? CompressionPreprocessor.None
            : _compressionSettings.Preprocessor;
    }

    private int ResolveCompressionWorkerCount()
    {
        if (_settings.CompressionWorkerCount > 0)
        {
            return Math.Clamp(_settings.CompressionWorkerCount, 1, 16);
        }

        if (!_compressionSettings.Enabled || _compressionSettings.Algorithm == CompressionAlgorithm.None)
        {
            return 1;
        }

        int reservedCores = Environment.ProcessorCount >= 8 ? 2 : 1;
        int availableCores = Math.Max(1, Environment.ProcessorCount - reservedCores);
        return Math.Clamp(Math.Min(availableCores, 16), 1, 16);
    }

    private int DurabilityCheckpointIntervalMs()
    {
        return Math.Clamp(_settings.FlushIntervalMs, 100, 1000);
    }

    private static int QueueCapacity(int configured)
    {
        return Math.Clamp(configured <= 0 ? 128 : configured, 16, 4096);
    }

    private static CompressionSettings CloneCompressionSettings(CompressionSettings source)
    {
        return new CompressionSettings
        {
            Enabled = source.Enabled,
            Algorithm = source.Algorithm,
            Preprocessor = source.Preprocessor,
            ChunkSizeMb = source.ChunkSizeMb,
            ZstdLevel = source.ZstdLevel,
            ZstdWindowLog = source.ZstdWindowLog,
            Lz4Level = source.Lz4Level,
            Lz4HcLevel = source.Lz4HcLevel,
            ZlibLevel = source.ZlibLevel,
            BZip2BlockSize = source.BZip2BlockSize,
            LpcOrder = source.LpcOrder
        };
    }

    private static CompressedCaptureCallbackBlockInfo CreateCallbackBlockInfo(AcquisitionBlock block)
    {
        return new CompressedCaptureCallbackBlockInfo(
            block.Header.BlockIndex,
            block.Header.MessageType.ToString(),
            block.Header.GroupId,
            block.Header.MachineId,
            block.Header.TotalDataCount,
            block.Header.DataCountPerChannel,
            block.Header.BufferCount,
            block.ChannelCount,
            block.Header.Layout.ToString(),
            block.Header.SampleTime);
    }

    private bool AllExpectedChannelsInitialized => _totalChannelCount == 0 || _initializedChannels.Count >= _totalChannelCount;

    private long SplitBytes()
    {
        if (_settings.FileSplitMb > 0)
        {
            return Math.Max(1L, _settings.FileSplitMb) * 1024L * 1024L;
        }

        return Math.Max(1L, _settings.FileSplitGb) * 1024L * 1024L * 1024L;
    }

    private int ChunkTargetBytes()
    {
        return checked((int)Math.Min(
            256L * 1024L * 1024L,
            Math.Max(1L, _compressionSettings.ChunkSizeMb) * 1024L * 1024L));
    }

    private int ResolveDataIndex(AcquisitionBlock block, ChannelDescriptor channel)
    {
        return block.Header.Layout == SampleDataLayout.ChannelContiguousFloat32
            ? channel.LocalDataIndex
            : channel.DataIndex;
    }

    private bool IsGlobalBlock(AcquisitionBlock block)
    {
        return block.Header.MessageType == DashSampleMessageType.AnalogMultiChannelData ||
               block.Header.GroupId < 0 ||
               block.Header.MachineId < 0 ||
               (_devices.Count > 1 && _totalChannelCount > 0 && block.ChannelCount == _totalChannelCount);
    }

    private DeviceDescriptor? ResolveDevice(SdkSampleData header)
    {
        return _devices.FirstOrDefault(d => d.DeviceId == header.GroupId) ??
               _devices.FirstOrDefault(d => d.DeviceId == header.MachineId);
    }

    private string CreateNextFilePath()
    {
        return Path.Combine(_runFolder, $"{_runStem}_{_fileIndex:0000}{CompressedCaptureFormat.Extension}");
    }

    private string CreateRunStem()
    {
        return _settings.NamingMode == FileNamingMode.Custom
            ? Sanitize(string.IsNullOrWhiteSpace(_settings.CustomFileName) ? "DashCapture" : _settings.CustomFileName.Trim())
            : $"DashCapture_{_startedAt:yyyyMMdd_HHmmss}";
    }

    private static string CreateUniqueDirectory(string rootPath, string stem)
    {
        string first = Path.Combine(rootPath, stem);
        if (!Directory.Exists(first) && !File.Exists(first))
        {
            Directory.CreateDirectory(first);
            return first;
        }

        int suffix = 1;
        while (true)
        {
            string candidate = Path.Combine(rootPath, $"{stem}_{suffix:000}");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }

            suffix++;
        }
    }

    private float SampleRateFor(DeviceDescriptor device)
    {
        return _latestSampleRates.TryGetValue(device.DeviceId, out float latest) && IsValidSampleRate(latest)
            ? latest
            : device.SampleRate;
    }

    private static bool IsValidSampleRate(float sampleRate)
    {
        return sampleRate > 0 && !float.IsNaN(sampleRate) && !float.IsInfinity(sampleRate);
    }

    private static string Sanitize(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Replace('.', '_').Replace(':', '_');
    }
}

internal sealed record ChannelCompressionJob(
    int ChannelOrdinal,
    ulong SampleStart,
    int SampleCount,
    int OriginalLength,
    CompressionAlgorithm Codec,
    CompressionPreprocessor Preprocessor,
    CompressionSettings Settings,
    byte[] Raw,
    IReadOnlyList<CompressedCaptureCallbackBlockInfo> CallbackBlocks);

internal sealed record CompressedChannelChunk(
    int ChannelOrdinal,
    ulong SampleStart,
    int SampleCount,
    int OriginalLength,
    int TransformedLength,
    byte[] Payload,
    byte Flags,
    CompressionAlgorithm Codec,
    CompressionPreprocessor Preprocessor,
    uint RawCrc32,
    uint PayloadCrc32,
    CaptureBlockSummary Summary,
    IReadOnlyList<CompressedCaptureCallbackBlockInfo> CallbackBlocks);

internal sealed class ChannelWriteBuffer
{
    private float[] _values = Array.Empty<float>();
    private readonly List<CompressedCaptureCallbackBlockInfo> _callbackBlocks = new();

    public float[] Values => _values;
    public int Count { get; private set; }
    public ulong SampleStart { get; private set; }
    public int ByteCount => Count * sizeof(float);
    public IReadOnlyList<CompressedCaptureCallbackBlockInfo> CallbackBlocks => _callbackBlocks;

    public void Append(ReadOnlySpan<float> values, ulong sampleStart, CompressedCaptureCallbackBlockInfo callbackBlock)
    {
        if (values.IsEmpty)
        {
            return;
        }

        if (Count == 0)
        {
            SampleStart = sampleStart;
        }

        EnsureCapacity(Count + values.Length);
        values.CopyTo(_values.AsSpan(Count, values.Length));
        Count += values.Length;
        _callbackBlocks.Add(callbackBlock);
    }

    public void Clear()
    {
        Count = 0;
        SampleStart = 0;
        _callbackBlocks.Clear();
    }

    private void EnsureCapacity(int required)
    {
        if (_values.Length >= required)
        {
            return;
        }

        int next = _values.Length == 0 ? 4096 : _values.Length;
        while (next < required)
        {
            next *= 2;
        }

        Array.Resize(ref _values, next);
    }
}

internal readonly record struct CaptureBlockSummary(float First, float Last, float Minimum, float Maximum)
{
    public static CaptureBlockSummary From(ReadOnlySpan<float> values)
    {
        if (values.IsEmpty)
        {
            return new CaptureBlockSummary(0, 0, 0, 0);
        }

        float first = values[0];
        float last = values[^1];
        float min = first;
        float max = first;
        foreach (float value in values)
        {
            if (value < min) min = value;
            if (value > max) max = value;
        }

        return new CaptureBlockSummary(first, last, min, max);
    }
}

internal static class CompressedCaptureFormat
{
    public const string Extension = ".dhcap";
    public const int Version = 3;
    public const int MinSupportedVersion = 1;
    public const byte StoredChunkFlag = 1;
    public const byte PreprocessedChunkFlag = 2;
    public const byte RawStoredChunkFlag = 4;
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("DHCAP01\0");
    public static readonly byte[] FooterMagic = Encoding.ASCII.GetBytes("DHCIDX3\0");
    public static readonly byte[] FooterMagicV2 = Encoding.ASCII.GetBytes("DHCIDX2\0");
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool IsCompressedCaptureFile(string path)
    {
        if (path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.EndsWith(".tdms", StringComparison.OrdinalIgnoreCase) && HasMagic(path);
    }

    private static bool HasMagic(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            Span<byte> buffer = stackalloc byte[Magic.Length];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Read(buffer) != Magic.Length)
            {
                return false;
            }

            return buffer.SequenceEqual(Magic);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}

internal sealed record CompressedCaptureManifest(
    int FormatVersion,
    string RunStem,
    string RunFolder,
    int SegmentIndex,
    DateTimeOffset StartedAt,
    DateTimeOffset CreatedAt,
    bool CompressionEnabled,
    CompressionAlgorithm EffectiveCodec,
    string Codec,
    CompressionPreprocessor EffectivePreprocessor,
    string RawType,
    string ByteOrder,
    string CallbackBlockSchema,
    CompressionAlgorithm Algorithm,
    CompressionPreprocessor Preprocessor,
    int ChunkSizeMb,
    int ZstdLevel,
    int ZstdWindowLog,
    int Lz4Level,
    int Lz4HcLevel,
    int ZlibLevel,
    int BZip2BlockSize,
    int LpcOrder,
    IReadOnlyList<CompressedCaptureChannelManifest> Channels);

internal sealed record CompressedCaptureIndex(
    int Version,
    int RecordCount,
    long RawBytes,
    long StoredBytes,
    IReadOnlyList<CompressedCaptureRecordIndex> Records);

internal sealed record CompressedCaptureRecordIndex(
    int ChannelOrdinal,
    ulong SampleStart,
    int SampleCount,
    int OriginalLength,
    int TransformedLength,
    int PayloadLength,
    byte Flags,
    long PayloadOffset,
    CompressionAlgorithm Codec,
    CompressionPreprocessor Preprocessor,
    uint RawCrc32,
    uint PayloadCrc32,
    float First,
    float Last,
    float Minimum,
    float Maximum,
    IReadOnlyList<CompressedCaptureCallbackBlockInfo> CallbackBlocks);

internal sealed record CompressedCaptureCallbackBlockInfo(
    long BlockIndex,
    string MessageType,
    int GroupId,
    int MachineId,
    long TotalDataCount,
    int DataCountPerChannel,
    int BufferCount,
    int ChannelCount,
    string Layout,
    long SampleTime = 0);

internal sealed record CompressedCaptureChannelManifest(
    int Ordinal,
    int DeviceId,
    string GroupName,
    string GroupDescription,
    double SampleRate,
    string RawType,
    string ByteOrder,
    string ChannelName,
    string ChannelDescription,
    string Unit,
    int ChannelId,
    int DataIndex,
    int LocalDataIndex);
