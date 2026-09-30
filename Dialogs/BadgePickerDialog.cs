using System.Windows;
using System.Windows.Controls;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Services;

namespace EarthOnline.Desktop.Dialogs;

/// <summary>
/// 徽章佩戴选择对话框（v1.0.3）：从已解锁成就里挑最多 3 枚别在主页徽章墙上。
/// 结果写入传入的 pinned 列表（调用方自行持久化到 settings.json）。
/// </summary>
public static class BadgePickerDialog
{
    public const int MaxPinned = 3;

    private static System.Windows.Media.Brush Bg => ThemeService.Brush("AppBgBrush");
    private static System.Windows.Media.Brush Card => ThemeService.Brush("CardBgBrush");
    private static System.Windows.Media.Brush Border => ThemeService.Brush("BorderBrush");
    private static System.Windows.Media.Brush Accent => ThemeService.Brush("AccentBrush");

    /// <summary>确定返回 true，pinned 被更新为勾选结果（按成就列表顺序）。</summary>
    public static bool Show(List<AchievementEntity> unlocked, List<string> pinned)
    {
        if (unlocked.Count == 0)
        {
            SimpleDialogs.Alert("还没有已解锁的成就，先去成就页解锁一枚吧 🏆", "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var selected = new HashSet<string>(pinned);

        var win = new ThemeDialogWindow("佩戴徽章（最多 3 枚）", 420, 560);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = $"已解锁 {unlocked.Count} 枚成就，勾选要佩戴的（最多 {MaxPinned} 枚）",
            FontSize = 12,
            Foreground = ThemeService.Brush("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        var list = new ScrollViewer { MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel();
        foreach (var a in unlocked)
        {
            var title = string.IsNullOrWhiteSpace(a.Title) ? "成就" : a.Title;
            var cb = new CheckBox
            {
                Content = $"🏆 {title}",
                IsChecked = selected.Contains(a.Id),
                FontSize = 13,
                Margin = new Thickness(2, 4, 0, 4),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            cb.Checked += (_, _) =>
            {
                if (selected.Count >= MaxPinned)
                {
                    cb.IsChecked = false;   // 触发 Unchecked → 不会加入
                    SimpleDialogs.Alert($"最多佩戴 {MaxPinned} 枚徽章，先取消一枚再试。", "地球Online",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                selected.Add(a.Id);
            };
            cb.Unchecked += (_, _) => selected.Remove(a.Id);
            panel.Children.Add(cb);
        }
        list.Content = panel;
        root.Children.Add(list);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancelButton = new Button
        {
            Content = "取消", MinWidth = 84, Height = 34,
            Foreground = ThemeService.Brush("TextPrimaryBrush"),
            Background = Card, BorderBrush = Border, BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        var okButton = new Button
        {
            Content = "保存", MinWidth = 84, Height = 34, Margin = new Thickness(6, 0, 0, 0),
            Foreground = System.Windows.Media.Brushes.White, Background = Accent,
            BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand
        };
        cancelButton.Click += (_, _) => win.DialogResult = false;
        okButton.Click += (_, _) => win.DialogResult = true;
        btnRow.Children.Add(cancelButton);
        btnRow.Children.Add(okButton);
        root.Children.Add(btnRow);

        win.SetBody(root);
        if (win.ShowDialog() != true) return false;

        pinned.Clear();
        foreach (var a in unlocked)
        {
            if (selected.Contains(a.Id)) pinned.Add(a.Id);
        }
        return true;
    }
}
