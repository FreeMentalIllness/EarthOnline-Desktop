using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace EarthOnline.Desktop.Dialogs;

/// <summary>
/// 收藏新增 / 编辑对话框（对标安卓收藏新增）：
/// 标题 / 分类下拉 / 备注 / 附件（选择文件 → 复制到本地私有目录 FilesDir 持久化）。
/// col 传 null = 新增；确定返回 true，结果写入 col 实例（调用方落库）。
/// </summary>
public static class CollectionDialog
{
    private static readonly SolidColorBrush Bg = new(Color.FromRgb(0xF8, 0xF6, 0xF2));
    private static readonly SolidColorBrush Border = new(Color.FromRgb(0xE8, 0xE2, 0xDA));
    private static readonly SolidColorBrush Accent = new(Color.FromRgb(0xD4, 0xA3, 0x73));
    private static readonly SolidColorBrush TextMain = new(Color.FromRgb(0x1E, 0x1A, 0x16));
    private static readonly SolidColorBrush TextSub = new(Color.FromRgb(0x7A, 0x72, 0x68));
    private static readonly SolidColorBrush Chip = new(Color.FromRgb(0xEF, 0xE9, 0xE0));

    /// <summary>对话框内暂存的附件（源路径 → 复制；IsPicked 区分「新增了附件」与「沿用旧附件」）。</summary>
    private sealed class PendingFile
    {
        public string? SourcePath;      // 新选的源文件
        public string? ExistingUri;     // 沿用旧附件
        public FileMeta Meta = new();
    }

