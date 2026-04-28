namespace DashCapture.Core.Models;

public sealed record AcquisitionFault(
    DateTimeOffset Timestamp,
    string Code,
    string Message,
    int? DeviceId = null,
    long? ExpectedPosition = null,
    long? ActualPosition = null);

public enum BackpressureLevel
{
    Normal,
    ReduceDisplay,
    PauseDisplay,
    StopRequired
}

public sealed record CaptureTelemetry(
    DateTimeOffset Timestamp,
    long BlocksReceived,
    long BytesReceived,
    long DisplayDrops,
    int StorageQueueDepth,
    int DisplayQueueDepth,
    BackpressureLevel BackpressureLevel,
    string Status);
