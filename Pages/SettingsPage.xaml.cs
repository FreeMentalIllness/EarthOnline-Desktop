using System.IO;
using System.Windows;
using System.Windows.Controls;
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
            LoadProfile(); LoadConfig(); LoadGeneral(); LoadAppearance(); LoadBackups();
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
        }
        finally { _suppressGeneral = false; }
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
        }
        catch (Exception ex)
        {
            MessageBox.Show("应用外观设置失败：" + ex.Message, "地球Online",
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
            var s = SettingsStore.Load();
            s.WallpaperPath = dlg.FileName;
            s.Save();
            ThemeService.ApplyWallpaper(s);
            WallpaperText.Text = Path.GetFileName(dlg.FileName);
            (Application.Current.MainWindow as MainWindow)?.RefreshCurrentPage();
        }
        catch (Exception ex)
        {
            MessageBox.Show("设置壁纸失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearWallpaper_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var s = SettingsStore.Load();
            s.WallpaperPath = "";
            s.Save();
            ThemeService.ApplyWallpaper(s);
            WallpaperText.Text = "未设置（使用纯色背景）";
            (Application.Current.MainWindow as MainWindow)?.RefreshCurrentPage();
        }
        catch (Exception ex)
        {
            MessageBox.Show("清除壁纸失败：" + ex.Message, "地球Online",
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
            MessageBox.Show("打开浏览器失败：" + ex.Message, "地球Online",
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
            MessageBox.Show("打开浏览器失败：" + ex.Message, "地球Online",
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
            MessageBox.Show("打开文件夹失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ==================== 通用（自启 / 更新） ====================

    private void LoadGeneral()
    {
        UpdateStatusText.Text = $"当前版本 v{UpdateService.CurrentVersion}";
        AboutVersionText.Text = $"版本 v{UpdateService.CurrentVersion} · 数据库与设置存于 %LOCALAPPDATA%\\EarthOnline";
        AboutTechText.Text = $"技术栈：C# / .NET {Environment.Version} · WPF · SQLite（本地）　数据目录：{AppPaths.RootDir}";
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
            UpdateStatusText.Text = AutoStartBox.IsChecked == true
                ? "已开启开机自启。" + UpdateStatusText.Text
                : "已关闭开机自启。" + UpdateStatusText.Text;
        }
        catch (Exception ex)
        {
            MessageBox.Show("设置开机自启失败：" + ex.Message, "地球Online",
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
            MessageBox.Show(r.Message, "地球Online · 检查更新",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool isNew = r.LatestVersion is not null
            && r.Message.StartsWith("发现新版本");

        if (isNew && r.ExeUrl is not null)
        {
            // 发现新版且 Release 带有 exe 资产 → 应用内下载 + bat 自更新
            var sizeText = r.ExeSize > 0 ? $"（约 {r.ExeSize / 1024 / 1024} MB）" : "";
            if (MessageBox.Show(
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
            if (MessageBox.Show(r.Message + "\n\n现在打开 Releases 页面手动下载吗？", "地球Online",
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
            MessageBox.Show("打开浏览器失败：" + ex.Message, "地球Online",
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

            // 落库成功后再清理被替换的旧头像文件（仅限本应用 avatar 目录内，对齐安卓规则）
            if (!string.IsNullOrEmpty(oldAvatarPath) &&
                !string.Equals(oldAvatarPath, p.AvatarPath, StringComparison.OrdinalIgnoreCase) &&
                oldAvatarPath.StartsWith(AppPaths.AvatarDir, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(oldAvatarPath))
            {
                try { File.Delete(oldAvatarPath); } catch { /* 清理失败不影响 */ }
            }

            AchievementNotifier.Check();
            LoadProfile();
        }
        catch (Exception ex)
        {
            MessageBox.Show("保存资料失败：" + ex.Message, "地球Online",
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
            MessageBox.Show("日期格式不对，请用 YYYY-MM-DD（例如 1995-08-20）", "地球Online",
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
            MessageBox.Show("已导出到：\n" + dlg.FileName, "地球Online", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("导出失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show($"已导入 {n} 条数据（按主键合并，未清空原有内容）。",
                "地球Online", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("导入失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
            MessageBox.Show("设置自动备份失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        var path = AutoBackupService.Snapshot(force: true);
        if (path is null)
        {
            MessageBox.Show("没有生成快照：可能数据为空，或写入失败。", "地球Online",
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
            MessageBox.Show("请先在上方列表里选择一份快照。", "地球Online",
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
            MessageBox.Show($"已从 {snap.Label} 恢复 {n} 条数据。",
                "地球Online", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("恢复失败：" + ex.Message, "地球Online",
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
            MessageBox.Show("打开文件夹失败：" + ex.Message, "地球Online",
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
        MessageBox.Show(r.Message, "地球Online",
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
        MessageBox.Show(r.Message, "地球Online",
            MessageBoxButton.OK, r.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }
}
