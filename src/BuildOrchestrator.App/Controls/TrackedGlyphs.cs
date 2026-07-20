using System.Windows.Media;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T57] Advance-width matematiği — WPF'te letter-spacing yok (dotnet/wpf#293); tracking, hair-space
/// KARAKTERİ eklemez, her karakterin GlyphRun advance'ine matematiksel ek yapar. SAF static fonksiyon:
/// UI/DrawingContext/Dispatcher bağımsız, yalnız zaten çözülmüş bir <see cref="GlyphTypeface"/> alır —
/// <see cref="TrackedTextBlock"/>'un OnRender/MeasureOverride'ı bunu çağırır, testler de doğrudan.
/// </summary>
public static class TrackedGlyphs
{
    /// <param name="RenderedText">Uppercase (varsa) uygulanmış, glyph'lere eşlenen nihai metin.</param>
    /// <param name="GlyphIndices">RenderedText ile aynı uzunlukta, karakter başına glyph index.</param>
    /// <param name="AdvanceWidths">RenderedText ile aynı uzunlukta, karakter başına advance (glyphAdvance×FontSize + FontSize×TrackingEm).</param>
    /// <param name="TotalWidth">AdvanceWidths toplamı.</param>
    public readonly record struct Result(string RenderedText, ushort[] GlyphIndices, double[] AdvanceWidths, double TotalWidth);

    private static readonly Result Empty = new(string.Empty, [], [], 0.0);

    /// <summary>
    /// Karakter başına advance = <c>glyphTypeface.AdvanceWidths[glyphIndex] * fontSize + fontSize * trackingEm</c>
    /// (design brief formülü, birebir). Uppercase, glyph eşlemesinden ÖNCE <c>ToUpperInvariant()</c> ile
    /// uygulanır. Eksik glyph (kapsam dışı karakter) → .notdef (index 0) ile sessiz fallback; bu kontrol
    /// yalnız A–Z + boşluk için kullanılacağından (FontAssetTests/TrackedGlyphsTests kapsamı doğrular),
    /// geniş fallback zinciri (Segoe UI Symbol vb.) kapsam dışı — YAGNI.
    /// </summary>
    public static Result Build(GlyphTypeface glyphTypeface, string text, double fontSize, double trackingEm, bool uppercase)
    {
        ArgumentNullException.ThrowIfNull(glyphTypeface);

        string rendered = uppercase ? (text ?? string.Empty).ToUpperInvariant() : text ?? string.Empty;
        if (rendered.Length == 0)
            return Empty; // 0 genişlik, çökme yok

        var glyphIndices = new ushort[rendered.Length];
        var advanceWidths = new double[rendered.Length];
        double tracking = fontSize * trackingEm;
        double total = 0.0;

        for (int i = 0; i < rendered.Length; i++)
        {
            ushort glyphIndex = glyphTypeface.CharacterToGlyphMap.TryGetValue(rendered[i], out ushort mapped)
                ? mapped
                : (ushort)0; // .notdef fallback — bkz. yukarıdaki xml doc
            double glyphAdvance = glyphTypeface.AdvanceWidths.TryGetValue(glyphIndex, out double w) ? w : 0.0;
            double advance = glyphAdvance * fontSize + tracking;

            glyphIndices[i] = glyphIndex;
            advanceWidths[i] = advance;
            total += advance;
        }

        return new Result(rendered, glyphIndices, advanceWidths, total);
    }
}
