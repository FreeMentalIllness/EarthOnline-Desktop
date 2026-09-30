using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Data.Models;
using EarthOnline.Desktop.Services;

namespace EarthOnline.Desktop.Dialogs;

/// <summary>
/// 完善资料对话框：昵称 / 生日 / 性别 / 国家 / 省份 / 个性签名 / 头像 emoji / 自定义字段。
/// 视觉与 SimpleDialogs 一致（暖白底 + 琥珀主色）。
/// 确定返回 true，结果写入传入的 profile 实例（调用方自行落库）。
/// </summary>
public static class ProfileDialog
{
    private static System.Windows.Media.Brush Bg => ThemeService.Brush("AppBgBrush");
    private static System.Windows.Media.Brush Card => ThemeService.Brush("CardBgBrush");
    private static System.Windows.Media.Brush Border => ThemeService.Brush("BorderBrush");
    private static System.Windows.Media.Brush Accent => ThemeService.Brush("AccentBrush");
    private static System.Windows.Media.Brush TextMain => ThemeService.Brush("TextPrimaryBrush");
    private static System.Windows.Media.Brush TextSub => ThemeService.Brush("TextSecondaryBrush");
    private static System.Windows.Media.Brush Chip => ThemeService.Brush("ChipFillBrush");

    private static readonly string[] AvatarEmojiOrder = { "🌍", "🚀", "🎮", "🐱", "🍃", "🎵", "⭐" };

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

    public static bool Show(ProfileEntity profile)
    {
        var win = new ThemeDialogWindow("完善资料", 480, 660);
        var okButton = new Button();

        var root = new StackPanel { Margin = new Thickness(20) };

        // ===== 头像 emoji 选择 =====
        root.Children.Add(new TextBlock
        {
            Text = "头像（emoji 预设）", FontSize = 13, Foreground = TextSub, Margin = new Thickness(0, 0, 0, 6)
        });
        var avatarPanel = new WrapPanel();
        string chosenAvatar = profile.AvatarKey ?? "";
        var avatarButtons = new Dictionary<string, Border>();
        foreach (var key in AvatarEmojiOrder)
        {
            var b = new Border
            {
                Width = 46, Height = 46, CornerRadius = new CornerRadius(23),
                Background = Chip, BorderBrush = Border, BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 8), Cursor = System.Windows.Input.Cursors.Hand,
                Child = new TextBlock
                {
                    Text = key, FontSize = 22,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            b.MouseLeftButtonUp += (_, _) =>
            {
                chosenAvatar = KeyOfEmoji(key);
                foreach (var kv in avatarButtons)
                {
                    kv.Value.BorderBrush = Border;
                    kv.Value.BorderThickness = new Thickness(1);
                }
                b.BorderBrush = Accent;
                b.BorderThickness = new Thickness(2);
            };
            if (KeyOfEmoji(key) == chosenAvatar) { b.BorderBrush = Accent; b.BorderThickness = new Thickness(2); }
            avatarButtons[key] = b;
            avatarPanel.Children.Add(b);
        }
        root.Children.Add(avatarPanel);

        // ===== 自定义头像图片（可选；原图完整复制不重编码 —— 对齐安卓端规则） =====
        root.Children.Add(new TextBlock
        {
            Text = "自定义头像图片（优先于 emoji 显示）", FontSize = 13, Foreground = TextSub,
            Margin = new Thickness(0, 14, 0, 6)
        });
        string pendingAvatarPath = profile.AvatarPath ?? "";
        // 记住打开对话框时的原值：取消 / 校验失败时要能撤销「已复制但没落库」的新头像
        string originalAvatarPath = pendingAvatarPath;
        void DiscardPendingAvatar() => AvatarService.Discard(pendingAvatarPath, originalAvatarPath);
        var imgRow = new StackPanel { Orientation = Orientation.Horizontal };
        var previewBorder = new Border
        {
            Width = 46, Height = 46, CornerRadius = new CornerRadius(23),
            Background = Chip, BorderBrush = Border, BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };
        var previewImage = new Image { Width = 44, Height = 44, Stretch = Stretch.UniformToFill };
        previewImage.Clip = new EllipseGeometry(new Point(22, 22), 22, 22);
        previewBorder.Child = previewImage;
        var imgNameText = new TextBlock
        {
            FontSize = 12, Foreground = TextSub, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0), MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis
        };
        void LoadPreview(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;   // 读完即释放文件句柄，避免锁定
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                previewImage.Source = bmp;
            }
            catch { previewImage.Source = null; }
        }
        if (File.Exists(pendingAvatarPath))
        {
            LoadPreview(pendingAvatarPath);
            imgNameText.Text = Path.GetFileName(pendingAvatarPath);
        }
        else
        {
            imgNameText.Text = "（未设置，使用 emoji）";
        }
        var pickImgBtn = MkBtn("选择图片…");
        pickImgBtn.Click += (_, _) =>
        {
            // 连续选图时先把上一张「已选但未提交」的临时图删掉，避免留下孤儿文件
            DiscardPendingAvatar();
            pendingAvatarPath = originalAvatarPath;

            // 选图 + 原图完整复制进私有目录（保留扩展名、不重编码）
            var dest = AvatarService.PickAndCopy();
            if (dest is null) return;

            // v1.0.3：原画质自定义裁剪（圆形取景，输出 PNG 无损落 avatar 目录）。
            // 用户取消裁剪则回退为使用未裁剪的原图。
            if (CropDialog.Show(dest, out var cropped, circular: true, AppPaths.AvatarDir) && cropped is not null)
            {
                AvatarService.Discard(dest, originalAvatarPath); // 删掉未裁剪的原图副本，避免孤儿文件
                pendingAvatarPath = cropped;
                LoadPreview(cropped);
                imgNameText.Text = Path.GetFileName(cropped);
            }
            else
            {
                pendingAvatarPath = dest;
                LoadPreview(dest);
                imgNameText.Text = Path.GetFileName(dest);
            }
        };
        var clearImgBtn = MkBtn("移除图片");
        clearImgBtn.Click += (_, _) =>
        {
            pendingAvatarPath = "";
            previewImage.Source = null;
            imgNameText.Text = "（未设置，使用 emoji）";
        };
        imgRow.Children.Add(previewBorder);
        imgRow.Children.Add(imgNameText);
        imgRow.Children.Add(pickImgBtn);
        imgRow.Children.Add(clearImgBtn);
        root.Children.Add(imgRow);

