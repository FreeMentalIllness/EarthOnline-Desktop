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
        InitDataRoot();
        AppPaths.EnsureDirectories();

        // 外观设置（主题/壁纸/字号）在主窗口创建前应用，启动即所见
        try { ThemeService.ApplyAll(SettingsStore.Load()); }
        catch { /* 外观失败用默认 */ }

        try
        {
            // 首次启动建库并补齐 Profile 行；已存在则应用待执行的迁移
            using var db = new AppDbContext(AppPaths.DbFile);
            db.EnsureReady();
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

        // 自定义数据目录不可写回落：启动即温和提示并引导修改（不崩溃、不静默）
        if (DataRootNotice != null)
            (MainWindow as MainWindow)?.ShowSyncBanner(DataRootNotice, true);

        // 自动同步开启时：冷启动后台拉取云端更新（不阻塞窗口显示）
        var s = SettingsStore.Load();
        if (s.HasConfig && s.AutoSync)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var r = await SyncService.PullIfRemoteNewerAsync();
                    // 结果走主窗口顶部提示条（对齐 Android 离线横幅），不打断使用
                    if (r.Ok && r.Changed)
                        (MainWindow as MainWindow)?.ShowSyncBanner(r.Message, false);
                    else if (!r.Ok && r.Message != "云端还没有备份文件" && r.Message != "未配置 WebDAV")
                        (MainWindow as MainWindow)?.ShowSyncBanner(r.Message, true);
                }
                catch { /* 同步失败绝不影响启动 */ }
            });
        }
    }

    /// <summary>解析并设置数据目录（方案 A 便携优先）：显式设置 &gt; 应用根 EarthOnlineData（可写）&gt; %LOCALAPPDATA%\EarthOnline；并从旧目录一次性迁移数据。</summary>
    private static void InitDataRoot()
    {
        DataRootNotice = null;
        try
        {
            var s = SettingsStore.Load();
            var chosen = ResolveDataRoot(s.DataDirectory);
            AppPaths.DataRoot = chosen;
            MigrateFromLegacy(chosen);
        }
        catch
        {
            // 解析失败：AppPaths 已有 %LOCALAPPDATA%\EarthOnline 的保守默认，不阻塞启动
        }
    }

    /// <summary>启动回落提示（自定义目录不可写等原因），主窗口就绪后经提示条展示一次。</summary>
    public static string? DataRootNotice { get; private set; }

    private static string ResolveDataRoot(string dataDirectory)
    {
        // 1) 用户显式指定且可写
        if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            try
            {
                System.IO.Directory.CreateDirectory(dataDirectory);
                return dataDirectory;
            }
            catch
            {
                // 自定义目录不可写：记录友好提示后回落默认（启动后在提示条引导用户修改，不崩溃）
                DataRootNotice = "自定义数据目录不可写（权限不足或路径无效），本次启动已使用默认目录。" +
                                 "请在「设置 → 数据目录」改成一个可写的普通文件夹。";
            }
        }
        // 2) 默认：应用根目录下的 EarthOnlineData（便于随程序携带），可写时优先
        try
        {
            var candidate = System.IO.Path.Combine(System.AppContext.BaseDirectory, "EarthOnlineData");
            System.IO.Directory.CreateDirectory(candidate);
            return candidate;
        }
        catch { }
        // 3) 回落：%LOCALAPPDATA%\EarthOnline
        return System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "EarthOnline");
    }

    /// <summary>首次切换到新目录时，把旧 LOCALAPPDATA 里的数据库/头像/备份等一次性复制到新目录（不删旧文件）。</summary>
    private static void MigrateFromLegacy(string chosen)
    {
        var legacy = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "EarthOnline");
        if (string.Equals(legacy, chosen, System.StringComparison.OrdinalIgnoreCase)) return;
        if (!System.IO.Directory.Exists(legacy)) return;
        if (!System.IO.File.Exists(System.IO.Path.Combine(legacy, "earth_online.db"))) return; // 旧目录无数据不搬
        if (System.IO.File.Exists(System.IO.Path.Combine(chosen, "earth_online.db"))) return;   // 新目录已有数据不覆盖
        try
        {
            System.IO.Directory.CreateDirectory(chosen);
            // 递归复制（含 avatar / files / backups 子目录与数据库附属的 -wal / -shm 文件），保持相对结构
            foreach (var file in System.IO.Directory.EnumerateFiles(legacy, "*", System.IO.SearchOption.AllDirectories))
            {
                var name = System.IO.Path.GetFileName(file);
                // 设置文件恒留在应用配置目录，不随数据目录迁移
                if (name.Equals("settings.json", System.StringComparison.OrdinalIgnoreCase)) continue;
                var rel = System.IO.Path.GetRelativePath(legacy, file);
                var dest = System.IO.Path.Combine(chosen, rel);
                var destDir = System.IO.Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir)) System.IO.Directory.CreateDirectory(destDir);
                if (!System.IO.File.Exists(dest))
                    System.IO.File.Copy(file, dest, overwrite: false);
            }
        }
        catch { /* 迁移失败不阻塞启动 */ }
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
