using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Dialogs;
using EarthOnline.Desktop.Services;
using EarthOnline.Desktop.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Pages;

public partial class HomePage : Page
{
    private readonly HomeViewModel _vm = new();

    public HomePage()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += (_, _) => _vm.Load();
    }

    /// <summary>快速入口按钮：根据 Tag 跳转到对应模块。</summary>
    private void Quick_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string key)
            (Application.Current.MainWindow as MainWindow)?.NavigateTo(key);
    }

    // ==================== 编辑资料 ====================

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            var p = db.Profile.FirstOrDefault() ?? new ProfileEntity { Id = 1 };
            bool isNew = db.Entry(p).State == EntityState.Detached;
            var oldAvatarPath = p.AvatarPath;

            if (!ProfileDialog.Show(p)) return;

            using var db2 = new AppDbContext(AppPaths.DbFile);
            var row = db2.Profile.Find(1);
            if (row is null) { db2.Profile.Add(p); }
            else { db2.Entry(row).CurrentValues.SetValues(p); }
            db2.SaveChanges();

            // 落库成功后统一清理被替换的旧头像（唯一入口 AvatarService，避免孤儿文件）
            AvatarService.CleanupOrphan(oldAvatarPath, p.AvatarPath);

            AchievementNotifier.Check();   // 性别彩蛋等依赖资料
            _vm.Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show("保存资料失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ==================== 世界日志 ====================

    private void MemoSave_Click(object sender, RoutedEventArgs e)
    {
        var type = (MemoType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "note";
        if (_vm.AddMemo(type, _vm.MemoInput)) _vm.Load();   // 问候语/连续记录随新记录刷新
    }

    private void MemoBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var type = (MemoType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "note";
        if (_vm.AddMemo(type, _vm.MemoInput)) _vm.Load();
    }

    /// <summary>心情一键记录（type=mood，text=「emoji 标签」，与两端一致）。</summary>
    private void Mood_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string mood && _vm.AddMemo("mood", mood)) _vm.Load();
    }

    private void MemoDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string id || string.IsNullOrEmpty(id)) return;
        if (!SimpleDialogs.Confirm("确定删除这条日志？")) return;
        _vm.DeleteMemo(id);
    }

    // ==================== 徽章墙（v1.0.3） ====================

    /// <summary>佩戴徽章：从已解锁成就里挑最多 3 枚，存 settings.json（零 DB 变更）。</summary>
    private void PickBadges_Click(object sender, RoutedEventArgs e)
    {
        List<AchievementEntity> unlocked;
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            unlocked = db.Achievements.AsNoTracking().Where(a => a.Unlocked).ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show("读取成就失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var pinned = SettingsStore.Load().PinnedAchievements;
        if (!BadgePickerDialog.Show(unlocked, pinned)) return;

        try
        {
            var s = SettingsStore.Load();
            s.PinnedAchievements = pinned;
            s.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show("保存徽章佩戴失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _vm.LoadPinnedBadges();
    }
}
