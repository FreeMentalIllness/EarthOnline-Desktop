using System.Text.Json.Serialization;

namespace EarthOnline.Desktop.Data.Entities;

/// <summary>
/// 收藏（对应安卓 CollectionEntity / 网页 state.collections）。
/// 文件二进制不入库，仅存元信息与路径。
/// </summary>
public class CollectionEntity
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    /// <summary>可空 = 未分类</summary>
    [JsonPropertyName("category")] public string? Category { get; set; }

    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("note")] public string? Note { get; set; }

    /// <summary>{name,mime,size} 元信息 JSON</summary>
    [JsonPropertyName("fileMetaJson")] public string? FileMetaJson { get; set; }

    /// <summary>本地持久化文件的路径（替代安卓的沙盒 Uri）</summary>
    [JsonPropertyName("fileUri")] public string? FileUri { get; set; }

    /// <summary>YYYY-MM-DD</summary>
    [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = "";

    /// <summary>回收站软删标记（ISO 时间），不入导出 JSON（v1.0.5）</summary>
    [JsonIgnore] public string? DeletedAt { get; set; }
}
