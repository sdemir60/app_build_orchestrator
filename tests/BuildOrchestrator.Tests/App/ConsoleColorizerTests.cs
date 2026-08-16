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

    /// <summary>
    /// <b>[DEĞİŞEN KURAL] Renk yalnız FORMATI BİLİNEN satırlara verilir.</b> Eski iddia serbest metinde
    /// <c>failed</c>/<c>succeeded</c> arıyordu; artık aranmıyor (gerekçe:
    /// <see cref="Classifier_colours_only_sources_whose_format_is_known"/>). Bu test, kalan iki kaynağın
    /// GERÇEK token fırçalarına bağlandığını doğrular — anahtar adı değil, çözülen fırça.
    /// </summary>
    [StaFact]
    public void Diagnostic_lines_map_to_their_status_text_brushes()
    {
        var tokens = LoadTokens();
        var colorizer = new ConsoleColorizer(Palette(tokens));

        Assert.Same(tokens["Brush.StatusFailText"],
            colorizer.ComputeSpans("Program.cs(9,5): error CS0103: name").Single().Brush);
        Assert.Same(tokens["Brush.StatusCycleText"],
            colorizer.ComputeSpans("warning NU1701: package restored").Single().Brush);

        // Serbest metindeki "failed"/"succeeded" ARTIK renk vermez — sıradan çıktı satırıdır.
        Assert.Same(tokens["Brush.TextSecondary"],
            colorizer.ComputeSpans("OSYS.Sales.Core failed — 2 errors").Single().Brush);
        Assert.Same(tokens["Brush.TextSecondary"],
            colorizer.ComputeSpans($"Build {Check} succeeded").Single().Brush);
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL] Yalnız FORMATI BİLİNEN kaynaklar renklenir; metin tahmini kaldırıldı.</b>
    ///
    /// <para><b>Eski iddia:</b> satırda <c>failed</c>/<c>succeeded</c>/<c>✓</c>/<c>✗</c> geçiyorsa kırmızı ya
    /// da yeşil; <c>warning</c> kelimesi nerede geçerse geçsin turuncu.</para>
    ///
    /// <para><b>Değişme gerekçesi (kullanıcı):</b> "kesin bir ayrım yoksa tek renk olabilir, tahmine göre
    /// yapıyorsak". O taramalar bir PROJE ADININ içindeki kelimeye de takılırdı; renk orada bilgi değil
    /// gürültüdür. Geriye iki kaynak kalır ve ikisi de tahmin değildir: MSBuild'in tanı satırı formatı ve
    /// uygulamanın kendi önekleri.</para>
    /// </summary>
    [Fact]
    public void Classifier_colours_only_sources_whose_format_is_known()
    {
        // Uygulamanın KENDİ bastığı satırlar — kaynağı biziz.
        Assert.Equal(ConsoleLineType.Cmd, ConsoleLineClassifier.Classify("msbuild Osys.sln /m:4"));
        Assert.Equal(ConsoleLineType.Cmd, ConsoleLineClassifier.Classify("git fetch origin main"));
        Assert.Equal(ConsoleLineType.Error, ConsoleLineClassifier.Classify("[hata] stop gönderilemedi: x"));
        Assert.Equal(ConsoleLineType.Warn, ConsoleLineClassifier.Classify("warning: git fetch failed"));

        // MSBuild'in tanı satırı formatı — kökenli ve kökensiz biçim.
        Assert.Equal(ConsoleLineType.Error, ConsoleLineClassifier.Classify("Program.cs(9,5): error CS0103: name"));
        Assert.Equal(ConsoleLineType.Warn, ConsoleLineClassifier.Classify("csc : warning CS1591: missing doc"));
        Assert.Equal(ConsoleLineType.Warn, ConsoleLineClassifier.Classify("warning NU1701: package restored"));

        // Bağımlılık uyarısı hem "warning" hem "failed" içerir: TURUNCU kalmalı, kırmızı değil.
        Assert.Equal(ConsoleLineType.Warn,
            ConsoleLineClassifier.Classify("warning: OSYS.Sales.Core failed in this run"));

        // ARTIK TAHMİN YOK: bu satırlar sıradan çıktıdır.
        Assert.Equal(ConsoleLineType.Info, ConsoleLineClassifier.Classify("Build succeeded in 2.9s"));
        Assert.Equal(ConsoleLineType.Info, ConsoleLineClassifier.Classify("Osys.Failed.Tests -> bin/x.dll"));
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
