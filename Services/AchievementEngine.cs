using System.IO;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Services;

/// <summary>
/// 自动成就引擎（与安卓 ui/achievements/AchievementCatalog.kt 的 AUTO_RULES 一一对应：
/// 27 条常规 + 18 条彩蛋 = 45 条）。
/// 规则表只在这里维护一份；引擎负责建行、回填分类、判定解锁。
/// </summary>
public static class AchievementEngine
{
    public sealed class AchStats
    {
        public int TasksDone { get; set; }
        public int Items { get; set; }
        public int Collections { get; set; }
        public int Memos { get; set; }
        public int Locations { get; set; }
        public int DaysLived { get; set; }
        public int Age { get; set; }
        public string Gender { get; set; } = "";
        public int Am3Memos { get; set; }
        public int NightOwlMemos { get; set; }
        public int EarlyBirdMemos { get; set; }
        public int Am3TasksDone { get; set; }
        /// <summary>空白标题保存被拒次数（唯一需持久化的计数，来自 SettingsStore）。</summary>
        public int BlankTitleTries { get; set; }
        public int LongTitleTasks { get; set; }
        public int EmojiTitleTasks { get; set; }
        public int PeriodTitleTasks { get; set; }
        public int TestTitleTasks { get; set; }
        public int OverdueOpenTodos { get; set; }
        public int MaxDoneInDay { get; set; }
        public int RecordStreak { get; set; }
        public int EmojiOnlyMemos { get; set; }
        public int NewYearBirth { get; set; }
    }

    public sealed class AutoRule
    {
        public string Key = "";
        public string Title = "";
        public string Desc = "";
        public string Category = "custom";
        public int Goal;
        public Func<AchStats, int> Current = _ => 0;
        public Func<AchStats, bool>? Test;

        public int GoalOf() => Math.Max(Goal, 1);
        public int ProgressOf(AchStats s) => Math.Min(Current(s), GoalOf());
        public bool Satisfied(AchStats s) => Test is not null ? Test(s) : (Goal > 0 && Current(s) >= Goal);
    }

    private static int MultiCount(AchStats s, int threshold) => new[]
    {
        s.TasksDone >= threshold, s.Items >= threshold, s.Collections >= threshold,
        s.Memos >= threshold, s.Locations >= threshold
    }.Count(x => x);

