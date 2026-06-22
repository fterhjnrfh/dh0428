using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DashCapture.Core.Models;
using DashCapture.Display;
using System.Globalization;

namespace DashCapture.App;

public sealed class WaveformControl : Control
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(232, 236, 241));
    private static readonly IBrush PlotBrush = new SolidColorBrush(Color.FromRgb(250, 252, 255));
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.FromRgb(72, 82, 96));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(20, 29, 39));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(101, 111, 126));
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromRgb(214, 221, 231)), 1);
    private static readonly Pen MinorGridPen = new(new SolidColorBrush(Color.FromRgb(236, 240, 245)), 1);
    private static readonly Pen AxisPen = new(AxisBrush, 1.2);
    private static readonly Pen ZeroLinePen = new(new SolidColorBrush(Color.FromRgb(116, 130, 149)), 1.4);
    private static readonly Pen PlotBorderPen = new(new SolidColorBrush(Color.FromRgb(184, 195, 209)), 1);
    private static readonly Pen PlotInnerBorderPen = new(new SolidColorBrush(Color.FromArgb(96, 255, 255, 255)), 1);
    private const double AxisFontSize = 11;
    private const double AxisTitleFontSize = 12;
    private const double LegendFontSize = 11;
    private const double MinimumPlotWidth = 64;
    private const double MinimumPlotHeight = 48;
    private static readonly Color[] Palette =
    {
        Color.FromRgb(18, 94, 172),
        Color.FromRgb(0, 126, 98),
        Color.FromRgb(190, 95, 24),
        Color.FromRgb(181, 57, 66),
        Color.FromRgb(105, 78, 190),
        Color.FromRgb(0, 132, 152),
        Color.FromRgb(154, 93, 31),
        Color.FromRgb(84, 112, 35)
    };

    private readonly Dictionary<ChannelKey, SeriesPens> _seriesPens = new();
    private EnvelopePoint[] _downsampleBuffer = Array.Empty<EnvelopePoint>();

    public WaveformStore? Store { get; set; }
    public IReadOnlyList<Core.Models.ChannelDescriptor>? Channels { get; set; }
    public double WindowSeconds { get; set; } = 5;
    public double DefaultYAxisAmplitude { get; set; }
    public WaveformRenderQuality RenderQuality { get; set; } = WaveformRenderQuality.Balanced;
    public double RenderBucketScale { get; set; } = 1.0;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = new(0, 0, Bounds.Width, Bounds.Height);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        context.FillRectangle(BackgroundBrush, bounds);

        double visibleSeconds = Math.Max(0.001, WindowSeconds);
        int requestedSeriesCount = Math.Max(1, Channels?.Count ?? 16);
        int initialBuckets = ResolveRenderBucketCount(Math.Max(1, bounds.Width), requestedSeriesCount, RenderQuality, RenderBucketScale);
        IReadOnlyList<WaveformSnapshot>? snapshot = Store?.SnapshotSeries(Channels, visibleSeconds, initialBuckets);
        if (snapshot is null || snapshot.Count == 0 || snapshot.All(series => series.Points.Length == 0))
        {
            double amplitude = GetDefaultYAxisAmplitude();
            double emptyMin = amplitude > 0 ? -amplitude : -1;
            double emptyMax = amplitude > 0 ? amplitude : 1;
            Rect emptyPlot = CreatePlotRect(bounds, emptyMin, emptyMax);
            DrawPlotSurface(context, emptyPlot);
            DrawAxes(
                context,
                bounds,
                emptyPlot,
                0,
                Math.Max(0.001, WindowSeconds),
                emptyMin,
                emptyMax,
                drawMinorGrid: true);
            DrawText(context, "\u6682\u65e0\u6ce2\u5f62\u6570\u636e", new Point(emptyPlot.Left + 14, emptyPlot.Top + 12), 14, MutedBrush);
            return;
        }

        int visibleSeriesCount = snapshot.Count(series => series.Points.Length > 0);
        bool lightweight = RenderQuality == WaveformRenderQuality.Lightweight || visibleSeriesCount >= 32;
        bool drawSignalHalo = RenderQuality == WaveformRenderQuality.Detailed && visibleSeriesCount <= 16;
        bool connectLastValues = RenderQuality != WaveformRenderQuality.Lightweight && visibleSeriesCount <= 24;
        (double xAxisStart, double sweepElapsedSeconds) = ResolveSweepAxis(snapshot, visibleSeconds);
        (double rawMin, double rawMax) = FindVisibleRange(snapshot, visibleSeconds);
        if (!IsFinite(rawMin) || !IsFinite(rawMax))
        {
            rawMin = -1;
            rawMax = 1;
        }

        (double yMin, double yMax) = NiceBoundsWithPadding(rawMin, rawMax, GetDefaultYAxisAmplitude());
        Rect plot = CreatePlotRect(bounds, yMin, yMax);
        DrawPlotSurface(context, plot);
        DrawAxes(context, bounds, plot, xAxisStart, xAxisStart + visibleSeconds, yMin, yMax, drawMinorGrid: !lightweight);

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
                int pointsPerSweep = PointsPerSweep(visibleSeconds, sampleRate);
                ReadOnlySpan<EnvelopePoint> renderSamples = series.SourcePointCount > 0
                    ? samples
                    : CurrentSweepSpan(samples, series.TotalPointCount, pointsPerSweep);
                if (renderSamples.Length == 0)
                {
                    continue;
                }

                int targetBuckets = Math.Min(
                    renderSamples.Length,
                    ResolveRenderBucketCount(width, visibleSeriesCount, RenderQuality, RenderBucketScale));
                ReadOnlySpan<EnvelopePoint> envelope = renderSamples;
                int envelopeCount = renderSamples.Length;
                if (renderSamples.Length > targetBuckets)
                {
                    EnsureDownsampleCapacity(targetBuckets);
                    envelopeCount = EnvelopeDownsampler.Downsample(
                        renderSamples,
                        targetBuckets,
                        _downsampleBuffer.AsSpan(0, targetBuckets));
                    envelope = _downsampleBuffer.AsSpan(0, envelopeCount);
                }

                int sourcePointCount = series.SourcePointCount > 0
                    ? series.SourcePointCount
                    : renderSamples.Length;
                double channelSeconds = Math.Min(
                    visibleSeconds,
                    Math.Max(0, sourcePointCount - 1) / sampleRate);
                double seriesSeconds = sweepElapsedSeconds > 0.000001
                    ? sweepElapsedSeconds
                    : channelSeconds;
                DrawEnvelope(
                    context,
                    envelope,
                    GetSeriesPen(series, channelIndex),
                    plot,
                    visibleSeconds,
                    seriesSeconds,
                    yMin,
                    yMax,
                    connectLastValues,
                    drawSignalHalo);
                channelIndex++;
            }
        }

        if (!lightweight)
        {
            DrawLegend(context, bounds, plot, snapshot);
        }
    }

    private static Rect CreatePlotRect(Rect bounds, double yMin, double yMax)
    {
        double top = bounds.Height >= 150 ? 22 : 18;
        double right = 8;
        double bottom = bounds.Height >= 150 ? 42 : 34;
        double plotHeight = Math.Max(MinimumPlotHeight, bounds.Height - top - bottom);
        double left = Math.Clamp(MeasureYAxisWidth(yMin, yMax, plotHeight) + 13, 38, 122);
        return new Rect(
            bounds.Left + left,
            bounds.Top + top,
            Math.Max(MinimumPlotWidth, bounds.Width - left - right),
            Math.Max(MinimumPlotHeight, bounds.Height - top - bottom));
    }

    private static void DrawPlotSurface(DrawingContext context, Rect plot)
    {
        context.FillRectangle(PlotBrush, plot);
        context.DrawRectangle(null, PlotBorderPen, plot, 3, 3);
        context.DrawRectangle(null, PlotInnerBorderPen, new Rect(plot.X + 1, plot.Y + 1, Math.Max(1, plot.Width - 2), Math.Max(1, plot.Height - 2)), 2, 2);
    }

    private void DrawAxes(DrawingContext context, Rect bounds, Rect plot, double xMin, double xMax, double yMin, double yMax, bool drawMinorGrid)
    {
        DrawAxisGrid(context, plot, yMin, yMax, vertical: false, drawMinorGrid: drawMinorGrid);
        DrawAxisGrid(context, plot, xMin, xMax, vertical: true, drawMinorGrid: drawMinorGrid);

        if (yMin < 0 && yMax > 0)
        {
            double zeroRatio = (0 - yMin) / Math.Max(0.000001, yMax - yMin);
            double zeroY = plot.Bottom - zeroRatio * plot.Height;
            context.DrawLine(ZeroLinePen, new Point(plot.Left, zeroY), new Point(plot.Right, zeroY));
        }

        context.DrawLine(AxisPen, plot.BottomLeft, plot.BottomRight);
        context.DrawLine(AxisPen, plot.BottomLeft, plot.TopLeft);
        DrawAxisTitles(context, bounds, plot);
    }

    private static void DrawAxisGrid(DrawingContext context, Rect plot, double min, double max, bool vertical, bool drawMinorGrid)
    {
        double range = Math.Max(0.000001, max - min);
        double targetPixels = vertical ? 96 : 52;
        int targetTicks = (int)Math.Clamp((vertical ? plot.Width : plot.Height) / targetPixels, 3, 10);
        double majorStep = NiceNumber(range / targetTicks, round: true);
        double minorStep = majorStep / 2;

        if (drawMinorGrid)
        {
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
        }

        double majorStart = Math.Ceiling(min / majorStep) * majorStep;
        double lastLabelEnd = double.NegativeInfinity;
        var yLabels = new List<AxisLabel>();
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
                string label = FormatTime(value);
                Size size = MeasureText(label, AxisFontSize);
                double labelX = Clamp(x - size.Width / 2, plot.Left, plot.Right - size.Width);
                if (labelX >= lastLabelEnd + 8)
                {
                    DrawText(context, label, new Point(labelX, plot.Bottom + 10), AxisFontSize, AxisBrush);
                    lastLabelEnd = labelX + size.Width;
                }
            }
            else
            {
                double y = plot.Bottom - ratio * plot.Height;
                context.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                string label = FormatAxisValue(value, majorStep);
                Size size = MeasureText(label, AxisFontSize);
                double labelY = Clamp(y - size.Height / 2, plot.Top, plot.Bottom - size.Height);
                yLabels.Add(new AxisLabel(label, plot.Left - size.Width - 6, labelY, size.Height));
            }
        }

        if (!vertical)
        {
            DrawYAxisLabels(context, yLabels);
        }
    }

    private static void DrawEnvelope(
        DrawingContext context,
        ReadOnlySpan<EnvelopePoint> envelope,
        SeriesPens pens,
        Rect plot,
        double visibleSeconds,
        double seriesSeconds,
        double min,
        double max,
        bool connectLastValues,
        bool drawSignalHalo)
    {
        if (envelope.Length == 0)
        {
            return;
        }

        double range = Math.Max(0.000001, max - min);
        double xRange = Math.Max(0.000001, visibleSeconds);
        Point? previous = null;
        for (int i = 0; i < envelope.Length; i++)
        {
            EnvelopePoint point = envelope[i];
            if (!IsFinite(point.Minimum) || !IsFinite(point.Maximum) || !IsFinite(point.Last))
            {
                previous = null;
                continue;
            }

            double seconds = envelope.Length == 1 ? 0 : i * seriesSeconds / (envelope.Length - 1);
            double x = plot.Left + seconds / xRange * plot.Width;
            double yMin = plot.Top + plot.Height - ((point.Minimum - min) / range) * plot.Height;
            double yMax = plot.Top + plot.Height - ((point.Maximum - min) / range) * plot.Height;
            double yLast = plot.Top + plot.Height - ((point.Last - min) / range) * plot.Height;
            if (!IsFinite(x) || !IsFinite(yMin) || !IsFinite(yMax) || !IsFinite(yLast) ||
                x < plot.Left - 0.5 || x > plot.Right + 0.5)
            {
                previous = null;
                continue;
            }

            x = Clamp(x, plot.Left, plot.Right);
            yMin = Clamp(yMin, plot.Top, plot.Bottom);
            yMax = Clamp(yMax, plot.Top, plot.Bottom);
            yLast = Clamp(yLast, plot.Top, plot.Bottom);
            DrawSignalLine(context, pens, new Point(x, yMin), new Point(x, yMax), drawSignalHalo);

            var current = new Point(x, yLast);
            if (connectLastValues && previous is not null && current.X >= previous.Value.X)
            {
                DrawSignalLine(context, pens, previous.Value, current, drawSignalHalo);
            }

            previous = connectLastValues ? current : null;
        }
    }

    private static (double Min, double Max) FindVisibleRange(IReadOnlyList<WaveformSnapshot> snapshot, double visibleSeconds)
    {
        double globalMin = double.PositiveInfinity;
        double globalMax = double.NegativeInfinity;
        foreach (WaveformSnapshot series in snapshot)
        {
            EnvelopePoint[] data = series.Points;
            int pointsPerSweep = PointsPerSweep(visibleSeconds, GetSampleRate(series.DisplaySampleRate));
            ReadOnlySpan<EnvelopePoint> visibleData = series.SourcePointCount > 0
                ? data
                : CurrentSweepSpan(data, series.TotalPointCount, pointsPerSweep);
            for (int i = 0; i < visibleData.Length; i++)
            {
                EnvelopePoint point = visibleData[i];
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

    private static (double StartSeconds, double ElapsedSeconds) ResolveSweepAxis(
        IReadOnlyList<WaveformSnapshot> snapshot,
        double visibleSeconds)
    {
        double latestElapsedSeconds = 0;
        foreach (WaveformSnapshot series in snapshot)
        {
            long total = series.TotalPointCount > 0 ? series.TotalPointCount : series.Points.Length;
            if (total <= 0)
            {
                continue;
            }

            double sampleRate = GetSampleRate(series.DisplaySampleRate);
            latestElapsedSeconds = Math.Max(latestElapsedSeconds, Math.Max(0, total - 1) / sampleRate);
        }

        double window = Math.Max(0.001, visibleSeconds);
        double sweepIndex = Math.Floor(latestElapsedSeconds / window);
        double start = sweepIndex * window;
        double elapsed = Math.Clamp(latestElapsedSeconds - start, 0, window);
        if (elapsed <= 0.000001 && latestElapsedSeconds > 0)
        {
            elapsed = window;
        }

        return (start, elapsed);
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

    private static int PointsPerSweep(double visibleSeconds, double sampleRate)
    {
        return Math.Max(1, (int)Math.Ceiling(Math.Max(0.001, visibleSeconds) * Math.Max(1, sampleRate)));
    }

    private static int ResolveRenderBucketCount(
        double width,
        int visibleSeriesCount,
        WaveformRenderQuality quality,
        double bucketScale)
    {
        int pixelBuckets = (int)Math.Clamp(Math.Ceiling(width), 64, 1800);
        double scale = IsFinite(bucketScale) ? Math.Clamp(bucketScale, 0.25, 2.0) : 1.0;
        int baseBuckets;
        if (quality == WaveformRenderQuality.Detailed && visibleSeriesCount <= 8)
        {
            baseBuckets = pixelBuckets;
            return ScaleBucketCount(baseBuckets, pixelBuckets, scale);
        }

        if (quality == WaveformRenderQuality.Balanced)
        {
            int cap = visibleSeriesCount >= 24 ? 480 : visibleSeriesCount >= 12 ? 720 : 1200;
            baseBuckets = Math.Min(pixelBuckets, cap);
            return ScaleBucketCount(baseBuckets, pixelBuckets, scale);
        }

        int lightweightCap = visibleSeriesCount >= 56 ? 220 :
            visibleSeriesCount >= 40 ? 280 :
            visibleSeriesCount >= 24 ? 360 :
            visibleSeriesCount >= 12 ? 520 :
            760;
        baseBuckets = Math.Min(pixelBuckets, lightweightCap);
        return ScaleBucketCount(baseBuckets, pixelBuckets, scale);
    }

    private static int ScaleBucketCount(int baseBuckets, int pixelBuckets, double scale)
    {
        int scaled = (int)Math.Round(baseBuckets * scale);
        return Math.Clamp(scaled, 48, pixelBuckets);
    }

    private static ReadOnlySpan<EnvelopePoint> CurrentSweepSpan(
        EnvelopePoint[] samples,
        long totalPointCount,
        int pointsPerSweep)
    {
        if (samples.Length == 0)
        {
            return ReadOnlySpan<EnvelopePoint>.Empty;
        }

        long total = totalPointCount > 0 ? totalPointCount : samples.Length;
        int sweepCount;
        if (total < pointsPerSweep)
        {
            sweepCount = (int)Math.Min(total, samples.Length);
        }
        else
        {
            int phase = (int)(total % pointsPerSweep);
            sweepCount = phase == 0 ? pointsPerSweep : phase;
            sweepCount = Math.Min(sweepCount, samples.Length);
        }

        if (sweepCount <= 0)
        {
            return ReadOnlySpan<EnvelopePoint>.Empty;
        }

        return samples.AsSpan(samples.Length - sweepCount, sweepCount);
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
        int targetTicks = (int)Math.Clamp(Math.Max(1, height - 66) / 52, 3, 10);
        double majorStep = NiceNumber(range / targetTicks, round: true);
        double majorStart = Math.Ceiling(min / majorStep) * majorStep;
        double width = MeasureText(FormatAxisValue(min, majorStep), AxisFontSize).Width;
        width = Math.Max(width, MeasureText(FormatAxisValue(max, majorStep), AxisFontSize).Width);
        for (double value = majorStart; value <= max + majorStep * 0.5; value += majorStep)
        {
            width = Math.Max(width, MeasureText(FormatAxisValue(value, majorStep), AxisFontSize).Width);
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

    private void EnsureDownsampleCapacity(int capacity)
    {
        if (_downsampleBuffer.Length >= capacity)
        {
            return;
        }

        int next = Math.Max(64, _downsampleBuffer.Length);
        while (next < capacity)
        {
            next *= 2;
        }

        _downsampleBuffer = new EnvelopePoint[next];
    }

    private SeriesPens GetSeriesPen(WaveformSnapshot series, int fallbackIndex)
    {
        var key = new ChannelKey(series.Channel);
        if (_seriesPens.TryGetValue(key, out SeriesPens pens))
        {
            return pens;
        }

        Color color = SeriesColor(series, fallbackIndex);
        pens = new SeriesPens(
            new Pen(new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)), 3.6),
            new Pen(new SolidColorBrush(color), 1.65));
        _seriesPens[key] = pens;
        return pens;
    }

    private static void DrawLegend(DrawingContext context, Rect bounds, Rect plot, IReadOnlyList<WaveformSnapshot> snapshot)
    {
        WaveformSnapshot[] items = snapshot
            .Where(series => series.Points.Length > 0)
            .ToArray();
        if (items.Length == 0)
        {
            return;
        }

        double top = bounds.Top + 4;
        double x = plot.Left + 6;
        double right = plot.Right;
        double rowCenterY = top + 8;
        int hidden = 0;
        for (int i = 0; i < items.Length; i++)
        {
            WaveformSnapshot series = items[i];
            string label = TrimText(SeriesLegendText(series), 18);
            Size labelSize = MeasureText(label, LegendFontSize);
            double itemWidth = 28 + labelSize.Width;
            if (x + itemWidth > right - 42)
            {
                hidden = items.Length - i;
                break;
            }

            var legendPen = new Pen(new SolidColorBrush(SeriesColor(series, i)), 2.4);
            context.DrawLine(legendPen, new Point(x, rowCenterY), new Point(x + 16, rowCenterY));
            DrawText(context, label, new Point(x + 22, top), LegendFontSize, LabelBrush);
            x += itemWidth + 16;
        }

        if (hidden > 0)
        {
            string text = $"+{hidden}";
            Size size = MeasureText(text, LegendFontSize);
            DrawText(context, text, new Point(right - size.Width, top), LegendFontSize, MutedBrush);
        }
    }

    private static string TrimText(string text, int maxLength)
    {
        return text.Length <= maxLength
            ? text
            : text[..Math.Max(1, maxLength - 1)] + "\u2026";
    }

    private static void DrawAxisTitles(DrawingContext context, Rect bounds, Rect plot)
    {
        if (plot.Top - bounds.Top >= 14)
        {
            DrawText(context, "\u5E45\u503C", new Point(bounds.Left + 3, bounds.Top + 4), AxisTitleFontSize, LabelBrush);
        }
    }

    private static void DrawYAxisLabels(DrawingContext context, List<AxisLabel> labels)
    {
        labels.Sort(static (left, right) => left.Y.CompareTo(right.Y));
        double lastBottom = double.NegativeInfinity;
        foreach (AxisLabel label in labels)
        {
            if (label.Y < lastBottom + 2)
            {
                continue;
            }

            DrawText(context, label.Text, new Point(label.X, label.Y), AxisFontSize, AxisBrush);
            lastBottom = label.Y + label.Height;
        }
    }

    private static void DrawSignalLine(DrawingContext context, SeriesPens pens, Point start, Point end, bool drawHalo)
    {
        if (drawHalo)
        {
            context.DrawLine(pens.Halo, start, end);
        }

        context.DrawLine(pens.Stroke, start, end);
    }

    private static string SeriesLegendText(WaveformSnapshot series)
    {
        string name = string.IsNullOrWhiteSpace(series.Channel.Name)
            ? $"\u901A\u9053 {series.Channel.ChannelId + 1}"
            : series.Channel.Name;
        return name;
    }

    private static double Clamp(double value, double min, double max)
    {
        return max <= min ? min : Math.Clamp(value, min, max);
    }

    private readonly record struct SeriesPens(Pen Halo, Pen Stroke);

    private readonly record struct AxisLabel(string Text, double X, double Y, double Height);
}

public enum WaveformRenderQuality
{
    Detailed,
    Balanced,
    Lightweight
}
