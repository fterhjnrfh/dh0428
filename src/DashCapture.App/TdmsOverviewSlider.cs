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
    private static readonly IBrush OutlineBrush = new SolidColorBrush(Color.FromArgb(170, 38, 119, 220));
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromArgb(40, 24, 35, 52));
    private static readonly IBrush WindowBrush = new SolidColorBrush(Color.FromArgb(82, 38, 119, 220));
    private static readonly IBrush HandleBrush = new SolidColorBrush(Color.FromArgb(190, 38, 119, 220));
    private static readonly Pen BorderPen = new(new SolidColorBrush(Color.FromRgb(199, 211, 228)), 1);
    private static readonly Pen WindowPen = new(new SolidColorBrush(Color.FromRgb(38, 119, 220)), 1.2);
    private static readonly Pen CenterPen = new(new SolidColorBrush(Color.FromArgb(90, 91, 108, 132)), 1);

    private IReadOnlyList<TdmsChannelEnvelope> _overview = Array.Empty<TdmsChannelEnvelope>();
    private Rect _lastTrack;
    private Point _dragStartPoint;
    private double _dragStartSeconds;
    private double _dragEndSeconds;
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
        DurationSeconds = Math.Max(0.000001, durationSeconds);
        SetView(ViewStartSeconds, ViewEndSeconds, DurationSeconds);
    }

    public void SetView(double startSeconds, double endSeconds, double durationSeconds)
    {
        DurationSeconds = Math.Max(0.000001, durationSeconds);
        (ViewStartSeconds, ViewEndSeconds) = ClampRange(startSeconds, endSeconds);
        IsEnabled = _overview.Count > 0 && DurationSeconds > 0.000001;
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

        if (_overview.Count == 0 || _overview.All(series => series.Points.Count == 0))
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
        (double min, double max) = FindRange();
        double range = Math.Max(0.000001, max - min);
        int bins = (int)Math.Clamp(track.Width, 64, 1800);
        var minimums = new double[bins];
        var maximums = new double[bins];
        Array.Fill(minimums, double.PositiveInfinity);
        Array.Fill(maximums, double.NegativeInfinity);

        foreach (TdmsChannelEnvelope series in _overview)
        {
            int count = series.Points.Count;
            if (count == 0)
            {
                continue;
            }

            for (int i = 0; i < count; i++)
            {
                int bin = Math.Clamp((int)((i + 0.5) / count * bins), 0, bins - 1);
                TdmsEnvelopePoint point = series.Points[i];
                if (point.Minimum < minimums[bin]) minimums[bin] = point.Minimum;
                if (point.Maximum > maximums[bin]) maximums[bin] = point.Maximum;
            }
        }

        double zeroRatio = Math.Clamp((0 - min) / range, 0, 1);
        double zeroY = track.Bottom - zeroRatio * track.Height;
        context.DrawLine(CenterPen, new Point(track.Left, zeroY), new Point(track.Right, zeroY));

        var pen = new Pen(OutlineBrush, 1);
        for (int i = 0; i < bins; i++)
        {
            if (double.IsInfinity(minimums[i]) || double.IsInfinity(maximums[i]))
            {
                continue;
            }

            double x = track.Left + (i + 0.5) * track.Width / bins;
            double yMin = track.Bottom - ((minimums[i] - min) / range) * track.Height;
            double yMax = track.Bottom - ((maximums[i] - min) / range) * track.Height;
            context.DrawLine(pen, new Point(x, yMin), new Point(x, yMax));
        }
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

    private (double Min, double Max) FindRange()
    {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        foreach (TdmsChannelEnvelope series in _overview)
        {
            foreach (TdmsEnvelopePoint point in series.Points)
            {
                if (!float.IsFinite(point.Minimum) || !float.IsFinite(point.Maximum))
                {
                    continue;
                }

                if (point.Minimum < min) min = point.Minimum;
                if (point.Maximum > max) max = point.Maximum;
            }
        }

        if (double.IsInfinity(min) || double.IsInfinity(max))
        {
            return (-1, 1);
        }

        return Math.Abs(max - min) < 0.000001 ? (min - 1, max + 1) : (min, max);
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
}
