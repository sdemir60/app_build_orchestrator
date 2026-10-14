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
        // [design v1.11.0 §1.1] warn AMBER'dir. [DEĞİŞEN KURAL] Eski iddia bunun cycle turuncusu
        // (`Brush.StatusCycleText`) olduğuydu; v1.11.0 turuncuyu bir statü kanalı olarak UI'dan tamamen
        // çıkardı — token dosyada kalır ama kimse okumaz.
        Assert.Same(tokens["Brush.AmberText"],
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

    /// <summary>[idle-memory · dalga 2 · fix 1 · I1] MSBuild'in kanonik tanı biçiminin (<c>CanonicalError</c>: köken <c>:</c>
    /// [alt kategori] <c>error|warning</c> [KOD] <c>:</c>) dört gerçekçi HATA hâli — TEK liste: kompakt konsolun "hatalar tam" testi de
    /// bunu kullanır (<see cref="CompactConsoleFilterTests"/>). Komut sözcüğüyle başlayan köken (düğüm çöküşü, eksik proje dosyası),
    /// metninde "Warning" geçen hata (NuGet'in SDK projelerinde varsayılan warning-as-error'u NU1605) ve alt kategorili biçim (XAML
    /// derleyicisi). Bu makinenin 1514 koşu logunda (662 k satır, Türkçe MSBuild) bu üç biçim yoktu: biçim MSBuild'in kendi tanımıdır.</summary>
    public static TheoryData<string> CanonicalMsBuildErrors { get; } = new()
    {
        "MSBUILD : error MSB4166: Child node \"2\" exited prematurely. Shutting down.",
        "MSBUILD : error MSB1009: Project file does not exist.",
        @"C:\x\A.csproj : error NU1605: Warning As Error: Detected package downgrade: X from 2.0 to 1.0 [C:\x\A.csproj]",
        @"C:\x\A.xaml(10,5): XamlCompiler error WMC1006: Cannot resolve the type 'X'",
    };

    /// <summary>
    /// [fix 1 · I1] Kanonik biçimin her hata hâli hatadır. Tür İLK tanı sözcüğüdür: <c>error NU1605: Warning As Error</c> hatadır —
    /// metindeki "Warning" sayılmaz.
    /// <para>KIRMIZI (eski sınıflandırıcı): <c>MSBUILD : …</c> satırları komut sözcüğüyle başladığı için komut (Cmd), NU1605 satırı
    /// <c>": Warning "</c> önce arandığı için uyarı, alt kategorili satır bilgi sayılıyordu.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(CanonicalMsBuildErrors))]
    public void Every_canonical_msbuild_error_form_is_an_error(string line) =>
        Assert.Equal(ConsoleLineType.Error, ConsoleLineClassifier.Classify(line));

    /// <summary>[dalga 2 son inceleme · defter 6] Kökensiz tanıda tür satırın başındaki İLK tanı sözcüğüdür: satır bir tür sözcüğüyle
    /// başlıyorsa kökensiz biçim önce denenir — metnin ilerisindeki iki noktalar ve onların önündeki "warning"/"error" sözcükleri
    /// türü değiştirmez, kod satır başındaki koddur.
    /// <para>KIRMIZI: düz köken araması önce koşuyordu — ilk iki noktaya kadarki bölüm ("error X1") köken sayılıyor, türü metnin
    /// ilerisindeki sözcük veriyordu (<c>error X1: see the warning list: y</c> → uyarı, kodu "list").</para></summary>
    [Theory]
    [InlineData("error X1: see the warning list: y", ConsoleLineType.Error, "X1")]
    [InlineData("warning NU1701: see the error list: y", ConsoleLineType.Warn, "NU1701")]
    public void An_origin_less_diagnostic_takes_its_type_and_code_from_the_first_category_word(string line, ConsoleLineType type,
        string code)
    {
        Assert.Equal(type, ConsoleLineClassifier.Classify(line.AsSpan(), out var parsed));
        Assert.Equal(code, parsed.ToString());
    }

    /// <summary>[fix 1 · I1 · koruma] Kanonik biçim yalnız tanı satırını tanır: sıradan bir komut satırı komut kalır; serbest metninde
    /// "error"/"warning" geçen satır — gerçek loglardaki csc komut satırı (<c>/errorreport:prompt</c>, 1599 satır), Türkçe MSBuild'in
    /// özet satırları (<c>    N Hata</c> / <c>    N Uyarı</c>, 1698'er satır) ve İngilizce MSBuild'in, terminal logger'ın, csc'nin
    /// özetleri — bilgi kalır.</summary>
    [Theory]
    [InlineData("msbuild Osys.sln /m:4", ConsoleLineType.Cmd)]
    [InlineData(@"C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\Roslyn\csc.exe /noconfig /nowarn:1701,1702 /fullpaths /nostdlib+ /errorreport:prompt /warn:4 /define:DEBUG;TRACE", ConsoleLineType.Info)]
    [InlineData("    0 Hata", ConsoleLineType.Info)]
    [InlineData("    3 Uyarı", ConsoleLineType.Info)]
    [InlineData("    0 Error(s)", ConsoleLineType.Info)]
    [InlineData("Build succeeded with 2 warning(s) in 3.1s", ConsoleLineType.Info)]
    [InlineData("Compile complete -- 0 errors, 1 warnings", ConsoleLineType.Info)]
    public void Command_lines_and_free_text_that_mentions_errors_keep_their_type(string line, ConsoleLineType type) =>
        Assert.Equal(type, ConsoleLineClassifier.Classify(line));

    /// <summary>
    /// [fix 1 · M4] Sınıflandırma ayırma yapmaz: kompakt konsol onu IPC thread'inde, VM'in kilidi altında her proje satırında sorar
    /// (OSYS koşusunda ~390 k satır) ve renklendirici her çizilen satırda. Isınmadan sonra binlerce karışık satır — bilgi, girintili,
    /// boş, komut, uyarı, hata, uygulamanın önekleri, kompakt özet, gerçek csc komut satırı — 0 bayt ayırır.
    /// <para>KIRMIZI (eski sınıflandırıcı): girintili her satırın kırpılmış kopyası (<c>TrimStart</c>) ve tanı aramasının satır başına
    /// birleştirdiği dört string — koşu başına yüz MB'ı aşan gen0 çöpü.</para>
    /// </summary>
    [Fact]
    public void Classifying_a_line_allocates_nothing()
    {
        string[] lines =
        [
            "  Copying file from X to Y", "Build succeeded.", "    0 Hata", "", "   ",
            "msbuild Osys.sln /m:4", "git fetch origin main",
            MainWindowHost.WarningLine(1), "warning NU1701: package restored",
            "csc : warning CS1591: missing doc", MainWindowHost.ErrorLine(2),
            "warning: R failed in this run", "[error] runFailed: x", "A: 7 more warnings — CS1591 ×7 — see the project log",
            @"C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\Roslyn\csc.exe /noconfig /nowarn:1701,1702 /fullpaths /errorreport:prompt",
            .. CanonicalMsBuildErrors.Select<object[], string>(row => (string)row[0]),
        ];
        foreach (string line in lines) ConsoleLineClassifier.Classify(line); // ısınma: JIT

        long allocated = PerfMeasure.AllocatedBy(() =>
        {
            for (int round = 0; round < 200; round++)
                foreach (string line in lines) ConsoleLineClassifier.Classify(line);
        });

        Assert.Equal(0, allocated);
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
