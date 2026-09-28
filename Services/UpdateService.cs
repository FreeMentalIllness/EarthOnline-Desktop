using System.Net.Http;
using System.Text.Json;

namespace EarthOnline.Desktop.Services;

/// <summary>
/// 检查更新：对接 GitHub Releases（FreeMentalIllness/EarthOnline-Desktop）。
/// 以 csproj 的 &lt;Version&gt; 为当前版本，与 latest release 的 tag（v1.0.0 格式）比较。
/// </summary>
public static class UpdateService
{
    private const string LatestApi = "https://api.github.com/repos/FreeMentalIllness/EarthOnline-Desktop/releases/latest";
    private const string ReleasesPage = "https://github.com/FreeMentalIllness/EarthOnline-Desktop/releases";

    /// <summary>当前程序版本（取自 csproj Version，形如 1.0.0）。</summary>
    public static string CurrentVersion =>
        typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public sealed record CheckResult(bool Ok, string Message, string? LatestVersion, string? DownloadPage);

    public static async Task<CheckResult> CheckAsync()
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            // GitHub API 要求带 User-Agent，否则 403
            http.DefaultRequestHeaders.UserAgent.ParseAdd("EarthOnline-Desktop/" + CurrentVersion);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var resp = await http.GetStringAsync(LatestApi);
            using var doc = JsonDocument.Parse(resp);
            var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var page = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() : null;

            var latest = tag.TrimStart('v', 'V').Trim();
            if (latest.Length == 0)
            {
                return new CheckResult(false, "仓库还没有发布任何 Release。", null, ReleasesPage);
            }

            if (CompareVersions(latest, CurrentVersion) > 0)
            {
                return new CheckResult(true,
                    $"发现新版本 v{latest}（当前 v{CurrentVersion}）。\n请到 GitHub Releases 页面下载最新版。",
                    latest, page ?? ReleasesPage);
            }

            return new CheckResult(true, $"已是最新版本 v{CurrentVersion}。", latest, page);
        }
        catch (HttpRequestException)
        {
            return new CheckResult(false, "无法连接 GitHub（检查网络或代理后重试）。", null, ReleasesPage);
        }
        catch (Exception ex)
        {
            return new CheckResult(false, "检查更新失败：" + ex.Message, null, ReleasesPage);
        }
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
