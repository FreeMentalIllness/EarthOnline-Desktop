using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace EarthOnline.Desktop.Services;

/// <summary>
/// 滚动条自动隐藏行为（通过 App.xaml 的隐式 ScrollBar 样式全局附加）：
///  - 初始 / 静止时透明淡出（保留命中区，鼠标移到滚动条位置即可唤出）；
///  - 滚动（拖动、滚轮、翻页，所有会改 Value 的操作都会触发 Scroll 事件）时淡入；
///  - 静止约 1.2 秒后自动淡出；鼠标悬停在滚动条上时保持可见。
/// 不引入任何依赖，纯 WPF 附加属性实现。
/// </summary>
public static class AutoHideScrollBar
{
    /// <summary>淡入 / 淡出动画时长。</summary>
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(180));

    /// <summary>滚动停止后延迟淡出的时间。</summary>
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(1200);

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(AutoHideScrollBar),
            new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool v) => d.SetValue(EnabledProperty, v);

    /// <summary>防重复附加标记（隐式样式可能对同一实例多次生效）。</summary>
    private static readonly DependencyProperty AttachedProperty =
        DependencyProperty.RegisterAttached("Attached", typeof(bool), typeof(AutoHideScrollBar),
            new PropertyMetadata(false));

    /// <summary>每条滚动条自己的延迟隐藏计时器。</summary>
    private static readonly DependencyProperty TimerProperty =
        DependencyProperty.RegisterAttached("Timer", typeof(DispatcherTimer), typeof(AutoHideScrollBar),
            new PropertyMetadata(null));

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollBar sb && (bool)e.NewValue) Attach(sb);
    }

    private static void Attach(ScrollBar sb)
    {
        if ((bool)sb.GetValue(AttachedProperty)) return;
        sb.SetValue(AttachedProperty, true);

        // 初始隐藏：不滚动就完全不干扰视觉
        sb.Opacity = 0;

        sb.Scroll += (_, _) => { Show(sb); ScheduleHide(sb); };
        sb.MouseEnter += (_, _) => { Show(sb); CancelHide(sb); };
        sb.MouseLeave += (_, _) => ScheduleHide(sb);
        sb.Unloaded += (_, _) => CancelHide(sb);
    }

    private static void Show(ScrollBar sb)
        => sb.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, FadeDuration));

    private static void ScheduleHide(ScrollBar sb)
    {
        var timer = (DispatcherTimer?)sb.GetValue(TimerProperty);
        if (timer is null)
        {
            timer = new DispatcherTimer { Interval = IdleDelay };
            timer.Tick += (_, _) =>
            {
                ((DispatcherTimer)sb.GetValue(TimerProperty)!).Stop();
                if (!sb.IsMouseOver)
                    sb.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, FadeDuration));
            };
            sb.SetValue(TimerProperty, timer);
        }
        timer.Stop();
        timer.Start();
    }

    private static void CancelHide(ScrollBar sb)
        => ((DispatcherTimer?)sb.GetValue(TimerProperty))?.Stop();
}
