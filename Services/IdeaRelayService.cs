using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Services;

/// <summary>灵感接力处理记录（idea_relay.json，已处理/已提示过的 memo id 列表）。</summary>
public sealed class IdeaRelayState
{
    public List<string> Processed { get; set; } = new();
}

/// <summary>
/// 跨端灵感接力：手机端记录的灵感（memos.type='idea'）随备份同步到桌面后，
/// 在导入/自动同步落库完成后检测本次新出现的灵感，温和弹窗询问是否转成待办。
///
///  - 不改备份导入的 REPLACE 合并语义，只在其后追加一步「检测 + 提示」；
///  - 已处理记录存 %LOCALAPPDATA%\EarthOnline\idea_relay.json（不动 DB 表结构）；
///  - 弹窗提示一次最多列 10 条，其余折叠为「…等 N 条」；
///  - 任意入口（手动导入 / 云端拉取 / 快照恢复）都调用 CheckAfterImport()，线程安全。
/// </summary>
public static class IdeaRelayService
{
    private static string StoreFile => Path.Combine(AppPaths.RootDir, "idea_relay.json");
    private static readonly object Gate = new();

    /// <summary>列表对话框一次最多展示的灵感条数。</summary>
    private const int MaxShown = 10;

    /// <summary>待办标题最长字数（超出截断）。</summary>
    private const int TitleMax = 60;

    // ==================== 入口 ====================

