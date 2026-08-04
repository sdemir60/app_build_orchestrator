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
