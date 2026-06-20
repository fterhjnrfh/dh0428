using DashCapture.Native;

namespace DashCapture.Analysis;

public sealed class GpuFftProcessor : IDisposable
{
    private readonly Lazy<GpuAvailability> _availability = new(CheckAvailability);

    public bool IsAvailable => _availability.Value.Available;
    public string DeviceName => _availability.Value.DeviceName;
    public string AvailabilityError => _availability.Value.Error;

    public bool TryComputeMagnitude(ReadOnlySpan<float> samples, Span<float> magnitudes, out string error)
    {
        error = string.Empty;
        if (!IsAvailable)
        {
            error = AvailabilityError;
            return false;
        }

        return CudaFftNative.TryComputeMagnitude(samples, magnitudes, out error);
    }

    public bool TryComputeMagnitudeBatch(
        ReadOnlySpan<float> samples,
        int fftSize,
        int batchCount,
        Span<float> magnitudes,
        out string error)
    {
        error = string.Empty;
        if (!IsAvailable)
        {
            error = AvailabilityError;
            return false;
        }

        return CudaFftNative.TryComputeMagnitudeBatch(samples, fftSize, batchCount, magnitudes, out error);
    }

    public void Dispose()
    {
        CudaFftNative.Dispose();
    }

    private static GpuAvailability CheckAvailability()
    {
        return CudaFftNative.IsAvailable(out string deviceName, out string error)
            ? new GpuAvailability(true, deviceName, string.Empty)
            : new GpuAvailability(false, string.Empty, error);
    }

    private sealed record GpuAvailability(bool Available, string DeviceName, string Error);
}
