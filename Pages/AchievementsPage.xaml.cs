using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Dialogs;
using EarthOnline.Desktop.Services;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Pages;

/// <summary>成就展示行（含彩蛋打码与 current/goal 进度）。</summary>
public class AchRow
{
    public string Id { get; set; } = "";
    public string Emoji { get; set; } = "🏅";
    public string DisplayTitle { get; set; } = "";
    public string DisplayDesc { get; set; } = "";
    public string StatusText { get; set; } = "";
    public bool IsCustom { get; set; }

    /// <summary>自动成就且未解锁时显示 0~100 进度。</summary>
    public bool ShowProgress { get; set; }
    public int ProgressValue { get; set; }
    public string ProgressText { get; set; } = "";

    /// <summary>分组键字符串（含分类 emoji / 名称 / 已解锁 x/y），同分类行共享同一字符串才归入一组。</summary>
    public string CategoryGroupLabel { get; set; } = "";

    /// <summary>分组排序号（按 CatMap 定义顺序）。</summary>
    public int SortOrder { get; set; }
}

public partial class AchievementsPage : Page
{
    /// <summary>分类 → emoji / 名称（与安卓 ACH_CATEGORIES 一致）。</summary>
    private static readonly Dictionary<string, (string Emoji, string Label)> CatMap = new()
    {
        ["task"] = ("📋", "任务"),
        ["bag"] = ("🎒", "背包"),
        ["collection"] = ("📚", "收藏"),
        ["map"] = ("🗺️", "足迹"),
        ["memo"] = ("📝", "日志"),
        ["growth"] = ("🌱", "成长"),
        ["general"] = ("🏅", "综合"),
        ["egg"] = ("🥚", "彩蛋"),
        ["custom"] = ("✍️", "自定义")
    };

    /// <summary>分组展示顺序（与安卓分类顺序一致）。</summary>
    private static readonly string[] CatOrder =
    {
        "task", "bag", "collection", "map", "memo", "growth", "general", "egg", "custom"
    };

    // 标记页面是否已 Loaded：避免 XAML 中 ComboBox 的 SelectedIndex="0"
    // 在 InitializeComponent 期间触发 SelectionChanged -> Reload 时 AchList 尚为 null 而崩溃。
    private bool _loaded;

