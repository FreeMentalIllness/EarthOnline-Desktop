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

    /// <summary>界面主题：light / dark（画刷颜色即时生效）。</summary>
    public string Theme { get; set; } = "light";

    /// <summary>全局字号缩放：0.9 小 / 1.0 标准 / 1.15 大（窗口 LayoutTransform）。</summary>
    public double FontScale { get; set; } = 1.0;

    /// <summary>自定义壁纸图片路径（空 = 使用纯色背景）。</summary>
    public string WallpaperPath { get; set; } = "";

    /// <summary>AI 接口根地址（OpenAI 兼容；程序内自动拼 /chat/completions）。不预设默认值。</summary>
    public string AiBaseUrl { get; set; } = "";

    /// <summary>AI 模型名（必填，由用户按服务商文档填写；不设默认值）。</summary>
    public string AiModel { get; set; } = "";

    /// <summary>
    /// AI API Key —— 只存 DPAPI 加密后的 Base64（SecretProtector.Protect），**绝不存明文**。
    /// 密文与当前 Windows 用户绑定，复制给别人 / 换机器都是废数据。
    /// </summary>
    public string AiKeyEnc { get; set; } = "";

    /// <summary>本地自动备份开关（对齐安卓 AutoBackupManager 的常开行为）。</summary>
    public bool AutoBackup { get; set; } = true;

    /// <summary>
    /// 是否已完成首次启动引导（对齐安卓 OnboardingScreen 只看一次的行为）。
    /// 设置页「重新查看引导」可置回 false 再次唤起。
    /// </summary>
    public bool Onboarded { get; set; }

    /// <summary>高德地图 API Key（DPAPI 加密后的 Base64；留空则用内置回退 Key）。</summary>
    public string AmapKeyEnc { get; set; } = "";

    /// <summary>高德 JS API 安全密钥（DPAPI 加密；可留空 —— 部分 Key 类型不需要）。</summary>
    public string AmapSecEnc { get; set; } = "";

    /// <summary>
    /// 自定义数据目录（v1.0.3）。留空 = 使用默认目录（应用根 EarthOnlineData，不可写时回落 %LOCALAPPDATA%\EarthOnline）。
    /// 设置后下一次启动生效；App 启动时会把旧目录的数据一次性迁移过来。
    /// </summary>
    public string DataDirectory { get; set; } = "";

    /// <summary>退出行为：minimize=最小化到托盘常驻后台；exit=直接退出程序。</summary>
    public string ExitBehavior { get; set; } = "minimize";

    /// <summary>
    /// 自定义称号（v1.0.3，对齐安卓 XpRules.titleFor）。留空显示默认「旅行者」。
    /// 存 settings.json 而非 profile 表 —— 零 DB 变更。
    /// </summary>
    [JsonPropertyName("customTitle")]
    public string CustomTitle { get; set; } = "";

    /// <summary>
    /// 徽章墙：主页佩戴的成就 id（最多 3 枚，按佩戴顺序展示）。同样走 settings.json。
    /// </summary>
    [JsonPropertyName("pinnedAchievements")]
    public List<string> PinnedAchievements { get; set; } = new();

    // 固定放在应用配置目录：设置里保存着「数据目录」，配置不能跟着数据目录走，否则形成循环依赖。
    private static string FilePath => AppPaths.SettingsFile;

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
        Directory.CreateDirectory(AppPaths.ConfigDir); // 配置目录独立于数据目录
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
