using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [design v1.12.1 §2.5 · §2.6] <b>İmlecin renk sıçraması.</b> Kırpma (<see cref="MotionTokens.BlinkMs"/>)
/// DEĞİŞMEDİ; üstüne ikinci bir saat biner ve imleç her yanışta konsol satır paletinden sıradaki rengi
/// taşır: <c>cmd → info → success → warn → error → dim</c>.
///
/// <para><b>Neden kesikli (discrete) keyframe:</b> geçişli bir renk kayması İSTENMEDİ — imlecin ritmi
/// kırpmanın ritmidir ve renk, kırpmanın DİBİNDE (opacity 0.1) atlar. Faz bunu sağlar:
/// <see cref="PhaseMs"/> = −(bir kırpmanın yarısı), yani her adım sınırı tam sönük ana denk gelir ve göz
/// hiçbir geçiş görmez — imleç yeni renkle DOĞAR.</para>
///
/// <para><b>Neden yerel fırça:</b> token fırçaları (Tokens.xaml) PAYLAŞILIR; birinin <c>Color</c>'ını
/// animasyonlamak onu okuyan her yüzeyi boyardı. İmleç bu yüzden kendi <see cref="SolidColorBrush"/>'ını
/// alır (<c>GraphView</c>'ın dalga sırasında yerel fırçaya devretmesiyle AYNI gerekçe) ve tur sökülünce
/// renk token referansına geri bırakılır.</para>
///
/// <para><b>Reduced motion:</b> tur hiç kurulmaz — imleç sabit kalır ve satırın rengini taşır. Event
/// stream'in ton kanalı (son olayın ikon rengi) bu yüzden ölmez: sıçrama yokken görünen renk odur.</para>
/// </summary>
public static class CursorHop
{
    /// <summary>Bir adım = bir tam kırpma (1.1s). Kırpma <see cref="MotionTokens.BlinkMs"/> yarım periyottur
    /// (<c>AutoReverse</c>), tam periyot onun iki katıdır — yeni bir sabit İCAT EDİLMEZ.</summary>
    internal const double StepMs = MotionTokens.BlinkMs * 2;

    /// <summary>Turun fazı: yarım kırpma GERİDEN başlar (negatif <see cref="Timeline.BeginTime"/>), böylece
    /// adım sınırları kırpmanın dibine düşer.</summary>
    internal const double PhaseMs = -MotionTokens.BlinkMs;

    /// <summary>Kare hızı: değerler KESİKLİ olduğu için ara kare yoktur — saat yalnız adım sınırlarını
    /// yakalayacak kadar sık dönmelidir. 20fps'te sınır en fazla 50ms kayar ve o an imleç zaten sönüktür.</summary>
    internal const int FrameRate = 20;

    /// <summary>Turun renkleri, SIRASIYLA. Kaynak konsolun kendi satır paletidir
    /// (<see cref="ConsolePalette.Keys"/>) — imleç, bir konsol satırının taşıyamayacağı hiçbir rengi almaz.</summary>
    public static readonly IReadOnlyList<string> BrushKeys =
    [
        ConsolePalette.Keys.Cmd,
        ConsolePalette.Keys.Info,
        ConsolePalette.Keys.Success,
        ConsolePalette.Keys.Warn,
        ConsolePalette.Keys.Error,
        ConsolePalette.Keys.Dim,
    ];

    /// <summary>Turun tam süresi — altı adım × bir kırpma = 6.6s.</summary>
    internal static double TotalMs => StepMs * BrushKeys.Count;

    /// <summary>Tur ŞU AN dönüyor mu (imlecin rengi yerel, animasyonlu bir fırçadan mı geliyor).</summary>
    public static bool IsRunning(Shape? cursor) => cursor?.Fill is SolidColorBrush { HasAnimatedProperties: true };

    /// <summary>
    /// [perf B4] Turun SAATİ — <see cref="CursorClock"/> iki imleç için TEK bir saat kurar ve her imleci ona bağlar
    /// (<see cref="Start"/>). Görünüm HENÜZ bir kaynak sözlüğüne bağlı değilse (DataContext, ağaca girmeden önce
    /// yazılabilir) palet çözülemez ve <c>null</c> döner: tur o karede kurulmaz, imleç token rengiyle sabit kalır.
    /// Sessiz atlama, ağaç dışı bir çağrının uygulamayı düşürmesinden İYİDİR — kayıp tek şey turdur.
    /// </summary>
    internal static AnimationClock? CreateClock(Func<string, object?> find) => CreateAnimation(find)?.CreateClock();

    /// <summary>
    /// Turu verilen (paylaşımlı) saate bağlar. <b>Zaten dönen bir tur YENİDEN kurulmaz</b> (<c>StatusGlyph</c>/
    /// <c>BuildingSpinner</c> deseni): her olayda yeniden bağlamak fırçayı değiştirir ve imleç hep ilk renkte takılı
    /// görünürdü. Her imleç kendi YEREL fırçasını alır; saat ise ortaktır, yani iki imleç aynı anda aynı renge atlar.
    /// </summary>
    internal static void Start(Shape cursor, AnimationClock clock)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(clock);
        if (IsRunning(cursor)) return;

        // Yerel fırça: taban renk turun İLK adımıdır, böylece saat bağlanmadan önceki tek karede bile imleç
        // paletin dışında bir renk taşımaz.
        var brush = new SolidColorBrush(FirstColor((ColorAnimationUsingKeyFrames)clock.Timeline));
        cursor.Fill = brush;
        brush.ApplyAnimationClock(SolidColorBrush.ColorProperty, clock);
    }

    /// <summary>Turu söker ve rengi verilen token referansına geri bırakır (yerel fırça terk edilir).</summary>
    public static void Stop(Shape cursor, string restKey)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        // YALNIZ turun kendi yerel fırçası sökülür: token fırçaları paylaşılır ve DONDURULMUŞTUR — donmuş bir
        // Freezable'da animasyon sökmek de kurmak kadar yasaktır (InvalidOperationException).
        if (cursor.Fill is SolidColorBrush { IsFrozen: false, HasAnimatedProperties: true } brush)
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        cursor.SetResourceReference(Shape.FillProperty, restKey);
    }

    /// <summary>[test yüzeyi] Turun zaman çizelgesi — anahtar sırası, adım süresi, faz ve tekrarı burada
    /// TEK yerde tanımlıdır. Paletin bir tonu çözülemiyorsa <c>null</c> (bkz. <see cref="Start"/>).</summary>
    internal static ColorAnimationUsingKeyFrames? CreateAnimation(Func<string, object?> find)
    {
        var colors = new List<Color>(BrushKeys.Count);
        foreach (string key in BrushKeys)
        {
            if ((find(key) as SolidColorBrush)?.Color is not { } color) return null;
            colors.Add(color);
        }
        // Çizelgenin KENDİSİ MotionTokens'ta kurulur — renk zaman çizelgelerinin tek kurucusu orasıdır
        // (kopya YASAK); burada yalnız HANGİ renkler, hangi ritimde sorusu cevaplanır.
        return MotionTokens.DiscreteColorCycle(colors, StepMs, PhaseMs, FrameRate);
    }

    private static Color FirstColor(ColorAnimationUsingKeyFrames animation) => animation.KeyFrames[0].Value;
}
