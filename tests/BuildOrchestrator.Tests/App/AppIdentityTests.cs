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
    /// <see cref="EmbeddedNameRule"/> ile <see cref="NameFreeSurfaceFiles"/> tarar. Ad testte de yazılmaz, kimlikten okunur.</summary>
    private static readonly Regex QuotedNameRule = new(Regex.Escape("\"" + AppIdentity.Product + "\""));

    /// <summary>Ürün adının kendisi — bir literalin HERHANGİ bir yerinde. Kaynak literalleri üzerinde çalışır
    /// (<see cref="SourceGuard.ScanLiteralText"/>): yorumdaki ad ihlal değildir.</summary>
    private static readonly Regex EmbeddedNameRule = new(Regex.Escape(AppIdentity.Product));

    /// <summary>Üretim projelerinin klasörleri (<c>src</c> altında).</summary>
    private static readonly string[] ProductionProjects =
        ["BuildOrchestrator.App", "BuildOrchestrator.Core", "BuildOrchestrator.Supervisor", "BuildOrchestrator.Contracts"];

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

        var scanned = SourceGuard.ScannedSrcFiles("*.cs");
        foreach (string project in ProductionProjects)
            Assert.Contains(scanned, f => f.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    /// <summary>
    /// Guard'ın KENDİ kanıtı (tırnaklı ad kuralı): tırnaklı tam ad Core yolunda bile satır numarasıyla raporlanır; cümleye gömülü ad bu
    /// kuralın görüş alanı DIŞINDADIR — onu <see cref="The_name_free_surfaces_never_embed_the_product_name_in_a_literal"/> yakalar. İki
    /// guard'ın iş bölümü budur.
    /// </summary>
    [Fact]
    public void The_quoted_name_rule_sees_a_planted_literal_in_core_but_not_a_name_inside_a_sentence()
    {
        const string fake = """
            internal static class Planted
            {
                public const string Title = "%PRODUCT%";
                public const string Sentence = "CPU used by %PRODUCT% — app, engine and compilers";
            }
            """;
        string planted = fake.Replace("%PRODUCT%", AppIdentity.Product);

        var hits = SourceGuard.ScanText(Path.Combine("BuildOrchestrator.Core", "Diagnostics", "Planted.cs"), planted, QuotedNameRule);

        string only = Assert.Single(hits);
        Assert.Contains("Planted.cs:3", only);
    }

    /// <summary>
    /// Ürün adını bir literalin PARÇASI olarak taşıyamayan yüzeylerin kaynak dosyaları — <c>src</c> köküne göre. Bunların cümleleri ürün
    /// adını İÇERİR; ad ya <see cref="AppIdentity.Product"/>'tan okunur ya da çağırandan parametre gelir:
    /// <list type="bullet">
    /// <item>Güncelleme yüzeyi (hap, kart, restart ekranı): <c>Updating …</c>, <c>Closing …</c> cümleleri.</item>
    /// <item>Kaynak şeridinin metinleri (Core): tooltip'ler (<c>CPU used by …</c>, <c>Memory used by …</c>) adı parametre alır; Core'da
    /// ad hiçbir literalin parçası değildir.</item>
    /// </list>
    /// Liste bilerek AÇIK ve dardır: adı cümleye gömen eski yerler de var (ör. ayar dosyası filtresi, notlar penceresinin alt başlığı),
    /// bu yüzden aynı kural tüm <c>src</c>'ye uygulanamaz — bir yüzey buraya, adı kimlikten okur hâle getirildiğinde eklenir.
    /// </summary>
    private static readonly string[] NameFreeSurfaceFiles =
    [
        Path.Combine("BuildOrchestrator.App", "ViewModels", "UpdateText.cs"),
        Path.Combine("BuildOrchestrator.App", "ViewModels", "UpdateRestartTimeline.cs"),
        Path.Combine("BuildOrchestrator.App", "Views", "UpdateRestartScreen.xaml"),
        Path.Combine("BuildOrchestrator.App", "Views", "UpdateRestartScreen.xaml.cs"),
        Path.Combine("BuildOrchestrator.Core", "Diagnostics", "ResourceStripText.cs"),
    ];

    /// <summary>
    /// [design v1.23.0 §2.12] KAYNAK GUARD'ı: restart ekranının cümleleri ürün adını İÇERİR (<c>Updating &lt;ürün&gt;</c>,
    /// <c>Closing &lt;ürün&gt;…</c>) ve tırnaklı-ad guard'ı yalnız TAM tırnaklı adı yakalar — cümleye gömülmüş ad ondan kaçar.
    /// <see cref="NameFreeSurfaceFiles"/> dosyalarında ad hiçbir literalin PARÇASI olamaz; cümle <see cref="AppIdentity.Product"/> ile
    /// kurulur (ya da ad parametre gelir). Ad testte de yazılmaz, kimlikten okunur.
    /// <para><b>[DEĞİŞEN KAPSAM — kaynak şeridi, fix 1]</b> Eski iddia: yalnız güncelleme yüzeyi
    /// (<c>The_update_surface_never_embeds_the_product_name_in_a_literal</c>, App'e göreli liste). Kaynak şeridinin Core'daki tooltip
    /// metinleri de ürün adını içeren kullanıcı metnidir ve ilk yazımda adı literalin içine gömmüştü (review I-1: <c>"CPU used by …"</c>
    /// sabiti ve <c>$"Memory used by … · app {…}"</c>) — tırnaklı-ad guard'ı bunu göremezdi. Liste artık <c>src</c> köküne göreli ve
    /// <c>ResourceStripText.cs</c>'i de içerir; güncelleme yüzeyi iddiası aynen sürer.</para>
    /// </summary>
    [Fact]
    public void The_name_free_surfaces_never_embed_the_product_name_in_a_literal()
    {
        var offenders = NameFreeSurfaceFiles
            .SelectMany(f => SourceGuard.ScanLiteralText(
                f, File.ReadAllText(Path.Combine(RepoPaths.SrcRoot, f)), Path.GetExtension(f), EmbeddedNameRule))
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Guard'ın KENDİ kanıtı (şerit metni kapsamı): dosya listede, diskte ve tokenizer ondan literal çıkarıyor (boş bir tarama sessizce
    /// yeşil kalırdı); gerçek dosya temiz; AYNI dosyaya eklenen — adı bir cümleye gömen düz ve interpolated — literaller satır
    /// numaralarıyla yakalanır, yorumdaki ad yakalanmaz. Eklenen iki satır ilk yazımdaki şerit metninin tam biçimidir.
    /// </summary>
    [Fact]
    public void The_name_free_guard_sees_a_planted_literal_in_the_strip_text_scope()
    {
        string relative = Path.Combine("BuildOrchestrator.Core", "Diagnostics", "ResourceStripText.cs");
        Assert.Contains(relative, NameFreeSurfaceFiles);
        string real = File.ReadAllText(Path.Combine(RepoPaths.SrcRoot, relative));
        Assert.NotEmpty(SourceLiterals.From(real, ".cs"));
        Assert.Empty(SourceGuard.ScanLiteralText(relative, real, ".cs", EmbeddedNameRule));

        const string fake = """
            // %PRODUCT% yalnız bir yorumda: guard literalleri tarar, yorum ihlal değildir.
            internal static class Planted
            {
                public const string Cpu = "CPU used by %PRODUCT% — app, engine and compilers";
                public static string Memory(string app) => $"Memory used by %PRODUCT% · app {app}";
            }
            """;
        string planted = real + "\n" + fake.Replace("%PRODUCT%", AppIdentity.Product) + "\n";

        var hits = SourceGuard.ScanLiteralText(relative, planted, ".cs", EmbeddedNameRule);

        Assert.Equal(2, hits.Count);
        Assert.Contains("CPU used by", hits[0]);
        Assert.Contains("Memory used by", hits[1]);
        int realLines = real.Count(c => c == '\n');
        Assert.All(hits, hit => Assert.True(int.Parse(hit.Split(':')[1]) > realLines, $"planted line expected after the real file: {hit}"));
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
