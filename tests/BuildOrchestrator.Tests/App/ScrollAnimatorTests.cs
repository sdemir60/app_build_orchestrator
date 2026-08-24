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

    /// <summary>
    /// <b>Aynı hedefe İKİNCİ bir hareket, panel arada BAŞKA bir yoldan taşınmışsa da gerçekten kaydırır.</b>
    ///
    /// <para>Kusur burada yaşıyordu ve konsolun <c>← Back</c> jestinde ölçüldü: <see cref="MotionTokens.SplineTo"/>
    /// <c>From</c>'suzdur ve <c>FillBehavior</c> varsayılanı <c>HoldEnd</c>'dir — biten bir animasyon DP'yi son
    /// hedefinde TUTMAYA devam eder. Panel sonra başka bir yoldan (doküman takası, <c>ScrollToVerticalOffset</c>)
    /// taşınırsa DP hâlâ eski hedefi tutar; <see cref="ScrollAnimator.AnimateTo"/>'nun taban tohumlaması o tutulan
    /// değerin ALTINDA kalır ve aynı hedefe açılan yeni animasyon efektif değeri hiç değiştirmez —
    /// <c>OnVerticalOffsetChanged</c> ateşlenmez, <c>ScrollToVerticalOffset</c> hiç çağrılmaz, panel yerinde
    /// kalır. Sahada bu, "geri dedim, hiçbir şey olmadı, metnin başında kaldı" olarak görülüyordu.</para>
    ///
    /// <para>Kural: <c>AnimateTo</c> her zaman ÇAĞIRANIN bildirdiği gerçek konumdan başlar. Hedefin bir öncekiyle
    /// aynı olması hareketi iptal etmez.</para>
    /// </summary>
    [StaFact]
    public void A_second_move_to_the_same_target_still_scrolls_when_the_panel_moved_by_another_path()
    {
        var sv = NewLiveScrollViewer();
        var spline = new KeySpline(0.65, 0, 0.35, 1);
        var duration = TimeSpan.FromMilliseconds(30);

        ScrollAnimator.AnimateTo(sv, 0, 500, animationsEnabled: true, duration, spline);
        DispatcherPump.PumpUntil(() => sv.VerticalOffset >= 499.5, TimeSpan.FromSeconds(3));
        Assert.InRange(sv.VerticalOffset, 499.5, 500.5); // ön-koşul: ilk hareket gerçekten oldu

        // Panel ScrollAnimator'ın DP'sinden GEÇMEYEN bir yolla başa döndü (konsolda: doküman takası + pin).
        sv.ScrollToVerticalOffset(0);
        sv.UpdateLayout();
        Assert.Equal(0, sv.VerticalOffset);

        ScrollAnimator.AnimateTo(sv, sv.VerticalOffset, 500, animationsEnabled: true, duration, spline);
        DispatcherPump.PumpUntil(() => sv.VerticalOffset >= 499.5, TimeSpan.FromSeconds(3));

        Assert.InRange(sv.VerticalOffset, 499.5, 500.5);
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

    /// <summary>
    /// <b>İptal, paneli animasyonun BIRAKTIĞI yerde bırakır — hareketin BAŞLADIĞI yere geri atmaz.</b>
    ///
    /// <para>Kusur sahada şöyle görülüyordu: konsolda tekerlekle yukarı çıkılır, <c>⌄ latest</c> ile dibe
    /// inilir, sonra tekrar tekerleğe dokunulur — panel dipten değil, pill'e basmadan ÖNCEKİ konumdan devam
    /// ederdi. Sebep: <see cref="ScrollAnimator.AnimateTo"/> DP'nin TABAN değerini hareketin başlangıç
    /// noktasına tohumlar, animasyon ise <c>HoldEnd</c> ile efektif değeri hedefte tutar. <c>BeginAnimation
    /// (prop, null)</c> o tutmayı kaldırınca efektif değer TABANA — yani eski konuma — düşer,
    /// <c>OnVerticalOffsetChanged</c> ateşlenir ve panel oraya kaydırılır.</para>
    ///
    /// <para>Kural: iptal bir <b>bırakma</b>dır, geri alma değil. Kullanıcı tekerleğe dokunduğu anda panel
    /// nerede duruyorsa orada kalır ve tekerlek oradan devam eder.</para>
    /// </summary>
    [StaFact]
    public void CancelForUser_leaves_the_panel_where_the_finished_animation_put_it()
    {
        var sv = NewLiveScrollViewer();

        ScrollAnimator.AnimateTo(sv, 0, 500, animationsEnabled: true, TimeSpan.FromMilliseconds(30), new KeySpline(0.65, 0, 0.35, 1));
        DispatcherPump.PumpUntil(() => sv.VerticalOffset >= 499.5, TimeSpan.FromSeconds(3));
        Assert.InRange(sv.VerticalOffset, 499.5, 500.5); // ön-koşul: hareket gerçekten hedefe vardı

        ScrollAnimator.CancelForUser(sv);
        sv.UpdateLayout();

        Assert.InRange(sv.VerticalOffset, 499.5, 500.5);
        Assert.InRange(ScrollAnimator.GetVerticalOffset(sv), 499.5, 500.5); // taban da varılan yeri taşır
    }

    /// <summary>
    /// Aynı kural hareket UÇUŞTAYKEN de geçerli: kullanıcı animasyonun ortasında tekerleğe dokunursa panel o
    /// ana kadar geldiği yerde durur, başlangıca geri sarmaz (bkz. kardeş test — aynı kök neden, taban değeri
    /// eski konumu taşıdığı için).
    /// </summary>
    [StaFact]
    public void CancelForUser_mid_flight_leaves_the_panel_at_the_position_it_had_reached()
    {
        var sv = NewLiveScrollViewer();

        ScrollAnimator.AnimateTo(sv, 0, 500, animationsEnabled: true, TimeSpan.FromMilliseconds(600), new KeySpline(0.65, 0, 0.35, 1));
        DispatcherPump.PumpUntil(() => sv.VerticalOffset >= 50, TimeSpan.FromSeconds(3));
        double reached = sv.VerticalOffset;
        Assert.True(reached >= 50, $"ön-koşul: animasyon ilerlemeliydi, offset={reached}");

        ScrollAnimator.CancelForUser(sv);
        sv.UpdateLayout();

        Assert.True(sv.VerticalOffset >= reached - 1, $"iptal geri sardı: {reached} → {sv.VerticalOffset}");
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
