namespace EarthOnline.Desktop.Services;

/// <summary>
/// 连续记录成长机制（v1.0.3，规格照安卓 util/GrowthStreak.kt）。
///
/// 口径：一天内留下任何一种记录（世界日志 / 完成任务 / 足迹）都算「记录日」。
/// 对用户不惩罚 —— 今天还没记录不断档（昨天连着就算），断更只鼓励回归、不清空历史荣誉。
/// </summary>
public static class GrowthStreak
{
    /// <summary>当前连续记录天数。今天没有记录时从昨天起算（今天还没过完，不算断）。</summary>
    public static int CurrentStreak(IReadOnlySet<string> dayKeys, DateTime? today = null)
    {
        if (dayKeys.Count == 0) return 0;
        var day = (today ?? DateTime.Today).Date;
        // 今天有记录从今天数；今天还没有则从昨天数（宽限今天）
        var cursor = dayKeys.Contains(day.ToString("yyyy-MM-dd")) ? day : day.AddDays(-1);
        int streak = 0;
        while (dayKeys.Contains(cursor.ToString("yyyy-MM-dd")))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }
        return streak;
    }

    /// <summary>
    /// 生长阶段（界面随投入逐渐生长）：
    /// 0 无记录 · 1 萌芽(≥3天) · 2 抽枝(≥7天) · 3 繁茂(≥21天) · 4 参天(≥60天)
    /// 阈值刻意低 —— 3 天就有可见变化，别让用户等一周。
    /// </summary>
    public static int Stage(int streakDays) => streakDays switch
    {
        >= 60 => 4,
        >= 21 => 3,
        >= 7 => 2,
        >= 3 => 1,
        _ => 0
    };

    /// <summary>阶段名（主页时间轴卡展示）</summary>
    public static string StageLabel(int stage) => stage switch
    {
        4 => "参天 🌳",
        3 => "繁茂 🌿",
        2 => "抽枝 ✨",
        1 => "萌芽 🌱",
        _ => ""
    };

    /// <summary>
    /// 回归鼓励语（不惩罚）：距上次记录已断 N 天时给一句温和的邀请；
    /// 从未记录或未断更（gap &lt; 2）返回 null。
    /// </summary>
    public static string? ComebackMessage(IReadOnlySet<string> dayKeys, DateTime? today = null)
    {
        if (dayKeys.Count == 0) return null;
        var t = (today ?? DateTime.Today).Date;

        int? gap = null;
        foreach (var k in dayKeys)
        {
            if (k.Length != 10 || !DateTime.TryParseExact(k, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var d)) continue;
            var g = (int)(t - d).TotalDays;
            if (g >= 0 && (gap is null || g > gap)) gap = g;
        }
        if (gap is null || gap < 2) return null;
        return $"有 {gap} 天没写日记了。回来继续，你的星球一直在。";
    }
}
