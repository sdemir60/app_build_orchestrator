using System.Windows;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.App.Graph;

/// <summary>
/// [G2 fix round 1 · A1] Graf etiketinin <b>çizilen</b> genişliğini ölçer — LOD kararı (etiket kurulacak mı)
/// bu ölçüme dayanır, <see cref="GraphLayout.NodeCellWidth"/> kelepçesine değil.
///
/// <para><b>Neden ölçüyoruz:</b> kelepçe (88,4px) etiketin ÜST SINIRIDIR, örtüşme koşulu değil. Kısa adlı bir
/// grafta (ör. <c>Base</c>, <c>Api</c>) 10-13 düğümlük bir katmanın etiketleri hiç örtüşmediği hâlde kelepçe
/// eşiğiyle düşerdi. Gerçek koşul <c>aralık &lt; çizilen genişlik</c>'tir.</para>
///
/// <para><b>Ölçüm yolu KOPYA DEĞİL:</b> advance-width matematiği <see cref="TrackedGlyphs"/>'tedir (T57) ve
/// buradan yeniden kullanılır; typeface çözümü <see cref="TrackedTextBlock"/> ile aynı desendir
/// (<c>Typeface.TryGetGlyphTypeface</c>). Font çözülemezse (kapsam dışı host) ölçüm <c>null</c> döner ve
/// çağıran kelepçeye düşer — yani G2'nin ilk turundaki (daha muhafazakâr) davranışa geri sarar, çökmez.</para>
/// </summary>
internal static class GraphLabelMetrics
{
    /// <summary>Graf etiketinin punto'su (design-v1 §2.3: kare altında mono 10px).</summary>
    public const double LabelFontSize = 10.0;

    private static FontFamily? _cachedFamily;
    private static GlyphTypeface? _cachedTypeface;

    /// <summary>Metnin <paramref name="fontFamily"/> (varsayılan <see cref="AppFonts.Mono"/>) ile
    /// <see cref="LabelFontSize"/>'da çizilen genişliği; typeface çözülemezse <c>null</c>.
    ///
    /// <para><paramref name="fontFamily"/> bir TEST SEAM'idir: <c>pack://</c> aileler gerçek bir
    /// <c>Application</c> olmadan çözülmez, bu yüzden testler <c>TestAssets/Fonts</c>'a <c>file://</c> tabanlı
    /// bir aile enjekte eder — <c>TrackedTextBlockTests</c>'in kurduğu desenin AYNISI.</para></summary>
    public static double? TryMeasure(string text, FontFamily? fontFamily = null)
    {
        var family = fontFamily ?? AppFonts.Mono;
        if (!ReferenceEquals(family, _cachedFamily))
        {
            _cachedFamily = family;
            var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            _cachedTypeface = typeface.TryGetGlyphTypeface(out var glyphTypeface) ? glyphTypeface : null;
        }

        if (_cachedTypeface is null) return null;
        // Etiket TrackedTextBlock DEĞİL düz bir TextBlock'tur ⇒ tracking yok, uppercase yok.
        return TrackedGlyphs.Build(_cachedTypeface, text, LabelFontSize, trackingEm: 0.0, uppercase: false).TotalWidth;
    }

    /// <summary>
    /// Bir katmanın EN GENİŞ etiketinin çizilen genişliği, hücre genişliğine kelepçeli (etiket orada
    /// <c>CharacterEllipsis</c> ile kırpıldığı için daha genişi çizilemez).
    ///
    /// <para>Katmanın yalnız <b>en uzun</b> adı ölçülür (katman başına TEK ölçüm): graf etiketleri tasarım
    /// gereği MONO'dur (<see cref="AppFonts.Mono"/>), dolayısıyla karakter sayısı sırası = genişlik sırasıdır.
    /// Varsayım bir gün bozulsa bile hata GÜVENLİ yöndedir: ölçüm hafif düşük çıkar ⇒ etiketler daha GEÇ
    /// düşer (A1'in istediği yön).</para>
    /// </summary>
    public static double WidestLabelWidth(IEnumerable<string> shortNames, FontFamily? fontFamily = null)
    {
        ArgumentNullException.ThrowIfNull(shortNames);

        string? longest = null;
        foreach (string name in shortNames)
            if (longest is null || name.Length > longest.Length) longest = name;

        if (longest is null) return 0.0;
        return Math.Min(GraphLayout.NodeCellWidth, TryMeasure(longest, fontFamily) ?? GraphLayout.NodeCellWidth);
    }
}
