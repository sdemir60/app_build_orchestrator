using System.IO;
using System.Windows.Media;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [It-0 · T64] Gömülü Geist/Geist Mono asset'lerinin kabul testleri (ağırlık ayrışması, ASCII kapsamı,
/// gerekli UI glif'lerinin bir sağlayıcısı olması) + gömülü font <b>pack URI'sinin TEK yerde</b> (AppFonts)
/// yazıldığının guard'ı.
/// </summary>
public class FontAssetTests
{
    static string FontsDir => Path.Combine(AppContext.BaseDirectory, "TestAssets", "Fonts");
    static GlyphTypeface Load(string file) => new(new Uri(Path.Combine(FontsDir, file)));

    [StaTheory]
    [InlineData("Geist-Regular.otf", 400)] [InlineData("Geist-Medium.otf", 500)] [InlineData("Geist-SemiBold.otf", 600)]
    [InlineData("GeistMono-Regular.otf", 400)] [InlineData("GeistMono-Medium.otf", 500)] [InlineData("GeistMono-SemiBold.otf", 600)]
    public void Weights_400_500_600_are_distinct_files(string file, int weight) // It-0 kabul: "400/500/600 ayrışıyor"
        => Assert.Equal(weight, Load(file).Weight.ToOpenTypeWeight());

    [StaFact]
    public void GeistMono_covers_full_ascii()
    {
        var map = Load("GeistMono-Regular.otf").CharacterToGlyphMap;
        for (char c = ' '; c <= '~'; c++) Assert.True(map.ContainsKey(c), $"ASCII eksik: U+{(int)c:X4} '{c}'");
    }

    [StaFact]
    public void Required_ui_glyphs_have_a_provider() // glif kapsam testi: Geist Mono VEYA fallback (Segoe UI Symbol)
    {
        char[] required = ['▸', '⌄', '▲', '✗', '✓', '⟳', '·', '—', '…', '→'];
        var mono = Load("GeistMono-Regular.otf").CharacterToGlyphMap;
        var segoe = new FontFamily("Segoe UI Symbol");
        var report = new List<string>();
        foreach (char c in required)
        {
            if (mono.ContainsKey(c)) { report.Add($"U+{(int)c:X4} '{c}': GeistMono"); continue; }
            bool fallback = segoe.GetTypefaces().Any(t => t.TryGetGlyphTypeface(out var g) && g.CharacterToGlyphMap.ContainsKey(c));
            Assert.True(fallback, $"'{c}' (U+{(int)c:X4}) ne Geist Mono'da ne Segoe UI Symbol'de — imleç/chevron gibi ÇİZİM gerekebilir (A13)");
            report.Add($"U+{(int)c:X4} '{c}': FALLBACK(SegoeUISymbol)");
        }
        // rapor kayda girer (Task 14): FALLBACK satırları CompositeFont FamilyMap'ine (Task 12) taşınır
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "glyph-coverage.txt"), report);
    }

    [Fact]
    public void Geist_SIL_OFL_licence_ships_with_the_application()
    {
        // [T64] OFL §"the above copyright notice ... shall be included in all copies": gömülü OTF'ler
        // uygulamanın içinde dağıtıldığı için lisans metni de ÇIKTIYA kopyalanmak zorunda (csproj Content +
        // CopyToOutputDirectory). Kaynaktaki varlığı yeterli DEĞİL — bu yüzden çıktı dizini de kontrol edilir.
        string source = Path.Combine(RepoPaths.AppSrcRoot, "Assets", "GEIST-LICENSE.txt");
        Assert.True(File.Exists(source), $"lisans kaynakta yok: {source}");
        Assert.Contains("SIL Open Font License", File.ReadAllText(source), StringComparison.Ordinal);

        string copied = Path.Combine(AppContext.BaseDirectory, "Assets", "GEIST-LICENSE.txt");
        Assert.True(File.Exists(copied), $"lisans çıktıya kopyalanmamış: {copied}");
    }

    [StaFact]
    public void Font_pack_uri_is_declared_in_exactly_one_place()
    {
        // [T64] AppFonts'un kendi doc'u "pack URI'yi her tüketicide tekrar yazma" diyordu ama kural üç yerde
        // ihlal ediliyordu (TrackedTextBlock/ConsoleView/FontAbWindow). Spikes HARİÇ: FontAbWindow T65'in
        // referans kabuğudur, App'ten bağımsız kalmalıdır (bkz. T65 kararı) — bilinçli tek istisna.
        int sites = RepoPaths.AppSourceFiles("*.cs")
            .Where(f => !f.Contains("Spikes", StringComparison.Ordinal))
            .Count(f => File.ReadAllText(f).Contains("component/Fonts/", StringComparison.Ordinal));

        Assert.Equal(1, sites);                                        // yalnız AppFonts.cs
    }
}
