using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Data.Models;
using EarthOnline.Desktop.Dialogs;
using EarthOnline.Desktop.Services;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Pages;

/// <summary>物品列表行（修复原版把分类 id 直接当文案显示的问题）。</summary>
public class ItemRow
{
    public ItemEntity Item { get; init; } = new();

    public string Icon => Item.Type == "virtual" ? "💾" : "📦";
    public string Name => Item.Name;
    public string TypeLabel => Item.Type == "virtual" ? "虚拟" : "实物";
    public string CategoryLabel => string.IsNullOrEmpty(CategoryName) ? "未分类" : CategoryName;
    public string CategoryName { get; init; } = "";
    public string CreatedAtLabel => string.IsNullOrEmpty(Item.CreatedAt) ? "" : Item.CreatedAt;

    // v1.0.3：物品故事卡 —— 描述以「📖」样式强调展示（有描述才显示）
    public bool HasStory => !string.IsNullOrWhiteSpace(Item.Description);
    public string Story => Item.Description ?? "";

    /// <summary>搜索文本（名称/描述/分类名，小写）。</summary>
    public string SearchText { get; init; } = "";
}

/// <summary>收藏列表行。</summary>
public class CollectionRow
{
    public CollectionEntity Col { get; init; } = new();

    public string Title => Col.Title;
    public string CategoryLabel => string.IsNullOrEmpty(CategoryName) ? "未分类" : CategoryName;
    public string CategoryName { get; init; } = "";
    public string CreatedAtLabel => string.IsNullOrEmpty(Col.CreatedAt) ? "" : Col.CreatedAt;

    public bool HasFile => !string.IsNullOrEmpty(FileLabel);
    public string FileLabel { get; init; } = "";
    public string SearchText { get; init; } = "";
}

public partial class BackpackPage : Page
{
    // ===== 防重入 / 防事件回环 / 防初始化期空引用 =====
    // _isReloading：ReloadAll 自身的重入锁（try-finally 复位）。SelectionChanged 是冒泡路由事件，
    //   ComboBox 触发的 SelectionChanged 会一路冒泡到 TabControl 的 Tabs_SelectionChanged；
    //   若 ReloadAll 没有锁，重建下拉 → 冒泡 → ReloadAll → 再重建 → … 同步无限递归 → 栈溢出。
    // _isLoadingItems / _isLoadingCollections：Load 方法自身的重入锁（try-finally 复位）。
    // _suppressFilterEvents：重建下拉项期间，抑制 SelectionChanged 透传到 Load 方法。
    // _loaded：标记页面已 Loaded。XAML 中 TabControl 默认选中首项、ComboBox 的 SelectedIndex="0"
    //   会在 InitializeComponent 期间触发 SelectionChanged / Tabs_SelectionChanged，此时
    //   文档顺序靠后的 ListBox（如 CollectionList）尚未实例化。在 Loaded 之前一律忽略这些事件。
    private bool _isReloading;
    private bool _isLoadingItems;
    private bool _isLoadingCollections;
    private bool _suppressFilterEvents;
    private bool _loaded;

