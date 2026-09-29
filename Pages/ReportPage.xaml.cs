using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EarthOnline.Desktop.Services;

namespace EarthOnline.Desktop.Pages;

/// <summary>
/// 周期性报告页（日报 / 周报 / 年报）。
/// 对应安卓 ReportViewModel + ReportScreen；区间口径与 XP 规则见 ReportService / XpRules。
/// </summary>
public partial class ReportPage : Page
{
    private ReportData? _data;

    /// <summary>标记页面是否已 Loaded：避免 XAML 中 RadioButton 的 IsChecked="True" 在
    /// InitializeComponent 期间触发 Checked 事件时，后续控件尚未创建而空引用。</summary>
    private bool _loaded;

    public ReportPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _loaded = true;
            Reload();
        };
    }

    private void Reload()
    {
        // 防御：控件未就绪时直接返回，杜绝 NullReferenceException
        if (RangeLabel == null || ChartTitle == null || SummaryText == null ||
            MetricStats == null || HighlightList == null || HighlightCard == null ||
            HighlightEmpty == null || ReportChart == null || ChartEmpty == null) return;

        try
        {
            var kind = CurrentKind();
            _data = ReportService.Build(kind);
            Render();
        }
        catch (Exception ex)
        {
            MessageBox.Show("生成报告失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private ReportKind CurrentKind()
    {
        if (KindYear?.IsChecked == true) return ReportKind.Year;
        if (KindWeek?.IsChecked == true) return ReportKind.Week;
        return ReportKind.Day;
    }

    private void Render()
    {
        var d = _data;
        if (d is null) return;

        RangeLabel.Text = d.RangeLabel;
        ChartTitle.Text = d.ChartTitle;
        SummaryText.Text = d.Summary;

        MetricStats.Children.Clear();
        MetricStats.Children.Add(MetricCard("完成任务", d.TasksDone));
        MetricStats.Children.Add(MetricCard("灵感", d.Memos));
        MetricStats.Children.Add(MetricCard("成就", d.Achievements));
        MetricStats.Children.Add(MetricCard("足迹", d.Locations));
        MetricStats.Children.Add(MetricCard("经验", d.Xp));

        HighlightList.Children.Clear();
        foreach (var h in d.Highlights)
        {
            HighlightList.Children.Add(new TextBlock
            {
                Text = h,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 6),
                Foreground = Brush("#1E1A16"),
                TextWrapping = TextWrapping.Wrap
            });
        }
        HighlightEmpty.Visibility = d.Highlights.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // 年报不展示亮点（与安卓一致），整卡隐藏
        HighlightCard.Visibility = d.Kind == ReportKind.Year ? Visibility.Collapsed : Visibility.Visible;

        DrawChart();
    }

    private void DrawChart()
    {
        var d = _data;
        if (d is null || ReportChart is null || ChartEmpty is null) return;

        if (d.IsBar)
        {
            // v1.0.3：柱子可点按 → 弹出该时段的记录明细（点按洞察）
            ChartRenderer.DrawBar(ReportChart, d.ChartLabels, d.ChartValues,
                d.Kind == ReportKind.Day ? 3 : 1, OnBarClick);
        }
        else
        {
            ChartRenderer.DrawLine(ReportChart, d.ChartLabels, d.ChartValues);
        }

        int total = 0;
        foreach (var v in d.ChartValues) total += v;
        ChartEmpty.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReportChart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    // ==================== 点按洞察（v1.0.3） ====================

    /// <summary>点按柱状图某柱 → 弹出该时段（日报=某小时 / 年报=某月）的记录明细窗口。</summary>
    private void OnBarClick(int bucketIndex)
    {
        var kind = CurrentKind();
        string header;
        try
        {
            header = kind switch
            {
                ReportKind.Year => $"{DateTime.Today.Year} 年 {bucketIndex + 1} 月的记录",
                ReportKind.Week => "这一格没有柱子",
                _ => $"今天 {bucketIndex:00}:00 – {(bucketIndex + 1) % 24:00}:00 的记录"
            };
            var rows = ReportService.BucketDetail(kind, bucketIndex);

            var win = new Window
            {
                Title = "记录明细",
                Width = 440,
                SizeToContent = SizeToContent.Height,
                MaxHeight = 520,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#F8F6F2"),
                Owner = Application.Current?.MainWindow
            };

            var root = new StackPanel { Margin = new Thickness(20) };
            root.Children.Add(new TextBlock
            {
                Text = header,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#1E1A16"),
                Margin = new Thickness(0, 0, 0, 10)
            });

            if (rows.Count == 0)
            {
                root.Children.Add(new TextBlock
                {
                    Text = "这一格还没有记录。留下一条，柱子就会长出来 🌱",
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush("#7A7268")
                });
            }
            else
            {
                var list = new ScrollViewer
                {
                    MaxHeight = 340,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };
                var panel = new StackPanel();
                foreach (var r in rows)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    row.Children.Add(new TextBlock
                    {
                        Text = r.Emoji, FontSize = 13, Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Top
                    });
                    row.Children.Add(new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(r.Text) ? "（无内容）" : r.Text,
                        FontSize = 13, MaxWidth = 260,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brush("#1E1A16")
                    });
                    row.Children.Add(new TextBlock
                    {
                        Text = r.TimeText, FontSize = 11, Margin = new Thickness(10, 2, 0, 0),
                        VerticalAlignment = VerticalAlignment.Top,
                        Foreground = Brush("#B0A89C")
                    });
                    panel.Children.Add(row);
                }
                list.Content = panel;
                root.Children.Add(list);
            }

            var closeBtn = new Button
            {
                Content = "关闭", MinWidth = 84, Height = 32, Margin = new Thickness(0, 14, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = Brush("#D4A373"), Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand
            };
            closeBtn.Click += (_, _) => win.Close();
            root.Children.Add(closeBtn);

            win.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            win.ShowDialog();
        }
        catch { /* 洞察属锦上添花，失败不打断报告页 */ }
    }

    // ==================== 交互 ====================

    private void Kind_Checked(object sender, RoutedEventArgs e)
    {
        // 页面未加载完（InitializeComponent 期间）不处理，等 Loaded 后的首次 Reload 统一构建
        if (!_loaded) return;
        // XAML 解析期间 IsChecked 会提前触发，此时 ReportChart 等尚未创建
        if (ReportChart is null) return;
        Reload();
    }

    // ==================== 小工具 ====================

    private static Border MetricCard(string label, int value)
    {
        // 直接写属性而非 FindResource：不依赖 Application.Current，隔离场景也不会空引用
        var border = new Border
        {
            Background = Brush("#FFFFFF"),
            BorderBrush = Brush("#E8E2DA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12, 14, 12),
            Width = 112,
            Margin = new Thickness(0, 0, 10, 10)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = value.ToString(CultureInfo.InvariantCulture),
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#1E1A16"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = Brush("#7A7268"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        });
        border.Child = stack;
        return border;
    }

    private static SolidColorBrush Brush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex));
}
