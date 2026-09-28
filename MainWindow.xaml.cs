using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Dialogs;
using EarthOnline.Desktop.Pages;
using EarthOnline.Desktop.Services;

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

    /// <summary>用户手动收起侧栏后的覆盖状态；null = 跟随窗口宽度自动判定。</summary>
    private bool? _manualCollapsed = null;

    /// <summary>侧栏宽度的动画代理属性：BeginAnimation 驱动 NavColumn 平滑伸缩，避免硬跳变。</summary>
    public static readonly DependencyProperty NavWidthProxyProperty =
        DependencyProperty.Register(nameof(NavWidthProxy), typeof(double), typeof(MainWindow),
            new PropertyMetadata(200d, (o, e) =>
            {
                var w = (MainWindow)o;
                if (w.NavColumn != null)
                    w.NavColumn.Width = new GridLength((double)e.NewValue);
            }));

    public double NavWidthProxy
    {
        get => (double)GetValue(NavWidthProxyProperty);
        set => SetValue(NavWidthProxyProperty, value);
    }

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Loaded += (_, _) =>
        {
            ApplyLayoutMode();
            // 启动后应用持久化的字号缩放（窗口就绪才可设 LayoutTransform）
            try { ThemeService.ApplyFontScale(SettingsStore.Load().FontScale); }
            catch { /* 外观失败用默认 */ }
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

    /// <summary>按当前窗口宽度（或被用户手动覆盖）切换侧栏形态。</summary>
    private void ApplyLayoutMode()
    {
        bool compact = _manualCollapsed ?? (ActualWidth < CompactThreshold);
        if (IsCompact == compact) return;
        IsCompact = compact;
        AnimateNavWidth(compact ? CompactNavWidth : ExpandedNavWidth);
    }

    /// <summary>平滑伸缩侧栏（180ms 缓出），保证跟手且不卡顿。</summary>
    private void AnimateNavWidth(double to)
    {
        BeginAnimation(NavWidthProxyProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    /// <summary>侧栏收起/展开开关：手动覆盖自动判定，图标随之翻转。</summary>
    private void ToggleNavBtn_Click(object sender, RoutedEventArgs e)
    {
        _manualCollapsed = !(_manualCollapsed ?? IsCompact);
        ApplyLayoutMode();
        if (ToggleIcon != null)
            ToggleIcon.Text = _manualCollapsed == true ? "⟩" : "⟨";
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
                "map" => new MapPage(),
                "data" => new DataPage(),
                "report" => new ReportPage(),
                "settings" => new SettingsPage(),
                "ai" => new AiPage(),
                _ => new HomePage(),
            };
            _pages[key] = page;
        }

        ContentFrame.Navigate(page);
        PlayEnterAnimation();
    }

    /// <summary>内容区进入动画：淡入 + 轻微上移（160ms 缓出），提供切换反馈而不牺牲性能。</summary>
    private void PlayEnterAnimation()
    {
        if (ContentFrame == null) return;
        ContentFrame.Opacity = 0;
        ContentFrame.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        var tt = new TranslateTransform(0, 10);
        ContentFrame.RenderTransform = tt;
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    /// <summary>供页面（如主页快速入口）触发导航。</summary>
    public void NavigateTo(string key) => Navigate(key);

    /// <summary>
    /// 清空页面缓存并重导航当前页（壁纸 / 主题大改后让内容区完全重建）。
    /// 供设置页外观切换后调用。
    /// </summary>
    public void RefreshCurrentPage()
    {
        _pages.Clear();
        Navigate(CurrentNavKey());
    }

    // ==================== 键盘快捷键 ====================

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control))
        {
            switch (e.Key)
            {
                case Key.S:
                    e.Handled = true;
                    _ = SaveShortcutAsync();
                    break;
                case Key.D1 or Key.NumPad1: e.Handled = true; NavList.SelectedIndex = 0; break;
                case Key.D2 or Key.NumPad2: e.Handled = true; NavList.SelectedIndex = 1; break;
                case Key.D3 or Key.NumPad3: e.Handled = true; NavList.SelectedIndex = 2; break;
                case Key.D4 or Key.NumPad4: e.Handled = true; NavList.SelectedIndex = 3; break;
                case Key.D5 or Key.NumPad5: e.Handled = true; NavList.SelectedIndex = 4; break;
                case Key.D6 or Key.NumPad6: e.Handled = true; NavList.SelectedIndex = 5; break;
                case Key.D7 or Key.NumPad7: e.Handled = true; NavList.SelectedIndex = 6; break;
                case Key.D8 or Key.NumPad8: e.Handled = true; NavList.SelectedIndex = 7; break;
                case Key.D9 or Key.NumPad9: e.Handled = true; NavList.SelectedIndex = 8; break;
            }
        }
    }

    /// <summary>Ctrl+S：配置了 WebDAV 就立即推送，否则提示数据已即时落盘。</summary>
    private static async Task SaveShortcutAsync()
    {
        try
        {
            var s = SettingsStore.Load();
            if (s.HasConfig)
            {
                await SyncService.PushAsync();
                UnlockToast.Show("已推送到云端", s.EffectiveRemotePath());
            }
            else
            {
                UnlockToast.Show("已保存", "数据实时写入本地数据库，无需手动保存");
            }
        }
        catch (Exception ex)
        {
            UnlockToast.Show("同步失败", ex.Message);
        }
    }

    /// <summary>关闭窗口不直接退出，而是最小化到系统托盘常驻后台（除非点了托盘「退出」）。</summary>
    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!App.ForceClose)
        {
            e.Cancel = true;
            Hide();
            App.NotifyTray("地球Online", "已最小化到系统托盘，常驻后台");
        }
    }
}
