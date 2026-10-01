using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace EarthOnline.Setup;

/// <summary>安装进度汇报单元。</summary>
public class InstallProgress
{
    public string? Message { get; init; }
    public double? Percent { get; init; }   // 0..100，null 表示不确定进度
    public bool Done { get; init; }
    public bool Failed { get; init; }
}

public static class SetupManager
{
    private const string ResourceSuffix = "EarthOnline.exe";
    private const string AppName = "地球Online";
    private const string RegKeyPath = @"Software\EarthOnline";
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "EarthOnline-Setup.log");

    /// <summary>
    /// 安装目录归一化：
    /// ① 用户只选了盘符根（如 D:\）→ D:\EarthOnline（单一专属文件夹，**不**再套一层）；
    /// ② 用户选了已含 EarthOnline 名字的目录 → 原样（并折叠历史遗留的重复层级）；
    /// ③ 其他目录（如 D:\MyApps）→ D:\MyApps\EarthOnline。
    /// 保证文件永不平铺在盘符根目录，且永不出现 D:\EarthOnline\EarthOnline 这类多余层级。
    /// </summary>
    public static string NormalizeInstallDir(string selected)
    {
        var path = selected?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "EarthOnline");
        }

        path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // 折叠重复的 EarthOnline 层级：旧版本选盘符根会生成 D:\EarthOnline\EarthOnline，
        // 这类路径再次传入时必须收敛为 D:\EarthOnline，否则越装越深。
        while (true)
        {
            if (!string.Equals(Path.GetFileName(path), "EarthOnline", StringComparison.OrdinalIgnoreCase)) break;
            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(parent)) break;
            if (!string.Equals(Path.GetFileName(parent), "EarthOnline", StringComparison.OrdinalIgnoreCase)) break;
            path = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        var name = Path.GetFileName(path);

        if (string.Equals(name, "EarthOnline", StringComparison.OrdinalIgnoreCase))
            return path;   // 已是专属文件夹

        if (string.IsNullOrWhiteSpace(name))
        {
            // 选中盘符根：D:\ → D:\EarthOnline
            var root = Path.GetPathRoot(path) ?? "C:\\";
            return Path.Combine(root, "EarthOnline");
        }

        return Path.Combine(path, "EarthOnline");
    }

    private static void Log(string line)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        }
        catch { /* 日志失败不影响安装 */ }
    }

    /// <summary>静默安装入口（命令行 /silent 调用）。返回进程退出码。</summary>
    public static int RunSilent(App.InstallOptions opts)
    {
        Log($"=== 静默安装开始 dir={opts.InstallDir} desktop={opts.Desktop} startMenu={opts.StartMenu} taskbar={opts.Taskbar} ===");
        var cts = new CancellationTokenSource();
        try
        {
            var result = InstallAsync(opts, p => Log(p.Message ?? $"progress={p.Percent}"),
                cts.Token).GetAwaiter().GetResult();
            if (result)
            {
                Log("=== 静默安装成功 ===");
                Console.WriteLine("安装成功。");
                return 0;
            }
            Log("=== 静默安装失败 ===");
            Console.WriteLine("安装失败，详见 " + LogPath);
            return 1;
        }
        catch (Exception ex)
        {
            Log("静默安装异常: " + ex);
            Console.WriteLine("安装异常: " + ex.Message);
            return 1;
        }
    }

    /// <summary>核心安装流程：释放主程序 + 创建快捷方式。</summary>
    public static async Task<bool> InstallAsync(
        App.InstallOptions opts,
        Action<InstallProgress> report,
        CancellationToken ct)
    {
        var dir = opts.InstallDir;
        try
        {
            report(new InstallProgress { Message = "准备安装目录…" });
            Directory.CreateDirectory(dir);

            // 1) 校验目录可写
            var probe = Path.Combine(dir, ".writetest");
            await File.WriteAllTextAsync(probe, "ok", ct);
            File.Delete(probe);

            // 2) 释放主程序
            report(new InstallProgress { Message = "正在释放 地球Online 主程序…", Percent = 0 });
            var total = await ExtractPayload(dir, p =>
                report(new InstallProgress { Percent = p }), ct);

            // 3) 快捷方式
            var shortcuts = new List<string>();
            if (opts.Desktop) shortcuts.Add("桌面");
            if (opts.StartMenu) shortcuts.Add("开始菜单");
            if (shortcuts.Count > 0)
                report(new InstallProgress { Message = "正在创建快捷方式（" + string.Join("、", shortcuts) + "）…" });

            var target = Path.Combine(dir, "EarthOnline.exe");
            if (opts.Desktop) CreateDesktopShortcut(target, dir);
            if (opts.StartMenu) CreateStartMenuShortcut(target, dir);

            // 3.5) 释放卸载器
            report(new InstallProgress { Message = "正在配置卸载程序…" });
            var uninstaller = Path.Combine(dir, "uninstall.exe");
            ExtractSelf(uninstaller);
            CreateStartMenuUninstallerShortcut(uninstaller);

            // 3.9) 注册「应用和功能」卸载入口（HKCU，无需管理员）
            RegisterUninstallEntry(dir, uninstaller, opts.Desktop, opts.StartMenu);

            // 4) 任务栏固定：Windows 10/11 限制第三方静默固定，最佳努力 + 引导提示
            if (opts.Taskbar)
            {
                report(new InstallProgress { Message = "尝试固定到任务栏（受系统限制可能需手动）…" });
                TryPinToTaskbar(target);
            }

            report(new InstallProgress
            {
                Message = "安装完成！",
                Percent = 100,
                Done = true
            });
            Log($"安装完成 dir={dir}");
            return true;
        }
        catch (OperationCanceledException)
        {
            report(new InstallProgress { Message = "已取消安装。", Failed = true });
            SafeCleanup(dir);
            return false;
        }
        catch (Exception ex)
        {
            Log("安装失败: " + ex);
            report(new InstallProgress { Message = "安装失败：" + ex.Message, Failed = true });
            SafeCleanup(dir);
            return false;
        }
    }

    private static void SafeCleanup(string dir)
    {
        try
        {
            if (Directory.Exists(dir) &&
                !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir, false);
        }
        catch { /* 忽略清理失败 */ }
    }

    /// <summary>从嵌入资源提取主程序到目标目录，按块拷贝并回报进度。</summary>
    private static async Task<long> ExtractPayload(string dir, Action<double> onPercent, CancellationToken ct)
    {
        var asm = Assembly.GetExecutingAssembly();
        string? resName = null;
        foreach (var n in asm.GetManifestResourceNames())
            if (n.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase))
            { resName = n; break; }

        if (resName is null)
            throw new InvalidOperationException("未在主程序集中找到嵌入的主程序资源。");

        var outPath = Path.Combine(dir, "EarthOnline.exe");
        await using var src = asm.GetManifestResourceStream(resName)
            ?? throw new InvalidOperationException("无法打开嵌入资源流。");

        long total = src.Length;
        const int bufSize = 1024 * 1024; // 1MB 块
        var buffer = new byte[bufSize];
        long copied = 0;

        await using var dst = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, bufSize, true);
        int read;
        while ((read = await src.ReadAsync(buffer, 0, bufSize, ct)) > 0)
        {
            await dst.WriteAsync(buffer, 0, read, ct);
            copied += read;
            if (total > 0) onPercent(Math.Min(100.0, copied * 100.0 / total));
        }
        await dst.FlushAsync(ct);

        // 释放后校验大小一致
        var info = new FileInfo(outPath);
        if (total > 0 && Math.Abs(info.Length - total) > 1024)
            throw new InvalidOperationException("释放文件大小校验不一致，可能损坏。");

        return total;
    }

    // ---------------- 卸载器 ----------------

    /// <summary>把安装器自身复制为 uninstall.exe（安装器本体即卸载器，/uninstall 模式）。</summary>
    private static void ExtractSelf(string destPath)
    {
        var self = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(self) &&
            string.Equals(Path.GetFileName(self), "uninstall.exe", StringComparison.OrdinalIgnoreCase))
        {
            // 已经是卸载器在运行（不应发生），跳过
            return;
        }
        if (string.IsNullOrEmpty(self) || !File.Exists(self))
            throw new InvalidOperationException("无法定位安装器自身以生成卸载程序。");

        File.Copy(self, destPath, overwrite: true);
        Log("释放卸载器: " + destPath);
    }

    private static void CreateStartMenuUninstallerShortcut(string uninstaller)
    {
        try
        {
            var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            var folder = Path.Combine(startMenu, "Programs", AppName);
            Directory.CreateDirectory(folder);
            WriteShortcut(Path.Combine(folder, "卸载 地球Online.lnk"), uninstaller,
                Path.GetDirectoryName(uninstaller)!, "卸载 地球Online");
        }
        catch (Exception ex)
        {
            Log("卸载快捷方式创建失败（非致命）: " + ex.Message);
        }
    }

    /// <summary>注册 HKCU\Software\EarthOnline + Uninstall 子键，出现在「应用和功能」。</summary>
    private static void RegisterUninstallEntry(string dir, string uninstaller, bool desktop, bool startMenu)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegKeyPath);
            key.SetValue("InstallDir", dir);
            key.SetValue("Version", GetSetupVersion());
            key.SetValue("InstalledAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            key.SetValue("DesktopShortcut", desktop ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
            key.SetValue("StartMenuShortcut", startMenu ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);

            var uninstallSub = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\EarthOnline";
            using var uk = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(uninstallSub);
            uk.SetValue("DisplayName", AppName + " 桌面端");
            uk.SetValue("DisplayVersion", GetSetupVersion());
            uk.SetValue("Publisher", "EarthOnline");
            uk.SetValue("InstallLocation", dir);
            uk.SetValue("DisplayIcon", Path.Combine(dir, "EarthOnline.exe"));
            uk.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
            uk.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall /silent");
            uk.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
            uk.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
            Log("注册卸载入口: HKCU\\" + uninstallSub);
        }
        catch (Exception ex)
        {
            Log("注册卸载入口失败: " + ex);
            throw;
        }
    }

    private static string GetSetupVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.4";

    // ---------------- 快捷方式 ----------------

    private static void CreateDesktopShortcut(string target, string workingDir)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var lnk = Path.Combine(desktop, "地球Online.lnk");
        WriteShortcut(lnk, target, workingDir, "地球Online 桌面端");
    }

    private static void CreateStartMenuShortcut(string target, string workingDir)
    {
        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        var folder = Path.Combine(startMenu, "Programs", "地球Online");
        Directory.CreateDirectory(folder);
        var lnk = Path.Combine(folder, "地球Online.lnk");
        WriteShortcut(lnk, target, workingDir, "地球Online 桌面端");
    }

    private static void WriteShortcut(string lnkPath, string target, string workingDir, string description)
    {
        var script = string.Join("\n", new[]
        {
            "$WshShell = New-Object -ComObject WScript.Shell",
            $"$lnk = $WshShell.CreateShortcut('{Escape(lnkPath)}')",
            $"$lnk.TargetPath = '{Escape(target)}'",
            $"$lnk.WorkingDirectory = '{Escape(workingDir)}'",
            "$lnk.IconLocation = '" + Escape(target) + ",0'",
            "$lnk.Description = '" + Escape(description) + "'",
            "$lnk.Save()"
        });
        RunPowerShell(script);

        // 后置校验：确认 .lnk 真实落盘（PowerShell 静默失败时给出明确日志）
        if (!File.Exists(lnkPath))
            Log("警告：快捷方式未成功创建（可能被安全软件拦截）: " + lnkPath);
        else
            Log($"创建快捷方式: {lnkPath}");
    }

    /// <summary>最佳努力固定到任务栏；Windows 限制下通常失败，调用方负责给出引导提示。</summary>
    private static void TryPinToTaskbar(string target)
    {
        try
        {
            var folder = Path.GetDirectoryName(target)!;
            var name = Path.GetFileName(target);
            var script = string.Join("\n", new[]
            {
                "$sa = New-Object -ComObject Shell.Application",
                "$folder = $sa.NameSpace('" + Escape(folder) + "')",
                "$item = $folder.ParseName('" + Escape(name) + "')",
                "$verbs = $item.Verbs()",
                "foreach ($v in $verbs) { if ($v.Name -like '*taskbar*' -or $v.Name -like '*任务栏*') { $v.DoIt(); break } }"
            });
            RunPowerShell(script);
            Log("已尝试固定到任务栏（结果取决于系统策略）。");
        }
        catch (Exception ex)
        {
            Log("固定任务栏尝试失败（非致命）: " + ex.Message);
        }
    }

    private static string Escape(string s) => s.Replace("'", "''");

    private static void RunPowerShell(string script)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "eo_setup_" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(tmp, script, System.Text.Encoding.UTF8);
        try
        {
            var psi = new ProcessStartInfo("powershell",
                "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + tmp + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)
                ?? throw new InvalidOperationException("无法启动 PowerShell。");
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit(30000);
            if (!string.IsNullOrWhiteSpace(err)) Log("PowerShell stderr: " + err);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }
}

