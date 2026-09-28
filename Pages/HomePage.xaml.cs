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

            if (!ProfileDialog.Show(p)) return;

            using var db2 = new AppDbContext(AppPaths.DbFile);
            var row = db2.Profile.Find(1);
            if (row is null) { db2.Profile.Add(p); }
            else { db2.Entry(row).CurrentValues.SetValues(p); }
            db2.SaveChanges();
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
        _vm.AddMemo(type, _vm.MemoInput);
    }

    private void MemoBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var type = (MemoType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "note";
        _vm.AddMemo(type, _vm.MemoInput);
    }

    /// <summary>心情一键记录（type=mood，text=「emoji 标签」，与两端一致）。</summary>
    private void Mood_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string mood) _vm.AddMemo("mood", mood);
    }

    private void MemoDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string id || string.IsNullOrEmpty(id)) return;
        if (!SimpleDialogs.Confirm("确定删除这条日志？")) return;
        _vm.DeleteMemo(id);
    }
}
