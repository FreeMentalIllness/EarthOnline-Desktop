using System.Windows;
using System.Windows.Controls;
using EarthOnline.Desktop.Pages;

namespace EarthOnline.Desktop;

/// <summary>
/// 主窗口：左侧导航（主页 / 任务 / 背包 / 成就 / 设置）+ 右侧内容区。
/// 窗口宽度不足时侧栏自动折叠为图标模式，保证小窗口 / 笔记本屏幕也不挤。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>侧栏是否处于「仅图标」紧凑模式（供 XAML 的 DataTrigger 使用）。</summary>
    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(MainWindow),
            new PropertyMetadata(false));

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>页面缓存：切换导航时不重建，保留页面内滚动位置与输入状态。</summary>
    private readonly Dictionary<string, Page> _pages = new();

    /// <summary>展开 / 紧凑侧栏的分界宽度（像素）。</summary>
    private const double CompactThreshold = 980;
    private const double ExpandedNavWidth = 200;
    private const double CompactNavWidth = 64;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ApplyLayoutMode();
            // XAML 解析期间 SelectedIndex 触发的首次导航会被 Navigate() 的 null 守卫吞掉，
            // 若此处不补一次，就会出现「侧栏选中项」与「右侧内容区」不一致（例如选中数据页却显示主页）。
            // 以侧栏选中项为准补一次导航，保证两者始终同步。
            Navigate(CurrentNavKey());
        };
    }

    /// <summary>当前侧栏选中项对应的页面 key（无选中时回落主页）。</summary>
    private string CurrentNavKey()
        => (NavList.SelectedItem as ListBoxItem)?.Tag?.ToString() ?? "home";

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyLayoutMode();
    }

    /// <summary>按当前窗口宽度切换侧栏形态。</summary>
    private void ApplyLayoutMode()
    {
        bool compact = ActualWidth < CompactThreshold;
        IsCompact = compact;
        NavColumn.Width = new GridLength(compact ? CompactNavWidth : ExpandedNavWidth);
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is ListBoxItem item)
        {
            Navigate(item.Tag?.ToString() ?? "home");
        }
    }

    /// <summary>切换到指定页面（不存在则创建并缓存）。</summary>
    private void Navigate(string key)
    {
        // XAML 解析期间 SelectedIndex="0" 会提前触发本方法，此时 Frame 尚未创建
        if (ContentFrame is null) return;

        if (!_pages.TryGetValue(key, out Page? page))
        {
            page = key switch
            {
                "tasks" => new TasksPage(),
                "backpack" => new BackpackPage(),
                "achievements" => new AchievementsPage(),
                "data" => new DataPage(),
                "report" => new ReportPage(),
                "settings" => new SettingsPage(),
                _ => new HomePage(),
            };
            _pages[key] = page;
        }

        ContentFrame.Navigate(page);
    }
}
