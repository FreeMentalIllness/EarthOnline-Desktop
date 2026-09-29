using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Dialogs;
using Microsoft.EntityFrameworkCore;
using EarthOnline.Desktop.Services;

namespace EarthOnline.Desktop.Dialogs;

/// <summary>
/// 背包分类管理对话框（对标安卓 CategoryManager / 网页分类管理模态）：
/// 按 scope 管理（物品与收藏各自独立），支持新增 / 重命名 / 删除。
/// 删除分类时，引用它的条目回落「未分类」（category 置空，条目不删），与两端语义一致。
/// 对话框内直接落库；返回 true 表示有改动（调用方刷新列表）。
/// </summary>
public static class CategoryDialog
{
    private static System.Windows.Media.Brush Bg => ThemeService.Brush("AppBgBrush");
    private static System.Windows.Media.Brush Card => ThemeService.Brush("CardBgBrush");
    private static System.Windows.Media.Brush Border => ThemeService.Brush("BorderBrush");
    private static System.Windows.Media.Brush Accent => ThemeService.Brush("AccentBrush");
    private static System.Windows.Media.Brush TextMain => ThemeService.Brush("TextPrimaryBrush");
    private static System.Windows.Media.Brush TextSub => ThemeService.Brush("TextSecondaryBrush");
    private static System.Windows.Media.Brush Chip => ThemeService.Brush("ChipFillBrush");

    public static bool Show(string scope)
    {
        bool isItem = scope == BagScope.Item;
        var win = new Window
        {
            Title = isItem ? "管理物品分类" : "管理收藏分类",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 560,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Bg,
            Owner = Application.Current?.MainWindow
        };
        bool changed = false;

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = "删除分类后，该分类下的条目会回落为「未分类」（条目本身不删）。",
            FontSize = 12, Foreground = TextSub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        });

        var list = new ListBox
        {
            Height = 200, Background = Card, BorderBrush = Border, BorderThickness = new Thickness(1), FontSize = 13
        };

        List<BagCategoryEntity> Load()
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            return db.BagCategories.AsNoTracking()
                .Where(c => c.Scope == scope)
                .OrderBy(c => c.SortOrder)
                .ToList();
        }

        void Fill()
        {
            int keep = list.SelectedIndex;
            list.Items.Clear();
            var cats = Load();
            if (cats.Count == 0)
            {
                list.Items.Add("（还没有分类，点下方「＋ 新增」创建）");
                list.IsEnabled = false;
            }
            else
            {
                list.IsEnabled = true;
                foreach (var c in cats) list.Items.Add(c.Name);
            }
            list.SelectedIndex = keep >= 0 && keep < list.Items.Count ? keep : -1;
        }

        Fill();
        root.Children.Add(list);

        var btnRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        Button MkBtn(string text, bool primary = false)
        {
            return new Button
            {
                Content = text, Height = 34, Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 8, 6),
                Foreground = primary ? Brushes.White : TextMain,
                Background = primary ? Accent : Card,
                BorderBrush = Border, BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        var addBtn = MkBtn("＋ 新增", primary: true);
        var renameBtn = MkBtn("✏️ 重命名");
        var delBtn = MkBtn("🗑 删除");
        btnRow.Children.Add(addBtn);
        btnRow.Children.Add(renameBtn);
        btnRow.Children.Add(delBtn);
        root.Children.Add(btnRow);

        var closeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0)
        };
        var closeBtn = new Button
        {
            Content = "完成", MinWidth = 84, Height = 34, Padding = new Thickness(14, 0, 14, 0),
            Foreground = Brushes.White, Background = Accent, BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        closeBtn.Click += (_, _) => win.DialogResult = true;
        closeRow.Children.Add(closeBtn);
        root.Children.Add(closeRow);

        // ===== 操作 =====
        addBtn.Click += (_, _) =>
        {
            var name = SimpleDialogs.Prompt("新增分类", "分类名称");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            var cats = Load();
            if (cats.Any(c => c.Name == name))
            {
                MessageBox.Show("已存在同名分类", "地球Online", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            using (var db = new AppDbContext(AppPaths.DbFile))
            {
                db.BagCategories.Add(new BagCategoryEntity
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = name,
                    Scope = scope,
                    SortOrder = cats.Count == 0 ? 0 : cats.Max(c => c.SortOrder) + 1
                });
                db.SaveChanges();
            }
            changed = true;
            Fill();
        };

        renameBtn.Click += (_, _) =>
        {
            var cats = Load();
            int idx = list.SelectedIndex;
            if (idx < 0 || idx >= cats.Count) return;
            var cur = cats[idx];
            var name = SimpleDialogs.Prompt("重命名分类", "新的分类名称", cur.Name);
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == cur.Name) return;
            name = name.Trim();
            if (cats.Any(c => c.Id != cur.Id && c.Name == name))
            {
                MessageBox.Show("已存在同名分类", "地球Online", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            using (var db = new AppDbContext(AppPaths.DbFile))
            {
                var row = db.BagCategories.Find(cur.Id);
                if (row is null) return;
                row.Name = name;
                db.SaveChanges();
            }
            changed = true;
            Fill();
        };

        delBtn.Click += (_, _) =>
        {
            var cats = Load();
            int idx = list.SelectedIndex;
            if (idx < 0 || idx >= cats.Count) return;
            var cur = cats[idx];
            if (!SimpleDialogs.Confirm($"确定删除分类「{cur.Name}」？\n该分类下的条目将回落为「未分类」。")) return;

            using (var db = new AppDbContext(AppPaths.DbFile))
            {
                var row = db.BagCategories.Find(cur.Id);
                if (row is not null) db.BagCategories.Remove(row);

                // 条目回落「未分类」：引用该分类的条目 category 置空（与两端语义一致）
                if (isItem)
                {
                    foreach (var it in db.Items.Where(i => i.Category == cur.Id).ToList())
                        it.Category = null;
                }
                else
                {
                    foreach (var c in db.Collections.Where(c => c.Category == cur.Id).ToList())
                        c.Category = null;
                }
                db.SaveChanges();
            }
            changed = true;
            Fill();
        };

        win.Content = root;
        win.ShowDialog();
        return changed;
    }
}
