namespace EarthOnline.Desktop.Data.Entities;

/// <summary>
/// 足迹地图坐标（对应安卓 LocationEntity / 网页 state.locations）。
/// 高德 / 通用坐标一律 [lng, lat] 顺序。
/// </summary>
public class LocationEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double Lat { get; set; }
    public double Lng { get; set; }

    /// <summary>YYYY-MM-DD</summary>
    public string Date { get; set; } = "";

    public string? Note { get; set; }

    /// <summary>List&lt;string&gt; 的 JSON（多标签）</summary>
    public string? TagsJson { get; set; }
}
