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
    // 配色改为按主题实时取（static readonly 会把浅色值固化，深色模式下网格/文字看不清）。
    // 取值走 ThemeService：无 Application 时（单测 / 设计时）按当前主题标记回落，不会空引用。
    private static Color Line => ThemeService.ColorOf("AccentBrush");
    private static Color Area => ThemeService.ColorOf("AccentBrush") with { A = 0x1F };
    private static Color Grid => ThemeService.ColorOf("ChartGridBrush");
    private static Color Text => ThemeService.ColorOf("TextSecondaryBrush");
    private static Color Point => ThemeService.ColorOf("StatusDoneBrush");

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

    /// <summary>进度环（0~1），中心叠文字由调用方放（Canvas 同一 Grid 里更简单）。</summary>
    public static void DrawProgressRing(Canvas canvas, double fraction, double thickness = 9)
    {
        canvas.Children.Clear();
        double size = Math.Min(canvas.ActualWidth, canvas.ActualHeight);
        if (size <= 0) return;
        double r = (size - thickness) / 2;
        double cx = canvas.ActualWidth / 2, cy = canvas.ActualHeight / 2;

        // 底环
        canvas.Children.Add(new Ellipse
        {
            Width = r * 2, Height = r * 2,
            StrokeThickness = thickness,
            Stroke = new SolidColorBrush(Grid),
            Fill = Brushes.Transparent
        });
        Canvas.SetLeft(canvas.Children[^1], cx - r);
        Canvas.SetTop(canvas.Children[^1], cy - r);

        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction <= 0) return;

        var arc = new Path
        {
            StrokeThickness = thickness,
            Stroke = new SolidColorBrush(Line),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        // 从顶部（-90°）顺时针
        double a = fraction >= 1 ? 359.99 : 360 * fraction;
        var rad = a * Math.PI / 180;
        var start = new Point(cx, cy - r);
        var end = new Point(cx + r * Math.Sin(rad), cy - r * Math.Cos(rad));
        var geo = new PathGeometry(new[]
        {
            new PathFigure(start, new[]
            {
                new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true)
            }, false)
        });
        arc.Data = geo;
        canvas.Children.Add(arc);
    }

    /// <summary>
    /// 环形占比图（任务完成度等）：全 0 时返回 false，调用方叠空状态文案。
    /// slices: (标签, 数值, 颜色)。
    /// </summary>
    public static bool DrawDonut(Canvas canvas,
        IReadOnlyList<(string Label, int Value, Color Color)> slices, double thickness = 22)
    {
        canvas.Children.Clear();
        double size = Math.Min(canvas.ActualWidth, canvas.ActualHeight);
        if (size <= 0 || slices.Count == 0) return false;
        int total = slices.Sum(s => s.Value);
        if (total <= 0) return false;

        double r = (size - thickness) / 2;
        double cx = canvas.ActualWidth / 2, cy = canvas.ActualHeight / 2;

        // 底环
        canvas.Children.Add(new Ellipse
        {
            Width = r * 2, Height = r * 2,
            StrokeThickness = thickness,
            Stroke = new SolidColorBrush(Grid),
            Fill = Brushes.Transparent
        });
        Canvas.SetLeft(canvas.Children[^1], cx - r);
        Canvas.SetTop(canvas.Children[^1], cy - r);

        double angle = 0; // 从顶部顺时针累计
        foreach (var (label, value, color) in slices)
        {
            if (value <= 0) continue;
            double sweep = 360.0 * value / total;
            var brush = new SolidColorBrush(color);

            if (sweep >= 359.9)
            {
                // 单一扇区占满：直接画整圆
                canvas.Children.Add(new Ellipse
                {
                    Width = r * 2, Height = r * 2,
                    StrokeThickness = thickness,
                    Stroke = brush,
                    Fill = Brushes.Transparent
                });
                Canvas.SetLeft(canvas.Children[^1], cx - r);
                Canvas.SetTop(canvas.Children[^1], cy - r);
            }
            else
            {
                var rad0 = angle * Math.PI / 180;
                var rad1 = (angle + sweep) * Math.PI / 180;
                var start = new Point(cx + r * Math.Sin(rad0), cy - r * Math.Cos(rad0));
                var end = new Point(cx + r * Math.Sin(rad1), cy - r * Math.Cos(rad1));
                canvas.Children.Add(new Path
                {
                    StrokeThickness = thickness,
                    Stroke = brush,
                    Data = new PathGeometry(new[]
                    {
                        new PathFigure(start, new[]
                        {
                            new ArcSegment(end, new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true)
                        }, false)
                    })
                });
            }
            angle += sweep;
        }
        return true;
    }

    /// <summary>
    /// 柱状图（labelStep 控制隔几个显示一次 X 轴标签）。
    /// v1.0.3：onBarClick 非空时柱子可点按，回调参数为柱下标（点按洞察用）。
    /// </summary>
    public static void DrawBar(Canvas canvas, IReadOnlyList<string> labels, IReadOnlyList<int> values,
        int labelStep = 1, Action<int>? onBarClick = null)
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
                RadiusX = 3, RadiusY = 3,
                Tag = i
            };
            if (onBarClick != null)
            {
                rect.Cursor = System.Windows.Input.Cursors.Hand;
                rect.MouseLeftButtonUp += (_, _) =>
                {
                    if (rect.Tag is int idx) onBarClick(idx);
                };
            }
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
