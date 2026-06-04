using System.Collections.Concurrent;
using DashCapture.Core.Models;

namespace DashCapture.Core.Acquisition;

public sealed class ContinuityTracker
{
    private readonly ConcurrentDictionary<int, long> _expectedPositions = new();

    public AcquisitionFault? Validate(SdkSampleData sample)
    {
        int deviceKey = sample.GroupId >= 0 ? sample.GroupId : sample.MachineId;
        int key = sample.ChannelId >= 0 ? HashCode.Combine(deviceKey, sample.ChannelId) : deviceKey;
        bool mismatch = false;
        long expected = 0;

        _expectedPositions.AddOrUpdate(
            key,
            addValueFactory: _ => sample.TotalDataCount + sample.DataCountPerChannel,
            updateValueFactory: (_, currentExpected) =>
            {
                expected = currentExpected;
                mismatch = sample.TotalDataCount != currentExpected;
                return sample.TotalDataCount + sample.DataCountPerChannel;
            });

        if (!mismatch)
        {
            return null;
        }

        return new AcquisitionFault(
            DateTimeOffset.UtcNow,
            "DATA_NOT_CONTINUOUS",
            sample.ChannelId >= 0
                ? $"Device {deviceKey} channel {sample.ChannelId} data is not continuous. Expected {expected}, actual {sample.TotalDataCount}."
                : $"Device {deviceKey} data is not continuous. Expected {expected}, actual {sample.TotalDataCount}.",
            deviceKey,
            expected,
            sample.TotalDataCount);
    }

    public void Reset() => _expectedPositions.Clear();
}
