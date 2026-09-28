namespace EarthOnline.Desktop.Data.Entities;

/// <summary>
/// 背包物品（对应安卓 ItemEntity / 网页 state.items）。
/// </summary>
public class ItemEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>virtual / physical（兼容字段）</summary>
    public string Type { get; set; } = "physical";

    public string? Description { get; set; }

    /// <summary>自定义分类 id 或空</summary>
    public string? Category { get; set; }

    /// <summary>YYYY-MM-DD</summary>
    public string CreatedAt { get; set; } = "";
}