    /// <summary>与安卓 AUTO_RULES 完全一致的 45 条规则。</summary>
    public static readonly List<AutoRule> Rules = new()
    {
        // ————— 任务 —————
        new AutoRule { Key="first_task", Title="初出茅庐", Desc="完成你的第一个任务", Category="task", Goal=1, Current=s=>s.TasksDone },
        new AutoRule { Key="five_tasks", Title="渐入佳境", Desc="累计完成 5 个任务", Category="task", Goal=5, Current=s=>s.TasksDone },
        new AutoRule { Key="ten_tasks", Title="小有成就", Desc="累计完成 10 个任务", Category="task", Goal=10, Current=s=>s.TasksDone },
        new AutoRule { Key="thirty_tasks", Title="三十而立", Desc="累计完成 30 个任务", Category="task", Goal=30, Current=s=>s.TasksDone },
        new AutoRule { Key="fifty_tasks", Title="任务大师", Desc="累计完成 50 个任务", Category="task", Goal=50, Current=s=>s.TasksDone },
        new AutoRule { Key="hundred_tasks", Title="百炼成钢", Desc="累计完成 100 个任务", Category="task", Goal=100, Current=s=>s.TasksDone },

        // ————— 背包 —————
        new AutoRule { Key="first_item", Title="初拾一物", Desc="拾取你的第一个物品", Category="bag", Goal=1, Current=s=>s.Items },
        new AutoRule { Key="ten_items", Title="背包满满", Desc="累计收集 10 个物品", Category="bag", Goal=10, Current=s=>s.Items },
        new AutoRule { Key="fifty_items", Title="移动仓库", Desc="累计收集 50 个物品", Category="bag", Goal=50, Current=s=>s.Items },

        // ————— 收藏 —————
        new AutoRule { Key="first_collection", Title="珍藏", Desc="完成第一次收藏", Category="collection", Goal=1, Current=s=>s.Collections },
        new AutoRule { Key="ten_collections", Title="博览群书", Desc="累计 10 条收藏", Category="collection", Goal=10, Current=s=>s.Collections },
        new AutoRule { Key="fifty_collections", Title="收藏大家", Desc="累计 50 条收藏", Category="collection", Goal=50, Current=s=>s.Collections },

        // ————— 足迹 —————
        new AutoRule { Key="first_location", Title="探索者", Desc="记录第一个足迹", Category="map", Goal=1, Current=s=>s.Locations },
        new AutoRule { Key="five_locations", Title="走南闯北", Desc="记录 5 个足迹", Category="map", Goal=5, Current=s=>s.Locations },
        new AutoRule { Key="twenty_locations", Title="环游世界", Desc="记录 20 个足迹", Category="map", Goal=20, Current=s=>s.Locations },

        // ————— 日志 —————
        new AutoRule { Key="first_memo", Title="记录者", Desc="写下第一条世界日志", Category="memo", Goal=1, Current=s=>s.Memos },
        new AutoRule { Key="ten_memos", Title="笔耕不辍", Desc="累计 10 条世界日志", Category="memo", Goal=10, Current=s=>s.Memos },
        new AutoRule { Key="fifty_memos", Title="生活诗人", Desc="累计 50 条世界日志", Category="memo", Goal=50, Current=s=>s.Memos },

        // ————— 成长 —————
        new AutoRule { Key="day_100", Title="百日之约", Desc="来到地球满 100 天", Category="growth", Goal=100, Current=s=>s.DaysLived },
        new AutoRule { Key="day_365", Title="一周岁", Desc="来到地球满 365 天", Category="growth", Goal=365, Current=s=>s.DaysLived },
        new AutoRule { Key="day_1000", Title="千日之行", Desc="来到地球满 1000 天", Category="growth", Goal=1000, Current=s=>s.DaysLived },
        new AutoRule { Key="day_5000", Title="万水千山", Desc="来到地球满 5000 天", Category="growth", Goal=5000, Current=s=>s.DaysLived },
        new AutoRule { Key="day_10000", Title="万日玩家", Desc="来到地球满 10000 天", Category="growth", Goal=10000, Current=s=>s.DaysLived },
        new AutoRule { Key="adult_18", Title="成年礼", Desc="等级（周岁）达到 18", Category="growth", Goal=18, Current=s=>s.Age },

        // ————— 综合（复合条件） —————
        new AutoRule { Key="all_rounder", Title="全能玩家", Desc="任务 / 物品 / 收藏 / 日志 / 足迹各至少 1", Category="general", Goal=5,
            Current=s=>MultiCount(s,1), Test=s=>MultiCount(s,1)==5 },
        new AutoRule { Key="five_star", Title="五光十色", Desc="任务20 · 物品20 · 收藏10 · 日志10 · 足迹5", Category="general", Goal=5,
            Current=s=>new[]{ s.TasksDone>=20, s.Items>=20, s.Collections>=10, s.Memos>=10, s.Locations>=5 }.Count(x=>x),
            Test=s=>s.TasksDone>=20 && s.Items>=20 && s.Collections>=10 && s.Memos>=10 && s.Locations>=5 },
        new AutoRule { Key="grand_slam", Title="大满贯", Desc="任务 / 物品 / 收藏 / 日志 / 足迹各达到 20", Category="general", Goal=5,
            Current=s=>MultiCount(s,20), Test=s=>MultiCount(s,20)==5 },

        // ————— 彩蛋（未解锁时 UI 打码） —————
        new AutoRule { Key="egg_walmart", Title="购物袋玩家", Desc="把性别设置成「沃尔玛购物袋」", Category="egg", Goal=1,
            Current=s=>s.Gender=="walmart"?1:0, Test=s=>s.Gender=="walmart" },
        new AutoRule { Key="egg_gender_fluid", Title="性别是流动的", Desc="性别选一个更离谱的答案", Category="egg", Goal=1,
            Current=s=>(s.Gender=="helicopter"||s.Gender=="potato")?1:0,
            Test=s=>s.Gender=="helicopter"||s.Gender=="potato" },
        new AutoRule { Key="egg_3am", Title="凌晨三点俱乐部", Desc="在凌晨 3 点写下一条世界日志", Category="egg", Goal=1, Current=s=>s.Am3Memos },
        new AutoRule { Key="egg_night_owl", Title="夜猫子", Desc="累计 3 条写于 0~5 点的日志", Category="egg", Goal=3, Current=s=>s.NightOwlMemos },
        new AutoRule { Key="egg_early_bird", Title="早起的鸟儿", Desc="累计 3 条写于 5~7 点的日志", Category="egg", Goal=3, Current=s=>s.EarlyBirdMemos },
        new AutoRule { Key="egg_midnight_task", Title="肝帝", Desc="在凌晨 3 点完成一个任务", Category="egg", Goal=1, Current=s=>s.Am3TasksDone },
        new AutoRule { Key="egg_blank_title", Title="空白也是一种态度", Desc="连续 3 次想保存一个空标题", Category="egg", Goal=3, Current=s=>s.BlankTitleTries },
        new AutoRule { Key="egg_long_title", Title="一句话说不完", Desc="给任务起一个 ≥30 字的标题", Category="egg", Goal=1, Current=s=>s.LongTitleTasks },
        new AutoRule { Key="egg_emoji_title", Title="表情包本人", Desc="任务标题里塞进 ≥5 个 emoji", Category="egg", Goal=1, Current=s=>s.EmojiTitleTasks },
        new AutoRule { Key="egg_period_title", Title="句号强迫症", Desc="累计 3 个以「。」结尾的任务标题", Category="egg", Goal=3, Current=s=>s.PeriodTitleTasks },
        new AutoRule { Key="egg_test_title", Title="测试工程师", Desc="累计 3 个名字里带「测试」的任务", Category="egg", Goal=3, Current=s=>s.TestTitleTasks },
        new AutoRule { Key="egg_overdue", Title="拖延症晚期", Desc="同时挂着 5 个逾期待办", Category="egg", Goal=5, Current=s=>s.OverdueOpenTodos },
        new AutoRule { Key="egg_marathon", Title="一日十杀", Desc="同一天里完成 10 个任务", Category="egg", Goal=10, Current=s=>s.MaxDoneInDay },
        new AutoRule { Key="egg_streak_7", Title="七日之约", Desc="连续 7 天记录世界日志", Category="egg", Goal=7, Current=s=>s.RecordStreak },
        new AutoRule { Key="egg_streak_30", Title="一个月不断更", Desc="连续 30 天记录世界日志", Category="egg", Goal=30, Current=s=>s.RecordStreak },
        new AutoRule { Key="egg_streak_365", Title="全年无休", Desc="连续 365 天记录世界日志", Category="egg", Goal=365, Current=s=>s.RecordStreak },
        new AutoRule { Key="egg_memo_emoji", Title="此时无声胜有声", Desc="写一条只有表情的日志", Category="egg", Goal=1, Current=s=>s.EmojiOnlyMemos },
        new AutoRule { Key="egg_newyear", Title="元旦宝宝", Desc="生日是 1 月 1 日", Category="egg", Goal=1, Current=s=>s.NewYearBirth, Test=s=>s.NewYearBirth>0 },
    };

