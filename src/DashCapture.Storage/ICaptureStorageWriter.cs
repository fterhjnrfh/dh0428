using DashCapture.Core.Memory;
using DashCapture.Core.Models;

namespace DashCapture.Storage;

public interface ICaptureStorageWriter : IDisposable
{
    string CurrentPath { get; }
    string CurrentFolder { get; }
    string CurrentAuditPath { get; }
    CaptureStorageStatistics Statistics { get; }
    event Action<Exception, string>? Faulted;

    void AppendBlock(AcquisitionBlock block);
    void UpdateDeviceRates(IReadOnlyList<DeviceDescriptor> devices);
    void Save();
}

public sealed record CaptureStorageStatistics(
    long RawBytes,
    long WrittenBytes,
    long PayloadBytes,
    long TotalBlocks,
    long CompressedBlocks,
    long StoredBlocks,
    long RawStoredBlocks,
    string Codec,
    string Preprocessor,
    double WriteThroughputMbPerSecond,
    int CompressionQueueDepth = 0,
    int WriteQueueDepth = 0,
    int CompressionWorkerCount = 0);
