using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DashCapture.Display;
using System.Globalization;

namespace DashCapture.App;

public sealed class WaveformControl : Control
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(242, 246, 251));
    private static readonly IBrush PlotBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.FromRgb(64, 76, 96));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(24, 35, 52));
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromRgb(216, 226, 238)), 1);
    private static readonly Pen MinorGridPen = new(new SolidColorBrush(Color.FromRgb(232, 238, 246)), 1);
    private static readonly Pen AxisPen = new(AxisBrush, 1.2);
    private static readonly Color[] Palette =
    {
        Color.FromRgb(24, 118, 210),
        Color.FromRgb(33, 150, 83),
        Color.FromRgb(230, 149, 0),
        Color.FromRgb(210, 69, 69),
        Color.FromRgb(123, 88, 210),
        Color.FromRgb(0, 143, 156),
        Color.FromRgb(214, 108, 32),
        Color.FromRgb(116, 132, 20)
    };

    public WaveformStore? Store { get; set; }
    public IReadOnlyList<Core.Models.ChannelDescriptor>? Channels { get; set; }
    public double WindowSeconds { get; set; } = 5;
    public double DefaultYAxisAmplitude { get; set; }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = Bounds;
        context.FillRectangle(BackgroundBrush, bounds);

        Rect plot = CreatePlotRect(bounds);
        context.FillRectangle(PlotBrush, plot);

        IReadOnlyList<WaveformSnapshot>? snapshot = Store?.SnapshotSeries(Channels);
        if (snapshot is null || snapshot.Count == 0 || snapshot.All(series => series.Points.Length == 0))
        {
            double amplitude = GetDefaultYAxisAmplitude();
            DrawAxes(
                context,
                plot,
                Math.Max(0.001, WindowSeconds),
                amplitude > 0 ? (float)-amplitude : -1,
                amplitude > 0 ? (float)amplitude : 1);
            return;
        }

        double visibleSeconds = FindVisibleSeconds(snapshot);
        (float rawMin, float rawMax) = FindVisibleRange(snapshot, visibleSeconds);
        if (rawMin == float.MaxValue || rawMax == float.MinValue)
        {
            rawMin = -1;
            rawMax = 1;
        }

        (float yMin, float yMax) = NiceBoundsWithPadding(rawMin, rawMax, GetDefaultYAxisAmplitude());
        DrawAxes(context, plot, visibleSeconds, yMin, yMax);

        double width = Math.Max(1, plot.Width);
        int channelIndex = 0;

        using (context.PushClip(plot))
        {
            foreach (WaveformSnapshot series in snapshot)
            {
                EnvelopePoint[] samples = series.Points;
                if (samples.Length == 0)
                {
                    continue;
                }

                double sampleRate = GetSampleRate(series.DisplaySampleRate);
                int sampleCount = Math.Min(samples.Length, Math.Max(1, (int)Math.Ceiling(visibleSeconds * sampleRate)));
                ReadOnlySpan<EnvelopePoint> visibleSamples = samples.AsSpan(samples.Length - sampleCount, sampleCount);
                EnvelopePoint[] envelope = EnvelopeDownsampler.Downsample(visibleSamples, (int)Math.Max(1, width));
                double seriesSeconds = Math.Min(visibleSeconds, sampleCount / sampleRate);
                var pen = new Pen(new SolidColorBrush(Palette[channelIndex % Palette.Length]), 1.4);
                DrawEnvelope(context, envelope, pen, plot, visibleSeconds, seriesSeconds, yMin, yMax);
                channelIndex++;
            }
        }
    }

    private static Rect CreatePlotRect(Rect bounds)
    {
        const double left = 70;
        const double top = 18;
        const double right = 18;
        const double bottom = 46;
        return new Rect(
            bounds.Left + left,
            bounds.Top + top,
            Math.Max(1, bounds.Width - left - right),
            Math.Max(1, bounds.Height - top - bottom));
    }

    private void DrawAxes(DrawingContext context, Rect plot, double visibleSeconds, float yMin, float yMax)
    {
        DrawAxisGrid(context, plot, yMin, yMax, vertical: false);
        DrawAxisGrid(context, plot, -Math.Max(0.001, visibleSeconds), 0, vertical: true);

        context.DrawLine(AxisPen, plot.BottomLeft, plot.BottomRight);
        context.DrawLine(AxisPen, plot.BottomLeft, plot.TopLeft);
        DrawText(context, "\u5E45\u503C", new Point(plot.Left - 58, plot.Top - 2), 13, LabelBrush);
        DrawText(context, "\u65F6\u95F4 (s)", new Point(plot.Right - 54, plot.Bottom + 27), 13, LabelBrush);
    }

    private static void DrawAxisGrid(DrawingContext context, Rect plot, double min, double max, bool vertical)
    {
        double range = Math.Max(0.000001, max - min);
        double targetPixels = vertical ? 92 : 54;
        int targetTicks = (int)Math.Clamp((vertical ? plot.Width : plot.Height) / targetPixels, 4, 12);
        double majorStep = NiceNumber(range / targetTicks, round: true);
        double minorStep = majorStep / 5;

        double minorStart = Math.Ceiling(min / minorStep) * minorStep;
        for (double value = minorStart; value <= max + minorStep * 0.5; value += minorStep)
        {
            double ratio = (value - min) / range;
            if (ratio < -0.0001 || ratio > 1.0001)
            {
                continue;
            }

            if (vertical)
            {
                double x = plot.Left + ratio * plot.Width;
                context.DrawLine(MinorGridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            }
            else
            {
                double y = plot.Bottom - ratio * plot.Height;
                context.DrawLine(MinorGridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            }
        }

        double majorStart = Math.Ceiling(min / majorStep) * majorStep;
        for (double value = majorStart; value <= max + majorStep * 0.5; value += majorStep)
        {
            double ratio = (value - min) / range;
            if (ratio < -0.0001 || ratio > 1.0001)
            {
                continue;
            }

            if (vertical)
            {
                double x = plot.Left + ratio * plot.Width;
                context.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                DrawText(context, FormatTime(value), new Point(x - 18, plot.Bottom + 9), 12, AxisBrush);
            }
            else
            {
                double y = plot.Bottom - ratio * plot.Height;
                context.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                DrawText(context, FormatAxisValue(value), new Point(plot.Left - 62, y - 9), 12, AxisBrush);
            }
        }
    }

    private static void DrawEnvelope(
        DrawingContext context,
        EnvelopePoint[] envelope,
        Pen pen,
        Rect plot,
        double visibleSeconds,
        double seriesSeconds,
        float min,
        float max)
    {
        if (envelope.Length == 0)
        {
            return;
        }

        double range = max - min;
        double xRange = Math.Max(0.000001, visibleSeconds);
        double xStart = -Math.Max(0.000001, seriesSeconds);
        Point? previous = null;
        for (int i = 0; i < envelope.Length; i++)
        {
            EnvelopePoint point = envelope[i];
            if (!IsFinite(point.Minimum) || !IsFinite(point.Maximum) || !IsFinite(point.Last))
            {
                previous = null;
                continue;
            }

            double seconds = envelope.Length == 1 ? 0 : xStart + i * seriesSeconds / (envelope.Length - 1);
            double x = plot.Left + (seconds + xRange) / xRange * plot.Width;
            double yMin = plot.Top + plot.Height - ((point.Minimum - min) / range) * plot.Height;
            double yMax = plot.Top + plot.Height - ((point.Maximum - min) / range) * plot.Height;
            double yLast = plot.Top + plot.Height - ((point.Last - min) / range) * plot.Height;
            context.DrawLine(pen, new Point(x, yMin), new Point(x, yMax));

            var current = new Point(x, yLast);
            if (previous is not null)
            {
                context.DrawLine(pen, previous.Value, current);
            }

            previous = current;
        }
    }

    private double FindVisibleSeconds(IReadOnlyList<WaveformSnapshot> snapshot)
    {
        double visibleSeconds = 0;
        foreach (WaveformSnapshot series in snapshot)
        {
            EnvelopePoint[] samples = series.Points;
            if (samples.Length == 0)
            {
                continue;
            }

            visibleSeconds = Math.Max(visibleSeconds, samples.Length / GetSampleRate(series.DisplaySampleRate));
        }

        return Math.Clamp(visibleSeconds <= 0 ? WindowSeconds : visibleSeconds, 0.001, Math.Max(0.001, WindowSeconds));
    }

    private static (float Min, float Max) FindVisibleRange(IReadOnlyList<WaveformSnapshot> snapshot, double visibleSeconds)
    {
        float globalMin = float.MaxValue;
        float globalMax = float.MinValue;
        foreach (WaveformSnapshot series in snapshot)
        {
            EnvelopePoint[] data = series.Points;
            int count = Math.Min(data.Length, Math.Max(1, (int)Math.Ceiling(visibleSeconds * GetSampleRate(series.DisplaySampleRate))));
            int start = Math.Max(0, data.Length - count);
            for (int i = start; i < data.Length; i++)
            {
                EnvelopePoint point = data[i];
                if (!IsFinite(point.Minimum) || !IsFinite(point.Maximum))
                {
                    continue;
                }

                if (point.Minimum < globalMin) globalMin = point.Minimum;
                if (point.Maximum > globalMax) globalMax = point.Maximum;
            }
        }

        return (globalMin, globalMax);
    }

    private static (float Min, float Max) NiceBoundsWithPadding(float min, float max, double defaultAmplitude)
    {
        if (defaultAmplitude > 0 && !double.IsNaN(defaultAmplitude) && !double.IsInfinity(defaultAmplitude))
        {
            double amplitude = Math.Max(Math.Abs(min), Math.Abs(max));
            if (amplitude <= defaultAmplitude)
            {
                return ((float)-defaultAmplitude, (float)defaultAmplitude);
            }

            min = (float)-amplitude;
            max = (float)amplitude;
        }

        if (Math.Abs(max - min) < 0.000001f)
        {
            float pad = Math.Max(1, Math.Abs(max) * 0.1f);
            min -= pad;
            max += pad;
        }
        else
        {
            float pad = (max - min) * 0.06f;
            min -= pad;
            max += pad;
        }

        double range = NiceNumber(max - min, round: false);
        double step = NiceNumber(range / 8, round: true);
        double niceMin = Math.Floor(min / step) * step;
        double niceMax = Math.Ceiling(max / step) * step;
        if (Math.Abs(niceMax - niceMin) < 0.000001)
        {
            niceMin -= 1;
            niceMax += 1;
        }

        return ((float)niceMin, (float)niceMax);
    }

    private static double GetSampleRate(float sampleRate)
    {
        return sampleRate > 0 && !float.IsNaN(sampleRate) && !float.IsInfinity(sampleRate)
            ? sampleRate
            : 1;
    }

    private double GetDefaultYAxisAmplitude()
    {
        return DefaultYAxisAmplitude > 0 && !double.IsNaN(DefaultYAxisAmplitude) && !double.IsInfinity(DefaultYAxisAmplitude)
            ? DefaultYAxisAmplitude
            : 0;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static double NiceNumber(double value, bool round)
    {
        double exponent = Math.Floor(Math.Log10(Math.Max(value, 0.0000001)));
        double fraction = value / Math.Pow(10, exponent);
        double niceFraction = round
            ? fraction < 1.5 ? 1 : fraction < 3 ? 2 : fraction < 7 ? 5 : 10
            : fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
        return niceFraction * Math.Pow(10, exponent);
    }

    private static string FormatAxisValue(double value)
    {
        double abs = Math.Abs(value);
        if (abs >= 1_000_000 || abs < 0.001 && abs > 0)
        {
            return value.ToString("0.###E+0", CultureInfo.InvariantCulture);
        }

        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string FormatTime(double seconds)
    {
        if (Math.Abs(seconds) < 0.0000005)
        {
            return "0";
        }

        return seconds.ToString(Math.Abs(seconds) < 1 ? "0.###" : "0.##", CultureInfo.InvariantCulture);
    }

    private static void DrawText(DrawingContext context, string text, Point origin, double fontSize, IBrush brush)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            brush);
        context.DrawText(formatted, origin);
    }
}
