using System.Text.Json.Serialization;

namespace EarthOnline.Desktop.Data.Entities;

/// <summary>
/// 成就（自动 + 手动；对应安卓 AchievementEntity / 网页 state.achievements）。
/// </summary>
public class AchievementEntity
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("desc")] public string Desc { get; set; } = "";

    /// <summary>auto / manual</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "manual";

    /// <summary>自动成就规则 key</summary>
    [JsonPropertyName("autoKey")] public string? AutoKey { get; set; }

    [JsonPropertyName("unlocked")] public bool Unlocked { get; set; }

    /// <summary>ISO</summary>
    [JsonPropertyName("unlockedAt")] public string? UnlockedAt { get; set; }

    /// <summary>分类 id（空 = 未分类，UI 归入「自定义」）。</summary>
    [JsonPropertyName("category")] public string? Category { get; set; }
}
