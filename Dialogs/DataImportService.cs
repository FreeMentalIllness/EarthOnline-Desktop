using System.IO;
using System.Text;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Services;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Dialogs;

/// <summary>
/// 数据导入增强（v1.0.5）：CSV 任务表 / Markdown 日记。
/// - CSV：首行为表头，按列名（title/status/category/dueDate/note/progress）容错匹配；
///   status 支持 done/完成 → done，其余按 planning 处理（todo 类自动归 todo）。
/// - Markdown：# / ## 开头的日期标题（yyyy-MM-dd）作为其后日记的 createdAt；
///   列表行去掉 - / * / 数字前缀后整行作为一条日志。
/// 全部走「新增」路径（不合并、不覆盖既有数据），主键独立生成。
/// </summary>
public static class DataImportService
{
    /// <summary>导入 CSV 任务，返回导入条数。</summary>
    public static int ImportCsvTasks(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length == 0) return 0;

        // 表头解析：title 必需
        var header = SplitCsvLine(lines[0]).Select(h => h.Trim().Trim('"').ToLowerInvariant()).ToList();
        int iT = header.IndexOf("title");
        if (iT < 0)
        {
            // 无表头：假定第一列是标题
            iT = -1;
            header = ["title"];
        }
        int iS = header.IndexOf("status"), iC = header.IndexOf("category"),
            iD = header.IndexOf("duedate"), iN = header.IndexOf("note"), iP = header.IndexOf("progress");

        int added = 0, today = 0;
        using var db = new AppDbContext(AppPaths.DbFile);
        var existMax = db.Tasks.AsNoTracking().Any() ? db.Tasks.AsNoTracking().Max(t => t.Order) : 0;
        var start = DateTime.Today;
        for (int i = iT < 0 ? 0 : 1; i < lines.Length; i++)
        {
            var cells = SplitCsvLine(lines[i]);
            if (cells.Count == 0 || cells.All(string.IsNullOrWhiteSpace)) continue;
            var title = (iT >= 0 && iT < cells.Count ? cells[iT] : cells.FirstOrDefault() ?? "").Trim().Trim('"');
            if (title.Length == 0) continue;

            var statusRaw = (iS >= 0 && iS < cells.Count ? cells[iS] : "").Trim().ToLowerInvariant();
            var isDone = statusRaw is "done" or "完成" or "已完" or "已完成" or "true" or "1";
            var category = iC >= 0 && iC < cells.Count ? cells[iC].Trim() : "todo";
            category = category is "main" or "side" or "todo" ? category : "todo";

            var day = start.AddDays(today++).ToString("yyyy-MM-dd");
            db.Tasks.Add(new TaskEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                Category = category,
                Title = title,
                Status = isDone ? "done" : "planning",
                Progress = isDone ? 100 : (iP >= 0 && iP < cells.Count && int.TryParse(cells[iP], out var pg) ? Math.Clamp(pg, 0, 100) : 0),
                Note = iN >= 0 && iN < cells.Count ? cells[iN].Trim().Trim('"') : null,
                DueDate = iD >= 0 && iD < cells.Count && DateOnly.TryParse(cells[iD], out var dd) ? dd.ToString("yyyy-MM-dd") : null,
                CreatedAt = day,
                LastModified = day,
                DoneAt = isDone ? DateTime.Now.ToString("o") : null,
                Order = ++existMax,
            });
            added++;
        }
        if (added > 0) db.SaveChanges();
        return added;
    }

    /// <summary>导入 Markdown 日记，返回导入条数。</summary>
    public static int ImportMarkdownDiary(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        using var db = new AppDbContext(AppPaths.DbFile);
        var now = DateTime.Now;
        var day = DateTime.Today;          // 当前生效日期（# 日期标题可改写）
        int added = 0;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var t = line.Trim();
            if (t.Length == 0) continue;

            // 日期标题：# 2025-06-01 / ## 2025年6月1日
            var head = t.TrimStart('#').Trim();
            if (t.StartsWith("#") && DateOnly.TryParse(head, out var d))
            {
                day = d.ToDateTime(TimeOnly.MinValue);
                continue;
            }

            // 去列表前缀：- / * / 1.
            if (t.StartsWith("- ") || t.StartsWith("* ")) t = t[2..].Trim();
            else if (t.Length > 2 && char.IsDigit(t[0]) && t[1] == '.' && (t[2] == ' ' || char.IsDigit(t[1])))
            {
                var sp = t.IndexOf(' ');
                if (sp > 0) t = t[(sp + 1)..].Trim();
            }
            if (t.Length == 0) continue;

            db.Memos.Add(new MemoEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = t,
                Type = "note",
                CreatedAt = day.Add(new TimeOnly(now.Hour, now.Minute, now.Second).ToTimeSpan()).ToString("o"),
            });
            added++;
        }
        if (added > 0) db.SaveChanges();
        return added;
    }

    /// <summary>单行 CSV 拆分（支持引号包裹的逗号）。</summary>
    private static List<string> SplitCsvLine(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        bool inQuote = false;
        foreach (var ch in line)
        {
            if (ch == '"') { inQuote = !inQuote; continue; }
            if (ch == ',' && !inQuote) { cells.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        cells.Add(sb.ToString());
        return cells;
    }
}
