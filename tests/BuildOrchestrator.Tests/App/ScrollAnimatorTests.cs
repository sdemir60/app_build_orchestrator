using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T59] ScrollAnimator — attached DP <c>VerticalOffset</c> + <c>DoubleAnimationUsingKeyFrames</c>
/// (Foundation süre/KeySpline) bir ScrollViewer'ı hedefe kaydırır; wheel/kullanıcı scroll ANINDA iptal
/// (<c>BeginAnimation(prop, null)</c>) + suppress bayrağı; reduced-motion → ANINDA (animasyonsuz).
///
/// <para><b>Neden gerçek (ekran dışı) Window:</b> doğrulandı — WPF'in animasyon clock'u yalnızca bir
/// PresentationSource'a bağlı bir Visual'de çalışır; bağlı olmayan bir kontrolde BeginAnimation SESSİZCE etkisiz
/// kalır (değer hiç değişmez, IsAnimated hep false). Bkz. <see cref="AnimationHost"/>.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ScrollAnimatorTests
{
    private static ScrollViewer NewLiveScrollViewer(double contentHeight = 3000, double viewport = 200)
    {
        var sv = new ScrollViewer { Content = new Border { Height = contentHeight, Width = 100 } };
        AnimationHost.ShowOffscreen(sv, width: 100, height: viewport);
        return sv;
    }

    // ---------------------------------------------------------------- BuildAnimation (saf fabrika, WPF clock'u YOK)

    [Fact]
    public void BuildAnimation_targets_the_given_value_duration_and_keyspline()
    {
        var spline = new KeySpline(0.65, 0, 0.35, 1);
        var duration = TimeSpan.FromMilliseconds(280);

        var animation = ScrollAnimator.BuildAnimation(to: 640, duration, spline);

        var frame = Assert.IsType<SplineDoubleKeyFrame>(Assert.Single(animation.KeyFrames));
        Assert.Equal(640, frame.Value);
        Assert.Equal(duration, frame.KeyTime.TimeSpan);
        Assert.Same(spline, frame.KeySpline);
    }

    // ---------------------------------------------------------------- AnimateTo — reduced-motion / instant paths (clock YOK)

    [StaFact]
    public void AnimateTo_with_animations_disabled_jumps_instantly_with_no_clock_attached()
    {
        var sv = new ScrollViewer { Content = new Border { Height = 3000, Width = 100 } };
        sv.Measure(new Size(100, 200));
        sv.Arrange(new Rect(0, 0, 100, 200));

        bool animated = ScrollAnimator.AnimateTo(sv, currentOffset: 0, targetOffset: 500,
            animationsEnabled: false, effectiveDuration: TimeSpan.FromMilliseconds(180), new KeySpline(0.65, 0, 0.35, 1));

        Assert.False(animated);
        Assert.Equal(500, ScrollAnimator.GetVerticalOffset(sv));
        Assert.False(DependencyPropertyHelper.GetValueSource(sv, ScrollAnimator.VerticalOffsetProperty).IsAnimated);
    }

    [StaFact]
    public void AnimateTo_with_a_zero_effective_duration_jumps_instantly_reduced_motion_via_Effective()
    {
        var sv = new ScrollViewer { Content = new Border { Height = 3000, Width = 100 } };
        sv.Measure(new Size(100, 200));
        sv.Arrange(new Rect(0, 0, 100, 200));

        // MotionSettings.Effective(token) döner TimeSpan.Zero iken (reduced-motion) — süre sıfırsa da anında atlar.
        bool animated = ScrollAnimator.AnimateTo(sv, 0, 500, animationsEnabled: true, effectiveDuration: TimeSpan.Zero,
            new KeySpline(0.65, 0, 0.35, 1));

        Assert.False(animated);
        Assert.Equal(500, ScrollAnimator.GetVerticalOffset(sv));
    }

    // ---------------------------------------------------------------- AnimateTo — gerçek (yaşayan) clock

    [StaFact]
    public void AnimateTo_with_animations_enabled_reaches_the_target_value_once_the_clock_completes()
    {
        var sv = NewLiveScrollViewer();

        bool animated = ScrollAnimator.AnimateTo(sv, 0, 500, animationsEnabled: true, TimeSpan.FromMilliseconds(30), new KeySpline(0.65, 0, 0.35, 1));
        Assert.True(animated);

        DispatcherPump.PumpUntil(() => ScrollAnimator.GetVerticalOffset(sv) >= 499.5, TimeSpan.FromSeconds(3));

        Assert.InRange(ScrollAnimator.GetVerticalOffset(sv), 499.5, 500.5);
        Assert.True(DependencyPropertyHelper.GetValueSource(sv, ScrollAnimator.VerticalOffsetProperty).IsAnimated);
    }

    [StaFact]
    public void CancelForUser_removes_the_active_animation_and_sets_the_suppressed_flag()
    {
        var sv = NewLiveScrollViewer();
        ScrollAnimator.AnimateTo(sv, 0, 500, animationsEnabled: true, TimeSpan.FromMilliseconds(500), new KeySpline(0.65, 0, 0.35, 1));
        // Clock'un property sistemine "yaşıyor" olarak yansıması bir compositor tick'i ister (doğrulandı) —
        // gerçek koşuda bu kaç frame içinde olur; testte kısa bir pompayla bekleriz (sleep-tahmini DEĞİL, koşul-tabanlı).
        DispatcherPump.PumpUntil(() => DependencyPropertyHelper.GetValueSource(sv, ScrollAnimator.VerticalOffsetProperty).IsAnimated, TimeSpan.FromSeconds(2));
        Assert.True(DependencyPropertyHelper.GetValueSource(sv, ScrollAnimator.VerticalOffsetProperty).IsAnimated);
        Assert.False(ScrollAnimator.GetIsUserSuppressed(sv));

        ScrollAnimator.CancelForUser(sv);

        Assert.False(DependencyPropertyHelper.GetValueSource(sv, ScrollAnimator.VerticalOffsetProperty).IsAnimated);
        Assert.True(ScrollAnimator.GetIsUserSuppressed(sv));
    }

    [StaFact]
    public void AnimateTo_clears_a_prior_user_suppression_a_new_programmatic_move_is_no_longer_fighting_the_user()
    {
        var sv = NewLiveScrollViewer();
        ScrollAnimator.AnimateTo(sv, 0, 500, true, TimeSpan.FromMilliseconds(500), new KeySpline(0.65, 0, 0.35, 1));
        ScrollAnimator.CancelForUser(sv);
        Assert.True(ScrollAnimator.GetIsUserSuppressed(sv));

        ScrollAnimator.AnimateTo(sv, 0, 200, true, TimeSpan.FromMilliseconds(500), new KeySpline(0.65, 0, 0.35, 1));

        Assert.False(ScrollAnimator.GetIsUserSuppressed(sv));
    }

    [StaFact]
    public void EnableUserCancellation_wires_PreviewMouseWheel_to_CancelForUser()
    {
        var sv = NewLiveScrollViewer();
        ScrollAnimator.AnimateTo(sv, 0, 500, true, TimeSpan.FromMilliseconds(500), new KeySpline(0.65, 0, 0.35, 1));
        DispatcherPump.PumpUntil(() => DependencyPropertyHelper.GetValueSource(sv, ScrollAnimator.VerticalOffsetProperty).IsAnimated, TimeSpan.FromSeconds(2));
        ScrollAnimator.EnableUserCancellation(sv);

        sv.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, 120)
        { RoutedEvent = UIElement.PreviewMouseWheelEvent });

        Assert.False(DependencyPropertyHelper.GetValueSource(sv, ScrollAnimator.VerticalOffsetProperty).IsAnimated);
        Assert.True(ScrollAnimator.GetIsUserSuppressed(sv));
    }
}
