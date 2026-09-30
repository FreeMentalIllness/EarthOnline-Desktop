using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Services;

namespace EarthOnline.Desktop.Dialogs;

/// <summary>
/// 轻量对话框（纯代码构造，不额外增加 XAML 文件）。
/// 统一视觉：白卡片 + 琥珀主按钮 + 12 圆角。
/// </summary>
public static class SimpleDialogs
{
    private static System.Windows.Media.Brush Bg => ThemeService.Brush("AppBgBrush");
    private static System.Windows.Media.Brush Card => ThemeService.Brush("CardBgBrush");
    private static System.Windows.Media.Brush Border => ThemeService.Brush("BorderBrush");
    private static System.Windows.Media.Brush Accent => ThemeService.Brush("AccentBrush");
    private static System.Windows.Media.Brush TextMain => ThemeService.Brush("TextPrimaryBrush");
    private static System.Windows.Media.Brush TextSub => ThemeService.Brush("TextSecondaryBrush");

    private static Button MakeButton(string text, bool primary)
    {
        var b = new Button
        {
            Content = text,
            MinWidth = 84,
            Height = 34,
            Padding = new Thickness(14, 0, 14, 0),
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        // 统一走 App.xaml 的全局按钮样式（琥珀主按钮 / 暖色次级按钮，圆角 9）
        b.Style = (Style)Application.Current.Resources[primary ? "PrimaryButtonStyle" : "SoftButtonStyle"];
        return b;
    }

    private static ThemeDialogWindow MakeWindow(string title, double width = 420)
    {
        return new ThemeDialogWindow(title, width);
    }

    /// <summary>单字段输入。取消返回 null。</summary>
    public static string? Prompt(string title, string label, string defaultValue = "", bool multiline = false)
    {
        var win = MakeWindow(title);
        var root = new StackPanel { Margin = new Thickness(18) };

        root.Children.Add(new TextBlock
        {
            Text = label, FontSize = 13, Foreground = TextSub, Margin = new Thickness(0, 0, 0, 8)
        });

        var box = new TextBox
        {
            Text = defaultValue,
            Padding = new Thickness(8),
            BorderBrush = Border,
            Background = Card,
            Foreground = TextMain,
            FontSize = 14
        };
        if (multiline)
        {
            box.AcceptesReturnWorkaround();
            box.Height = 90;
            box.TextWrapping = TextWrapping.Wrap;
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        root.Children.Add(box);

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var ok = MakeButton("确定", true);
        var cancel = MakeButton("取消", false);
        bar.Children.Add(cancel);
        bar.Children.Add(ok);
        root.Children.Add(bar);

        string? result = null;
        ok.Click += (_, _) => { result = box.Text; win.DialogResult = true; };
        cancel.Click += (_, _) => { win.DialogResult = false; };
        win.SetBody(root);

        box.Focus();
        box.SelectAll();

        return win.ShowDialog() == true ? result : null;
    }

    /// <summary>主题化确认框（替代系统 MessageBox，与全端暖色圆角风格一致）。</summary>
    public static bool Confirm(string message, string title = "确认")
    {
        var win = MakeWindow(title, 380);
        var root = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

        root.Children.Add(new TextBlock
        {
            Text = message, FontSize = 13, LineHeight = 20, TextWrapping = TextWrapping.Wrap,
            Foreground = TextMain
        });

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var no = MakeButton("取消", false);
        var yes = MakeButton("确定", true);
        no.Margin = new Thickness(0, 0, 0, 0);
        yes.Margin = new Thickness(6, 0, 0, 0);
        no.Click += (_, _) => win.DialogResult = false;
        yes.Click += (_, _) => win.DialogResult = true;
        bar.Children.Add(no);
        bar.Children.Add(yes);
        root.Children.Add(bar);

        win.SetBody(root);
        return win.ShowDialog() == true;
    }

    /// <summary>
    /// 主题化提示弹窗：签名与 System.Windows.MessageBox.Show 兼容（含 YesNo 返回值），
    /// 便于全项目一处替换。No/Cancel 关闭一律返回 No。
    /// </summary>
    public static MessageBoxResult Alert(
        string message, string title = "地球Online",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        bool askYesNo = buttons == MessageBoxButton.YesNo || buttons == MessageBoxButton.YesNoCancel;
        var win = MakeWindow(title, 380);
        var root = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

        root.Children.Add(new TextBlock
        {
            Text = message, FontSize = 13, LineHeight = 20, TextWrapping = TextWrapping.Wrap,
            Foreground = TextMain
        });

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var result = MessageBoxResult.None;
        if (askYesNo)
        {
            var no = MakeButton("否", false);
            var yes = MakeButton("是", true);
            no.Click += (_, _) => { result = MessageBoxResult.No; win.DialogResult = false; };
            yes.Click += (_, _) => { result = MessageBoxResult.Yes; win.DialogResult = true; };
            bar.Children.Add(no);
            bar.Children.Add(yes);
        }
        else
        {
            var ok = MakeButton("好的", true);
            ok.Click += (_, _) => { result = MessageBoxResult.OK; win.DialogResult = true; };
            bar.Children.Add(ok);
        }
        root.Children.Add(bar);

        win.SetBody(root);
        win.ShowDialog();
        return result;
    }

    /// <summary>任务编辑（新建 / 修改）。返回是否点了「保存」。</summary>
    public static bool EditTask(TaskEntity task)
    {
        var win = MakeWindow(string.IsNullOrEmpty(task.Id) ? "新建任务" : "编辑任务", 460);
        var root = new StackPanel { Margin = new Thickness(18) };

        // 标题
        root.Children.Add(Label("标题"));
        var titleBox = new TextBox { Text = task.Title, Padding = new Thickness(8), FontSize = 14, Background = Card, BorderBrush = Border };
        root.Children.Add(titleBox);

        // 分类 + 状态
        var row1 = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        row1.ColumnDefinitions.Add(new ColumnDefinition());
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        row1.ColumnDefinitions.Add(new ColumnDefinition());

        var catBox = new ComboBox
        {
            Padding = new Thickness(8), Background = Card, BorderBrush = Border,
            ItemsSource = new[] { new KV("main", "主线"), new KV("side", "支线"), new KV("todo", "待办") },
            DisplayMemberPath = "Label", SelectedValuePath = "Value", SelectedValue = task.Category
        };
        var statusBox = new ComboBox
        {
            Padding = new Thickness(8), Background = Card, BorderBrush = Border,
            ItemsSource = new[]
            {
                new KV("planning", "筹划中"), new KV("active", "进行中"),
                new KV("paused", "已暂停"), new KV("done", "已完成")
            },
            DisplayMemberPath = "Label", SelectedValuePath = "Value", SelectedValue = task.Status
        };
        Grid.SetColumn(catBox, 0);
        Grid.SetColumn(statusBox, 2);
        row1.Children.Add(catBox);
        row1.Children.Add(statusBox);
        root.Children.Add(row1);

        // 进度
        root.Children.Add(Label($"进度：{task.Progress}%", 12));
        var progressText = (TextBlock)root.Children[^1];
        var slider = new Slider
        {
            Minimum = 0, Maximum = 100, Value = task.Progress,
            TickFrequency = 5, IsSnapToTickEnabled = true, Margin = new Thickness(0, 2, 0, 0)
        };
        slider.ValueChanged += (_, _) => progressText.Text = $"进度：{(int)slider.Value}%";
        root.Children.Add(slider);

        // 截止日期
        root.Children.Add(Label("截止日期（仅待办，留空不限）", 12));
        var dateBox = new TextBox
        {
            Text = task.DueDate ?? "", Padding = new Thickness(8), Background = Card, BorderBrush = Border,
            ToolTip = "格式 YYYY-MM-DD"
        };
        root.Children.Add(dateBox);

        // 备注
        root.Children.Add(Label("备注", 12));
        var noteBox = new TextBox
        {
            Text = task.Note ?? "", Padding = new Thickness(8), Background = Card, BorderBrush = Border,
            Height = 70, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        root.Children.Add(noteBox);

        // 按钮
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var ok = MakeButton("保存", true);
        var cancel = MakeButton("取消", false);
        bar.Children.Add(cancel);
        bar.Children.Add(ok);
        root.Children.Add(bar);

        ok.Click += (_, _) =>
        {
            var t = (titleBox.Text ?? "").Trim();
            if (t.Length == 0)
            {
                // 彩蛋 egg_blank_title 的唯一计数来源（无法从数据推导，必须持久化）
                var st = Data.SettingsStore.Load();
                st.BlankTitleTries++;
                st.Save();
                SimpleDialogs.Alert("标题不能为空", "地球Online", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            task.Title = t;
            task.Category = (catBox.SelectedValue as string) ?? "todo";
            task.Status = (statusBox.SelectedValue as string) ?? "planning";
            task.Progress = (int)slider.Value;
            task.DueDate = string.IsNullOrWhiteSpace(dateBox.Text) ? null : dateBox.Text.Trim();
            task.Note = string.IsNullOrWhiteSpace(noteBox.Text) ? null : noteBox.Text.Trim();
            win.DialogResult = true;
        };
        cancel.Click += (_, _) => win.DialogResult = false;

        win.SetBody(root);
        titleBox.Focus();
        return win.ShowDialog() == true;
    }

    private static TextBlock Label(string text, int topMargin = 0) => new()
    {
        Text = text, FontSize = 12, Foreground = TextSub, Margin = new Thickness(0, topMargin, 0, 6)
    };

    public sealed class KV
    {
        public string Value { get; set; } = "";
        public string Label { get; set; } = "";
        public KV(string v, string l) { Value = v; Label = l; }
    }
}

/// <summary>TextBox.AcceptsReturn 的便捷设置（单独放扩展，避免主逻辑里拼写错误）。</summary>
internal static class TextBoxExt
{
    public static void AcceptesReturnWorkaround(this TextBox box) => box.AcceptsReturn = true;
}
