using System.Globalization;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Services;

/// <summary>区间汇总：新增任务 / 完成任务 / 新增灵感 / 解锁成就。</summary>
public sealed class RangeSummary
{
    public int NewTasks { get; set; }
    public int DoneTasks { get; set; }
    public int NewMemos { get; set; }
    public int NewAchievements { get; set; }
}

/// <summary>趋势序列（对应网页 buildTrendSeries 的返回结构）。</summary>
public sealed class TrendSeries
{
    /// <summary>'7d' 近 7 天 / '4w' 近 4 周</summary>
    public string Range { get; set; } = "7d";
    public List<string> Labels { get; set; } = new();
    public List<int> Values { get; set; } = new();
    public int Total { get; set; }
}

/// <summary>
/// 看板聚合服务（对应网页 modules/stats.js 的 getDashboardData）。
///
/// 聚合口径（与网页 stats.js 注释、安卓 ReportViewModel 完全一致）：
/// - 新增任务数：dayKeyOf(t.createdAt) 落在窗口内
/// - 完成任务数：t.doneAt 非空且 dayKeyOf(t.doneAt) 落在窗口内
/// - 新增灵感数：全部 memo（note + important + idea 全量统计）
/// - 解锁成就数：a.unlocked 且 dayKeyOf(a.unlockedAt) 落在窗口内
/// </summary>
public static class StatsService
{
    // ==================== 日期工具 ====================

    public static string TodayStr() => DateTime.Today.ToString("yyyy-MM-dd");

    public static string DayOffset(int days) => DateTime.Today.AddDays(days).ToString("yyyy-MM-dd");

    /// <summary>
    /// 取日键：'YYYY-MM-DDTHH:mm:ss…' 与 'YYYY-MM-DD' 两种格式都取前 10 位。
    /// 与网页 dayKeyOf 同口径（纯字符串截取，不做时区换算）。
    /// </summary>
    public static string DayKeyOf(string? ts)
        => string.IsNullOrEmpty(ts) || ts.Length < 10 ? "" : ts.Substring(0, 10);

    /// <summary>闭区间判断（字符串序比较，'YYYY-MM-DD' 定长故等价于日期序）。</summary>
    public static bool InDayRange(string key, string fromDay, string toDay)
        => key.Length == 10
        && string.CompareOrdinal(key, fromDay) >= 0
        && string.CompareOrdinal(key, toDay) <= 0;

    /// <summary>
    /// 把任意时间字符串解析成 DateTime（按本地墙钟时间理解）。
    /// 支持 'YYYY-MM-DD' 与 ISO 两种；解析失败返回 false。
    /// </summary>
    public static bool TryParseTs(string? s, out DateTime dt)
    {
        dt = default;
        if (string.IsNullOrWhiteSpace(s)) return false;

        if (s.Length >= 19 &&
            DateTime.TryParseExact(s.Substring(0, 19), "yyyy-MM-ddTHH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var withTime))
        {
            dt = DateTime.SpecifyKind(withTime, DateTimeKind.Local);
            return true;
        }

        if (s.Length >= 10 &&
            DateTime.TryParseExact(s.Substring(0, 10), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            dt = DateTime.SpecifyKind(dateOnly, DateTimeKind.Local);
            return true;
        }

        return false;
    }

    // ==================== 聚合 ====================

    /// <summary>窗口内完成的任务数（趋势分桶复用）。</summary>
    public static int CountDoneInRange(List<TaskEntity> tasks, string startDay, string endDay)
    {
        int n = 0;
        foreach (var t in tasks)
        {
            if (string.IsNullOrEmpty(t.DoneAt)) continue;
            if (InDayRange(DayKeyOf(t.DoneAt), startDay, endDay)) n++;
        }
        return n;
    }

    /// <summary>区间汇总（闭区间）。</summary>
    public static RangeSummary BuildRangeSummary(
        List<TaskEntity> tasks, List<MemoEntity> memos, List<AchievementEntity> achs,
        string startDay, string endDay)
    {
        var s = new RangeSummary();
        foreach (var t in tasks)
        {
            if (InDayRange(DayKeyOf(t.CreatedAt), startDay, endDay)) s.NewTasks++;
            if (!string.IsNullOrEmpty(t.DoneAt) &&
                InDayRange(DayKeyOf(t.DoneAt), startDay, endDay)) s.DoneTasks++;
        }
        foreach (var m in memos)
        {
            if (InDayRange(DayKeyOf(m.CreatedAt), startDay, endDay)) s.NewMemos++;
        }
        foreach (var a in achs)
        {
            if (!a.Unlocked || string.IsNullOrEmpty(a.UnlockedAt)) continue;
            if (InDayRange(DayKeyOf(a.UnlockedAt), startDay, endDay)) s.NewAchievements++;
        }
        return s;
    }

    /// <summary>年度汇总：今年新增的任务 / 今年完成的任务（保证完成率 ∈[0,100]）。</summary>
    public static RangeSummary BuildYearSummary(
        List<TaskEntity> tasks, List<MemoEntity> memos, List<AchievementEntity> achs, int year)
        => BuildRangeSummary(tasks, memos, achs, $"{year}-01-01", $"{year}-12-31");

    /// <summary>
    /// 趋势序列：'7d' 逐日完成任务数；'4w' 按 [-27,-21] [-20,-14] [-13,-7] [-6,0] 四周分桶。
    /// 桶边界与网页 buildTrendSeries 逐字一致。
    /// </summary>
    public static TrendSeries BuildTrendSeries(List<TaskEntity> tasks, string range)
    {
        var mode = range == "4w" ? "4w" : "7d";
        var series = new TrendSeries { Range = mode };
        var today = TodayStr();

        if (mode == "7d")
        {
            for (int i = 6; i >= 0; i--)
            {
                var k = DayOffset(-i);
                series.Labels.Add(k.Substring(5, 2) + "/" + k.Substring(8, 2)); // MM/DD
                series.Values.Add(CountDoneInRange(tasks, k, k));
            }
        }
        else
        {
            var buckets = new[] { (-27, -21), (-20, -14), (-13, -7), (-6, 0) };
            var names = new[] { "4周前", "3周前", "2周前", "本周" };
            for (int i = 0; i < buckets.Length; i++)
            {
                series.Labels.Add(names[i]);
                series.Values.Add(CountDoneInRange(tasks, DayOffset(buckets[i].Item1), DayOffset(buckets[i].Item2)));
            }
        }

        foreach (var v in series.Values) series.Total += v;
        return series;
    }

    /// <summary>按日聚合完成任务数（供日历视图着色）。</summary>
    public static Dictionary<string, int> DoneCountByDay(List<TaskEntity> tasks)
    {
        var map = new Dictionary<string, int>();
        foreach (var t in tasks)
        {
            if (string.IsNullOrEmpty(t.DoneAt)) continue;
            var k = DayKeyOf(t.DoneAt);
            if (k.Length != 10) continue;
            map[k] = map.TryGetValue(k, out var c) ? c + 1 : 1;
        }
        return map;
    }

    // ==================== 读取 ====================

    /// <summary>一次性读取看板所需的全部数据（AsNoTracking，读多写少）。</summary>
    public static (List<TaskEntity> Tasks, List<MemoEntity> Memos, List<AchievementEntity> Achs) LoadAll()
    {
        using var db = new AppDbContext(AppPaths.DbFile);
        return (
            db.Tasks.AsNoTracking().ToList(),
            db.Memos.AsNoTracking().ToList(),
            db.Achievements.AsNoTracking().ToList()
        );
    }
}
