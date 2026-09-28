namespace EarthOnline.Desktop.Data.Entities;

/// <summary>
/// 收藏（对应安卓 CollectionEntity / 网页 state.collections）。
/// 文件二进制不入库，仅存元信息与路径。
/// </summary>
public class CollectionEntity
{
    public string Id { get; set; } = "";

    /// <summary>可空 = 未分类</summary>
    public string? Category { get; set; }

    public string Title { get; set; } = "";
    public string? Note { get; set; }

    /// <summary>{name,mime,size} 元信息 JSON</summary>
    public string? FileMetaJson { get; set; }

    /// <summary>本地持久化文件的路径（替代安卓的沙盒 Uri）</summary>
    public string? FileUri { get; set; }

    /// <summary>YYYY-MM-DD</summary>
    public string CreatedAt { get; set; } = "";
}
