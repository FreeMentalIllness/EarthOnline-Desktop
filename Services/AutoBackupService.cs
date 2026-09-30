using System.Globalization;
using System.IO;
using EarthOnline.Desktop.Data;

namespace EarthOnline.Desktop.Services;

/// <summary>自动备份快照（供设置页展示）。</summary>
public class BackupSnapshot
{
    public string Path { get; init; } = "";
    public DateTime Time { get; init; }
    public long Bytes { get; init; }
    public string Label => Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                           + " · " + (Bytes / 1024.0 < 1024
                               ? Math.Max(1, (long)Math.Round(Bytes / 1024.0)) + " KB"
                               : (Bytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) + " MB");
}

/// <summary>
/// 本地自动备份：数据库每次写入后防抖留一份 JSON 快照，只保留最近 3 份。
/// 对齐安卓 AutoBackupManager：
///  - 防抖 DEBOUNCE = 4s（连续写合并成一次快照）；
///  - 最小间隔 MIN_INTERVAL = 60s（防止「改一个字写一份」刷爆磁盘）；
///  - 保留 KEEP = 3 份，按文件名时间戳排序滚动删除（不依赖文件系统 mtime）；
///  - 文件名同安卓：auto-yyyyMMdd-HHmmss.json。
///
/// 触发方式说明：EF Core 没有 Room 的 InvalidationTracker，改为在 AppDbContext.SaveChanges
/// 里统一发信号（所有写入都经过 DbContext，不会有漏网路径；本服务只写文件不写库，不会自激循环）。
/// </summary>
public static class AutoBackupService
{
    /// <summary>保留份数（与安卓 AutoBackupManager.KEEP 一致）。</summary>
    public const int Keep = 3;
    private const int DebounceMs = 4_000;
    private const int MinIntervalMs = 60_000;

    private static readonly object Gate = new();
    private static readonly object WriteGate = new();

    private static CancellationTokenSource? _pending;
    private static DateTime _lastAt = DateTime.MinValue;
    private static bool _enabled = true;
    private static bool _started;

    /// <summary>启动时调用（幂等）：读开关并准备目录。</summary>
    public static void Start()
    {
        lock (Gate)
        {
            _started = true;
            _enabled = SettingsStore.Load().AutoBackup;
        }
        try { Directory.CreateDirectory(AppPaths.BackupDir); } catch { /* 目录失败不影响主流程 */ }
    }

    /// <summary>设置页改开关后调用，立即生效。</summary>
    public static void RefreshEnabled() => _enabled = SettingsStore.Load().AutoBackup;

    public static bool Enabled => _enabled;

    /// <summary>数据库发生写入 —— 调度一次防抖快照。</summary>
    public static void Signal()
    {
        if (!_started || !_enabled) return;
        lock (Gate)
        {
            _pending?.Cancel();
            _pending = new CancellationTokenSource();
            var ct = _pending.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DebounceMs, ct);
                    if (ct.IsCancellationRequested) return;
                    await Task.Run(() => Snapshot(force: false), ct);
                }
                catch { /* 备份失败绝不影响使用 */ }
            });
        }
    }

    /// <summary>
    /// 立刻写一份快照（设置页「立即备份」走 force=true）。
    /// </summary>
    /// <returns>快照路径；被节流 / 空库时返回 null。</returns>
    public static string? Snapshot(bool force)
    {
        lock (WriteGate)
        {
            var now = DateTime.Now;
            if (!force && (now - _lastAt).TotalMilliseconds < MinIntervalMs) return null;

            string text;
            try { text = BackupService.ExportJson(); }
            catch { return null; }
            if (text.Length < 32) return null;               // 空库没必要留快照

            try
            {
                Directory.CreateDirectory(AppPaths.BackupDir);
                var path = Path.Combine(AppPaths.BackupDir,
                    "auto-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json");
                File.WriteAllText(path, text);
                _lastAt = now;
                Trim();
                return path;
            }
            catch { return null; }
        }
    }

    /// <summary>只保留最近 [Keep] 份。</summary>
    private static void Trim()
    {
        try
        {
            var files = ListRaw();
            for (int i = Keep; i < files.Count; i++)
            {
                try { File.Delete(files[i].Path); } catch { /* 占用中则下次再删 */ }
            }
        }
        catch { /* 清理失败无害 */ }
    }

    /// <summary>快照列表（新 → 旧）。</summary>
    public static List<BackupSnapshot> ListSnapshots() => ListRaw()
        .Select(f => new BackupSnapshot
        {
            Path = f.Path,
            Time = f.Time,
            Bytes = f.Bytes
        })
        .ToList();

    private static List<BackupSnapshot> ListRaw()
    {
        try
        {
            if (!Directory.Exists(AppPaths.BackupDir)) return new List<BackupSnapshot>();
            return Directory.GetFiles(AppPaths.BackupDir, "auto-*.json")
                .Select(p => new FileInfo(p))
                .Select(f => new BackupSnapshot
                {
                    Path = f.FullName,
                    Time = ParseName(f.Name) ?? f.LastWriteTime,
                    Bytes = f.Length
                })
                .OrderByDescending(s => s.Time)
                .ToList();
        }
        catch { return new List<BackupSnapshot>(); }
    }

    /// <summary>文件名解析失败（手工改名过）时回落到文件修改时间。</summary>
    private static DateTime? ParseName(string name)
    {
        var s = name.StartsWith("auto-") ? name[5..] : name;
        s = s.EndsWith(".json") ? s[..^5] : s;
        return DateTime.TryParseExact(s, "yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
    }

    /// <summary>从某份快照恢复（走 BackupService 的按主键合并语义，与导入备份一致）。</summary>
    public static int Restore(string path)
    {
        var text = File.ReadAllText(path);
        return BackupService.ImportJson(text);
    }

    /// <summary>清空全部本地自动备份快照（清空数据时调用，避免旧快照残留已清数据）。</summary>
    public static void ClearSnapshots()
    {
        try
        {
            if (!Directory.Exists(AppPaths.BackupDir)) return;
            foreach (var f in Directory.GetFiles(AppPaths.BackupDir, "auto-*.json"))
            {
                try { File.Delete(f); } catch { /* 占用中则忽略 */ }
            }
        }
        catch { /* 清理失败无害 */ }
    }
}
