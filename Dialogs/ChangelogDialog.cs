using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EarthOnline.Desktop.Services;

namespace EarthOnline.Desktop.Dialogs;

/// <summary>
/// 完整更新日志（独立弹窗）：从关于页整体抽出，避免关于页视觉冗长。
/// 内容与三端 Release Notes 保持同步；新增版本时在此追加一个版本块即可。
/// </summary>
public static class ChangelogDialog
{
    private static Brush T(string key) => ThemeService.Brush(key);

    public static void Show()
    {
        var win = new ThemeDialogWindow("更新日志", 520, 640);
        var root = new StackPanel();

        root.Children.Add(VersionBlock(
            "v1.0.4（迭代中）",
            new[]
            {
                "深色模式全量主题化：指标卡、日历热图、图表、AI 气泡与全部弹窗随主题取色",
                "引导完成后正确回到主页（不再卡在引导页）",
                "窗口位置 / 尺寸 / 最大化与侧栏折叠状态记忆，越界自动回正",
                "卡片悬浮反馈去模糊：边框高亮 + 底色微变，清晰且跟手",
                "二级窗口全面主题化：统一圆角壳、自绘标题栏、暖色确认与输入弹窗",
                "数据库初始化加固：老库基线标记，避免升级时迁移重放失败"
            }));

        root.Children.Add(VersionBlock(
            "v1.0.2 · 收尾与体验完善",
            new[]
            {
                "新增首次启动引导（建角色 / 功能简介 / WebDAV / 导入导出）",
                "地图 Key 支持自定义配置，未配置时友好降级为列表视图",
                "头像文件统一入口管理，杜绝孤儿文件",
                "设置页新增快捷键说明卡与自动备份管理"
            }));

        root.Children.Add(VersionBlock(
            "v1.0.1 · 桌面端 UI 与功能补齐",
            new[]
            {
                "应用图标与移动端统一（exe / 窗口 / 托盘 / 标题栏）",
                "自定义头像图片（原图复制 + 圆形显示）",
                "任务明细面板：状态 / 进度 / 备注 / 时间 / 子任务",
                "背包与成就按分类分组（分组头置顶 / 可展开）",
                "新增足迹地图页（高德地图，与网页端同 Key）",
                "新增 AI 助手（OpenAI 兼容接口，密钥经 Windows DPAPI 加密后存于本机）",
                "新增本地自动备份（保留最近 3 份快照，命名与安卓一致）",
                "快捷键说明与地图离线降级提示"
            }));

        root.Children.Add(VersionBlock(
            "v1.0.0 · 首个正式版",
            new[] { "三端数据互通、任务树、背包收藏、成就彩蛋、数据看板、WebDAV 同步、深色主题。" },
            last: true));

        win.SetBody(new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 520
        });
        win.ShowDialog();
    }

    /// <summary>一个版本块：版本号标题 + 条目列表。</summary>
    private static StackPanel VersionBlock(string title, string[] items, bool last = false)
    {
        var sp = new StackPanel { Margin = new Thickness(0, last ? 0 : 6, 0, 14) };

        sp.Children.Add(new TextBlock
        {
            Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = T("TextPrimaryBrush"), Margin = new Thickness(0, 0, 0, 6)
        });
        foreach (var item in items)
        {
            sp.Children.Add(new TextBlock
            {
                Text = "· " + item, FontSize = 12, LineHeight = 19, TextWrapping = TextWrapping.Wrap,
                Foreground = T("TextSecondaryBrush"), Margin = new Thickness(0, 2, 0, 0)
            });
        }
        return sp;
    }
}
