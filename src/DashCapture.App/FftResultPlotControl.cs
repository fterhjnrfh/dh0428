using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DashCapture.Analysis;

namespace DashCapture.App;

public enum FftTrendMetric
{
    PeakFrequency,
    PeakMagnitude
}

public sealed class FftResultPlotControl : Control
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(232, 236, 241));
    private static readonly IBrush PlotBrush = Brushes.White;
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.FromRgb(72, 82, 96));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(20, 29, 39));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(101, 111, 126));
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromRgb(214, 220, 228)), 1);
    private static readonly Pen MinorGridPen = new(new SolidColorBrush(Color.FromRgb(230, 234, 239)), 1);
    private static readonly Pen AxisPen = new(AxisBrush, 1.2);
    private static readonly Pen SpectrumPen = new(new SolidColorBrush(Color.FromRgb(31, 91, 140)), 1.2);
    private static readonly Pen TrendPen = new(new SolidColorBrush(Color.FromRgb(35, 117, 80)), 1.4);
    private static readonly Pen PeakPen = new(new SolidColorBrush(Color.FromRgb(159, 66, 66)), 1.2);
    private const double AxisFontSize = 11;
    private const double TitleFontSize = 13;
    private const double MinPlotWidth = 64;
    private const double MinPlotHeight = 64;

    private FftResultFrame? _spectrum;
    private FftChannelTrend? _trend;

    public FftTrendMetric TrendMetric { get; private set; }

    public void SetData(FftResultFrame? spectrum, FftChannelTrend? trend, FftTrendMetric metric)
    {
        _spectrum = spectrum;
        _trend = trend;
        TrendMetric = metric;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = new(0, 0, Bounds.Width, Bounds.Height);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        context.FillRectangle(BackgroundBrush, bounds);
        double gap = 18;
        double panelHeight = Math.Max(MinPlotHeight, (bounds.Height - gap) / 2);
        Rect topArea = new(bounds.Left, bounds.Top, bounds.Width, panelHeight);
        Rect bottomArea = new(bounds.Left, bounds.Top + panelHeight + gap, bounds.Width, Math.Max(MinPlotHeight, bounds.Height - panelHeight - gap));

        DrawSpectrum(context, CreatePlotRect(topArea), topArea);
        DrawTrend(context, CreatePlotRect(bottomArea), bottomArea);
    }

    private void DrawSpectrum(DrawingContext context, Rect plot, Rect area)
    {
        context.FillRectangle(PlotBrush, plot);
        FftResultFrame? frame = _spectrum;
        if (frame is null || frame.Magnitudes.Length == 0)
        {
            DrawAxes(context, plot, 0, 1, 0, 1, "频率 (Hz)", "幅值");
            DrawText(context, "未加载 FFT 频谱", new Point(plot.Left + 14, plot.Top + 12), TitleFontSize, MutedBrush);
            return;
        }

        double xMax = Math.Max(frame.FrequencyResolution, (frame.Magnitudes.Length - 1) * frame.FrequencyResolution);
        double yMax = FindMagnitudeMax(frame.Magnitudes);
        if (yMax <= 0 || double.IsNaN(yMax) || double.IsInfinity(yMax))
        {
            yMax = 1;
        }

        yMax = NiceCeiling(yMax);
        DrawAxes(context, plot, 0, xMax, 0, yMax, "频率 (Hz)", "幅值");
        FftPeak peak = frame.FindPeak(ignoreDc: true);
        string peakText = peak.BinIndex >= 0 ? $"  主峰 {peak.FrequencyHz:0.###} Hz" : string.Empty;
        DrawPanelTitle(context, $"频谱：{frame.ChannelName}  第 {frame.WindowIndex:N0} 帧{peakText}", plot, area);

        using (context.PushClip(plot))
        {
            DrawMagnitudeLine(context, frame, plot, xMax, yMax);
            DrawPeakMarker(context, peak, plot, xMax, yMax);
        }
    }

    private void DrawTrend(DrawingContext context, Rect plot, Rect area)
    {
        context.FillRectangle(PlotBrush, plot);
        FftChannelTrend? trend = _trend;
        if (trend is null || trend.Points.Count == 0)
        {
            DrawAxes(context, plot, 0, 1, 0, 1, "时间 (s)", TrendMetric == FftTrendMetric.PeakFrequency ? "峰值Hz" : "峰值幅值");
            DrawText(context, "未加载趋势", new Point(plot.Left + 14, plot.Top + 12), TitleFontSize, MutedBrush);
            return;
        }

        double xMin = trend.Points[0].TimeSeconds;
        double xMax = trend.Points[^1].TimeSeconds;
        if (xMax <= xMin)
        {
            xMax = xMin + 1;
        }

        (double yMin, double yMax) = FindTrendRange(trend, TrendMetric);
        (yMin, yMax) = NiceBounds(yMin, yMax);
        string yTitle = TrendMetric == FftTrendMetric.PeakFrequency ? "峰值Hz" : "峰值幅值";
        DrawAxes(context, plot, xMin, xMax, yMin, yMax, "时间 (s)", yTitle);
        DrawPanelTitle(context, $"趋势：{FormatChannelDisplayName(trend.Channel)}  {trend.Points.Count:N0} 点", plot, area);

        using (context.PushClip(plot))
        {
            DrawTrendLine(context, trend, plot, xMin, xMax, yMin, yMax);
        }
    }

    private static Rect CreatePlotRect(Rect area)
    {
        double top = 34;
        double right = 12;
        double bottom = 38;
        double left = 94;
        return new Rect(
            area.Left + left,
            area.Top + top,
            Math.Max(MinPlotWidth, area.Width - left - right),
            Math.Max(MinPlotHeight, area.Height - top - bottom));
    }

    private static void DrawAxes(DrawingContext context, Rect plot, double xMin, double xMax, double yMin, double yMax, string xTitle, string yTitle)
    {
        DrawGrid(context, plot, xMin, xMax, vertical: true);
        DrawGrid(context, plot, yMin, yMax, vertical: false);
        context.DrawLine(AxisPen, plot.BottomLeft, plot.BottomRight);
        context.DrawLine(AxisPen, plot.BottomLeft, plot.TopLeft);
        DrawText(context, xTitle, new Point(plot.Right - 74, plot.Bottom + 18), AxisFontSize, LabelBrush);
        DrawText(context, yTitle, new Point(plot.Left - 86, plot.Top - 26), AxisFontSize, LabelBrush);
    }

    private static void DrawPanelTitle(DrawingContext context, string title, Rect plot, Rect area)
    {
        DrawText(context, title, new Point(plot.Left + 8, Math.Max(area.Top + 2, plot.Top - 26)), TitleFontSize, LabelBrush);
    }

    private static void DrawGrid(DrawingContext context, Rect plot, double min, double max, bool vertical)
    {
        double range = Math.Max(0.000001, max - min);
        double targetPixels = vertical ? 110 : 46;
        int targetTicks = (int)Math.Clamp((vertical ? plot.Width : plot.Height) / targetPixels, 3, 9);
        double majorStep = NiceNumber(range / targetTicks, round: true);
        double minorStep = majorStep / 2;

        double minorStart = Math.Ceiling(min / minorStep) * minorStep;
        for (double value = minorStart; value <= max + minorStep * 0.5; value += minorStep)
        {
            double ratio = (value - min) / range;
            if (ratio < -0.001 || ratio > 1.001)
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
            if (ratio < -0.001 || ratio > 1.001)
            {
                continue;
            }

            if (vertical)
            {
                double x = plot.Left + ratio * plot.Width;
                context.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                DrawText(context, FormatAxisValue(value, majorStep), new Point(x - 18, plot.Bottom + 4), AxisFontSize, AxisBrush);
            }
            else
            {
                double y = plot.Bottom - ratio * plot.Height;
                context.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                DrawText(context, FormatAxisValue(value, majorStep), new Point(plot.Left - 68, y - 8), AxisFontSize, AxisBrush);
            }
        }
    }

    private static void DrawMagnitudeLine(DrawingContext context, FftResultFrame frame, Rect plot, double xMax, double yMax)
    {
        int count = frame.Magnitudes.Length;
        int target = Math.Min(count, Math.Max(1, (int)plot.Width * 2));
        Point? previous = null;
        for (int bucket = 0; bucket < target; bucket++)
        {
            int start = bucket * count / target;
            int end = Math.Max(start + 1, (bucket + 1) * count / target);
            float max = 0;
            int best = start;
            for (int i = start; i < end && i < count; i++)
            {
                float value = frame.Magnitudes[i];
                if (!float.IsNaN(value) && !float.IsInfinity(value) && value > max)
                {
                    max = value;
                    best = i;
                }
            }

            double frequency = best * frame.FrequencyResolution;
            Point point = new(
                plot.Left + frequency / Math.Max(0.000001, xMax) * plot.Width,
                plot.Bottom - Math.Clamp(max / Math.Max(0.000001, yMax), 0, 1) * plot.Height);
            if (previous.HasValue)
            {
                context.DrawLine(SpectrumPen, previous.Value, point);
            }

            previous = point;
        }
    }

    private static void DrawPeakMarker(DrawingContext context, FftPeak peak, Rect plot, double xMax, double yMax)
    {
        if (peak.BinIndex < 0)
        {
            return;
        }

        double x = plot.Left + peak.FrequencyHz / Math.Max(0.000001, xMax) * plot.Width;
        double y = plot.Bottom - Math.Clamp(peak.Magnitude / Math.Max(0.000001, yMax), 0, 1) * plot.Height;
        context.DrawLine(PeakPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        DrawText(context, $"主峰 {peak.FrequencyHz:0.###} Hz", new Point(Math.Clamp(x + 6, plot.Left + 4, plot.Right - 92), Math.Max(plot.Top + 4, y - 18)), AxisFontSize, LabelBrush);
    }

    private void DrawTrendLine(DrawingContext context, FftChannelTrend trend, Rect plot, double xMin, double xMax, double yMin, double yMax)
    {
        int count = trend.Points.Count;
        int target = Math.Min(count, Math.Max(1, (int)plot.Width * 2));
        Point? previous = null;
        for (int bucket = 0; bucket < target; bucket++)
        {
            int index = target == count ? bucket : bucket * count / target;
            FftTrendPoint item = trend.Points[Math.Clamp(index, 0, count - 1)];
            double value = TrendMetric == FftTrendMetric.PeakFrequency ? item.PeakFrequencyHz : item.PeakMagnitude;
            Point point = new(
                plot.Left + (item.TimeSeconds - xMin) / Math.Max(0.000001, xMax - xMin) * plot.Width,
                plot.Bottom - (value - yMin) / Math.Max(0.000001, yMax - yMin) * plot.Height);
            if (previous.HasValue)
            {
                context.DrawLine(TrendPen, previous.Value, point);
            }

            previous = point;
        }
    }

    private static double FindMagnitudeMax(IReadOnlyList<float> values)
    {
        double max = 0;
        foreach (float value in values)
        {
            if (!float.IsNaN(value) && !float.IsInfinity(value) && value > max)
            {
                max = value;
            }
        }

        return max;
    }

    private static (double Min, double Max) FindTrendRange(FftChannelTrend trend, FftTrendMetric metric)
    {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        foreach (FftTrendPoint point in trend.Points)
        {
            double value = metric == FftTrendMetric.PeakFrequency ? point.PeakFrequencyHz : point.PeakMagnitude;
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                continue;
            }

            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        if (double.IsInfinity(min) || double.IsInfinity(max))
        {
            return (0, 1);
        }

        return (min, max);
    }

    private static (double Min, double Max) NiceBounds(double min, double max)
    {
        if (Math.Abs(max - min) < 0.000001)
        {
            double pad = Math.Max(1, Math.Abs(max) * 0.1);
            return (min - pad, max + pad);
        }

        double padding = (max - min) * 0.08;
        double niceMin = Math.Floor((min - padding) / NiceNumber((max - min) / 5, round: true)) * NiceNumber((max - min) / 5, round: true);
        double niceMax = Math.Ceiling((max + padding) / NiceNumber((max - min) / 5, round: true)) * NiceNumber((max - min) / 5, round: true);
        return (niceMin, niceMax);
    }

    private static double NiceCeiling(double value)
    {
        double step = NiceNumber(value / 5, round: true);
        return Math.Max(step, Math.Ceiling(value / step) * step);
    }

    private static double NiceNumber(double value, bool round)
    {
        if (value <= 0 || double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1;
        }

        double exponent = Math.Floor(Math.Log10(value));
        double fraction = value / Math.Pow(10, exponent);
        double niceFraction = round
            ? fraction < 1.5 ? 1 : fraction < 3 ? 2 : fraction < 7 ? 5 : 10
            : fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
        return niceFraction * Math.Pow(10, exponent);
    }

    private static string FormatAxisValue(double value, double step)
    {
        if (Math.Abs(value) >= 10000)
        {
            return value.ToString("0.#e0", CultureInfo.InvariantCulture);
        }

        if (Math.Abs(step) < 0.01)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        if (Math.Abs(step) < 1)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        return value.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static string FormatChannelDisplayName(FftChannelOverview channel)
    {
        return string.IsNullOrWhiteSpace(channel.ChannelName)
            ? $"设备 {channel.Key.DeviceId + 1}/通道 {channel.Key.ChannelId}"
            : $"设备 {channel.Key.DeviceId + 1}/{channel.ChannelName}";
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
