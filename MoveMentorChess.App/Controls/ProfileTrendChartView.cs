using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MoveMentorChess.App.Controls;

public enum ProfileTrendChartKind
{
    Line,
    Bars
}

public sealed record ProfileTrendChartPoint(string Label, double? Value);

public sealed record ProfileTrendChartSeries(
    string Name,
    IBrush Stroke,
    IReadOnlyList<ProfileTrendChartPoint> Points,
    ProfileTrendChartKind Kind = ProfileTrendChartKind.Line);

public sealed class ProfileTrendChartView : Control
{
    public static readonly StyledProperty<IReadOnlyList<ProfileTrendChartSeries>> SeriesProperty =
        AvaloniaProperty.Register<ProfileTrendChartView, IReadOnlyList<ProfileTrendChartSeries>>(
            nameof(Series),
            []);

    public IReadOnlyList<ProfileTrendChartSeries> Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Rect bounds = Bounds;
        if (bounds.Width <= 20 || bounds.Height <= 20)
        {
            return;
        }

        Rect plot = new(44, 12, Math.Max(1, bounds.Width - 56), Math.Max(1, bounds.Height - 58));
        context.DrawRectangle(Brush.Parse("#13242E"), null, bounds);

        IReadOnlyList<ProfileTrendChartSeries> visibleSeries = Series
            .Where(series => series.Points.Any(point => point.Value.HasValue))
            .ToList();
        if (visibleSeries.Count == 0)
        {
            DrawText(context, "No chart data yet", 14, Brush.Parse("#9EB5C5"), new Point(16, 16));
            return;
        }

        List<double> values = visibleSeries
            .SelectMany(series => series.Points)
            .Where(point => point.Value.HasValue)
            .Select(point => point.Value!.Value)
            .ToList();
        (double min, double max) = BuildAxisRange(values);
        bool hasBars = visibleSeries.Any(series => series.Kind == ProfileTrendChartKind.Bars);
        if (hasBars)
        {
            min = 0;
            max = BuildRoundedMax(values.Max());
        }

        if (Math.Abs(max - min) < 1)
        {
            max = min + 1;
        }

        DrawGrid(context, plot, min, max);
        int barSeriesCount = visibleSeries.Count(series => series.Kind == ProfileTrendChartKind.Bars);
        int barSeriesIndex = 0;
        foreach (ProfileTrendChartSeries series in visibleSeries)
        {
            if (series.Kind == ProfileTrendChartKind.Bars)
            {
                DrawBars(context, plot, series, min, max, barSeriesIndex, barSeriesCount);
                barSeriesIndex++;
            }
            else
            {
                DrawLine(context, plot, series, min, max);
            }
        }

