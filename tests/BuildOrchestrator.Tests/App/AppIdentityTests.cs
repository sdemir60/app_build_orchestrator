using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Ürün kimliği TEK kaynaktan gelir: <c>Directory.Build.props</c> → assembly attribute'ları →
/// <see cref="AppIdentity"/>. UI hiçbir yerde ürün adını, sürümü ya da telif metnini yeniden yazmaz.
/// </summary>
[Collection("Console UI (serial)")] // EngineHost/VM kuran StaFact'lerle seri (kaynak çekişmesi deseni)
public class AppIdentityTests
{
    private static readonly XNamespace None = "";

    private static XDocument Props() =>
        XDocument.Load(Path.Combine(RepoPaths.RepoRoot, "Directory.Build.props"));

    [Fact]
    public void Product_and_version_come_from_the_assembly_not_from_a_literal()
    {
        var assembly = typeof(AppIdentity).Assembly;
        Assert.Equal(assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product, AppIdentity.Product);
        Assert.Equal(
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            AppIdentity.Version);
    }

    /// <summary>Telif TEK PARÇA olarak attribute'tan gelir — yıl ve şirket adı UI'da birleştirilmez ve
    /// <c>DateTime.Now.Year</c> KULLANILMAZ (telif yılı bir çalışma-zamanı değeri değildir).</summary>
    [Fact]
    public void Copyright_is_declared_once_in_directory_build_props()
    {
        string declared = Props().Descendants(None + "Copyright").Single().Value;
        Assert.False(string.IsNullOrWhiteSpace(declared));
        Assert.Equal(declared, AppIdentity.Copyright);
        Assert.Equal(declared,
            typeof(AppIdentity).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright);
    }

    /// <summary>[design v1.19.0 §2.10] Telif metni tasarımın yazdığı gibi: <c>© 2026 Delta Yazılım</c>. Değer
    /// props'ta TEK yerde durur; About sekmesinin Copyright satırı ve assembly attribute'u oradan okur.
    /// <para><b>[DEĞİŞEN KURAL — design v1.19.0]</b> ESKİ DEĞER <c>© 2026 Delta</c> idi ve yalnız "tek kaynak"
    /// kuralı pinliydi, metnin kendisi değil. Tasarım firma adını tam yazar; props dosyası UTF-8 kalmalı (ı).</para></summary>
    [Fact]
    public void The_copyright_reads_as_the_design_writes_it()
    {
        Assert.Equal("© 2026 Delta Yazılım", Props().Descendants(None + "Copyright").Single().Value);
        Assert.Equal("© 2026 Delta Yazılım", AppIdentity.Copyright);
    }

    /// <summary>[design v1.19.0 §2.10] About sekmesinin tanım paragrafı — metin birebir, tek yeri
    /// <see cref="AppIdentity.Overview"/>.</summary>
    [Fact]
    public void The_overview_paragraph_is_verbatim()
        => Assert.Equal(
            "Build Orchestrator discovers the projects under the repository root, works out the dependency graph and "
            + "builds in that order — only what changed, in parallel where the graph allows. The plan, the running "
            + "build and its result stay visible while it works.",
            AppIdentity.Overview);

