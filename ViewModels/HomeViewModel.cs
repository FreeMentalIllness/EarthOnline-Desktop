using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Data.Models;
using EarthOnline.Desktop.Services;
using Microsoft.EntityFrameworkCore;

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

            // Lv = 周岁（安卓口径）；经验条 = 距下一级生日进度（网页口径）
            var (age, daysToNext, progress, hasBirth) = LifeStats.Compute(p?.BirthDate);
            HasLevel = hasBirth;
            LevelText = hasBirth ? $"Lv.{age}" : "";
            LevelSubText = hasBirth ? $"距 Lv.{age + 1} 还有 {daysToNext} 天" : "";
            LevelProgress = progress;
            var lived = LifeStats.DaysLived(p?.BirthDate);
            DaysLivedText = hasBirth && lived > 0 ? $"🌍 已存活 {lived} 天" : "";

            CustomFields.Clear();
            foreach (var cf in ProfileService.ReadCustomFields(p))
                CustomFields.Add(string.IsNullOrWhiteSpace(cf.Value) ? cf.Label : $"{cf.Label}：{cf.Value}");

            TaskCount = db.Tasks.Count();
            ItemCount = db.Items.Count();
            AchievementCount = db.Achievements.Count(a => a.Unlocked);
            InspirationCount = db.Memos.Count();

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
        ReloadMemos();
        ReloadActivities();
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
            System.Windows.MessageBox.Show("保存日志失败：" + ex.Message, "地球Online",
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
            System.Windows.MessageBox.Show("删除日志失败：" + ex.Message, "地球Online",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }
        ReloadMemos();
        if (InspirationCount > 0) InspirationCount--;
    }
}
