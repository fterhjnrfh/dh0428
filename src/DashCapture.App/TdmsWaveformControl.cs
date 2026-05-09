using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DashCapture.Storage;

namespace DashCapture.App;

public sealed class TdmsWaveformControl : Control
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(242, 246, 251));
    private static readonly IBrush PlotBrush = Brushes.White;
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.FromRgb(64, 76, 96));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(24, 35, 52));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
    private static readonly IBrush ProbeBrush = new SolidColorBrush(Color.FromRgb(18, 24, 33));
    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.FromArgb(46, 38, 119, 220));
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromRgb(216, 226, 238)), 1);
    private static readonly Pen MinorGridPen = new(new SolidColorBrush(Color.FromRgb(232, 238, 246)), 1);
    private static readonly Pen AxisPen = new(AxisBrush, 1.2);
    private static readonly Pen ProbePen = new(new SolidColorBrush(Color.FromRgb(18, 24, 33)), 1);
    private static readonly Pen SelectionPen = new(new SolidColorBrush(Color.FromRgb(38, 119, 220)), 1);
    private static readonly Color[] Palette =
    {
        Color.FromRgb(24, 118, 210),
        Color.FromRgb(33, 150, 83),
        Color.FromRgb(230, 149, 0),
        Color.FromRgb(210, 69, 69),
        Color.FromRgb(123, 88, 210),
        Color.FromRgb(0, 143, 156),
        Color.FromRgb(214, 108, 32),
        Color.FromRgb(116, 132, 20),
        Color.FromRgb(14, 116, 144),
        Color.FromRgb(190, 24, 93)
    };

    private Rect _lastPlot;
    private double _lastYMin = -1;
    private double _lastYMax = 1;
    private Point _dragStart;
    private Point _dragCurrent;
    private double _dragRangeStart;
    private double _dragRangeEnd;
    private bool _panRangeChanged;
    private bool _isSelecting;
    private bool _isPanning;
    private ProbePoint? _probe;
    private long _lastPreviewInvalidateMs;

    public TdmsWaveformControl()
    {
        Focusable = true;
    }

    public event Action<double, double>? ViewRangeRequested;
    public event Action<string>? ProbeChanged;

    public IReadOnlyList<TdmsChannelEnvelope> Series { get; private set; } = Array.Empty<TdmsChannelEnvelope>();
    public double StartSeconds { get; private set; }
    public double EndSeconds { get; private set; }
    public double? FixedYMin { get; set; }
    public double? FixedYMax { get; set; }

    public void SetSeries(IReadOnlyList<TdmsChannelEnvelope> series, double startSeconds, double endSeconds)
    {
        Series = series;
        StartSeconds = startSeconds;
        EndSeconds = Math.Max(startSeconds + 0.000001, endSeconds);
        _probe = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = Bounds;
        context.FillRectangle(BackgroundBrush, bounds);

        Rect plot = CreatePlotRect(bounds);
        _lastPlot = plot;
        context.FillRectangle(PlotBrush, plot);

        if (Series.Count == 0 || Series.All(series => series.Points.Count == 0))
        {
            _lastYMin = -1;
            _lastYMax = 1;
            DrawAxes(context, plot, -1, 1);
            DrawText(context, "No TDMS data loaded", new Point(plot.Left + 16, plot.Top + 16), 15, MutedBrush);
            return;
        }

        (float rawMin, float rawMax) = FindRange(Series);
        double yMin = FixedYMin ?? rawMin;
        double yMax = FixedYMax ?? rawMax;
        if (Math.Abs(yMax - yMin) < 0.000001)
        {
            yMin -= 1;
            yMax += 1;
        }

        (float niceMin, float niceMax) = NiceBounds((float)yMin, (float)yMax);
        if (FixedYMin.HasValue || FixedYMax.HasValue)
        {
            niceMin = (float)yMin;
            niceMax = (float)yMax;
        }

        _lastYMin = niceMin;
        _lastYMax = niceMax;
        DrawAxes(context, plot, niceMin, niceMax);

        using (context.PushClip(plot))
        {
            int index = 0;
            foreach (TdmsChannelEnvelope series in Series)
            {
                if (series.Points.Count == 0)
                {
                    continue;
                }

                var pen = new Pen(new SolidColorBrush(Palette[index % Palette.Length]), 1.25);
                DrawEnvelope(context, series.Points, series.SampleCount, pen, plot, niceMin, niceMax);
                index++;
            }
        }

        DrawLegend(context, plot);
        DrawProbe(context, plot);
        DrawSelection(context, plot);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        Point point = e.GetPosition(this);
        if (!_lastPlot.Contains(point))
        {
            return;
        }

        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        _dragStart = point;
        _dragCurrent = point;
        _dragRangeStart = StartSeconds;
        _dragRangeEnd = EndSeconds;
        _panRangeChanged = false;

        if (properties.IsRightButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _isPanning = true;
        }
        else if (properties.IsLeftButtonPressed)
        {
            _isSelecting = true;
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point point = e.GetPosition(this);
        if (_isPanning)
        {
            _dragCurrent = point;
            double range = Math.Max(0.000001, _dragRangeEnd - _dragRangeStart);
            double dx = point.X - _dragStart.X;
            double shift = -dx / Math.Max(1, _lastPlot.Width) * range;
            PreviewRange(_dragRangeStart + shift, _dragRangeEnd + shift);
            _panRangeChanged = true;
            e.Handled = true;
            return;
        }

        if (_isSelecting)
        {
            _dragCurrent = point;
            InvalidatePreview();
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        Point point = e.GetPosition(this);
        double moved = Math.Abs(point.X - _dragStart.X) + Math.Abs(point.Y - _dragStart.Y);

        if (_isSelecting)
        {
            _isSelecting = false;
            _dragCurrent = point;
            if (Math.Abs(point.X - _dragStart.X) >= 8)
            {
                double start = PixelToTime(Math.Min(_dragStart.X, point.X));
                double end = PixelToTime(Math.Max(_dragStart.X, point.X));
                RequestRange(start, end);
            }
            else if (moved < 8)
            {
                UpdateProbe(point);
            }
        }

        if (_isPanning && _panRangeChanged)
        {
            RequestRange(StartSeconds, EndSeconds);
        }

        _isPanning = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Point point = e.GetPosition(this);
        if (!_lastPlot.Contains(point))
        {
            return;
        }

        double factor = e.Delta.Y > 0 ? 0.75 : 1.35;
        double anchor = PixelToTime(point.X);
        double start = anchor - (anchor - StartSeconds) * factor;
        double end = anchor + (EndSeconds - anchor) * factor;
        RequestRange(start, end);
        e.Handled = true;
    }

    private void RequestRange(double start, double end)
    {
        if (double.IsNaN(start) || double.IsNaN(end) || Math.Abs(end - start) < 0.000001)
        {
            return;
        }

        if (start > end)
        {
            (start, end) = (end, start);
        }

        ViewRangeRequested?.Invoke(start, end);
    }

    private void PreviewRange(double start, double end)
    {
        if (double.IsNaN(start) || double.IsNaN(end) || Math.Abs(end - start) < 0.000001)
        {
            return;
        }

        if (start > end)
        {
            (start, end) = (end, start);
        }

        StartSeconds = start;
        EndSeconds = end;
        InvalidatePreview();
    }

    private void InvalidatePreview()
    {
        long now = Environment.TickCount64;
        if (now - _lastPreviewInvalidateMs < 16)
        {
            return;
        }

        _lastPreviewInvalidateMs = now;
        InvalidateVisual();
    }

    private void UpdateProbe(Point point)
    {
        if (!_lastPlot.Contains(point) || Series.Count == 0)
        {
            return;
        }

        double bestDistance = double.MaxValue;
        ProbePoint? best = null;
        double yRange = Math.Max(0.000001, _lastYMax - _lastYMin);

        foreach (TdmsChannelEnvelope series in Series)
        {
            if (series.Points.Count == 0)
            {
                continue;
            }

            int bucket = (int)Math.Clamp((point.X - _lastPlot.Left) / Math.Max(1, _lastPlot.Width) * series.Points.Count, 0, series.Points.Count - 1);
            TdmsEnvelopePoint envelope = series.Points[bucket];
            double x = _lastPlot.Left + (bucket + 0.5) * _lastPlot.Width / series.Points.Count;
            foreach (float value in new[] { envelope.Last, envelope.Maximum, envelope.Minimum, envelope.First })
            {
                double y = _lastPlot.Top + _lastPlot.Height - ((value - _lastYMin) / yRange) * _lastPlot.Height;
                double distance = Math.Abs(point.X - x) + Math.Abs(point.Y - y);
                if (distance < bestDistance)
                {
                    double seconds = StartSeconds + (bucket + 0.5) / series.Points.Count * (EndSeconds - StartSeconds);
                    bestDistance = distance;
                    best = new ProbePoint(series.Channel.DisplayName, seconds, value, new Point(x, y));
                }
            }
        }

        _probe = best;
        if (best is not null)
        {
            ProbePoint probe = best.Value;
            ProbeChanged?.Invoke($"{probe.Channel}    t={FormatTimePrecise(probe.Seconds)} s    y={FormatAxisValue(probe.Value)}");
        }

        InvalidateVisual();
    }

    private double PixelToTime(double x)
    {
        double ratio = Math.Clamp((x - _lastPlot.Left) / Math.Max(1, _lastPlot.Width), 0, 1);
        return StartSeconds + ratio * (EndSeconds - StartSeconds);
    }

    private static Rect CreatePlotRect(Rect bounds)
    {
        const double left = 82;
        const double top = 22;
        const double right = 28;
        const double bottom = 58;
        return new Rect(
            bounds.Left + left,
            bounds.Top + top,
            Math.Max(1, bounds.Width - left - right),
            Math.Max(1, bounds.Height - top - bottom));
    }

    private void DrawAxes(DrawingContext context, Rect plot, float yMin, float yMax)
    {
        DrawAxisGrid(context, plot, yMin, yMax, vertical: false);
        DrawAxisGrid(context, plot, StartSeconds, Math.Max(StartSeconds + 0.000001, EndSeconds), vertical: true);

        context.DrawLine(AxisPen, plot.BottomLeft, plot.BottomRight);
        context.DrawLine(AxisPen, plot.BottomLeft, plot.TopLeft);
        DrawText(context, "Value", new Point(plot.Left - 66, plot.Top - 4), 13, LabelBrush);
        DrawText(context, "Time (s)", new Point(plot.Right - 60, plot.Bottom + 34), 13, LabelBrush);
    }

    private static void DrawAxisGrid(DrawingContext context, Rect plot, double min, double max, bool vertical)
    {
        double range = Math.Max(0.000001, max - min);
        double targetPixels = vertical ? 100 : 58;
        int targetTicks = (int)Math.Clamp((vertical ? plot.Width : plot.Height) / targetPixels, 4, 14);
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
                DrawText(context, FormatTime(value), new Point(x - 22, plot.Bottom + 10), 12, AxisBrush);
            }
            else
            {
                double y = plot.Bottom - ratio * plot.Height;
                context.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                DrawText(context, FormatAxisValue(value), new Point(plot.Left - 70, y - 9), 12, AxisBrush);
            }
        }
    }

    private static void DrawEnvelope(
        DrawingContext context,
        IReadOnlyList<TdmsEnvelopePoint> envelope,
        ulong sampleCount,
        Pen pen,
        Rect plot,
        float min,
        float max)
    {
        double range = max - min;
        bool drawConnectingLine = sampleCount <= (ulong)Math.Max(1, envelope.Count) * 8UL;
        Point? previousLast = null;
        for (int i = 0; i < envelope.Count; i++)
        {
            TdmsEnvelopePoint point = envelope[i];
            double bucketStart = plot.Left + i * plot.Width / envelope.Count;
            double bucketEnd = plot.Left + (i + 1) * plot.Width / envelope.Count;
            double x = (bucketStart + bucketEnd) * 0.5;
            double yMin = plot.Top + plot.Height - ((point.Minimum - min) / range) * plot.Height;
            double yMax = plot.Top + plot.Height - ((point.Maximum - min) / range) * plot.Height;

            context.DrawLine(pen, new Point(x, yMin), new Point(x, yMax));

            if (!drawConnectingLine)
            {
                continue;
            }

            double yFirst = plot.Top + plot.Height - ((point.First - min) / range) * plot.Height;
            double yLast = plot.Top + plot.Height - ((point.Last - min) / range) * plot.Height;
            var currentFirst = new Point(bucketStart, yFirst);
            var currentLast = new Point(bucketEnd, yLast);
            if (previousLast is not null)
            {
                context.DrawLine(pen, previousLast.Value, currentFirst);
            }

            context.DrawLine(pen, currentFirst, currentLast);
            previousLast = currentLast;
        }
    }

    private void DrawLegend(DrawingContext context, Rect plot)
    {
        double x = plot.Left + 10;
        double y = plot.Top + 10;
        int index = 0;
        foreach (TdmsChannelEnvelope series in Series.Take(12))
        {
            IBrush brush = new SolidColorBrush(Palette[index % Palette.Length]);
            context.FillRectangle(brush, new Rect(x, y + 4, 18, 3));
            DrawText(context, series.Channel.DisplayName, new Point(x + 24, y - 3), 12, LabelBrush);
            y += 19;
            index++;
        }

        if (Series.Count > 12)
        {
            DrawText(context, $"+{Series.Count - 12}", new Point(x + 24, y - 3), 12, MutedBrush);
        }
    }

    private void DrawProbe(DrawingContext context, Rect plot)
    {
        if (_probe is null)
        {
            return;
        }

        ProbePoint probe = _probe.Value;
        context.DrawLine(ProbePen, new Point(probe.Position.X, plot.Top), new Point(probe.Position.X, plot.Bottom));
        context.DrawLine(ProbePen, new Point(plot.Left, probe.Position.Y), new Point(plot.Right, probe.Position.Y));
        context.DrawEllipse(ProbeBrush, ProbePen, new Rect(probe.Position.X - 4, probe.Position.Y - 4, 8, 8));

        string label = $"t={FormatTimePrecise(probe.Seconds)}s  y={FormatAxisValue(probe.Value)}";
        var size = MeasureText(label, 12);
        double x = Math.Min(plot.Right - size.Width - 18, probe.Position.X + 12);
        double y = Math.Max(plot.Top + 8, probe.Position.Y - 28);
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), new Rect(x - 6, y - 4, size.Width + 12, size.Height + 8));
        DrawText(context, label, new Point(x, y), 12, LabelBrush);
    }

    private void DrawSelection(DrawingContext context, Rect plot)
    {
        if (!_isSelecting)
        {
            return;
        }

        double left = Math.Clamp(Math.Min(_dragStart.X, _dragCurrent.X), plot.Left, plot.Right);
        double right = Math.Clamp(Math.Max(_dragStart.X, _dragCurrent.X), plot.Left, plot.Right);
        var rect = new Rect(left, plot.Top, Math.Max(1, right - left), plot.Height);
        context.FillRectangle(SelectionBrush, rect);
        context.DrawRectangle(SelectionPen, rect);
    }

    private static (float Min, float Max) FindRange(IReadOnlyList<TdmsChannelEnvelope> series)
    {
        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (TdmsChannelEnvelope item in series)
        {
            foreach (TdmsEnvelopePoint point in item.Points)
            {
                if (point.Minimum < min) min = point.Minimum;
                if (point.Maximum > max) max = point.Maximum;
            }
        }

        if (min == float.MaxValue || max == float.MinValue)
        {
            return (-1, 1);
        }

        return Math.Abs(max - min) < 0.000001f ? (min - 1, max + 1) : (min, max);
    }

    private static (float Min, float Max) NiceBounds(float min, float max)
    {
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
        if (abs >= 1000 || abs < 0.01 && abs > 0)
        {
            return value.ToString("0.###E+0", CultureInfo.InvariantCulture);
        }

        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string FormatTime(double seconds)
    {
        if (seconds >= 3600)
        {
            return (seconds / 3600).ToString("0.###h", CultureInfo.InvariantCulture);
        }

        if (seconds >= 60)
        {
            return (seconds / 60).ToString("0.###m", CultureInfo.InvariantCulture);
        }

        return seconds.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string FormatTimePrecise(double seconds)
    {
        return seconds.ToString(seconds < 1 ? "0.######" : "0.#####", CultureInfo.InvariantCulture);
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

    private readonly record struct ProbePoint(string Channel, double Seconds, double Value, Point Position);
}
