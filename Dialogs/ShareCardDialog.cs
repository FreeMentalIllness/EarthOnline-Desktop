using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Services;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Dialogs;

/// <summary>
/// 分享人生卡（v1.0.5）：等级 / 连续记录 / 本月关键词 / 签名 → 渲染成 PNG 长图。
/// 颜色全部经 ThemeService（深浅主题各自成图），保存到用户选择的路径。
/// </summary>
public static class ShareCardDialog
{
    public static void Show()
    {
        try
        {
            // ---- 组数据 ----
            using var db = new AppDbContext(AppPaths.DbFile);
            var profile = db.Profile.FirstOrDefault();
            var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).ToString("yyyy-MM-dd");
            var doneThisMonth = db.Tasks.AsNoTracking().Count(t => t.Status == "done" && t.DoneAt != null && string.Compare(t.DoneAt, monthStart, StringComparison.Ordinal) >= 0);
            var memoThisMonth = db.Memos.AsNoTracking().Count(m => string.Compare(m.CreatedAt, monthStart, StringComparison.Ordinal) >= 0);

            // 连续记录：当日键集走 CurrentStreak（与主页同口径）
            var dayKeys = new HashSet<string>();
            foreach (var t in db.Tasks.AsNoTracking().Where(t => t.DoneAt != null).ToList())
                if (DateTime.TryParse(t.DoneAt, out var dt)) dayKeys.Add(dt.Date.ToString("yyyy-MM-dd"));
            foreach (var m in db.Memos.AsNoTracking().ToList())
                if (DateTime.TryParse(m.CreatedAt, out var mt)) dayKeys.Add(mt.Date.ToString("yyyy-MM-dd"));
            var streak = GrowthStreak.CurrentStreak(dayKeys);

            var name = string.IsNullOrWhiteSpace(profile?.Name) ? "无名玩家" : profile!.Name;
            var days = 0;
            if (DateTime.TryParse(profile?.BirthDate, out var birth))
                days = (DateTime.Today - birth.Date).Days;
            var levelText = days > 0 ? $"Lv.{DateTime.Today.Year - birth.Year - (DateTime.Today < birth.AddYears(DateTime.Today.Year - birth.Year) ? 1 : 0)}" : "未设置";

            // 本月关键词：日志里出现频次最高的 2~4 字词（超简版：取高频词元的启发式太重，改用本月概览文案）
            var keyword = $"✅ {doneThisMonth} 个任务 · 📝 {memoThisMonth} 条日志";

            // ---- 渲染 ----
            const double w = 720, h = 900;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var bg = ThemeService.Brush("CardBgBrush");
                var accent = ThemeService.Brush("AccentBrush");
                var main = ThemeService.Brush("TextPrimaryBrush");
                var sub = ThemeService.Brush("TextSecondaryBrush");

                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
                // 圆角底
                dc.DrawRoundedRectangle(bg, new Pen(ThemeService.Brush("BorderBrush"), 2),
                    new Rect(16, 16, w - 32, h - 32), 24, 24);
                // 顶部琥珀条
                dc.DrawRoundedRectangle(accent, null, new Rect(16, 16, w - 32, 8), 4, 4);

                void Text(string s, double x, double y, double size, Brush brush, bool bold = false)
                {
                    var ft = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Microsoft YaHei UI"),
                            FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
                        size, brush, 1.25);
                    dc.DrawText(ft, new Point(x, y));
                }

                Text("🌍 地球Online", 56, 64, 22, sub);
                Text(name, 56, 120, 44, main, bold: true);
                Text($"生存第 {days:N0} 天", 56, 186, 18, sub);

                // 三块指标
                double y = 300;
                Text("等级", 56, y, 15, sub); Text(levelText, 260, y - 10, 26, main, bold: true);
                Text("连续记录", 56, y + 80, 15, sub); Text($"{streak} 天", 260, y + 70, 26, main, bold: true);
                Text("本月节奏", 56, y + 160, 15, sub); Text(keyword, 260, y + 152, 20, main);

                // 分隔线 + 签名
                dc.DrawLine(new Pen(ThemeService.Brush("BorderBrush"), 1), new Point(56, y + 250), new Point(w - 56, y + 250));
                var sign = SettingsStore.Load().CustomTitle ?? "";
                Text(string.IsNullOrWhiteSpace(sign) ? "在地球Online，认真过好每一天。" : sign, 56, y + 280, 17, sub);
                Text(DateTime.Today.ToString("yyyy 年 MM 月 dd 日"), 56, h - 90, 14, ThemeService.Brush("TextMutedBrush"));
            }

            var rtb = new RenderTargetBitmap((int)(w * 1.5), (int)(h * 1.5), 144, 144, PixelFormats.Pbgra32);
            rtb.Render(visual);

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存人生卡",
                Filter = "PNG 图片|*.png",
                FileName = $"EarthOnline_人生卡_{DateTime.Today:yyyyMMdd}.png",
            };
            if (dlg.ShowDialog() != true) return;

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(dlg.FileName);
            enc.Save(fs);

            SimpleDialogs.Alert("人生卡已保存：\n" + dlg.FileName, "地球Online · 分享人生卡",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("生成失败：" + ex.Message, "地球Online", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
