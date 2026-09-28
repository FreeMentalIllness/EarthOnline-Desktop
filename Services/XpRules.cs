namespace EarthOnline.Desktop.Services;

/// <summary>
/// 经验值（XP）换算规则 —— 三端唯一口径（对应安卓 util/XpRules.kt）。
/// 改这里会同时影响报告页与将来的小组件，务必一起看。
/// </summary>
public static class XpRules
{
    public const int TASK_DONE = 10;
    public const int ACHIEVEMENT = 50;
    public const int MEMO = 5;
    public const int LOCATION = 8;

    public static int Of(int tasksDone, int achievements, int memos, int locations)
        => tasksDone * TASK_DONE
         + achievements * ACHIEVEMENT
         + memos * MEMO
         + locations * LOCATION;
}
