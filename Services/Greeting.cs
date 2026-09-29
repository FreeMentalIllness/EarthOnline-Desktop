namespace EarthOnline.Desktop.Services;

/// <summary>
/// 动态问候语（v1.0.3，规格照安卓 util/Greeting.kt）。
/// 按时间段切换，并感知用户最近的心情记录 —— 让主页开口说第一句话。
/// 全部本地规则，无网络依赖。
/// </summary>
public static class Greeting
{
    /// <summary>
    /// </summary>
    /// <param name="hour">当前小时（0-23）</param>
    /// <param name="latestMoodText">最近一条「心情」类日志文本（可为 null）；命中关键词时换成关心式文案</param>
    /// <param name="streakDays">连续记录天数（&gt;=3 时附上认可）</param>
    public static string Build(int hour, string? latestMoodText, int streakDays)
    {
        var baseLine = hour switch
        {
            >= 0 and <= 4 => "夜深了，早点休息",
            >= 5 and <= 8 => "早上好，新的一天开始了",
            >= 9 and <= 11 => "上午好，今天想推进点什么？",
            12 or 13 => "午安，记得吃口饭",
            >= 14 and <= 17 => "下午好，慢慢来也可以",
            >= 18 and <= 22 => "晚上好，今天辛苦了",
            _ => "夜深了，今天辛苦了"
        };

        var moodLine = MoodCareLine(latestMoodText);
        var streakLine = streakDays >= 3 ? $"已连续记录 {streakDays} 天 🔥" : null;
        return string.Join(" · ", new[] { baseLine, moodLine, streakLine }.Where(s => s is not null));
    }

    /// <summary>最近心情里带着雨/累/低落等词时，多一句关心（正面心情不画蛇添足）</summary>
    private static string? MoodCareLine(string? moodText)
    {
        var t = moodText?.Trim();
        if (string.IsNullOrEmpty(t)) return null;

        string[] lowKeyWords = { "累", "烦", "低落", "难过", "焦虑", "压力", "emo", "丧", "哭", "失眠" };
        string[] rainWords = { "雨", "阴", "降温" };
        if (lowKeyWords.Any(t.Contains)) return "无论晴雨，你的记录都在";
        if (rainWords.Any(t.Contains)) return "外面天气一般，愿心里有光";
        return null;
    }
}
