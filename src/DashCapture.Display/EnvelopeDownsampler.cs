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

            bool hasValue = false;
            float first = float.NaN;
            float last = float.NaN;
            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = start; i < end; i++)
            {
                float value = samples[i];
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    continue;
                }

                if (!hasValue)
                {
                    first = value;
                    hasValue = true;
                }

                last = value;
                if (value < min) min = value;
                if (value > max) max = value;
            }

            output[pixel] = hasValue
                ? new EnvelopePoint(pixel, first, last, min, max)
                : new EnvelopePoint(pixel, float.NaN, float.NaN, float.NaN, float.NaN);
        }

        return output;
    }

    public static EnvelopePoint[] Downsample(ReadOnlySpan<EnvelopePoint> points, int pixelWidth)
    {
        if (points.Length == 0 || pixelWidth <= 0)
        {
            return Array.Empty<EnvelopePoint>();
        }

        int buckets = Math.Min(points.Length, pixelWidth);
        var output = new EnvelopePoint[buckets];
        for (int pixel = 0; pixel < buckets; pixel++)
        {
            int start = (int)((long)pixel * points.Length / buckets);
            int end = (int)((long)(pixel + 1) * points.Length / buckets);
            if (end <= start)
            {
                end = start + 1;
            }

            bool hasValue = false;
            float first = float.NaN;
            float last = float.NaN;
            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = start; i < end; i++)
            {
                EnvelopePoint point = points[i];
                if (float.IsNaN(point.Minimum) || float.IsInfinity(point.Minimum) ||
                    float.IsNaN(point.Maximum) || float.IsInfinity(point.Maximum))
                {
                    continue;
                }

                if (!hasValue)
                {
                    first = point.First;
                    hasValue = true;
                }

                last = point.Last;
                if (point.Minimum < min) min = point.Minimum;
                if (point.Maximum > max) max = point.Maximum;
            }

            output[pixel] = hasValue
                ? new EnvelopePoint(pixel, first, last, min, max)
                : new EnvelopePoint(pixel, float.NaN, float.NaN, float.NaN, float.NaN);
        }

        return output;
    }
}
