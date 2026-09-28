using System.IO;
using System.Windows;
using System.Windows.Controls;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Services;
using Hardcodet.Wpf.TaskbarNotification;

namespace EarthOnline.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>是否真正退出（托盘「退出」置位；否则关闭窗口只是最小化到托盘）。</summary>
    public static bool ForceClose { get; set; }

    private TaskbarIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.EnsureDirectories();

        // 外观设置（主题/壁纸/字号）在主窗口创建前应用，启动即所见
        try { ThemeService.ApplyAll(SettingsStore.Load()); }
        catch { /* 外观失败用默认 */ }

        try
        {
            // 首次启动建库并补齐 Profile 行；已存在则应用待执行的迁移
            using var db = new AppDbContext(AppPaths.DbFile);
            db.EnsureCreated();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "本地数据库初始化失败：\n" + ex.Message,
                "地球Online",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        base.OnStartup(e);

        SetupTray();

        // 本地自动备份：监听落库信号，防抖留最近 3 份快照（对齐安卓 AutoBackupManager）
        try { AutoBackupService.Start(); }
        catch { /* 备份功能失败不影响使用 */ }

        // 自动同步开启时：冷启动后台拉取云端更新（不阻塞窗口显示）
        var s = SettingsStore.Load();
        if (s.HasConfig && s.AutoSync)
        {
            _ = Task.Run(async () =>
            {
                try { await SyncService.PullIfRemoteNewerAsync(); }
                catch { /* 同步失败绝不影响启动 */ }
            });
        }
    }

    /// <summary>构建系统托盘图标 + 右键菜单（Windows 专属常驻后台）。</summary>
    private void SetupTray()
    {
        _tray = new TaskbarIcon
        {
            Icon = LoadTrayIcon() ?? System.Drawing.SystemIcons.Application,
            ToolTipText = "地球Online（左键/双击显示，右键菜单）",
            Visibility = Visibility.Visible
        };

        var menu = new ContextMenu();
        var show = new MenuItem { Header = "显示主窗口" };
        show.Click += (_, _) => ShowMainWindow();
        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) =>
        {
            ForceClose = true;
            Shutdown();
        };
        menu.Items.Add(show);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);

        _tray.ContextMenu = menu;
        _tray.TrayMouseDoubleClick += (_, _) => ShowMainWindow();
    }

    /// <summary>从打包资源加载应用 logo 作为托盘图标（与移动端一致）；失败回落系统默认。</summary>
    private static System.Drawing.Icon? LoadTrayIcon()
    {
        try
        {
            var sri = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app_logo.ico"));
            if (sri?.Stream is null) return null;
            using var ms = new MemoryStream();
            sri.Stream.CopyTo(ms);
            ms.Position = 0;
            return new System.Drawing.Icon(ms);
        }
        catch { return null; }
    }

    /// <summary>把已隐藏/最小化的主窗口恢复到前台。</summary>
    public static void ShowMainWindow()
    {
        if (Current.MainWindow is MainWindow w)
        {
            w.Show();
            if (w.WindowState == WindowState.Minimized)
                w.WindowState = WindowState.Normal;
            w.Activate();
        }
    }

    /// <summary>弹一个托盘气泡提示（如「已最小化到托盘」）。</summary>
    public static void NotifyTray(string title, string message)
    {
        if (Current is App app && app._tray is not null)
        {
            app._tray.ShowBalloonTip(title, message, BalloonIcon.Info);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 退出时推一次本地最新存档（与安卓「切后台推送」一致），最多等 6 秒
        try
        {
            var s = SettingsStore.Load();
            if (s.HasConfig && s.AutoSync)
            {
                SyncService.PushAsync(force: true).Wait(TimeSpan.FromSeconds(6));
            }
        }
        catch
        {
            // 退出路径上吞掉所有异常，避免挡住关机流程
        }

        _tray?.Dispose();
        base.OnExit(e);
    }
}
