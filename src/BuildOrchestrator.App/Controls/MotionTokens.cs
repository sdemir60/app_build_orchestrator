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

    /// <summary>
    /// <b>KESİKLİ renk turu</b> — <paramref name="colors"/>'ı sırayla, her birini <paramref name="stepMs"/>
    /// kadar gösteren sonsuz bir zaman çizelgesi. Ara kare YOKTUR: renk bir adımdan diğerine ATLAR.
    ///
    /// <para><b>Neden burada:</b> renk zaman çizelgelerinin TEK kurucusu bu dosyadır (kopya YASAK; guard
    /// <c>ColorTransitionFlashTests.No_app_file_builds_its_own_color_keyframe_outside_the_shared_builder</c>).
    /// <see cref="SplineColorTo"/>'nun premultiply düzeltmesi burada GEREKMEZ ve uygulanmaz — o düzeltme
    /// interpolasyonun ürettiği çakmayı önler, kesikli bir keyframe ise hiç interpolasyon yapmaz.</para>
    ///
    /// <para><paramref name="beginMs"/> negatif verilebilir: çizelge geçmişte başlamış gibi kurulur, yani
    /// adım sınırları istenen faza kaydırılır (bkz. <see cref="CursorHop"/>).</para>
    /// </summary>
    public static ColorAnimationUsingKeyFrames DiscreteColorCycle(
        IReadOnlyList<Color> colors, double stepMs, double beginMs, int frameRate)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var animation = new ColorAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(stepMs * colors.Count)),
            BeginTime = TimeSpan.FromMilliseconds(beginMs),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        for (int i = 0; i < colors.Count; i++)
            animation.KeyFrames.Add(new DiscreteColorKeyFrame(
                colors[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * stepMs))));
        Timeline.SetDesiredFrameRate(animation, frameRate);
        return animation;
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
    /// <see cref="SplineTo"/>'nun RENK karşılığı — bir geçişin İKİ ucunu da AÇIKÇA taşıyan tek zaman çizelgesi
    /// kurucusu. Başlangıç t=0'daki bir <see cref="DiscreteColorKeyFrame"/> ile ilan edilir; aradaki tek
    /// <see cref="SplineColorKeyFrame"/> o değerden hedefe eğriyle akar.
    ///
    /// <para><b>Neden başlangıç AÇIK yazılır:</b> WPF from'suz bir keyframe'i property'nin taban değerinden
    /// başlatır ve taban <c>Colors.Transparent</c> ise o değer <c>#00FFFFFF</c>'tir — yani BEYAZ.</para>
    ///
    /// <para><b>Neden alfa farklıysa yol ÖRNEKLENİR (premultiply pariteti).</b> WPF renk kanallarını
    /// PREMULTIPLY ETMEDEN interpole eder, CSS ise eder. Uçların alfası farklı olduğunda fark görünürdür:
    /// RGB hedefe koşarken alfa henüz yüksek kaldığı için ara kare ekranda İKİ UCUN DA DIŞINA taşar. Ölçülen
    /// örnek — chip'in hover'dan aktife geçişi (<c>#FF202024</c> → <c>#1FEDA10F</c>, alt bar zemininde):
    /// ortası <c>(84,63,25)</c>, uçlar ise <c>(32,32,36)</c> ve <c>(46,37,22)</c>. Yani renk çıkıp geri
    /// dönüyordu; kullanıcı bunu "git gel efekti" diye tarif etti. Premultiplied yol aynı noktada
    /// <c>(39,35,29)</c> verir — iki ucun arasında.</para>
    ///
    /// <para>Premultiplied interpolasyon tek bir keyframe ile ifade EDİLEMEZ (alfaya bölünen bir rasyonel
    /// fonksiyondur), bu yüzden yol <see cref="PremultipliedSamples"/> noktada örneklenir. Örnekler
    /// EĞRİNİN PARAMETRESİ boyunca alınır: bir <see cref="KeySpline"/> zamanı ve ilerlemeyi aynı parametreden
    /// üretir (<c>X(s)</c> ve <c>Y(s)</c>), yani eğriyi ters çevirmeye gerek kalmaz ve yumuşatma birebir
    /// korunur. <b>Alfa eşitse hiçbir şey değişmez:</b> ortak alfa çarpanı sadeleşir, premultiplied ve düz
    /// interpolasyon AYNI sonucu verir — o durumda tek bir <see cref="SplineColorKeyFrame"/> kalır.</para>
    ///
    /// <para>Bu kural, eski "sıfır-alfalı ucu diğer ucun RGB'siyle eşitle" düzeltmesinin GENEL hâlidir ve onu
    /// kapsar: alfası sıfır olan ucun premultiplied katkısı zaten sıfırdır, dolayısıyla RGB yol boyunca
    /// hedefte sabit kalır ve yalnız alfa akar. Kanıt: <c>ColorTransitionFlashTests</c>.</para>
    /// </summary>
    public static ColorAnimationUsingKeyFrames SplineColorTo(Color from, Color to, TimeSpan duration, KeySpline keySpline)
    {
        ArgumentNullException.ThrowIfNull(keySpline);
        var animation = new ColorAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteColorKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));

        if (from.A == to.A)
        {
            animation.KeyFrames.Add(new SplineColorKeyFrame(to, KeyTime.FromTimeSpan(duration), keySpline));
            return animation;
        }

        for (int i = 1; i <= PremultipliedSamples; i++)
        {
            double s = (double)i / PremultipliedSamples;
            double time = CubicBezier(keySpline.ControlPoint1.X, keySpline.ControlPoint2.X, s);
            double progress = CubicBezier(keySpline.ControlPoint1.Y, keySpline.ControlPoint2.Y, s);
            animation.KeyFrames.Add(new LinearColorKeyFrame(
                PremultipliedLerp(from, to, progress),
                KeyTime.FromTimeSpan(TimeSpan.FromTicks((long)(duration.Ticks * time)))));
        }
        return animation;
    }

    /// <summary>Örneklenen yolun çözünürlüğü. Örnekler arasında WPF düz interpolasyon yapar; 120ms'lik bir
    /// geçişte bu, kare başına birkaç ms'lik parçalar demektir ve eğriden sapma yuvarlama gürültüsünün
    /// altındadır.</summary>
    private const int PremultipliedSamples = 16;

    /// <summary>Uçları <c>(0,0)</c> ve <c>(1,1)</c> olan kübik Bézier'in tek eksendeki değeri. Bir
    /// <see cref="KeySpline"/> zamanı da ilerlemeyi de bu fonksiyonla, AYNI <paramref name="s"/>'ten üretir.</summary>
    private static double CubicBezier(double c1, double c2, double s)
    {
        double u = 1 - s;
        return (3 * u * u * s * c1) + (3 * u * s * s * c2) + (s * s * s);
    }

    /// <summary>
    /// İki rengin premultiplied karışımı — tarayıcının renk geçişiyle aynı yol. Alfa doğrudan, kanallar ise
    /// alfayla ÇARPILMIŞ hâlde karışır ve sonuçta yeni alfaya geri bölünür.
    ///
    /// <para>Alfa sıfıra düştüğünde renk ekranda hiçbir şeye katkı vermez; kanallar o noktada hedefin
    /// RGB'sinde bırakılır (sıfıra bölmek yerine), böylece bir sonraki örnek de aynı tondan devam eder.</para>
    /// </summary>
    private static Color PremultipliedLerp(Color from, Color to, double t)
    {
        double alpha = from.A + ((to.A - from.A) * t);
        if (alpha <= 0) return Color.FromArgb(0, to.R, to.G, to.B);

        return Color.FromArgb((byte)Math.Round(alpha),
            Channel(from.A * from.R, to.A * to.R),
            Channel(from.A * from.G, to.A * to.G),
            Channel(from.A * from.B, to.A * to.B));

        byte Channel(double premultipliedFrom, double premultipliedTo)
            => (byte)Math.Clamp(Math.Round((premultipliedFrom + ((premultipliedTo - premultipliedFrom) * t)) / alpha), 0, 255);
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
        var fast = ResolveFast(host);
        if (!fast.Animate)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = to;
            return;
        }

        // brush.Color uçuştaki animasyonun O ANKİ değerini verir — geçiş bulunduğu yerden devam eder (retarget).
        var animation = SplineColorTo(brush.Color, to, fast.Duration, fast.Spline);
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>[T60] <see cref="TransitionColor"/>'ın double karşılığı — Switch başparmağının 120ms'lik
    /// <c>translateX</c> geçişi (_ds_bundle.js:900-903) gibi konum/opaklık geçişleri için.</summary>
    public static void TransitionDouble(FrameworkElement host, Animatable target, DependencyProperty property, double to)
    {
        var fast = ResolveFast(host);
        if (!fast.Animate)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, to);
            return;
        }

        target.BeginAnimation(property, SplineTo(to, fast.Duration, fast.Spline), HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>[SCROLLBAR-HOVER] <see cref="TransitionColor"/>'ın Thickness karşılığı — scrollbar hap'ının
    /// hover'da 3px kenardan 1px kenara açılması gibi İÇERLEK geçişleri için. Hedef öğenin KENDİSİDİR
    /// (<c>Padding</c> gibi bir <see cref="FrameworkElement"/> özelliği); <see cref="TransitionDouble"/>'ın
    /// <see cref="Animatable"/> hedefinden farkı budur, gate/süre/eğri aynıdır.</summary>
    public static void TransitionThickness(FrameworkElement target, DependencyProperty property, Thickness to)
    {
        var fast = ResolveFast(target);
        if (!fast.Animate)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, to);
            return;
        }

        // WPF'in düz ThicknessAnimation'ı KeySpline alamaz — SplineTo ile aynı gerekçeyle keyframe biçimi.
        var animation = new ThicknessAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new SplineThicknessKeyFrame(to, KeyTime.FromTimeSpan(fast.Duration), fast.Spline));
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// [design v1.11.0 §9-4 · §2.3] <b>Token anahtarıyla verilen bir fırçaya GEÇİŞ</b> — işaretleme
    /// dalgasının "amber'a yanma"sı ve onun geri dönüşü. İşaretleme dalgasına giren HER yüzey (graf düğümünün
    /// çerçevesi/zemini/küpü, satırın şeridi, noktası ve adı) buradan boyanır; ikinci bir geçiş yolu YOKTUR.
    ///
    /// <para><b>İki kip:</b> <paramref name="animate"/> false ise yüzey <see cref="FrameworkElement.SetResourceReference"/>
    /// ile PAYLAŞILAN (donmuş) token fırçasına bağlanır ve renk anında oturur — varsayılan ve ucuz yol. true ise
    /// yüzey kendi DONMAMIŞ kopyasına devreder ve rengi animasyonla akar; sonraki bir <c>animate:false</c>
    /// çağrısı onu token referansına GERİ verir, yani yüzey referansını kalıcı kaybetmez.</para>
    ///
    /// <para><paramref name="resourceHost"/> ayrı verilir (<see cref="IconPaint.Apply"/> ile aynı gerekçe):
    /// graf düğümleri henüz ağaca girmemişken boyanır ve kendi <c>TryFindResource</c>'ları hiçbir sözlüğe
    /// ulaşamaz.</para>
    /// </summary>
    public static void TransitionTokenBrush(FrameworkElement resourceHost, FrameworkElement target,
        DependencyProperty property, string brushKey, bool animate, double durationMs)
    {
        var to = (resourceHost.TryFindResource(brushKey) as SolidColorBrush)?.Color;
        // Motion sinyalinin kapısı ÇAĞIRANDADIR (GraphView.AnimationsEnabledProvider / satırın MotionGate'i):
        // burada ikinci kez sorulsaydı karar iki yere dağılır ve test host'unun sinyali üretim yolundan
        // ayrışırdı. Burası yalnız "geçiş istendi mi ve hedef çözüldü mü" sorusuna bakar.
        bool motion = animate && to is not null && durationMs > 0;

        if (!motion)
        {
            if (target.GetValue(property) is SolidColorBrush previous && !previous.IsFrozen)
                previous.BeginAnimation(SolidColorBrush.ColorProperty, null);
            target.SetResourceReference(property, brushKey);
            return;
        }

        if (target.GetValue(property) is not SolidColorBrush local || local.IsFrozen)
        {
            // Devir: donmuş token fırçasının O ANKI renginden başlayan yerel bir kopya.
            local = new SolidColorBrush((target.GetValue(property) as SolidColorBrush)?.Color ?? to!.Value);
            target.SetValue(property, local);
        }
        if (local.Color == to!.Value) return; // zaten hedefte — boşuna animasyon kurma

        var spline = ResolveKeySpline(resourceHost, "KeySpline.EaseStandard", new KeySpline(0.4, 0, 0.2, 1));
        local.BeginAnimation(SolidColorBrush.ColorProperty,
            SplineColorTo(local.Color, to.Value, TimeSpan.FromMilliseconds(durationMs), spline),
            HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>DS'in "durum değişimi" geçişinin ORTAK kapısı: <c>--duration-fast</c> + <c>--ease-standard</c> +
    /// motion sinyali. Üç <c>TransitionX</c> metodu da (renk / double / thickness) BİREBİR aynı üç satırı
    /// yazıyordu — tek yer (kopya YASAK, CLAUDE.md). Süre ve sinyal ÇAĞRI ANINDA taze okunur (motion
    /// sözleşmesi); süre 0'a düşmüşse (reduced-motion) <c>Animate</c> false'tur ve çağıran hedefe anında yazar.</summary>
    private static FastTransition ResolveFast(FrameworkElement host)
    {
        var duration = ResolveDuration(host, "Duration.Fast", 120.0);          // prototip: --duration-fast
        var spline = ResolveKeySpline(host, "KeySpline.EaseStandard", new KeySpline(0.4, 0, 0.2, 1)); // --ease-standard
        // [W2] statik sinyalin TEK okuma ifadesi
        bool animate = MotionGate.StaticAnimationsEnabled && duration.TimeSpan > TimeSpan.Zero;
        return new FastTransition(animate, duration.TimeSpan, spline);
    }

    private readonly record struct FastTransition(bool Animate, TimeSpan Duration, KeySpline Spline);

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
