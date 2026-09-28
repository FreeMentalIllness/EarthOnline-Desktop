namespace EarthOnline.Desktop.Data.Entities;

/// <summary>
/// 成就（自动 + 手动；对应安卓 AchievementEntity / 网页 state.achievements）。
/// </summary>
public class AchievementEntity
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Desc { get; set; } = "";

    /// <summary>auto / manual</summary>
    public string Type { get; set; } = "manual";

    /// <summary>自动成就规则 key</summary>
    public string? AutoKey { get; set; }

    public bool Unlocked { get; set; }

    /// <summary>ISO</summary>
    public string? UnlockedAt { get; set; }

    /// <summary>
    /// 分类 id（见安卓 AchievementCatalog.ACH_CATEGORIES）。
    /// 空 = 未分类（旧行），UI 上归入「自定义」。
    /// </summary>
    public string? Category { get; set; }
}