    /// <summary>彩蛋未解锁时的占位标题 / 说明（与安卓 ACH_EGG_MASK 一致）。</summary>
    public const string EggMaskTitle = "？？？";
    public const string EggMaskDesc = "隐藏成就 · 达成条件保密。触发一次，就会自己现身。";

    /// <summary>
    /// 计算统计 → 判定全部规则 → 写库（建行 / 回填分类 / 标记解锁）。
    /// 返回本次新解锁的条数。
    /// </summary>
    public static int Evaluate()
    {
        using var db = new AppDbContext(AppPaths.DbFile);
        var s = ComputeStats(db);
        int newly = 0;

        foreach (var rule in Rules)
        {
            var row = db.Achievements.Find(rule.Key);
            bool ok = rule.Satisfied(s);

            if (row is null)
            {
                row = new AchievementEntity
                {
                    Id = rule.Key,
                    Title = rule.Title,
                    Desc = rule.Desc,
                    Type = "auto",
                    AutoKey = rule.Key,
                    Category = rule.Category,
                    Unlocked = ok,
                    UnlockedAt = ok ? DateTime.Now.ToString("o") : null
                };
                db.Achievements.Add(row);
                if (ok) newly++;
            }
            else
            {
                // 回填分类 / 标题（旧行可能缺）
                row.Category = rule.Category;
                row.Type = "auto";
                row.AutoKey = rule.Key;
                if (string.IsNullOrEmpty(row.Title)) row.Title = rule.Title;
                if (string.IsNullOrEmpty(row.Desc)) row.Desc = rule.Desc;

                if (ok && !row.Unlocked)
                {
                    row.Unlocked = true;
                    row.UnlockedAt = DateTime.Now.ToString("o");
                    newly++;
                }
                // 已解锁的成就永不回退（与安卓一致：撤销只针对手动成就）
            }
        }

        db.SaveChanges();
        return newly;
    }

