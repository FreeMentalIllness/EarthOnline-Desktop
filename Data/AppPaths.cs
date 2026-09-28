using System.IO;

namespace EarthOnline.Desktop.Data;

/// <summary>
/// 桌面端本地存储路径（对应安卓 filesDir / 网页 IndexedDB 的角色）。
/// 统一放在 %LOCALAPPDATA%\EarthOnline 下，便于备份与排查。
/// </summary>
public static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\EarthOnline</summary>
    public static string RootDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EarthOnline");

    /// <summary>数据库文件（文件名与安卓 earth_online.db 保持一致）。</summary>
    public static string DbFile { get; } = Path.Combine(RootDir, "earth_online.db");

    /// <summary>头像原图目录（对应安卓 filesDir/avatar）。</summary>
    public static string AvatarDir { get; } = Path.Combine(RootDir, "avatar");

    /// <summary>收藏文件目录（对应安卓沙盒文件）。</summary>
    public static string FilesDir { get; } = Path.Combine(RootDir, "files");

    /// <summary>自动备份目录。</summary>
    public static string BackupDir { get; } = Path.Combine(RootDir, "backups");

    /// <summary>创建所有目录（幂等）。</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(AvatarDir);
        Directory.CreateDirectory(FilesDir);
        Directory.CreateDirectory(BackupDir);
    }
}