        // ===== 基础字段 =====
        var nameBox = Field(root, "昵称", profile.Name);
        // v1.0.3：称号 —— 不动 profile 表，存 settings.json 的 customTitle 键（零 DB 变更）
        var titleBox = Field(root, "称号（留空显示默认「旅行者」，最长 12 字）",
            SettingsStore.Load().CustomTitle);
        var birthBox = Field(root, "生日（YYYY-MM-DD，留空=未设置）", profile.BirthDate);
        var countryBox = Field(root, "国家 / 区服", profile.Country);
        var provinceBox = Field(root, "省份 / 地区", profile.Province);
        var signatureBox = Field(root, "个性签名", profile.Signature);

        // ===== 性别下拉 =====
        root.Children.Add(new TextBlock
        {
            Text = "性别", FontSize = 13, Foreground = TextSub, Margin = new Thickness(0, 10, 0, 4)
        });
        var genderBox = new ComboBox { FontSize = 13, Padding = new Thickness(8, 5, 8, 5) };
        int genderIndex = 0;
        for (int i = 0; i < ProfileService.GenderOptions.Length; i++)
        {
            var (key, label) = ProfileService.GenderOptions[i];
            genderBox.Items.Add(new ComboBoxItem { Content = label, Tag = key });
            if (key == profile.Gender) genderIndex = i;
        }
        genderBox.SelectedIndex = genderIndex;
        root.Children.Add(genderBox);

        // ===== 自定义字段 =====
        root.Children.Add(new TextBlock
        {
            Text = "自定义字段（标签 + 值，可增删）", FontSize = 13, Foreground = TextSub,
            Margin = new Thickness(0, 14, 0, 4)
        });
        var fields = ProfileService.ReadCustomFields(profile);
        var fieldList = new ListBox
        {
            Height = 110, Background = Card, BorderBrush = Border, BorderThickness = new Thickness(1),
            FontSize = 13
        };
        FillFieldList(fieldList, fields);
        root.Children.Add(fieldList);