    private static AchStats ComputeStats(AppDbContext db)
    {
        var tasks = db.Tasks.AsNoTracking().ToList();
        var memos = db.Memos.AsNoTracking().ToList();
        var profile = db.Profile.FirstOrDefault();

        var s = new AchStats
        {
            TasksDone = tasks.Count(t => t.Status == "done"),
            Items = db.Items.Count(),
            Collections = db.Collections.Count(),
            Memos = memos.Count,
            Locations = db.Locations.Count(),
            Gender = profile?.Gender ?? "",
            BlankTitleTries = SettingsStore.Load().BlankTitleTries
        };

        // 存活天数 / 周岁
        if (DateTime.TryParse(profile?.BirthDate, out var birth))
        {
            s.DaysLived = (DateTime.Today - birth.Date).Days;
            s.Age = DateTime.Today.Year - birth.Year -
                    (DateTime.Today < birth.AddYears(DateTime.Today.Year - birth.Year) ? 1 : 0);
            s.NewYearBirth = (birth.Month == 1 && birth.Day == 1) ? 1 : 0;
        }

        // 日志时间特征
        foreach (var m in memos)
        {
            var h = HourOf(m.CreatedAt);
            if (h == 3) s.Am3Memos++;
            if (h is >= 0 and < 5) s.NightOwlMemos++;
            if (h is >= 5 and < 7) s.EarlyBirdMemos++;
            if (CountEmoji(m.Text) >= 2 && StripNonText(m.Text).Length == 0) s.EmojiOnlyMemos++;
        }
        s.RecordStreak = LongestStreak(memos.Select(m => DayKeyOf(m.CreatedAt)));

        // 任务文本特征
        foreach (var t in tasks)
        {
            if (t.Status == "done" && HourOf(t.DoneAt) == 3) s.Am3TasksDone++;
            if ((t.Title ?? "").Length >= 30) s.LongTitleTasks++;
            if (CountEmoji(t.Title ?? "") >= 5) s.EmojiTitleTasks++;
            if ((t.Title ?? "").EndsWith("。")) s.PeriodTitleTasks++;
            var title = t.Title ?? "";
            if (title.Contains("测试") || title.Contains("test", StringComparison.OrdinalIgnoreCase)) s.TestTitleTasks++;
            if (t.Category == "todo" && t.Status != "done" && !string.IsNullOrEmpty(t.DueDate) &&
                DateTime.TryParse(t.DueDate, out var due) && due.Date < DateTime.Today) s.OverdueOpenTodos++;
        }
        s.MaxDoneInDay = tasks.Where(t => t.Status == "done" && !string.IsNullOrEmpty(t.DoneAt))
            .GroupBy(t => DayKeyOf(t.DoneAt))
            .Select(g => g.Count())
            .DefaultIfEmpty(0)
            .Max();

        return s;
    }

    // ---------- 小工具 ----------

    private static int HourOf(string? iso)
    {
        if (DateTime.TryParse(iso, out var dt)) return dt.Hour;
        return -1;
    }

    private static string DayKeyOf(string? iso)
    {
        if (DateTime.TryParse(iso, out var dt)) return dt.ToString("yyyy-MM-dd");
        return (iso ?? "").Length >= 10 ? iso!.Substring(0, 10) : "";
    }

    /// <summary>连续记录天数（日键去重后按自然日相邻计数，对应安卓 longestStreak）。</summary>
    private static int LongestStreak(IEnumerable<string> days)
    {
        var set = days.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).Distinct().OrderBy(x => x).ToList();
        if (set.Count == 0) return 0;
        int best = 1, cur = 1;
        DateTime? prev = null;
        foreach (var d in set)
        {
            if (!DateTime.TryParse(d, out var dt)) continue;
            cur = (prev is not null && (dt - prev.Value).TotalDays <= 1.5 && dt > prev) ? cur + 1 : 1;
            prev = dt;
            if (cur > best) best = cur;
        }
        return best;
    }

    /// <summary>统计 emoji 个数（代理对 + 常见符号区，与安卓 countEmoji / 网页 EMOJI_RE 同口径）。</summary>
    private static int CountEmoji(string text)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { count++; i++; continue; }
            int code = c;
            if (code is >= 0x2600 and <= 0x27BF || code is >= 0x2B00 and <= 0x2BFF ||
                code is >= 0x2190 and <= 0x21FF || code is >= 0x2300 and <= 0x23FF || code is >= 0x25A0 and <= 0x25FF)
                count++;
        }
        return count;
    }

    private static string StripNonText(string text) =>
        new string(text.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray());
}
