using System.Text.Json.Serialization;

namespace EarthOnline.Desktop.Data.Entities;

/// <summary>
/// 背包物品（对应安卓 ItemEntity / 网页 state.items）。
/// </summary>
public class ItemEntity
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>virtual / physical（兼容字段）</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "physical";

    [JsonPropertyName("description")] public string? Description { get; set; }

    /// <summary>自定义分类 id 或空</summary>
    [JsonPropertyName("category")] public string? Category { get; set; }

    /// <summary>YYYY-MM-DD</summary>
    [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = "";
}
