using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [D6] design-v1 <c>.bo-pop-in</c> giriş animasyonu (BuildApp.jsx:21/:33) — branch/worktree popover'ları ve Build
/// menüsü ORTAK kullanır (kopya YASAK, CLAUDE.md): <c>opacity 0→1</c> + <c>translateY(4px)→0</c> + <c>scale(.985)→1</c>,
/// 140ms, <c>ease-out</c>. KAPANIŞ animasyonu YOKtur (popover anında gizlenir).
///
/// <para><b>Motion sözleşmesi:</b> <c>AnimationsEnabled</c> BAŞLATMA ANINDA taze okunur (reduced-motion'da hiç
/// animasyon kurulmaz — öğe son duruma SNAP eder); eğri <c>KeySpline.EaseOut</c> token'ından taze çözülür.
/// 140ms/4px/.985 design token DEĞİLDİR (bileşenin kendi ölçüsü — StickyRibbon'ın <c>IndeterminateSweepMs</c>
/// deseni) → kaynak satırıyla birlikte adlandırılmış sabit olarak yazılır.</para>
/// </summary>
internal static class PopIn
{
    // [A13/T4 fix-1 · A3] internal — PopoverTests artık değeri saf `Assert.Equal` ile pinliyor.
    internal const double DurationMs = 140.0;  // BuildApp.jsx:21 `.14s`
    private const double RiseFromPx = 4.0;    // BuildApp.jsx:33 `translateY(4px)`
    private const double ScaleFrom = 0.985;   // BuildApp.jsx:33 `scale(.985)`

    /// <summary>[design-v1.2.1 §2.10] Modal DİYALOG girişi: 180ms fade + 6px yukarı, ÖLÇEK YOK. Popover'dan
    /// ayrı ölçüler — diyalog daha büyük bir yüzeydir, aynı 140/4/.985 orada fazla tez durur.
    /// <para>180, <c>Duration.Base</c> token'ının ta kendisidir ve çalışma anında ORADAN çözülür; buradaki
    /// sabit yalnız fallback ve testin pinlediği değerdir (<c>MotionTokens.ResolveDuration</c> deseni).</para></summary>
    internal const double DialogDurationMs = 180.0;
    internal const double DialogRiseFromPx = 6.0;

    /// <summary>Popover / Build menüsü girişi (140ms, 4px, .985).</summary>
    public static void Play(FrameworkElement element)
        => Play(element, TimeSpan.FromMilliseconds(DurationMs), RiseFromPx, ScaleFrom);

    /// <summary>[design-v1.2.1 §2.10] Modal diyalog girişi — süre <c>Duration.Base</c> token'ından taze
    /// çözülür (motion sözleşmesi: ms literali çağrı yerinde YAZILMAZ).</summary>
    public static void PlayDialog(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var duration = MotionTokens.ResolveDuration(element, "Duration.Base", DialogDurationMs);
        Play(element, duration.TimeSpan, DialogRiseFromPx, scaleFrom: 1.0);
    }

    private static void Play(FrameworkElement element, TimeSpan duration, double riseFromPx, double scaleFrom)
    {
        // Önceki (uçuşta kalmış) animasyonları bırak — her açılışta taze başlar.
        element.BeginAnimation(UIElement.OpacityProperty, null);

        bool animate = MotionGate.StaticAnimationsEnabled; // [W2 fix-1] statik sinyalin TEK kapısı
        if (!animate)
        {
            element.Opacity = 1.0;
            element.RenderTransform = Transform.Identity;
            return;
        }

        var spline = MotionTokens.ResolveKeySpline(element, "KeySpline.EaseOut", new KeySpline(0.22, 1, 0.36, 1));

        element.RenderTransformOrigin = new Point(0.5, 0.5); // CSS transform-origin varsayılanı = center
        var translate = new TranslateTransform(0, riseFromPx);

        // Ölçek 1.0 ise TransformGroup kurulmaz: diyalog girişinde ölçek YOKTUR ve boş bir grup hem gereksiz
        // hem de "hangi transform?" sorusunu bulanıklaştırır (test doğrudan TranslateTransform bekler).
        if (scaleFrom is 1.0)
        {
            element.RenderTransform = translate;
        }
        else
        {
            var scale = new ScaleTransform(scaleFrom, scaleFrom);
            element.RenderTransform = new TransformGroup { Children = { scale, translate } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Rise(scaleFrom, 1.0, duration, spline));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Rise(scaleFrom, 1.0, duration, spline));
        }

        element.Opacity = 0.0;
        element.BeginAnimation(UIElement.OpacityProperty, Rise(0.0, 1.0, duration, spline));
        translate.BeginAnimation(TranslateTransform.YProperty, Rise(riseFromPx, 0.0, duration, spline));
    }

    // GraphView.PlayRevealStagger deseni: 0'da Discrete "from" + hedefe Spline (CSS keyframe `from`/`to` paritesi).
    private static DoubleAnimationUsingKeyFrames Rise(double from, double to, TimeSpan duration, KeySpline spline)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration), spline));
        return animation;
    }
}
