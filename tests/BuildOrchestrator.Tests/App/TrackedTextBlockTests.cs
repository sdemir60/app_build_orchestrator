using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T57] TrackedTextBlock: DP kablajı + GlyphRun/Measure kanalı. pack:// tabanlı varsayılan FontFamily
/// xunit host'ta çözülmez (FontAssetTests deseniyle aynı sebep) — testler <see cref="TestFontFamily"/> ile
/// TestAssets/Fonts'a (aynı OTF dosyaları) file:// tabanlı bir FontFamily enjekte eder; production kod
/// yolu (BuildGlyphRun/MeasureOverride) DEĞİŞMEDEN aynen egzersiz edilir.
/// </summary>
public class TrackedTextBlockTests
{
    static string FontsDir => Path.Combine(AppContext.BaseDirectory, "TestAssets", "Fonts");

    // ConsoleView/FontAbWindow'daki pack URI kalıbının file:// karşılığı — aynı klasördeki OTF'ler
    // ("Geist" iç aile adını taşıyan Geist-Regular/Medium/SemiBold.otf) taranarak çözülür.
    static FontFamily TestFontFamily => new(new Uri(FontsDir + Path.DirectorySeparatorChar), "./#Geist");
    static GlyphTypeface Load(string file) => new(new Uri(Path.Combine(FontsDir, file)));

    static TrackedTextBlock MakeBlock(string text, bool uppercase = true, double trackingEm = 0.07, double fontSize = 11.0)
        => new()
        {
            Text = text,
            Uppercase = uppercase,
            TrackingEm = trackingEm,
            FontSize = fontSize,
            FontFamily = TestFontFamily,
        };

    [StaFact]
    public void Defaults_match_design_v1_caps_label_spec()
    {
        var block = new TrackedTextBlock();

        Assert.Equal(string.Empty, block.Text);
        Assert.Equal(0.07, block.TrackingEm);
        Assert.Equal(11.0, block.FontSize);
        Assert.True(block.Uppercase);
        Assert.Equal(FontWeights.Medium, block.FontWeight);
        Assert.Equal("./#Geist", block.FontFamily.Source);
    }

    [StaFact]
    public void BuildGlyphRun_advance_total_equals_glyph_advance_plus_per_character_tracking()
    {
        var typeface = Load("Geist-Medium.otf");
        const string text = "dependency graph";
        const double fontSize = 11.0;
        const double trackingEm = 0.07;
        var block = MakeBlock(text, trackingEm: trackingEm, fontSize: fontSize);

        var run = block.BuildGlyphRun();

        Assert.NotNull(run);
        string upper = text.ToUpperInvariant();
        double expectedTotal = upper.Sum(c => typeface.AdvanceWidths[typeface.CharacterToGlyphMap[c]] * fontSize)
                                + upper.Length * fontSize * trackingEm;
        Assert.Equal(upper.Length, run.GlyphIndices.Count);
        Assert.Equal(expectedTotal, run.AdvanceWidths.Sum(), precision: 9);
    }

    [StaFact]
    public void BuildGlyphRun_uses_uppercase_glyphs_for_lowercase_input()
    {
        var typeface = Load("Geist-Medium.otf");
        var block = MakeBlock("dependency graph");

        var run = block.BuildGlyphRun();

        Assert.NotNull(run);
        ushort[] expectedGlyphs = "DEPENDENCY GRAPH".Select(c => typeface.CharacterToGlyphMap[c]).ToArray();
        Assert.Equal(expectedGlyphs, run.GlyphIndices);
    }

    [StaFact]
    public void MeasureOverride_width_matches_BuildGlyphRun_advance_total()
    {
        var block = MakeBlock("projects");

        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var run = block.BuildGlyphRun();

        Assert.NotNull(run);
        Assert.Equal(run.AdvanceWidths.Sum(), block.DesiredSize.Width, precision: 9);
    }

    [StaFact]
    public void Empty_text_yields_zero_width_and_null_glyph_run_no_crash()
    {
        var block = MakeBlock(string.Empty);

        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Null(block.BuildGlyphRun());
        Assert.Equal(0.0, block.DesiredSize.Width);
    }

    [StaFact]
    public void Uppercase_false_keeps_original_casing_glyphs()
    {
        var typeface = Load("Geist-Medium.otf");
        var block = MakeBlock("Graph", uppercase: false);

        var run = block.BuildGlyphRun();

        Assert.NotNull(run);
        ushort[] expectedGlyphs = "Graph".Select(c => typeface.CharacterToGlyphMap[c]).ToArray();
        Assert.Equal(expectedGlyphs, run.GlyphIndices);
    }

    [StaFact]
    public void Foreground_resolves_to_Brush_TextFaint_token_when_merged_in_logical_tree()
    {
        // pack:// üzerinden App.xaml/Tokens.xaml merge zinciri xunit host'ta yok — TokenBrushesTests
        // deseniyle aynı: Tokens.xaml'i dosyadan yükleyip bir logical-tree ata (Border) üstünde
        // Resources olarak sun; SetResourceReference'ın (hardcode değil, gerçek token tüketimi) doğru
        // anahtarı ("Brush.TextFaint") çözdüğünü kanıtlar.
        string tokensPath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "Resources", "Tokens.xaml");
        using var stream = File.OpenRead(tokensPath);
        var tokens = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream);

        var block = new TrackedTextBlock();
        var host = new System.Windows.Controls.Border { Resources = tokens, Child = block };

        var expected = (Color)ColorConverter.ConvertFromString("#54545c")!;
        var actual = Assert.IsType<SolidColorBrush>(block.Foreground).Color;
        Assert.Equal(expected, actual);
        GC.KeepAlive(host);
    }

    [StaFact]
    public void Property_change_invalidates_measure()
    {
        var block = MakeBlock("projects");
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double before = block.DesiredSize.Width;

        block.Text = "dependency graph";
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.NotEqual(before, block.DesiredSize.Width);
    }
}
