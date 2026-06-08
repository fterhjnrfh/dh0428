using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DashCapture.Storage;

namespace DashCapture.App;

public sealed class TdmsOverviewSlider : Control
{
    private static readonly IBrush TrackBrush = new SolidColorBrush(Color.FromRgb(236, 243, 252));
    private static readonly IBrush EmptyBrush = new SolidColorBrush(Color.FromRgb(190, 202, 220));
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromArgb(40, 24, 35, 52));
    private static readonly IBrush WindowBrush = new SolidColorBrush(Color.FromArgb(82, 38, 119, 220));
    private static readonly IBrush HandleBrush = new SolidColorBrush(Color.FromArgb(190, 38, 119, 220));
    private static readonly Pen BorderPen = new(new SolidColorBrush(Color.FromRgb(199, 211, 228)), 1);
    private static readonly Pen OverviewPen = new(new SolidColorBrush(Color.FromArgb(190, 38, 119, 220)), 1);
    private static readonly Pen WindowPen = new(new SolidColorBrush(Color.FromRgb(38, 119, 220)), 1.2);
    private static readonly Pen CenterPen = new(new SolidColorBrush(Color.FromArgb(90, 91, 108, 132)), 1);

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
        Height = 54;
        MinHeight = 48;
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

        double zeroRatio = Math.Clamp((0 - min) / range, 0, 1);
        double zeroY = track.Bottom - zeroRatio * track.Height;
        context.DrawLine(CenterPen, new Point(track.Left, zeroY), new Point(track.Right, zeroY));

        for (int i = 0; i < _overviewBins.Length; i++)
        {
            OverviewBin bin = _overviewBins[i];
            if (bin.Count == 0)
            {
                continue;
            }

            double x = track.Left + (i + 0.5) * track.Width / _overviewBins.Length;
            double yMin = track.Bottom - ((bin.Minimum - min) / range) * track.Height;
            double yMax = track.Bottom - ((bin.Maximum - min) / range) * track.Height;
            context.DrawLine(OverviewPen, new Point(x, yMin), new Point(x, yMax));
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

            for (int i = 0; i < count; i++)
            {
                TdmsEnvelopePoint point = series.Points[i];
                if (!float.IsFinite(point.Minimum) || !float.IsFinite(point.Maximum))
                {
                    continue;
                }

                if (point.Minimum < min) min = point.Minimum;
                if (point.Maximum > max) max = point.Maximum;
                int binIndex = Math.Clamp((int)((i + 0.5) / count * binCount), 0, binCount - 1);
                ref OverviewBin bin = ref bins[binIndex];
                if (bin.Count == 0)
                {
                    bin.Minimum = point.Minimum;
                    bin.Maximum = point.Maximum;
                }
                else
                {
                    if (point.Minimum < bin.Minimum) bin.Minimum = point.Minimum;
                    if (point.Maximum > bin.Maximum) bin.Maximum = point.Maximum;
                }

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

        if (Math.Abs(max - min) < 0.000001)
        {
            min -= 1;
            max += 1;
        }

        _overviewMin = min;
        _overviewMax = max;
        _overviewBins = bins;
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
        public int Count;
    }
}
