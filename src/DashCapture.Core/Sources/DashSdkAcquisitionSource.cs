using System;
using System.Globalization;
using System.Runtime.InteropServices;
using DashCapture.Core.Configuration;
using DashCapture.Core.Models;
using DashCapture.Native;

namespace DashCapture.Core.Sources;

public sealed class DashSdkAcquisitionSource : IAcquisitionSource
{
    private readonly SdkSettings _settings;
    private readonly List<DeviceDescriptor> _devices = new();
    private DashSdkNative.SampleDataChangeEventHandle? _callback;
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
        int init = DashSdkNative.InitMacControl(EnsureTrailingSlash(configDir));
        if (init < 0)
        {
            throw new InvalidOperationException($"InitMacControl failed with code {init}.");
        }

        _callback = OnNativeSample;
        int callbackResult = DashSdkNative.SetDataChangeCallBackFun(_callback);
        if (callbackResult < 0)
        {
            throw new InvalidOperationException($"SetDataChangeCallBackFun failed with code {callbackResult}.");
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
        DashSdkNative.ChangeGetDataStatus(_settings.GetDataType == GetDataType.SingleMachine);
        DashSdkNative.StartMacSample();
        _sampling = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_sampling)
        {
            DashSdkNative.StopMacSample();
            _sampling = false;
        }

        return Task.CompletedTask;
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

        int count = DashSdkNative.GetAllMacOnlineCount();
        for (int i = 0; i < count; i++)
        {
            DeviceDescriptor? descriptor = CreateDevice(i);
            if (descriptor is not null)
            {
                _devices.Add(descriptor);
            }
        }
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
            float sampleRate = DashSdkNative.GetMacCurrentSampleFreqEx(deviceId);
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
                    DataIndex: channelId,
                    Online: channelOnline != 0,
                    Name: $"AI{deviceId + 1}-{channelId + 1}",
                    Unit: "raw"));
            }

            return new DeviceDescriptor(deviceId, ip, sampleRate, online, channels);
        }
        finally
        {
            Marshal.FreeHGlobal(ipBuffer);
        }
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
                new IntPtr(varSampleData)));
        }
        catch
        {
            // The native SDK owns the callback thread. Never throw back into it.
        }
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
