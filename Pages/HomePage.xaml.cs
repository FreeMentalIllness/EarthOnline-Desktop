using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    /// <summary>概览统计卡点击：根据 Tag 跳转到对应模块（v1.0.3 主页交互重构）。</summary>
    private void StatCard_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string key)
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
            SimpleDialogs.Alert("保存资料失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ==================== 世界日志 ====================

    private string _memoType = "note";
    private string _moodTag = "";

    public HomePage()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += (_, _) => _vm.Load();
        // 默认选中「随笔」标签
        ApplyMemoTagSelection(MemoTypeRow.Children.OfType<Button>()
            .FirstOrDefault(b => (b.Tag as string) == "type:note"));
    }

    /// <summary>
    /// 统一标签行：随笔/重要/灵感 设定类型；「心情」展开/收起心情快选行；
    /// 选中具体心情后未输入文字点「记录」一键直达（与网页 / 安卓 mood 口径一致）。
    /// </summary>
    private void MemoTag_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string tag) return;

        if (tag.StartsWith("type:", StringComparison.Ordinal))
        {
            _memoType = tag["type:".Length..];
            _moodTag = "";
            MoodRow.Visibility = Visibility.Collapsed;
            ApplyMemoTagSelection(sender as Button);
            return;
        }

        if (tag == "mood-open")
        {
            // 「心情」统一入口：展开快选并置为心情类型；再点一次收起
            bool open = MoodRow.Visibility != Visibility.Visible;
            MoodRow.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            _memoType = "mood";
            ApplyMemoTagSelection(open ? sender as Button : null);
            return;
        }

        if (tag.StartsWith("mood:", StringComparison.Ordinal))
        {
            _memoType = "mood";
            _moodTag = tag["mood:".Length..];
            var moodEntry = MemoTypeRow.Children.OfType<Button>()
                .FirstOrDefault(b => (b.Tag as string) == "mood-open");
            ApplyMemoTagSelection(sender as Button, moodEntry);
            // 保留原「心情一键记录」体验：文字为空时选心情立即落一条
            if (string.IsNullOrWhiteSpace(_vm.MemoInput))
            {
                if (_vm.AddMemo("mood", _moodTag)) _vm.Load();
            }
        }
    }

    /// <summary>选中态高亮：选中标签琥珀底白字，其余还原样式默认（类型行 + 心情快选行统一处理）。</summary>
    private void ApplyMemoTagSelection(params Button?[] selected)
    {
        if (MemoTypeRow is null) return;
        var sel = selected.Where(b => b is not null).OfType<Button>().ToHashSet();
        var all = MemoTypeRow.Children.OfType<Button>()
            .Concat(MoodRow?.Children.OfType<Button>() ?? Enumerable.Empty<Button>());
        foreach (var b in all)
        {
            bool on = sel.Contains(b);
            b.Background = on ? ThemeService.Brush("AccentBrush") : null;
            b.BorderBrush = on ? ThemeService.Brush("AccentBrush") : null;
            b.Foreground = on ? Brushes.White : null;
        }
    }

    private void MemoSave_Click(object sender, RoutedEventArgs e) => SaveMemo();

    private void MemoBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SaveMemo();
    }

    private void SaveMemo()
    {
        var text = _vm.MemoInput ?? "";
        // 心情类型且未输入文字：直接记录选中的心情标签（与安卓/网页 mood 口径一致）
        if (_memoType == "mood" && string.IsNullOrWhiteSpace(text)) text = _moodTag;
        if (_vm.AddMemo(_memoType, text)) _vm.Load();
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
            SimpleDialogs.Alert("读取成就失败：" + ex.Message, "地球Online",
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
            SimpleDialogs.Alert("保存徽章佩戴失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _vm.LoadPinnedBadges();
    }
}