    /// <summary>导入/同步落库完成后调用（可在任意线程）：有新灵感时在 UI 线程弹提示。</summary>
    public static void CheckAfterImport()
    {
        var app = Application.Current;
        if (app is null) return;
        if (app.Dispatcher.CheckAccess()) ShowRelayFlow();
        else app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ShowRelayFlow));
    }

    private static void ShowRelayFlow()
    {
        try
        {
            var ideas = DetectNewIdeas();
            if (ideas.Count == 0) return;

            // 弹窗优先：自定义 Window（贴现有琥珀风格），不选「看看」也记为已处理，避免每次同步重复打扰
            if (!ShowPrompt(ideas.Count))
            {
                MarkProcessed(ideas.Select(m => m.Id));
                return;
            }

            ShowList(ideas);
            // 列表关闭后全部记为已处理（无论是否转化），避免下次同步再次提示
            MarkProcessed(ideas.Select(m => m.Id));
        }
        catch
        {
            // 提示失败绝不影响导入/同步主流程
        }
    }

    // ==================== 检测与转化 ====================

    /// <summary>本次落库后仍「未处理过」的灵感（type='idea' 且文本非空）。</summary>
    private static List<MemoEntity> DetectNewIdeas()
    {
        List<string> processed;
        lock (Gate) processed = LoadState().Processed;
        var set = new HashSet<string>(processed);

        using var db = new AppDbContext(AppPaths.DbFile);
        return db.Memos.AsNoTracking()
            .Where(m => m.Type == "idea")
            .AsEnumerable()
            .Where(m => m.Id.Length > 0 && !set.Contains(m.Id) && !string.IsNullOrWhiteSpace(m.Text))
            .OrderBy(m => m.CreatedAt)
            .ToList();
    }

    /// <summary>灵感 → 待办（tasks 表）：新 guid、todo/planning、标题截 60 字、order=当前最大+1。</summary>
    private static void ConvertToTask(MemoEntity memo)
    {
        using var db = new AppDbContext(AppPaths.DbFile);
        int maxOrder = db.Tasks.Any() ? db.Tasks.Max(t => t.Order) : 0;
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var text = memo.Text.Trim();

        db.Tasks.Add(new TaskEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            ParentId = null,
            Category = "todo",
            Title = text.Length > TitleMax ? text[..TitleMax] : text,
            Status = "planning",
            Progress = 0,
            CreatedAt = today,
            LastModified = today,
            Order = maxOrder + 1
        });
        db.SaveChanges();
    }

    // ==================== 已处理记录（JSON 文件，不动表结构） ====================

    private static IdeaRelayState LoadState()
    {
        try
        {
            if (File.Exists(StoreFile))
            {
                return JsonSerializer.Deserialize<IdeaRelayState>(File.ReadAllText(StoreFile)) ?? new IdeaRelayState();
            }
        }
        catch
        {
            // 坏文件按空处理，下次保存覆盖
        }
        return new IdeaRelayState();
    }

    private static void MarkProcessed(IEnumerable<string> ids)
    {
        lock (Gate)
        {
            var state = LoadState();
            var set = new HashSet<string>(state.Processed);
            bool changed = false;
            foreach (var id in ids)
            {
                if (set.Add(id)) changed = true;
            }
            if (!changed) return;
            state.Processed = set.ToList();
            AppPaths.EnsureDirectories();
            File.WriteAllText(StoreFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    // ==================== 弹窗 UI（贴 SimpleDialogs 的琥珀风格，纯代码构造） ====================

    private static System.Windows.Media.Brush Bg => ThemeService.Brush("AppBgBrush");
    private static System.Windows.Media.Brush Card => ThemeService.Brush("CardBgBrush");
    private static System.Windows.Media.Brush Border => ThemeService.Brush("BorderBrush");
    private static System.Windows.Media.Brush Accent => ThemeService.Brush("AccentBrush");
    private static System.Windows.Media.Brush TextMain => ThemeService.Brush("TextPrimaryBrush");
    private static System.Windows.Media.Brush TextSub => ThemeService.Brush("TextSecondaryBrush");

    private static Window MakeWindow(string title, double width)
    {
        return new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Bg,
            Owner = Application.Current?.MainWindow
        };
    }

    private static Button MakeButton(string text, bool primary)
    {
        return new Button
        {
            Content = text,
            MinWidth = 84,
            Height = 34,
            Padding = new Thickness(14, 0, 14, 0),
            Margin = new Thickness(6, 0, 0, 0),
            Foreground = primary ? Brushes.White : TextMain,
            Background = primary ? Accent : Card,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };
    }

    /// <summary>温和提示：「同步后发现 N 条来自手机的灵感，要看看并转成待办吗？」</summary>
    private static bool ShowPrompt(int count)
    {
        var win = MakeWindow("灵感接力", 380);
        var root = new StackPanel { Margin = new Thickness(20) };

        root.Children.Add(new TextBlock
        {
            Text = $"同步后发现 {count} 条来自手机的灵感，要看看并转成待办吗？",
            FontSize = 14,
            Foreground = TextMain,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var later = MakeButton("忽略", false);
        var look = MakeButton("看看", true);
        later.Click += (_, _) => win.DialogResult = false;
        look.Click += (_, _) => win.DialogResult = true;
        bar.Children.Add(later);
        bar.Children.Add(look);
        root.Children.Add(bar);

        win.Content = root;
        return win.ShowDialog() == true;
    }

    /// <summary>灵感列表：每条一键「转为待办」，一次最多 10 条，其余折叠为「…等 N 条」。</summary>
    private static void ShowList(List<MemoEntity> ideas)
    {
        var win = MakeWindow("来自手机的灵感", 460);
        var root = new StackPanel { Margin = new Thickness(18) };

        root.Children.Add(new TextBlock
        {
            Text = "点击「转为待办」把灵感加入任务清单（该灵感在手机端保持不变）。",
            FontSize = 12,
            Foreground = TextSub,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var scroll = new ScrollViewer
        {
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var list = new StackPanel();
        foreach (var memo in ideas.Take(MaxShown))
        {
            list.Children.Add(MakeRow(memo));
        }
        scroll.Content = list;
        root.Children.Add(scroll);

        if (ideas.Count > MaxShown)
        {
            root.Children.Add(new TextBlock
            {
                Text = $"…等 {ideas.Count} 条",
                FontSize = 12,
                Foreground = TextSub,
                Margin = new Thickness(0, 8, 0, 0)
            });
        }

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var done = MakeButton("完成", true);
        done.Click += (_, _) => win.Close();
        bar.Children.Add(done);
        root.Children.Add(bar);

        win.Content = root;
        win.ShowDialog();
    }

    private static Border MakeRow(MemoEntity memo)
    {
        var text = new TextBlock
        {
            Text = memo.Text.Trim(),
            FontSize = 13,
            Foreground = TextMain,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        var btn = new Button
        {
            Content = "转为待办",
            MinWidth = 76,
            Height = 28,
            Padding = new Thickness(10, 0, 10, 0),
            Foreground = Brushes.White,
            Background = Accent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        btn.Click += (_, _) =>
        {
            try
            {
                ConvertToTask(memo);
                AchievementNotifier.Check();
                btn.IsEnabled = false;
                btn.Content = "已转为待办";
            }
            catch (Exception ex)
            {
                MessageBox.Show("转换失败：" + ex.Message, "地球Online",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        var grid = new Grid { MinWidth = 380 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 0);
        Grid.SetColumn(btn, 1);
        grid.Children.Add(text);
        grid.Children.Add(btn);

        return new Border
        {
            Background = Card,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Child = grid
        };
    }
}
