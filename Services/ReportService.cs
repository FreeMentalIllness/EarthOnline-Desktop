using System.Globalization;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Services;

/// <summary>报告周期（对应安卓 ReportKind：日报 / 周报 / 年报）。</summary>
public enum ReportKind
{
    Day,
    Week,
    Year
}

/// <summary>报告结果（对应安卓 ReportUiState）。</summary>
public sealed class ReportData
{
    public ReportKind Kind { get; set; } = ReportKind.Day;
    public string RangeLabel { get; set; } = "";
    public string ChartTitle { get; set; } = "";

    /// <summary>true = 柱状图；false = 折线图</summary>
    public bool IsBar { get; set; }

    public int TasksDone { get; set; }
    public int Memos { get; set; }
    public int Achievements { get; set; }
    public int Locations { get; set; }
    public int Xp { get; set; }

    public List<string> ChartLabels { get; set; } = new();
    public List<int> ChartValues { get; set; } = new();

    public string Summary { get; set; } = "";
    public List<string> Highlights { get; set; } = new();
}

/// <summary>
/// 周期性报告服务（日报 / 周报 / 年报）。
/// 数据流与安卓 ReportViewModel 一致：周期 → 区间 → 各表聚合 → UI 状态。
///
/// 区间口径（对应安卓 util/DateUtils）：
/// - 日报：今天 00:00（含）~ 明天 00:00（不含）；日键 today ~ today
/// - 周报：today-6 00:00（含）~ 明天 00:00（不含）；日键 today-6 ~ today
/// - 年报：今年 1/1 00:00（含）~ 明年 1/1 00:00（不含）；日键 YYYY-01-01 ~ YYYY-12-31
/// tasks/memos/achievements 走 ISO 左闭右开，locations.date 走日键闭区间（该字段是 YYYY-MM-DD）。
/// </summary>
public static class ReportService
{
    private sealed class Range
    {
        public DateTime From { get; init; }
        public DateTime To { get; init; }
        public string FromDay { get; init; } = "";
        public string ToDay { get; init; } = "";
    }

    private static Range RangeOf(ReportKind kind)
    {
        var today = DateTime.Today;
        return kind switch
        {
            ReportKind.Week => new Range
            {
                From = today.AddDays(-6),
                To = today.AddDays(1),
                FromDay = today.AddDays(-6).ToString("yyyy-MM-dd"),
                ToDay = today.ToString("yyyy-MM-dd")
            },
            ReportKind.Year =>
            new Range
            {
                From = new DateTime(today.Year, 1, 1),
                To = new DateTime(today.Year + 1, 1, 1),
                FromDay = $"{today.Year}-01-01",
                ToDay = $"{today.Year}-12-31"
            },
            _ => new Range
            {
                From = today,
                To = today.AddDays(1),
                FromDay = today.ToString("yyyy-MM-dd"),
                ToDay = today.ToString("yyyy-MM-dd")
            }
        };
    }

    /// <summary>生成指定周期的报告。</summary>
    public static ReportData Build(ReportKind kind)
    {
        var r = RangeOf(kind);

        using var db = new AppDbContext(AppPaths.DbFile);

        var doneList = db.Tasks.AsNoTracking()
            .Where(t => t.DoneAt != null)
            .ToList()
            .Where(t => StatsService.TryParseTs(t.DoneAt, out var d) && d >= r.From && d < r.To)
            .OrderBy(t => t.DoneAt)
            .Take(500)
            .ToList();

        var memoList = db.Memos.AsNoTracking().ToList()
            .Where(m => StatsService.TryParseTs(m.CreatedAt, out var d) && d >= r.From && d < r.To)
            .OrderBy(m => m.CreatedAt)
            .Take(500)
            .ToList();

        var achList = db.Achievements.AsNoTracking()
            .Where(a => a.Unlocked && a.UnlockedAt != null)
            .ToList()
            .Where(a => StatsService.TryParseTs(a.UnlockedAt, out var d) && d >= r.From && d < r.To)
            .OrderBy(a => a.UnlockedAt)
            .Take(500)
            .ToList();

        var locations = db.Locations.AsNoTracking().ToList()
            .Count(l => !string.IsNullOrEmpty(l.Date)
                        && StatsService.InDayRange(l.Date, r.FromDay, r.ToDay));

        int tasksDone = doneList.Count;
        int memos = memoList.Count;
        int achievements = achList.Count;

        int xp = XpRules.Of(tasksDone, achievements, memos, locations);

        return kind switch
        {
            ReportKind.Week => BuildWeek(r, tasksDone, memos, achievements, locations, xp, memoList, doneList, achList),
            ReportKind.Year => BuildYear(r, tasksDone, memos, achievements, locations, xp, memoList, doneList, achList),
            _ => BuildDay(r, tasksDone, memos, achievements, locations, xp, memoList, doneList, achList)
        };
    }

