using DashCapture.Core.Models;

namespace DashCapture.Core.Sources;

public interface IAcquisitionSource : IAsyncDisposable
{
    event Action<SdkSampleData>? SampleReceived;
    IReadOnlyList<DeviceDescriptor> Devices { get; }
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken);
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
