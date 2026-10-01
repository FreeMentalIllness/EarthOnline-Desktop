using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Services;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Dialogs;

/// <summary>
/// 全局搜索（v1.0.5，Ctrl+F）：任务 / 物品 / 收藏 / 世界日志 / 足迹 一框全查。
/// 选中结果回车或双击 → 跳转到对应模块（数据已在内存，无需再查）。
/// </summary>
public static class GlobalSearchDialog
{
    private sealed class Hit
    {
        public string Module = "";    // tasks / backpack / memos / map
        public string Emoji = "";
        public string Title = "";
        public string Sub = "";
    }

    private static Brush TextMain => ThemeService.Brush("TextPrimaryBrush");
    private static Brush TextSub => ThemeService.Brush("TextSecondaryBrush");
    private static Brush TextMuted => ThemeService.Brush("TextMutedBrush");
    private static Brush CardBg => ThemeService.Brush("ChipFillBrush");
    private static Brush Accent => ThemeService.Brush("AccentBrush");

    private static ListBox _results = null!;
    private static TextBlock _hint = null!;

    /// <summary>弹搜索框。返回值无用，仅为语义完整。</summary>
    public static void Show()
    {
        var win = new ThemeDialogWindow("🔍 全局搜索", 520);
        var root = new StackPanel { Margin = new Thickness(20) };

        var box = new TextBox { FontSize = 15, Padding = new Thickness(10, 8, 10, 8) };
        box.TextChanged += (_, _) => Search(box.Text);
        root.Children.Add(box);

        root.Children.Add(new TextBlock
        {
            Text = "任务 · 物品 · 收藏 · 世界日志 · 足迹　（Esc 关闭，双击结果跳转）",
            FontSize = 11, Foreground = TextMuted, Margin = new Thickness(0, 6, 0, 8),
        });

        _hint = new TextBlock { FontSize = 12, Foreground = TextMuted, Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed };
        root.Children.Add(_hint);

        _results = new ListBox
        {
            MaxHeight = 380, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        _results.ItemTemplate = RowTemplate();
        _results.MouseDoubleClick += (_, _) => Go(win);
        _results.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Go(win); e.Handled = true; } };
        root.Children.Add(_results);

        win.SetBody(root);
        win.Owner = System.Windows.Application.Current?.MainWindow;
        win.Loaded += (_, _) => box.Focus();

        // Esc 关闭
        win.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) win.Close(); };

        try { win.ShowDialog(); } catch { win.Show(); }
    }

    private static void Go(ThemeDialogWindow win)
    {
        if (_results.SelectedItem is not Hit h) return;
        var mw = System.Windows.Application.Current?.MainWindow as MainWindow;
        win.Close();
        mw?.NavigateTo(h.Module);
    }

    private static void Search(string q)
    {
        q = q.Trim().ToLowerInvariant();
        _results.Items.Clear();
        if (q.Length == 0) { _hint.Visibility = Visibility.Collapsed; return; }

        var hits = new List<Hit>();
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);

            // 任务（含子任务标题 + 备注）
            foreach (var t in db.Tasks.AsNoTracking().ToList())
            {
                if (t.Title.ToLowerInvariant().Contains(q) || (t.Note ?? "").ToLowerInvariant().Contains(q))
                {
                    var statusLabel = t.Status switch
                    {
                        "done" => "已完成", "planning" => "规划中", "active" => "进行中",
                        "paused" => "已暂停", _ => t.Status,
                    };
                    hits.Add(new Hit
                    {
                        Module = "tasks", Emoji = t.Status == "done" ? "✅" : "🗂",
                        Title = t.Title,
                        Sub = "任务 · " + statusLabel,
                    });
                }
            }

            // 物品 / 收藏
            foreach (var i in db.Items.AsNoTracking().ToList())
                if (i.Name.ToLowerInvariant().Contains(q) || (i.Description ?? "").ToLowerInvariant().Contains(q))
                    hits.Add(new Hit { Module = "backpack", Emoji = "🎒", Title = i.Name, Sub = "物品 · " + (i.Description ?? "") });
            foreach (var c in db.Collections.AsNoTracking().ToList())
                if (c.Title.ToLowerInvariant().Contains(q) || (c.Note ?? "").ToLowerInvariant().Contains(q))
                    hits.Add(new Hit { Module = "backpack", Emoji = "⭐", Title = c.Title, Sub = "收藏 · " + (c.Note ?? "") });

            // 世界日志
            foreach (var m in db.Memos.AsNoTracking().ToList())
                if (m.Text.ToLowerInvariant().Contains(q))
                    hits.Add(new Hit { Module = "home", Emoji = "📝", Title = m.Text, Sub = "世界日志 · " + m.CreatedAt[..Math.Min(10, m.CreatedAt.Length)] });

            // 足迹
            foreach (var l in db.Locations.AsNoTracking().ToList())
                if (l.Name.ToLowerInvariant().Contains(q))
                    hits.Add(new Hit { Module = "map", Emoji = "📍", Title = l.Name, Sub = "足迹 · " + (l.Note ?? "") });
        }
        catch { }

        foreach (var h in hits.Take(40)) _results.Items.Add(h);
        _hint.Visibility = Visibility.Visible;
        _hint.Text = hits.Count == 0 ? $"没有与「{q}」匹配的结果" : $"共 {hits.Count} 条结果" + (hits.Count > 40 ? "（仅显示前 40 条）" : "");
    }

    /// <summary>结果行渲染（DataTemplate 的代码版，避免 XAML 资源注入）。</summary>
    public static DataTemplate RowTemplate()
    {
        var tpl = new DataTemplate();
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var emoji = new FrameworkElementFactory(typeof(TextBlock));
        emoji.SetBinding(TextBlock.TextProperty, new Binding("Emoji"));
        emoji.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 8, 0));
        var col = new FrameworkElementFactory(typeof(StackPanel));
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new Binding("Title"));
        title.SetValue(TextBlock.FontSizeProperty, 13.0);
        title.SetValue(TextBlock.ForegroundProperty, TextMain);
        title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        var sub = new FrameworkElementFactory(typeof(TextBlock));
        sub.SetBinding(TextBlock.TextProperty, new Binding("Sub"));
        sub.SetValue(TextBlock.FontSizeProperty, 11.0);
        sub.SetValue(TextBlock.ForegroundProperty, TextSub);
        col.AppendChild(title);
        col.AppendChild(sub);
        panel.AppendChild(emoji);
        panel.AppendChild(col);
        tpl.VisualTree = panel;
        return tpl;
    }
}
