using System;

namespace DashCapture.Storage;

public static class NativeDeinterleaver
{
    public static unsafe void CopyFloatChannel(IntPtr source, int sampleCount, int channelCount, int dataIndex, Span<float> destination)
    {
        if (sampleCount <= 0 || channelCount <= 0 || dataIndex < 0 || dataIndex >= channelCount)
        {
            return;
        }

        if (destination.Length < sampleCount)
        {
            throw new ArgumentException("Destination is smaller than the requested sample count.", nameof(destination));
        }

        float* src = (float*)source.ToPointer();
        for (int i = 0; i < sampleCount; i++)
        {
            destination[i] = src[i * channelCount + dataIndex];
        }
    }
}
