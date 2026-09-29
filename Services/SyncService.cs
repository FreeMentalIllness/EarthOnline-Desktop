using EarthOnline.Desktop.Data;

namespace EarthOnline.Desktop.Services;

/// <summary>
/// WebDAV 双端同步（对应安卓 CloudSyncManager / 网页 modules/webdav.js）。
///
/// 冲突策略：**按修改时间** —— 云端备份的 exportedAt 与本地记录的「上次同步时间」比对，
/// 只有云端更新才拉取覆盖本地；推送后把导出时间写回作为下次基准。
///
/// 远端文件名与安卓 / 网页保持一致（默认 /earth_online_backup.json），三端才能真正互通。
/// </summary>
public static class SyncService
{
    /// <summary>与安卓 CloudSyncManager.DEFAULT_REMOTE_PATH / 网页 WEBDAV_DEFAULT_PATH 一致（下划线）。</summary>
    public const string DefaultRemotePath = "/earth_online_backup.json";

    public sealed record SyncResult(bool Ok, string Message, bool Changed = false);

    private static readonly SemaphoreSlim Mutex = new(1, 1);

    private static WebDavService.DavConfig DavOf(SettingsStore s) =>
        new(s.Url, s.User, s.Pass);

    /// <summary>拉取：云端 exportedAt 新于本地上次同步时间时才导入。force=true 忽略自动同步开关。</summary>
    public static async Task<SyncResult> PullIfRemoteNewerAsync(bool force = false)
    {
        await Mutex.WaitAsync();
        try
        {
            var s = SettingsStore.Load();
            if (!s.HasConfig) return new SyncResult(false, "未配置 WebDAV");
            if (!force && !s.AutoSync) return new SyncResult(false, "自动同步已关闭");

            var text = await WebDavService.DownloadTextAsync(DavOf(s), s.EffectiveRemotePath());
            if (text is null) return new SyncResult(false, "云端还没有备份文件");

            var remoteAt = BackupService.ReadExportedAt(text);
            var localAt = ParseMillis(s.LastSyncAt);
            if (remoteAt <= localAt) return new SyncResult(true, "云端无更新", false);

            int n = BackupService.ImportJson(text);
            s.LastSyncAt = RemoteExportedAtString(text);
            s.Save();
            // 跨端灵感接力：落库后检测来自手机的新灵感并温和提示（内部自行调度到 UI 线程）
            IdeaRelayService.CheckAfterImport();
            return new SyncResult(true, $"已拉取云端最新数据（{n} 条）", true);
        }
        catch (Exception ex)
        {
            return new SyncResult(false, "下载失败：" + ex.Message);
        }
        finally
        {
            Mutex.Release();
        }
    }

    /// <summary>推送：导出本地全量 → PUT → 记录同步时间。force=true 忽略自动同步开关。</summary>
    public static async Task<SyncResult> PushAsync(bool force = false)
    {
        await Mutex.WaitAsync();
        try
        {
            var s = SettingsStore.Load();
            if (!s.HasConfig) return new SyncResult(false, "未配置 WebDAV");
            if (!force && !s.AutoSync) return new SyncResult(false, "自动同步已关闭");

            var text = BackupService.ExportJson();

            await WebDavService.UploadTextAsync(DavOf(s), s.EffectiveRemotePath(), text);

            s.LastSyncAt = RemoteExportedAtString(text);
            s.Save();
            return new SyncResult(true, "已推送到云端", true);
        }
        catch (Exception ex)
        {
            return new SyncResult(false, "上传失败：" + ex.Message);
        }
        finally
        {
            Mutex.Release();
        }
    }

    /// <summary>测试连接：列出远端目录，成功即配置可用。</summary>
    public static async Task<SyncResult> TestAsync()
    {
        var s = SettingsStore.Load();
        if (!s.HasConfig) return new SyncResult(false, "未配置 WebDAV");
        try
        {
            var list = await WebDavService.ListAsync(DavOf(s), "/");
            return new SyncResult(true, $"连接成功（{list.Count} 个条目）");
        }
        catch (Exception ex)
        {
            return new SyncResult(false, "连接失败：" + ex.Message);
        }
    }

    /// <summary>取备份串里的 exportedAt 原文（用于写回 LastSyncAt，保持三端口径一致）。</summary>
    private static string RemoteExportedAtString(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("exportedAt", out var v) &&
                v.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return v.GetString() ?? "";
            }
        }
        catch
        {
            // 忽略
        }
        return DateTime.Now.ToString("o");
    }

    /// <summary>
    /// 时间字符串 → 毫秒。兼容三种来源：网页 toISOString()（带 Z）、
    /// 安卓 LocalDateTime.now()（无时区）、空串 → 0。
    /// </summary>
    public static long ParseMillis(string? value)
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0) return 0L;
        if (DateTimeOffset.TryParse(v, out var dto)) return dto.ToUnixTimeMilliseconds();
        if (DateTime.TryParse(v, out var dt))
        {
            return new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeMilliseconds();
        }
        return 0L;
    }
}
