using DashCapture.Core.Memory;
using DashCapture.Core.Models;

namespace DashCapture.Storage;

public interface ICaptureStorageWriter : IDisposable
{
    string CurrentPath { get; }
    string CurrentFolder { get; }
    string CurrentAuditPath { get; }
    event Action<Exception, string>? Faulted;

    void AppendBlock(AcquisitionBlock block);
    void UpdateDeviceRates(IReadOnlyList<DeviceDescriptor> devices);
    void Save();
}
