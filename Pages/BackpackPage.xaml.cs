using System.Windows;
using System.Windows.Controls;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Dialogs;
using EarthOnline.Desktop.Services;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Pages;

public partial class BackpackPage : Page
{
    public BackpackPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ReloadAll();
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e) => ReloadAll();

    private void ReloadAll()
    {
        LoadCategories();
        LoadItems();
        LoadCollections();
    }

    // ==================== 分类 ====================

    private List<BagCategoryEntity> CategoriesOf(string scope)
    {
        using var db = new AppDbContext(AppPaths.DbFile);
        return db.BagCategories.AsNoTracking()
            .Where(c => c.Scope == scope)
            .OrderBy(c => c.SortOrder)
            .ToList();
    }

    private void LoadCategories()
    {
        FillFilter(ItemCategoryFilter, CategoriesOf(BagScope.Item));
        FillFilter(CollectionCategoryFilter, CategoriesOf(BagScope.Collection));
    }

    private static void FillFilter(ComboBox box, List<BagCategoryEntity> list)
    {
        var keep = box.SelectedIndex;
        box.Items.Clear();
        box.Items.Add(new ComboBoxItem { Content = "全部分类", Tag = "" });
        box.Items.Add(new ComboBoxItem { Content = "未分类", Tag = "__none__" });
        foreach (var c in list)
        {
            box.Items.Add(new ComboBoxItem { Content = c.Name, Tag = c.Id });
        }
        box.SelectedIndex = keep >= 0 && keep < box.Items.Count ? keep : 0;
    }

    private static string? FilterValue(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag?.ToString();

    // ==================== 物品 ====================

    private void ItemFilter_Changed(object sender, SelectionChangedEventArgs e) => LoadItems();

    private void LoadItems()
    {
        using var db = new AppDbContext(AppPaths.DbFile);
        var q = db.Items.AsNoTracking().AsQueryable();
        var f = FilterValue(ItemCategoryFilter);
        if (f == "__none__") q = q.Where(i => i.Category == null || i.Category == "");
        else if (!string.IsNullOrEmpty(f)) q = q.Where(i => i.Category == f);
        ItemList.ItemsSource = q.OrderByDescending(i => i.CreatedAt).ToList();
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        var name = SimpleDialogs.Prompt("新增物品", "物品名称");
        if (string.IsNullOrWhiteSpace(name)) return;

        var desc = SimpleDialogs.Prompt("新增物品", "描述（可留空）", multiline: true) ?? "";

        using var db = new AppDbContext(AppPaths.DbFile);
        db.Items.Add(new ItemEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name.Trim(),
            Type = "physical",
            Description = string.IsNullOrWhiteSpace(desc) ? null : desc.Trim(),
            CreatedAt = DateTime.Today.ToString("yyyy-MM-dd")
        });
        db.SaveChanges();
        AchievementEngine.Evaluate();
        ReloadAll();
    }

    private void AssignItem_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ItemEntity item) return;
        var cats = CategoriesOf(BagScope.Item);
        var names = new[] { "（未分类）" }.Concat(cats.Select(c => c.Name)).ToArray();
        var pick = SimpleDialogs.Prompt(
            "归类物品",
            "输入分类名（可选：" + string.Join(" / ", names) + "）",
            item.Category ?? "");
        if (pick is null) return;

        var id = cats.FirstOrDefault(c => c.Name == pick.Trim())?.Id ?? (pick.Trim().Length == 0 ? null : pick.Trim());
        using var db = new AppDbContext(AppPaths.DbFile);
        var row = db.Items.Find(item.Id);
        if (row is null) return;
        row.Category = string.IsNullOrWhiteSpace(id) ? null : id;
        db.SaveChanges();
        ReloadAll();
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ItemEntity item) return;
        if (!SimpleDialogs.Confirm($"确定删除物品「{item.Name}」？")) return;
        using var db = new AppDbContext(AppPaths.DbFile);
        var row = db.Items.Find(item.Id);
        if (row is not null) db.Items.Remove(row);
        db.SaveChanges();
        AchievementEngine.Evaluate();
        ReloadAll();
    }

    // ==================== 收藏 ====================

    private void CollectionFilter_Changed(object sender, SelectionChangedEventArgs e) => LoadCollections();

    private void LoadCollections()
    {
        using var db = new AppDbContext(AppPaths.DbFile);
        var q = db.Collections.AsNoTracking().AsQueryable();
        var f = FilterValue(CollectionCategoryFilter);
        if (f == "__none__") q = q.Where(c => c.Category == null || c.Category == "");
        else if (!string.IsNullOrEmpty(f)) q = q.Where(c => c.Category == f);
        CollectionList.ItemsSource = q.OrderByDescending(c => c.CreatedAt).ToList();
    }

    private void AddCollection_Click(object sender, RoutedEventArgs e)
    {
        var title = SimpleDialogs.Prompt("新增收藏", "收藏标题");
        if (string.IsNullOrWhiteSpace(title)) return;
        var note = SimpleDialogs.Prompt("新增收藏", "备注（可留空）", multiline: true) ?? "";

        using var db = new AppDbContext(AppPaths.DbFile);
        db.Collections.Add(new CollectionEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title.Trim(),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt = DateTime.Today.ToString("yyyy-MM-dd")
        });
        db.SaveChanges();
        AchievementEngine.Evaluate();
        ReloadAll();
    }

    private void AssignCollection_Click(object sender, RoutedEventArgs e)
    {
        if (CollectionList.SelectedItem is not CollectionEntity col) return;
        var cats = CategoriesOf(BagScope.Collection);
        var pick = SimpleDialogs.Prompt(
            "归类收藏",
            "输入分类名（已有：" + string.Join(" / ", cats.Select(c => c.Name).DefaultIfEmpty("无")) + "）",
            col.Category ?? "");
        if (pick is null) return;

        var id = cats.FirstOrDefault(c => c.Name == pick.Trim())?.Id ?? (pick.Trim().Length == 0 ? null : pick.Trim());
        using var db = new AppDbContext(AppPaths.DbFile);
        var row = db.Collections.Find(col.Id);
        if (row is null) return;
        row.Category = string.IsNullOrWhiteSpace(id) ? null : id;
        db.SaveChanges();
        ReloadAll();
    }

    private void DeleteCollection_Click(object sender, RoutedEventArgs e)
    {
        if (CollectionList.SelectedItem is not CollectionEntity col) return;
        if (!SimpleDialogs.Confirm($"确定删除收藏「{col.Title}」？")) return;
        using var db = new AppDbContext(AppPaths.DbFile);
        var row = db.Collections.Find(col.Id);
        if (row is not null) db.Collections.Remove(row);
        db.SaveChanges();
        AchievementEngine.Evaluate();
        ReloadAll();
    }
}
