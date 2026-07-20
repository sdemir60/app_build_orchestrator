using System.IO;
using System.Linq;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T57] Advance-width matematiği SAF fonksiyon olarak (UI/DrawingContext bağımsız): karakter başına
/// FontSize×TrackingEm ek + uppercase dönüşümü. Girdi <see cref="GlyphTypeface"/> TestAssets'ten dosya
/// Uri ile yüklenir (pack:// xunit host'ta çözülmez — FontAssetTests ile aynı desen).
/// </summary>
public class TrackedGlyphsTests
{
    static string FontsDir => Path.Combine(AppContext.BaseDirectory, "TestAssets", "Fonts");
    static GlyphTypeface Load(string file) => new(new Uri(Path.Combine(FontsDir, file)));

    [StaFact]
    public void Build_sums_glyph_advance_plus_per_character_tracking()
    {
        var typeface = Load("Geist-Medium.otf");
        const string text = "dependency graph";
        const double fontSize = 11.0;
        const double trackingEm = 0.07;

        var result = TrackedGlyphs.Build(typeface, text, fontSize, trackingEm, uppercase: true);

        string upper = text.ToUpperInvariant();
        double expectedTotal = 0.0;
        foreach (char c in upper)
        {
            ushort glyphIndex = typeface.CharacterToGlyphMap[c];
            expectedTotal += typeface.AdvanceWidths[glyphIndex] * fontSize + fontSize * trackingEm;
        }

        Assert.Equal(upper.Length, result.GlyphIndices.Length);
        Assert.Equal(upper.Length, result.AdvanceWidths.Length);
        Assert.Equal(expectedTotal, result.TotalWidth, precision: 9);
        // N karakter × (fontSize*trackingEm) ek payı toplamda ayrıca doğrulanır (kanıt: brief'teki formül).
        Assert.Equal(upper.Length * fontSize * trackingEm, result.AdvanceWidths.Sum() - upper.Sum(c => typeface.AdvanceWidths[typeface.CharacterToGlyphMap[c]] * fontSize), precision: 9);
    }

    [StaFact]
    public void Build_maps_uppercase_glyphs_not_lowercase()
    {
        var typeface = Load("Geist-Medium.otf");
        var result = TrackedGlyphs.Build(typeface, "graph", fontSize: 11.0, trackingEm: 0.07, uppercase: true);

        Assert.Equal("GRAPH", result.RenderedText);
        for (int i = 0; i < result.RenderedText.Length; i++)
        {
            ushort expectedGlyph = typeface.CharacterToGlyphMap[result.RenderedText[i]];
            Assert.Equal(expectedGlyph, result.GlyphIndices[i]);
        }
    }

    [StaFact]
    public void Build_without_uppercase_keeps_original_casing()
    {
        var typeface = Load("Geist-Medium.otf");
        var result = TrackedGlyphs.Build(typeface, "Graph", fontSize: 11.0, trackingEm: 0.07, uppercase: false);

        Assert.Equal("Graph", result.RenderedText);
    }

    [StaFact]
    public void Build_empty_text_returns_zero_width_no_crash()
    {
        var typeface = Load("Geist-Medium.otf");

        var result = TrackedGlyphs.Build(typeface, string.Empty, fontSize: 11.0, trackingEm: 0.07, uppercase: true);

        Assert.Equal(0.0, result.TotalWidth);
        Assert.Empty(result.GlyphIndices);
        Assert.Empty(result.AdvanceWidths);
    }

    [StaFact]
    public void Build_null_text_treated_as_empty_no_crash()
    {
        var typeface = Load("Geist-Medium.otf");

        var result = TrackedGlyphs.Build(typeface, null!, fontSize: 11.0, trackingEm: 0.07, uppercase: true);

        Assert.Equal(0.0, result.TotalWidth);
        Assert.Empty(result.GlyphIndices);
    }

    [StaFact]
    public void Geist_covers_uppercase_az_and_space_no_fallback_needed()
    {
        var map = Load("Geist-Medium.otf").CharacterToGlyphMap;
        for (char c = 'A'; c <= 'Z'; c++)
            Assert.True(map.ContainsKey(c), $"Geist'te eksik glyph: '{c}'");
        Assert.True(map.ContainsKey(' '), "Geist'te eksik glyph: boşluk");
    }
}
