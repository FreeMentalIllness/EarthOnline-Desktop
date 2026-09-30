using System.IO;
using System.Security.Principal;
using System.Windows;

namespace EarthOnline.Setup;

public partial class App : System.Windows.Application
{
    /// <summary>安装选项（由命令行或向导填充）。</summary>
    public record InstallOptions(
        string InstallDir,
        bool Desktop,
        bool StartMenu,
        bool Taskbar,
        bool Silent,
        bool Elevated,
        bool AutoStart);

    internal static InstallOptions ParseArgs(string[] args)
    {
        var opts = new InstallOptions(
            InstallDir: string.Empty,
            Desktop: false,
            StartMenu: false,
            Taskbar: false,
            Silent: false,
            Elevated: false,
            AutoStart: false);

        bool anyFlag = false;
        foreach (var a in args)
        {
            var arg = a.Trim();
            var lower = arg.ToLowerInvariant();
            if (lower is "/silent" or "-silent" or "--silent") opts = opts with { Silent = true };
            else if (lower is "/elevated") opts = opts with { Elevated = true };
            else if (lower is "/autostart" or "-autostart") opts = opts with { AutoStart = true };
            else if (lower.StartsWith("/dir=")) opts = opts with { InstallDir = arg.Substring(5).Trim('"') };
            else if (lower is "/desktop") { opts = opts with { Desktop = true }; anyFlag = true; }
            else if (lower is "/startmenu") { opts = opts with { StartMenu = true }; anyFlag = true; }
            else if (lower is "/taskbar") { opts = opts with { Taskbar = true }; anyFlag = true; }
        }

        // 静默模式下若未显式指定任意快捷方式开关，则默认桌面+开始菜单
        if (opts.Silent && !anyFlag)
            opts = opts with { Desktop = true, StartMenu = true };

        if (string.IsNullOrWhiteSpace(opts.InstallDir))
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            opts = opts with { InstallDir = Path.Combine(local, "EarthOnline") };
        }

        // 与向导路径逻辑保持一致：盘符根/任意目录统一归一化为专属文件夹
        opts = opts with { InstallDir = SetupManager.NormalizeInstallDir(opts.InstallDir) };

        return opts;
    }

    internal static bool IsAdministrator()
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>受保护目录（写入需要管理员权限）。</summary>
    internal static bool NeedsAdmin(string dir)
    {
        var upper = dir.Replace('/', '\\').ToLowerInvariant();
        var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            .Replace('/', '\\').ToLowerInvariant();
        var progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            .Replace('/', '\\').ToLowerInvariant();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            .Replace('/', '\\').ToLowerInvariant();
        return upper.StartsWith(progFiles) || upper.StartsWith(progFilesX86) || upper.StartsWith(windows);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = e.Args;

        // /uninstall 模式（优先处理）
        bool isUninstall = false;
        foreach (var a in args)
        {
            var lower = a.Trim().ToLowerInvariant();
            if (lower is "/uninstall" or "-uninstall") isUninstall = true;
        }

        if (isUninstall)
        {
            RunUninstall();
            return;
        }

        var opts = ParseArgs(args);

        if (opts.Silent)
        {
            // 静默模式：不显示窗口，直接执行安装并退出
            // 注意必须在无同步上下文的线程池线程上执行，否则 await 续体
            // 投递回被阻塞的 UI 线程会造成死锁（安装永久卡在第一步 IO）
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var code = Task.Run(() => SetupManager.RunSilent(opts)).GetAwaiter().GetResult();
            Environment.Exit(code);
            return;
        }

        var win = new MainWindow(opts);
        MainWindow = win;
        win.Show();
    }

    private void RunUninstall()
    {
        var dir = GetInstallDirFromRegistry();

        // 兜底：注册表缺失时，用卸载器自身所在目录（内含主程序才可信）
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            var self = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (File.Exists(Path.Combine(self, "EarthOnline.exe")))
                dir = self;
        }

        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            System.Windows.Forms.MessageBox.Show(
                "未找到安装记录，无法执行卸载。",
                "卸载失败",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Error);
            Shutdown();
            return;
        }

        UninstallManager.Uninstall(dir);
        Shutdown();
    }

    private static string? GetInstallDirFromRegistry()
    {
        try
        {
            using var reg = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\EarthOnline");
            return reg?.GetValue("InstallDir") as string;
        }
        catch
        {
            return null;
        }
    }
}
