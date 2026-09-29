using System.IO;

namespace EarthOnline.Desktop.Data;

/// <summary>
/// 桌面端本地存储路径（对应安卓 filesDir / 网页 IndexedDB 的角色）。
/// 默认放在「应用根目录\EarthOnlineData」（不可写时回落 %LOCALAPPDATA%\EarthOnline）；
/// 用户可在设置里指定任意自定义目录（DataRoot 在启动时由 App 解析设置）。
/// </summary>
public static class AppPaths
{
    /// <summary>当前数据根目录（运行时由 App.InitDataRoot 设定；默认先给一个保守值，避免静态构造期空引用）。</summary>
    public static string DataRoot
    {
        get => _dataRoot;
        set { _dataRoot = value; Recompute(); }
    }

    private static string _dataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EarthOnline");

    /// <summary>数据库文件（文件名与安卓 earth_online.db 保持一致）。</summary>
    public static string DbFile { get; set; } = "";

    /// <summary>头像原图目录（对应安卓 filesDir/avatar）。</summary>
    public static string AvatarDir { get; private set; } = "";

    /// <summary>收藏文件目录（对应安卓沙盒文件）。</summary>
    public static string FilesDir { get; private set; } = "";

    /// <summary>自动备份目录。</summary>
    public static string BackupDir { get; private set; } = "";

    /// <summary>RootDir（= DataRoot 本身）。</summary>
    public static string RootDir => _dataRoot;

    /// <summary>根据 DataRoot 重新派生所有子目录与 DbFile（DataRoot 变更后调用）。</summary>
    private static void Recompute()
    {
        DbFile = Path.Combine(_dataRoot, "earth_online.db");
        AvatarDir = Path.Combine(_dataRoot, "avatar");
        FilesDir = Path.Combine(_dataRoot, "files");
        BackupDir = Path.Combine(_dataRoot, "backups");
    }

    static AppPaths()
    {
        Recompute();
    }

    /// <summary>创建所有目录（幂等）。</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(_dataRoot);
        Directory.CreateDirectory(AvatarDir);
        Directory.CreateDirectory(FilesDir);
        Directory.CreateDirectory(BackupDir);
    }
}