    public BackpackPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _loaded = true;
            ReloadAll();
        };
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 根治冒泡回环：只响应 TabControl 自身的选择变化。
        if (!ReferenceEquals(e.OriginalSource, Tabs)) return;
        if (!_loaded) return;
        ReloadAll();
    }

    private void ReloadAll()
    {
        if (_isReloading) return; // 重入锁：即使未来有漏网的冒泡路径，也在此处截断递归
        _isReloading = true;
        try
        {
            LoadCategories();
            LoadItems();
            LoadCollections();
        }
        finally
        {
            _isReloading = false;
        }
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
        // 重建下拉项会改 SelectedIndex，进而触发 SelectionChanged。
        _suppressFilterEvents = true;
        try
        {
            FillFilter(ItemCategoryFilter, CategoriesOf(BagScope.Item));
            FillFilter(CollectionCategoryFilter, CategoriesOf(BagScope.Collection));
        }
        finally
        {
            _suppressFilterEvents = false;
        }
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

    private void ManageItemCategories_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        if (CategoryDialog.Show(BagScope.Item))
        {
            LoadCategories();
            LoadItems();
        }
    }

    private void ManageCollectionCategories_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        if (CategoryDialog.Show(BagScope.Collection))
        {
            LoadCategories();
            LoadCollections();
        }
    }

    // ==================== 物品 ====================

    private void ItemFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return; // 重建下拉期间忽略，不参与递归
        if (!_loaded) return;              // 初始化期间忽略，避免控件未就绪
        LoadItems();
    }

    private void ItemSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ItemSearchHint is not null) ItemSearchHint.Visibility =
            ItemSearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!_loaded) return;
        LoadItems();
    }

    private void LoadItems()
    {
        if (_isLoadingItems) return; // 重入锁：防止 SelectionChanged 嵌套触发导致无限递归 / 栈溢出
        if (ItemList == null) return; // 控件未实例化（如所在 Tab 尚未激活）时安全跳过
        _isLoadingItems = true;
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            var q = db.Items.AsNoTracking().AsQueryable();
            var f = FilterValue(ItemCategoryFilter);
            if (f == "__none__") q = q.Where(i => i.Category == null || i.Category == "");
            else if (!string.IsNullOrEmpty(f)) q = q.Where(i => i.Category == f);

            var catList = db.BagCategories.AsNoTracking()
                .Where(c => c.Scope == BagScope.Item)
                .OrderBy(c => c.SortOrder)
                .ToList();
            var catNames = catList.ToDictionary(c => c.Id, c => c.Name);
            var catOrder = new Dictionary<string, int>();
            for (int i = 0; i < catList.Count; i++) catOrder[catList[i].Id] = i;
            var keyword = (ItemSearchBox?.Text ?? "").Trim();
            var rows = q.ToList()
                // 分组顺序 = 分类定义顺序（SortOrder）；未分类排最后，组内按创建时间倒序
                .OrderBy(i => i.Category is { Length: > 0 } && catOrder.TryGetValue(i.Category, out var oi) ? oi : int.MaxValue)
                .ThenByDescending(i => i.CreatedAt)
                .Select(i =>
                {
                    var catName = i.Category is { Length: > 0 } && catNames.TryGetValue(i.Category, out var n)
                        ? n : "";
                    return new ItemRow
                    {
                        Item = i,
                        CategoryName = catName,
                        SearchText = (i.Name + " " + (i.Description ?? "") + " " + catName).ToLowerInvariant()
                    };
                })
                .Where(r => keyword.Length == 0 || r.SearchText.Contains(keyword.ToLowerInvariant()))
                .ToList();

            // 分类分组顶置显示：CollectionView 分组，头显示分类名 + 数量
            var view = new ListCollectionView(rows);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ItemRow.CategoryLabel)));
            ItemList.ItemsSource = view;
        }
        finally
        {
            _isLoadingItems = false;
        }
    }

    private ItemEntity? SelectedItemEntity()
    {
        var row = ItemList?.SelectedItem as ItemRow;
        return row?.Item;
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        var item = new ItemEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = "physical",
            CreatedAt = DateTime.Today.ToString("yyyy-MM-dd")
        };
        if (!ItemDialog.Show(item, isNew: true)) return;

        using var db = new AppDbContext(AppPaths.DbFile);
        db.Items.Add(item);
        db.SaveChanges();
        AchievementNotifier.Check();
        ReloadAll();
    }

    private void EditItem_Click(object sender, RoutedEventArgs e)
    {
        var sel = SelectedItemEntity();
        if (sel is null) return;

        var copy = new ItemEntity
        {
            Id = sel.Id, Name = sel.Name, Type = sel.Type, Description = sel.Description,
            Category = sel.Category, CreatedAt = sel.CreatedAt
        };
        if (!ItemDialog.Show(copy, isNew: false)) return;

        using var db = new AppDbContext(AppPaths.DbFile);
        var row = db.Items.Find(sel.Id);
        if (row is null) return;
        db.Entry(row).CurrentValues.SetValues(copy);
        db.SaveChanges();
        ReloadAll();
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        var sel = SelectedItemEntity();
        if (sel is null) return;
        if (!SimpleDialogs.Confirm($"确定删除物品「{sel.Name}」？")) return;
        using var db = new AppDbContext(AppPaths.DbFile);
        var row = db.Items.Find(sel.Id);
        if (row is not null) db.Items.Remove(row);
        db.SaveChanges();
        AchievementNotifier.Check();
        ReloadAll();
    }

    // ==================== 收藏 ====================

    private void CollectionFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return; // 重建下拉期间忽略，不参与递归
        if (!_loaded) return;              // 初始化期间忽略，避免控件未就绪
        LoadCollections();
    }

    private void CollectionSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (CollectionSearchHint is not null) CollectionSearchHint.Visibility =
            CollectionSearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!_loaded) return;
        LoadCollections();
    }

    private void LoadCollections()
    {
        if (_isLoadingCollections) return; // 重入锁
        if (CollectionList == null) return; // 控件未实例化（如所在 Tab 尚未激活）时安全跳过
        _isLoadingCollections = true;
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            var q = db.Collections.AsNoTracking().AsQueryable();
            var f = FilterValue(CollectionCategoryFilter);
            if (f == "__none__") q = q.Where(c => c.Category == null || c.Category == "");
            else if (!string.IsNullOrEmpty(f)) q = q.Where(c => c.Category == f);

            var catList = db.BagCategories.AsNoTracking()
                .Where(c => c.Scope == BagScope.Collection)
                .OrderBy(c => c.SortOrder)
                .ToList();
            var catNames = catList.ToDictionary(c => c.Id, c => c.Name);
            var catOrder = new Dictionary<string, int>();
            for (int i = 0; i < catList.Count; i++) catOrder[catList[i].Id] = i;
            var keyword = (CollectionSearchBox?.Text ?? "").Trim();
            var rows = q.ToList()
                // 分组顺序 = 分类定义顺序（SortOrder）；未分类排最后，组内按创建时间倒序
                .OrderBy(c => c.Category is { Length: > 0 } && catOrder.TryGetValue(c.Category, out var oi) ? oi : int.MaxValue)
                .ThenByDescending(c => c.CreatedAt)
                .Select(c =>
                {
                    var catName = c.Category is { Length: > 0 } && catNames.TryGetValue(c.Category, out var n)
                        ? n : "";
                    string? fileLabel = null;
                    if (!string.IsNullOrEmpty(c.FileMetaJson))
                    {
                        try
                        {
                            var meta = JsonSerializer.Deserialize<FileMeta>(c.FileMetaJson);
                            if (!string.IsNullOrEmpty(meta?.Name))
                            {
                                var kb = meta.Size / 1024.0;
                                var size = kb >= 1024
                                    ? (kb / 1024).ToString("F1") + " MB"
                                    : Math.Max(1, (int)Math.Round(kb)) + " KB";
                                fileLabel = "📎 " + meta.Name + "（" + size + "）";
                                if (!File.Exists(c.FileUri)) fileLabel += " ⚠️文件缺失";
                            }
                        }
                        catch { /* 元信息坏数据忽略 */ }
                    }
                    return new CollectionRow
                    {
                        Col = c,
                        CategoryName = catName,
                        FileLabel = fileLabel ?? "",
                        SearchText = (c.Title + " " + (c.Note ?? "") + " " + catName).ToLowerInvariant()
                    };
                })
                .Where(r => keyword.Length == 0 || r.SearchText.Contains(keyword.ToLowerInvariant()))
                .ToList();

            // 分类分组顶置显示：CollectionView 分组，头显示分类名 + 数量
            var view = new ListCollectionView(rows);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CollectionRow.CategoryLabel)));
            CollectionList.ItemsSource = view;
        }
        finally
        {
            _isLoadingCollections = false;
        }
    }

    private CollectionEntity? SelectedCollectionEntity()
    {
        var row = CollectionList?.SelectedItem as CollectionRow;
        return row?.Col;
    }

    private void AddCollection_Click(object sender, RoutedEventArgs e)
    {
        var col = new CollectionEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.Today.ToString("yyyy-MM-dd")
        };
        if (!CollectionDialog.Show(col, isNew: true)) return;

        using var db = new AppDbContext(AppPaths.DbFile);
        db.Collections.Add(col);
        db.SaveChanges();
        AchievementNotifier.Check();
        ReloadAll();
    }

    private void EditCollection_Click(object sender, RoutedEventArgs e)
    {
        var sel = SelectedCollectionEntity();
        if (sel is null) return;

        var copy = new CollectionEntity
        {
            Id = sel.Id, Title = sel.Title, Note = sel.Note, Category = sel.Category,
            FileMetaJson = sel.FileMetaJson, FileUri = sel.FileUri, CreatedAt = sel.CreatedAt
        };
        if (!CollectionDialog.Show(copy, isNew: false)) return;

        using var db = new AppDbContext(AppPaths.DbFile);
        var row = db.Collections.Find(sel.Id);
        if (row is null) return;
        db.Entry(row).CurrentValues.SetValues(copy);
        db.SaveChanges();
        ReloadAll();
    }

    private void OpenCollectionFile_Click(object sender, RoutedEventArgs e)
    {
        var sel = SelectedCollectionEntity();
        if (sel is null) return;
        CollectionDialog.OpenFile(sel.FileUri);
    }

    private void DeleteCollection_Click(object sender, RoutedEventArgs e)
    {
        var sel = SelectedCollectionEntity();
        if (sel is null) return;
        if (!SimpleDialogs.Confirm($"确定删除收藏「{sel.Title}」？")) return;
        using var db = new AppDbContext(AppPaths.DbFile);
        var row = db.Collections.Find(sel.Id);
        if (row is not null)
        {
            db.Collections.Remove(row);
            db.SaveChanges(); // 先落库成功，再清理附件文件
        }
        // 删除条目后清理其附件文件（仅限 FilesDir 内的私有文件）
        if (!string.IsNullOrEmpty(sel.FileUri) &&
            sel.FileUri.StartsWith(AppPaths.FilesDir, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(sel.FileUri))
        {
            try { File.Delete(sel.FileUri); } catch { /* 清理失败不影响 */ }
        }
        AchievementNotifier.Check();
        ReloadAll();
    }

    // ==================== 右键菜单 ====================

    // 右键命中行时先选中它（WPF 默认右键不改变选中），菜单里的编辑/删除才能拿到 SelectedItem
    private void ListBoxItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem lbi) lbi.IsSelected = true;
    }

    // 未选中行（右键空白区）或页面未就绪时不出菜单
    private void ItemList_ContextOpening(object sender, ContextMenuEventArgs e)
    {
        if (!_loaded || SelectedItemEntity() is null) { e.Handled = true; return; }
    }

    private void CollectionList_ContextOpening(object sender, ContextMenuEventArgs e)
    {
        if (!_loaded || SelectedCollectionEntity() is null) { e.Handled = true; return; }
    }

    // ==================== 文件拖拽存入背包 ====================

    private void BackpackPage_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void BackpackPage_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files is null) return;

        int added = 0;
        foreach (var src in files)
        {
            try
            {
                AppPaths.EnsureDirectories();
                var name = Path.GetFileName(src);
                var dest = Path.Combine(AppPaths.FilesDir, Guid.NewGuid().ToString("N") + "_" + name);
                File.Copy(src, dest, overwrite: true);
                var fi = new FileInfo(dest);
                using var db = new AppDbContext(AppPaths.DbFile);
                db.Items.Add(new ItemEntity
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = name,
                    Type = "physical",
                    Description = "拖入文件 → " + dest,
                    CreatedAt = DateTime.Today.ToString("yyyy-MM-dd")
                });
                db.SaveChanges();
                added++;
            }
            catch { /* 单个文件失败不影响其余 */ }
        }

        if (added > 0)
        {
            AchievementNotifier.Check();
            ReloadAll();
        }
    }
}
