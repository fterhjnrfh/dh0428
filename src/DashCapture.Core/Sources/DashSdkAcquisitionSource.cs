using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using DashCapture.Core.Configuration;
using DashCapture.Core.Models;
using DashCapture.Native;

namespace DashCapture.Core.Sources;

public sealed class DashSdkAcquisitionSource : IAcquisitionSource
{
    private readonly SdkSettings _settings;
    private readonly List<DeviceDescriptor> _devices = new();
    private DashSdkNative.SampleDataChangeEventHandle? _callback;
    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;
    private IntPtr _pollingBuffer;
    private int _pollingBufferSize;
    private int _pollingBlockIndex;
    private int _emptyGlobalPolls;
    private bool _fallbackToSingleDevicePolling;
    private bool _sampling;

    public DashSdkAcquisitionSource(SdkSettings settings)
    {
        _settings = settings;
    }

    public event Action<SdkSampleData>? SampleReceived;
    public IReadOnlyList<DeviceDescriptor> Devices => _devices;
    public bool IsConnected { get; private set; }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        NativeBootstrap.AddSearchDirectory(_settings.DashRoot);
        string configDir = Path.GetFullPath(_settings.ConfigDir);

        DashSdkNative.InitMacControl(EnsureTrailingSlash(configDir));

        if (_settings.ReadoutMode == SdkReadoutMode.Callback)
        {
            _callback = OnNativeSample;
            int callbackResult = DashSdkNative.SetDataChangeCallBackFun(_callback);
            if (callbackResult < 0)
            {
                throw new InvalidOperationException($"SetDataChangeCallBackFun failed with code {callbackResult}.");
            }
        }

