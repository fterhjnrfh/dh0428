namespace DashCapture.Display;

public static class EnvelopeDownsampler
{
    public static EnvelopePoint[] Downsample(ReadOnlySpan<float> samples, int pixelWidth)
    {
        if (samples.Length == 0 || pixelWidth <= 0)
        {
            return Array.Empty<EnvelopePoint>();
        }

        int buckets = Math.Min(samples.Length, pixelWidth);
        var output = new EnvelopePoint[buckets];
        for (int pixel = 0; pixel < buckets; pixel++)
        {
            int start = (int)((long)pixel * samples.Length / buckets);
            int end = (int)((long)(pixel + 1) * samples.Length / buckets);
            if (end <= start)
            {
                end = start + 1;
            }

            float first = samples[start];
            float last = samples[end - 1];
            float min = first;
            float max = first;
            for (int i = start + 1; i < end; i++)
            {
                float value = samples[i];
                if (value < min) min = value;
                if (value > max) max = value;
            }

            output[pixel] = new EnvelopePoint(pixel, first, last, min, max);
        }

        return output;
    }
}
