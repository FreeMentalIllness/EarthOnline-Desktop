using System.Text.Json.Serialization;

namespace EarthOnline.Desktop.Data.Entities;

/// <summary>
/// 最近动态 feed（对应安卓 ActivityEntity / 网页 state.activities，最多保留 50 条环形裁剪）。
/// </summary>
public class ActivityEntity
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    /// <summary>ISO</summary>
    [JsonPropertyName("time")] public string Time { get; set; } = "";

    /// <summary>ach / task / item / memo</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";

    [JsonPropertyName("title")] public string Title { get; set; } = "";
}
