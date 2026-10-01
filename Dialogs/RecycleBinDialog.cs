using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Services;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Dialogs;

/// <summary>
/// 回收站（v1.0.5）：任务 / 世界日志的软删除暂存区。
/// - 删除操作只打 DeletedAt 标记，30 天内可在此恢复；
/// - 启动时 PurgeExpired() 永久清理超过 30 天的行；
/// - DeletedAt 不参与导出 JSON（JsonIgnore），不影响 WebDAV 按主键合并。
/// </summary>
public static class RecycleBinService
{
    public const int RetainDays = 30;

    private static DateTimeOffset CutOff =>
        DateTimeOffset.Now.AddDays(-RetainDays);

    /// <summary>启动时调用：永久清理超过保留期的软删行。返回清理条数。</summary>
    public static int PurgeExpired()
    {
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            var cut = CutOff.ToString("o");
            var oldTasks = db.Tasks.IgnoreQueryFilters().Where(t => t.DeletedAt != null && string.Compare(t.DeletedAt, cut, StringComparison.Ordinal) < 0).ToList();
            var oldMemos = db.Memos.IgnoreQueryFilters().Where(m => m.DeletedAt != null && string.Compare(m.DeletedAt, cut, StringComparison.Ordinal) < 0).ToList();
            db.Tasks.RemoveRange(oldTasks);
            db.Memos.RemoveRange(oldMemos);
            if (oldTasks.Count + oldMemos.Count > 0) db.SaveChanges();
            return oldTasks.Count + oldMemos.Count;
        }
        catch { return 0; }   // 清理失败不影响使用
    }

    public static int Count()
    {
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            return db.Tasks.IgnoreQueryFilters().Count(t => t.DeletedAt != null)
                 + db.Memos.IgnoreQueryFilters().Count(m => m.DeletedAt != null);
        }
        catch { return 0; }
    }

    /// <summary>恢复：清掉 DeletedAt。任务恢复时若父任务仍在回收站，一并恢复（保持层级完整）。</summary>
    public static bool Restore(string kind, string id)
    {
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            if (kind == "task") RestoreTaskCascade(db, id);
            else
            {
                var m = db.Memos.IgnoreQueryFilters().FirstOrDefault(x => x.Id == id);
                if (m is null) return false;
                m.DeletedAt = null;
            }
            db.SaveChanges();
            return true;
        }
        catch { return false; }
    }

    private static void RestoreTaskCascade(AppDbContext db, string id)
    {
        var row = db.Tasks.IgnoreQueryFilters().FirstOrDefault(t => t.Id == id);
        if (row is null) return;
        // 父任务在回收站里 → 先恢复父级，避免孤儿节点
        if (!string.IsNullOrEmpty(row.ParentId))
        {
            var parent = db.Tasks.IgnoreQueryFilters().FirstOrDefault(t => t.Id == row.ParentId);
            if (parent?.DeletedAt != null) RestoreTaskCascade(db, row.ParentId!);
        }
        row.DeletedAt = null;
    }

    /// <summary>永久删除单条（不可恢复，需确认）。</summary>
    public static bool DeleteForever(string kind, string id)
    {
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            if (kind == "task")
            {
                var t = db.Tasks.IgnoreQueryFilters().FirstOrDefault(x => x.Id == id);
                if (t is null) return false;
                db.Tasks.Remove(t);
            }
            else
            {
                var m = db.Memos.IgnoreQueryFilters().FirstOrDefault(x => x.Id == id);
                if (m is null) return false;
                db.Memos.Remove(m);
            }
            db.SaveChanges();
            return true;
        }
        catch { return false; }
    }
}

/// <summary>回收站窗口：分「任务 / 日志」两页，支持恢复与永久删除。</summary>
public static class RecycleBinDialog
{
    private class Row
    {
        public string Kind = "";      // task / memo
        public string Id = "";
        public string Title = "";
        public string DeletedAt = "";
        public string KindLabel => Kind == "task" ? "🗂" : "📝";
    }

    private static Brush TextMain => ThemeService.Brush("TextPrimaryBrush");
    private static Brush TextSub => ThemeService.Brush("TextSecondaryBrush");
    private static Brush TextMuted => ThemeService.Brush("TextMutedBrush");
    private static Brush BorderSoft => ThemeService.Brush("BorderBrush");

