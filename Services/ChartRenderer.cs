using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EarthOnline.Desktop.Services;

/// <summary>
/// 零依赖图表渲染（直接用 WPF 的 Shape 画到 Canvas，不引任何图表库）。
/// 配色与网页 css/style.css 的 token 一致（Canvas 读不到 CSS 变量，故硬编码，
/// 与网页 stats.js 的 CHART_THEME 同处理方式）。
/// </summary>
public static class ChartRenderer
{
    private static readonly Color Line = Color.FromRgb(0xD4, 0xA3, 0x73);
    private static readonly Color Area = Color.FromArgb(0x1F, 0xD4, 0xA3, 0x73);
    private static readonly Color Grid = Color.FromRgb(0xE8, 0xE2, 0xDA);
    private static readonly Color Text = Color.FromRgb(0x7A, 0x72, 0x68);
    private static readonly Color Point = Color.FromRgb(0xC4, 0x9A, 0x6C);

    private const double PaddingLeft = 28;
    private const double PaddingRight = 12;
    private const double PaddingTop = 10;
    private const double PaddingBottom = 22;

    /// <summary>折线 + 面积图（全 0 时不画网格线，由调用方给空状态文案）。</summary>
    public static void DrawLine(Canvas canvas, IReadOnlyList<string> labels, IReadOnlyList<int> values)
    {
        canvas.Children.Clear();
        if (canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;
        if (labels.Count == 0 || values.Count == 0) return;

        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        double x0 = PaddingLeft, x1 = w - PaddingRight;
        double y0 = PaddingTop, y1 = h - PaddingBottom;
        double plotW = Math.Max(x1 - x0, 1);
        double plotH = Math.Max(y1 - y0, 1);

        int max = Math.Max(values.Max(), 1);

        // 横向网格 + Y 轴刻度
        for (int i = 0; i <= 3; i++)
        {
            double y = y0 + plotH * i / 3.0;
            canvas.Children.Add(new Line
            {
                X1 = x0, Y1 = y, X2 = x1, Y2 = y,
                Stroke = new SolidColorBrush(Grid), StrokeThickness = 1, SnapsToDevicePixels = true
            });
            var tb = new TextBlock
            {
                Text = ((int)Math.Round(max * (3 - i) / 3.0)).ToString(CultureInfo.InvariantCulture),
                FontSize = 10,
                Foreground = new SolidColorBrush(Text)
            };
            Canvas.SetLeft(tb, 2);
            Canvas.SetTop(tb, y - 7);
            canvas.Children.Add(tb);
        }

        int n = values.Count;
        double StepX() => n <= 1 ? 0 : plotW / (n - 1);

        var pts = new PointCollection();
        for (int i = 0; i < n; i++)
        {
            double x = n <= 1 ? (x0 + x1) / 2 : x0 + StepX() * i;
            double y = y1 - plotH * (values[i] / (double)max);
            pts.Add(new Point(x, y));
        }

        // 面积
        var poly = new Polygon
        {
            Points = new PointCollection(pts),
            Fill = new SolidColorBrush(Area),
            StrokeThickness = 0
        };
        if (n >= 2)
        {
            poly.Points.Add(new Point(pts[^1].X, y1));
            poly.Points.Add(new Point(pts[0].X, y1));
        }
        canvas.Children.Add(poly);

        // 折线
        canvas.Children.Add(new Polyline
        {
            Points = pts,
            Stroke = new SolidColorBrush(Line),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            SnapsToDevicePixels = true
        });

        // 点 + X 轴标签
        for (int i = 0; i < n; i++)
        {
            var p = pts[i];
            canvas.Children.Add(new Ellipse
            {
                Width = 7, Height = 7,
                Fill = new SolidColorBrush(Point),
                Stroke = new SolidColorBrush(Line),
                StrokeThickness = 1
            });
            var e = canvas.Children[^1];
            Canvas.SetLeft(e, p.X - 3.5);
            Canvas.SetTop(e, p.Y - 3.5);

            var lb = new TextBlock
            {
                Text = labels[i],
                FontSize = 10,
                Foreground = new SolidColorBrush(Text)
            };
            lb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(lb, Math.Min(Math.Max(p.X - lb.DesiredSize.Width / 2, 0), w - lb.DesiredSize.Width));
            Canvas.SetTop(lb, y1 + 4);
            canvas.Children.Add(lb);
        }
    }

    /// <summary>柱状图（labelStep 控制隔几个显示一次 X 轴标签）。</summary>
    public static void DrawBar(Canvas canvas, IReadOnlyList<string> labels, IReadOnlyList<int> values, int labelStep = 1)
    {
        canvas.Children.Clear();
        if (canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;
        if (labels.Count == 0 || values.Count == 0) return;

        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        double x0 = PaddingLeft, x1 = w - PaddingRight;
        double y0 = PaddingTop, y1 = h - PaddingBottom;
        double plotW = Math.Max(x1 - x0, 1);
        double plotH = Math.Max(y1 - y0, 1);

        int max = Math.Max(values.Max(), 1);

        for (int i = 0; i <= 3; i++)
        {
            double y = y0 + plotH * i / 3.0;
            canvas.Children.Add(new Line
            {
                X1 = x0, Y1 = y, X2 = x1, Y2 = y,
                Stroke = new SolidColorBrush(Grid), StrokeThickness = 1, SnapsToDevicePixels = true
            });
            var tb = new TextBlock
            {
                Text = ((int)Math.Round(max * (3 - i) / 3.0)).ToString(CultureInfo.InvariantCulture),
                FontSize = 10,
                Foreground = new SolidColorBrush(Text)
            };
            Canvas.SetLeft(tb, 2);
            Canvas.SetTop(tb, y - 7);
            canvas.Children.Add(tb);
        }

        int n = values.Count;
        double slot = plotW / n;
        double barW = Math.Max(Math.Min(slot * 0.62, 28), 2);

        for (int i = 0; i < n; i++)
        {
            double cx = x0 + slot * (i + 0.5);
            double vh = plotH * (values[i] / (double)max);
            double top = y1 - vh;

            var rect = new Rectangle
            {
                Width = barW,
                Height = Math.Max(vh, values[i] > 0 ? 2 : 0),
                Fill = new SolidColorBrush(Line),
                RadiusX = 3, RadiusY = 3
            };
            Canvas.SetLeft(rect, cx - barW / 2);
            Canvas.SetTop(rect, top);
            canvas.Children.Add(rect);

            if (labelStep <= 1 || i % labelStep == 0)
            {
                var lb = new TextBlock { Text = labels[i], FontSize = 10, Foreground = new SolidColorBrush(Text) };
                lb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(lb, Math.Min(Math.Max(cx - lb.DesiredSize.Width / 2, 0), w - lb.DesiredSize.Width));
                Canvas.SetTop(lb, y1 + 4);
                canvas.Children.Add(lb);
            }
        }
    }
}