    public static bool Show(CollectionEntity col, bool isNew)
    {
        var win = new Window
        {
            Title = isNew ? "新增收藏" : "编辑收藏",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Bg,
            Owner = Application.Current?.MainWindow
        };
        var root = new StackPanel { Margin = new Thickness(20) };

        // ===== 标题 =====
        root.Children.Add(new TextBlock
        {
            Text = "收藏标题", FontSize = 13, Foreground = TextSub, Margin = new Thickness(0, 0, 0, 4)
        });
        var titleBox = new TextBox
        {
            Text = isNew ? "" : col.Title, Padding = new Thickness(8, 6, 8, 6), FontSize = 13
        };
        if (isNew) titleBox.Focus();
        root.Children.Add(titleBox);

        // ===== 分类 =====
        root.Children.Add(new TextBlock
        {
            Text = "分类", FontSize = 13, Foreground = TextSub, Margin = new Thickness(0, 10, 0, 4)
        });
        var catBox = new ComboBox { FontSize = 13, Padding = new Thickness(8, 5, 8, 5) };
        List<BagCategoryEntity> cats;
        using (var db0 = new AppDbContext(AppPaths.DbFile))
        {
            cats = db0.BagCategories.AsNoTracking()
                .Where(c => c.Scope == BagScope.Collection)
                .OrderBy(c => c.SortOrder).ToList();
        }
        catBox.Items.Add(new ComboBoxItem { Content = "（未分类）", Tag = "" });
        foreach (var c in cats) catBox.Items.Add(new ComboBoxItem { Content = c.Name, Tag = c.Id });
        int keepCat = 0;
        for (int i = 0; i < catBox.Items.Count; i++)
        {
            if (((ComboBoxItem)catBox.Items[i]!).Tag as string == (col.Category ?? "")) keepCat = i;
        }
        catBox.SelectedIndex = keepCat;
        root.Children.Add(catBox);

        // ===== 备注 =====
        root.Children.Add(new TextBlock
        {
            Text = "备注（可留空）", FontSize = 13, Foreground = TextSub, Margin = new Thickness(0, 10, 0, 4)
        });
        var noteBox = new TextBox
        {
            Text = col.Note ?? "", Padding = new Thickness(8, 6, 8, 6), FontSize = 13,
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            Height = 64, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        root.Children.Add(noteBox);

        // ===== 附件 =====
        root.Children.Add(new TextBlock
        {
            Text = "附件（复制进本地私有目录，离线可用）", FontSize = 13, Foreground = TextSub,
            Margin = new Thickness(0, 10, 0, 4)
        });
        var pending = new PendingFile();
        if (!isNew && !string.IsNullOrEmpty(col.FileMetaJson))
        {
            try { pending.Meta = JsonSerializer.Deserialize<FileMeta>(col.FileMetaJson) ?? new FileMeta(); }
            catch { pending.Meta = new FileMeta(); }
            pending.ExistingUri = col.FileUri;
        }

        var fileText = new TextBlock { FontSize = 12, Foreground = TextMain, TextWrapping = TextWrapping.Wrap };
        void UpdateFileText()
        {
            if (pending.SourcePath is null && pending.ExistingUri is null &&
                string.IsNullOrEmpty(pending.Meta.Name))
            {
                fileText.Text = "（未选择附件）";
                fileText.Foreground = TextSub;
            }
            else
            {
                var sizeKb = pending.Meta.Size / 1024.0;
                var sizeText = sizeKb >= 1024
                    ? (sizeKb / 1024).ToString("F1") + " MB"
                    : Math.Max(1, (int)Math.Round(sizeKb)).ToString() + " KB";
                fileText.Text = $"📎 {pending.Meta.Name}（{sizeText}）";
                fileText.Foreground = TextMain;
            }
        }
        UpdateFileText();
        root.Children.Add(fileText);

        var fileBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        Button MkBtn(string text)
        {
            return new Button
            {
                Content = text, Height = 32, Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 8, 0), Foreground = TextMain, Background = Chip,
                BorderBrush = Border, BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }
        var pickBtn = MkBtn("📎 选择文件…");
        var clearBtn = MkBtn("✖ 移除附件");
        fileBtns.Children.Add(pickBtn);
        fileBtns.Children.Add(clearBtn);
        root.Children.Add(fileBtns);

        pickBtn.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Title = "选择附件", Filter = "所有文件|*.*" };
            if (dlg.ShowDialog() != true) return;
            var fi = new FileInfo(dlg.FileName);
            pending.SourcePath = dlg.FileName;
            pending.ExistingUri = null;
            pending.Meta = new FileMeta
            {
                Name = fi.Name,
                Mime = GuessMime(fi.Extension),
                Size = fi.Length
            };
            UpdateFileText();
        };
        clearBtn.Click += (_, _) =>
        {
            pending.SourcePath = null;
            pending.ExistingUri = null;
            pending.Meta = new FileMeta();
            UpdateFileText();
        };

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
        if (string.IsNullOrWhiteSpace(titleBox.Text))
        {
            MessageBox.Show("收藏标题不能为空", "地球Online", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // ===== 落附件文件（复制到 FilesDir；失败不阻断保存，仅丢弃附件） =====
        string? oldUri = col.FileUri;
        if (pending.SourcePath is not null)
        {
            try
            {
                AppPaths.EnsureDirectories();
                var dest = Path.Combine(AppPaths.FilesDir,
                    Guid.NewGuid().ToString("N") + "_" + Path.GetFileName(pending.SourcePath));
                File.Copy(pending.SourcePath, dest, overwrite: true);
                col.FileUri = dest;
                col.FileMetaJson = JsonSerializer.Serialize(pending.Meta);
                // 换新附件成功后，删除旧文件（同拖拽入库语义：落库成功后清理）
                TryDeleteOld(oldUri, dest);
            }
            catch (Exception ex)
            {
                MessageBox.Show("附件复制失败（已保存其余内容）：" + ex.Message, "地球Online",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                col.FileUri = pending.ExistingUri ?? col.FileUri;
            }
        }
        else if (pending.ExistingUri is null && !isNew)
        {
            // 用户点了「移除附件」
            col.FileUri = null;
            col.FileMetaJson = null;
            TryDeleteOld(oldUri, null);
        }

        col.Title = titleBox.Text.Trim();
        col.Category = ((catBox.SelectedItem as ComboBoxItem)?.Tag as string) is { Length: > 0 } cid ? cid : null;
        col.Note = string.IsNullOrWhiteSpace(noteBox.Text) ? null : noteBox.Text.Trim();
        return true;
    }

    /// <summary>打开收藏附件（系统默认程序）。</summary>
    public static void OpenFile(string? uri)
    {
        if (string.IsNullOrEmpty(uri) || !File.Exists(uri))
        {
            MessageBox.Show("附件文件不存在（可能已被移动或删除）", "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("打开附件失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void TryDeleteOld(string? oldUri, string? keepUri)
    {
        try
        {
            if (string.IsNullOrEmpty(oldUri) || oldUri == keepUri) return;
            if (!oldUri.StartsWith(AppPaths.FilesDir, StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(oldUri)) File.Delete(oldUri);
        }
        catch { /* 旧文件清理失败不影响保存 */ }
    }

    private static string GuessMime(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" => "image/" + ext.TrimStart('.').Replace("jpg", "jpeg"),
        ".mp4" or ".mkv" or ".webm" or ".avi" => "video/" + ext.TrimStart('.'),
        ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" => "audio/" + ext.TrimStart('.'),
        ".pdf" => "application/pdf",
        ".txt" or ".md" => "text/plain",
        ".zip" => "application/zip",
        ".rar" => "application/x-rar-compressed",
        ".7z" => "application/x-7z-compressed",
        ".doc" or ".docx" => "application/msword",
        ".xls" or ".xlsx" => "application/vnd.ms-excel",
        _ => "application/octet-stream"
    };
}
