using System;
using DashCapture.Core.Configuration;
using DashCapture.Core.Memory;
using DashCapture.Core.Models;
using DashCapture.Native;

namespace DashCapture.Storage;

public sealed class TdmsCaptureWriter : IDisposable
{
    private readonly StorageSettings _settings;
    private readonly IReadOnlyList<DeviceDescriptor> _devices;
    private readonly Dictionary<int, IntPtr> _groups = new();
    private readonly Dictionary<ChannelKey, IntPtr> _channels = new();
    private readonly HashSet<ChannelKey> _initializedChannels = new();
    private readonly Dictionary<int, float> _latestSampleRates = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private readonly int _totalChannelCount;
    private IntPtr _file;
    private RawBlockAuditWriter? _auditWriter;
    private int _fileIndex;
    private string _currentPath = string.Empty;
    private readonly string _runFolder;
    private readonly string _runStem;
    private long _segmentPayloadBytes;
    private bool _rollPending;

    public TdmsCaptureWriter(StorageSettings settings, IReadOnlyList<DeviceDescriptor> devices)
    {
        _settings = settings;
        _devices = devices;
        _totalChannelCount = devices.Sum(d => d.Channels.Count);
        NativeBootstrap.AddSearchDirectory(settings.TdmRuntimeDir);
        Directory.CreateDirectory(settings.RootPath);
        _runStem = CreateRunStem();
        _runFolder = CreateUniqueDirectory(settings.RootPath, _runStem);
        OpenNextFile();
    }

    public string CurrentPath => _currentPath;
    public string CurrentFolder => _runFolder;
    public string CurrentAuditPath => _auditWriter?.CurrentPath ?? string.Empty;

    public void AppendBlock(AcquisitionBlock block)
    {
        if (_file == IntPtr.Zero)
        {
            return;
        }

        if (_rollPending)
        {
            OpenNextFile();
            _rollPending = false;
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
            if (!IsValidSampleRate(device.SampleRate))
            {
                continue;
            }

            if (_latestSampleRates.TryGetValue(device.DeviceId, out float current) &&
                Math.Abs(current - device.SampleRate) <= Math.Max(1, Math.Abs(current) * 0.005))
            {
                continue;
            }

            _latestSampleRates[device.DeviceId] = device.SampleRate;
            if (_groups.TryGetValue(device.DeviceId, out IntPtr group))
            {
                TdmNative.ThrowIfError(
                    TdmNative.DDC_SetChannelGroupPropertyString(group, "description", CreateDeviceDescription(device)),
                    "DDC_SetChannelGroupPropertyString");
            }
        }
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

    public void Save()
    {
        if (_file == IntPtr.Zero || !AllExpectedChannelsInitialized)
        {
            return;
        }

        _auditWriter?.Flush();
        TdmNative.ThrowIfError(TdmNative.DDC_SaveFile(_file), "DDC_SaveFile");
    }

    public void Dispose()
    {
        _auditWriter?.Flush();
        _auditWriter?.Dispose();
        _auditWriter = null;

        if (_file == IntPtr.Zero)
        {
            return;
        }

        try
        {
            TdmNative.DDC_SaveFile(_file);
        }
        finally
        {
            TdmNative.DDC_CloseFile(_file);
            _file = IntPtr.Zero;
        }
    }

    private bool AllExpectedChannelsInitialized => _totalChannelCount == 0 || _initializedChannels.Count >= _totalChannelCount;

    private void AppendChannel(ChannelDescriptor channel, ReadOnlySpan<float> values)
    {
        ChannelKey key = new(channel);
        if (!_channels.TryGetValue(key, out IntPtr handle))
        {
            return;
        }

        unsafe
        {
            fixed (float* ptr = values)
            {
                TdmNative.ThrowIfError(
                    TdmNative.DDC_AppendDataValuesFloat(handle, new IntPtr(ptr), (UIntPtr)values.Length),
                    "DDC_AppendDataValuesFloat");
            }
        }

        _initializedChannels.Add(key);
        _segmentPayloadBytes += values.Length * sizeof(float);
    }

    private void OpenNextFile()
    {
        DisposeCurrentFile();
        _groups.Clear();
        _channels.Clear();
        _initializedChannels.Clear();
        _segmentPayloadBytes = 0;

        _fileIndex++;
        _currentPath = CreateNextFilePath();
        _auditWriter = _settings.EnableRawBlockAudit ? new RawBlockAuditWriter(_currentPath) : null;

        TdmNative.ThrowIfError(
            TdmNative.DDC_CreateFile(
                _currentPath,
                TdmNative.TdmsFileType,
                $"{_runStem} segment {_fileIndex:0000}",
                $"Raw data captured from DASH SDK. RunFolder={_runFolder}; Segment={_fileIndex:0000}; StartedAt={_startedAt:O}",
                "DASH Capture",
                Environment.UserName,
                out _file),
            "DDC_CreateFile");

        foreach (DeviceDescriptor device in _devices)
        {
            string groupName = $"Device_{device.DeviceId + 1:0000}_{Sanitize(device.IpAddress)}";
            string description = CreateDeviceDescription(device);
            TdmNative.ThrowIfError(TdmNative.DDC_AddChannelGroup(_file, groupName, description, out IntPtr group), "DDC_AddChannelGroup");
            _groups[device.DeviceId] = group;

            foreach (ChannelDescriptor channel in device.Channels)
            {
                string channelName = $"AI{channel.ChannelId + 1:000}";
                string channelDescription = $"DeviceId={channel.DeviceId}; ChannelId={channel.ChannelId}; DataIndex={channel.DataIndex}; LocalDataIndex={channel.LocalDataIndex}; RawType=float32";
                TdmNative.ThrowIfError(
                    TdmNative.DDC_AddChannel(group, DdcDataType.Float, channelName, channelDescription, channel.Unit, out IntPtr channelHandle),
                    "DDC_AddChannel");
                _channels[new ChannelKey(channel)] = channelHandle;
            }
        }
    }

    private void RollFileIfNeeded()
    {
        if (!AllExpectedChannelsInitialized)
        {
            return;
        }

        long splitBytes = SplitBytes();
        if (_segmentPayloadBytes >= splitBytes)
        {
            Save();
            _rollPending = true;
        }
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
        return Path.Combine(_runFolder, $"{_runStem}_{_fileIndex:0000}.tdms");
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

    private long SplitBytes()
    {
        if (_settings.FileSplitMb > 0)
        {
            return Math.Max(1L, _settings.FileSplitMb) * 1024L * 1024L;
        }

        return Math.Max(1L, _settings.FileSplitGb) * 1024L * 1024L * 1024L;
    }

    private void DisposeCurrentFile()
    {
        if (_file == IntPtr.Zero)
        {
            return;
        }

        TdmNative.DDC_SaveFile(_file);
        TdmNative.DDC_CloseFile(_file);
        _file = IntPtr.Zero;
        _auditWriter?.Dispose();
        _auditWriter = null;
    }

    private static string Sanitize(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Replace('.', '_').Replace(':', '_');
    }

    private string CreateDeviceDescription(DeviceDescriptor device)
    {
        float sampleRate = _latestSampleRates.TryGetValue(device.DeviceId, out float latest) && IsValidSampleRate(latest)
            ? latest
            : device.SampleRate;
        return $"DeviceId={device.DeviceId}; Ip={device.IpAddress}; SampleRate={sampleRate}";
    }

    private static bool IsValidSampleRate(float sampleRate)
    {
        return sampleRate > 0 && !float.IsNaN(sampleRate) && !float.IsInfinity(sampleRate);
    }
}
