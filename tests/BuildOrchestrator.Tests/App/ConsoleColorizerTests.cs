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

    // [A13/T3 fix-1 · B5] Sözlük yükleme + palet kurulumu ARTIK tek yerde (DsResources) — buradaki kopya
    // XamlReader.Load(stream) yolunu kullanıyordu ve DsResources.Load'un yaptığı clr-namespace tamamlamasını
    // ATLIYORDU (Tokens.xaml bir gün App tipine atıf verirse sessizce ayrışırdı).
    private static ResourceDictionary LoadTokens() => DsResources.Load("Tokens.xaml");

    private static ConsolePalette Palette(ResourceDictionary tokens) => DsResources.ConsolePaletteFrom(tokens);


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
        // [DEĞİŞEN KURAL — design v1.7.0 §2.5] Komut satırının işareti artık ▸ öneki DEĞİL, satırın kendisidir:
        // ▸ kolonu ve saat damgası kaldırıldı, geriye uygulamanın çağırdığı aracın adı kaldı.
        Assert.Equal(ConsoleLineType.Cmd, ConsoleLineClassifier.Classify("msbuild Osys.sln /m:4"));
        Assert.Equal(ConsoleLineType.Cmd, ConsoleLineClassifier.Classify("git fetch origin main"));
        Assert.Equal(ConsoleLineType.Error, ConsoleLineClassifier.Classify("[hata] stop gönderilemedi: x"));
        Assert.Equal(ConsoleLineType.Error, ConsoleLineClassifier.Classify("Program.cs(9,5): error CS0103: name"));
        Assert.Equal(ConsoleLineType.Warn, ConsoleLineClassifier.Classify("csc : warning CS1591: missing doc"));
        Assert.Equal(ConsoleLineType.Success, ConsoleLineClassifier.Classify("Build succeeded in 2.9s"));
        Assert.Equal(ConsoleLineType.Info, ConsoleLineClassifier.Classify("Determining projects to restore..."));
        Assert.Equal(ConsoleLineType.Info, ConsoleLineClassifier.Classify(""));
    }

    // [KALDIRILDI — design v1.7.0 §2.5] Konsolun daktilosu, saat sütunu ve satır-bazlı kaskadı kaldırıldı;
    // bu iddiaların konusu artık yok. Yerlerine gelen davranış: satırlar anında basılır, prompt satırı yalnız
    // imleç + "ready" taşır, panel geçişi tek parça tilt-in'dir.

    [StaFact]
    public void ComputeSpans_never_mutates_the_input_text_color_is_view_only()
    {
        var tokens = LoadTokens();
        var colorizer = new ConsoleColorizer(Palette(tokens));
        string line = "git fetch origin main";

        _ = colorizer.ComputeSpans(line);

        Assert.Equal("git fetch origin main", line); // string immutable, ama niyeti kanıtla
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
