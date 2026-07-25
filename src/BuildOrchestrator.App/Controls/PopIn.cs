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
    private const double DurationMs = 140.0;  // BuildApp.jsx:21 `.14s`
    private const double RiseFromPx = 4.0;    // BuildApp.jsx:33 `translateY(4px)`
    private const double ScaleFrom = 0.985;   // BuildApp.jsx:33 `scale(.985)`

    public static void Play(FrameworkElement element)
    {
        // Önceki (uçuşta kalmış) animasyonları bırak — her açılışta taze başlar.
        element.BeginAnimation(UIElement.OpacityProperty, null);

        bool animate = App.Motion?.AnimationsEnabled ?? false;
        if (!animate)
        {
            element.Opacity = 1.0;
            element.RenderTransform = Transform.Identity;
            return;
        }

        var spline = MotionTokens.ResolveKeySpline(element, "KeySpline.EaseOut", new KeySpline(0.22, 1, 0.36, 1));
        var duration = TimeSpan.FromMilliseconds(DurationMs);

        element.RenderTransformOrigin = new Point(0.5, 0.5); // CSS transform-origin varsayılanı = center
        var scale = new ScaleTransform(ScaleFrom, ScaleFrom);
        var translate = new TranslateTransform(0, RiseFromPx);
        element.RenderTransform = new TransformGroup { Children = { scale, translate } };

        element.Opacity = 0.0;
        element.BeginAnimation(UIElement.OpacityProperty, Rise(0.0, 1.0, duration, spline));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Rise(ScaleFrom, 1.0, duration, spline));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Rise(ScaleFrom, 1.0, duration, spline));
        translate.BeginAnimation(TranslateTransform.YProperty, Rise(RiseFromPx, 0.0, duration, spline));
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
