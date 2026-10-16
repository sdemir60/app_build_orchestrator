using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [2026-10-16, ölçüldü] App'te 3B YOKTUR: ne <c>Viewport3D</c> ne <c>System.Windows.Media.Media3D</c> ad alanı. Bir <c>Viewport3D</c>'nin ilk
/// çizimi pencerenin boyutuyla orantılı grafik belleğini sürece bağlar ve WPF, öğe gizlenip boşaltılsa da onu geri vermez: 4K tam ekranda
/// konsolun eski 3B panel geçişi, proje sayfasına ilk tıklamada uygulamayı ~300 MB'den 1,1 GB'ye çıkarıp orada bırakıyordu (aynı binary,
/// yalnız o geçiş kapalıyken ~330 MB). Geçiş 2B'ye alındı (<c>ConsoleView.PlayTiltIn</c>); bu guard 3B'nin başka bir panelden sessizce geri
/// dönmesini önler — davranış testleri bunu ancak 4K bir pencerede görür, bu guard her yerde görür.
///
/// <para>Kardeş kaynak guard'ları gibi <see cref="SourceGuard"/> ile taranır; kaynak taramadan önce yorumsuz, literalsiz koda indirilir
/// (<see cref="SourceLiterals.CodeOnly"/>): kuralı ANLATAN doküman ya da XAML yorumu kuralı İHLAL eden kod değildir. XAML da taranır —
/// <c>&lt;Viewport3D</c> bir XAML öğesi olarak da gelebilir.</para>
/// </summary>
public sealed class NoViewport3DGuardTests
{
    private static readonly Regex ThreeD = new(@"\bViewport3D\b|\bMedia3D\b", RegexOptions.Compiled);

    [Fact]
    public void The_app_has_no_3D_element_and_no_Media3D_using()
    {
        // Vakum kapısı: tarama GERÇEKTEN konsolun görünümünü görmeli.
        Assert.Contains(SourceGuard.ScannedAppFiles("*.cs"), f => f.EndsWith("ConsoleView.xaml.cs", StringComparison.Ordinal));
        Assert.Contains(SourceGuard.ScannedAppFiles("*.xaml"), f => f.EndsWith("ConsoleView.xaml", StringComparison.Ordinal));

        var offenders = SourceGuard.ScanSrcIdentifiers("*.cs", ThreeD)
            .Concat(RepoPaths.AppSourceFiles("*.xaml").SelectMany(f =>
                SourceGuard.ScanText(Path.GetRelativePath(RepoPaths.SrcRoot, f), MarkupOnly(File.ReadAllText(f)), ThreeD)))
            .Where(o => o.StartsWith("BuildOrchestrator.App", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0,
            "App'te 3B öğe ya da Media3D kullanımı var (4K'da ~830 MB grafik belleği — ARCHITECTURE §20):" + Environment.NewLine + "  "
            + string.Join(Environment.NewLine + "  ", offenders));
    }

    /// <summary>XAML, XML yorumları satır sayısı bozulmadan boşluğa çevrilerek taranır — kuralı anlatan bir XAML yorumu ihlal değildir.</summary>
    private static string MarkupOnly(string xaml) =>
        Regex.Replace(xaml, "<!--[\\s\\S]*?-->", m => Regex.Replace(m.Value, "[^\r\n]", " "));

    /// <summary>Guard'ın kendi kanıtı (C#): koddaki 3B yakalanır, yorumdaki ve literaldeki anılması yakalanmaz.</summary>
    [Theory]
    [InlineData("using System.Windows.Media.Media3D;\n", 1)]
    [InlineData("    internal Viewport3D Tilt3D => PART_Tilt3D;\n", 1)]
    [InlineData("    /// bir Viewport3D 4K'da ~830 MB bağlar (Media3D)\n", 0)]
    [InlineData("    string why = \"Viewport3D is banned\";\n", 0)]
    public void The_rule_sees_code_but_not_comments_or_strings(string source, int expected)
    {
        Assert.Equal(expected, SourceGuard.ScanCodeIdentifiers("Fake.cs", source, ThreeD).Count);
    }

    /// <summary>Guard'ın kendi kanıtı (XAML): öğe olarak 3B yakalanır, XML yorumundaki anılması yakalanmaz.</summary>
    [Theory]
    [InlineData("    <Viewport3D x:Name=\"PART_Tilt3D\" Visibility=\"Collapsed\" />\n", 1)]
    [InlineData("    <!-- bir Viewport3D 4K'da ~830 MB baglar -->\n    <Grid />\n", 0)]
    public void The_rule_sees_markup_but_not_xml_comments(string xaml, int expected)
    {
        Assert.Equal(expected, SourceGuard.ScanText("Fake.xaml", MarkupOnly(xaml), ThreeD).Count);
    }
}
