using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Services;

namespace EarthOnline.Desktop.Dialogs;

/// <summary>
/// 自定义裁剪对话框（v1.0.3）：原图全分辨率加载，支持「拖拽移动 + 滑块缩放」选择区域，
/// 确认后按源像素裁剪并保存为 PNG（无损、保留原图画质，不降分辨率/不重编码压缩）。
/// 头像用圆形取景框，壁纸用方形取景框；输出文件落在指定目录（头像→avatar，壁纸→数据根）。
/// </summary>
public static class CropDialog
{
    // 取景视口（画布）边长（设备无关像素）
    private const double Viewport = 340;
    // 取景框直径（圆形/方形边长）占视口比例
    private const double FrameRatio = 0.82;

    /// <summary>打开裁剪对话框。成功返回 true 并通过 croppedPath 给出裁剪后的 PNG 路径。</summary>
    /// <param name="sourcePath">原图路径（原图会被完整读入，不会被修改）。</param>
    /// <param name="croppedPath">输出：裁剪后 PNG 的完整路径。</param>
    /// <param name="circular">true=圆形取景（头像），false=方形取景（壁纸）。</param>
    /// <param name="outputDir">输出目录；省略则使用头像目录。</param>
    public static bool Show(string sourcePath, out string? croppedPath, bool circular = true, string? outputDir = null)
    {
        croppedPath = null;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return false;

        BitmapImage? bmp = null;
        try
        {
            bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // 读完即释放文件句柄
            bmp.UriSource = new Uri(sourcePath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze(); // 冻结后可用于 CroppedBitmap 且线程安全
        }
        catch
        {
            SimpleDialogs.Alert("无法读取该图片，请换一张试试。", "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (bmp is null || bmp.PixelWidth == 0 || bmp.PixelHeight == 0) return false;

        double iw = bmp.PixelWidth;
        double ih = bmp.PixelHeight;

        // ---- 窗口与画布 ----
        var canvas = new Canvas
        {
            Width = Viewport,
            Height = Viewport,
            Background = ThemeService.FromHex("#EFE9E0"),
            ClipToBounds = true,
            Cursor = Cursors.Hand
        };

        var scale = new ScaleTransform();
        var translate = new TranslateTransform();
        var img = new Image
        {
            Source = bmp,
            Width = iw,
            Height = ih,
            Stretch = Stretch.None,
            RenderTransform = new TransformGroup { Children = { scale, translate } }
        };
        canvas.Children.Add(img);

        // 取景框（圆形或方形）
        double frame = Viewport * FrameRatio;
        var frameBorder = new Border
        {
            Width = frame,
            Height = frame,
            BorderBrush = ThemeService.FromHex("#D4A373"),
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,
            CornerRadius = circular ? new CornerRadius(frame / 2) : new CornerRadius(8),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(frameBorder, (Viewport - frame) / 2);
        Canvas.SetTop(frameBorder, (Viewport - frame) / 2);
        canvas.Children.Add(frameBorder);

        // 初始：缩放至刚好放下（留点边距），居中
        // 滑块范围随原图尺寸动态计算：固定 0.1~3 会让超大图/极小图的初始缩放落在范围外，
        // 表现为「刚打开是对的，一动滑块就跳变」。
        double fit = Math.Min(Viewport / iw, Viewport / ih);
        double minZ = Math.Max(0.01, fit * 0.4);
        double maxZ = Math.Min(40, Math.Max(fit * 8, 4));
        double z = Math.Clamp(fit * 0.92, minZ, maxZ);
        double tx = (Viewport - iw * z) / 2;
        double ty = (Viewport - ih * z) / 2;
        ApplyTransform(scale, translate, z, tx, ty);

        var zoom = new Slider
        {
            Minimum = minZ,
            Maximum = maxZ,
            Value = z,
            Width = 220,
            TickFrequency = (maxZ - minZ) / 20,
            IsSnapToTickEnabled = false,
            VerticalAlignment = VerticalAlignment.Center
        };

        // 拖拽平移
        bool dragging = false;
        Point last;
        canvas.MouseLeftButtonDown += (_, e) =>
        {
            dragging = true;
            last = e.GetPosition(canvas);
            canvas.CaptureMouse();
        };
        canvas.MouseLeftButtonUp += (_, _) =>
        {
            dragging = false;
            canvas.ReleaseMouseCapture();
        };
        canvas.MouseMove += (_, e) =>
        {
            if (!dragging) return;
            var p = e.GetPosition(canvas);
            tx += p.X - last.X;
            ty += p.Y - last.Y;
            last = p;
            ApplyTransform(scale, translate, zoom.Value, tx, ty);
        };

        // 缩放统一入口：以锚点（鼠标位置 / 取景框中心）为不动点换算平移，再同步滑块
        bool syncingSlider = false;
        void SetZoom(double nz, double? anchorX, double? anchorY)
        {
            nz = Math.Clamp(nz, minZ, maxZ);
            if (Math.Abs(nz - z) < 0.0001) return;
            double mx = anchorX ?? Viewport / 2.0, my = anchorY ?? Viewport / 2.0;
            tx = mx - (mx - tx) * (nz / z);
            ty = my - (my - ty) * (nz / z);
            z = nz;
            ApplyTransform(scale, translate, z, tx, ty);
            syncingSlider = true;
            zoom.Value = z;
            syncingSlider = false;
        }

        // 鼠标滚轮缩放：锚在指针位置（朝指针处放大 / 缩小），与滑块联动
        canvas.MouseWheel += (_, e) =>
        {
            e.Handled = true;
            var p = e.GetPosition(canvas);
            SetZoom(z * (e.Delta > 0 ? 1.12 : 1 / 1.12), p.X, p.Y);
        };

        // 缩放以取景框中心为锚点：否则会以左上角为锚点，越缩越往一边漂
        zoom.ValueChanged += (_, _) =>
        {
            if (syncingSlider) return;   // 滚轮 / 程序性设值触发的回调不做二次换算
            SetZoom(zoom.Value, null, null);
        };

        // ---- 按钮 ----
        var ok = new Button
        {
            Content = "确定裁剪",
            MinWidth = 96,
            Height = 34,
            Padding = new Thickness(14, 0, 14, 0),
            Foreground = Brushes.White,
            Background = ThemeService.FromHex("#D4A373"),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        var cancel = new Button
        {
            Content = "取消",
            MinWidth = 84,
            Height = 34,
            Padding = new Thickness(14, 0, 14, 0),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand
        };

        var win = new ThemeDialogWindow(circular ? "裁剪头像" : "裁剪壁纸", 420, 680);

        bool result = false;
        string? pickedPath = null;
        ok.Click += (_, _) =>
        {
            if (TryCrop(bmp, zoom.Value, tx, ty, frame, circular, out var outPath, outputDir))
            {
                pickedPath = outPath;
                result = true;
                win.DialogResult = true;
            }
            else
            {
                SimpleDialogs.Alert("裁剪失败，请重试。", "地球Online",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        cancel.Click += (_, _) => win.DialogResult = false;

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "拖拽移动图片 · 滚轮或滑块缩放，框内即最终效果",
            FontSize = 12,
            Foreground = ThemeService.FromHex("#7A7268"),
            Margin = new Thickness(0, 0, 0, 10)
        });
        root.Children.Add(canvas);
        var ctrlRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        ctrlRow.Children.Add(new TextBlock
        {
            Text = "缩放",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Foreground = ThemeService.FromHex("#1E1A16")
        });
        ctrlRow.Children.Add(zoom);
        root.Children.Add(ctrlRow);
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        root.Children.Add(btnRow);
        win.SetBody(root);

        win.ShowDialog();
        croppedPath = pickedPath;
        return result;
    }

    private static void ApplyTransform(ScaleTransform scale, TranslateTransform translate, double z, double tx, double ty)
    {
        scale.ScaleX = z;
        scale.ScaleY = z;
        translate.X = tx;
        translate.Y = ty;
    }

    /// <summary>按当前视图计算源像素裁剪矩形，输出 PNG（无损）。</summary>
    private static bool TryCrop(BitmapImage bmp, double z, double tx, double ty, double frame,
        bool circular, out string? outPath, string? outputDir)
    {
        outPath = null;
        try
        {
            double iw = bmp.PixelWidth;
            double ih = bmp.PixelHeight;

            // 取景框左上角在画布中的坐标
            double fx = (Viewport - frame) / 2;
            double fy = (Viewport - frame) / 2;
            // 对应到源像素坐标
            double sx = (fx - tx) / z;
            double sy = (fy - ty) / z;
            double cw = frame / z;
            double ch = frame / z;

            // 夹取到源图有效范围内
            if (cw >= iw) { sx = 0; cw = iw; }
            else { sx = Math.Clamp(sx, 0, iw - cw); }
            if (ch >= ih) { sy = 0; ch = ih; }
            else { sy = Math.Clamp(sy, 0, ih - ch); }

            int X = (int)Math.Round(sx);
            int Y = (int)Math.Round(sy);
            int W = (int)Math.Round(cw);
            int H = (int)Math.Round(ch);
            W = Math.Max(1, Math.Min(W, (int)iw - X));
            H = Math.Max(1, Math.Min(H, (int)ih - Y));
            if (X < 0) { W += X; X = 0; }
            if (Y < 0) { H += Y; Y = 0; }
            if (W <= 0 || H <= 0) return false;

            var crop = new CroppedBitmap(bmp, new Int32Rect(X, Y, W, H));

            // 头像输出上限 512px：超大原图自动等比压缩（Fant 高质量降采样），
            // 显示端 76px / 44px 圆形仍保有数倍超采样，肉眼无损、文件体积与解码内存可控。
            // 壁纸（circular=false）不受此限，保留全分辨率。
            BitmapSource final = crop;
            if (circular)
            {
                int maxEdge = Math.Max(crop.PixelWidth, crop.PixelHeight);
                if (maxEdge > 512)
                {
                    double k = 512.0 / maxEdge;
                    int tw = Math.Max(1, (int)Math.Round(crop.PixelWidth * k));
                    int th = Math.Max(1, (int)Math.Round(crop.PixelHeight * k));
                    var scaler = new System.Windows.Controls.Image
                    {
                        Source = crop,
                        Stretch = Stretch.Fill,
                        Width = tw,
                        Height = th
                    };
                    RenderOptions.SetBitmapScalingMode(scaler, BitmapScalingMode.Fant);
                    scaler.Measure(new Size(tw, th));
                    scaler.Arrange(new Rect(0, 0, tw, th));
                    var rtb = new RenderTargetBitmap(tw, th, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(scaler);
                    rtb.Freeze();
                    final = rtb;
                }
            }

            var dir = string.IsNullOrWhiteSpace(outputDir)
                ? AppPaths.AvatarDir
                : outputDir;
            Directory.CreateDirectory(dir);
            outPath = Path.Combine(dir, "crop_" + Guid.NewGuid().ToString("N") + ".png");

            var encoder = new PngBitmapEncoder(); // PNG 无损，保留原图画质
            encoder.Frames.Add(BitmapFrame.Create(final));
            using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
            encoder.Save(fs);
            return true;
        }
        catch
        {
            outPath = null;
            return false;
        }
    }
}
