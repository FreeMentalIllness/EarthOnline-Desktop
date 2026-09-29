namespace EarthOnline.Desktop.Services;

/// <summary>
/// 季节限定徽章（v1.0.3，规格照安卓 util/SeasonTheme.kt）。
/// 按四立节气近似切分（立春 2/4、立夏 5/5、立秋 8/7、立冬 11/7）。
/// 桌面端只取名称与 emoji 做点缀徽章，不动全局主题色（暖底是品牌资产）。
/// </summary>
public sealed record Season(string Id, string Label, string Emoji);

public static class SeasonTheme
{
    public static readonly Season Spring = new("spring", "春樱季", "🌸");
    public static readonly Season Summer = new("summer", "夏夜季", "🌙");
    public static readonly Season Autumn = new("autumn", "秋叶季", "🍂");
    public static readonly Season Winter = new("winter", "冬雪季", "❄️");

    public static Season SeasonOf(int month1based, int day) =>
        (month1based == 2 && day >= 4) || month1based is 3 or 4 || (month1based == 5 && day < 5) ? Spring
        : (month1based == 5 && day >= 5) || month1based is 6 or 7 || (month1based == 8 && day < 7) ? Summer
        : (month1based == 8 && day >= 7) || month1based is 9 or 10 || (month1based == 11 && day < 7) ? Autumn
        : Winter;

    public static Season Current()
    {
        var t = DateTime.Today;
        return SeasonOf(t.Month, t.Day);
    }
}
