using System.Text.Json.Serialization;

namespace EarthOnline.Desktop.Data.Entities;

/// <summary>分类作用域：物品</summary>
public static class BagScope
{
    public const string Item = "item";
    public const string Collection = "collection";
}

/// <summary>
/// 背包自定义分类（物品 / 收藏各一套，由 Scope 区分）。
/// 对应安卓 BagCategoryEntity / 网页 state.collectionCategories。
/// 删除分类时，引用它的条目回落为「未分类」（条目本身不删）。
/// </summary>
public class BagCategoryEntity
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>BagScope.Item 或 BagScope.Collection</summary>
    [JsonPropertyName("scope")] public string Scope { get; set; } = BagScope.Item;

    /// <summary>排序号，越小越靠前</summary>
    [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
}
