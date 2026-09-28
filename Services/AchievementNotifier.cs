using EarthOnline.Desktop.Dialogs;

namespace EarthOnline.Desktop.Services;

/// <summary>
/// 成就判定 + Steam 风格悬浮通知的一站式入口。
/// 业务页（任务/背包/主页等）写库后调 Check()，取代裸调 AchievementEngine.Evaluate()。
/// </summary>
public static class AchievementNotifier
{
    /// <summary>判定成就，有新解锁时右下角弹悬浮通知（含提示音）。</summary>
    public static void Check()
    {
        try
        {
            var (newly, titles) = AchievementEngine.EvaluateDetailed();
            if (newly <= 0) return;

            if (titles.Count == 1)
            {
                UnlockToast.Show(titles[0]);
            }
            else
            {
                var shown = string.Join("、", titles.Take(3)) + (titles.Count > 3 ? "…" : "");
                UnlockToast.Show($"新解锁 {newly} 条成就", shown);
            }
        }
        catch
        {
            // 通知失败不影响业务；下次进入成就页仍会正确展示
        }
    }
}
