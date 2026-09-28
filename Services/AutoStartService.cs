using System.IO;
using Microsoft.Win32;

namespace EarthOnline.Desktop.Services;

/// <summary>
/// 开机自启：写入 HKCU\Software\Microsoft\Windows\CurrentVersion\Run。
/// 只影响当前用户，不需要管理员权限；卸载/取消勾选即删除该键值。
/// </summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EarthOnlineDesktop";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string v && File.Exists(v.Trim('"'));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>写入/更新自启项（指向当前 exe 路径，带引号防路径含空格）。</summary>
    public static void Enable()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            throw new InvalidOperationException("无法定位当前程序路径，开机自启设置失败。");

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, $"\"{exe}\"");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled) Enable();
        else Disable();
    }
}
