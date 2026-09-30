using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EarthOnline.Desktop.Services;

namespace EarthOnline.Desktop.Dialogs;

/// <summary>
/// 统一主题化对话框窗体：去掉系统默认的方形灰壳，
/// 全部二级窗口（编辑资料 / 任务编辑 / 选择器 / 确认与输入弹窗）共用同一套
/// 圆角卡体（14）+ 主题配色 + 自绘标题栏（可拖动、关闭按钮）。
/// 无模糊无阴影层（遵守悬浮反馈规范），用 1px 暖边框做层级分离。
/// </summary>
public sealed class ThemeDialogWindow : Window
{
    private readonly Border _bodyHost;

    public ThemeDialogWindow(string title, double width, double maxHeight = 720)
    {
        Title = title;
        Width = width;
        MaxHeight = maxHeight;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        // Owner 需要宿主窗口已显示；自测/无头环境下未显示会抛异常，防御性忽略
        try { Owner = Application.Current?.MainWindow; } catch { /* 宿主未显示时忽略 */ }

        var card = new Border
        {
            Background = ThemeService.Brush("CardBgBrush"),
            BorderBrush = ThemeService.Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14)
        };

        // ---- 标题栏 ----
        var titleBar = new Grid { Height = 46, Background = Brushes.Transparent };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeService.Brush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 0, 0)
        };
        Grid.SetColumn(titleText, 0);
        titleBar.Children.Add(titleText);

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 32,
            Height = 32,
            Margin = new Thickness(0, 0, 10, 0),
            FontSize = 13,
            Foreground = ThemeService.Brush("TextSecondaryBrush"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        closeBtn.Click += (_, _) => Close();
        Grid.SetColumn(closeBtn, 1);
        titleBar.Children.Add(closeBtn);

        // 标题栏整体可拖动（关闭按钮不吞拖动：按钮自身处理点击）
        titleBar.MouseLeftButtonDown += (_, _) =>
        {
            try { DragMove(); } catch { /* 非激活态拖动会抛异常，忽略 */ }
        };

        _bodyHost = new Border
        {
            Padding = new Thickness(20, 4, 20, 20),
            Child = null
        };
        // 关键：限制主体高度上限。否则 StackPanel 纵向给 ScrollViewer 无限高度，
        // 内容超过窗口 MaxHeight 时被直接裁掉且永远滚不动（「编辑资料无法下滑」根因）。
        // 72 = 标题栏 46 + 主体上下 padding 24 + 卡片边框 2。
        _bodyHost.MaxHeight = Math.Max(200, maxHeight - 72);

        var stack = new StackPanel();
        stack.Children.Add(titleBar);
        stack.Children.Add(_bodyHost);
        card.Child = stack;
        Content = card;

        // Esc 关闭（等同取消）
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    /// <summary>设置对话框主体内容（原有 win.Content = root 的替代入口）。</summary>
    public void SetBody(UIElement body) => _bodyHost.Child = body;
}
