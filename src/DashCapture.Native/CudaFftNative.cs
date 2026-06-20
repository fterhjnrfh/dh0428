using System.Runtime.InteropServices;
using System.Text;

namespace DashCapture.Native;

public static unsafe class CudaFftNative
{
    private const string LibName = "DashCapture.CudaFft";

    public static bool IsAvailable(out string deviceName, out string error)
    {
        deviceName = string.Empty;
        error = string.Empty;
        try
        {
            byte[] buffer = new byte[256];
            fixed (byte* ptr = buffer)
            {
                int result = dc_cuda_fft_get_device_name(ptr, buffer.Length);
                if (result != 0)
                {
                    error = $"CUDA device query failed with code {result}.";
                    return false;
                }
            }

            deviceName = DecodeNullTerminated(buffer);
            return !string.IsNullOrWhiteSpace(deviceName);
        }
        catch (Exception ex) when (IsNativeLoadException(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryComputeMagnitude(ReadOnlySpan<float> samples, Span<float> magnitudes, out string error)
    {
        error = string.Empty;
        if (samples.IsEmpty)
        {
            return true;
        }

        try
        {
            byte[] errorBytes = new byte[512];
            fixed (float* samplePtr = samples)
            fixed (float* magnitudePtr = magnitudes)
            fixed (byte* errorPtr = errorBytes)
            {
                int result = dc_cuda_fft_compute_magnitude(
                    samplePtr,
                    samples.Length,
                    magnitudePtr,
                    magnitudes.Length,
                    errorPtr,
                    errorBytes.Length);
                if (result == 0)
                {
                    return true;
                }

                error = DecodeNullTerminated(errorBytes);
                if (string.IsNullOrWhiteSpace(error))
                {
                    error = $"CUDA FFT failed with code {result}.";
                }

                return false;
            }
        }
        catch (Exception ex) when (IsNativeLoadException(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryComputeMagnitudeBatch(
        ReadOnlySpan<float> samples,
        int fftSize,
        int batchCount,
        Span<float> magnitudes,
        out string error)
    {
        error = string.Empty;
        if (batchCount <= 0)
        {
            return true;
        }

        int binCount = fftSize / 2 + 1;
        int requiredSampleCount = checked(fftSize * batchCount);
        int requiredMagnitudeCount = checked(binCount * batchCount);
        if (fftSize <= 0 || samples.Length < requiredSampleCount || magnitudes.Length < requiredMagnitudeCount)
        {
            error = "Invalid CUDA FFT batch input or output length.";
            return false;
        }

        try
        {
            byte[] errorBytes = new byte[512];
            fixed (float* samplePtr = samples)
            fixed (float* magnitudePtr = magnitudes)
            fixed (byte* errorPtr = errorBytes)
            {
                int result = dc_cuda_fft_compute_magnitude_batch(
                    samplePtr,
                    fftSize,
                    batchCount,
                    magnitudePtr,
                    requiredMagnitudeCount,
                    errorPtr,
                    errorBytes.Length);
                if (result == 0)
                {
                    return true;
                }

                error = DecodeNullTerminated(errorBytes);
                if (string.IsNullOrWhiteSpace(error))
                {
                    error = $"CUDA FFT batch failed with code {result}.";
                }

                return false;
            }
        }
        catch (Exception ex) when (IsNativeLoadException(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    public static void Dispose()
    {
        try
        {
            dc_cuda_fft_dispose();
        }
        catch (Exception ex) when (IsNativeLoadException(ex))
        {
        }
    }

    private static string DecodeNullTerminated(byte[] bytes)
    {
        int length = Array.IndexOf(bytes, (byte)0);
        if (length < 0)
        {
            length = bytes.Length;
        }

        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    private static bool IsNativeLoadException(Exception ex)
    {
        return ex is DllNotFoundException ||
               ex is EntryPointNotFoundException ||
               ex is BadImageFormatException;
    }

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int dc_cuda_fft_get_device_name(byte* buffer, int capacity);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int dc_cuda_fft_compute_magnitude(
        float* samples,
        int fftSize,
        float* magnitudes,
        int magnitudeCount,
        byte* error,
        int errorCapacity);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int dc_cuda_fft_compute_magnitude_batch(
        float* samples,
        int fftSize,
        int batchCount,
        float* magnitudes,
        int magnitudeCount,
        byte* error,
        int errorCapacity);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void dc_cuda_fft_dispose();
}
