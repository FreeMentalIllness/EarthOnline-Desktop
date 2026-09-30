using System.Windows;
using System.Windows.Forms;

namespace EarthOnline.Setup;

public partial class MainWindow : Window
{
    private readonly App.InstallOptions _opts;
    private CancellationTokenSource? _cts;
    private string _installDir = string.Empty;
    private bool _createdDesktop, _createdStartMenu, _taskbarRequested;

    public MainWindow(App.InstallOptions opts)
    {
        InitializeComponent();
        _opts = opts;

        VersionText.Text = "版本 " + (GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.4");

        if (opts.AutoStart)
        {
            // 管理员提权后回跑：用传入选项直接开始安装
            PathBox.Text = opts.InstallDir;
            DesktopChk.IsChecked = opts.Desktop;
            StartMenuChk.IsChecked = opts.StartMenu;
            TaskbarChk.IsChecked = opts.Taskbar;
            Loaded += (_, _) => BeginInstall();
        }
        else
        {
            PathBox.Text = opts.InstallDir;
            DesktopChk.IsChecked = true;
            StartMenuChk.IsChecked = true;
            TaskbarChk.IsChecked = opts.Taskbar;
        }
    }

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "选择安装盘符或目录（将自动创建 地球Online 专属文件夹）",
            UseDescriptionForTitle = true,
            SelectedPath = PathBox.Text,
            ShowNewFolderButton = true
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            PathBox.Text = SetupManager.NormalizeInstallDir(dlg.SelectedPath);
            PathHint.Text = "实际安装到：" + PathBox.Text;
        }
    }

    private void UseProgramFilesBtn_Click(object sender, RoutedEventArgs e)
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        PathBox.Text = System.IO.Path.Combine(pf, "EarthOnline");
    }

    private void InstallBtn_Click(object sender, RoutedEventArgs e)
    {
        var path = SetupManager.NormalizeInstallDir(PathBox.Text.Trim());
        PathBox.Text = path;
        if (string.IsNullOrWhiteSpace(path))
        {
            System.Windows.MessageBox.Show("请先选择安装位置。", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        // 受保护目录需管理员：请求提权并重跑
        if (App.NeedsAdmin(path) && !App.IsAdministrator() && !_opts.Elevated)
        {
            var res = System.Windows.MessageBox.Show(
                $"安装到“{path}”需要管理员权限。是否以管理员身份重新运行安装程序？",
                "需要管理员权限", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                RelaunchElevated(path, DesktopChk.IsChecked == true,
                    StartMenuChk.IsChecked == true, TaskbarChk.IsChecked == true);
                return;
            }
            return; // 用户拒绝提权，留在选项页
        }

        BeginInstall();
    }

    private void RelaunchElevated(string path, bool desktop, bool startMenu, bool taskbar)
    {
        try
        {
            var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                ?? System.AppContext.BaseDirectory;
            var args = $"/elevated /autostart /dir=\"{path}\""
                + (desktop ? " /desktop" : "")
                + (startMenu ? " /startmenu" : "")
                + (taskbar ? " /taskbar" : "");
            var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
            {
                Verb = "runas",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("提权启动失败：" + ex.Message, "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void BeginInstall()
    {
        _installDir = SetupManager.NormalizeInstallDir(PathBox.Text.Trim());
        _createdDesktop = DesktopChk.IsChecked == true;
        _createdStartMenu = StartMenuChk.IsChecked == true;
        _taskbarRequested = TaskbarChk.IsChecked == true;

        OptionsGrid.Visibility = Visibility.Collapsed;
        ProgressGrid.Visibility = Visibility.Visible;
        DoneGrid.Visibility = Visibility.Collapsed;
        ProgressBar.IsIndeterminate = true;
        LogText.Text = string.Empty;
        ProgressTitle.Text = "正在安装 地球Online…";
        InstallBtn.IsEnabled = false;
        CancelBtn.Content = "取消";

        _cts = new CancellationTokenSource();
        var opts = new App.InstallOptions(
            InstallDir: _installDir,
            Desktop: _createdDesktop,
            StartMenu: _createdStartMenu,
            Taskbar: _taskbarRequested,
            Silent: false, Elevated: _opts.Elevated, AutoStart: false);

        _ = SetupManager.InstallAsync(opts, OnProgress, _cts.Token)
            .ContinueWith(_ => { }, TaskScheduler.Default);
    }

    private void OnProgress(InstallProgress p)
    {
        Dispatcher.Invoke(() =>
        {
            if (p.Message is not null)
            {
                LogText.Text += p.Message + Environment.NewLine;
                if (!p.Done && !p.Failed) ProgressTitle.Text = p.Message;
            }

            if (p.Percent is not null)
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = p.Percent.Value;
            }
            else if (!p.Done && !p.Failed)
            {
                ProgressBar.IsIndeterminate = true;
            }

            if (p.Done) ShowDone();
            else if (p.Failed)
            {
                ProgressTitle.Text = "安装未完成";
                ProgressBar.IsIndeterminate = false;
                CancelBtn.Content = "关闭";
                InstallBtn.IsEnabled = true;
            }
        });
    }

    private void ShowDone()
    {
        ProgressGrid.Visibility = Visibility.Collapsed;
        DoneGrid.Visibility = Visibility.Visible;

        var lines = new System.Text.StringBuilder();
        lines.AppendLine("已安装到：" + _installDir);
        var created = new List<string>();
        if (_createdDesktop) created.Add("桌面快捷方式");
        if (_createdStartMenu) created.Add("开始菜单快捷方式");
        lines.AppendLine("已创建：" + (created.Count > 0 ? string.Join("、", created) : "（无）"));
        if (_taskbarRequested) lines.AppendLine("任务栏：已尝试固定，如未生效请手动固定。");
        DoneSummary.Text = lines.ToString().TrimEnd();

        TaskbarNote.Visibility = _taskbarRequested ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ProgressGrid.Visibility == Visibility.Visible && _cts is not null)
        {
            _cts.Cancel();
            CancelBtn.IsEnabled = false;
            LogText.Text += "正在取消…" + Environment.NewLine;
            return;
        }
        Close();
    }

    private void RunBtn_Click(object sender, RoutedEventArgs e)
    {
        AppLauncher.Launch(_installDir);
        Close();
    }

    private void FinishBtn_Click(object sender, RoutedEventArgs e) => Close();
}
