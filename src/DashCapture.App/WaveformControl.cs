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
    public double WindowSeconds { get; set; } = 5;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = Bounds;
        context.FillRectangle(BackgroundBrush, bounds);

        Rect plot = CreatePlotRect(bounds);
        context.FillRectangle(PlotBrush, plot);

        IReadOnlyDictionary<Core.Models.ChannelDescriptor, float[]>? snapshot = Store?.Snapshot();
        if (snapshot is null || snapshot.Count == 0 || snapshot.Values.All(v => v.Length == 0))
        {
            DrawAxes(context, plot, -1, 1);
            return;
        }

        float globalMin = float.MaxValue;
        float globalMax = float.MinValue;
        foreach (float[] data in snapshot.Values)
        {
            foreach (float value in data)
            {
                if (value < globalMin) globalMin = value;
                if (value > globalMax) globalMax = value;
            }
        }

        if (globalMin == float.MaxValue || Math.Abs(globalMax - globalMin) < 0.000001f)
        {
            globalMin = -1;
            globalMax = 1;
        }

        (float yMin, float yMax) = NiceBounds(globalMin, globalMax);
        DrawAxes(context, plot, yMin, yMax);

        double width = Math.Max(1, plot.Width);
        double height = Math.Max(1, plot.Height);
        double left = plot.Left;
        double top = plot.Top;
        int channelIndex = 0;

        foreach ((var channel, float[] samples) in snapshot)
        {
            if (samples.Length == 0)
            {
                continue;
            }

            EnvelopePoint[] envelope = EnvelopeDownsampler.Downsample(samples, (int)Math.Max(1, width));
            var pen = new Pen(new SolidColorBrush(Palette[channelIndex % Palette.Length]), 1.4);
            DrawEnvelope(context, envelope, pen, left, top, width, height, yMin, yMax);
            channelIndex++;
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

    private void DrawAxes(DrawingContext context, Rect plot, float yMin, float yMax)
    {
        int yTicks = 6;
        int xTicks = 6;
        double range = yMax - yMin;

        for (int i = 0; i <= yTicks; i++)
        {
            double ratio = i / (double)yTicks;
            double y = plot.Bottom - ratio * plot.Height;
            context.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            double value = yMin + range * ratio;
            DrawText(context, FormatAxisValue(value), new Point(plot.Left - 58, y - 9), 12, AxisBrush);
        }

        for (int i = 0; i <= xTicks; i++)
        {
            double ratio = i / (double)xTicks;
            double x = plot.Left + ratio * plot.Width;
            context.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            double seconds = -WindowSeconds + WindowSeconds * ratio;
            DrawText(context, seconds.ToString("0.##", CultureInfo.InvariantCulture), new Point(x - 13, plot.Bottom + 9), 12, AxisBrush);
        }

        context.DrawLine(AxisPen, plot.BottomLeft, plot.BottomRight);
        context.DrawLine(AxisPen, plot.BottomLeft, plot.TopLeft);
        DrawText(context, "\u5E45\u503C", new Point(plot.Left - 58, plot.Top - 2), 13, LabelBrush);
        DrawText(context, "\u65F6\u95F4 (s)", new Point(plot.Right - 54, plot.Bottom + 27), 13, LabelBrush);
    }

    private static void DrawEnvelope(
        DrawingContext context,
        EnvelopePoint[] envelope,
        Pen pen,
        double left,
        double top,
        double width,
        double height,
        float min,
        float max)
    {
        if (envelope.Length == 0)
        {
            return;
        }

        double range = max - min;
        Point? previous = null;
        for (int i = 0; i < envelope.Length; i++)
        {
            EnvelopePoint point = envelope[i];
            double x = left + (envelope.Length == 1 ? 0 : i * width / (envelope.Length - 1));
            double yMin = top + height - ((point.Minimum - min) / range) * height;
            double yMax = top + height - ((point.Maximum - min) / range) * height;
            double yLast = top + height - ((point.Last - min) / range) * height;
            context.DrawLine(pen, new Point(x, yMin), new Point(x, yMax));

            var current = new Point(x, yLast);
            if (previous is not null)
            {
                context.DrawLine(pen, previous.Value, current);
            }

            previous = current;
        }
    }

    private static (float Min, float Max) NiceBounds(float min, float max)
    {
        double range = NiceNumber(max - min, round: false);
        double step = NiceNumber(range / 6, round: true);
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
            return value.ToString("0.##E+0", CultureInfo.InvariantCulture);
        }

        return value.ToString("0.###", CultureInfo.InvariantCulture);
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
