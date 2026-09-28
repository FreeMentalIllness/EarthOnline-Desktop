using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using EarthOnline.Desktop.Services;

namespace EarthOnline.Desktop.Data;

/// <summary>
/// 本地设置（WebDAV 配置 + 同步开关 + 上次同步时间）。
/// 对应安卓 SettingsDataStore 里的 webdavConfig / autoSync / lastSyncAt。
/// 存 %LOCALAPPDATA%\EarthOnline\settings.json。
/// </summary>
public class SettingsStore
{
    public string Url { get; set; } = "";
    public string User { get; set; } = "";
    public string Pass { get; set; } = "";

    /// <summary>远端文件路径，留空则用默认值（与安卓 / 网页一致）。</summary>
    public string RemotePath { get; set; } = "";

    public bool AutoSync { get; set; } = true;

    /// <summary>上次同步时间（ISO 或 LocalDateTime 字符串，用于冲突比对）。</summary>
    public string LastSyncAt { get; set; } = "";

    /// <summary>
    /// 空白标题保存被拒次数 —— 彩蛋成就 egg_blank_title 用。
    /// 这是唯一无法从数据推导的计数（安卓 AchStats.blankTitleTries 同源），必须持久化。
    /// </summary>
    public int BlankTitleTries { get; set; }

    private static string FilePath => Path.Combine(AppPaths.RootDir, "settings.json");

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static SettingsStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var text = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<SettingsStore>(text, Opts) ?? new SettingsStore();
            }
        }
        catch
        {
            // 损坏则回落默认值，绝不因设置文件问题导致启动失败
        }
        return new SettingsStore();
    }

    public void Save()
    {
        AppPaths.EnsureDirectories();
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
    }

    public bool HasConfig => !string.IsNullOrWhiteSpace(Url);

    /// <summary>远端路径：留空时取默认值 /earth_online_backup.json（安卓 CloudSyncManager 同逻辑）。</summary>
    public string EffectiveRemotePath()
    {
        var p = (RemotePath ?? "").Trim();
        if (p.Length == 0) return SyncService.DefaultRemotePath;
        return p.StartsWith("/") ? p : "/" + p;
    }
}
