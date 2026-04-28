namespace DashCapture.Core.Models;

public sealed record ChannelDescriptor(
    int DeviceId,
    string DeviceIp,
    int ChannelId,
    int DataIndex,
    bool Online,
    string Name,
    string Unit = "raw");

public sealed record DeviceDescriptor(
    int DeviceId,
    string IpAddress,
    float SampleRate,
    bool Online,
    IReadOnlyList<ChannelDescriptor> Channels)
{
    public string DisplayName => $"Device {DeviceId + 1} ({IpAddress})";
}

public readonly record struct ChannelKey(int DeviceId, int ChannelId)
{
    public override string ToString() => $"{DeviceId}:{ChannelId}";
}
