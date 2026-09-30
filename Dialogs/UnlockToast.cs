using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Media;
using EarthOnline.Desktop.Services;

namespace EarthOnline.Desktop.Dialogs;

/// <summary>
/// Steam 风格成就解锁悬浮通知：右下角滑入、停留后淡出、提示音。
/// 静态队列逐条弹出（多成就同时解锁时排队，不叠窗）。
/// </summary>
public static class UnlockToast
{
    private static readonly Queue<string> Pending = new();
    private static bool _showing;

    /// <summary>弹一条通知（外部入口；正在显示时排队）。</summary>
    public static void Show(string title, string subtitle = "")
    {
        Pending.Enqueue(title + "|" + subtitle);
        if (!_showing) ShowNext();
    }

    private static void ShowNext()
    {
        if (Pending.Count == 0) { _showing = false; return; }
        _showing = true;

        var (title, subtitle) = Pending.Dequeue().Split('|') switch
        {
            var a when a.Length >= 2 => (a[0], a[1]),
            var a => (a[0], "")
        };

        var win = BuildWindow(title, subtitle);
        win.Closed += (_, _) => ShowNext();
        // ShowActivated=false：不抢焦点（游戏/工作中不被打断，Steam 行为）
        win.Show();
    }

    private static Window BuildWindow(string title, string subtitle)
    {
        var icon = new TextBlock
        {
            Text = "🏆",
            FontSize = 30,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = "成就已解锁",
            FontSize = 11,
            Foreground = ThemeService.FromHex("#B0A89C"),
            Margin = new Thickness(0, 0, 0, 3)
        });
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeService.FromHex("#1E1A16"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 260
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            stack.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 12,
                Foreground = ThemeService.FromHex("#7A7268"),
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 260
            });
        }

        var card = new Border
        {
            Background = ThemeService.FromHex("#FFFFFF"),
            BorderBrush = ThemeService.FromHex("#D4A373"),
            BorderThickness = new Thickness(1, 1, 3, 1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, stack } }
            // 悬浮/浮层规范：不用 DropShadowEffect（高斯模糊类，重栅格化发虚），改暖色描边分层
        };

        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = card
        };
        win.MouseDown += (_, _) => win.Close();

        // 定位：工作区右下角（避开任务栏）
        win.Loaded += (_, _) =>
        {
            double left = SystemParameters.WorkArea.Right - win.ActualWidth - 18;
            double top = SystemParameters.WorkArea.Bottom - win.ActualHeight - 18;
            win.Left = SystemParameters.WorkArea.Right + 20;  // 先放屏幕外再滑入
            win.Top = top;

            var slide = new DoubleAnimation(left, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            win.BeginAnimation(Window.LeftProperty, slide);

            // 提示音（系统提示音，零资源依赖；失败静默）
            try { SystemSounds.Exclamation.Play(); } catch { /* 无声卡环境忽略 */ }

            // 停留 4.2s 后淡出关闭
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(4200)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(380));
                fade.Completed += (_, _) => win.Close();
                win.BeginAnimation(Window.OpacityProperty, fade);
            };
            timer.Start();
        };

        return win;
    }
}
