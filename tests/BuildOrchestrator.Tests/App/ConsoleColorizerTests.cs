using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T56/3a] ConsoleColorizer + ConsoleLineParser/Classifier: satır düz metninden offset-bazlı renk aralıkları
/// (saat=text-faint, ▸=amber-text, gövde=tip rengi). Brush'lar TOKEN'dan (Tokens.xaml) — headless host'ta
/// dosyadan yüklenir (TokenBrushesTests deseni). Belge DÜZ metin kalır; renk yalnız görsel katmandır.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ConsoleColorizerTests
{
    private const char Arrow = '▸';  // ▸
    private const char Check = '✓';  // ✓

    private static ResourceDictionary LoadTokens()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestAssets", "Resources", "Tokens.xaml");
        using var stream = File.OpenRead(path);
        return (ResourceDictionary)XamlReader.Load(stream);
    }

    private static ConsolePalette Palette(ResourceDictionary tokens) => ConsolePalette.FromLookup(k => tokens[k]);

    [StaFact]
    public void Command_line_with_clock_and_arrow_produces_faint_clock_amber_arrow_and_primary_body()
    {
        var tokens = LoadTokens();
        var palette = Palette(tokens);
        var colorizer = new ConsoleColorizer(palette);
        string line = $"12:04:07 {Arrow} git fetch origin main";

        var spans = colorizer.ComputeSpans(line);

        // saat [0,8) = text-faint
        var clock = spans[0];
        Assert.Equal(0, clock.Offset);
        Assert.Equal(8, clock.Length);
        Assert.Same(tokens["Brush.TextFaint"], clock.Brush);

        // ▸ (index 9) = amber-text, tam 1 karakter
        var arrow = spans.Single(s => s.Length == 1 && s.Offset == line.IndexOf(Arrow));
        Assert.Same(tokens["Brush.AmberText"], arrow.Brush);

        // gövde (son span) = cmd = text-primary; hiçbir span text-secondary/success DEĞİL
        Assert.Same(tokens["Brush.TextPrimary"], spans[^1].Brush);
        Assert.All(spans, s => Assert.True(s.Length > 0));
        // aralıklar ardışık ve satırı tam kaplar
        Assert.Equal(0, spans[0].Offset);
        Assert.Equal(line.Length, spans[^1].Offset + spans[^1].Length);
        for (int i = 1; i < spans.Count; i++)
            Assert.Equal(spans[i - 1].Offset + spans[i - 1].Length, spans[i].Offset);
    }

    [StaFact]
    public void Plain_info_line_without_clock_or_arrow_is_a_single_secondary_span()
    {
        var tokens = LoadTokens();
        var colorizer = new ConsoleColorizer(Palette(tokens));
        string line = "Restoring project references";

        var spans = colorizer.ComputeSpans(line);

        var span = Assert.Single(spans);
        Assert.Equal(0, span.Offset);
        Assert.Equal(line.Length, span.Length);
        Assert.Same(tokens["Brush.TextSecondary"], span.Brush); // info
    }

    [StaFact]
    public void Error_and_warning_and_success_bodies_map_to_their_status_text_brushes()
    {
        var tokens = LoadTokens();
        var colorizer = new ConsoleColorizer(Palette(tokens));

        Assert.Same(tokens["Brush.StatusFailText"],
            colorizer.ComputeSpans("OSYS.Sales.Core failed — 2 errors").Single().Brush);
        Assert.Same(tokens["Brush.StatusCycleText"],
            colorizer.ComputeSpans("warning NU1701: package restored").Single().Brush);
        Assert.Same(tokens["Brush.StatusSuccessText"],
            colorizer.ComputeSpans($"Build {Check} succeeded").Single().Brush);
    }

    [Fact]
    public void Classifier_maps_reliable_signals_and_defaults_to_info()
    {
        Assert.Equal(ConsoleLineType.Cmd, ConsoleLineClassifier.Classify($"{Arrow} msbuild Osys.sln /m:4"));
        Assert.Equal(ConsoleLineType.Cmd, ConsoleLineClassifier.Classify($"12:04:07 {Arrow} git fetch"));
        Assert.Equal(ConsoleLineType.Error, ConsoleLineClassifier.Classify("[hata] stop gönderilemedi: x"));
        Assert.Equal(ConsoleLineType.Error, ConsoleLineClassifier.Classify("Program.cs(9,5): error CS0103: name"));
        Assert.Equal(ConsoleLineType.Warn, ConsoleLineClassifier.Classify("csc : warning CS1591: missing doc"));
        Assert.Equal(ConsoleLineType.Success, ConsoleLineClassifier.Classify("Build succeeded in 2.9s"));
        Assert.Equal(ConsoleLineType.Info, ConsoleLineClassifier.Classify("Determining projects to restore..."));
        Assert.Equal(ConsoleLineType.Info, ConsoleLineClassifier.Classify(""));
    }

    [Fact]
    public void Parser_detects_clock_and_arrow_offsets_and_leaves_plain_lines_bare()
    {
        var cmd = ConsoleLineParser.Layout($"12:04:07 {Arrow} git");
        Assert.Equal(ConsoleLineType.Cmd, cmd.Type);
        Assert.Equal(new ConsoleSpan(0, 8), cmd.Clock);
        Assert.Equal(new ConsoleSpan(9, 1), cmd.Icon);

        var plain = ConsoleLineParser.Layout("no clock here");
        Assert.Null(plain.Clock);
        Assert.Null(plain.Icon);

        // 8-haneli olmayan / geçersiz saat prefix'i saat sayılmaz
        Assert.Null(ConsoleLineParser.Layout("1:2:3 short").Clock);
        Assert.Null(ConsoleLineParser.Layout("abcdefgh not a clock").Clock);
    }

    [StaFact]
    public void ComputeSpans_never_mutates_the_input_text_color_is_view_only()
    {
        var tokens = LoadTokens();
        var colorizer = new ConsoleColorizer(Palette(tokens));
        string line = $"12:04:07 {Arrow} git fetch";

        _ = colorizer.ComputeSpans(line);

        Assert.Equal($"12:04:07 {Arrow} git fetch", line); // string immutable, ama niyeti kanıtla
        Assert.Empty(colorizer.ComputeSpans(""));           // boş satır → boş aralık listesi
    }

    [StaFact]
    public void Colorizer_wired_into_ConsoleView_keeps_document_text_plain()
    {
        var tokens = LoadTokens();
        var view = new ConsoleView();
        view.EnableColorizer(Palette(tokens));

        view.AppendBatch($"12:04:07 {Arrow} git fetch origin main\n");
        view.AppendBatch("OSYS.Domain.Service failed — 2 errors\n");

        // Belge markup'sız düz metin — kopyalanınca anlamlı (renk yalnız LineTransformer katmanı).
        Assert.Equal($"12:04:07 {Arrow} git fetch origin main\nOSYS.Domain.Service failed — 2 errors\n",
            view.Document.Text);
    }
}
