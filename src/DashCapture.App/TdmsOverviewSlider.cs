using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DashCapture.Storage;

namespace DashCapture.App;

public sealed class TdmsOverviewSlider : Control
{
    private const double HeatStripHeight = 9;
    private const double EnvelopeBottomGap = 4;
    private const double MinimumSeriesRange = 0.000001;

    private static readonly IBrush TrackBrush = new SolidColorBrush(Color.FromRgb(248, 250, 252));
    private static readonly IBrush EmptyBrush = new SolidColorBrush(Color.FromRgb(174, 184, 197));
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromArgb(28, 20, 29, 39));
    private static readonly IBrush WindowBrush = new SolidColorBrush(Color.FromArgb(30, 31, 91, 140));
    private static readonly IBrush HandleBrush = new SolidColorBrush(Color.FromArgb(190, 31, 91, 140));
    private static readonly Pen BorderPen = new(new SolidColorBrush(Color.FromRgb(190, 198, 208)), 1);
    private static readonly Pen OverviewPen = new(new SolidColorBrush(Color.FromArgb(95, 31, 91, 140)), 1);
    private static readonly Pen OverviewOutlinePen = new(new SolidColorBrush(Color.FromArgb(230, 31, 91, 140)), 1.2);
    private static readonly Pen CenterTrendPen = new(new SolidColorBrush(Color.FromArgb(210, 35, 117, 80)), 1);
    private static readonly Pen WindowPen = new(new SolidColorBrush(Color.FromRgb(31, 91, 140)), 1.2);
    private static readonly Pen CenterPen = new(new SolidColorBrush(Color.FromArgb(80, 82, 92, 106)), 1);
    private static readonly IBrush HeatEmptyBrush = new SolidColorBrush(Color.FromArgb(60, 174, 184, 197));
    private static readonly IBrush[] HeatBrushes = CreateHeatBrushes();

    private IReadOnlyList<TdmsChannelEnvelope> _overview = Array.Empty<TdmsChannelEnvelope>();
    private OverviewBin[] _overviewBins = Array.Empty<OverviewBin>();
    private Rect _lastTrack;
    private Point _dragStartPoint;
    private double _dragStartSeconds;
    private double _dragEndSeconds;
    private double _overviewMin = -1;
    private double _overviewMax = 1;
    private bool _hasOverviewPoints;
    private DragMode _dragMode;

    public TdmsOverviewSlider()
    {
        Focusable = true;
        Height = 76;
        MinHeight = 66;
    }

    public event Action<double, double>? RangeRequested;

    public double DurationSeconds { get; private set; } = 1;
    public double ViewStartSeconds { get; private set; }
    public double ViewEndSeconds { get; private set; } = 1;

    public void SetOverview(IReadOnlyList<TdmsChannelEnvelope> overview, double durationSeconds)
    {
        _overview = overview;
        _overviewBins = Array.Empty<OverviewBin>();
        _hasOverviewPoints = overview.Any(series => series.Points.Count > 0);
        DurationSeconds = Math.Max(0.000001, durationSeconds);
        SetView(ViewStartSeconds, ViewEndSeconds, DurationSeconds);
    }

    public void SetView(double startSeconds, double endSeconds, double durationSeconds)
    {
        DurationSeconds = Math.Max(0.000001, durationSeconds);
        (ViewStartSeconds, ViewEndSeconds) = ClampRange(startSeconds, endSeconds);
        IsEnabled = _hasOverviewPoints && DurationSeconds > 0.000001;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = Bounds;
        _lastTrack = new Rect(
            bounds.Left + 1,
            bounds.Top + 5,
            Math.Max(1, bounds.Width - 2),
            Math.Max(1, bounds.Height - 10));

        context.FillRectangle(TrackBrush, _lastTrack);
        context.DrawRectangle(BorderPen, _lastTrack);

        if (_overview.Count == 0 || !_hasOverviewPoints)
        {
            double y = _lastTrack.Top + _lastTrack.Height * 0.5;
            context.DrawLine(new Pen(EmptyBrush, 1), new Point(_lastTrack.Left + 8, y), new Point(_lastTrack.Right - 8, y));
            return;
        }

        DrawOverview(context, _lastTrack);
        DrawWindow(context, _lastTrack);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_overview.Count == 0 || !_lastTrack.Contains(e.GetPosition(this)))
        {
            return;
        }

        Focus();
        Point point = e.GetPosition(this);
        Rect window = WindowRect(_lastTrack);
        _dragMode = ResolveDragMode(point, window);
        if (_dragMode == DragMode.None)
        {
            CenterWindowAt(point.X);
            _dragMode = DragMode.Move;
        }

        _dragStartPoint = point;
        _dragStartSeconds = ViewStartSeconds;
        _dragEndSeconds = ViewEndSeconds;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragMode == DragMode.None)
        {
            return;
        }

        Point point = e.GetPosition(this);
        double deltaSeconds = (point.X - _dragStartPoint.X) / Math.Max(1, _lastTrack.Width) * DurationSeconds;
        double minWindow = Math.Max(0.000001, DurationSeconds / 100_000);

        switch (_dragMode)
        {
            case DragMode.Move:
                SetWindow(_dragStartSeconds + deltaSeconds, _dragEndSeconds + deltaSeconds);
                break;
            case DragMode.ResizeStart:
                SetWindow(Math.Min(_dragEndSeconds - minWindow, _dragStartSeconds + deltaSeconds), _dragEndSeconds);
                break;
            case DragMode.ResizeEnd:
                SetWindow(_dragStartSeconds, Math.Max(_dragStartSeconds + minWindow, _dragEndSeconds + deltaSeconds));
                break;
        }

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragMode == DragMode.None)
        {
            return;
        }

        _dragMode = DragMode.None;
        e.Pointer.Capture(null);
        RangeRequested?.Invoke(ViewStartSeconds, ViewEndSeconds);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_overview.Count == 0 || !_lastTrack.Contains(e.GetPosition(this)))
        {
            return;
        }

        Point point = e.GetPosition(this);
        double factor = e.Delta.Y > 0 ? 0.75 : 1.35;
        double anchor = PixelToSeconds(point.X);
        double start = anchor - (anchor - ViewStartSeconds) * factor;
        double end = anchor + (ViewEndSeconds - anchor) * factor;
        SetWindow(start, end);
        RangeRequested?.Invoke(ViewStartSeconds, ViewEndSeconds);
        e.Handled = true;
    }

    private void DrawOverview(DrawingContext context, Rect track)
    {
        EnsureOverviewBins(track);
        if (_overviewBins.Length == 0)
        {
            double emptyY = track.Top + track.Height * 0.5;
            context.DrawLine(new Pen(EmptyBrush, 1), new Point(track.Left + 8, emptyY), new Point(track.Right - 8, emptyY));
            return;
        }

        double min = _overviewMin;
        double max = _overviewMax;
        double range = Math.Max(0.000001, max - min);
        Rect envelopeTrack = new(
            track.Left,
            track.Top,
            track.Width,
            Math.Max(1, track.Height - HeatStripHeight - EnvelopeBottomGap));
        Rect heatTrack = new(
            track.Left,
            envelopeTrack.Bottom + EnvelopeBottomGap,
            track.Width,
            HeatStripHeight);

        double zeroRatio = Math.Clamp((0 - min) / range, 0, 1);
        double zeroY = envelopeTrack.Bottom - zeroRatio * envelopeTrack.Height;
        context.DrawLine(CenterPen, new Point(envelopeTrack.Left, zeroY), new Point(envelopeTrack.Right, zeroY));

        Point? previousMin = null;
        Point? previousMax = null;
        Point? previousCenter = null;
        double columnWidth = Math.Max(1, track.Width / _overviewBins.Length);
        for (int i = 0; i < _overviewBins.Length; i++)
        {
            OverviewBin bin = _overviewBins[i];
            if (bin.Count == 0)
            {
                previousMin = null;
                previousMax = null;
                previousCenter = null;
                double emptyLeft = heatTrack.Left + i * heatTrack.Width / _overviewBins.Length;
                context.FillRectangle(HeatEmptyBrush, new Rect(emptyLeft, heatTrack.Top, columnWidth + 0.5, heatTrack.Height));
                continue;
            }

            double x = envelopeTrack.Left + (i + 0.5) * envelopeTrack.Width / _overviewBins.Length;
            double left = heatTrack.Left + i * heatTrack.Width / _overviewBins.Length;
            double yMin = envelopeTrack.Bottom - ((bin.NormalizedMinimum - min) / range) * envelopeTrack.Height;
            double yMax = envelopeTrack.Bottom - ((bin.NormalizedMaximum - min) / range) * envelopeTrack.Height;
            var minPoint = new Point(x, yMin);
            var maxPoint = new Point(x, yMax);
            context.DrawLine(OverviewPen, minPoint, maxPoint);
            if (previousMin.HasValue && previousMax.HasValue)
            {
                context.DrawLine(OverviewOutlinePen, previousMin.Value, minPoint);
                context.DrawLine(OverviewOutlinePen, previousMax.Value, maxPoint);
            }

            previousMin = minPoint;
            previousMax = maxPoint;
            if (bin.CenterCount > 0)
            {
                double center = bin.CenterSum / bin.CenterCount;
                double yCenter = envelopeTrack.Bottom - ((center - min) / range) * envelopeTrack.Height;
                var centerPoint = new Point(x, yCenter);
                if (previousCenter.HasValue)
                {
                    context.DrawLine(CenterTrendPen, previousCenter.Value, centerPoint);
                }

                previousCenter = centerPoint;
            }
            else
            {
                previousCenter = null;
            }

            context.FillRectangle(HeatBrush(bin.AnomalyScore), new Rect(left, heatTrack.Top, columnWidth + 0.5, heatTrack.Height));
        }
    }

    private void EnsureOverviewBins(Rect track)
    {
        int binCount = (int)Math.Clamp(track.Width, 64, 1800);
        if (_overviewBins.Length == binCount)
        {
            return;
        }

        var bins = new OverviewBin[binCount];
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;

        foreach (TdmsChannelEnvelope series in _overview)
        {
            int count = series.Points.Count;
            if (count == 0)
            {
                continue;
            }

            (double seriesCenter, double seriesHalfRange) = FindSeriesScale(series);

            double sampleRate = series.Channel.SampleRate > 0 && !double.IsNaN(series.Channel.SampleRate) && !double.IsInfinity(series.Channel.SampleRate)
                ? series.Channel.SampleRate
                : 1;
            double startSample = series.StartSample;
            double sampleCount = Math.Max(1, series.SampleCount);
            for (int i = 0; i < count; i++)
            {
                TdmsEnvelopePoint point = series.Points[i];
                if (!float.IsFinite(point.Minimum) || !float.IsFinite(point.Maximum))
                {
                    continue;
                }

                if (point.Minimum < min) min = point.Minimum;
                if (point.Maximum > max) max = point.Maximum;
                double normalizedMinimum = Normalize(point.Minimum, seriesCenter, seriesHalfRange);
                double normalizedMaximum = Normalize(point.Maximum, seriesCenter, seriesHalfRange);
                if (normalizedMinimum > normalizedMaximum)
                {
                    (normalizedMinimum, normalizedMaximum) = (normalizedMaximum, normalizedMinimum);
                }

                double centerSample = startSample + (i + 0.5) * sampleCount / count;
                double centerSeconds = centerSample / sampleRate;
                int binIndex = Math.Clamp((int)(centerSeconds / DurationSeconds * binCount), 0, binCount - 1);
                ref OverviewBin bin = ref bins[binIndex];
                if (bin.Count == 0)
                {
                    bin.Minimum = point.Minimum;
                    bin.Maximum = point.Maximum;
                    bin.NormalizedMinimum = normalizedMinimum;
                    bin.NormalizedMaximum = normalizedMaximum;
                }
                else
                {
                    if (point.Minimum < bin.Minimum) bin.Minimum = point.Minimum;
                    if (point.Maximum > bin.Maximum) bin.Maximum = point.Maximum;
                    if (normalizedMinimum < bin.NormalizedMinimum) bin.NormalizedMinimum = normalizedMinimum;
                    if (normalizedMaximum > bin.NormalizedMaximum) bin.NormalizedMaximum = normalizedMaximum;
                }

                bin.CenterSum += Normalize((point.First + point.Last) * 0.5, seriesCenter, seriesHalfRange);
                bin.CenterCount++;
                bin.Count++;
            }
        }

        if (double.IsInfinity(min) || double.IsInfinity(max))
        {
            _overviewBins = Array.Empty<OverviewBin>();
            _overviewMin = -1;
            _overviewMax = 1;
            return;
        }

        ScoreAnomalies(bins);
        (min, max) = FindNormalizedRange(bins);
        if (Math.Abs(max - min) < 0.000001)
        {
            min -= 1;
            max += 1;
        }
        else
        {
            double padding = Math.Max(0.08, (max - min) * 0.08);
            min -= padding;
            max += padding;
        }

        _overviewMin = min;
        _overviewMax = max;
        _overviewBins = bins;
    }

    private static (double Center, double HalfRange) FindSeriesScale(TdmsChannelEnvelope series)
    {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        foreach (TdmsEnvelopePoint point in series.Points)
        {
            if (!float.IsFinite(point.Minimum) || !float.IsFinite(point.Maximum))
            {
                continue;
            }

            if (point.Minimum < min) min = point.Minimum;
            if (point.Maximum > max) max = point.Maximum;
        }

        if (double.IsInfinity(min) || double.IsInfinity(max))
        {
            return (0, 1);
        }

        double halfRange = Math.Max(MinimumSeriesRange, (max - min) * 0.5);
        return ((max + min) * 0.5, halfRange);
    }

    private static double Normalize(double value, double center, double halfRange)
    {
        return Math.Clamp((value - center) / Math.Max(MinimumSeriesRange, halfRange), -1.5, 1.5);
    }

    private static (double Min, double Max) FindNormalizedRange(IReadOnlyList<OverviewBin> bins)
    {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        foreach (OverviewBin bin in bins)
        {
            if (bin.Count == 0)
            {
                continue;
            }

            if (bin.NormalizedMinimum < min) min = bin.NormalizedMinimum;
            if (bin.NormalizedMaximum > max) max = bin.NormalizedMaximum;
        }

        if (double.IsInfinity(min) || double.IsInfinity(max))
        {
            return (-1, 1);
        }

        return (min, max);
    }

    private static void ScoreAnomalies(OverviewBin[] bins)
    {
        double[] amplitudes = bins
            .Where(bin => bin.Count > 0)
            .Select(bin => Math.Max(0, bin.NormalizedMaximum - bin.NormalizedMinimum))
            .ToArray();
        if (amplitudes.Length == 0)
        {
            return;
        }

        double amplitudeMedian = Median(amplitudes);
        double amplitudeMad = Median(amplitudes.Select(value => Math.Abs(value - amplitudeMedian)).ToArray());

        double previousCenter = 0;
        bool hasPreviousCenter = false;
        var deltas = new List<double>(bins.Length);
        for (int i = 0; i < bins.Length; i++)
        {
            if (bins[i].Count == 0 || bins[i].CenterCount == 0)
            {
                continue;
            }

            double center = bins[i].CenterSum / bins[i].CenterCount;
            if (hasPreviousCenter)
            {
                deltas.Add(Math.Abs(center - previousCenter));
            }

            previousCenter = center;
            hasPreviousCenter = true;
        }

        double deltaMedian = deltas.Count > 0 ? Median(deltas.ToArray()) : 0;
        double deltaMad = deltas.Count > 0 ? Median(deltas.Select(value => Math.Abs(value - deltaMedian)).ToArray()) : 0;
        previousCenter = 0;
        hasPreviousCenter = false;
        for (int i = 0; i < bins.Length; i++)
        {
            if (bins[i].Count == 0)
            {
                continue;
            }

            double amplitude = Math.Max(0, bins[i].NormalizedMaximum - bins[i].NormalizedMinimum);
            double amplitudeScore = RobustScore(amplitude, amplitudeMedian, amplitudeMad);
            double deltaScore = 0;
            if (bins[i].CenterCount > 0)
            {
                double center = bins[i].CenterSum / bins[i].CenterCount;
                if (hasPreviousCenter)
                {
                    deltaScore = RobustScore(Math.Abs(center - previousCenter), deltaMedian, deltaMad);
                }

                previousCenter = center;
                hasPreviousCenter = true;
            }

            bins[i].AnomalyScore = Math.Clamp(Math.Max(amplitudeScore, deltaScore), 0, 1);
        }
    }

    private static double RobustScore(double value, double median, double mad)
    {
        double scale = Math.Max(0.025, mad * 1.4826);
        return Math.Clamp((value - median - scale * 2.0) / (scale * 6.0), 0, 1);
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0)
        {
            return 0;
        }

        Array.Sort(values);
        int middle = values.Length / 2;
        return values.Length % 2 == 0
            ? (values[middle - 1] + values[middle]) * 0.5
            : values[middle];
    }

    private static IBrush HeatBrush(double score)
    {
        int index = (int)Math.Round(Math.Clamp(score, 0, 1) * (HeatBrushes.Length - 1));
        return HeatBrushes[index];
    }

    private static IBrush[] CreateHeatBrushes()
    {
        var brushes = new IBrush[32];
        Color calm = Color.FromArgb(125, 47, 124, 104);
        Color warning = Color.FromArgb(190, 203, 143, 49);
        Color hot = Color.FromArgb(225, 176, 62, 62);
        for (int i = 0; i < brushes.Length; i++)
        {
            double ratio = (double)i / (brushes.Length - 1);
            Color color = ratio < 0.55
                ? Lerp(calm, warning, ratio / 0.55)
                : Lerp(warning, hot, (ratio - 0.55) / 0.45);
            brushes[i] = new SolidColorBrush(color);
        }

        return brushes;
    }

    private static Color Lerp(Color start, Color end, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        return Color.FromArgb(
            (byte)Math.Round(start.A + (end.A - start.A) * ratio),
            (byte)Math.Round(start.R + (end.R - start.R) * ratio),
            (byte)Math.Round(start.G + (end.G - start.G) * ratio),
            (byte)Math.Round(start.B + (end.B - start.B) * ratio));
    }

    private void DrawWindow(DrawingContext context, Rect track)
    {
        Rect window = WindowRect(track);
        if (window.Left > track.Left)
        {
            context.FillRectangle(DimBrush, new Rect(track.Left, track.Top, window.Left - track.Left, track.Height));
        }

        if (window.Right < track.Right)
        {
            context.FillRectangle(DimBrush, new Rect(window.Right, track.Top, track.Right - window.Right, track.Height));
        }

        context.FillRectangle(WindowBrush, window);
        context.DrawRectangle(WindowPen, window);

        double handleHeight = Math.Max(16, window.Height * 0.62);
        double handleTop = window.Top + (window.Height - handleHeight) * 0.5;
        context.FillRectangle(HandleBrush, new Rect(window.Left + 3, handleTop, 3, handleHeight));
        context.FillRectangle(HandleBrush, new Rect(window.Right - 6, handleTop, 3, handleHeight));
    }

    private DragMode ResolveDragMode(Point point, Rect window)
    {
        const double handlePixels = 10;
        if (Math.Abs(point.X - window.Left) <= handlePixels)
        {
            return DragMode.ResizeStart;
        }

        if (Math.Abs(point.X - window.Right) <= handlePixels)
        {
            return DragMode.ResizeEnd;
        }

        return window.Contains(point) ? DragMode.Move : DragMode.None;
    }

    private void CenterWindowAt(double x)
    {
        double width = ViewEndSeconds - ViewStartSeconds;
        double center = PixelToSeconds(x);
        SetWindow(center - width * 0.5, center + width * 0.5);
    }

    private void SetWindow(double startSeconds, double endSeconds)
    {
        (ViewStartSeconds, ViewEndSeconds) = ClampRange(startSeconds, endSeconds);
        InvalidateVisual();
    }

    private (double Start, double End) ClampRange(double startSeconds, double endSeconds)
    {
        if (startSeconds > endSeconds)
        {
            (startSeconds, endSeconds) = (endSeconds, startSeconds);
        }

        double width = Math.Max(0.000001, endSeconds - startSeconds);
        width = Math.Min(width, DurationSeconds);
        startSeconds = Math.Clamp(startSeconds, 0, Math.Max(0, DurationSeconds - width));
        return (startSeconds, startSeconds + width);
    }

    private Rect WindowRect(Rect track)
    {
        double leftRatio = Math.Clamp(ViewStartSeconds / DurationSeconds, 0, 1);
        double rightRatio = Math.Clamp(ViewEndSeconds / DurationSeconds, 0, 1);
        double left = track.Left + leftRatio * track.Width;
        double right = track.Left + rightRatio * track.Width;
        if (right - left < 6)
        {
            double center = (left + right) * 0.5;
            left = Math.Max(track.Left, center - 3);
            right = Math.Min(track.Right, center + 3);
        }

        return new Rect(left, track.Top, Math.Max(1, right - left), track.Height);
    }

    private double PixelToSeconds(double x)
    {
        double ratio = Math.Clamp((x - _lastTrack.Left) / Math.Max(1, _lastTrack.Width), 0, 1);
        return ratio * DurationSeconds;
    }

    private enum DragMode
    {
        None,
        Move,
        ResizeStart,
        ResizeEnd
    }

    private struct OverviewBin
    {
        public double Minimum;
        public double Maximum;
        public double NormalizedMinimum;
        public double NormalizedMaximum;
        public double CenterSum;
        public int CenterCount;
        public double AnomalyScore;
        public int Count;
    }
}