/// <summary>主程序启动辅助（完成页"立即运行"调用）。</summary>
public static class AppLauncher
{
    public static void Launch(string installDir)
    {
        var exe = Path.Combine(installDir, "EarthOnline.exe");
        if (!File.Exists(exe)) return;
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = installDir,
            UseShellExecute = true
        };
        Process.Start(psi);
    }
}

/// <summary>卸载执行（uninstall.exe /uninstall 调用）。</summary>
public static class UninstallManager
{
    private const string AppName = "地球Online";
    private const string UninstallRegPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\EarthOnline";
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "EarthOnline-Uninstall.log");

    private static void Log(string line)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        }
        catch { /* 日志失败不影响卸载 */ }
    }

    public static void Uninstall(string installDir)
    {
        Log($"=== 卸载开始 dir={installDir} ===");

        // 0) 结束运行中的主程序（最佳努力，否则目录删不掉）
        TryKillMainApp();

        // 1) 清理快捷方式（桌面 + 开始菜单：主程序、卸载器两个 lnk + 文件夹）
        RemoveShortcuts();

        // 2) 清理注册表（应用键 + 「应用和功能」卸载键）
        RemoveRegistryEntries();

        // 3) 删除安装目录（卸载器自身在目录内 → 需延迟自删除兜底）
        RemoveInstallDir(installDir);

        Log("=== 卸载流程结束 ===");
    }

    private static void TryKillMainApp()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("EarthOnline"))
            {
                try { p.Kill(entireProcessTree: true); Log("已结束主程序进程 " + p.Id); }
                catch (Exception ex) { Log($"结束主程序 {p.Id} 失败（忽略）: {ex.Message}"); }
                finally { p.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            Log("枚举主程序进程失败（忽略）: " + ex.Message);
        }
    }

    private static void RemoveShortcuts()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            DeleteIfExists(Path.Combine(desktop, AppName + ".lnk"));

            var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            var folder = Path.Combine(startMenu, "Programs", AppName);
            DeleteIfExists(Path.Combine(folder, AppName + ".lnk"));
            DeleteIfExists(Path.Combine(folder, "卸载 " + AppName + ".lnk"));
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder, false);
                Log("已删除开始菜单文件夹: " + folder);
            }
        }
        catch (Exception ex)
        {
            Log("清理快捷方式失败（继续）: " + ex.Message);
        }
    }

    private static void RemoveRegistryEntries()
    {
        try
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(UninstallRegPath, throwOnMissingSubKey: false);
            Log("已删除卸载注册表项: HKCU\\" + UninstallRegPath);
        }
        catch (Exception ex)
        {
            Log("删除卸载注册表项失败（继续）: " + ex.Message);
        }

        try
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\EarthOnline", throwOnMissingSubKey: false);
            Log("已删除应用注册表项: HKCU\\Software\\EarthOnline");
        }
        catch (Exception ex)
        {
            Log("删除应用注册表项失败（继续）: " + ex.Message);
        }
    }

    private static void RemoveInstallDir(string installDir)
    {
        try
        {
            if (!Directory.Exists(installDir))
            {
                Log("安装目录不存在，跳过删除。");
                return;
            }

            try
            {
                Directory.Delete(installDir, true);
                Log("安装目录已删除: " + installDir);
            }
            catch
            {
                // 常规失败（多为卸载器自身文件被占用）→ 延迟自删除
                Log("常规删除失败，转入延迟自删除。");
                SpawnDelayedSelfCleanup(installDir);
                return;
            }

            // 成功后再清理空的专属外层文件夹（如 D:\EarthOnline\EarthOnline 的外层）
            TryRemoveEmptyWrapper(installDir);
        }
        catch (Exception ex)
        {
            Log("删除安装目录失败（继续）: " + ex.Message);
        }
    }

    /// <summary>安装目录删除成功后，若其父目录名为 EarthOnline 且已空，一并移除。</summary>
    private static void TryRemoveEmptyWrapper(string installDir)
    {
        try
        {
            var parent = Directory.GetParent(installDir);
            if (parent is null) return;
            if (!string.Equals(parent.Name, "EarthOnline", StringComparison.OrdinalIgnoreCase)) return;
            if (Directory.Exists(parent.FullName) && !Directory.EnumerateFileSystemEntries(parent.FullName).Any())
            {
                Directory.Delete(parent.FullName, false);
                Log("已删除空外层文件夹: " + parent.FullName);
            }
        }
        catch { /* 非致命 */ }
    }

    /// <summary>延迟自删除：分离一个 cmd 进程，等待本进程退出后删除整个安装目录（顺带清理空外层文件夹）。</summary>
    private static void SpawnDelayedSelfCleanup(string installDir)
    {
        try
        {
            var dir = installDir.TrimEnd('\\');
            var args = "/C ping -n 3 127.0.0.1 > nul & rd /s /q \"" + dir + "\"";

            try
            {
                var parent = Directory.GetParent(dir);
                if (parent is not null &&
                    string.Equals(parent.Name, "EarthOnline", StringComparison.OrdinalIgnoreCase))
                    args += " & rd /q \"" + parent.FullName.TrimEnd('\\') + "\"";
            }
            catch { /* 外层清理为尽力而为 */ }

            var psi = new ProcessStartInfo("cmd.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi);
            Log("已派发延迟自删除任务。");
        }
        catch (Exception ex)
        {
            Log("派发延迟自删除失败: " + ex.Message);
        }
    }

    /// <summary>删除文件（带重试与只读属性复位，规避杀软/资源管理器的瞬时占用）。</summary>
    private static void DeleteIfExists(string path)
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                if (!File.Exists(path)) return;
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                Log("已删除: " + path);
                return;
            }
            catch (Exception ex)
            {
                Log($"删除失败(第{i + 1}次): " + path + " - " + ex.Message);
                Thread.Sleep(400);
            }
        }
    }
}
