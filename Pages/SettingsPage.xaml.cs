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
        Loaded += (_, _) => { LoadProfile(); LoadConfig(); };
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
        AchievementEngine.Evaluate();   // 生日影响「成长」类成就
        LoadProfile();
    }

    // ==================== 本地备份 ====================

    private void ExportLocal_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "导出备份",
            FileName = $"earth_online_backup_{DateTime.Now:yyyyMMdd_HHmm}.json",
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
            AchievementEngine.Evaluate();
            LoadProfile();
            MessageBox.Show($"已导入 {n} 条数据（按主键合并，未清空原有内容）。",
                "地球Online", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("导入失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
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
        AchievementEngine.Evaluate();
        MessageBox.Show(r.Message, "地球Online",
            MessageBoxButton.OK, r.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }
}
