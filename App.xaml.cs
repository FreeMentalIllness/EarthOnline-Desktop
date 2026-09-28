using System.Windows;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Services;

namespace EarthOnline.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.EnsureDirectories();

        try
        {
            // 首次启动建库并补齐 Profile 行；已存在则应用待执行的迁移
            using var db = new AppDbContext(AppPaths.DbFile);
            db.EnsureCreated();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "本地数据库初始化失败：\n" + ex.Message,
                "地球Online",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        base.OnStartup(e);

        // 自动同步开启时：冷启动后台拉取云端更新（不阻塞窗口显示）
        var s = SettingsStore.Load();
        if (s.HasConfig && s.AutoSync)
        {
            _ = Task.Run(async () =>
            {
                try { await SyncService.PullIfRemoteNewerAsync(); }
                catch { /* 同步失败绝不影响启动 */ }
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 退出时推一次本地最新存档（与安卓「切后台推送」一致），最多等 6 秒
        try
        {
            var s = SettingsStore.Load();
            if (s.HasConfig && s.AutoSync)
            {
                SyncService.PushAsync(force: true).Wait(TimeSpan.FromSeconds(6));
            }
        }
        catch
        {
            // 退出路径上吞掉所有异常，避免挡住关机流程
        }
        base.OnExit(e);
    }
}