        RefreshDevices();
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConnected) throw new InvalidOperationException("SDK is not connected.");

        DashSdkNative.SetGetDataCountEveryTime(_settings.DataCountEveryTime);
        StartNativeSampling(_settings.GetDataType);
        if (_settings.ReadoutMode == SdkReadoutMode.PollEachDevice)
        {
            StartPolling();
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await StopPollingAsync().ConfigureAwait(false);

        if (_sampling)
        {
            DashSdkNative.StopMacSample();
            _sampling = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        if (IsConnected)
        {
            DashSdkNative.QuitMacControl();
            IsConnected = false;
        }
    }

    private void RefreshDevices()
    {
        _devices.Clear();
        if (!DashSdkNative.RefindAndConnecMac())
        {
            return;
        }

        TryLoadMacParameter();

        int count = DashSdkNative.GetAllMacOnlineCount();
        var discovered = new List<DeviceDescriptor>(count);
        for (int i = 0; i < count; i++)
        {
            DeviceDescriptor? descriptor = CreateDevice(i);
            if (descriptor is not null)
            {
                discovered.Add(descriptor);
            }
        }

        IEnumerable<DeviceDescriptor> activeDevices = discovered
            .Where(device => device.Online)
            .GroupBy(device => device.IpAddress, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());

        if (_settings.MaxDeviceCount > 0)
        {
            activeDevices = activeDevices.Take(_settings.MaxDeviceCount);
        }

        _devices.AddRange(NormalizeDataIndices(activeDevices.ToList()));
    }

    private DeviceDescriptor? CreateDevice(int index)
    {
        IntPtr ipBuffer = Marshal.AllocHGlobal(DashSdkNative.StandardCapacity);
        try
        {
            int result = DashSdkNative.GetMacInfoFromIndex(index, out int deviceId, ipBuffer, DashSdkNative.StandardCapacity, out int usedBytes);
            if (result < 0)
            {
                return null;
            }

            string ip = NativeString.FromAnsiBuffer(ipBuffer, usedBytes);
            bool online = DashSdkNative.GetMacLinkStatus(deviceId, ip) != 0;
            float sampleRate = ResolveSampleRate(deviceId, ip);
            int channelCount = Math.Max(0, DashSdkNative.GetMacCurrentChnCount(deviceId, ip));
            var channels = new List<ChannelDescriptor>(channelCount);

            for (int i = 0; i < channelCount; i++)
            {
                int channelResult = DashSdkNative.GetChannelIDFromAllChannelIndex(deviceId, ip, i, out int channelId, out int channelOnline);
                if (channelResult < 0)
                {
                    continue;
                }

                channels.Add(new ChannelDescriptor(
                    deviceId,
                    ip,
                    channelId,
                    DataIndex: ResolveDataIndex(deviceId, channelId, i),
                    LocalDataIndex: ResolveLocalDataIndex(deviceId, channelId, i),
                    Online: channelOnline != 0,
                    Name: $"AI{deviceId + 1}-{channelId + 1}",
                    Unit: "raw",
                    SampleRate: sampleRate));
            }

            return new DeviceDescriptor(deviceId, ip, sampleRate, online, channels);
        }
        finally
        {
            Marshal.FreeHGlobal(ipBuffer);
        }
    }

    private float ResolveSampleRate(int deviceId, string ip)
    {
        float? configuredSampleRate = TryReadConfiguredSampleRate(deviceId, ip);
        if (configuredSampleRate is { } configured && IsValidSampleRate(configured))
        {
            return configured;
        }

        float sdkSampleRate = DashSdkNative.GetMacCurrentSampleFreqEx(deviceId);
        return IsValidSampleRate(sdkSampleRate) ? sdkSampleRate : 1;
    }

    private void TryLoadMacParameter()
    {
        if (string.IsNullOrWhiteSpace(_settings.ParamDir))
        {
            return;
        }

        string paramDir = Path.GetFullPath(_settings.ParamDir);
        string allGroupChannelPath = Path.Combine(paramDir, "AllGroupChannel.xml");
        if (!File.Exists(allGroupChannelPath))
        {
            return;
        }

        try
        {
            DashSdkNative.LoadMacParameter(paramDir);
        }
        catch
        {
            // Keep discovery working if the exported hardware parameter folder is stale.
        }
    }

    private float? TryReadConfiguredSampleRate(int deviceId, string ip)
    {
        string tmpDir = Path.Combine(Path.GetFullPath(_settings.ConfigDir), "Tmp");
        if (!Directory.Exists(tmpDir))
        {
            return null;
        }

        string exactPath = Path.Combine(tmpDir, $"Machine{ip}_{deviceId}.xml");
        IEnumerable<string> candidates = File.Exists(exactPath)
            ? new[] { exactPath }
            : Directory.EnumerateFiles(tmpDir, $"Machine{ip}_*.xml");

        foreach (string path in candidates)
        {
            try
            {
                XDocument document = XDocument.Load(path, LoadOptions.None);
                float? sampleRate = ReadSampleRateElement(document, "SampleFrequency") ??
                                    ReadSampleRateElement(document, "SampleFreq");
                if (sampleRate is { } value && IsValidSampleRate(value))
                {
                    return value;
                }
            }
            catch
            {
                // Ignore stale or partially written SDK temp files and fall back to the native value.
            }
        }

        return null;
    }

    private static float? ReadSampleRateElement(XDocument document, string elementName)
    {
        string? value = document
            .Descendants(elementName)
            .Select(element => element.Value)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float sampleRate)
            ? sampleRate
            : null;
    }

    private static bool IsValidSampleRate(float sampleRate)
    {
        return sampleRate > 0 && !float.IsNaN(sampleRate) && !float.IsInfinity(sampleRate);
    }

    private List<DeviceDescriptor> NormalizeDataIndices(IReadOnlyList<DeviceDescriptor> devices)
    {
        var normalized = new List<DeviceDescriptor>(devices.Count);
        bool useGlobalDataIndex = _settings.GetDataType == GetDataType.MultiMachine;
        int totalChannelCount = devices.Sum(device => device.Channels.Count);
        bool globalIndicesValid = useGlobalDataIndex &&
                                  AreIndicesValid(devices.SelectMany(device => device.Channels).Select(channel => channel.DataIndex), totalChannelCount);

        int globalIndex = 0;
        foreach (DeviceDescriptor device in devices)
        {
            bool localIndicesValid = AreIndicesValid(device.Channels.Select(channel => channel.LocalDataIndex), device.Channels.Count);
            var channels = new List<ChannelDescriptor>(device.Channels.Count);
            for (int i = 0; i < device.Channels.Count; i++)
            {
                ChannelDescriptor channel = device.Channels[i];
                int localDataIndex = localIndicesValid ? channel.LocalDataIndex : i;
                int dataIndex = useGlobalDataIndex
                    ? globalIndicesValid ? channel.DataIndex : globalIndex
                    : localDataIndex;

                channels.Add(channel with
                {
                    DataIndex = dataIndex,
                    LocalDataIndex = localDataIndex
                });
                globalIndex++;
            }

            normalized.Add(device with { Channels = channels });
        }

        return normalized;
    }

    private static bool AreIndicesValid(IEnumerable<int> indices, int expectedCount)
    {
        int[] values = indices.ToArray();
        return values.Length == expectedCount &&
               values.All(value => value >= 0 && value < expectedCount) &&
               values.Distinct().Count() == values.Length;
    }

    private int ResolveDataIndex(int deviceId, int channelId, int fallbackIndex)
    {
        int dataIndex = _settings.GetDataType == GetDataType.MultiMachine
            ? DashSdkNative.GetAllMacDataIndex(deviceId, channelId)
            : DashSdkNative.GetOneMacDataIndex(deviceId, channelId);

        return dataIndex >= 0 ? dataIndex : fallbackIndex;
    }

    private static int ResolveLocalDataIndex(int deviceId, int channelId, int fallbackIndex)
    {
        int dataIndex = DashSdkNative.GetOneMacDataIndex(deviceId, channelId);
        return dataIndex >= 0 ? dataIndex : fallbackIndex;
    }

    private void OnNativeSample(
        long sampleTime,
        int groupIdSize,
        IntPtr groupInfo,
        int nMessageType,
        int nGroupID,
        int nChannelStyle,
        int nChannelID,
        int nMachineID,
        long nTotalDataCount,
        int nDataCountPerChannel,
        int nBufferCount,
        int nBlockIndex,
        long varSampleData)
    {
        try
        {
            if (_settings.ReadoutMode != SdkReadoutMode.Callback)
            {
                return;
            }

            string groupText = NativeString.FromAnsiBuffer(groupInfo, groupIdSize);
            SampleReceived?.Invoke(new SdkSampleData(
                sampleTime,
                groupText,
                nMessageType,
                nGroupID,
                nChannelStyle,
                nChannelID,
                nMachineID,
                nTotalDataCount,
                nDataCountPerChannel,
                nBufferCount,
                nBlockIndex,
                new IntPtr(varSampleData),
                SampleDataLayout.SampleInterleavedFloat32));
        }
        catch
        {
            // The native SDK owns the callback thread. Never throw back into it.
        }
    }

    private void StartNativeSampling(GetDataType dataType)
    {
        DashSdkNative.ChangeGetDataStatus(dataType == GetDataType.SingleMachine);
        DashSdkNative.StartMacSample();
        _sampling = true;
    }

    private void StartPolling()
    {
        _pollingCts = new CancellationTokenSource();
        _pollingBufferSize = Math.Max(1, _settings.PollBufferMb) * 1024 * 1024;
        _pollingBuffer = Marshal.AllocHGlobal(_pollingBufferSize);
        _pollingBlockIndex = 0;
        _emptyGlobalPolls = 0;
        _fallbackToSingleDevicePolling = false;
        _pollingTask = Task.Run(() => PollEachDeviceAsync(_pollingCts.Token));
    }

    private async Task StopPollingAsync()
    {
        if (_pollingCts is not null)
        {
            _pollingCts.Cancel();
        }

        if (_pollingTask is not null)
        {
            try
            {
                await _pollingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_pollingBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_pollingBuffer);
            _pollingBuffer = IntPtr.Zero;
        }

        _pollingCts?.Dispose();
        _pollingCts = null;
        _pollingTask = null;
        _pollingBufferSize = 0;
    }

    private async Task PollEachDeviceAsync(CancellationToken cancellationToken)
    {
        int idleDelayMs = Math.Max(1, _settings.PollIntervalMs);
        while (!cancellationToken.IsCancellationRequested)
        {
            bool tryGlobal = _settings.GetDataType == GetDataType.MultiMachine && !_fallbackToSingleDevicePolling;
            bool receivedAny = tryGlobal
                ? PollAllDevices(cancellationToken)
                : PollSingleDevices(cancellationToken);

            if (tryGlobal)
            {
                TrackGlobalPollingResult(receivedAny);
            }

            if (!receivedAny)
            {
                await Task.Delay(idleDelayMs, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await Task.Yield();
            }
        }
    }

    private void TrackGlobalPollingResult(bool receivedAny)
    {
        if (receivedAny)
        {
            _emptyGlobalPolls = 0;
            return;
        }

        _emptyGlobalPolls++;
        int threshold = Math.Max(100, 1000 / Math.Max(1, _settings.PollIntervalMs));
        if (_emptyGlobalPolls < threshold)
        {
            return;
        }

        _fallbackToSingleDevicePolling = true;
        _emptyGlobalPolls = 0;
        if (!_sampling)
        {
            DashSdkNative.ChangeGetDataStatus(true);
            return;
        }

        DashSdkNative.StopMacSample();
        DashSdkNative.ChangeGetDataStatus(true);
        DashSdkNative.StartMacSample();
    }

    private bool PollAllDevices(CancellationToken cancellationToken)
    {
        int maxBlocks = Math.Max(1, _settings.MaxPollBlocksPerDevice);
        bool receivedAny = false;
        for (int block = 0; block < maxBlocks && !cancellationToken.IsCancellationRequested; block++)
        {
            int result = DashSdkNative.GetAllMacChnData(
                _pollingBufferSize,
                _pollingBuffer,
                out long totalPosition,
                out long receiveCount,
                out long channelCount);

            if (result < 0 || receiveCount <= 0 || channelCount <= 0)
            {
                break;
            }

            int byteCount = ToByteCount(receiveCount, channelCount);
            if (byteCount <= 0 || byteCount > _pollingBufferSize)
            {
                break;
            }

            receivedAny = true;
            SampleReceived?.Invoke(new SdkSampleData(
                SampleTime: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                GroupInfo: string.Empty,
                MessageType: DashSampleMessageType.AnalogMultiChannelData,
                GroupId: -1,
                ChannelStyle: 0,
                ChannelId: -1,
                MachineId: -1,
                TotalDataCount: totalPosition,
                DataCountPerChannel: checked((int)receiveCount),
                BufferCount: byteCount,
                BlockIndex: Interlocked.Increment(ref _pollingBlockIndex),
                DataPointer: _pollingBuffer,
                Layout: SampleDataLayout.ChannelContiguousFloat32));
        }

        return receivedAny;
    }

    private bool PollSingleDevices(CancellationToken cancellationToken)
    {
        bool receivedAny = false;
        int maxBlocks = Math.Max(1, _settings.MaxPollBlocksPerDevice);
        foreach (DeviceDescriptor device in _devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!device.Online || device.Channels.Count == 0)
            {
                continue;
            }

            for (int block = 0; block < maxBlocks && !cancellationToken.IsCancellationRequested; block++)
            {
                int result = DashSdkNative.GetOneMacChnData_New(
                    device.DeviceId,
                    out long receiveCount,
                    out long channelCount,
                    out long totalPosition,
                    _pollingBufferSize,
                    _pollingBuffer);

                if (result < 0 || receiveCount <= 0 || channelCount <= 0)
                {
                    break;
                }

                int byteCount = ToByteCount(receiveCount, channelCount);
                if (byteCount <= 0 || byteCount > _pollingBufferSize)
                {
                    break;
                }

                receivedAny = true;
                SampleReceived?.Invoke(new SdkSampleData(
                    SampleTime: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    GroupInfo: string.Empty,
                    MessageType: DashSampleMessageType.SingleGroupAnalogData,
                    GroupId: device.DeviceId,
                    ChannelStyle: 0,
                    ChannelId: -1,
                    MachineId: device.DeviceId,
                    TotalDataCount: totalPosition,
                    DataCountPerChannel: checked((int)receiveCount),
                    BufferCount: byteCount,
                    BlockIndex: Interlocked.Increment(ref _pollingBlockIndex),
                    DataPointer: _pollingBuffer,
                    Layout: SampleDataLayout.ChannelContiguousFloat32));
            }
        }

        return receivedAny;
    }

    private static int ToByteCount(long receiveCount, long channelCount)
    {
        long bytes = receiveCount * channelCount * sizeof(float);
        return bytes is > 0 and <= int.MaxValue ? (int)bytes : 0;
    }

    private static string EnsureTrailingSlash(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) ||
            path.EndsWith(Path.AltDirectorySeparatorChar.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}