    [Fact]
    public void The_tagline_is_a_single_sentence()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppIdentity.Tagline));
        Assert.EndsWith(".", AppIdentity.Tagline, StringComparison.Ordinal);
    }

    /// <summary>Tırnaklı TAM ürün adı (tırnaklarıyla). Ham metin kuralıdır: adın bir cümleye gömülü hâlini YAKALAMAZ — onu
    /// <see cref="EmbeddedNameRule"/> tarar (<see cref="No_source_file_embeds_the_product_name_in_a_literal"/>). Ad testte de yazılmaz, kimlikten
    /// okunur.</summary>
    private static readonly Regex QuotedNameRule = new(Regex.Escape("\"" + AppIdentity.Product + "\""));

    /// <summary>Ürün adının kendisi — bir literalin HERHANGİ bir yerinde. Kaynak literalleri üzerinde çalışır
    /// (<see cref="SourceGuard.ScanLiteralText"/>): yorumdaki ad ihlal değildir.</summary>
    private static readonly Regex EmbeddedNameRule = new(Regex.Escape(AppIdentity.Product));

    /// <summary>
    /// KAYNAK GUARD'ı: ürün adı hiçbir üretim dosyasında literal olarak YAZILMAZ. Title bar'daki başlık
    /// metni bunu ihlal ediyordu (<c>MainWindow.xaml</c>, <c>Text="Build Orchestrator"</c>) ve About hero'su
    /// üçüncü kopya olacaktı — ikisi de artık <see cref="AppIdentity.Product"/> okur.
    /// <para><c>Window.Title</c> HARİÇ TUTULMAZ: o da aynı sabitten sürülebilir. Tek meşru literal
    /// <c>Directory.Build.props</c>'taki <c>&lt;Product&gt;</c>'tır ve o bir MSBuild dosyasıdır — bu tarama
    /// <c>src</c> altındaki .cs/.xaml dosyalarına bakar.</para>
    /// <para><b>[DEĞİŞEN KAPSAM — kaynak şeridi, fix 1]</b> Eski iddia: tarama yalnız <c>src/BuildOrchestrator.App</c>'e bakardı
    /// (<c>No_app_source_file_writes_the_product_name_as_a_literal</c>). Core'daki şerit tooltip'leri ürün adını içeren kullanıcı metni
    /// üretir ve ilk yazımda adı literal olarak gömmüştü (review I-1); kural App'e özgü değil projenin kuralıdır. Tarama temizdi — Core,
    /// Supervisor ve Contracts'ta tırnaklı tam ad yoktu — bu yüzden kapsam hiçbir şey gevşetilmeden tüm <c>src</c>'ye genişledi ve her
    /// üretim projesinin gerçekten taranan kümede olduğu ayrıca assert edilir (boş bir tarama guard'ı sessizce yeşil bırakırdı).</para>
    /// </summary>
    [Fact]
    public void No_source_file_writes_the_product_name_as_a_literal()
    {
        var offenders = SourceGuard.ScanSrc("*.cs", QuotedNameRule).Concat(SourceGuard.ScanSrc("*.xaml", QuotedNameRule)).ToList();

        Assert.Empty(offenders);
        SourceGuard.AssertScansEveryProductionProject("*.cs");
    }

    /// <summary>
    /// Guard'ın KENDİ kanıtı (tırnaklı ad kuralı): tırnaklı tam ad satır numarasıyla raporlanır; cümleye gömülü ad bu kuralın görüş alanı
    /// DIŞINDADIR — onu <see cref="No_source_file_embeds_the_product_name_in_a_literal"/> yakalar. İki guard'ın iş bölümü budur.
    /// Kuralın hangi projeleri taradığı burada sınanmaz (yol yalnız bir rapor etiketidir): onu <see cref="No_source_file_writes_the_product_name_as_a_literal"/>
    /// içindeki "her üretim projesi tarandı" iddiası pinler.
    /// </summary>
    [Fact]
    public void The_quoted_name_rule_reports_a_planted_literal_with_its_line_but_not_a_name_inside_a_sentence()
    {
        const string fake = """
            internal static class Planted
            {
                public const string Title = "%PRODUCT%";
                public const string Sentence = "CPU used by %PRODUCT% — app, engine and compilers";
            }
            """;
        string planted = fake.Replace("%PRODUCT%", AppIdentity.Product);

        var hits = SourceGuard.ScanText("Planted.cs", planted, QuotedNameRule);

        string only = Assert.Single(hits);
        Assert.Contains("Planted.cs:3", only);
    }

    /// <summary>
    /// Ürün adının bir literalin PARÇASI olarak yazılabildiği TEK yerler — <c>src</c> köküne göre, her biri gerekçesiyle. Geri kalan her üretim
    /// dosyasında (sonradan eklenenler dahil) ad hiçbir literalin parçası olamaz: cümle <see cref="AppIdentity.Product"/> ile kurulur ya da ad
    /// çağırandan parametre gelir (Core'daki şerit tooltip'leri gibi).
    /// <list type="bullet">
    /// <item><c>Core/MsBuild/WpfTemporaryAssemblyTargets.cs</c>: motorun kendi önbelleğine (<c>%LOCALAPPDATA%\…\msbuild\</c>) yazdığı MSBuild
    /// dosyasındaki bir XML YORUMU — dosyayı kimin yazdığını söyler, hiçbir yüzeyde görünmez. Core App'in kimliğini görmez; adı bu yorum için
    /// kendi assembly'sinin özniteliğinden okumak, sabit bir dosya içeriğini bir çalışma zamanı okumasına bağlardı — kullanıcıya hiçbir kazancı
    /// yok.</item>
    /// </list>
    /// Muafiyet dosyaya değil oradaki TEK literale aittir: aynı dosyaya eklenen ikinci bir ad yakalanır
    /// (<see cref="The_exemptions_are_exactly_the_files_that_still_embed_the_name"/>).
    /// </summary>
    private static readonly string[] EmbeddedNameExemptions =
    [
        Path.Combine("BuildOrchestrator.Core", "MsBuild", "WpfTemporaryAssemblyTargets.cs"),
    ];

    /// <summary>
    /// [design v1.23.0 §2.12] KAYNAK GUARD'ı: ürün adı hiçbir üretim dosyasında bir literalin PARÇASI değildir. Tırnaklı-ad guard'ı yalnız TAM
    /// tırnaklı adı yakalar; cümleye gömülmüş ad (<c>Updating &lt;ürün&gt;</c>, <c>CPU used by &lt;ürün&gt; — …</c>, <c>Release notes for
    /// &lt;ürün&gt;.</c>) ondan kaçar. Tarama bütün <c>src</c>'dir, muaf olan yalnız <see cref="EmbeddedNameExemptions"/>. Ad testte de yazılmaz,
    /// kimlikten okunur.
    /// <para><b>[DEĞİŞEN KAPSAM — temizlik madde 2]</b> Eski iddia opt-in bir listeydi (<c>NameFreeSurfaceFiles</c>: güncelleme yüzeyinin dört
    /// dosyası ve <c>ResourceStripText.cs</c>; test <c>The_name_free_surfaces_never_embed_the_product_name_in_a_literal</c>). Adı cümleye gömen YENİ
    /// bir dosya görünmezdi ve dört eski kullanım listenin dışında borç olarak duruyordu: ayar dosyası filtresi (<c>SettingsFile.FileFilter</c>),
    /// What's new alt başlığı (<c>NotesDialog.xaml</c>), font spike'ının örnek satırı ve MSBuild yorumu. İlk üçü artık adı kimlikten okur,
    /// dördüncüsü gerekçeli istisnadır; güncelleme yüzeyi ve şerit metni iddiası bütün <c>src</c>'nin parçası olarak sürer.</para>
    /// </summary>
    [Fact]
    public void No_source_file_embeds_the_product_name_in_a_literal()
    {
        Assert.Empty(SourceGuard.ScanSrcCodeAndMarkupLiterals(EmbeddedNameRule, EmbeddedNameExemptions));
        SourceGuard.AssertScansEveryProductionProject("*.cs");
    }

    /// <summary>
    /// Guard'ın KENDİ kanıtı (istisnalar): istisnasız tarama TAM OLARAK istisna listesindeki dosyaları raporlar, her birinde TEK literal. Üç şeyi
    /// birden pinler: tarama istisna dosyasını gerçekten görür (muafiyet olmasa yakalardı); listede bayat bir istisna kalmaz — adı artık taşımayan bir
    /// dosya listeden düşmelidir, yoksa oraya ileride yazılan ad sessizce muaf kalırdı; ve muafiyet o tek literalle sınırlıdır — aynı dosyaya
    /// eklenen ikinci bir ad kırmızı verir (bütün-dal incelemesi: dosya düzeyindeki karşılaştırma onu görmüyordu).
    /// </summary>
    [Fact]
    public void The_exemptions_are_exactly_the_files_that_still_embed_the_name()
    {
        var hits = SourceGuard.ScanSrcCodeAndMarkupLiterals(EmbeddedNameRule);
        var files = hits.Select(hit => hit[..hit.IndexOf(':')]).Distinct(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(EmbeddedNameExemptions.Order(StringComparer.OrdinalIgnoreCase), files.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(EmbeddedNameExemptions.Length, hits.Count);
    }

    /// <summary>
    /// Guard'ın KENDİ kanıtı (kural): hiçbir listede adı geçmeyen YENİ bir dosyaya gömülen ad — düz ve interpolated C# literalinde ve bir XAML
    /// özniteliğinde — satır numarasıyla yakalanır; yorumdaki ad yakalanmaz. Hangi dosyaların tarandığı burada sınanmaz: onu guard'ın kendisi ("her
    /// üretim projesi tarandı") ve istisna testi pinler. Eklenen satırlar şerit metninin ilk yazımındaki ve What's new alt başlığının eski biçimidir.
    /// </summary>
    [Fact]
    public void The_embedded_name_rule_catches_a_name_planted_in_a_new_file_but_not_in_a_comment()
    {
        const string code = """
            // %PRODUCT% yalnız bir yorumda: guard literalleri tarar, yorum ihlal değildir.
            internal static class Planted
            {
                public const string Cpu = "CPU used by %PRODUCT% — app, engine and compilers";
                public static string Memory(string app) => $"Memory used by %PRODUCT% · app {app}";
            }
            """;
        const string markup = """
            <!-- %PRODUCT% yalnız bir yorumda -->
            <TextBlock Text="Release notes for %PRODUCT%." />
            """;
        string newFile = Path.Combine("BuildOrchestrator.App", "Views", "PlantedSurface");
        Assert.DoesNotContain(EmbeddedNameExemptions, exemption => exemption.StartsWith(newFile, StringComparison.OrdinalIgnoreCase));

        var codeHits = SourceGuard.ScanLiteralText(newFile + ".xaml.cs", code.Replace("%PRODUCT%", AppIdentity.Product), ".cs", EmbeddedNameRule);
        var markupHits = SourceGuard.ScanLiteralText(newFile + ".xaml", markup.Replace("%PRODUCT%", AppIdentity.Product), ".xaml", EmbeddedNameRule);

        Assert.Equal(2, codeHits.Count); // C# literali tırnaklarıyla raporlanır, XAML özniteliğinin değeri tırnaksız
        Assert.StartsWith(newFile + ".xaml.cs:4: \"CPU used by", codeHits[0]);
        Assert.StartsWith(newFile + ".xaml.cs:5: $\"Memory used by", codeHits[1]);
        Assert.StartsWith(newFile + ".xaml:2: Release notes for", Assert.Single(markupHits));
    }

    /// <summary>
    /// KAYNAK GUARD'ı: uygulama ikonunun pack URI'si de TEK yerde yazılır — <see cref="AppIdentity.AppIconUri"/>.
    ///
    /// <para>Ürün adının kardeşi: ikon da ürün kimliğidir ve iki tüketicisi vardır (pencere/taskbar ikonu ve
    /// tepsi bildiriminin büyük ikonu). İkinci bir literal kopyası SESSİZCE ayrışır — dosya adı ya da klasör
    /// değişince biri düzelir, diğeri çalışma zamanında patlar (ctor'da çözülemeyen bir kaynak). Guard sabitin
    /// KENDİ dosyasını hariç tutar ve geri kalan tüm .cs/.xaml'de literali arar.</para></summary>
    [Fact]
    public void The_app_icon_uri_is_written_in_exactly_one_place()
    {
        var offenders = RepoPaths.AppSourceFiles("*.cs").Concat(RepoPaths.AppSourceFiles("*.xaml"))
            .Where(f => File.ReadAllText(f).Contains(AppIdentity.AppIconUri, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepoPaths.AppSrcRoot, f))
            .ToList();

        Assert.Equal([Path.Combine("Services", "AppIdentity.cs")], offenders);
    }

    // ------------------------------------------------------------------ motor kimliği

    /// <summary>Motor sürümü + PID artık SAKLANIR. Önceden <c>OnEngineReady</c> sürümü yalnız konsol satırına
    /// yazıp atıyordu ve <c>EngineReadyEvent.Pid</c> hiç kullanılmıyordu; About ikisini de gösterir.</summary>
    [StaFact]
    public async Task Engine_ready_stores_the_version_and_pid_and_still_writes_the_boot_line()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, MainWindowHost.NeverTickingBatcher(), () => "r1")
            { LegacyWorktreePoolRoot = TestPaths.MissingLegacyPoolRoot }; // [final review M8]

        Assert.Null(vm.EngineVersion);
        Assert.Null(vm.EnginePid);

        vm.OnEngineReady("1.0.0+it5", 4242);

        Assert.Equal("1.0.0+it5", vm.EngineVersion);
        Assert.Equal(4242, vm.EnginePid);
        // Konsolun boot satırı DEĞİŞMEDİ (davranış aynı; değer ayrıca saklanıyor). Kardeşi:
        // EnginePreflightTests.Engine_ready_writes_the_version_into_the_console_boot_line.
        Assert.Contains("Engine ready — v1.0.0+it5", vm.GetRunDocumentText(), StringComparison.Ordinal);
    }
}
