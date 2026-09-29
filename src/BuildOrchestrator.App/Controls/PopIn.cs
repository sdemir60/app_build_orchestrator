using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [D6] design-v1 <c>.bo-pop-in</c> giriş animasyonu (BuildApp.jsx:21/:33) — branch popover'ı ve Build
/// menüsü ORTAK kullanır (kopya YASAK, CLAUDE.md): <c>opacity 0→1</c> + <c>translateY(4px)→0</c> + <c>scale(.985)→1</c>,
/// 140ms, <c>ease-out</c>. KAPANIŞ animasyonu YOKtur (popover anında gizlenir).
///
/// <para>Aynı gövde diğer girişleri de taşır (kopya YASAK): modal diyalog (<see cref="PlayDialog"/>), güncelleme
/// hapı (<see cref="PlayEntrance"/>), güncelleme kartı (<see cref="PlayDropIn"/>) ve restart ekranı
/// (<see cref="PlayFadeIn"/>) — fark yalnız süre, yön, ölçek ve ölçek merkezidir.</para>
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

    /// <summary>[design v1.23.0 §9] Güncelleme hapının ve kartının girişi (<c>bo-upd-in</c> / <c>bo-drop-in</c>,
    /// BuildApp.jsx:46-47): 4px YUKARIDAN iner — popover'ın pop-in'i ise aşağıdan yükselir, işaret bu yüzden ters.</summary>
    private const double EntranceRiseFromPx = -4.0;

    /// <summary>CSS <c>transform-origin</c> varsayılanı — popover, Build menüsü ve diyalog girişleri.</summary>
    private static readonly Point CenterOrigin = new(0.5, 0.5);

    /// <summary>[design v1.23.0 §2.12] Güncelleme hapının giriş süresi — <c>Duration.Slow</c> token'ından taze çözülür
    /// (tasarım 280ms ease-out; reduced-motion token'ı sıfırlar).</summary>
    internal static TimeSpan EntranceDuration(FrameworkElement element) => MotionTokens.ResolveSlow(element).TimeSpan;

    /// <summary>[design v1.23.0 §2.12 · §9] Güncelleme hapının (ve ayracının) BİR KEZLİK girişi: <c>opacity 0 +
    /// translateY(-4px)</c> → düz, <c>Duration.Slow</c> + <c>ease-out</c>, ölçek yok. Sonrasında hareket yoktur.</summary>
    public static void PlayEntrance(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Play(element, EntranceDuration(element), EntranceRiseFromPx, scaleFrom: 1.0, CenterOrigin);
    }

    /// <summary>[design v1.23.0 §2.12] Güncelleme kartının ölçek merkezi: kart hapın altından SARKAR, sol üst köşesi
    /// hapla hizalıdır (CSS <c>transformOrigin: '0 0'</c>).</summary>
    private static readonly Point TopLeftOrigin = new(0, 0);

    /// <summary>[design v1.23.0 §2.12 · §9] Güncelleme kartının girişi (<c>bo-drop-in</c>, BuildApp.jsx:47): pop-in'in
    /// 140ms'i ve .985 ölçeği, ama 4px YUKARIDAN iner ve sol üst köşeden büyür.</summary>
    public static void PlayDropIn(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Play(element, TimeSpan.FromMilliseconds(DurationMs), EntranceRiseFromPx, ScaleFrom, TopLeftOrigin);
    }

    /// <summary>[design v1.23.0 §2.12] Restart ekranının giriş süresi — <c>Duration.Base</c> token'ından taze çözülür
    /// (tasarım 180ms; reduced-motion token'ı sıfırlar). Modal girişiyle AYNI token.</summary>
    internal static TimeSpan FadeInDuration(FrameworkElement element) =>
        MotionTokens.ResolveDuration(element, "Duration.Base", DialogDurationMs).TimeSpan;

    /// <summary>[design v1.23.0 §2.12 · §9] Restart ekranının girişi (<c>bo-fade-in</c>, BuildApp.jsx:48): yalnız
    /// <c>opacity 0 → 1</c>, <c>Duration.Base</c> + <c>ease-out</c> — kayma ve ölçek yok (ekran pencerenin tamamını
    /// örter, yerinden oynayacak bir kenarı yoktur).</summary>
    public static void PlayFadeIn(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Play(element, FadeInDuration(element), riseFromPx: 0.0, scaleFrom: 1.0, CenterOrigin);
    }

    /// <summary>Popover / Build menüsü girişi (140ms, 4px, .985).</summary>
    public static void Play(FrameworkElement element)
        => Play(element, TimeSpan.FromMilliseconds(DurationMs), RiseFromPx, ScaleFrom, CenterOrigin);

    /// <summary>[design-v1.2.1 §2.10] Modal diyalog girişi — süre <c>Duration.Base</c> token'ından taze
    /// çözülür (motion sözleşmesi: ms literali çağrı yerinde YAZILMAZ).</summary>
    public static void PlayDialog(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Play(element, FadeInDuration(element), DialogRiseFromPx, scaleFrom: 1.0, CenterOrigin);
    }

    /// <summary>Girişlerin ORTAK gövdesi. <paramref name="riseFromPx"/> pozitifse öğe aşağıdan yükselir, negatifse
    /// yukarıdan iner, sıfırsa yerinde durur (yalnız sönüm — transform kurulmaz); <paramref name="origin"/> ölçeğin
    /// merkezidir (göreli, 0..1).</summary>
    private static void Play(FrameworkElement element, TimeSpan duration, double riseFromPx, double scaleFrom, Point origin)
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

        var spline = MotionTokens.ResolveEaseOut(element);

        element.Opacity = 0.0;
        element.BeginAnimation(UIElement.OpacityProperty, Rise(0.0, 1.0, duration, spline));

        // Kayma da ölçek de yoksa giriş yalnız bir sönümdür — transform KURULMAZ (hareketsiz bir transform'u her karede
        // taşımanın anlamı yok).
        if (riseFromPx is 0.0 && scaleFrom is 1.0)
        {
            element.RenderTransform = Transform.Identity;
            return;
        }

        element.RenderTransformOrigin = origin;
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
