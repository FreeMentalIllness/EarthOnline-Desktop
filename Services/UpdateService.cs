using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EarthOnline.Desktop.Services;

/// <summary>
/// 检查更新：对接 GitHub Releases（FreeMentalIllness/EarthOnline-Desktop）。
/// 以 csproj 的 &lt;Version&gt; 为当前版本，与 latest release 的 tag（v1.0.0 格式）比较。
/// 发现新版本时支持应用内下载（%TEMP%\EarthOnline_Update.exe）并生成 update.bat
/// 无人值守替换当前 exe 后自动重启，全程无需手动操作。
/// </summary>
public static class UpdateService
{
    private const string LatestApi = "https://api.github.com/repos/FreeMentalIllness/EarthOnline-Desktop/releases/latest";
    private const string ReleasesPage = "https://github.com/FreeMentalIllness/EarthOnline-Desktop/releases";
    private const string TempUpdateExe = "EarthOnline_Update.exe";

    /// <summary>当前程序版本（取自 csproj Version，形如 1.0.0）。</summary>
    public static string CurrentVersion =>
        typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public sealed record CheckResult(
        bool Ok, string Message,
        string? LatestVersion, string? ExeUrl, long ExeSize, string? DownloadPage);

    /// <summary>检查 latest release：解析 tag_name 与 .exe 资产直链。</summary>
    public static async Task<CheckResult> CheckAsync()
    {
        try
        {
            using var http = NewClient(15);
            var resp = await http.GetStringAsync(LatestApi);
            using var doc = JsonDocument.Parse(resp);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var page = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;

            var latest = tag.TrimStart('v', 'V').Trim();
            if (latest.Length == 0)
            {
                return new CheckResult(false, "仓库还没有发布任何 Release。", null, null, 0, ReleasesPage);
            }

            // 资产里找 exe（Release 可能同时带 zip 等其他文件，优先 .exe，其次第一个资产）
            string? exeUrl = null;
            long exeSize = 0;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var url = a.TryGetProperty("browser_download_url", out var bu) ? bu.GetString() : null;
                    if (url is null) continue;
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        exeUrl = url;
                        exeSize = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                        break;
                    }
                    exeUrl ??= url;
                    exeSize = exeSize == 0 && a.TryGetProperty("size", out var s0) ? s0.GetInt64() : exeSize;
                }
            }

            if (CompareVersions(latest, CurrentVersion) > 0)
            {
                return new CheckResult(true,
                    $"发现新版本 v{latest}（当前 v{CurrentVersion}）。",
                    latest, exeUrl, exeSize, page ?? ReleasesPage);
            }

            return new CheckResult(true, $"已是最新版本 v{CurrentVersion}。", latest, exeUrl, exeSize, page);
        }
        catch (HttpRequestException)
        {
            return new CheckResult(false, "网络异常，请检查网络或代理。", null, null, 0, ReleasesPage);
        }
        catch (TaskCanceledException)
        {
            return new CheckResult(false, "网络异常，请检查网络或代理。", null, null, 0, ReleasesPage);
        }
        catch (Exception ex)
        {
            return new CheckResult(false, "检查更新失败：" + ex.Message, null, null, 0, ReleasesPage);
        }
    }

    /// <summary>
    /// 下载最新 exe 到 %TEMP%\EarthOnline_Update.exe（带进度回调与取消），成功后弹确认，
    /// 生成 update.bat 并立即退出程序，由 bat 完成「等待→覆盖→重启→自删」。
    /// 返回 false 表示用户取消或下载失败（失败已弹窗提示，不闪退）。
    /// </summary>
    public static async Task<bool> DownloadAndInstallAsync(
        string exeUrl, long exeSize, Action<string>? statusText)
    {
        var tempExe = Path.Combine(Path.GetTempPath(), TempUpdateExe);

        try
        {
            // 清掉上次可能残留的半成品
            try { File.Delete(tempExe); } catch { /* 不存在则忽略 */ }

            using var cts = new CancellationTokenSource();
            var win = new UpdateProgressWindow(exeSize, cts);
            var progress = new Progress<double>(p =>
            {
                win.SetProgress(p);
                if (statusText is not null)
                    statusText($"正在下载更新… {p:F0}%");
            });

            win.Show();
            bool ok;
            try
            {
                ok = await DownloadToFileAsync(exeUrl, tempExe, progress, cts.Token);
            }
            finally
            {
                win.Close();
            }

            if (!ok)
            {
                statusText?.Invoke("下载已取消。");
                return false;
            }

            // 下载完成后确认 → 生成 bat → 退出
            var answer = MessageBox.Show(
                $"更新包已下载完成（{exeSize / 1024 / 1024} MB）。\n\n" +
                "点击「确定」后程序将自动退出并安装更新（约 5 秒后自动重启），\n" +
                "期间请不要手动启动程序。",
                "地球Online · 更新",
                MessageBoxButton.OKCancel, MessageBoxImage.Information);

            if (answer != MessageBoxResult.OK)
            {
                statusText?.Invoke("已取消安装（更新包保留在临时目录，可删除）。");
                return false;
            }

            ApplyUpdateAndRestart(tempExe);
            return true;
        }
        catch (HttpRequestException)
        {
            TryDelete(tempExe);
            MessageBox.Show("网络异常，请检查网络或代理。", "地球Online · 更新",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            TryDelete(tempExe);
            MessageBox.Show("更新失败：" + ex.Message, "地球Online · 更新",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return false;
    }

    /// <summary>写 update.bat 并立刻退出程序。bat：等 5 秒 → 覆盖当前 exe → 重启 → 自删。</summary>
    private static void ApplyUpdateAndRestart(string tempExe)
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrEmpty(current))
            throw new InvalidOperationException("无法定位当前程序路径。");

        var bat = Path.Combine(Path.GetTempPath(), "update_earthonline.bat");
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("rem EarthOnline auto-update (ASCII only to avoid codepage issues)");
        sb.AppendLine("%WINDIR%\\System32\\timeout.exe /t 5 /nobreak >nul");
        sb.AppendLine(":retry");
        sb.AppendLine($"copy /Y \"{tempExe}\" \"{current}\" >nul 2>&1");
        sb.AppendLine("if errorlevel 1 (");
        sb.AppendLine("  %WINDIR%\\System32\\timeout.exe /t 2 /nobreak >nul");
        sb.AppendLine("  goto retry");
        sb.AppendLine(")");
        sb.AppendLine($"start \"\" \"{current}\"");
        sb.AppendLine("del \"%~f0\"");
        // ANSI(GBK) 编码：兼容含中文的安装路径，cmd 默认代码页可正确解析
        File.WriteAllText(bat, sb.ToString(), Encoding.Default);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c \"" + bat + "\"",
            CreateNoWindow = true,
            UseShellExecute = false
        });

        // 立即退出；OnExit 里的托盘清理与自动推送照常执行
        Application.Current.Shutdown();
    }

    /// <summary>流式下载到文件，8KB 一段写盘并回报百分比。</summary>
    private static async Task<bool> DownloadToFileAsync(
        string url, string dest, IProgress<double> progress, CancellationToken ct)
    {
        using var http = NewClient(300); // 大文件给足超时（按连接空闲计）
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        long? total = resp.Content.Headers.ContentLength;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            written += read;
            if (total is > 0) progress.Report(written * 100.0 / total.Value);
        }
        progress.Report(100);
        return true;
    }

    /// <summary>带 User-Agent 的 HttpClient（GitHub API/资产下载必需）。</summary>
    private static HttpClient NewClient(int timeoutSeconds)
    {
        var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("EarthOnline-Desktop/" + CurrentVersion);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* 尽力而为 */ }
    }

    /// <summary>按数值段比较 x.y.z 版本号，返回 正/零/负。</summary>
    private static int CompareVersions(string a, string b)
    {
        var va = Version.TryParse(Normalize(a), out var x) ? x : new Version(0, 0, 0);
        var vb = Version.TryParse(Normalize(b), out var y) ? y : new Version(0, 0, 0);
        return va.CompareTo(vb);
    }

    private static string Normalize(string v)
    {
        // 只保留数字与点，去掉诸如 "1.0.1-beta" 的尾巴；不足三段补 0
        var chars = v.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray();
        var s = new string(chars).Trim('.');
        var parts = s.Split('.');
        if (parts.Length >= 3) return string.Join('.', parts.Take(3));
        return (s + new string('.', 3 - parts.Length)).Replace("..", ".0.").Trim('.');
    }
}

