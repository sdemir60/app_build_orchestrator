using System.IO;
using System.Reflection;
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

    /// <summary>
    /// KAYNAK GUARD'ı: ürün adı hiçbir üretim dosyasında literal olarak YAZILMAZ. Title bar'daki başlık
    /// metni bunu ihlal ediyordu (<c>MainWindow.xaml</c>, <c>Text="Build Orchestrator"</c>) ve About hero'su
    /// üçüncü kopya olacaktı — ikisi de artık <see cref="AppIdentity.Product"/> okur.
    /// <para><c>Window.Title</c> HARİÇ TUTULMAZ: o da aynı sabitten sürülebilir. Tek meşru literal
    /// <c>Directory.Build.props</c>'taki <c>&lt;Product&gt;</c>'tır ve o bir MSBuild dosyasıdır — bu tarama
    /// yalnız <c>src/BuildOrchestrator.App</c> altındaki .cs/.xaml dosyalarına bakar.</para>
    /// </summary>
    [Fact]
    public void No_app_source_file_writes_the_product_name_as_a_literal()
    {
        string literal = "\"" + AppIdentity.Product + "\"";
        var offenders = RepoPaths.AppSourceFiles("*.cs").Concat(RepoPaths.AppSourceFiles("*.xaml"))
            .Where(f => File.ReadAllText(f).Contains(literal, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepoPaths.AppSrcRoot, f))
            .ToList();

        Assert.Empty(offenders);
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
        var vm = new RunViewModel(engine, MainWindowHost.NeverTickingBatcher(), () => "r1");

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
