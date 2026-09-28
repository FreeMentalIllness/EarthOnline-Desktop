namespace EarthOnline.Desktop.Data.Entities;

/// <summary>
/// 世界日志 / 灵感闪念（对应安卓 MemoEntity / 网页 state.memos）。
/// </summary>
public class MemoEntity
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";

    /// <summary>note / important / idea</summary>
    public string Type { get; set; } = "note";

    /// <summary>ISO</summary>
    public string CreatedAt { get; set; } = "";
}
