using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Dialogs;
using EarthOnline.Desktop.Services;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Pages;

/// <summary>树形展示用的节点（TaskEntity + 子节点）。</summary>
public class TaskNode
{
    public TaskEntity Task { get; set; } = new();
    public ObservableCollection<TaskNode> Children { get; set; } = new();

    public string Id => Task.Id;
    public string Title => Task.Title;
    public string ProgressText => $"{(Task.Status == "done" ? 100 : Task.Progress)}%";
    public string DueText => string.IsNullOrEmpty(Task.DueDate) ? "" : "截止 " + Task.DueDate;
    public string StatusEmoji => Task.Status switch
    {
        "done" => "✅",
        "active" => "🔥",
        "paused" => "⏸",
        _ => "📝"
    };
}

public partial class TasksPage : Page
{
    private List<TaskEntity> _all = new();

    public TasksPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
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
        return node;
    }

    private TaskEntity? SelectedTask()
    {
        if (TaskTree.SelectedItem is TaskNode n) return n.Task;
        return null;
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => BuildTree();

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
        AchievementEngine.Evaluate();
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
        AchievementEngine.Evaluate();
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
        AchievementEngine.Evaluate();
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
        AchievementEngine.Evaluate();
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
