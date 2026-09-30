using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Dialogs;
using EarthOnline.Desktop.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace EarthOnline.Desktop.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        DbPathText.Text = AppPaths.DbFile;
        Loaded += (_, _) =>
        {
            LoadProfile(); LoadConfig(); LoadGeneral(); LoadAppearance(); LoadBackups(); LoadAmap(); LoadDataDir();
        };
    }

    // ==================== 外观（主题 / 字号 / 壁纸） ====================

    private void LoadAppearance()
    {
        var s = SettingsStore.Load();
        _suppressGeneral = true;
        try
        {
            bool dark = ThemeService.IsDark(s);
            ThemeDark.IsChecked = dark;
            ThemeLight.IsChecked = !dark;

            double scale = Math.Clamp(s.FontScale <= 0 ? 1.0 : s.FontScale, 0.8, 1.4);
            (scale switch
            {
                <= 0.95 => FontSmall,
                >= 1.1 => FontBig,
                _ => FontStd
            }).IsChecked = true;

            WallpaperText.Text = string.IsNullOrEmpty(s.WallpaperPath)
                ? "未设置（使用纯色背景）"
                : Path.GetFileName(s.WallpaperPath);

            WallpaperOpacitySlider.Value = Math.Clamp(s.WallpaperOpacity <= 0 ? 1.0 : s.WallpaperOpacity, 0.1, 1.0);
            UpdateWallpaperOpacityText();
        }
        finally { _suppressGeneral = false; }
    }

    /// <summary>壁纸透明度滑杆：即时预览并保存（深色模式下 ThemeService 自动 ×0.4）。</summary>
    private void WallpaperOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (WallpaperOpacityText is null) return; // 初始化期防御
        UpdateWallpaperOpacityText();
        if (_suppressGeneral) return;
        try
        {
            var s = SettingsStore.Load();
            s.WallpaperOpacity = Math.Clamp(e.NewValue, 0.1, 1.0);
            s.Save();
            ThemeService.ApplyWallpaper(s);
        }
        catch { /* 滑杆失败不影响使用 */ }
    }

    private void UpdateWallpaperOpacityText()
        => WallpaperOpacityText.Text = $"{Math.Round(WallpaperOpacitySlider.Value * 100)}%";

    // ==================== 设置搜索（按关键词过滤卡片） ====================

    private void SettingsSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SettingsRoot is null || SettingsSearchBox is null) return; // 初始化期防御
        string q = (SettingsSearchBox.Text ?? "").Trim();
        SearchClearBtn.Visibility = q.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var child in SettingsRoot.Children)
        {
            // 只过滤卡片；标题行（PageTitle/SubTitle/Section 标题）保持原位
            if (child is not Border card) continue;
            if (q.Length == 0) { card.Visibility = Visibility.Visible; continue; }
            string text = CollectText(card);
            card.Visibility = text.Contains(q, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void SearchClear_Click(object sender, RoutedEventArgs e)
    {
        SettingsSearchBox.Text = "";
        SettingsSearch_TextChanged(sender, null!);
        SettingsSearchBox.Focus();
    }

    /// <summary>收集卡片内全部 TextBlock 文本（含按钮 Content）作为搜索语料。</summary>
    private static string CollectText(DependencyObject root)
    {
        var sb = new System.Text.StringBuilder();
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is TextBlock tb) sb.Append(tb.Text).Append(' ');
            if (c is Button b && b.Content is string s) sb.Append(s).Append(' ');
            if (c is System.Windows.Controls.Primitives.ToggleButton tg && tg.Content is string ts) sb.Append(ts).Append(' ');
            sb.Append(CollectText(c));
        }
        return sb.ToString();
    }

    /// <summary>主题 / 字号切换（RadioButton Checked 共用；初始化期由 _suppressGeneral 挡住）。</summary>
    private void Appearance_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressGeneral) return;
        if (ThemeLight is null || ThemeDark is null || FontSmall is null) return; // 初始化期控件未就绪

        try
        {
            var s = SettingsStore.Load();
            s.Theme = ThemeDark.IsChecked == true ? "dark" : "light";
            s.FontScale = double.Parse(
                (FontSmall.IsChecked == true ? FontSmall : FontBig.IsChecked == true ? FontBig : FontStd).Tag?.ToString() ?? "1.0",
                System.Globalization.CultureInfo.InvariantCulture);
            s.Save();

            ThemeService.ApplyTheme(s);
            ThemeService.ApplyFontScale(s.FontScale);

            // 主题换了要重建内容区：指标卡 / 日历热图 / 图表等是代码生成的，
            // 颜色只在生成时取一次，不重建就会留着上一套主题的配色。
            if (e.Source is RadioButton rb && (rb == ThemeLight || rb == ThemeDark))
                (Application.Current.MainWindow as MainWindow)?.RefreshCurrentPage();
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("应用外观设置失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PickWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择壁纸图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            // v1.0.3：原画质自定义裁剪（方形取景），输出 PNG 无损落数据根目录
            if (!CropDialog.Show(dlg.FileName, out var cropped, circular: false, AppPaths.RootDir) || cropped is null)
                return;

            var s = SettingsStore.Load();
            s.WallpaperPath = cropped;
            s.Save();
            ThemeService.ApplyWallpaper(s);
            WallpaperText.Text = Path.GetFileName(cropped);
            (Application.Current.MainWindow as MainWindow)?.RefreshCurrentPage();
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("设置壁纸失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearWallpaper_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var s = SettingsStore.Load();
            // 删除旧的裁剪壁纸文件（仅限数据根目录内的 crop_ 文件，避免误删用户原图）
            if (!string.IsNullOrEmpty(s.WallpaperPath) &&
                s.WallpaperPath.StartsWith(AppPaths.RootDir, StringComparison.OrdinalIgnoreCase))
            {
                try { if (File.Exists(s.WallpaperPath)) File.Delete(s.WallpaperPath); } catch { /* 忽略 */ }
            }
            s.WallpaperPath = "";
            s.Save();
            ThemeService.ApplyWallpaper(s);
            WallpaperText.Text = "未设置（使用纯色背景）";
            (Application.Current.MainWindow as MainWindow)?.RefreshCurrentPage();
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("清除壁纸失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenRepo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/FreeMentalIllness/EarthOnline-Desktop",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("打开浏览器失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenIssues_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/FreeMentalIllness/EarthOnline-Desktop/issues",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("打开浏览器失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppPaths.RootDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("打开文件夹失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenChangelog_Click(object sender, RoutedEventArgs e)
    {
        Dialogs.ChangelogDialog.Show();
    }

    /// <summary>赞助弹窗：收款码 + 说明 + 赞助者名单（不再跳转仓库）。</summary>
    private void OpenSponsor_Click(object sender, RoutedEventArgs e)
    {
        Dialogs.SponsorDialog.Show();
    }

    // ==================== 通用（自启 / 更新） ====================

    private void LoadGeneral()
    {
        UpdateStatusText.Text = $"当前版本 v{UpdateService.CurrentVersion}";
        AboutVersionText.Text = $"版本 v{UpdateService.CurrentVersion} · 数据目录：{AppPaths.RootDir}";
        AboutTechText.Text = $"技术栈：C# / .NET {Environment.Version} · WPF · SQLite（本地）　数据目录：{AppPaths.RootDir}";
        // 赞助者：与 Web / Android 三端同一名单、同一顺序（勿加「首席 / 不分先后」等修饰词）
        AboutSponsorsText.Text = "❤️ 赞助者：海神唐三 · Seastar · 清浅";
        // 初始化期间会触发 Checked/Unchecked，先挂再设值的顺序由 _suppressGeneral 保证
        _suppressGeneral = true;
        try { AutoStartBox.IsChecked = AutoStartService.IsEnabled(); }
        finally { _suppressGeneral = false; }
    }

    private bool _suppressGeneral;

    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressGeneral) return; // 初始化赋值时不写注册表
        try
        {
            AutoStartService.SetEnabled(AutoStartBox.IsChecked == true);
            // 状态文案整体重写（此前是在旧文本前拼接，来回切换会把句子越叠越长）
            UpdateStatusText.Text = AutoStartBox.IsChecked == true
                ? $"已开启开机自启（当前版本 v{UpdateService.CurrentVersion}）。"
                : $"已关闭开机自启（当前版本 v{UpdateService.CurrentVersion}）。";
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("设置开机自启失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ==================== 退出行为（v1.0.3） ====================

    private void ExitBehavior_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressGeneral) return;
        try
        {
            var s = SettingsStore.Load();
            s.ExitBehavior = ExitDirect.IsChecked == true ? "exit" : "minimize";
            s.Save();
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("保存退出行为失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ==================== 数据存放位置（v1.0.3） ====================

    private void LoadDataDir()
    {
        try
        {
            var s = SettingsStore.Load();
            // 输入框只存「用户显式指定的目录」，留空 = 用默认目录。
            // 此前这里填的是当前生效目录，用户没改目录只是点了一下保存，
            // 就会把默认路径固化成自定义目录（重启后含义完全不同）。
            DataDirBox.Text = s.DataDirectory ?? "";
            DataDirStatus.Text = string.IsNullOrWhiteSpace(s.DataDirectory)
                ? $"当前默认目录：{AppPaths.RootDir}。填入自定义目录并保存后重启生效。"
                : $"已指定自定义目录：{s.DataDirectory}（重启后生效）。当前生效：{AppPaths.RootDir}";
            _suppressGeneral = true;
            try
            {
                bool exit = string.Equals(s.ExitBehavior, "exit", StringComparison.OrdinalIgnoreCase);
                ExitDirect.IsChecked = exit;
                ExitMinimize.IsChecked = !exit;
            }
            finally { _suppressGeneral = false; }
        }
        catch (Exception ex)
        {
            DataDirStatus.Text = "读取数据目录失败：" + ex.Message;
        }
    }

    private void BrowseDataDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // WPF 无原生文件夹选择器：用 OpenFileDialog 选文件夹的通用技巧（ValidateNames=false）
            var dlg = new OpenFileDialog
            {
                Title = "选择数据存放目录",
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "选择此文件夹即代表该目录",
                Filter = "文件夹|*.folder"
            };
            if (dlg.ShowDialog() == true)
            {
                var dir = Path.GetDirectoryName(dlg.FileName);
                if (!string.IsNullOrEmpty(dir)) DataDirBox.Text = dir;
            }
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("选择目录失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetDataDir_Click(object sender, RoutedEventArgs e)
    {
        DataDirBox.Text = "";
        DataDirStatus.Text = $"已清空：将回到默认目录 {Path.Combine(AppContext.BaseDirectory, "EarthOnlineData")}；点「保存目录」后重启生效。";
    }

    private void ApplyDataDir_Click(object sender, RoutedEventArgs e)
    {
        var target = (DataDirBox.Text ?? "").Trim();

        // 保存前预检：目标目录必须真实可写（防系统受保护目录导致重启后数据落错地方）
        if (!string.IsNullOrWhiteSpace(target))
        {
            string probe = Path.Combine(target, "earth_online_write_probe.tmp");
            try
            {
                Directory.CreateDirectory(target);
                File.WriteAllText(probe, "probe");
                File.Delete(probe);
            }
            catch (UnauthorizedAccessException)
            {
                SimpleDialogs.Alert(
                    "该目录没有写入权限（常见于 Program Files、Windows 等系统受保护目录）。\n\n" +
                    "请换一个可写的普通文件夹，例如：\n" +
                    "  · D:\\EarthOnlineData\n" +
                    "  · 或直接留空使用默认目录（应用旁 EarthOnlineData）。",
                    "数据目录不可写");
                return;
            }
            catch (Exception ex)
            {
                SimpleDialogs.Alert(
                    "无法使用该目录：" + ex.Message + "\n\n" +
                    "请确认路径存在且可写（网络盘/U 盘可能未就绪），或直接留空使用默认目录。",
                    "数据目录无效");
                return;
            }
        }

        try
        {
            var s = SettingsStore.Load();
            s.DataDirectory = target;
            s.Save();
            DataDirStatus.Text = string.IsNullOrWhiteSpace(s.DataDirectory)
                ? "已保存：使用默认目录。" + "重启应用后生效。"
                : "已保存自定义目录：" + s.DataDirectory + "。重启应用后生效（旧目录数据会在下次启动时自动迁移）。";
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("保存数据目录失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        UpdateStatusText.Text = $"当前版本 v{UpdateService.CurrentVersion}，正在检查更新…";
        var r = await UpdateService.CheckAsync();
        UpdateStatusText.Text = r.Message;

        if (!r.Ok)
        {
            // 网络异常 / 解析失败：弹窗提示，绝不闪退
            SimpleDialogs.Alert(r.Message, "地球Online · 检查更新",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool isNew = r.LatestVersion is not null
            && r.Message.StartsWith("发现新版本");

        if (isNew && r.ExeUrl is not null)
        {
            // 发现新版且 Release 带有 exe 资产 → 应用内下载 + bat 自更新
            var sizeText = r.ExeSize > 0 ? $"（约 {r.ExeSize / 1024 / 1024} MB）" : "";
            if (SimpleDialogs.Alert(
                    r.Message + $"\n\n是否立即下载并自动安装{sizeText}？\n下载完成后程序将自动退出、覆盖并重启。",
                    "地球Online · 更新",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                await UpdateService.DownloadAndInstallAsync(r.ExeUrl, r.ExeSize,
                    text => UpdateStatusText.Text = text);
            }
            else
            {
                UpdateStatusText.Text = $"已跳过更新（最新 v{r.LatestVersion}，可随时再次检查）。";
            }
        }
        else if (isNew && r.DownloadPage is not null)
        {
            // 新版存在但资产里没有 exe（异常发布）→ 退回打开下载页
            if (SimpleDialogs.Alert(r.Message + "\n\n现在打开 Releases 页面手动下载吗？", "地球Online",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                OpenReleases_Click(sender, e);
            }
        }
    }

    private void OpenReleases_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/FreeMentalIllness/EarthOnline-Desktop/releases",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("打开浏览器失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ==================== 资料 ====================

    private void LoadProfile()
    {
        using var db = new AppDbContext(AppPaths.DbFile);
        var p = db.Profile.FirstOrDefault();
        NameText.Text = string.IsNullOrWhiteSpace(p?.Name) ? "（未设置）" : p!.Name;
        BirthText.Text = string.IsNullOrWhiteSpace(p?.BirthDate) ? "（未设置）" : p!.BirthDate;
    }

    private void EditName_Click(object sender, RoutedEventArgs e)
    {
        using var db = new AppDbContext(AppPaths.DbFile);
        var p = db.Profile.FirstOrDefault();
        var input = SimpleDialogs.Prompt("修改昵称", "昵称", p?.Name ?? "");
        if (input is null) return;

        using var db2 = new AppDbContext(AppPaths.DbFile);
        var row = db2.Profile.Find(1) ?? new Data.Entities.ProfileEntity { Id = 1 };
        if (db2.Entry(row).State == EntityState.Detached) db2.Profile.Add(row);
        row.Name = input.Trim();
        db2.SaveChanges();
        LoadProfile();
    }

    private void EditProfileFull_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            var p = db.Profile.FirstOrDefault() ?? new Data.Entities.ProfileEntity { Id = 1 };
            var oldAvatarPath = p.AvatarPath;

            if (!ProfileDialog.Show(p)) return;

            using var db2 = new AppDbContext(AppPaths.DbFile);
            var row = db2.Profile.Find(1);
            if (row is null) { db2.Profile.Add(p); }
            else { db2.Entry(row).CurrentValues.SetValues(p); }
            db2.SaveChanges();

            // 落库成功后统一清理被替换的旧头像（唯一入口 AvatarService，避免孤儿文件）
            AvatarService.CleanupOrphan(oldAvatarPath, p.AvatarPath);

            AchievementNotifier.Check();
            LoadProfile();
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("保存资料失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EditBirth_Click(object sender, RoutedEventArgs e)
    {
        using var db = new AppDbContext(AppPaths.DbFile);
        var p = db.Profile.FirstOrDefault();
        var input = SimpleDialogs.Prompt("修改生日", "生日（格式 YYYY-MM-DD）", p?.BirthDate ?? "");
        if (input is null) return;

        var v = input.Trim();
        if (v.Length > 0 && !DateTime.TryParse(v, out _))
        {
            SimpleDialogs.Alert("日期格式不对，请用 YYYY-MM-DD（例如 1995-08-20）", "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        using var db2 = new AppDbContext(AppPaths.DbFile);
        var row = db2.Profile.Find(1) ?? new Data.Entities.ProfileEntity { Id = 1 };
        if (db2.Entry(row).State == EntityState.Detached) db2.Profile.Add(row);
        row.BirthDate = v;
        db2.SaveChanges();
        AchievementNotifier.Check();   // 生日影响「成长」类成就
        LoadProfile();
    }

    // ==================== 本地备份 ====================

    private void ExportLocal_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "导出备份",
            // 与网页端导出文件名前缀完全一致（earth-online-backup-），便于人眼区分三端备份
            FileName = $"earth-online-backup-{DateTime.Now:yyyy-MM-dd-HHmmss}.json",
            Filter = "JSON 文件|*.json"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, BackupService.ExportJson());
            SimpleDialogs.Alert("已导出到：\n" + dlg.FileName, "地球Online", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("导出失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportLocal_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "选择备份文件", Filter = "JSON 文件|*.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            int n = BackupService.ImportJson(File.ReadAllText(dlg.FileName));
            AchievementNotifier.Check();
            LoadProfile();
            SimpleDialogs.Alert($"已导入 {n} 条数据（按主键合并，未清空原有内容）。",
                "地球Online", MessageBoxButton.OK, MessageBoxImage.Information);
            // 跨端灵感接力：导入落库后检测来自手机的新灵感并温和提示
            IdeaRelayService.CheckAfterImport();
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("导入失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ==================== 地图 Key（高德） ====================

    private void LoadAmap()
    {
        var cfg = AmapConfig.Load();
        // 界面只显示用户自填的那份（内置回退不回显，避免把回退 Key 暴露在输入框里）
        AmapKeyBox.Text = cfg.IsCustom ? cfg.Key : "";
        AmapSecBox.Text = cfg.IsCustom ? cfg.Sec : "";
        AmapStatusText.Text = cfg.IsCustom
            ? "已使用你自己的 Key（加密存于本机）。清空后回落到内置回退 Key。"
            : AmapConfig.HasDefault
                ? "当前使用内置回退 Key（来自本地可选文件 Assets/amap_default.json，不入库）。填入你自己的 Key 可覆盖。"
                : "未配置 Key，也没有内置回退：地图页会自动降级为列表视图（足迹增删改不受影响）。";
    }

    private void SaveAmapKey_Click(object sender, RoutedEventArgs e)
    {
        var key = AmapKeyBox.Text.Trim();
        if (key.Length == 0)
        {
            SimpleDialogs.Alert("要清空请点「清空（回落内置）」；保存需要填写 Key。", "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            AmapConfig.Save(key, AmapSecBox.Text.Trim());
            LoadAmap();
            SimpleDialogs.Alert("已保存（密钥经 Windows DPAPI 加密后存于本机）。重新打开地图页生效。",
                "地球Online", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("保存失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearAmapKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AmapConfig.Save("", "");
            LoadAmap();
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("清空失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>重新唤起首次引导（标记置回未完成并跳转）。</summary>
    private void RestartOnboarding_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var s = SettingsStore.Load();
            s.Onboarded = false;
            s.Save();
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("重置引导状态失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        (Application.Current.MainWindow as MainWindow)?.NavigateTo("onboarding");
    }

    // ==================== 自动备份（对齐安卓 AutoBackupManager） ====================

    private void LoadBackups()
    {
        _suppressGeneral = true;
        try
        {
            AutoBackupBox.IsChecked = SettingsStore.Load().AutoBackup;
        }
        finally { _suppressGeneral = false; }

        var list = AutoBackupService.ListSnapshots();
        BackupList.ItemsSource = list;
        BackupStatusText.Text = list.Count == 0
            ? "还没有自动备份快照。数据发生变更并静置 4 秒后会自动生成第一份。"
            : $"共 {list.Count} 份（最多保留 {AutoBackupService.Keep} 份）　目录：{AppPaths.BackupDir}";
    }

    private void AutoBackup_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressGeneral) return;   // 初始化赋值时不写文件
        try
        {
            var s = SettingsStore.Load();
            s.AutoBackup = AutoBackupBox.IsChecked == true;
            s.Save();
            AutoBackupService.RefreshEnabled();
            BackupStatusText.Text = s.AutoBackup
                ? "已开启自动备份：数据变更静置 4 秒后自动生成快照。"
                : "已关闭自动备份（不会影响已生成的快照，也不会关闭 WebDAV 同步）。";
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("设置自动备份失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        var path = AutoBackupService.Snapshot(force: true);
        if (path is null)
        {
            SimpleDialogs.Alert("没有生成快照：可能数据为空，或写入失败。", "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        LoadBackups();
        BackupStatusText.Text = "已生成快照：" + Path.GetFileName(path);
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedItem is not BackupSnapshot snap)
        {
            SimpleDialogs.Alert("请先在上方列表里选择一份快照。", "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!SimpleDialogs.Confirm(
                $"确定用 {snap.Label} 的快照恢复？\n按主键合并覆盖本地同 id 的记录，不会清空其他内容。"))
        {
            return;
        }

        try
        {
            int n = AutoBackupService.Restore(snap.Path);
            AchievementNotifier.Check();
            LoadProfile();
            LoadBackups();
            SimpleDialogs.Alert($"已从 {snap.Label} 恢复 {n} 条数据。",
                "地球Online", MessageBoxButton.OK, MessageBoxImage.Information);
            // 跨端灵感接力：快照恢复落库后同样检测来自手机的新灵感
            IdeaRelayService.CheckAfterImport();
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("恢复失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.BackupDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppPaths.BackupDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("打开文件夹失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ==================== WebDAV ====================

    private void LoadConfig()
    {
        var s = SettingsStore.Load();
        UrlBox.Text = s.Url;
        UserBox.Text = s.User;
        PassBox.Password = s.Pass;
        PathBox.Text = s.RemotePath;
        AutoSyncBox.IsChecked = s.AutoSync;
        SyncStatusText.Text = string.IsNullOrEmpty(s.LastSyncAt)
            ? "尚未同步过。远端默认文件：" + SyncService.DefaultRemotePath
            : "上次同步：" + s.LastSyncAt + "　远端文件：" + s.EffectiveRemotePath();
    }

    private void SaveConfig_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsStore.Load();
        s.Url = UrlBox.Text.Trim();
        s.User = UserBox.Text.Trim();
        s.Pass = PassBox.Password;
        s.RemotePath = PathBox.Text.Trim();
        s.AutoSync = AutoSyncBox.IsChecked == true;
        s.Save();
        SyncStatusText.Text = "配置已保存。远端文件：" + s.EffectiveRemotePath();
    }

    private async void TestConn_Click(object sender, RoutedEventArgs e)
    {
        SaveConfig_Click(sender, e);
        SyncStatusText.Text = "正在测试连接…";
        var r = await SyncService.TestAsync();
        SyncStatusText.Text = r.Message;
    }

    private async void Push_Click(object sender, RoutedEventArgs e)
    {
        SaveConfig_Click(sender, e);
        SyncStatusText.Text = "正在推送…";
        var r = await SyncService.PushAsync(force: true);
        SyncStatusText.Text = r.Message;
        LoadConfig();
        SimpleDialogs.Alert(r.Message, "地球Online",
            MessageBoxButton.OK, r.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async void Pull_Click(object sender, RoutedEventArgs e)
    {
        SaveConfig_Click(sender, e);
        if (!SimpleDialogs.Confirm("从云端恢复会用云端数据覆盖本地同 id 的记录，确定继续？")) return;

        SyncStatusText.Text = "正在拉取…";
        var r = await SyncService.PullIfRemoteNewerAsync(force: true);
        SyncStatusText.Text = r.Message;
        LoadConfig();
        LoadProfile();
        AchievementNotifier.Check();
        SimpleDialogs.Alert(r.Message, "地球Online",
            MessageBoxButton.OK, r.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }
}
