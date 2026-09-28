namespace EarthOnline.Desktop.Data.Entities;

/// <summary>
/// 任务（支持父子嵌套；对应安卓 TaskEntity / 网页 state.tasks）。
/// </summary>
public class TaskEntity
{
    public string Id { get; set; } = "";
    public string? ParentId { get; set; }

    /// <summary>main / side / todo</summary>
    public string Category { get; set; } = "todo";

    public string Title { get; set; } = "";

    /// <summary>planning / active / paused / done</summary>
    public string Status { get; set; } = "planning";

    /// <summary>0-100</summary>
    public int Progress { get; set; }

    public string? Note { get; set; }

    /// <summary>仅 todo 使用，YYYY-MM-DD</summary>
    public string? DueDate { get; set; }

    /// <summary>YYYY-MM-DD</summary>
    public string CreatedAt { get; set; } = "";

    /// <summary>YYYY-MM-DD</summary>
    public string LastModified { get; set; } = "";

    /// <summary>
    /// 最近一次进入 done 的时间（ISO）。非 done 时为 null —— 完成任务数的唯一口径。
    /// </summary>
    public string? DoneAt { get; set; }

    /// <summary>排序号。安卓列名为 sort_order（`order` 是 SQL 关键字，故改名）。</summary>
    public int Order { get; set; }
}
