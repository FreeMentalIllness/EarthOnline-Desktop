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
        Loaded += (_, _) =>
        {
            _loaded = true;
            Reload();
        };
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
        }
        catch (Exception ex)
        {
            MessageBox.Show("加载任务失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BuildTree()
    {
        // 防御：控件未就绪时直接返回，杜绝 NullReferenceException
        if (TaskTree == null) return;

        var filter = (CategoryFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        var scoped = string.IsNullOrEmpty(filter) ? _all : _all.Where(t => t.Category == filter).ToList();

        var nodes = new ObservableCollection<TaskNode>();
        foreach (var t in scoped.Where(t => string.IsNullOrEmpty(t.ParentId)))
        {
            nodes.Add(BuildNode(t, scoped));
        }
        TaskTree.ItemsSource = nodes;
    }

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
    }

    private void AddRoot_Click(object sender, RoutedEventArgs e) => CreateTask(null);

    private void AddChild_Click(object sender, RoutedEventArgs e)
    {
        var parent = SelectedTask();
        if (parent is null)
        {
            MessageBox.Show("请先选中一个任务作为父任务", "地球Online", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show("请先选中一个任务", "地球Online", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private static void DeleteCascade(AppDbContext db, string id)
    {
        foreach (var child in db.Tasks.Where(t => t.ParentId == id).ToList())
        {
            DeleteCascade(db, child.Id);
        }
        var row = db.Tasks.Find(id);
        if (row is not null) db.Tasks.Remove(row);
    }
}