        var fieldBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        Button MkBtn(string text)
        {
            return new Button
            {
                Content = text, MinWidth = 72, Height = 30, Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 8, 0),
            };
        }
        var addField = MkBtn("＋ 添加");
        var editField = MkBtn("✏️ 修改");
        var delField = MkBtn("🗑 删除");
        // 自定义字段操作按钮统一主题化
        addField.Style = (Style)Application.Current.Resources["SoftButtonStyle"];
        editField.Style = (Style)Application.Current.Resources["SoftButtonStyle"];
        delField.Style = (Style)Application.Current.Resources["SoftButtonStyle"];
        addField.Click += (_, _) =>
        {
            var label = SimpleDialogs.Prompt("添加自定义字段", "标签（例如：职业）");
            if (string.IsNullOrWhiteSpace(label)) return;
            var value = SimpleDialogs.Prompt("添加自定义字段", "值（例如：冒险者）") ?? "";
            fields.Add(new CustomField
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = label.Trim(), Value = value.Trim()
            });
            FillFieldList(fieldList, fields);
        };
        editField.Click += (_, _) =>
        {
            int idx = fieldList.SelectedIndex;
            if (idx < 0 || idx >= fields.Count) return;
            var f = fields[idx];
            var label = SimpleDialogs.Prompt("修改自定义字段", "标签", f.Label);
            if (label is null) return;
            var value = SimpleDialogs.Prompt("修改自定义字段", "值", f.Value) ?? "";
            f.Label = label.Trim();
            f.Value = value.Trim();
            FillFieldList(fieldList, fields);
        };
        delField.Click += (_, _) =>
        {
            int idx = fieldList.SelectedIndex;
            if (idx < 0 || idx >= fields.Count) return;
            fields.RemoveAt(idx);
            FillFieldList(fieldList, fields);
        };
        fieldBtns.Children.Add(addField);
        fieldBtns.Children.Add(editField);
        fieldBtns.Children.Add(delField);
        root.Children.Add(fieldBtns);

        // ===== 确定按钮 =====
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        // 主体按钮统一走全局样式（暖色圆角，与主页/设置页完全一致）
        var cancelButton = new Button { Content = "取消", MinWidth = 84, Height = 34 };
        cancelButton.Style = (Style)Application.Current.Resources["SoftButtonStyle"];
        okButton = new Button
        {
            Content = "保存", MinWidth = 84, Height = 34, Padding = new Thickness(14, 0, 14, 0),
            Margin = new Thickness(6, 0, 0, 0),
        };
        okButton.Style = (Style)Application.Current.Resources["PrimaryButtonStyle"];
        cancelButton.Click += (_, _) => win.DialogResult = false;
        okButton.Click += (_, _) => win.DialogResult = true;
        btnRow.Children.Add(cancelButton);
        btnRow.Children.Add(okButton);
        root.Children.Add(btnRow);

        win.SetBody(new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        if (win.ShowDialog() != true)
        {
            DiscardPendingAvatar();     // 取消：删掉已复制但没落库的头像
            return false;
        }

        // ===== 校验 + 回写 =====
        var birth = birthBox.Text.Trim();
        if (birth.Length > 0 && !DateTime.TryParse(birth, out _))
        {
            DiscardPendingAvatar();     // 校验失败同样算放弃本次编辑
            MessageBox.Show("生日格式不对，请用 YYYY-MM-DD（例如 1995-08-20）", "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        profile.Name = nameBox.Text.Trim();
        profile.BirthDate = birth;
        profile.Country = countryBox.Text.Trim();
        profile.Province = provinceBox.Text.Trim();
        profile.Signature = signatureBox.Text.Trim();
        profile.Gender = (genderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        profile.AvatarKey = chosenAvatar;
        AvatarService.Apply(profile, pendingAvatarPath);
        profile.CustomFieldsJson = ProfileService.WriteCustomFields(fields);

        // v1.0.3：称号落 settings.json（写失败不拦住资料保存）
        try
        {
            var s = SettingsStore.Load();
            s.CustomTitle = titleBox.Text.Trim();
            s.Save();
        }
        catch { /* 忽略 */ }
        return true;
    }

    private static string KeyOfEmoji(string emoji)
    {
        foreach (var (key, e) in ProfileService.AvatarPresets)
        {
            if (e == emoji) return key;
        }
        return "";
    }

    private static void FillFieldList(ListBox list, List<CustomField> fields)
    {
        int keep = list.SelectedIndex;
        list.Items.Clear();
        foreach (var f in fields)
        {
            list.Items.Add(string.IsNullOrWhiteSpace(f.Value) ? f.Label : $"{f.Label}：{f.Value}");
        }
        list.SelectedIndex = keep >= 0 && keep < list.Items.Count ? keep : -1;
    }
}
