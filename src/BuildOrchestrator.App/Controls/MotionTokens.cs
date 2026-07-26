using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T59] Foundation <c>Duration.*</c>/<c>KeySpline.*</c> kaynaklarını bir <see cref="FrameworkElement"/> üzerinden
/// çözen TEK paylaşımlı yardımcı (motion sözleşmesi: "tokens/durations consumed by key — no hardcoded ms/hex").
///
/// <para><b>Neden burada:</b> <see cref="Console.ConsoleView"/>'ın önceki (T56/3b) private <c>ResolveDuration</c>
/// metodunun BİREBİR aynı deseni (<c>TryFindResource</c> + fallback literal) — T59'un ScrollAnimator/BottomAnchor/
/// FollowScroll/LatestPill kablajı da AYNI ihtiyacı duyduğundan buraya çıkarılıp PAYLAŞILDI (kopya YASAK,
/// CLAUDE.md). ConsoleView artık bunu çağırır; davranış DEĞİŞMEDİ (aynı TryFindResource + aynı fallback deseni).</para>
/// </summary>
internal static class MotionTokens
{
    /// <summary>İmleç blink periyodu (design-v1 §2.5: "1.0→0.1, 0.55s"). Adlandırılmış sabit — süreyi çağrı
    /// yerinde literal yazmak YASAK (<c>StatusGlyph.PulseMs</c> / <c>BuildingSpinner.RotationMs</c> deseni;
    /// guard: <c>NoHardcodedMotionTests</c>). Duration.* token ailesine ait DEĞİLDİR: bu süre effects.css'te
    /// yoktur, §2.5'e özgüdür.</summary>
    internal const double BlinkMs = 550.0;

    public static Duration ResolveDuration(FrameworkElement host, string key, double fallbackMs)
        => host.TryFindResource(key) is Duration d ? d : new Duration(TimeSpan.FromMilliseconds(fallbackMs));

