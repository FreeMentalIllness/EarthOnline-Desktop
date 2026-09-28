using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Dialogs;

/// <summary>
/// 物品新增 / 编辑对话框（对标安卓 AddItemSheet）：
/// 名称 / 实物-虚拟类型 / 分类下拉（含未分类）/ 描述。
/// item 传 null = 新增；确定返回 true，结果写入 item 实例（调用方落库）。
/// </summary>
public static class ItemDialog
{
    private static readonly SolidColorBrush Bg = new(Color.FromRgb(0xF8, 0xF6, 0xF2));
    private static readonly SolidColorBrush Border = new(Color.FromRgb(0xE8, 0xE2, 0xDA));
    private static readonly SolidColorBrush Accent = new(Color.FromRgb(0xD4, 0xA3, 0x73));
    private static readonly SolidColorBrush TextMain = new(Color.FromRgb(0x1E, 0x1A, 0x16));
    private static readonly SolidColorBrush TextSub = new(Color.FromRgb(0x7A, 0x72, 0x68));

    private static TextBox Field(StackPanel root, string label, string initial)
    {
        root.Children.Add(new TextBlock
        {
            Text = label, FontSize = 13, Foreground = TextSub, Margin = new Thickness(0, 10, 0, 4)
        });
        var box = new TextBox { Text = initial, Padding = new Thickness(8, 6, 8, 6), FontSize = 13 };
        root.Children.Add(box);
        return box;
    }

    public static bool Show(ItemEntity item, bool isNew)
    {
        var win = new Window
        {
            Title = isNew ? "新增物品" : "编辑物品",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Bg,
            Owner = Application.Current?.MainWindow
        };
        var root = new StackPanel { Margin = new Thickness(20) };

        var nameBox = Field(root, "物品名称", isNew ? "" : item.Name);
        if (isNew) nameBox.Focus();

        // ===== 类型切换（实物 / 虚拟）=====
        root.Children.Add(new TextBlock
        {
            Text = "类型", FontSize = 13, Foreground = TextSub, Margin = new Thickness(0, 10, 0, 4)
        });
        var typePanel = new StackPanel { Orientation = Orientation.Horizontal };
        var physicalBtn = new ToggleButtonLike("📦 实物", item.Type != "virtual");
        var virtualBtn = new ToggleButtonLike("💾 虚拟", item.Type == "virtual");
        physicalBtn.Click += (_, _) => { physicalBtn.Checked = true; virtualBtn.Checked = false; };
        virtualBtn.Click += (_, _) => { virtualBtn.Checked = true; physicalBtn.Checked = false; };
        typePanel.Children.Add(physicalBtn);
        typePanel.Children.Add(virtualBtn);
        root.Children.Add(typePanel);

        // ===== 分类下拉 =====
        root.Children.Add(new TextBlock
        {
            Text = "分类", FontSize = 13, Foreground = TextSub, Margin = new Thickness(0, 10, 0, 4)
        });
        var catBox = new ComboBox { FontSize = 13, Padding = new Thickness(8, 5, 8, 5) };
        List<BagCategoryEntity> cats;
        using (var db0 = new AppDbContext(AppPaths.DbFile))
        {
            cats = db0.BagCategories.AsNoTracking()
                .Where(c => c.Scope == BagScope.Item)
                .OrderBy(c => c.SortOrder).ToList();
        }
        catBox.Items.Add(new ComboBoxItem { Content = "（未分类）", Tag = "" });
        foreach (var c in cats) catBox.Items.Add(new ComboBoxItem { Content = c.Name, Tag = c.Id });
        int keepCat = 0;
        for (int i = 0; i < catBox.Items.Count; i++)
        {
            if (((ComboBoxItem)catBox.Items[i]!).Tag as string == (item.Category ?? "")) keepCat = i;
        }
        catBox.SelectedIndex = keepCat;
        root.Children.Add(catBox);

        var descBox = Field(root, "描述（可留空）", item.Description ?? "");

        // ===== 按钮 =====
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var cancel = new Button
        {
            Content = "取消", MinWidth = 84, Height = 34, Padding = new Thickness(14, 0, 14, 0),
            Foreground = TextMain, Background = Brushes.White, BorderBrush = Border,
            BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand
        };
        var ok = new Button
        {
            Content = "保存", MinWidth = 84, Height = 34, Padding = new Thickness(14, 0, 14, 0),
            Margin = new Thickness(6, 0, 0, 0), Foreground = Brushes.White, Background = Accent,
            BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand
        };
        cancel.Click += (_, _) => win.DialogResult = false;
        ok.Click += (_, _) => win.DialogResult = true;
        btnRow.Children.Add(cancel);
        btnRow.Children.Add(ok);
        root.Children.Add(btnRow);
        win.Content = root;

        if (win.ShowDialog() != true) return false;
        if (string.IsNullOrWhiteSpace(nameBox.Text))
        {
            MessageBox.Show("物品名称不能为空", "地球Online", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        item.Name = nameBox.Text.Trim();
        item.Type = virtualBtn.Checked ? "virtual" : "physical";
        item.Category = ((catBox.SelectedItem as ComboBoxItem)?.Tag as string) is { Length: > 0 } cid ? cid : null;
        item.Description = string.IsNullOrWhiteSpace(descBox.Text) ? null : descBox.Text.Trim();
        return true;
    }

    /// <summary>轻量切换按钮（避免引 Material）。</summary>
    private class ToggleButtonLike : Button
    {
        private static readonly SolidColorBrush Accent = new(Color.FromRgb(0xD4, 0xA3, 0x73));
        private static readonly SolidColorBrush Chip = new(Color.FromRgb(0xEF, 0xE9, 0xE0));
        private static readonly SolidColorBrush Border = new(Color.FromRgb(0xE8, 0xE2, 0xDA));
        private static readonly SolidColorBrush TextMain = new(Color.FromRgb(0x1E, 0x1A, 0x16));

        public bool Checked
        {
            get => _checked;
            set { _checked = value; Apply(); }
        }
        private bool _checked;

        public ToggleButtonLike(string text, bool @checked)
        {
            Content = text;
            Height = 32;
            Padding = new Thickness(12, 0, 12, 0);
            Margin = new Thickness(0, 0, 8, 0);
            Cursor = System.Windows.Input.Cursors.Hand;
            Checked = @checked;
            Click += (_, _) => Checked = true;
        }

        private void Apply()
        {
            Background = Checked ? Accent : Chip;
            Foreground = Checked ? Brushes.White : TextMain;
            BorderBrush = Checked ? Accent : Border;
            BorderThickness = new Thickness(1);
        }
    }
}
