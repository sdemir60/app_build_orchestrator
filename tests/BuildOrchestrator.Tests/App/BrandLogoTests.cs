using System.IO;
using System.Windows.Controls;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Marka logosu TEK yerde çizilir. Önceden Path verisi <c>MainWindow.xaml</c>'de inline duruyordu; About
/// hero'su onu ikinci kez yazacaktı (kopya YASAK, CLAUDE.md) — bu yüzden bir kontrole çıkarıldı.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class BrandLogoTests
{
    /// <summary>Logonun ayırt edici ilk figürü (amber accent parçası, design-v1 delta-logo-dark.svg).
    /// Bu dizgi kaynak ağacında TAM BİR kez geçmelidir.</summary>
    private const string SignatureFigure = "M81.069,13.488";

    [Fact]
    public void The_logo_geometry_is_declared_in_exactly_one_source_file()
    {
        var carriers = RepoPaths.AppSourceFiles("*.xaml")
            .Concat(RepoPaths.AppSourceFiles("*.cs"))
            .Where(f => File.ReadAllText(f).Contains(SignatureFigure, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepoPaths.AppSrcRoot, f))
            .ToList();

        Assert.Equal([Path.Combine("Controls", "BrandLogo.xaml")], carriers);
    }

    /// <summary>Kontrol GERÇEKTEN çiziyor: realize edildiğinde ağaçta boyanmış Path'ler var ve verilen
    /// yükseklikte oranını korur (Viewbox Uniform — logo geniş bir işarettir).</summary>
    [StaFact]
    public void The_logo_renders_its_paths_and_keeps_its_aspect_ratio()
    {
        var host = DsResources.NewHost();
        var logo = new BrandLogo { Height = 20 };
        var window = DsResources.Realize(host, logo);

        var paths = DsResources.Descendants(logo).OfType<System.Windows.Shapes.Path>().ToList();
        Assert.NotEmpty(paths);
        // Token fırçaları çözüldü (ham renk yasağını NoHardcodedColorTests ayrıca pinler).
        Assert.All(paths, p => Assert.NotNull(p.Fill));

        Assert.IsType<Viewbox>(logo.Content);
        Assert.Equal(20.0, logo.ActualHeight, precision: 1);
        Assert.True(logo.ActualWidth > logo.ActualHeight, "Uniform ölçek oranı korumadı (logo geniştir)");
        GC.KeepAlive(window);
    }
}