    private static StackPanel _list = null!;
    private static TextBlock _empty = null!;

    public static void Show()
    {
        var win = new ThemeDialogWindow("🗑 回收站", 560, 640);
        var root = new StackPanel { Margin = new Thickness(20) };

        root.Children.Add(new TextBlock
        {
            Text = $"删除的任务与世界日志会在这里保留 {RecycleBinService.RetainDays} 天，超期自动清理。恢复任务时，其上级任务会一并恢复。",
            FontSize = 11, Foreground = TextMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
        });

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 490 };
        _list = new StackPanel();
        scroll.Content = _list;
        root.Children.Add(scroll);

        _empty = new TextBlock
        {
            Text = "回收站是空的，干干净净 ✨",
            FontSize = 13, Foreground = TextMuted, Margin = new Thickness(0, 24, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        root.Children.Add(_empty);

        win.SetBody(root);
        Refresh();
        win.Owner = System.Windows.Application.Current?.MainWindow;
        try { win.ShowDialog(); } catch { win.Show(); }
    }

    private static void Refresh()
    {
        _list.Children.Clear();
        var rows = new List<Row>();
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            rows.AddRange(db.Tasks.IgnoreQueryFilters()
                .Where(t => t.DeletedAt != null)
                .OrderByDescending(t => t.DeletedAt)
                .Select(t => new Row { Kind = "task", Id = t.Id, Title = t.Title, DeletedAt = t.DeletedAt ?? "" }));
            rows.AddRange(db.Memos.IgnoreQueryFilters()
                .Where(m => m.DeletedAt != null)
                .OrderByDescending(m => m.DeletedAt)
                .Select(m => new Row { Kind = "memo", Id = m.Id, Title = m.Text, DeletedAt = m.DeletedAt ?? "" }));
        }
        catch { }

        _empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var r in rows)
        {
            var panel = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(actions, Dock.Right);
            var restore = new Button
            {
                Content = "恢复", Padding = new Thickness(10, 3, 10, 3), FontSize = 12,
                Style = (Style)System.Windows.Application.Current.Resources["SoftButtonStyle"],
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            restore.Click += (_, _) =>
            {
                if (RecycleBinService.Restore(r.Kind, r.Id))
                {
                    AchievementNotifier.Check();
                    Refresh();
                }
            };
            var kill = new Button
            {
                Content = "永久删除", Padding = new Thickness(10, 3, 10, 3), FontSize = 12, Margin = new Thickness(6, 0, 0, 0),
                Style = (Style)System.Windows.Application.Current.Resources["SoftButtonStyle"],
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            kill.Click += (_, _) =>
            {
                if (!SimpleDialogs.Confirm("永久删除后无法恢复，确定？")) return;
                if (RecycleBinService.DeleteForever(r.Kind, r.Id)) Refresh();
            };
            actions.Children.Add(restore);
            actions.Children.Add(kill);
            panel.Children.Add(actions);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var remain = RemainingText(r.DeletedAt);
            info.Children.Add(new TextBlock
            {
                FontSize = 13, Foreground = TextMain, TextTrimming = TextTrimming.CharacterEllipsis,
                Inlines = { new System.Windows.Documents.Run(r.KindLabel + " ") , new System.Windows.Documents.Run(r.Title) },
            });
            info.Children.Add(new TextBlock { FontSize = 11, Foreground = TextMuted, Text = remain });
            panel.Children.Add(info);

            var card = new Border
            {
                BorderBrush = BorderSoft, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(2, 8, 2, 8),
            };
            card.Child = panel;
            _list.Children.Add(card);
        }
    }

    private static string RemainingText(string deletedAt)
    {
        if (DateTimeOffset.TryParse(deletedAt, out var t))
        {
            var left = RecycleBinService.RetainDays - (int)(DateTimeOffset.Now - t).TotalDays;
            return left <= 0 ? "即将自动清理" : $"{t.ToLocalTime():yyyy-MM-dd HH:mm} 删除 · 还剩 {left} 天";
        }
        return "已删除";
    }
}
