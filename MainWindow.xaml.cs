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
            RestoreWindowGeometry();
            ApplyLayoutMode();
            // 启动后应用持久化的字号缩放（窗口就绪才可设 LayoutTransform）
            try { ThemeService.ApplyFontScale(SettingsStore.Load().FontScale); }
            catch { /* 外观失败用默认 */ }
            // XAML 解析期间 SelectedIndex 触发的首次导航会被 Navigate() 的 null 守卫吞掉，
            // 若此处不补一次，就会出现「侧栏选中项」与「右侧内容区」不一致（例如选中数据页却显示主页）。
            // 以侧栏选中项为准补一次导航，保证两者始终同步。
            Navigate(CurrentNavKey());

            // 首次启动先看引导（只看一次；设置页可重新唤起）
            try
            {
                if (!SettingsStore.Load().Onboarded) Navigate("onboarding");
            }
            catch { /* 标记读取失败就当已引导，绝不拦住用户 */ }
        };
    }

    /// <summary>引导完成：标记已引导并回到主页。</summary>
    public void FinishOnboarding()
    {
        try
        {
            var s = SettingsStore.Load();
            s.Onboarded = true;
            s.Save();
        }
        catch { /* 标记失败最多是下次再看一次 */ }

        // 丢弃引导页缓存：设置页「重新查看引导」时才是全新第 1 步，而不是停在结束态
        _pages.Remove("onboarding");

        // 必须显式导航回主页，不能只设 NavList.SelectedIndex = 0：
        // 首次启动引导时侧栏本来就选中第 0 项，索引没变不会触发 SelectionChanged，
        // 结果内容区仍停留在引导页（引导「完成」了却回不去）。
        if (NavList.Items.Count > 0) NavList.SelectedIndex = 0;
        Navigate("home");
    }

    /// <summary>当前侧栏选中项对应的页面 key（无选中时回落主页）。</summary>
    private string CurrentNavKey()
        => (NavList.SelectedItem as ListBoxItem)?.Tag?.ToString() ?? "home";

    // ==================== 窗口几何记忆（v1.0.4） ====================

    /// <summary>启动时恢复上次的位置 / 尺寸 / 最大化状态；越界（换显示器、分辨率变小）时自动回正。</summary>
    private void RestoreWindowGeometry()
    {
        try
        {
            var s = SettingsStore.Load();
            _manualCollapsed = s.NavCollapsed ? true : null;   // false 不记忆：留 null 让宽度自动判定

            if (s.WindowWidth > 0 && s.WindowHeight > 0)
            {
                Width = Math.Clamp(s.WindowWidth, MinWidth, SystemParameters.VirtualScreenWidth);
                Height = Math.Clamp(s.WindowHeight, MinHeight, SystemParameters.VirtualScreenHeight);
            }
            if (s.WindowLeft >= 0 && s.WindowTop >= 0 && FitsOnScreen(s.WindowLeft, s.WindowTop, Width, Height))
            {
                Left = s.WindowLeft;
                Top = s.WindowTop;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }
            if (s.WindowMaximized) WindowState = WindowState.Maximized;
        }
        catch
        {
            // 几何恢复失败就用 XAML 默认尺寸，绝不影响启动
        }
    }

    /// <summary>窗口是否整体落在虚拟屏幕内（避免恢复后跑到屏幕外看不见）。</summary>
    private static bool FitsOnScreen(double left, double top, double w, double h)
    {
        double vx = SystemParameters.VirtualScreenLeft;
        double vy = SystemParameters.VirtualScreenTop;
        double vw = SystemParameters.VirtualScreenWidth;
        double vh = SystemParameters.VirtualScreenHeight;
        if (vw <= 0 || vh <= 0) return true;
        return left >= vx - 1 && top >= vy - 1 && left + w <= vx + vw + 1 && top + h <= vy + vh + 1;
    }

    /// <summary>把当前位置 / 尺寸 / 最大化状态写入设置（关闭窗口时落盘，不做实时写入）。</summary>
    private void SaveWindowGeometry()
    {
        try
        {
            var s = SettingsStore.Load();
            var b = WindowState == WindowState.Minimized ? RestoreBounds : new Rect(Left, Top, Width, Height);
            if (b.Width > 0 && b.Height > 0)
            {
                s.WindowLeft = b.Left;
                s.WindowTop = b.Top;
                s.WindowWidth = b.Width;
                s.WindowHeight = b.Height;
            }
            s.WindowMaximized = WindowState == WindowState.Maximized;
            s.NavCollapsed = _manualCollapsed == true;
            s.Save();
        }
        catch
        {
            // 保存失败不影响退出
        }
    }

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
        // 折叠状态即时落盘（与窗口几何同一份设置，关窗口那次保存会一并带上）
        try
        {
            var s = SettingsStore.Load();
            s.NavCollapsed = _manualCollapsed == true;
            s.Save();
        }
        catch { /* 记忆失败不影响使用 */ }
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
                "onboarding" => new OnboardingPage { OnFinished = FinishOnboarding },
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

    /// <summary>关闭窗口行为由设置决定：直接退出放行；否则最小化到系统托盘常驻后台（除非点了托盘「退出」）。</summary>
    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        // 无论这次关闭是「真退出」还是「最小化到托盘」，当前几何都值得记住
        SaveWindowGeometry();

        bool exit = App.ForceClose;
        if (!exit)
        {
            try
            {
                exit = string.Equals(SettingsStore.Load().ExitBehavior, "exit", StringComparison.OrdinalIgnoreCase);
            }
            catch { /* 读取失败回落最小化到托盘 */ }
        }
        if (!exit)
        {
            e.Cancel = true;
            Hide();
            App.NotifyTray("地球Online", "已最小化到系统托盘，常驻后台");
        }
    }
}
