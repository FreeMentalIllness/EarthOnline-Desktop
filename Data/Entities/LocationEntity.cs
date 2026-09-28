using System.Text.Json.Serialization;

namespace EarthOnline.Desktop.Data.Entities;

/// <summary>
/// 足迹地图坐标（对应安卓 LocationEntity / 网页 state.locations）。
/// 高德 / 通用坐标一律 [lng, lat] 顺序。
/// </summary>
public class LocationEntity
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("lat")] public double Lat { get; set; }
    [JsonPropertyName("lng")] public double Lng { get; set; }

    /// <summary>YYYY-MM-DD</summary>
    [JsonPropertyName("date")] public string Date { get; set; } = "";

    [JsonPropertyName("note")] public string? Note { get; set; }

    /// <summary>List&lt;string&gt; 的 JSON（多标签）</summary>
    [JsonPropertyName("tagsJson")] public string? TagsJson { get; set; }
}
