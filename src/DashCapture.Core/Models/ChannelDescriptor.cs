namespace DashCapture.Core.Models;

public sealed record ChannelDescriptor(
    int DeviceId,
    string DeviceIp,
    int ChannelId,
    int DataIndex,
    int LocalDataIndex,
    bool Online,
    string Name,
    string Unit = "raw",
    float SampleRate = 1);

public sealed record DeviceDescriptor(
    int DeviceId,
    string IpAddress,
    float SampleRate,
    bool Online,
    IReadOnlyList<ChannelDescriptor> Channels)
{
    public string DisplayName => $"Device {DeviceId + 1} ({IpAddress})";
}

public readonly record struct ChannelKey(string DeviceIp, int DeviceId, int ChannelId)
{
    public ChannelKey(ChannelDescriptor channel)
        : this(channel.DeviceIp, channel.DeviceId, channel.ChannelId)
    {
    }

    public override string ToString() => $"{DeviceIp}:{DeviceId}:{ChannelId}";
}
