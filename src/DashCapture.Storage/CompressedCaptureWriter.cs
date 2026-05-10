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
    private readonly IReadOnlyList<DeviceDescriptor> _devices;
    private readonly Dictionary<ChannelKey, CompressedCaptureChannelManifest> _channels = new();
    private readonly Dictionary<ChannelKey, ulong> _sampleCounts = new();
    private readonly HashSet<ChannelKey> _initializedChannels = new();
    private readonly Dictionary<int, float> _latestSampleRates = new();
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
    private bool _rollPending;

    public CompressedCaptureWriter(StorageSettings settings, IReadOnlyList<DeviceDescriptor> devices)
    {
        _settings = settings;
        _devices = devices;
        _totalChannelCount = devices.Sum(device => device.Channels.Count);
        Directory.CreateDirectory(settings.RootPath);
        _runStem = CreateRunStem();
        _runFolder = CreateUniqueDirectory(settings.RootPath, _runStem);
        OpenNextFile();
    }

    public string CurrentPath => _currentPath;
    public string CurrentFolder => _runFolder;
    public string CurrentAuditPath => _auditWriter?.CurrentPath ?? string.Empty;
    public event Action<Exception, string>? Faulted;

    public void AppendBlock(AcquisitionBlock block)
    {
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
        _auditWriter?.Flush();
        _writer?.Flush();
        _stream?.Flush(flushToDisk: false);
    }

    public void Dispose()
    {
        FinalizeCurrentFile();
    }

    private void AppendGlobalBlock(AcquisitionBlock block, int sampleCount)
    {
        int channelCount = Math.Max(1, block.ChannelCount);
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
                AppendChannel(channel, scratch.AsSpan(0, sampleCount));
            }
        }

        RollFileIfNeeded();
    }

    private void AppendDeviceBlock(AcquisitionBlock block, DeviceDescriptor device, int sampleCount)
    {
        int channelCount = Math.Max(1, block.ChannelCount);
        float[] scratch = new float[sampleCount];

        foreach (ChannelDescriptor channel in device.Channels)
        {
            int dataIndex = ResolveDataIndex(block, channel);
            if (dataIndex < 0 || dataIndex >= channelCount)
            {
                continue;
            }

            NativeDeinterleaver.CopyFloatChannel(block.DataPointer, sampleCount, channelCount, dataIndex, scratch, block.Header.Layout);
            AppendChannel(channel, scratch.AsSpan(0, sampleCount));
        }
    }

    private void AppendChannel(ChannelDescriptor channel, ReadOnlySpan<float> values)
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

        byte[] raw = MemoryMarshal.AsBytes(values).ToArray();
        byte[] payload = CompressedTdmsCodec.EncodePayload(raw, _settings.Compression, out int transformedLength, out byte flags);
        ulong sampleStart = _sampleCounts.TryGetValue(key, out ulong current) ? current : 0;

        _writer.Write(channelInfo.Ordinal);
        _writer.Write(sampleStart);
        _writer.Write(values.Length);
        _writer.Write(raw.Length);
        _writer.Write(transformedLength);
        _writer.Write(payload.Length);
        _writer.Write(flags);
        _writer.Write(payload);

        _sampleCounts[key] = sampleStart + (ulong)values.Length;
        _initializedChannels.Add(key);
        _segmentPayloadBytes += raw.Length;
    }

    private void OpenNextFile()
    {
        FinalizeCurrentFile();
        _channels.Clear();
        _sampleCounts.Clear();
        _initializedChannels.Clear();
        _segmentPayloadBytes = 0;

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
                string channelDescription = $"DeviceId={channel.DeviceId}; ChannelId={channel.ChannelId}; DataIndex={channel.DataIndex}; LocalDataIndex={channel.LocalDataIndex}; RawType=float32";
                var item = new CompressedCaptureChannelManifest(
                    ordinal,
                    device.DeviceId,
                    groupName,
                    groupDescription,
                    sampleRate,
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
            _runStem,
            _runFolder,
            _fileIndex,
            _startedAt,
            DateTimeOffset.Now,
            _settings.Compression.Algorithm,
            _settings.Compression.Preprocessor,
            Math.Clamp(_settings.Compression.ChunkSizeMb, 1, 256),
            Math.Clamp(_settings.Compression.ZstdLevel, -5, 22),
            Math.Clamp(_settings.Compression.ZstdWindowLog, 0, 31),
            Math.Clamp(_settings.Compression.Lz4Level, 0, 12),
            Math.Clamp(_settings.Compression.Lz4HcLevel, 3, 12),
            Math.Clamp(_settings.Compression.ZlibLevel, 0, 9),
            Math.Clamp(_settings.Compression.BZip2BlockSize, 1, 9),
            Math.Clamp(_settings.Compression.LpcOrder, 1, 4),
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
            FinalizeCurrentFile();
            _rollPending = true;
        }
    }

    private void FinalizeCurrentFile()
    {
        try
        {
            _writer?.Flush();
            _stream?.Flush(flushToDisk: false);
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex, _currentPath);
        }
        finally
        {
            _writer?.Dispose();
            _writer = null;
            _stream?.Dispose();
            _stream = null;
            _auditWriter?.Dispose();
            _auditWriter = null;
        }
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

internal static class CompressedCaptureFormat
{
    public const string Extension = ".dhcap";
    public const int Version = 1;
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("DHCAP01\0");
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
    string RunStem,
    string RunFolder,
    int SegmentIndex,
    DateTimeOffset StartedAt,
    DateTimeOffset CreatedAt,
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

internal sealed record CompressedCaptureChannelManifest(
    int Ordinal,
    int DeviceId,
    string GroupName,
    string GroupDescription,
    double SampleRate,
    string ChannelName,
    string ChannelDescription,
    string Unit,
    int ChannelId,
    int DataIndex,
    int LocalDataIndex);
