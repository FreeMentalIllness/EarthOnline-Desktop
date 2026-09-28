using System.Text.Json.Serialization;

namespace EarthOnline.Desktop.Data.Entities;

/// <summary>
/// 任务（支持父子嵌套；对应安卓 TaskEntity / 网页 state.tasks）。
/// </summary>
public class TaskEntity
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("parentId")] public string? ParentId { get; set; }

    /// <summary>main / side / todo</summary>
    [JsonPropertyName("category")] public string Category { get; set; } = "todo";

    [JsonPropertyName("title")] public string Title { get; set; } = "";

    /// <summary>planning / active / paused / done</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = "planning";

    /// <summary>0-100</summary>
    [JsonPropertyName("progress")] public int Progress { get; set; }

    [JsonPropertyName("note")] public string? Note { get; set; }

    /// <summary>仅 todo 使用，YYYY-MM-DD</summary>
    [JsonPropertyName("dueDate")] public string? DueDate { get; set; }

    /// <summary>YYYY-MM-DD</summary>
    [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = "";

    /// <summary>YYYY-MM-DD</summary>
    [JsonPropertyName("lastModified")] public string LastModified { get; set; } = "";

    /// <summary>最近一次进入 done 的时间（ISO）。非 done 时为 null —— 完成任务数唯一口径。</summary>
    [JsonPropertyName("doneAt")] public string? DoneAt { get; set; }

    /// <summary>排序号。安卓列名 sort_order（`order` 是 SQL 关键字，故改名）。</summary>
    [JsonPropertyName("order")] public int Order { get; set; }
}
