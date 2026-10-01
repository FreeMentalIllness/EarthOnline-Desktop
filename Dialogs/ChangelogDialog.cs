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
            "v1.0.3 · 视觉统一与数据目录",
            new[]
            {
                "主页卡片整体可点击，直达对应页面",
                "头像 / 壁纸裁剪升级为原图质量输出，支持缩放选区",
                "背包 / 成就分类改为芯片样式，支持滚轮横向滚动",
                "主题切换根治：画刷整体动态替换，深浅切换立即生效",
                "支持自定义数据目录（承载数据库 / 头像 / 文件 / 备份，旧数据递归迁移）",
                "侧栏导航重排、退出行为可选（最小化到托盘或退出）"
            }));

        root.Children.Add(VersionBlock(
            "v1.0.5 · 全量迭代",
            new[]
            {
                "全局搜索：Ctrl+F 一框全查任务 / 物品 / 收藏 / 日志 / 足迹，双击结果直达",
                "回收站：任务 / 日志 / 物品 / 收藏 / 足迹删除均保留 30 天，可随时恢复，超期自动清理",
                "批量操作：Ctrl / Shift 点选多个任务，批量完成 / 删除（进回收站）/ 导出 JSON",
                "键盘快捷键：Ctrl+N 新建任务；Ctrl+1~8 切换模块",
                "数据导入增强：CSV 任务表、Markdown 日记（支持 # 日期标题）",
                "分享人生卡：等级 / 连续记录 / 本月节奏一键生成 PNG 长图",
                "记忆相册：导入老照片，留住回忆并解锁隐藏成就",
                "通知提醒：任务到期、同步完成走托盘通知，成就解锁保持悬浮 Toast",
                "深色模式可跟随系统，地图夜间样式可跟随深色主题",
                "同步失败提示全部中文化（密码错误 / 404 / 超时各有人话）",
                "应用内自动更新：启动静默检查 + 一键下载，bat 无缝替换重启"
            }));

        root.Children.Add(VersionBlock(
            "v1.0.4",
            new[]
            {
                "深色模式全量主题化：指标卡、日历热图、图表、AI 气泡与全部弹窗随主题取色",
                "深色配色与 Web / Android 三端统一，强调色深色提亮，按钮文字对比度达 WCAG AA",
                "引导完成后正确回到主页（不再卡在引导页）",
                "窗口位置 / 尺寸 / 最大化与侧栏折叠状态记忆，越界自动回正",
                "卡片悬浮反馈去模糊：边框高亮 + 底色微变，清晰且跟手",
                "二级窗口全面主题化：统一圆角壳、自绘标题栏、暖色确认与输入弹窗",
                "数据库初始化加固：老库基线标记，避免升级时迁移重放失败",
                "下拉框显示修复，列表滚动条自动隐藏",
                "概览页数据卡与足迹卡换位，日志与心情入口合并",
                "空状态壁纸显示修复；头像 / 壁纸裁剪支持滚轮缩放",
                "新增赞助弹窗（设置页入口）"
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
