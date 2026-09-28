namespace EarthOnline.Desktop.Data.Entities;

/// <summary>
/// 最近动态 feed（对应安卓 ActivityEntity / 网页 state.activities，最多保留 50 条环形裁剪）。
/// </summary>
public class ActivityEntity
{
    public string Id { get; set; } = "";

    /// <summary>ISO</summary>
    public string Time { get; set; } = "";

    /// <summary>ach / task / item / memo</summary>
    public string Kind { get; set; } = "";

    public string Title { get; set; } = "";
}