    // ==================== 日报 ====================

    private static ReportData BuildDay(Range r, int tasksDone, int memos, int achievements, int locations,
        int xp, List<MemoEntity> memoList, List<TaskEntity> doneList, List<AchievementEntity> achList)
    {
        var buckets = new int[24];
        foreach (var m in memoList)
            if (StatsService.TryParseTs(m.CreatedAt, out var d)) buckets[d.Hour]++;
        foreach (var t in doneList)
            if (StatsService.TryParseTs(t.DoneAt, out var d)) buckets[d.Hour]++;
        foreach (var a in achList)
            if (StatsService.TryParseTs(a.UnlockedAt, out var d)) buckets[d.Hour]++;

        var labels = new List<string>();
        for (int h = 0; h < 24; h++) labels.Add(h % 3 == 0 ? h.ToString("D2", CultureInfo.InvariantCulture) : "");

        return new ReportData
        {
            Kind = ReportKind.Day,
            RangeLabel = r.FromDay,
            ChartTitle = "今日 24 小时活跃分布",
            IsBar = true,
            TasksDone = tasksDone,
            Memos = memos,
            Achievements = achievements,
            Locations = locations,
            Xp = xp,
            ChartLabels = labels,
            ChartValues = buckets.ToList(),
            Summary = DaySummary(tasksDone, memos, achievements, locations, xp, buckets),
            Highlights = HighlightsOf(memoList, doneList, achList)
        };
    }

    private static string DaySummary(int tasksDone, int memos, int achievements, int locations, int xp, int[] buckets)
    {
        int total = buckets.Sum();
        if (total == 0) return "今天还没有任何记录。写下第一条世界日志，就算开局了。";

        int peakHour = -1, peakVal = 0;
        for (int i = 0; i < buckets.Length; i++)
        {
            if (buckets[i] > peakVal) { peakVal = buckets[i]; peakHour = i; }
        }
        var peakText = peakHour >= 0 ? $"{peakHour}:00 前后最活跃" : "分布比较均匀";

        return $"今天共 {total} 次记录，{peakText}。" +
               $"完成 {tasksDone} 个任务、记下 {memos} 条灵感，" +
               $"解锁 {achievements} 个成就，获得 {xp} 点经验。";
    }

    // ==================== 周报 ====================

    private static ReportData BuildWeek(Range r, int tasksDone, int memos, int achievements, int locations,
        int xp, List<MemoEntity> memoList, List<TaskEntity> doneList, List<AchievementEntity> achList)
    {
        var days = Enumerable.Range(0, 7).Select(i => r.To.AddDays(-7 + i).ToString("yyyy-MM-dd")).ToList();
        var index = days.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        var buckets = new int[7];

        void Bump(string? ts)
        {
            if (!StatsService.TryParseTs(ts, out var d)) return;
            if (index.TryGetValue(d.ToString("yyyy-MM-dd"), out var i)) buckets[i]++;
        }
        foreach (var m in memoList) Bump(m.CreatedAt);
        foreach (var t in doneList) Bump(t.DoneAt);
        foreach (var a in achList) Bump(a.UnlockedAt);

        int total = buckets.Sum();
        int active = buckets.Count(v => v > 0);
        var summary = total == 0
            ? "这一周还是空的。明天先完成一个小任务试试。"
            : $"近 7 天有 {active} 天留下记录，合计 {total} 次。" +
              $"完成任务 {tasksDone} 个，新增灵感 {memos} 条，" +
              $"解锁成就 {achievements} 个，标记足迹 {locations} 处。";

        return new ReportData
        {
            Kind = ReportKind.Week,
            RangeLabel = $"{days[0].Substring(5).Replace('-', '/')} – {days[^1].Substring(5).Replace('-', '/')}",
            ChartTitle = "近 7 天活跃趋势",
            IsBar = false,
            TasksDone = tasksDone,
            Memos = memos,
            Achievements = achievements,
            Locations = locations,
            Xp = xp,
            ChartLabels = days.Select(d => WeekdayLabel(d)).ToList(),
            ChartValues = buckets.ToList(),
            Summary = summary,
            Highlights = HighlightsOf(memoList, doneList, achList)
        };
    }