    /// <summary>[3b M-4 · D3 §3] Aktif-satır / build-in-progress / event-stream imleçlerinin ORTAK blink
    /// animasyonu (design-v1 §2.5: 1.0→0.1, 0.55s, SineEase in/out, 30fps, sonsuz). Tek kaynak — üç başlatıcı
    /// (<see cref="Console.ConsoleView"/> StartBlink/StartBuildBlink + <see cref="Views.EventStreamView"/>
    /// StartCursorBlink) bunu paylaşır (verbatim kopya YASAK, CLAUDE.md).</summary>
    public static DoubleAnimation CreateBlinkAnimation()
    {
        var blink = new DoubleAnimation(1.0, 0.1, new Duration(TimeSpan.FromMilliseconds(BlinkMs)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(blink, 30);
        return blink;
    }

    public static KeySpline ResolveKeySpline(FrameworkElement host, string key, KeySpline fallback)
        => host.TryFindResource(key) is KeySpline k ? k : fallback;

    /// <summary>
    /// From'SUZ, tek <see cref="SplineDoubleKeyFrame"/>'lik "hedefe git" animasyonu — CSS <c>transition</c>
    /// paritesinin WPF karşılığı: <c>HandoffBehavior.SnapshotAndReplace</c> ile başlatıldığında uçuştaki bir
    /// animasyonun O ANKİ değerinden devam eder (retarget), tıpkı CSS'in yeni bir hedefe geçişi gibi. WPF'in düz
    /// <c>DoubleAnimation</c>'ı bir <c>KeySpline</c> alamadığı için keyframe biçimi kullanılır.
    ///
    /// <para>[T63] <see cref="ScrollAnimator.BuildAnimation"/> (T59, scroll offset) ve <c>GraphView</c>'ın kamera
    /// transform'u BİREBİR aynı şekle ihtiyaç duyar — tek tanım burada (kopya YASAK, CLAUDE.md).</para>
    /// </summary>
    public static DoubleAnimationUsingKeyFrames SplineTo(double to, TimeSpan duration, KeySpline keySpline)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration), keySpline));
        return animation;
    }

    /// <summary>
    /// [T60] CSS <c>transition: &lt;renk&gt; var(--duration-fast) var(--ease-standard)</c> paritesi — DS'in
    /// TÜM 120ms renk geçişlerinin TEK yolu (Button/Chip/IconButton/Segment/Switch/Input ve
    /// <see cref="LatestPill"/> aynı metodu çağırır; kopya YASAK, CLAUDE.md).
    ///
    /// <para><b>A13.2 — hedef ZORUNLU olarak template-lokal bir brush'tır:</b> Tokens.xaml'deki brush'lar
    /// PAYLAŞILIR ve donmuştur; onları animate etmek hem imkânsızdır hem de tüm tüketicileri etkilerdi.
    /// Çağıran, kendi (donmamış) kopyasını verir — bkz. <see cref="DsTransition"/>.</para>
    ///
    /// <para><b>Neden kod-tarafı (T60 Step 1 kararı):</b> saf-XAML yolu ÖLÇÜLDÜ ve kapalı çıktı —
    /// <c>ControlTemplate.Triggers</c> içindeki bir <c>Storyboard</c> şablon mühürlenirken (Seal) DONDURULMAK
    /// ZORUNDADIR ve <c>{DynamicResource Duration.Fast}</c> bunu imkânsız kılar (InvalidOperationException:
    /// "Bu Storyboard zaman çizelgesi ağacı iş parçacıkları arasında kullanılmak üzere dondurulamıyor").
    /// Kanıt testleri: MotionResourcesTests'teki iki spike.</para>
    ///
    /// <para>Süre/eğri ve <c>AnimationsEnabled</c> BAŞLATMA ANINDA taze okunur (motion sözleşmesi);
    /// <see cref="HandoffBehavior.SnapshotAndReplace"/> uçuştaki bir geçişi O ANKİ renginden devraldırır
    /// (CSS'in yeni bir hedefe geçişiyle aynı davranış).</para>
    /// </summary>
    public static void TransitionColor(FrameworkElement host, SolidColorBrush brush, Color to)
    {
        bool animationsEnabled = MotionGate.StaticAnimationsEnabled; // [W2] statik sinyalin TEK okuma ifadesi
        var duration = ResolveDuration(host, "Duration.Fast", 120.0);          // prototip: --duration-fast
        var spline = ResolveKeySpline(host, "KeySpline.EaseStandard", new KeySpline(0.4, 0, 0.2, 1)); // --ease-standard

        if (!animationsEnabled || duration.TimeSpan <= TimeSpan.Zero)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = to;
            return;
        }

        var animation = new ColorAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new SplineColorKeyFrame(to, KeyTime.FromTimeSpan(duration.TimeSpan), spline));
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>[T60] <see cref="TransitionColor"/>'ın double karşılığı — Switch başparmağının 120ms'lik
    /// <c>translateX</c> geçişi (_ds_bundle.js:900-903) gibi konum/opaklık geçişleri için.</summary>
    public static void TransitionDouble(FrameworkElement host, Animatable target, DependencyProperty property, double to)
    {
        bool animationsEnabled = MotionGate.StaticAnimationsEnabled; // [W2] statik sinyalin TEK okuma ifadesi
        var duration = ResolveDuration(host, "Duration.Fast", 120.0);
        var spline = ResolveKeySpline(host, "KeySpline.EaseStandard", new KeySpline(0.4, 0, 0.2, 1));

        if (!animationsEnabled || duration.TimeSpan <= TimeSpan.Zero)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, to);
            return;
        }

        target.BeginAnimation(property, SplineTo(to, duration.TimeSpan, spline), HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>[M-1] <see cref="Console.ConsoleView.AnimateToBottom"/> ve
    /// <see cref="StickyLayerList.AnimateScrollTo"/>'nun BİREBİR aynı desenini (taze <c>AnimationsEnabled</c> +
    /// <c>Duration.Slow</c> + <c>KeySpline.EaseInOut</c> + <see cref="ScrollAnimator.AnimateTo"/>) tek yerde
    /// toplar — iki host tipi (AvalonEdit <c>TextEditor</c> / <c>ScrollViewer</c>) FARKLI olsa da ikisi de
    /// <see cref="UIElement"/> + <c>ScrollToVerticalOffset(double)</c> sunduğundan <see cref="ScrollAnimator"/>
    /// üstünden ORTAK sarılabilir (kopya YASAK, CLAUDE.md). Motion sinyali ÇAĞRI ANINDA taze okunur (sözleşme).</summary>
    public static bool AnimateSlowEaseInOut(FrameworkElement host, UIElement scrollTarget, double currentOffset, double targetOffset)
    {
        bool animationsEnabled = MotionGate.StaticAnimationsEnabled; // [W2] statik sinyalin TEK okuma ifadesi
        var duration = ResolveDuration(host, "Duration.Slow", 280.0);
        var spline = ResolveKeySpline(host, "KeySpline.EaseInOut", new KeySpline(0.65, 0, 0.35, 1));
        return ScrollAnimator.AnimateTo(scrollTarget, currentOffset, targetOffset, animationsEnabled, duration.TimeSpan, spline);
    }
}
