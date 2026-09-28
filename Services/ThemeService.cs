using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EarthOnline.Desktop.Data;

namespace EarthOnline.Desktop.Services;

/// <summary>
/// 外观服务：深浅主题 / 全局字号 / 壁纸。
/// 主题实现：直接修改 App.Resources 中各 SolidColorBrush 的 Color —— 画刷未冻结，
/// 改 Color 后所有引用它的 UI（含已加载页面与侧栏）立即变色，无需 DynamicResource 重构。
/// 字号实现：主窗口 LayoutTransform 缩放，等效全局字号且零侵入。
/// 壁纸实现：替换 Resources["AppBgBrush"] 为 ImageBrush（新建/重导航的页面生效）。
/// </summary>
public static class ThemeService
{
    private static readonly (string Key, Color Light, Color Dark)[] Palette =
    {
        ("AppBgBrush",        Color.FromRgb(0xF8, 0xF6, 0xF2), Color.FromRgb(0x1F, 0x1D, 0x1B)),
        ("CardBgBrush",       Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x2A, 0x27, 0x24)),
        ("BorderBrush",       Color.FromRgb(0xE8, 0xE2, 0xDA), Color.FromRgb(0x3F, 0x3B, 0x36)),
        ("TextPrimaryBrush",  Color.FromRgb(0x1E, 0x1A, 0x16), Color.FromRgb(0xF2, 0xF2, 0xF5)),
        ("TextSecondaryBrush",Color.FromRgb(0x7A, 0x72, 0x68), Color.FromRgb(0xB6, 0xB6, 0xBF)),
        ("TextMutedBrush",    Color.FromRgb(0xB0, 0xA8, 0x9C), Color.FromRgb(0x8F, 0x8F, 0x99)),
        // 强调色两套主题共用
        ("AccentBrush",       Color.FromRgb(0xD4, 0xA3, 0x73), Color.FromRgb(0xD4, 0xA3, 0x73)),
    };

    public static bool IsDark(SettingsStore s) => string.Equals(s.Theme, "dark", StringComparison.OrdinalIgnoreCase);

    /// <summary>按设置应用主题（App 启动 / 切换时调用）。</summary>
    public static void ApplyTheme(SettingsStore s) => ApplyTheme(IsDark(s));

    public static void ApplyTheme(bool dark)
    {
        var res = Application.Current?.Resources;
        if (res is null) return;
        foreach (var (key, light, darkColor) in Palette)
        {
            if (res[key] is SolidColorBrush brush && !brush.IsFrozen)
            {
                brush.Color = dark ? darkColor : light;
            }
        }
    }

    /// <summary>应用全局字号缩放（0.9 / 1.0 / 1.15）。</summary>
    public static void ApplyFontScale(double scale)
    {
        var win = Application.Current?.MainWindow;
        if (win is null) return;
        scale = Math.Clamp(scale, 0.8, 1.4);
        win.LayoutTransform = Math.Abs(scale - 1.0) < 0.01
            ? System.Windows.Media.Transform.Identity
            : new ScaleTransform(scale, scale);
    }

    /// <summary>应用壁纸（AppBgBrush 换成图片；路径为空还原纯色）。</summary>
    public static void ApplyWallpaper(SettingsStore s)
    {
        var res = Application.Current?.Resources;
        if (res is null) return;

        var path = s.WallpaperPath ?? "";
        if (path.Length > 0 && File.Exists(path))
        {
            try
            {
                var img = new ImageBrush(new BitmapImage(new Uri(path)))
                {
                    Stretch = Stretch.UniformToFill,
                    Opacity = 1.0
                };
                // 冻结以支持跨线程/提升性能
                if (img.CanFreeze) img.Freeze();
                res["AppBgBrush"] = img;
                return;
            }
            catch
            {
                // 图片损坏回落纯色
            }
        }

        // 还原纯色（与 App.xaml 初始值一致；当前主题色由画刷 Color 决定）
        var solid = new SolidColorBrush(Color.FromRgb(0xF8, 0xF6, 0xF2));
        if (IsDark(s)) solid.Color = Color.FromRgb(0x1F, 0x1D, 0x1B);
        res["AppBgBrush"] = solid;
    }

    /// <summary>启动时一次性应用全部外观设置。</summary>
    public static void ApplyAll(SettingsStore s)
    {
        ApplyWallpaper(s);   // 先放壁纸（替换画刷对象）
        ApplyTheme(s);       // 再改 Color（对纯色画刷生效）
        ApplyFontScale(s.FontScale);
    }
}
