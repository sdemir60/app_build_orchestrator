using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T60 · B2 review carried-forward #1] Bir ikonu Icons.xaml'deki BOYA SEMANTİĞİYLE birlikte bir
/// <see cref="Path"/>'e uygulayan TEK kod-tarafı yol. Sözlük her <c>Icon.X</c> için kardeş bir
/// <c>Icon.X.StrokeThickness</c> taşır: <c>&gt;0</c> = konturlu, <c>0</c> = dolu (bkz. Icons.xaml başlığı).
///
/// <para><b>Neden gerekli:</b> B2 geometriyi tek kaynağa indirdi ama kalınlık/kip'i indirmedi; iki tüketici
/// (ConsoleHeader, GraphView) sayıları elle taşıyordu ve sözlükle ayrışmalarını gören bir test yoktu. Artık
/// sayı yalnız sözlüktedir ve fill/stroke ayrımı da oradan gelir (kopya YASAK, CLAUDE.md).</para>
/// </summary>
internal static class IconPaint
{
    /// <summary>Sözlükteki kardeş kalınlık anahtarının adı (<c>Icon.Copy</c> → <c>Icon.Copy.StrokeThickness</c>).</summary>
    public static string ThicknessKey(string iconKey) => iconKey + ".StrokeThickness";

    /// <summary>Kalınlığı çözer. Kaynak zinciri yoksa (headless test host) <c>null</c> döner — çağıran
    /// sessizce boyamayı atlar; ConsoleView/LatestPill'in TryFindResource deseniyle aynı.</summary>
    public static double? TryResolveThickness(FrameworkElement host, string iconKey)
        => host.TryFindResource(ThicknessKey(iconKey)) is double t ? t : null;

    /// <summary>
    /// <paramref name="path"/>'e ikonun geometrisini VE boyasını sürer. Geometri ve boya fırçası
    /// <see cref="FrameworkElement.SetResourceReference"/> ile bağlanır (sözlük merge edilmemişse sessizce
    /// çözümsüz kalır — <c>{StaticResource}</c> throw ederdi).
    /// </summary>
    /// <param name="resourceHost">Kalınlığın okunacağı kaynak KAPSAMI. <paramref name="path"/>'in KENDİSİ
    /// değildir: ikonlar çoğu zaman daha ağaca girmemiş bir görsel kurulurken boyanır (GraphView düğümleri,
    /// ConsoleHeader'ın ctor'u) ve o anda <c>path.TryFindResource</c> hiçbir sözlüğe ulaşamaz. Fırça ve
    /// geometri <see cref="FrameworkElement.SetResourceReference"/> ile ERTELENMİŞ bağlanır, ama kalınlık
    /// gerçek bir SAYI olmak zorundadır — bu yüzden ağaçtaki bir host gerekir.</param>
    /// <param name="brushKey">Boyanın token anahtarı; ikon DOLUYSA <c>Fill</c>'e, KONTURLUYSA <c>Stroke</c>'a
    /// bağlanır — çağıran hangisi olduğunu bilmek ZORUNDA değildir, karar sözlüğündür.</param>
    /// <param name="animate">[design v1.11.0 §9-4] Boya geçişle mi otursun (işaretleme dalgası) yoksa anında
    /// mı — karar çağırandır, yol tektir (<see cref="MotionTokens.TransitionTokenBrush"/>).</param>
    /// <param name="durationMs">Geçiş süresi; <paramref name="animate"/> false iken anlamsızdır.</param>
    public static void Apply(Path path, FrameworkElement resourceHost, string iconKey, string brushKey,
        bool animate = false, double durationMs = 0)
    {
        path.SetResourceReference(Path.DataProperty, iconKey);

        double? thickness = TryResolveThickness(resourceHost, iconKey);
        if (thickness is null) return; // kaynak yok — geometri bağlandı, boya üretimde çözülecek

        if (thickness.Value > 0)
        {
            path.Fill = null;
            path.StrokeThickness = thickness.Value;
            path.StrokeStartLineCap = PenLineCap.Round;
            path.StrokeEndLineCap = PenLineCap.Round;
            path.StrokeLineJoin = PenLineJoin.Round;
            MotionTokens.TransitionTokenBrush(resourceHost, path, Shape.StrokeProperty, brushKey, animate, durationMs);
        }
        else
        {
            path.Stroke = null;
            path.StrokeThickness = 0;
            MotionTokens.TransitionTokenBrush(resourceHost, path, Shape.FillProperty, brushKey, animate, durationMs);
        }
    }
}
