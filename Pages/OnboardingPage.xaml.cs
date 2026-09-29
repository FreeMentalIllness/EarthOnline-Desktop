using System.IO;
using System.Windows;
using System.Windows.Controls;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace EarthOnline.Desktop.Pages;

/// <summary>
/// 首次启动引导（4 步：欢迎建角色 → 功能简介 → WebDAV 同步 → 数据导入导出）。
/// 对齐安卓 OnboardingScreen 的「先建角色再开始」思路，并把桌面端特有的
/// WebDAV / 备份导入导出前置到引导里（网页端没有独立引导页，这两项原本要在设置页翻）。
/// 完成后写 SettingsStore.Onboarded = true，之后不再自动弹出；设置页可重新唤起。
/// </summary>
public partial class OnboardingPage : Page
{
    private int _step = 1;
    private const int LastStep = 4;

    /// <summary>引导结束后的回调（由主窗口注入，用于跳回主页）。</summary>
    public Action? OnFinished { get; set; }

    public OnboardingPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            var p = db.Profile.AsNoTracking().FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(p?.Name)) NameBox.Text = p!.Name;
            if (!string.IsNullOrWhiteSpace(p?.BirthDate)) BirthBox.Text = p!.BirthDate;

            var s = SettingsStore.Load();
            UrlBox.Text = s.Url;
            UserBox.Text = s.User;
            PassBox.Password = s.Pass;
            ShowStep(1);
        };
    }

    // ==================== 步骤切换 ====================

    private void ShowStep(int step)
    {
        _step = step;
        Step1.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;

        DimAll();
        // 取主题画刷而非硬编码颜色，深色主题下步骤指示同样可读
        (step switch { 2 => Dot2, 3 => Dot3, 4 => Dot4, _ => Dot1 }).Foreground = Brush("TextPrimaryBrush");

        PrevBtn.Visibility = step == 1 ? Visibility.Collapsed : Visibility.Visible;
        NextBtn.Content = step == LastStep ? "🚀 开始使用" : "下一步";
        SkipBtn.Visibility = step == LastStep ? Visibility.Collapsed : Visibility.Visible;
    }

    private void DimAll()
    {
        var dim = Brush("TextMutedBrush");
        Dot1.Foreground = dim; Dot2.Foreground = dim;
        Dot3.Foreground = dim; Dot4.Foreground = dim;
    }

    /// <summary>按 key 取主题画刷（取不到时回落到灰，绝不因资源缺失崩页面）。</summary>
    private static System.Windows.Media.Brush Brush(string key)
    {
        try
        {
            if (Application.Current?.TryFindResource(key) is System.Windows.Media.Brush b) return b;
        }
        catch { /* 设计时 / 单测场景可能无 Application */ }
        return System.Windows.Media.Brushes.Gray;
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_step > 1) ShowStep(_step - 1);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 1 && !SaveProfile()) return;   // 生日格式不对就停在当前步
        if (_step == 3) SaveWebDav();

        if (_step < LastStep) { ShowStep(_step + 1); return; }

        Finish();
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => Finish();

    /// <summary>写「已完成引导」标记并回主页。</summary>
    private void Finish()
    {
        try
        {
            var s = SettingsStore.Load();
            s.Onboarded = true;
            s.Save();
        }
        catch { /* 标记写失败最多是下次再看一次引导，不影响使用 */ }

        OnFinished?.Invoke();
    }

    // ==================== 步骤 1：角色 ====================

    private bool SaveProfile()
    {
        var birth = BirthBox.Text.Trim();
        if (birth.Length > 0 && !DateTime.TryParse(birth, out _))
        {
            MessageBox.Show("生日格式不对，请用 YYYY-MM-DD（例如 1995-08-20）", "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            var row = db.Profile.Find(1);
            if (row is null)
            {
                row = new ProfileEntity { Id = 1 };
                db.Profile.Add(row);
            }
            row.Name = NameBox.Text.Trim();
            row.BirthDate = birth;
            db.SaveChanges();
            AchievementNotifier.Check();   // 生日影响「成长」类成就
        }
        catch (Exception ex)
        {
            MessageBox.Show("保存角色失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return true;
    }

    // ==================== 步骤 3：WebDAV ====================

    private void SaveWebDav()
    {
        try
        {
            var s = SettingsStore.Load();
            s.Url = UrlBox.Text.Trim();
            s.User = UserBox.Text.Trim();
            s.Pass = PassBox.Password;
            s.AutoSync = true;
            s.Save();
        }
        catch { /* 配置失败可之后在设置页补 */ }
    }

    private async void TestConn_Click(object sender, RoutedEventArgs e)
    {
        SaveWebDav();
        SyncTipText.Text = "正在测试连接…";
        var r = await SyncService.TestAsync();
        SyncTipText.Text = r.Message;
    }

    // ==================== 步骤 4：数据导入导出 ====================

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "选择备份文件", Filter = "JSON 文件|*.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            int n = BackupService.ImportJson(File.ReadAllText(dlg.FileName));
            AchievementNotifier.Check();
            DataTipText.Text = $"已导入 {n} 条数据（按主键合并，未清空原有内容）。";
            // 跨端灵感接力：导入落库后检测来自手机的新灵感并温和提示
            IdeaRelayService.CheckAfterImport();
        }
        catch (Exception ex)
        {
            MessageBox.Show("导入失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "导出备份",
            FileName = $"earth-online-backup-{DateTime.Now:yyyy-MM-dd-HHmmss}.json",
            Filter = "JSON 文件|*.json"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, BackupService.ExportJson());
            DataTipText.Text = "已导出到：" + dlg.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show("导出失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Pull_Click(object sender, RoutedEventArgs e)
    {
        SaveWebDav();
        DataTipText.Text = "正在从云端拉取…";
        var r = await SyncService.PullIfRemoteNewerAsync(force: true);
        DataTipText.Text = r.Message;
        AchievementNotifier.Check();
    }
}
