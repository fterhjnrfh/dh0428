using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DashCapture.Display;
using System.Globalization;

namespace DashCapture.App;

public sealed class WaveformControl : Control
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(232, 236, 241));
    private static readonly IBrush PlotBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.FromRgb(72, 82, 96));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(20, 29, 39));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(101, 111, 126));
    private static readonly IBrush LegendBackgroundBrush = new SolidColorBrush(Color.FromArgb(226, 255, 255, 255));
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromRgb(214, 220, 228)), 1);
    private static readonly Pen MinorGridPen = new(new SolidColorBrush(Color.FromRgb(230, 234, 239)), 1);
    private static readonly Pen AxisPen = new(AxisBrush, 1.2);
    private static readonly Pen ZeroLinePen = new(new SolidColorBrush(Color.FromRgb(128, 141, 158)), 1.3);
    private static readonly Pen PlotBorderPen = new(new SolidColorBrush(Color.FromRgb(190, 198, 208)), 1);
    private static readonly Color[] Palette =
    {
        Color.FromRgb(32, 91, 138),
        Color.FromRgb(38, 117, 85),
        Color.FromRgb(153, 106, 38),
        Color.FromRgb(153, 72, 70),
        Color.FromRgb(93, 86, 133),
        Color.FromRgb(45, 112, 124),
        Color.FromRgb(132, 85, 45),
        Color.FromRgb(100, 111, 48)
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

        IReadOnlyList<WaveformSnapshot>? snapshot = Store?.SnapshotSeries(Channels);
        if (snapshot is null || snapshot.Count == 0 || snapshot.All(series => series.Points.Length == 0))
        {
            double amplitude = GetDefaultYAxisAmplitude();
            double emptyMin = amplitude > 0 ? -amplitude : -1;
            double emptyMax = amplitude > 0 ? amplitude : 1;
            Rect emptyPlot = CreatePlotRect(bounds, emptyMin, emptyMax);
            DrawPlotSurface(context, emptyPlot);
            DrawAxes(
                context,
                emptyPlot,
                Math.Max(0.001, WindowSeconds),
                emptyMin,
                emptyMax);
            DrawText(context, "\u6682\u65e0\u6ce2\u5f62\u6570\u636e", new Point(emptyPlot.Left + 14, emptyPlot.Top + 12), 14, MutedBrush);
            return;
        }

        double visibleSeconds = FindVisibleSeconds(snapshot);
        (double rawMin, double rawMax) = FindVisibleRange(snapshot, visibleSeconds);
        if (!IsFinite(rawMin) || !IsFinite(rawMax))
        {
            rawMin = -1;
            rawMax = 1;
        }

        (double yMin, double yMax) = NiceBoundsWithPadding(rawMin, rawMax, GetDefaultYAxisAmplitude());
        Rect plot = CreatePlotRect(bounds, yMin, yMax);
        DrawPlotSurface(context, plot);
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
                var pen = new Pen(new SolidColorBrush(SeriesColor(series, channelIndex)), 1.4);
                DrawEnvelope(context, envelope, pen, plot, visibleSeconds, seriesSeconds, yMin, yMax);
                channelIndex++;
            }
        }

        DrawLegend(context, plot, snapshot);
    }

    private static Rect CreatePlotRect(Rect bounds, double yMin, double yMax)
    {
        const double top = 18;
        const double right = 18;
        const double bottom = 48;
        double left = Math.Clamp(MeasureYAxisWidth(yMin, yMax, bounds.Height) + 20, 72, 148);
        return new Rect(
            bounds.Left + left,
            bounds.Top + top,
            Math.Max(1, bounds.Width - left - right),
            Math.Max(1, bounds.Height - top - bottom));
    }

    private static void DrawPlotSurface(DrawingContext context, Rect plot)
    {
        context.FillRectangle(PlotBrush, plot);
        context.DrawRectangle(null, PlotBorderPen, plot);
    }

    private void DrawAxes(DrawingContext context, Rect plot, double visibleSeconds, double yMin, double yMax)
    {
        DrawAxisGrid(context, plot, yMin, yMax, vertical: false);
        DrawAxisGrid(context, plot, -Math.Max(0.001, visibleSeconds), 0, vertical: true);

        if (yMin < 0 && yMax > 0)
        {
            double zeroRatio = (0 - yMin) / Math.Max(0.000001, yMax - yMin);
            double zeroY = plot.Bottom - zeroRatio * plot.Height;
            context.DrawLine(ZeroLinePen, new Point(plot.Left, zeroY), new Point(plot.Right, zeroY));
        }

        context.DrawLine(AxisPen, plot.BottomLeft, plot.BottomRight);
        context.DrawLine(AxisPen, plot.BottomLeft, plot.TopLeft);
        DrawText(context, "\u5E45\u503C", new Point(Math.Max(8, plot.Left - 68), plot.Top - 2), 13, LabelBrush);
        DrawText(context, "\u65F6\u95F4 (s)", new Point(plot.Right - 58, plot.Bottom + 28), 13, LabelBrush);
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
                string label = FormatAxisValue(value, majorStep);
                double labelWidth = MeasureText(label, 12).Width;
                DrawText(context, label, new Point(plot.Left - labelWidth - 10, y - 9), 12, AxisBrush);
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
        double min,
        double max)
    {
        if (envelope.Length == 0)
        {
            return;
        }

        double range = Math.Max(0.000001, max - min);
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

    private static (double Min, double Max) FindVisibleRange(IReadOnlyList<WaveformSnapshot> snapshot, double visibleSeconds)
    {
        double globalMin = double.PositiveInfinity;
        double globalMax = double.NegativeInfinity;
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

    private static (double Min, double Max) NiceBoundsWithPadding(double min, double max, double defaultAmplitude)
    {
        if (defaultAmplitude > 0 && !double.IsNaN(defaultAmplitude) && !double.IsInfinity(defaultAmplitude))
        {
            double amplitude = Math.Max(Math.Abs(min), Math.Abs(max));
            if (amplitude <= defaultAmplitude)
            {
                return (-defaultAmplitude, defaultAmplitude);
            }

            min = -amplitude;
            max = amplitude;
        }

        if (Math.Abs(max - min) < 0.000001)
        {
            double pad = Math.Max(1e-6, Math.Max(1, Math.Abs(max)) * 0.08);
            min -= pad;
            max += pad;
        }
        else
        {
            double pad = (max - min) * 0.08;
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

        return (niceMin, niceMax);
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

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
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

    private static string FormatAxisValue(double value, double step)
    {
        if (Math.Abs(value) <= Math.Max(1e-12, Math.Abs(step) * 1e-7))
        {
            return "0";
        }

        double abs = Math.Abs(value);
        if (abs >= 1_000_000 || abs < 0.000001 && abs > 0)
        {
            return value.ToString("0.######E+0", CultureInfo.InvariantCulture);
        }

        int decimals = step > 0
            ? (int)Math.Clamp(Math.Ceiling(-Math.Log10(step)) + 1, 0, 8)
            : 4;
        string format = decimals == 0 ? "0" : "0." + new string('#', decimals);
        return value.ToString(format, CultureInfo.InvariantCulture);
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

    private static Size MeasureText(string text, double fontSize)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            Brushes.Black);
        return new Size(formatted.Width, formatted.Height);
    }

    private static double MeasureYAxisWidth(double min, double max, double height)
    {
        double range = Math.Max(0.000001, max - min);
        int targetTicks = (int)Math.Clamp(Math.Max(1, height - 66) / 54, 4, 12);
        double majorStep = NiceNumber(range / targetTicks, round: true);
        double majorStart = Math.Ceiling(min / majorStep) * majorStep;
        double width = MeasureText(FormatAxisValue(min, majorStep), 12).Width;
        width = Math.Max(width, MeasureText(FormatAxisValue(max, majorStep), 12).Width);
        for (double value = majorStart; value <= max + majorStep * 0.5; value += majorStep)
        {
            width = Math.Max(width, MeasureText(FormatAxisValue(value, majorStep), 12).Width);
        }

        return width;
    }

    private static Color SeriesColor(WaveformSnapshot series, int fallbackIndex)
    {
        int hash = 17;
        unchecked
        {
            foreach (char c in series.Channel.DeviceIp)
            {
                hash = hash * 31 + c;
            }

            hash = hash * 31 + series.Channel.DeviceId;
            hash = hash * 31 + series.Channel.ChannelId;
        }

        int index = Math.Abs(hash == int.MinValue ? fallbackIndex : hash) % Palette.Length;
        return Palette[index];
    }

    private static void DrawLegend(DrawingContext context, Rect plot, IReadOnlyList<WaveformSnapshot> snapshot)
    {
        WaveformSnapshot[] items = snapshot
            .Where(series => series.Points.Length > 0)
            .Take(6)
            .ToArray();
        if (items.Length == 0)
        {
            return;
        }

        double rowHeight = 18;
        double width = Math.Min(220, Math.Max(120, plot.Width * 0.38));
        double height = items.Length * rowHeight + 8;
        var panel = new Rect(plot.Right - width - 8, plot.Top + 8, width, height);
        context.DrawRectangle(LegendBackgroundBrush, PlotBorderPen, panel, 6);

        for (int i = 0; i < items.Length; i++)
        {
            WaveformSnapshot series = items[i];
            double y = panel.Top + 8 + i * rowHeight;
            var pen = new Pen(new SolidColorBrush(SeriesColor(series, i)), 2);
            context.DrawLine(pen, new Point(panel.Left + 10, y + 7), new Point(panel.Left + 24, y + 7));
            string name = string.IsNullOrWhiteSpace(series.Channel.Name)
                ? $"\u901a\u9053 {series.Channel.ChannelId + 1}"
                : series.Channel.Name;
            DrawText(context, TrimText(name, 20), new Point(panel.Left + 30, y - 1), 12, LabelBrush);
        }
    }

    private static string TrimText(string text, int maxLength)
    {
        return text.Length <= maxLength
            ? text
            : text[..Math.Max(1, maxLength - 1)] + "\u2026";
    }
}
