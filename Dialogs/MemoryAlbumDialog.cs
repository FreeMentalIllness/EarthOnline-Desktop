using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Services;

namespace EarthOnline.Desktop.Dialogs;

/// <summary>
/// 记忆相册（v1.0.5）：老照片导入 + 浏览。
/// 照片原图完整复制到 files/album/（对齐安卓策略：不重编码）；
/// 计数进 SettingsStore.MemoryPhotos，驱动成就 egg_memory_album。
/// </summary>
public static class MemoryAlbumDialog
{
    private static readonly string[] Exts = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };

    private static Brush TextSub => ThemeService.Brush("TextSecondaryBrush");
    private static Brush TextMuted => ThemeService.Brush("TextMutedBrush");
    private static Brush BorderBrushSoft => ThemeService.Brush("BorderBrush");

    private static string AlbumDir => Path.Combine(AppPaths.FilesDir, "album");

    private static WrapPanel _grid = null!;
    private static TextBlock _countText = null!;
    private static TextBlock _emptyText = null!;

    public static void Show()
    {
        var win = new ThemeDialogWindow("记忆相册", 600, 640);
        var root = new StackPanel { Margin = new Thickness(20) };

        var head = new DockPanel();
        var importBtn = new Button
        {
            Content = "＋ 导入老照片", Padding = new Thickness(12, 6, 12, 6),
            Style = (Style)System.Windows.Application.Current.Resources["PrimaryButtonStyle"],
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        importBtn.Click += Import_Click;
        DockPanel.SetDock(importBtn, Dock.Right);
        head.Children.Add(importBtn);

        _countText = new TextBlock { FontSize = 12, Foreground = TextSub, VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(_countText);
        root.Children.Add(head);

        root.Children.Add(new TextBlock
        {
            Text = "老照片原图保存在本机数据目录（不重编码、不上传），随时可来翻一翻。",
            FontSize = 11, Foreground = TextMuted, Margin = new Thickness(0, 6, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        });

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 460 };
        _grid = new WrapPanel();
        scroll.Content = _grid;
        root.Children.Add(scroll);

        _emptyText = new TextBlock
        {
            Text = "相册还是空的。导入第一张老照片，留住一段记忆 ✨",
            FontSize = 13, Foreground = TextMuted, Margin = new Thickness(0, 24, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        root.Children.Add(_emptyText);

        win.SetBody(root);
        Refresh();
        win.Owner = System.Windows.Application.Current?.MainWindow;
        try { win.ShowDialog(); } catch { win.Show(); }
    }

    private static void Import_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择要导入的老照片",
                Filter = "图片|*.jpg;*.jpeg;*.png;*.gif;*.webp;*.bmp|所有文件|*.*",
                Multiselect = true,
            };
            if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0) return;

            Directory.CreateDirectory(AlbumDir);
            int added = 0;
            foreach (var src in dlg.FileNames)
            {
                var ext = Path.GetExtension(src).ToLowerInvariant();
                if (!Exts.Contains(ext)) continue;
                // 原图完整复制不重编码（对齐安卓图片策略）
                var dest = Path.Combine(AlbumDir,
                    DateTime.Now.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8] + ext);
                File.Copy(src, dest, overwrite: false);
                added++;
            }

            if (added > 0)
            {
                var st = SettingsStore.Load();
                st.MemoryPhotos += added;
                st.Save();
                AchievementNotifier.Check();
            }
            Refresh();
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("导入失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void Refresh()
    {
        _grid.Children.Clear();
        var list = new List<string>();
        try
        {
            if (Directory.Exists(AlbumDir))
                list = Directory.GetFiles(AlbumDir).OrderByDescending(f => f).ToList();
        }
        catch { /* 目录不可读则按空相册展示 */ }

        _countText.Text = $"共 {list.Count} 张";
        _emptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var f in list.Take(60))
        {
            var border = new Border
            {
                Width = 104, Height = 104, Margin = new Thickness(4),
                CornerRadius = new CornerRadius(10),
                BorderBrush = BorderBrushSoft, BorderThickness = new Thickness(1),
            };
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;   // 释放文件句柄
                bmp.UriSource = new Uri(f);
                bmp.EndInit();
                border.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
                ToolTipService.SetToolTip(border, Path.GetFileName(f));
            }
            catch
            {
                border.Child = new TextBlock
                {
                    Text = "🖼", FontSize = 28,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }
            _grid.Children.Add(border);
        }
    }
}
