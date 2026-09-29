namespace EarthOnline.Desktop.Services;

/// <summary>
/// 经验值（XP）换算规则 —— 三端唯一口径（对应安卓 util/XpRules.kt v1.0.3）。
/// 改这里会同时影响报告页与将来的小组件，务必一起看。
/// 所有展示 XP 的地方统一走 <see cref="TotalXp"/>，不要再手写乘加表达式。
/// </summary>
public static class XpRules
{
    public const int TASK_DONE = 10;
    public const int ACHIEVEMENT = 50;
    public const int MEMO = 5;
    public const int LOCATION = 8;

    /// <summary>v1.0.3：背包每拾取一件物品</summary>
    public const int ITEM = 3;

    /// <summary>v1.0.3：记忆相册每导入一张照片（桌面端暂无此来源，调用方传 0）</summary>
    public const int PHOTO = 2;

    /// <summary>
    /// 累计经验总值。所有端共用这一个函数。
    /// items / photos 允许缺省：调用方拿不到这两项计数时传 0。
    /// </summary>
    public static int TotalXp(int tasksDone, int achievements, int memos, int locations,
        int items = 0, int photos = 0)
        => tasksDone * TASK_DONE
         + achievements * ACHIEVEMENT
         + memos * MEMO
         + locations * LOCATION
         + items * ITEM
         + photos * PHOTO;

    /// <summary>
    /// 连续记录加成（v1.0.3）：连续记录天数带来的额外经验。
    /// 阈值刻意放低 —— 3 天就有第一笔奖励，不要让用户等到一周之后才感受到成长。
    /// </summary>
    public static int StreakBonus(int streakDays) => streakDays switch
    {
        >= 60 => 120,
        >= 30 => 50,
        >= 14 => 30,
        >= 7 => 20,
        >= 3 => 5,
        _ => 0
    };

    /// <summary>
    /// 等级称号（v1.0.3）：简单化 —— 不搞花哨命名，用户没自定义时一律显示「旅行者」；
    /// 设置过自定义称号则完全以用户的为准。
    /// </summary>
    public const string DEFAULT_TITLE = "旅行者";

    public static string TitleFor(string? custom)
    {
        var t = (custom ?? "").Trim();
        if (t.Length == 0) return DEFAULT_TITLE;
        return t.Length <= 12 ? t : t[..12];
    }
}
