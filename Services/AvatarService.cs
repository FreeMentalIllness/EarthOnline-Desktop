using System.IO;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using Microsoft.Win32;

namespace EarthOnline.Desktop.Services;

/// <summary>
/// 头像文件统一管理（唯一入口，避免多入口各自复制 / 各自删除产生孤儿文件）。
/// 规则与安卓端一致：
///  - 选图后**原图完整复制**进私有 avatar 目录，不重编码；
///  - 旧文件在**新值落库成功之后**才删；
///  - 只删本应用 avatar 目录内的文件（用户放在别处的图片绝不碰）。
/// </summary>
public static class AvatarService
{
    /// <summary>打开选图对话框并把原图复制进头像目录。返回新路径；取消或失败返回 null。</summary>
    public static string? PickAndCopy()
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择头像图片",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*"
            };
            if (dlg.ShowDialog() != true) return null;

            AppPaths.EnsureDirectories();
            var dest = Path.Combine(AppPaths.AvatarDir,
                Guid.NewGuid().ToString("N") + Path.GetExtension(dlg.FileName));
            File.Copy(dlg.FileName, dest, overwrite: true);
            return dest;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("复制头像失败：" + ex.Message, "地球Online",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return null;
        }
    }

    /// <summary>把新头像路径写入实体（空 = 回落到 emoji）。只改内存，不碰文件。</summary>
    public static void Apply(ProfileEntity p, string? newPath)
    {
        p.AvatarPath = string.IsNullOrWhiteSpace(newPath) ? null : newPath;
    }

    /// <summary>
    /// 落库成功后调用：清理被替换掉的旧头像文件。
    /// 仅在「路径确已变化 + 位于本应用 avatar 目录内」时删除，失败静默忽略。
    /// </summary>
    public static void CleanupOrphan(string? oldPath, string? keptPath)
    {
        if (string.IsNullOrEmpty(oldPath)) return;
        if (string.Equals(oldPath, keptPath, StringComparison.OrdinalIgnoreCase)) return;
        if (!oldPath.StartsWith(AppPaths.AvatarDir, StringComparison.OrdinalIgnoreCase)) return;
        try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { /* 清理失败不影响 */ }
    }

    /// <summary>
    /// 放弃一次未提交的选图（对话框取消 / 校验失败）：删掉刚复制进来、还没落库的临时头像。
    /// 这是「取消也会留孤儿文件」的唯一补丁点。
    /// </summary>
    public static void Discard(string? pendingPath, string? originalPath)
    {
        if (string.IsNullOrEmpty(pendingPath)) return;
        if (string.Equals(pendingPath, originalPath, StringComparison.OrdinalIgnoreCase)) return;
        if (!pendingPath.StartsWith(AppPaths.AvatarDir, StringComparison.OrdinalIgnoreCase)) return;
        try { if (File.Exists(pendingPath)) File.Delete(pendingPath); } catch { /* 清理失败不影响 */ }
    }
}
