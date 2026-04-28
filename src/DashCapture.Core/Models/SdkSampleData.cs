using System;

namespace DashCapture.Core.Models;

public readonly record struct SdkSampleData(
    long SampleTime,
    string GroupInfo,
    int MessageType,
    int GroupId,
    int ChannelStyle,
    int ChannelId,
    int MachineId,
    long TotalDataCount,
    int DataCountPerChannel,
    int BufferCount,
    int BlockIndex,
    IntPtr DataPointer);

public static class DashSampleMessageType
{
    public const int AnalogData = 0;
    public const int SingleGroupAnalogData = 5;
    public const int AnalogMultiChannelData = 21;
}
