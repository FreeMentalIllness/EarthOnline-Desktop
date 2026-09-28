namespace EarthOnline.Desktop.Services;

/// <summary>
/// 人生等级（与安卓 LifeStats.kt / 网页主页口径一致）：
/// Lv = 周岁；经验条 = 距下一个生日的天数进度（网页口径）。
/// </summary>
public static class LifeStats
{
    /// <summary>生日 → (周岁 Lv, 距下一级天数, 经验进度 0~100, 是否有生日)。</summary>
    public static (int Age, int DaysToNext, int Progress, bool HasBirth) Compute(
        string? birthDate, DateTime? today = null)
    {
        var now = today ?? DateTime.Today;
        if (string.IsNullOrWhiteSpace(birthDate) || !DateTime.TryParse(birthDate, out var birth))
        {
            return (0, 0, 0, false);
        }
        birth = birth.Date;

        int age = now.Year - birth.Year;
        if (now < birth.AddYears(age)) age--;          // 今年生日还没过
        if (age < 0) return (0, 0, 0, false);

        var nextBirthday = birth.AddYears(age);
        if (nextBirthday <= now) nextBirthday = birth.AddYears(age + 1);
        int daysToNext = (int)(nextBirthday - now).TotalDays;

        // 上一个生日 = nextBirthday 减一年；进度 = 已过去天数 / 两个生日间隔天数
        var lastBirthday = nextBirthday.AddYears(-1);
        int span = (int)(nextBirthday - lastBirthday).TotalDays;
        int passed = (int)(now - lastBirthday).TotalDays;
        int progress = span <= 0 ? 0 : Math.Clamp((int)Math.Round(passed * 100.0 / span), 0, 100);

        return (age, daysToNext, progress, true);
    }

    /// <summary>已存活天数（安卓小组件/主页口径）。</summary>
    public static int DaysLived(string? birthDate, DateTime? today = null)
    {
        var now = today ?? DateTime.Today;
        if (string.IsNullOrWhiteSpace(birthDate) || !DateTime.TryParse(birthDate, out var birth))
        {
            return 0;
        }
        return Math.Max(0, (int)(now.Date - birth.Date).TotalDays);
    }
}
