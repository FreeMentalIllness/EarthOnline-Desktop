using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Data.Models;
using EarthOnline.Desktop.Services;
using Microsoft.EntityFrameworkCore;
using EarthOnline.Desktop.Dialogs;

namespace EarthOnline.Desktop.ViewModels;

/// <summary>主页日志列表行（世界日志卡）。</summary>
public class MemoRow
{
    public string Id { get; init; } = "";
    public string Text { get; init; } = "";
    /// <summary>类型 emoji（随笔📝 重要❗ 灵感💡 心情🎭）。</summary>
    public string TypeEmoji { get; init; } = "📝";
    /// <summary>展示时间（今天显示 时:分，否则 MM-dd）。</summary>
    public string TimeText { get; init; } = "";
}

/// <summary>主页「最近动态」行（任务完成 / 日志 / 成就解锁合成时间轴）。</summary>
public class ActivityRow
{
    public string Emoji { get; init; } = "✨";
    public string Text { get; init; } = "";
    public string TimeText { get; init; } = "";
}

/// <summary>主页徽章墙上的一枚已佩戴徽章（成就）。</summary>
public class BadgeRow
{
    public string Title { get; init; } = "";
    public string SubText { get; init; } = "";
}

/// <summary>
/// 主页视图模型（MVVM，CommunityToolkit.Mvvm）。
/// 负责读取资料与各项统计、世界日志读写，供 HomePage 绑定。
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "未设置昵称";
    [ObservableProperty] private string _birthDate = "";
    [ObservableProperty] private string _signature = "";
    [ObservableProperty] private string _avatarEmoji = "🌍";
    [ObservableProperty] private string _gender = "";

    // ---- 自定义头像图片（非空时优先于 emoji 显示；圆形裁剪由视图负责，对齐安卓 Coil Crop） ----
    [ObservableProperty] private ImageSource? _avatarImage;
    [ObservableProperty] private bool _hasAvatarImage;

    // ---- 等级 / 经验条（Lv=周岁，进度=距下一个生日的天数占比） ----
    [ObservableProperty] private bool _hasLevel;
    [ObservableProperty] private string _levelText = "";
    [ObservableProperty] private string _levelSubText = "";
    [ObservableProperty] private double _levelProgress;   // 0~100
    [ObservableProperty] private string _daysLivedText = "";

    [ObservableProperty] private int _taskCount;
    [ObservableProperty] private int _itemCount;
    [ObservableProperty] private int _achievementCount;
    [ObservableProperty] private int _inspirationCount;

    [ObservableProperty] private ObservableCollection<string> _customFields = new();

    // ---- 世界日志 ----
    [ObservableProperty] private string _memoInput = "";
    [ObservableProperty] private ObservableCollection<MemoRow> _recentMemos = new();
    [ObservableProperty] private bool _noMemos = true;

    // ---- 人生卡 / 最近动态 ----
    [ObservableProperty] private string _lifeCardText = "";
    [ObservableProperty] private ObservableCollection<ActivityRow> _recentActivities = new();
    [ObservableProperty] private bool _noActivities = true;

    // ---- v1.0.3：问候语 / 季节徽章 / 连续记录 / 今日一签 / 历年今日 / 速览 / 称号 / 徽章墙 ----
    [ObservableProperty] private string _greetingText = "";
    [ObservableProperty] private string _seasonBadge = "";

    [ObservableProperty] private string _streakText = "";
    [ObservableProperty] private string _comebackText = "";
    [ObservableProperty] private bool _hasStreakCard;

    [ObservableProperty] private string _dailyPickText = "";
    [ObservableProperty] private string _dailyPickSub = "";
    [ObservableProperty] private bool _hasDailyPick;

    [ObservableProperty] private string _thisDayText = "";
    [ObservableProperty] private bool _hasThisDay;

    [ObservableProperty] private string _titleLine = "";
    [ObservableProperty] private string _levelStatText = "—";
    [ObservableProperty] private int _footprintCount;
    [ObservableProperty] private string _doneRateText = "—";

    [ObservableProperty] private ObservableCollection<BadgeRow> _badges = new();
    [ObservableProperty] private bool _noBadges = true;

    /// <summary>从 SQLite 读取概览（兼容三端数据）。</summary>
    public void Load()
    {
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            var p = db.Profile.FirstOrDefault();

            Name = string.IsNullOrWhiteSpace(p?.Name) ? "未设置昵称" : p!.Name;
            BirthDate = string.IsNullOrWhiteSpace(p?.BirthDate) ? "未设置生日" : p!.BirthDate;
            Signature = p?.Signature ?? "";
            Gender = ProfileService.GenderLabel(p?.Gender);
            AvatarEmoji = ProfileService.AvatarEmoji(p?.AvatarKey);
            LoadAvatarImage(p?.AvatarPath);

            // Lv = 周岁（安卓口径）；经验条 = 距下一级生日进度（网页口径）
            var (age, daysToNext, progress, hasBirth) = LifeStats.Compute(p?.BirthDate);
            HasLevel = hasBirth;
            LevelText = hasBirth ? $"Lv.{age}" : "";
            LevelSubText = hasBirth ? $"距 Lv.{age + 1} 还有 {daysToNext} 天" : "";
            LevelProgress = progress;
            var lived = LifeStats.DaysLived(p?.BirthDate);
            DaysLivedText = hasBirth && lived > 0 ? $"🌍 已存活 {lived} 天" : "";

            // v1.0.3：称号（settings.json）+ 等级 → 「称号 · Lv.X」（称号留空时为「旅行者」）
            var settings = SettingsStore.Load();
            var title = XpRules.TitleFor(settings.CustomTitle);
            TitleLine = hasBirth ? $"{title} · Lv.{age}" : title;
            LevelStatText = hasBirth ? $"Lv.{age}" : "未设置";

            CustomFields.Clear();
            foreach (var cf in ProfileService.ReadCustomFields(p))
                CustomFields.Add(string.IsNullOrWhiteSpace(cf.Value) ? cf.Label : $"{cf.Label}：{cf.Value}");

            TaskCount = db.Tasks.Count();
            ItemCount = db.Items.Count();
            AchievementCount = db.Achievements.Count(a => a.Unlocked);
            InspirationCount = db.Memos.Count();
            FootprintCount = db.Locations.Count();
            int totalTasks = TaskCount;
            int doneTasks = db.Tasks.Count(t => t.Status == "done");
            DoneRateText = totalTasks <= 0 ? "—" : $"{(int)Math.Round(doneTasks * 100.0 / totalTasks)}%";

            // 人生卡：存活天数（整百纪念）+ 生日倒计时 + 累计概览
            if (hasBirth && lived > 0)
            {
                var mile = lived % 100 == 0 ? "　🎉 今天是整百纪念日！" : "";
                LifeCardText = $"🌍 你已在地球Online 存活 {lived} 天{mile}\n" +
                    $"📖 累计完成任务 {TaskCount} 个 · 写下日志 {InspirationCount} 条 · 解锁成就 {AchievementCount} 枚 · 收集物品 {ItemCount} 件";
            }
            else
            {
                LifeCardText = "";
            }
        }
        catch (Exception ex)
        {
            Name = "数据加载失败：" + ex.Message;
        }
        ReloadHomeCards();
        LoadPinnedBadges();
        ReloadMemos();
        ReloadActivities();
    }

    // ==================== v1.0.3：主页卡片（问候 / 连续记录 / 今日一签 / 历年今日） ====================

    /// <summary>
    /// 记录日 = 日志日 ∪ 完成任务日 ∪ 足迹日（GrowthStreak.kt 口径）。
    /// 顺带收集今日一签 / 历年今日的素材池，一次查询全搞定。
    /// </summary>
    private void ReloadHomeCards()
    {
        GreetingText = "";
        SeasonBadge = "";
        StreakText = "";
        ComebackText = "";
        HasStreakCard = false;
        HasDailyPick = false;
        HasThisDay = false;

        try
        {
            var today = DateTime.Today;
            var todayStr = today.ToString("yyyy-MM-dd");
            var season = SeasonTheme.Current();
            SeasonBadge = $"{season.Emoji} {season.Label}";

            var dayKeys = new HashSet<string>();
            var pickPool = new List<(string Emoji, string Text, string Day)>();   // 今日一签素材
            var thisDayPool = new List<(int Year, string Emoji, string Text)>();  // 历年今日素材
            string? latestMood = null;

            using (var db = new AppDbContext(AppPaths.DbFile))
            {
                var mmdd = today.ToString("MM-dd");
                foreach (var m in db.Memos.AsNoTracking().OrderBy(m => m.CreatedAt).ToList())
                {
                    var k = StatsService.DayKeyOf(m.CreatedAt);
                    if (k.Length == 10)
                    {
                        dayKeys.Add(k);
                        // 今日一签：只抽「过去」的日志（今天的不算惊喜）
                        if (string.CompareOrdinal(k, todayStr) < 0)
                            pickPool.Add((TypeEmojiOf(m.Type), (m.Text ?? "").Trim(), k));
                        // 历年今日
                        if (k.EndsWith(mmdd, StringComparison.Ordinal) &&
                            int.TryParse(k.AsSpan(0, 4), out var y) && y < today.Year)
                            thisDayPool.Add((y, TypeEmojiOf(m.Type), (m.Text ?? "").Trim()));
                    }
                    if (latestMood is null && m.Type == "mood" && !string.IsNullOrWhiteSpace(m.Text))
                        latestMood = m.Text;
                }
                foreach (var t in db.Tasks.AsNoTracking().ToList())
                {
                    if (string.IsNullOrEmpty(t.DoneAt)) continue;
                    var k = StatsService.DayKeyOf(t.DoneAt);
                    if (k.Length != 10) continue;
                    dayKeys.Add(k);
                    if (string.CompareOrdinal(k, todayStr) < 0)
                        pickPool.Add(("✅", string.IsNullOrWhiteSpace(t.Title) ? "未命名任务" : t.Title, k));
                    if (k.EndsWith(mmdd, StringComparison.Ordinal) &&
                        int.TryParse(k.AsSpan(0, 4), out var y2) && y2 < today.Year)
                        thisDayPool.Add((y2, "✅", string.IsNullOrWhiteSpace(t.Title) ? "未命名任务" : t.Title));
                }
                foreach (var l in db.Locations.AsNoTracking().ToList())
                {
                    if (string.IsNullOrEmpty(l.Date) || l.Date.Length != 10) continue;
                    dayKeys.Add(l.Date);
                    if (string.CompareOrdinal(l.Date, todayStr) < 0)
                        pickPool.Add(("📍", string.IsNullOrWhiteSpace(l.Name) ? "一处足迹" : l.Name, l.Date));
                    if (l.Date.EndsWith(mmdd, StringComparison.Ordinal) &&
                        int.TryParse(l.Date.AsSpan(0, 4), out var y3) && y3 < today.Year)
                        thisDayPool.Add((y3, "📍", string.IsNullOrWhiteSpace(l.Name) ? "一处足迹" : l.Name));
                }
            }

            // ---- 动态问候语（Greeting.kt 规格）----
            int streak = GrowthStreak.CurrentStreak(dayKeys);
            GreetingText = Greeting.Build(DateTime.Now.Hour, latestMood, streak);

            // ---- 连续记录成长阶段（GrowthStreak.kt 规格）----
            var comeback = GrowthStreak.ComebackMessage(dayKeys, today);
            if (streak > 0)
            {
                int stage = GrowthStreak.Stage(streak);
                StreakText = stage > 0
                    ? $"已连续记录 {streak} 天 · {GrowthStreak.StageLabel(stage)}"
                    : $"已连续记录 {streak} 天 · 再坚持到 3 天就会萌芽 🌱";
            }
            else if (dayKeys.Count == 0)
            {
                StreakText = "写下今天的第一条记录，让星球开始生长 🌱";
            }
            ComebackText = comeback ?? "";
            HasStreakCard = StreakText.Length > 0 || ComebackText.Length > 0;

            // ---- 今日一签：日期做种子的确定性抽取 ----
            if (pickPool.Count > 0)
            {
                int seed = today.Year * 10000 + today.Month * 100 + today.Day;
                var pick = pickPool[new Random(seed).Next(pickPool.Count)];
                DailyPickText = pick.Text.Length > 60 ? pick.Text[..60] + "…" : pick.Text;
                DailyPickSub = $"{pick.Emoji} 来自 {pick.Day}";
                HasDailyPick = true;
            }

            // ---- 历年今日：取最近一个有记录的年份 ----
            if (thisDayPool.Count > 0)
            {
                var best = thisDayPool.OrderBy(x => x.Year).Last();
                var text = best.Text.Length > 50 ? best.Text[..50] + "…" : best.Text;
                ThisDayText = $"{best.Year} 年的今天：{best.Emoji} {text}";
                HasThisDay = true;
            }
        }
        catch { /* 主页卡片属锦上添花，读失败静默降级 */ }
    }

    /// <summary>重新加载徽章墙（佩戴的成就）。佩戴选择保存在 settings.json。</summary>
    public void LoadPinnedBadges()
    {
        Badges.Clear();
        try
        {
            var pinned = SettingsStore.Load().PinnedAchievements;
            if (pinned.Count > 0)
            {
                using var db = new AppDbContext(AppPaths.DbFile);
                var unlocked = db.Achievements.AsNoTracking().Where(a => a.Unlocked).ToList();
                foreach (var id in pinned)
                {
                    var a = unlocked.FirstOrDefault(x => x.Id == id);
                    if (a is null) continue;
                    Badges.Add(new BadgeRow
                    {
                        Title = string.IsNullOrWhiteSpace(a.Title) ? "成就" : a.Title,
                        SubText = StatsService.DayKeyOf(a.UnlockedAt ?? "")
                    });
                }
            }
        }
        catch { /* 读失败静默降级 */ }
        NoBadges = Badges.Count == 0;
    }

    /// <summary>
    /// 加载自定义头像图片：路径存在才启用；OnLoad 一次性读入并 Freeze，
    /// 不持有文件句柄（之后替换/删除头像文件不会被锁）。
    /// </summary>
    private void LoadAvatarImage(string? path)
    {
        AvatarImage = null;
        HasAvatarImage = false;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            AvatarImage = bmp;
            HasAvatarImage = true;
        }
        catch { /* 坏图静默回落 emoji */ }
    }

    // ==================== 最近动态（合成时间轴） ====================
    private static DateTime? TryTime(string? v)
        => DateTime.TryParse(v, out var t) ? t : null;

    public void ReloadActivities()
    {
        RecentActivities.Clear();
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            var today = DateTime.Today;

            var items = new List<(DateTime? Time, string Emoji, string Text, string Raw)>();

            // 任务完成（doneAt 口径与数据页一致）
            foreach (var t in db.Tasks.AsNoTracking().Where(t => t.Status == "done" && t.DoneAt != null))
            {
                var time = TryTime(t.DoneAt);
                if (time is null) continue;
                items.Add((time, "✅", $"完成任务「{(string.IsNullOrWhiteSpace(t.Title) ? "未命名" : t.Title)}」", t.DoneAt!));
            }
            // 日志
            foreach (var m in db.Memos.AsNoTracking())
            {
                var time = TryTime(m.CreatedAt);
                if (time is null) continue;
                items.Add((time, TypeEmojiOf(m.Type), $"写下日志「{m.Text}」", m.CreatedAt));
            }
            // 成就解锁
            foreach (var a in db.Achievements.AsNoTracking().Where(a => a.Unlocked && a.UnlockedAt != null))
            {
                var time = TryTime(a.UnlockedAt);
                if (time is null) continue;
                items.Add((time, "🏆", $"解锁成就「{a.Title}」", a.UnlockedAt!));
            }

            foreach (var it in items.OrderByDescending(x => x.Time).Take(8))
            {
                string time = it.Time!.Value.Date == today
                    ? it.Time.Value.ToString("HH:mm")
                    : it.Time.Value.ToString("MM-dd");
                RecentActivities.Add(new ActivityRow
                {
                    Emoji = it.Emoji,
                    Text = it.Text.Length > 40 ? it.Text[..40] + "…" : it.Text,
                    TimeText = time
                });
            }
        }
        catch { /* 读失败静默降级 */ }
        NoActivities = RecentActivities.Count == 0;
    }

    // ==================== 世界日志 ====================

    private static string TypeEmojiOf(string type) => type switch
    {
        "important" => "❗",
        "idea" => "💡",
        "mood" => "🎭",
        _ => "📝"
    };

    public void ReloadMemos()
    {
        RecentMemos.Clear();
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            var today = DateTime.Today;
            foreach (var m in db.Memos.AsNoTracking()
                         .OrderByDescending(m => m.CreatedAt)
                         .Take(5))
            {
                string time = "";
                if (DateTime.TryParse(m.CreatedAt, out var t))
                {
                    time = t.Date == today ? t.ToString("HH:mm") : t.ToString("MM-dd");
                }
                RecentMemos.Add(new MemoRow
                {
                    Id = m.Id,
                    Text = m.Text,
                    TypeEmoji = TypeEmojiOf(m.Type),
                    TimeText = time
                });
            }
        }
        catch { /* 读失败不弹窗，主页日志卡静默降级 */ }
        NoMemos = RecentMemos.Count == 0;
    }

    /// <summary>写一条世界日志。成功返回 true 并刷新列表与统计。</summary>
    public bool AddMemo(string type, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return false;

        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            db.Memos.Add(new MemoEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = text,
                Type = string.IsNullOrEmpty(type) ? "note" : type,
                // 与网页 doneAt/createdAt 口径一致：ISO UTC
                CreatedAt = DateTime.Now.ToString("o")
            });
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("保存日志失败：" + ex.Message, "地球Online",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return false;
        }

        AchievementNotifier.Check();   // 日志类成就（含凌晨三点等彩蛋）
        ReloadMemos();
        InspirationCount++;
        MemoInput = "";
        return true;
    }

    public void DeleteMemo(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            var row = db.Memos.Find(id);
            if (row is not null) db.Memos.Remove(row);
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("删除日志失败：" + ex.Message, "地球Online",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }
        ReloadMemos();
        if (InspirationCount > 0) InspirationCount--;
    }
}
