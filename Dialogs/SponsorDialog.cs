using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EarthOnline.Desktop.Services;

namespace EarthOnline.Desktop.Dialogs;

/// <summary>
/// 赞助弹窗（设置页「💖 赞助」入口）：展示微信收款码 + 说明文字 + 赞助者名单。
/// 收款码为内嵌资源（pack URI），单文件发布同样可用；图片缺失时只显示文字不阻断。
/// </summary>
public static class SponsorDialog
{
    /// <summary>赞助者名单：与 Web / Android 三端同一名单、同一顺序（勿加修饰词）。</summary>
    private const string SponsorsLine = "海神唐三 · Seastar · 清浅";

    public static void Show()
    {
        var win = new ThemeDialogWindow("💖 赞助支持", 400, 780);

        var root = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };

        root.Children.Add(new TextBlock
        {
            Text = "地球Online 一直免费、无广告，数据全部存放在你自己的设备上。\n" +
                   "如果它陪你坚持了一段小小的旅程，欢迎请作者喝一杯 ☕",
            FontSize = 13, LineHeight = 21, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = ThemeService.Brush("TextPrimaryBrush")
        });

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri("pack://application:,,,/Assets/sponsor_wechat.jpg");
            bmp.DecodePixelWidth = 560;   // 显示 ≤280px，2 倍超采样保证码点锐利可扫
            bmp.EndInit();
            bmp.Freeze();
            var qr = new Image
            {
                Source = bmp,
                MaxWidth = 280,
                MaxHeight = 360,
                Margin = new Thickness(0, 14, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(qr, BitmapScalingMode.HighQuality);
            root.Children.Add(qr);
        }
        catch
        {
            root.Children.Add(new TextBlock
            {
                Text = "（收款码加载失败，可到 GitHub 仓库主页扫码）",
                FontSize = 12, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 14, 0, 4),
                Foreground = ThemeService.Brush("TextMutedBrush")
            });
        }

        root.Children.Add(new TextBlock
        {
            Text = "微信扫码 · 金额随意，心意最重\n赞助会用于服务器、开发与维护投入 ❤️",
            FontSize = 12, LineHeight = 19, TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = ThemeService.Brush("TextSecondaryBrush")
        });

        root.Children.Add(new Border
        {
            Height = 1, Background = ThemeService.Brush("BorderBrush"),
            Margin = new Thickness(0, 14, 0, 12)
        });

        root.Children.Add(new TextBlock
        {
            Text = "❤️ 赞助者名单",
            FontSize = 12, TextAlignment = TextAlignment.Center,
            Foreground = ThemeService.Brush("TextSecondaryBrush")
        });
        root.Children.Add(new TextBlock
        {
            Text = SponsorsLine,
            FontSize = 14, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = ThemeService.Brush("TextPrimaryBrush")
        });

        var ok = new Button
        {
            Content = "谢谢 ❤️",
            MinWidth = 96, Height = 34, Padding = new Thickness(14, 0, 14, 0),
            Margin = new Thickness(0, 18, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        ok.Style = (Style)Application.Current.Resources["PrimaryButtonStyle"];
        ok.Click += (_, _) => win.Close();
        root.Children.Add(ok);

        win.SetBody(new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        win.ShowDialog();
    }
}
