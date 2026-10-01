using System.Text.Json.Serialization;

namespace EarthOnline.Desktop.Data.Entities;

/// <summary>
/// 世界日志 / 灵感闪念（对应安卓 MemoEntity / 网页 state.memos）。
/// </summary>
public class MemoEntity
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";

    /// <summary>note / important / idea</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "note";

    /// <summary>ISO</summary>
    [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = "";

    /// <summary>
    /// 回收站：软删除时间（ISO）。null=正常；非 null=已进回收站，30 天后启动时永久清理。
    /// 本端独有，[JsonIgnore] 保证导出 JSON 与三端形状一致（不参与同步合并）。
    /// </summary>
    [JsonIgnore] public string? DeletedAt { get; set; }
}