        DrawXAxisLabels(context, visibleSeries[0].Points, plot);
        DrawLegend(context, visibleSeries, plot);
    }

    private static void DrawGrid(DrawingContext context, Rect plot, double min, double max)
    {
        Pen gridPen = new(Brush.Parse("#284556"), 1);
        Pen axisPen = new(Brush.Parse("#4F6B7A"), 1);
        for (int i = 0; i <= 4; i++)
        {
            double y = plot.Bottom - (plot.Height * i / 4.0);
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            double value = min + ((max - min) * i / 4.0);
            DrawText(context, Math.Round(value).ToString(), 11, Brush.Parse("#9EB5C5"), new Point(4, y - 8));
        }

        context.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        context.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
    }

    private static (double Min, double Max) BuildAxisRange(IReadOnlyList<double> values)
    {
        double max = values.Max();
        double min = values.Min();
        double step = max <= 10 ? 1 : max <= 100 ? 10 : 50;
        return (Math.Floor(min / step) * step, Math.Ceiling(max / step) * step);
    }

    private static double BuildRoundedMax(double max)
    {
        double step = max <= 10 ? 1 : max <= 100 ? 10 : 50;
        return Math.Max(step, Math.Ceiling(max / step) * step);
    }

    private static void DrawLine(DrawingContext context, Rect plot, ProfileTrendChartSeries series, double min, double max)
    {
        List<Point> points = BuildPoints(plot, series.Points, min, max);
        if (points.Count == 0)
        {
            return;
        }

        Pen pen = new(series.Stroke, 2);
        for (int i = 1; i < points.Count; i++)
        {
            context.DrawLine(pen, points[i - 1], points[i]);
        }

        foreach (Point point in points)
        {
            context.DrawEllipse(series.Stroke, null, point, 3.5, 3.5);
        }
    }

    private static void DrawBars(
        DrawingContext context,
        Rect plot,
        ProfileTrendChartSeries series,
        double min,
        double max,
        int seriesIndex,
        int seriesCount)
    {
        int pointCount = Math.Max(1, series.Points.Count);
        double slot = plot.Width / pointCount;
        int safeSeriesCount = Math.Max(1, seriesCount);
        double groupWidth = slot * 0.68;
        double width = Math.Clamp(groupWidth / safeSeriesCount, 3, 18);
        for (int i = 0; i < series.Points.Count; i++)
        {
            if (!series.Points[i].Value.HasValue)
            {
                continue;
            }

            double groupStart = plot.Left + (slot * i) + ((slot - groupWidth) / 2.0);
            double x = groupStart + (width * seriesIndex);
            double y = ScaleY(plot, series.Points[i].Value!.Value, min, max);
            Rect bar = new(x, y, width, plot.Bottom - y);
            context.DrawRectangle(series.Stroke, null, bar);
        }
    }

    private static List<Point> BuildPoints(Rect plot, IReadOnlyList<ProfileTrendChartPoint> input, double min, double max)
    {
        List<ProfileTrendChartPoint> points = input.Where(point => point.Value.HasValue).ToList();
        if (points.Count == 0)
        {
            return [];
        }

        double step = points.Count == 1 ? 0 : plot.Width / (points.Count - 1);
        return points
            .Select((point, index) => new Point(
                points.Count == 1 ? plot.Left + plot.Width / 2.0 : plot.Left + step * index,
                ScaleY(plot, point.Value!.Value, min, max)))
            .ToList();
    }

    private static double ScaleY(Rect plot, double value, double min, double max)
    {
        double normalized = (value - min) / (max - min);
        return plot.Bottom - Math.Clamp(normalized, 0, 1) * plot.Height;
    }

    private static void DrawLegend(DrawingContext context, IReadOnlyList<ProfileTrendChartSeries> series, Rect plot)
    {
        double x = plot.Left;
        double y = plot.Bottom + 24;
        foreach (ProfileTrendChartSeries item in series.Take(3))
        {
            context.DrawRectangle(item.Stroke, null, new Rect(x, y + 4, 10, 10));
            DrawText(context, item.Name, 11, Brush.Parse("#D7E2EA"), new Point(x + 14, y));
            x += Math.Min(170, item.Name.Length * 7 + 34);
        }
    }

    private static void DrawXAxisLabels(DrawingContext context, IReadOnlyList<ProfileTrendChartPoint> points, Rect plot)
    {
        IReadOnlyList<ProfileTrendChartPoint> labeled = points
            .Where(point => !string.IsNullOrWhiteSpace(point.Label))
            .ToList();
        if (labeled.Count == 0)
        {
            return;
        }

        int lastIndex = labeled.Count - 1;
        IEnumerable<int> indexes = labeled.Count <= 4
            ? Enumerable.Range(0, labeled.Count)
            : [0, labeled.Count / 2, lastIndex];

        foreach (int index in indexes.Distinct().Order())
        {
            ProfileTrendChartPoint point = labeled[index];
            double x = labeled.Count == 1
                ? plot.Left + plot.Width / 2.0
                : plot.Left + (plot.Width * index / lastIndex);
            string label = point.Label.Length > 10 ? point.Label[^10..] : point.Label;
            FormattedText formatted = CreateFormattedText(label, 10, Brush.Parse("#9EB5C5"));
            context.DrawText(
                formatted,
                new Point(Math.Clamp(x - formatted.Width / 2.0, plot.Left, plot.Right - formatted.Width), plot.Bottom + 4));
        }
    }

    private static void DrawText(DrawingContext context, string text, double size, IBrush brush, Point origin)
    {
        context.DrawText(CreateFormattedText(text, size, brush), origin);
    }

    private static FormattedText CreateFormattedText(string text, double size, IBrush brush)
    {
        return new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            size,
            brush);
    }
}
