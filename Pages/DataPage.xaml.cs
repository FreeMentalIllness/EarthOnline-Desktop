using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Services;
using EarthOnline.Desktop.Dialogs;

namespace EarthOnline.Desktop.Pages;

/// <summary>
/// 数据看板（对应网页 renderDashboard / modules/stats.js）。
/// 两个子视图：数据概览（周期汇总 + 趋势图）、日历视图（按月看每天完成任务数）。
/// 聚合口径与网页、安卓完全一致，见 StatsService 注释。
/// </summary>
public partial class DataPage : Page
{
    private List<TaskEntity> _tasks = new();
    private List<MemoEntity> _memos = new();
    private List<AchievementEntity> _achs = new();
    private Dictionary<string, int> _doneByDay = new();

    /// <summary>标记页面是否已 Loaded：避免 XAML 中 RadioButton 的 IsChecked="True" 在
    /// InitializeComponent 期间触发 Checked 事件时，后续控件尚未创建而空引用。</summary>
    private bool _loaded;

    /// <summary>日历当前停留的月份（恒为该月 1 号）。</summary>
    private DateTime _monthCursor = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public DataPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            BuildWeekdayHeader();
            _loaded = true;
            Reload();
        };
    }

    // ==================== 载入 ====================

    private void Reload()
    {
        // 防御：控件未就绪时直接返回，杜绝 NullReferenceException
        if (WeekStats == null || MonthStats == null || YearStats == null ||
            YearTitle == null || TrendChart == null || TrendEmpty == null ||
            MonthLabel == null || DayGrid == null || DayDetail == null) return;

        try
        {
            var (tasks, memos, achs) = StatsService.LoadAll();
            _tasks = tasks;
            _memos = memos;
            _achs = achs;
            _doneByDay = StatsService.DoneCountByDay(_tasks);

            var today = StatsService.TodayStr();
            FillStats(WeekStats, StatsService.BuildRangeSummary(_tasks, _memos, _achs, StatsService.DayOffset(-6), today));
            FillStats(MonthStats, StatsService.BuildRangeSummary(_tasks, _memos, _achs, StatsService.DayOffset(-29), today));

            int year = DateTime.Today.Year;
            YearTitle.Text = year + " 年";
            FillStats(YearStats, StatsService.BuildYearSummary(_tasks, _memos, _achs, year));

            DrawStatusDonut();
            DrawTrend();
            MonthLabel.Text = _monthCursor.ToString("yyyy 年 M 月", CultureInfo.InvariantCulture);
            BuildCalendar();
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("加载数据失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ==================== 指标卡 ====================

    private static void FillStats(WrapPanel panel, RangeSummary s)
    {
        panel.Children.Clear();
        panel.Children.Add(StatCard("新增任务", s.NewTasks));
        panel.Children.Add(StatCard("完成任务", s.DoneTasks));
        panel.Children.Add(StatCard("新增灵感", s.NewMemos));
        panel.Children.Add(StatCard("解锁成就", s.NewAchievements));
    }

    private static Border StatCard(string label, int value)
    {
        // 直接写属性而非 FindResource：不依赖 Application.Current，单测/隔离场景也不会空引用
        var border = new Border
        {
            // 随主题取值：深色模式下卡片/描边跟着变，不再出现白块（不依赖 FindResource，隔离场景也安全）
            Background = ThemeService.Brush("CardBgBrush"),
            BorderBrush = ThemeService.Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12, 14, 12),
            Width = 128,
            Margin = new Thickness(0, 0, 10, 10)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = value.ToString(CultureInfo.InvariantCulture),
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeService.Brush("TextPrimaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = ThemeService.Brush("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        });
        border.Child = stack;
        return border;
    }

    // ==================== 任务完成度环形图 ====================

    /// <summary>状态配色（存画笔键而非硬编码色值，深色模式下由 ThemeService 给出另一套）。</summary>
    private static readonly (string Status, string Label, string BrushKey)[] StatusSlices =
    {
        ("done", "已完成", "StatusDoneBrush"),
        ("active", "进行中", "StatusActiveBrush"),
        ("paused", "已暂停", "StatusPausedBrush"),
        ("planning", "规划中", "StatusPlanningBrush")
    };

    private void DrawStatusDonut()
    {
        if (StatusDonut is null || DonutLegend is null || DonutEmpty is null || DonutCenterNum is null) return;

        var slices = new List<(string, int, Color)>();
        int total = 0;
        foreach (var (status, label, brushKey) in StatusSlices)
        {
            int n = _tasks.Count(t => t.Status == status);
            total += n;
            if (n > 0) slices.Add((label, n, ThemeService.ColorOf(brushKey)));
        }

        bool drew = ChartRenderer.DrawDonut(StatusDonut, slices);
        DonutEmpty.Visibility = drew ? Visibility.Collapsed : Visibility.Visible;
        DonutCenterNum.Text = total.ToString(CultureInfo.InvariantCulture);

        DonutLegend.Children.Clear();
        if (!drew) return;

        foreach (var (status, label, brushKey) in StatusSlices)
        {
            int n = _tasks.Count(t => t.Status == status);
            if (n <= 0) continue;

            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 18, 4) };
            item.Children.Add(new Border
            {
                Width = 12, Height = 12, CornerRadius = new CornerRadius(3),
                Background = ThemeService.Brush(brushKey),
                VerticalAlignment = VerticalAlignment.Center
            });
            item.Children.Add(new TextBlock
            {
                Text = $" {label} {n}",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeService.Brush("TextPrimaryBrush")
            });
            DonutLegend.Children.Add(item);
        }
    }

    private void StatusDonut_SizeChanged(object sender, SizeChangedEventArgs e) => DrawStatusDonut();

    // ==================== 趋势图 ====================

    private void DrawTrend()
    {
        if (TrendChart is null || TrendEmpty is null) return;

        string range = Range4w?.IsChecked == true ? "4w" : "7d";
        var series = StatsService.BuildTrendSeries(_tasks, range);

        ChartRenderer.DrawLine(TrendChart, series.Labels, series.Values);
        TrendEmpty.Visibility = series.Total == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TrendChart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawTrend();

    // ==================== 日历 ====================

    private static readonly string[] WeekdayNames = { "日", "一", "二", "三", "四", "五", "六" };

    private void BuildWeekdayHeader()
    {
        WeekdayHeader.Children.Clear();
        WeekdayHeader.ColumnDefinitions.Clear();
        for (int i = 0; i < 7; i++)
        {
            WeekdayHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tb = new TextBlock
            {
                Text = WeekdayNames[i],
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 12,
                Foreground = ThemeService.Brush("TextSecondaryBrush")
            };
            Grid.SetColumn(tb, i);
            WeekdayHeader.Children.Add(tb);
        }
    }

    private void BuildCalendar()
    {
        DayGrid.Children.Clear();
        DayGrid.RowDefinitions.Clear();
        DayGrid.ColumnDefinitions.Clear();

        for (int i = 0; i < 7; i++)
        {
            DayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var first = new DateTime(_monthCursor.Year, _monthCursor.Month, 1);
        int offset = (int)first.DayOfWeek;                       // 周日 = 0
        int days = DateTime.DaysInMonth(first.Year, first.Month);
        int rows = (int)Math.Ceiling((offset + days) / 7.0);
        for (int r = 0; r < rows; r++)
        {
            DayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
        }

        var todayKey = StatsService.TodayStr();
        var dueByDay = _tasks
            .Where(t => t.Status != "done" && !string.IsNullOrEmpty(t.DueDate))
            .GroupBy(t => t.DueDate!.Substring(0, 10))
            .ToDictionary(g => g.Key, g => g.Count());

        for (int d = 1; d <= days; d++)
        {
            int idx = offset + d - 1;
            int row = idx / 7;
            int col = idx % 7;

            var day = new DateTime(first.Year, first.Month, d);
            string key = day.ToString("yyyy-MM-dd");
            int count = _doneByDay.TryGetValue(key, out var c) ? c : 0;

            // 热图 3 档：1 → 浅琥珀，2-3 → 中琥珀，4+ → 深琥珀（0 = 空格）
            string heatKey = count switch
            {
                >= 4 => "Heat3Brush",
                >= 2 => "Heat2Brush",
                >= 1 => "Heat1Brush",
                _ => "Heat0Brush"
            };
            int due = dueByDay.TryGetValue(key, out var dd) ? dd : 0;

            var cell = new Border
            {
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Background = ThemeService.Brush(heatKey),
                BorderBrush = key == todayKey ? ThemeService.Brush("AccentBrush") : ThemeService.Brush("BorderBrush"),
                BorderThickness = key == todayKey ? new Thickness(2) : new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = key
            };
            cell.MouseLeftButtonUp += (_, _) => ShowDayDetail(key);

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = d.ToString(CultureInfo.InvariantCulture),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 14,
                Foreground = ThemeService.Brush("TextPrimaryBrush")
            });
            stack.Children.Add(new TextBlock
            {
                Text = count > 0 ? "✓ " + count.ToString(CultureInfo.InvariantCulture) : "",
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = ThemeService.Brush("AccentBrush")
            });
            // 截止日红点（当天有未完成任务到期）
            if (due > 0)
            {
                stack.Children.Add(new Ellipse
                {
                    Width = 6, Height = 6,
                    Fill = ThemeService.Brush("DueDotBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
            cell.Child = stack;

            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            DayGrid.Children.Add(cell);
        }
    }

    private void ShowDayDetail(string key)
    {
        var doneTitles = _tasks
            .Where(t => !string.IsNullOrEmpty(t.DoneAt) && StatsService.DayKeyOf(t.DoneAt) == key)
            .Select(t => string.IsNullOrWhiteSpace(t.Title) ? "（未命名任务）" : t.Title)
            .ToList();
        var dueTitles = _tasks
            .Where(t => t.Status != "done" && (t.DueDate ?? "").StartsWith(key))
            .Select(t => string.IsNullOrWhiteSpace(t.Title) ? "（未命名任务）" : t.Title)
            .ToList();

        var parts = new List<string>();
        parts.Add(doneTitles.Count == 0
            ? "没有完成的任务"
            : $"完成 {doneTitles.Count} 个：" + string.Join("、", doneTitles));
        if (dueTitles.Count > 0)
            parts.Add($"📌 到期 {dueTitles.Count} 个：" + string.Join("、", dueTitles));

        DayDetail.Text = $"{key} " + string.Join("；", parts);
    }

    // ==================== 交互 ====================

    private void View_Checked(object sender, RoutedEventArgs e)
    {
        // 页面未加载完（InitializeComponent 期间）不处理，等 Loaded 后的首次 Reload 统一刷新
        if (!_loaded) return;
        // XAML 解析期间 IsChecked 会提前触发，此时后续元素尚未创建
        if (OverviewPanel is null || CalendarPanel is null || TrendBar is null || MonthBar is null) return;

        bool calendar = ViewCalendar?.IsChecked == true;
        OverviewPanel.Visibility = calendar ? Visibility.Collapsed : Visibility.Visible;
        CalendarPanel.Visibility = calendar ? Visibility.Visible : Visibility.Collapsed;
        TrendBar.Visibility = calendar ? Visibility.Collapsed : Visibility.Visible;
        MonthBar.Visibility = calendar ? Visibility.Visible : Visibility.Collapsed;

        if (calendar) DrawTrendOrSkip();
    }

    /// <summary>概览隐藏时 Canvas 尺寸为 0，切回来要重画一次。</summary>
    private void DrawTrendOrSkip()
    {
        if (OverviewPanel?.Visibility == Visibility.Visible) DrawTrend();
    }

    private void Range_Checked(object sender, RoutedEventArgs e)
    {
        // 页面未加载完（InitializeComponent 期间）不处理
        if (!_loaded) return;
        if (TrendChart is null) return;
        DrawTrend();
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e) => ShiftMonth(-1);

    private void NextMonth_Click(object sender, RoutedEventArgs e) => ShiftMonth(1);

    private void ThisMonth_Click(object sender, RoutedEventArgs e)
    {
        _monthCursor = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        MonthLabel.Text = _monthCursor.ToString("yyyy 年 M 月", CultureInfo.InvariantCulture);
        BuildCalendar();
    }

    private void ShiftMonth(int delta)
    {
        _monthCursor = _monthCursor.AddMonths(delta);
        MonthLabel.Text = _monthCursor.ToString("yyyy 年 M 月", CultureInfo.InvariantCulture);
        BuildCalendar();
    }

}