/// <summary>
/// 更新下载进度窗（纯代码构建，无 XAML）：进度条 + 状态文本 + 取消按钮。
/// 关闭窗口 = 取消下载。
/// </summary>
public sealed class UpdateProgressWindow : Window
{
    private readonly ProgressBar _bar = new() { Height = 10, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 14, 0, 0) };
    private readonly TextBlock _text = new()
    {
        Text = "正在连接 GitHub…",
        FontSize = 13,
        Foreground = ThemeService.Brush("TextPrimaryBrush")
    };
    private readonly long _total;

    public UpdateProgressWindow(long total, CancellationTokenSource cts)
    {
        _total = total;
        Title = "地球Online · 正在下载更新";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;

        var cancel = new Button { Content = "取消", Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => Close();

        Content = new StackPanel { Margin = new Thickness(18) };
        ((StackPanel)Content).Children.Add(_text);
        ((StackPanel)Content).Children.Add(_bar);
        ((StackPanel)Content).Children.Add(cancel);

        Closed += (_, _) =>
        {
            try { cts.Cancel(); } catch { /* 已取消/已释放 */ }
        };
    }

    public void SetProgress(double percent)
    {
        _bar.Value = percent;
        _text.Text = _total > 0
            ? $"正在下载更新… {percent:F0}%（{_total / 1024 / 1024} MB）"
            : $"正在下载更新… {percent:F0}%";
    }
}
