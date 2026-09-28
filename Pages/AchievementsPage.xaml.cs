using System.Windows;
using System.Windows.Controls;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Dialogs;
using EarthOnline.Desktop.Services;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Pages;

/// <summary>成就展示行（含彩蛋打码）。</summary>
public class AchRow
{
    public string Id { get; set; } = "";
    public string Emoji { get; set; } = "🏅";
    public string DisplayTitle { get; set; } = "";
    public string DisplayDesc { get; set; } = "";
    public string StatusText { get; set; } = "";
    public bool IsCustom { get; set; }
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

    public AchievementsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        // 先跑一次引擎（建行 / 回填分类 / 判定解锁），再展示
        AchievementEngine.Evaluate();

        using var db = new AppDbContext(AppPaths.DbFile);
        var rows = db.Achievements.AsNoTracking().ToList();

        var filter = (CategoryFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        var view = new List<AchRow>();

        foreach (var a in rows)
        {
            var cat = string.IsNullOrEmpty(a.Category) ? "custom" : a.Category;
            if (!string.IsNullOrEmpty(filter) && cat != filter) continue;

            var (emoji, _) = CatMap.TryGetValue(cat, out var v) ? v : ("🏅", "自定义");
            bool isEgg = cat == "egg";
            bool masked = isEgg && !a.Unlocked;

            view.Add(new AchRow
            {
                Id = a.Id,
                Emoji = a.Unlocked ? emoji : (isEgg ? "🥚" : "🔒"),
                DisplayTitle = masked ? AchievementEngine.EggMaskTitle : a.Title,
                DisplayDesc = masked ? AchievementEngine.EggMaskDesc : a.Desc,
                StatusText = a.Unlocked
                    ? (string.IsNullOrEmpty(a.UnlockedAt) ? "已解锁" : "✅ " + a.UnlockedAt.Substring(0, 10))
                    : "未解锁",
                IsCustom = a.Type != "auto"
            });
        }

        AchList.ItemsSource = view;
        var unlocked = rows.Count(r => r.Unlocked);
        SummaryText.Text = $"已解锁 {unlocked} / {rows.Count} 条（共 {AchievementEngine.Rules.Count} 条自动成就，含彩蛋）";
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => Reload();

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        int newly = AchievementEngine.Evaluate();
        Reload();
        MessageBox.Show(newly > 0 ? $"新增解锁 {newly} 条成就！" : "已重新判定，没有新的解锁。",
            "地球Online", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show("自动成就由系统判定，不能删除。", "地球Online", MessageBoxButton.OK, MessageBoxImage.Information);
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
