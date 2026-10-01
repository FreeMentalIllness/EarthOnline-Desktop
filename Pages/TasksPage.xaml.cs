using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Dialogs;
using EarthOnline.Desktop.Services;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Pages;

/// <summary>
/// 树形展示用的节点（TaskEntity + 子节点）。
/// 父任务的 ProgressText 由子任务聚合（显示口径，不落库），可折叠。
/// </summary>
public class TaskNode : INotifyPropertyChanged
{
    public TaskEntity Task { get; set; } = new();
    public ObservableCollection<TaskNode> Children { get; set; } = new();

    private bool _isExpanded = true;
    /// <summary>折叠/展开状态（TreeViewItem 双向绑定；默认展开）。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded))); }
    }

    public string Id => Task.Id;
    public string Title => Task.Title;
    /// <summary>完成态：标题打删除线（XAML DataTrigger 用）。</summary>
    public bool IsDone => Task.Status == "done";
    public string DueText => string.IsNullOrEmpty(Task.DueDate) ? "" : "截止 " + Task.DueDate;

    /// <summary>进度文案：叶子显示自身进度；父任务显示子任务聚合（Σ 标记），百分比不含 Σ 时与自身一致。</summary>
    public string ProgressText { get; set; } = "";

    /// <summary>子任务连接线标记（非根节点显示 └）。</summary>
    public string Connector => string.IsNullOrEmpty(Task.ParentId) ? "" : "└";
    public bool HasParent => !string.IsNullOrEmpty(Task.ParentId);

    public string StatusEmoji => Task.Status switch
    {
        "done" => "✅",
        "active" => "🔥",
        "paused" => "⏸",
        _ => "📝"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class TasksPage : Page
{
    private List<TaskEntity> _all = new();
    // 标记页面是否已 Loaded：避免 XAML 中 ComboBox 的 SelectedIndex="0"
    // 在 InitializeComponent 期间触发 SelectionChanged -> BuildTree 时 TaskTree 尚为 null 而崩溃。
    private bool _loaded;

    public TasksPage()
    {
        InitializeComponent();
        HideDoneBox.IsChecked = SettingsStore.Load().HideDoneTasks;
        Loaded += (_, _) =>
        {
            _loaded = true;
            Reload();
        };
    }

    /// <summary>「隐藏已完成任务」开关：切换即重建树并持久化偏好。</summary>
    private void HideDone_Changed(object sender, RoutedEventArgs e)
    {
        if (HideDoneBox is null) return; // 初始化期防御
        try
        {
            var s = SettingsStore.Load();
            s.HideDoneTasks = HideDoneBox.IsChecked == true;
            s.Save();
        }
        catch { /* 偏好保存失败不影响切换 */ }
        if (_loaded) BuildTree();
    }

    private void Reload()
    {
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            _all = db.Tasks.AsNoTracking()
                .OrderBy(t => t.Order)
                .ThenBy(t => t.CreatedAt)
                .ToList();
            BuildTree();
            ClearBatchVisual();   // 树重建后多选高亮失效，同步清空勾选集
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("加载任务失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BuildTree()
    {
        // 防御：控件未就绪时直接返回，杜绝 NullReferenceException
        if (TaskTree == null) return;

        var filter = (CategoryFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        var scoped = string.IsNullOrEmpty(filter) ? _all : _all.Where(t => t.Category == filter).ToList();

        // 隐藏已完成：done 且所有后代都 done 的节点整体隐藏（有未完成子任务的父亲保留）
        if (HideDoneBox?.IsChecked == true)
            scoped = scoped.Where(t => !IsEffectivelyDone(t, scoped)).ToList();

        var nodes = new ObservableCollection<TaskNode>();
        foreach (var t in scoped.Where(t => string.IsNullOrEmpty(t.ParentId)))
        {
            nodes.Add(BuildNode(t, scoped));
        }
        TaskTree.ItemsSource = nodes;
    }

    /// <summary>自身 done 且子任务池里不存在未完成后代 → 视为「可隐藏」。</summary>
    private static bool IsEffectivelyDone(TaskEntity t, List<TaskEntity> pool)
        => t.Status == "done" && !pool.Any(c => c.ParentId == t.Id && !IsEffectivelyDone(c, pool));

    private static TaskNode BuildNode(TaskEntity task, List<TaskEntity> pool)
    {
        var node = new TaskNode { Task = task };
        foreach (var child in pool.Where(c => c.ParentId == task.Id))
        {
            node.Children.Add(BuildNode(child, pool));
        }
        node.ProgressText = ProgressOf(node);
        return node;
    }

    /// <summary>
    /// 进度文案：叶子 = 自身进度（done 视作 100）；
    /// 父任务 = 子任务聚合（各子项 done 视作 100，其余取自身值，递归）平均，带 Σ 标记。
    /// 仅显示口径，不落库（与安卓端一致：父任务进度不由子任务回写）。
    /// </summary>
    private static string ProgressOf(TaskNode node)
    {
        if (node.Children.Count == 0)
        {
            int p = node.Task.Status == "done" ? 100 : node.Task.Progress;
            return $"{p}%";
        }
        int agg = (int)Math.Round(node.Children.Average(PercentOf));
        return $"Σ {agg}%";
    }

    private static int PercentOf(TaskNode node)
    {
        if (node.Children.Count > 0)
        {
            return (int)Math.Round(node.Children.Average(PercentOf));
        }
        return node.Task.Status == "done" ? 100 : node.Task.Progress;
    }

    /// <summary>全部展开 / 折叠（只改顶层与递归子节点的 IsExpanded）。</summary>
    private void SetAllExpanded(bool expanded)
    {
        if (TaskTree?.ItemsSource is not ObservableCollection<TaskNode> roots) return;
        void Walk(ObservableCollection<TaskNode> nodes)
        {
            foreach (var n in nodes)
            {
                n.IsExpanded = expanded;
                Walk(n.Children);
            }
        }
        Walk(roots);
    }

    private void ExpandAll_Click(object sender, RoutedEventArgs e) => SetAllExpanded(true);

    private void CollapseAll_Click(object sender, RoutedEventArgs e) => SetAllExpanded(false);

    // ==================== 明细面板 ====================

    /// <summary>选中变化 → 右侧明细联动刷新（未选中显示占位文案）。</summary>
    private void TaskTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DetailPanel is null || DetailEmpty is null) return;   // InitializeComponent 期间防御
        UpdateDetail(SelectedTask());
    }

    private void UpdateDetail(TaskEntity? t)
    {
        bool has = t is not null;
        DetailPanel.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        DetailEmpty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        if (!has) return;

        var task = t!;
        DetailTitle.Text = string.IsNullOrWhiteSpace(task.Title) ? "（未命名任务）" : task.Title;

        int p = task.Status == "done" ? 100 : task.Progress;
        DetailProgress.Value = p;
        DetailProgressText.Text = $"进度 {p}%";

        DetailStatus.Text = task.Status switch
        {
            "done" => "✅ 已完成",
            "active" => "🔥 进行中",
            "paused" => "⏸ 已暂停",
            _ => "📝 规划中"
        };
        DetailCategory.Text = task.Category switch
        {
            "main" => "主线",
            "side" => "支线",
            _ => "待办"
        };
        DetailDue.Text = string.IsNullOrEmpty(task.DueDate) ? "—" : task.DueDate;
        DetailNote.Text = string.IsNullOrWhiteSpace(task.Note) ? "—" : task.Note;
        DetailCreated.Text = string.IsNullOrEmpty(task.CreatedAt) ? "—" : task.CreatedAt;
        DetailModified.Text = string.IsNullOrEmpty(task.LastModified) ? "—" : task.LastModified;
        DetailDoneAt.Text = string.IsNullOrEmpty(task.DoneAt)
            ? "—"
            : DateTime.TryParse(task.DoneAt, out var dt) ? dt.ToString("yyyy-MM-dd HH:mm") : task.DoneAt;

        // 子任务数（直接子级 + 全部后代）
        int direct = _all.Count(c => c.ParentId == task.Id);
        int total = CountDescendants(task.Id);
        DetailChildren.Text = direct == 0 ? "无子任务" : $"子任务 {direct} 个（含后代共 {total} 个）";
    }

    private int CountDescendants(string id)
    {
        int n = 0;
        foreach (var c in _all.Where(x => x.ParentId == id))
        {
            n += 1 + CountDescendants(c.Id);
        }
        return n;
    }

    /// <summary>明细面板的编辑按钮（与工具条编辑一致）。</summary>
    private void EditDetail_Click(object sender, RoutedEventArgs e) => Edit_Click(sender, e);

    private TaskEntity? SelectedTask()
    {
        if (TaskTree == null) return null;
        if (TaskTree.SelectedItem is TaskNode n) return n.Task;
        return null;
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 只响应下拉框自身的选择变化：SelectionChanged 是冒泡路由事件，
        // 未来若工具条子树加入其它 Selector，也不会把本页拖进递归。
        if (!ReferenceEquals(e.OriginalSource, CategoryFilter)) return;
        // 页面未加载完（InitializeComponent 期间）不处理，等 Loaded 后的首次 Reload 统一构建
        if (!_loaded) return;
        BuildTree();
    }

    // ==================== 右键菜单 ====================

    // 右键命中节点时先选中它（WPF 默认右键不改变选中），菜单里的编辑/完成/删除才能拿到 SelectedItem
    private void TreeViewItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem tvi) tvi.IsSelected = true;
    }

    // 空白处/未选中时不出菜单；已选中时按状态刷新「标记完成/取消完成」文案
    private void TaskContextMenu_Opening(object sender, ContextMenuEventArgs e)
    {
        if (!_loaded) { e.Handled = true; return; }
        var sel = SelectedTask();
        if (sel is null) { e.Handled = true; return; }
        if (ToggleDoneItem is not null)
        {
            ToggleDoneItem.Header = sel.Status == "done" ? "↩️ 取消完成" : "✅ 标记完成";
        }
        UpdateBatchMenuVisibility();
    }

    private void AddRoot_Click(object sender, RoutedEventArgs e) => CreateTask(null);

    /// <summary>Ctrl+N 入口：打开新建任务对话框（模态，天然防连点重复）。</summary>
    public void StartCreate() => CreateTask(null);

    private void AddChild_Click(object sender, RoutedEventArgs e)
    {
        var parent = SelectedTask();
        if (parent is null)
        {
            SimpleDialogs.Alert("请先选中一个任务作为父任务", "地球Online", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        CreateTask(parent.Id);
    }

    private void CreateTask(string? parentId)
    {
        var filter = (CategoryFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var task = new TaskEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            ParentId = parentId,
            Category = string.IsNullOrEmpty(filter) ? "todo" : filter,
            Title = "",
            Status = "planning",
            Progress = 0,
            CreatedAt = DateTime.Today.ToString("yyyy-MM-dd"),
            LastModified = DateTime.Today.ToString("yyyy-MM-dd")
        };

        if (!SimpleDialogs.EditTask(task)) return;

        using var db = new AppDbContext(AppPaths.DbFile);
        db.Tasks.Add(task);
        // doneAt：完成任务数的唯一口径 —— 新建即完成也要写
        if (task.Status == "done") task.DoneAt ??= DateTime.Now.ToString("o");
        db.SaveChanges();
        AchievementNotifier.Check();
        Reload();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        var sel = SelectedTask();
        if (sel is null)
        {
            SimpleDialogs.Alert("请先选中一个任务", "地球Online", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var copy = new TaskEntity
        {
            Id = sel.Id, ParentId = sel.ParentId, Category = sel.Category, Title = sel.Title,
            Status = sel.Status, Progress = sel.Progress, Note = sel.Note, DueDate = sel.DueDate,
            CreatedAt = sel.CreatedAt, LastModified = sel.LastModified, DoneAt = sel.DoneAt, Order = sel.Order
        };

        if (!SimpleDialogs.EditTask(copy)) return;

        using var db = new AppDbContext(AppPaths.DbFile);
        var row = db.Tasks.Find(sel.Id);
        if (row is null) return;
        db.Entry(row).CurrentValues.SetValues(copy);
        row.LastModified = DateTime.Today.ToString("yyyy-MM-dd");
        row.DoneAt = row.Status == "done" ? (row.DoneAt ?? DateTime.Now.ToString("o")) : null;
        db.SaveChanges();
        AchievementNotifier.Check();
        Reload();
    }

    private void ToggleDone_Click(object sender, RoutedEventArgs e)
    {
        var sel = SelectedTask();
        if (sel is null) return;

        using var db = new AppDbContext(AppPaths.DbFile);
        var row = db.Tasks.Find(sel.Id);
        if (row is null) return;

        if (row.Status == "done")
        {
            row.Status = "planning";
            row.DoneAt = null;
        }
        else
        {
            row.Status = "done";
            row.Progress = 100;
            row.DoneAt = DateTime.Now.ToString("o");
        }
        row.LastModified = DateTime.Today.ToString("yyyy-MM-dd");
        db.SaveChanges();
        AchievementNotifier.Check();
        Reload();
    }

    /// <summary>打开回收站（恢复 / 永久删除，v1.0.5）。</summary>
    private void RecycleBin_Click(object sender, RoutedEventArgs e)
    {
        Dialogs.RecycleBinDialog.Show();
        Reload();
    }

    // ==================== 批量操作（v1.0.5：Ctrl/Shift 点选 + 右键菜单） ====================

    /// <summary>多选勾选集（任务 Id）。Ctrl/Shift + 左键点行加入/移出。</summary>
    private readonly HashSet<string> _batch = new();

    /// <summary>Ctrl/Shift + 左键：把行加入 / 移出勾选集（不高亮选中态，绿色勾选角标）。</summary>
    private void TreeViewItem_BatchClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem tvi && tvi.DataContext is TaskNode node && node.Task is { } t)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                if (!_batch.Add(t.Id))
                {
                    _batch.Remove(t.Id);
                    tvi.Background = null;
                }
                else
                {
                    var c = ThemeService.ColorOf("AccentBrush");
                    tvi.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(60, c.R, c.G, c.B));
                }
                e.Handled = true;
            }
        }
    }

    private void ClearBatchVisual()
    {
        // 树已重建，旧 TreeViewItem 全部丢弃，无需还原背景；只需清空勾选集
        _batch.Clear();
    }

    /// <summary>右键菜单按勾选集数量开合（单选场景隐藏批量项）。</summary>
    private void UpdateBatchMenuVisibility()
    {
        bool on = _batch.Count > 1;
        BatchDoneItem.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        BatchDeleteItem.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        BatchExportItem.Visibility = _batch.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BatchDone_Click(object sender, RoutedEventArgs e)
    {
        if (_batch.Count == 0) return;
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            foreach (var id in _batch)
            {
                var row = db.Tasks.Find(id);
                if (row is null || row.Status == "done") continue;
                row.Status = "done";
                row.Progress = 100;
                row.DoneAt = DateTime.Now.ToString("o");
                row.LastModified = DateTime.Today.ToString("yyyy-MM-dd");
            }
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("批量完成失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        AchievementNotifier.Check();
        Reload();
    }

    private void BatchDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_batch.Count == 0) return;
        if (!SimpleDialogs.Confirm($"把勾选的 {_batch.Count} 个任务移入回收站？（30 天内可恢复）")) return;
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            foreach (var id in _batch.ToList())
            {
                // 复用级联软删除；已软删的行 Find 不到（查询过滤器），天然幂等
                DeleteCascade(db, id);
                _batch.Remove(id);
            }
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("批量删除失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        AchievementNotifier.Check();
        Reload();
    }

    private void BatchExport_Click(object sender, RoutedEventArgs e)
    {
        if (_batch.Count == 0) return;
        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出勾选任务",
                Filter = "JSON 文件|*.json",
                FileName = $"earthonline_tasks_{DateTime.Now:yyyyMMdd_HHmm}.json",
            };
            if (dlg.ShowDialog() != true) return;

            using var db = new AppDbContext(AppPaths.DbFile);
            var rows = db.Tasks.AsNoTracking()
                .Where(t => _batch.Contains(t.Id))
                .OrderBy(t => t.Order).ToList();
            var opts = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            };
            System.IO.File.WriteAllText(dlg.FileName,
                System.Text.Json.JsonSerializer.Serialize(rows, opts));
            SimpleDialogs.Alert($"已导出 {rows.Count} 个任务到：\n{dlg.FileName}", "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("导出失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var sel = SelectedTask();
        if (sel is null) return;
        if (!SimpleDialogs.Confirm($"确定删除「{sel.Title}」及其全部子任务？")) return;

        using var db = new AppDbContext(AppPaths.DbFile);
        DeleteCascade(db, sel.Id);   // 对应网页 deleteTaskCascade
        db.SaveChanges();
        AchievementNotifier.Check();
        Reload();
    }

    /// <summary>v1.0.5 回收站：级联软删除（含全部子任务），30 天后启动时永久清理。</summary>
    private static void DeleteCascade(AppDbContext db, string id)
    {
        // 注意：全局查询过滤器会隐藏已软删行，这里用 IgnoreQueryFilters 兼顾重删场景
        foreach (var child in db.Tasks.IgnoreQueryFilters().Where(t => t.ParentId == id && t.DeletedAt == null).ToList())
        {
            DeleteCascade(db, child.Id);
        }
        var row = db.Tasks.Find(id);
        if (row is not null) { row.DeletedAt = DateTime.Now.ToString("o"); db.SaveChanges(); }
    }
}
