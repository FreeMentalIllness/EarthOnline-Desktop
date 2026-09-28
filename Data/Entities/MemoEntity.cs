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
}