    private static string WeekdayLabel(string day)
    {
        if (!DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return day.Substring(Math.Max(0, day.Length - 2));
        return d.DayOfWeek switch
        {
            DayOfWeek.Sunday => "周日",
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            _ => "周六"
        };
    }

    // ==================== 年报 ====================

    private static ReportData BuildYear(Range r, int tasksDone, int memos, int achievements, int locations,
        int xp, List<MemoEntity> memoList, List<TaskEntity> doneList, List<AchievementEntity> achList)
    {
        var buckets = new int[12];
        foreach (var m in memoList)
            if (StatsService.TryParseTs(m.CreatedAt, out var d)) buckets[d.Month - 1]++;
        foreach (var t in doneList)
            if (StatsService.TryParseTs(t.DoneAt, out var d)) buckets[d.Month - 1]++;
        foreach (var a in achList)
            if (StatsService.TryParseTs(a.UnlockedAt, out var d)) buckets[d.Month - 1]++;

        int total = buckets.Sum();
        int bestMonth = -1, bestVal = 0;
        for (int i = 0; i < buckets.Length; i++)
        {
            if (buckets[i] > bestVal) { bestVal = buckets[i]; bestMonth = i; }
        }

        var summary = total == 0
            ? "今年还没有记录。人生存档从第一条开始。"
            : $"今年共 {total} 次记录，{(bestMonth >= 0 ? $"最活跃的是 {bestMonth + 1} 月" : "各月分布相近")}。" +
              $"完成任务 {tasksDone} 个，解锁成就 {achievements} 个，" +
              $"新增灵感 {memos} 条、足迹 {locations} 处。";

        return new ReportData
        {
            Kind = ReportKind.Year,
            RangeLabel = $"{r.From.Year} 年",
            ChartTitle = $"{r.From.Year} 年逐月活跃分布",
            IsBar = true,
            TasksDone = tasksDone,
            Memos = memos,
            Achievements = achievements,
            Locations = locations,
            Xp = xp,
            ChartLabels = Enumerable.Range(1, 12).Select(m => m.ToString(CultureInfo.InvariantCulture)).ToList(),
            ChartValues = buckets.ToList(),
            Summary = summary,
            Highlights = new List<string>()   // 年报不展示 highlights，避免过长（与安卓一致）
        };
    }

    // ==================== 亮点 ====================

    private static List<string> HighlightsOf(
        List<MemoEntity> memoList, List<TaskEntity> doneList, List<AchievementEntity> achList)
    {
        var outList = new List<string>();
        foreach (var a in achList.Take(3)) outList.Add($"🏆 解锁「{Fallback(a.Title, "成就")}」");
        foreach (var t in doneList.Take(3)) outList.Add($"✅ 完成「{Fallback(t.Title, "任务")}」");
        foreach (var m in memoList.Take(2))
        {
            var text = (m.Text ?? "").Trim().Replace('\n', ' ');
            outList.Add($"💭 {(text.Length > 18 ? text.Substring(0, 18) + "…" : text)}");
        }
        return outList;
    }

    private static string Fallback(string s, string def) => string.IsNullOrWhiteSpace(s) ? def : s;
}
