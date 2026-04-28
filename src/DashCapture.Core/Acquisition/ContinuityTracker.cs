using System.Collections.Concurrent;
using DashCapture.Core.Models;

namespace DashCapture.Core.Acquisition;

public sealed class ContinuityTracker
{
    private readonly ConcurrentDictionary<int, long> _expectedPositions = new();

    public AcquisitionFault? Validate(SdkSampleData sample)
    {
        int key = sample.GroupId >= 0 ? sample.GroupId : sample.MachineId;
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
            $"Device {key} data is not continuous. Expected {expected}, actual {sample.TotalDataCount}.",
            key,
            expected,
            sample.TotalDataCount);
    }

    public void Reset() => _expectedPositions.Clear();
}
