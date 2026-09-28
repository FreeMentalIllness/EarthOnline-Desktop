using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Services;

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

    /// <summary>日历当前停留的月份（恒为该月 1 号）。</summary>
    private DateTime _monthCursor = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public DataPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            BuildWeekdayHeader();
            Reload();
        };
    }

    // ==================== 载入 ====================

    private void Reload()
    {
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

            DrawTrend();
            MonthLabel.Text = _monthCursor.ToString("yyyy 年 M 月", CultureInfo.InvariantCulture);
            BuildCalendar();
        }
        catch (Exception ex)
        {
            MessageBox.Show("加载数据失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
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
            Background = Brush("#FFFFFF"),
            BorderBrush = Brush("#E8E2DA"),
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
                Foreground = Brush("#7A7268")
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

        for (int d = 1; d <= days; d++)
        {
            int idx = offset + d - 1;
            int row = idx / 7;
            int col = idx % 7;

            var day = new DateTime(first.Year, first.Month, d);
            string key = day.ToString("yyyy-MM-dd");
            int count = _doneByDay.TryGetValue(key, out var c) ? c : 0;

            var cell = new Border
            {
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Background = count > 0 ? Brush("#F5E9DC") : Brush("#FAF8F5"),
                BorderBrush = key == todayKey ? Brush("#D4A373") : Brush("#E8E2DA"),
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
                Foreground = Brush("#1E1A16")
            });
            stack.Children.Add(new TextBlock
            {
                Text = count > 0 ? "✓ " + count.ToString(CultureInfo.InvariantCulture) : "",
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = Brush("#D4A373")
            });
            cell.Child = stack;

            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            DayGrid.Children.Add(cell);
        }
    }

    private void ShowDayDetail(string key)
    {
        var titles = _tasks
            .Where(t => !string.IsNullOrEmpty(t.DoneAt) && StatsService.DayKeyOf(t.DoneAt) == key)
            .Select(t => string.IsNullOrWhiteSpace(t.Title) ? "（未命名任务）" : t.Title)
            .ToList();

        DayDetail.Text = titles.Count == 0
            ? $"{key} 没有完成的任务。"
            : $"{key} 完成 {titles.Count} 个任务：" + string.Join("、", titles);
    }

    // ==================== 交互 ====================

    private void View_Checked(object sender, RoutedEventArgs e)
    {
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

    private static SolidColorBrush Brush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex));
}
