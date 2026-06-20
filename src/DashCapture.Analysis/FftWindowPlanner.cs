using DashCapture.Core.Configuration;
using DashCapture.Core.Models;

namespace DashCapture.Analysis;

internal static class FftWindowPlanner
{
    public static FftWindowParameters Resolve(AnalysisSettings settings, ChannelDescriptor channel)
    {
        if (!settings.UseSampleRateWindowing)
        {
            return new FftWindowParameters(
                Math.Max(2, settings.WindowSampleCount),
                Math.Max(1, settings.HopSampleCount));
        }

        double sampleRate = IsValidSampleRate(channel.SampleRate) ? channel.SampleRate : 1;
        double resolutionHz = settings.FftResolutionHz > 0 &&
                              !double.IsNaN(settings.FftResolutionHz) &&
                              !double.IsInfinity(settings.FftResolutionHz)
            ? settings.FftResolutionHz
            : 1.0;
        double overlapRatio = double.IsNaN(settings.FftOverlapRatio) || double.IsInfinity(settings.FftOverlapRatio)
            ? 0.9
            : Math.Clamp(settings.FftOverlapRatio, 0, 0.999);
        int windowSampleCount = Math.Max(2, ClampToInt(Math.Round(sampleRate / resolutionHz)));
        int hopSampleCount = Math.Max(1, ClampToInt(Math.Round(windowSampleCount * (1.0 - overlapRatio))));
        return new FftWindowParameters(windowSampleCount, hopSampleCount);
    }

    private static bool IsValidSampleRate(float sampleRate)
    {
        return sampleRate > 0 && !float.IsNaN(sampleRate) && !float.IsInfinity(sampleRate);
    }

    private static int ClampToInt(double value)
    {
        if (value <= 1)
        {
            return 1;
        }

        return value >= int.MaxValue ? int.MaxValue : (int)value;
    }
}

internal readonly record struct FftWindowParameters(int WindowSampleCount, int HopSampleCount);
