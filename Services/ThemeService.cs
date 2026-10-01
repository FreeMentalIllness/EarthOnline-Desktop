using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EarthOnline.Desktop.Data;

namespace EarthOnline.Desktop.Services;

/// <summary>
/// 外观服务：深浅主题 / 全局字号 / 壁纸。
/// 主题实现：整体替换 App.Resources 中的画刷对象。
/// 注意：资源字典里的 Freezable 会被框架自动冻结，「改 Color」会静默失败（历史 bug 根因），
/// 故改为替换对象；XAML 侧画刷引用统一用 DynamicResource，替换后已加载界面立即刷新。
/// 字号实现：主窗口 LayoutTransform 缩放，等效全局字号且零侵入。
/// 壁纸实现：替换 Resources["AppBgBrush"] 为 ImageBrush（新建/重导航的页面生效）。
/// </summary>
public static class ThemeService
{
    private static readonly (string Key, Color Light, Color Dark)[] Palette =
    {
        ("AppBgBrush",        Color.FromRgb(0xF8, 0xF6, 0xF2), Color.FromRgb(0x1A, 0x1A, 0x1A)),
        ("CardBgBrush",       Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x23, 0x23, 0x26)),
        ("BorderBrush",       Color.FromRgb(0xE8, 0xE2, 0xDA), Color.FromRgb(0x3F, 0x3F, 0x46)),
        ("TextPrimaryBrush",  Color.FromRgb(0x1E, 0x1A, 0x16), Color.FromRgb(0xF2, 0xF2, 0xF5)),
        ("TextSecondaryBrush",Color.FromRgb(0x7A, 0x72, 0x68), Color.FromRgb(0xB6, 0xB6, 0xBF)),
        ("TextMutedBrush",    Color.FromRgb(0xB0, 0xA8, 0x9C), Color.FromRgb(0x8F, 0x8F, 0x99)),
        // 强调色两套主题共用
        ("AccentBrush",       Color.FromRgb(0xD4, 0xA3, 0x73), Color.FromRgb(0xE0, 0xA9, 0x6D)),
        // ---- 代码生成界面专用（指标卡 / 日历热图 / 图表 / 气泡 / 芯片）----
        ("SoftBgBrush",       Color.FromRgb(0xF8, 0xF6, 0xF2), Color.FromRgb(0x2E, 0x2B, 0x27)),
        // 壁纸模式：侧栏与内容区文字垫层（半透明遮罩，壁纸微透、文字可读）
        ("NavMaskBrush",      Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF), Color.FromArgb(0xF0, 0x1F, 0x1D, 0x1B)),
        ("ContentScrimBrush", Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF), Color.FromArgb(0xD9, 0x1F, 0x1D, 0x1B)),
        ("ChipFillBrush",     Color.FromRgb(0xEF, 0xE9, 0xE0), Color.FromRgb(0x3A, 0x36, 0x30)),
        ("Heat0Brush",        Color.FromRgb(0xFA, 0xF8, 0xF5), Color.FromRgb(0x26, 0x23, 0x20)),
        ("Heat1Brush",        Color.FromRgb(0xF5, 0xE9, 0xDC), Color.FromRgb(0x3A, 0x32, 0x2A)),
        ("Heat2Brush",        Color.FromRgb(0xEB, 0xD3, 0xB3), Color.FromRgb(0x56, 0x45, 0x2F)),
        ("Heat3Brush",        Color.FromRgb(0xD9, 0xB4, 0x8A), Color.FromRgb(0x7A, 0x5F, 0x3C)),
        ("DueDotBrush",       Color.FromRgb(0xE7, 0x6F, 0x51), Color.FromRgb(0xE8, 0x8B, 0x72)),
        ("StatusDoneBrush",   Color.FromRgb(0xD4, 0xA3, 0x73), Color.FromRgb(0xD4, 0xA3, 0x73)),
        ("StatusActiveBrush", Color.FromRgb(0x8A, 0xA7, 0x9B), Color.FromRgb(0x7E, 0x9E, 0x90)),
        ("StatusPausedBrush", Color.FromRgb(0xE3, 0xC1, 0xA2), Color.FromRgb(0xC9, 0xA4, 0x87)),
        ("StatusPlanningBrush",Color.FromRgb(0xC9, 0xC2, 0xB8), Color.FromRgb(0x6E, 0x68, 0x62)),
        ("AiBubbleUserBrush", Color.FromRgb(0xF3, 0xE4, 0xD2), Color.FromRgb(0x4A, 0x3B, 0x2C)),
        ("AiBubbleAiBrush",   Color.FromRgb(0xF6, 0xF4, 0xF0), Color.FromRgb(0x33, 0x30, 0x2C)),
        ("ChartGridBrush",    Color.FromRgb(0xE8, 0xE2, 0xDA), Color.FromRgb(0x3F, 0x3B, 0x36)),
        ("ChartTextBrush",    Color.FromRgb(0xB0, 0xA8, 0x9C), Color.FromRgb(0x8F, 0x8F, 0x99)),
        // ---- 悬浮反馈专用（卡片 / 按钮悬浮时的高亮；禁模糊后统一的跟手反馈）----
        ("HoverBgBrush",      Color.FromRgb(0xF3, 0xEE, 0xE8), Color.FromRgb(0x2C, 0x2C, 0x30)),
        ("HoverBorderBrush",  Color.FromRgb(0xD9, 0xC4, 0xA6), Color.FromRgb(0x5C, 0x54, 0x44)),
    };

    /// <summary>当前是否为深色主题（ApplyTheme 后有效；无 Application 时按最近一次设置判定）。</summary>
    public static bool Dark { get; private set; }

    public static bool IsDark(SettingsStore s) => string.Equals(s.Theme, "dark", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 取当前主题下的画刷（供代码生成的控件使用）。
    /// 优先取 Application.Resources（与 XAML DynamicResource 同源），取不到时按 Palette 当前主题回落，
    /// 保证单测 / 无 Application 的场景也不会空引用或黑块。
    /// </summary>
    public static SolidColorBrush Brush(string key)
    {
        if (Application.Current?.Resources is { } res && res[key] is SolidColorBrush b) return b;
        return new SolidColorBrush(ColorOf(key));
    }

    /// <summary>取当前主题下的颜色（Palette 权威表；未登记时回落中性灰）。</summary>
    public static Color ColorOf(string key)
    {
        foreach (var (k, light, darkColor) in Palette)
            if (k == key) return Dark ? darkColor : light;
        return Dark ? Color.FromRgb(0x8F, 0x8F, 0x99) : Color.FromRgb(0xB0, 0xA8, 0x9C);
    }

    /// <summary>
    /// 历史硬编码十六进制色 → 当前主题画笔（代码生成 UI 的统一收敛口）。
    /// 早年为省事直接在 C# 里写死色值，深色模式下会出现白块 / 黑字看不清；
    /// 这里按色值语义映射到调色板键，未登记的色原样返回（如纯白前景、品牌橙）。
    /// </summary>
    private static readonly (string Hex, string Key)[] HexMap =
    {
        ("#FFFFFF", "CardBgBrush"),
        ("#FAF8F5", "Heat0Brush"),
        ("#F5E9DC", "Heat1Brush"),
        ("#EBD3B3", "Heat2Brush"),
        ("#D9B48A", "Heat3Brush"),
        ("#F8F6F2", "SoftBgBrush"),
        ("#EFE9E0", "ChipFillBrush"),
        ("#F6F4F0", "AiBubbleAiBrush"),
        ("#F3E4D2", "AiBubbleUserBrush"),
        ("#E8E2DA", "BorderBrush"),
        ("#D4A373", "AccentBrush"),
        ("#B07B3F", "AccentBrush"),
        ("#1E1A16", "TextPrimaryBrush"),
        ("#4A443C", "TextPrimaryBrush"),
        ("#7A7268", "TextSecondaryBrush"),
        ("#B0A89C", "TextMutedBrush"),
        ("#E76F51", "DueDotBrush"),
    };

    public static SolidColorBrush FromHex(string hex)
    {
        foreach (var (h, key) in HexMap)
            if (string.Equals(h, hex, StringComparison.OrdinalIgnoreCase)) return Brush(key);
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return new SolidColorBrush(Colors.Gray); }
    }

    /// <summary>按设置应用主题（App 启动 / 切换时调用）。</summary>
    public static void ApplyTheme(SettingsStore s) => ApplyTheme(IsDark(s));

    /// <summary>主题切换完成通知（亮↔暗变化后触发；订阅方如地图页用于联动夜间样式）。</summary>
    public static event Action<bool>? ThemeChanged;

    public static void ApplyTheme(bool dark)
    {
        var changed = Dark != dark;
        Dark = dark;   // 先记状态：无 Application 时 ColorOf 也能给出正确主题色
        var res = Application.Current?.Resources;
        if (res is null) return;
        foreach (var (key, light, darkColor) in Palette)
        {
            // 说明：Application.Resources 里的 Freezable 会被框架自动冻结（BAML 加载期即冻结，
            // 运行时塞进去的新画刷同样会被冻结），因此「改 Color」这条路走不通——冻结后赋值静默失败。
            // 改为整体替换资源对象：XAML 侧统一用 DynamicResource 引用，替换后已加载界面会立即刷新。
            if (key == "AppBgBrush" && res[key] is ImageBrush) continue; // 有壁纸时不拿纯色盖掉它
            res[key] = new SolidColorBrush(dark ? darkColor : light);
        }
        if (changed) ThemeChanged?.Invoke(dark);
    }

    /// <summary>应用全局字号缩放（0.9 / 1.0 / 1.15）。
    /// 作用于主窗口根 Grid 的 LayoutTransform：缩放整棵视觉树（侧栏 + 内容区同步放大），
    /// 并触发窗口重新测量自动调整尺寸，比直接缩放 Window 更可靠、无裁剪。</summary>
    public static void ApplyFontScale(double scale)
    {
        var win = Application.Current?.MainWindow;
        if (win is null) return;
        scale = Math.Clamp(scale, 0.8, 1.4);
        var transform = Math.Abs(scale - 1.0) < 0.01
            ? (Transform)Transform.Identity
            : new ScaleTransform(scale, scale);
        if (win is MainWindow mw && mw.RootGrid != null)
            mw.RootGrid.LayoutTransform = transform;
        else
            win.LayoutTransform = transform;
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
                // 不透明度可调（设置页滑杆）；深色模式自动 ×0.4（对齐安卓壁纸透出口径），保证文字可读
                double op = Math.Clamp(s.WallpaperOpacity <= 0 ? 1.0 : s.WallpaperOpacity, 0.1, 1.0);
                if (IsDark(s)) op *= 0.4;
                var img = new ImageBrush(new BitmapImage(new Uri(path)))
                {
                    Stretch = Stretch.UniformToFill,
                    Opacity = op
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

        // 还原纯色（对象整体替换，DynamicResource 会刷新；当前主题色由 IsDark 决定）
        var target = IsDark(s) ? Color.FromRgb(0x1F, 0x1D, 0x1B) : Color.FromRgb(0xF8, 0xF6, 0xF2);
        res["AppBgBrush"] = new SolidColorBrush(target);
    }

    /// <summary>启动时一次性应用全部外观设置。</summary>
    public static void ApplyAll(SettingsStore s)
    {
        ApplyTheme(s);          // 先铺好调色板（动态资源即时刷新）
        ApplyWallpaper(s);      // 再放壁纸（有壁纸则覆盖背景画刷）
        ApplyFontScale(s.FontScale);
    }
}