    public AchievementsPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _loaded = true;
            Reload();
        };
    }

    private void Reload()
    {
        // 防御：控件未就绪时直接返回，杜绝 NullReferenceException
        if (CategoryFilter == null || AchList == null || SummaryText == null ||
            OverviewRing == null || RingText == null || OverviewText == null || OverviewSub == null) return;

        try
        {
            // 先跑一次引擎（建行 / 回填分类 / 判定解锁），再取实时统计画进度
            AchievementEngine.Evaluate();
            var stats = AchievementEngine.ComputeStatsSnapshot();
            var ruleByAutoKey = AchievementEngine.Rules.ToDictionary(r => r.Key, r => r);

            using var db = new AppDbContext(AppPaths.DbFile);
            var rows = db.Achievements.AsNoTracking().ToList();

            var filter = (CategoryFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            bool onlyUnlocked = OnlyUnlocked?.IsChecked == true;
            var view = new List<AchRow>();

            // 各分类的「已解锁 / 总数」统计（基于全量，避免筛选导致分组头计数失真）
            var catUnlocked = new Dictionary<string, int>();
            var catTotal = new Dictionary<string, int>();
            foreach (var a in rows)
            {
                var c = string.IsNullOrEmpty(a.Category) ? "custom" : a.Category;
                catTotal[c] = catTotal.TryGetValue(c, out var t) ? t + 1 : 1;
                if (a.Unlocked) catUnlocked[c] = catUnlocked.TryGetValue(c, out var u) ? u + 1 : 1;
            }

            string GroupLabelOf(string cat)
            {
                var (emj, lbl) = CatMap.TryGetValue(cat, out var cv) ? cv : ("🏅", "自定义");
                int un = catUnlocked.TryGetValue(cat, out var u) ? u : 0;
                int tt = catTotal.TryGetValue(cat, out var t) ? t : 0;
                return $"{emj} {lbl} · 已解锁 {un}/{tt}";
            }

            foreach (var a in rows)
            {
                if (onlyUnlocked && a.Unlocked) continue;

                var cat = string.IsNullOrEmpty(a.Category) ? "custom" : a.Category;
                if (!string.IsNullOrEmpty(filter) && cat != filter) continue;

                var (emoji, _) = CatMap.TryGetValue(cat, out var v) ? v : ("🏅", "自定义");
                bool isEgg = cat == "egg";
                bool masked = isEgg && !a.Unlocked;

                var row = new AchRow
                {
                    Id = a.Id,
                    Emoji = a.Unlocked ? emoji : (isEgg ? "🥚" : "🔒"),
                    DisplayTitle = masked ? AchievementEngine.EggMaskTitle : a.Title,
                    DisplayDesc = masked ? AchievementEngine.EggMaskDesc : a.Desc,
                    StatusText = a.Unlocked
                        ? (string.IsNullOrEmpty(a.UnlockedAt) ? "已解锁" : "✅ " + a.UnlockedAt.Substring(0, 10))
                        : "未解锁",
                    IsCustom = a.Type != "auto",
                    CategoryGroupLabel = GroupLabelOf(cat),
                    SortOrder = Array.IndexOf(CatOrder, cat) is var idx && idx >= 0 ? idx : CatOrder.Length
                };

                // 未解锁的自动成就展示 current/goal（彩蛋打码的除外——保持神秘感）
                if (!a.Unlocked && !masked && !string.IsNullOrEmpty(a.AutoKey) &&
                    ruleByAutoKey.TryGetValue(a.AutoKey, out var rule))
                {
                    int cur = rule.ProgressOf(stats);
                    int goal = rule.GoalOf();
                    row.ShowProgress = true;
                    row.ProgressValue = (int)Math.Round(100.0 * cur / goal);
                    row.ProgressText = $"{cur} / {goal}";
                }

                view.Add(row);
            }

            // 按分类顺序排好后交给 CollectionView 分组（Expander 分区由 GroupStyle.ContainerStyle 渲染）
            var ordered = view.OrderBy(r => r.SortOrder).ThenBy(r => r.DisplayTitle).ToList();
            var grouped = new ListCollectionView(ordered);
            grouped.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AchRow.CategoryGroupLabel)));
            AchList.ItemsSource = grouped;

            // 总览：进度环 + 计数
            int unlocked = rows.Count(r => r.Unlocked);
            int total = rows.Count;
            double frac = total == 0 ? 0 : (double)unlocked / total;
            ChartRenderer.DrawProgressRing(OverviewRing, frac);
            RingText.Text = $"{(int)Math.Round(frac * 100)}%";
            OverviewText.Text = $"已解锁 {unlocked} / {total}";
            OverviewSub.Text = $"共 {AchievementEngine.Rules.Count} 条自动成就，含隐藏彩蛋";

            SummaryText.Text = "自动成就与自定义成就";
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("加载成就失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 只响应下拉框自身的选择变化：SelectionChanged 是冒泡路由事件，
        // 未来若卡片子树加入其它 Selector，也不会把本页拖进递归。
        if (!ReferenceEquals(e.OriginalSource, CategoryFilter)) return;
        // 页面未加载完（InitializeComponent 期间）不处理，等 Loaded 后的首次 Reload 统一构建
        if (!_loaded) return;
        Reload();
    }

    private void OnlyUnlocked_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        Reload();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        var (newly, titles) = AchievementEngine.EvaluateDetailed();
        Reload();
        if (newly > 0)
        {
            // 常规弹窗只做兜底提示；Steam 风格通知在业务页自动触发
            SimpleDialogs.Alert($"新增解锁 {newly} 条成就！" +
                (newly > 3 ? $"等 {newly} 条" : "：" + string.Join("、", titles)),
                "地球Online", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            SimpleDialogs.Alert("已重新判定，没有新的解锁。", "地球Online", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void AddCustom_Click(object sender, RoutedEventArgs e)
    {
        var title = SimpleDialogs.Prompt("自定义成就", "成就名称");
        if (string.IsNullOrWhiteSpace(title)) return;
        var desc = SimpleDialogs.Prompt("自定义成就", "说明（可留空）", multiline: true) ?? "";

        using var db = new AppDbContext(AppPaths.DbFile);
        db.Achievements.Add(new AchievementEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title.Trim(),
            Desc = string.IsNullOrWhiteSpace(desc) ? "手动添加的成就" : desc.Trim(),
            Type = "manual",
            Category = "custom",
            Unlocked = false
        });
        db.SaveChanges();
        Reload();
    }

    private void DeleteCustom_Click(object sender, RoutedEventArgs e)
    {
        if (AchList.SelectedItem is not AchRow row) return;
        if (!row.IsCustom)
        {
            SimpleDialogs.Alert("自动成就由系统判定，不能删除。", "地球Online", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!SimpleDialogs.Confirm($"确定删除自定义成就「{row.DisplayTitle}」？")) return;

        using var db = new AppDbContext(AppPaths.DbFile);
        var entity = db.Achievements.Find(row.Id);
        if (entity is not null) db.Achievements.Remove(entity);
        db.SaveChanges();
        Reload();
    }
}
