using System.IO;
using System.Xml.Linq;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Üçüncü-taraf atıf tablosu. <b>Sürümler csproj'dan KOPYALANMAZ</b> — çalışma zamanında yüklenen
/// assembly'den okunur; aksi halde csproj'daki sürümü UI'da ikinci kez yazmış olurduk (kopya YASAK).
/// </summary>
public class ThirdPartyNoticesTests
{
    private static readonly XNamespace None = "";

    private static XDocument AppCsproj() =>
        XDocument.Load(Path.Combine(RepoPaths.AppSrcRoot, "BuildOrchestrator.App.csproj"));

    [Fact]
    public void Every_component_has_a_display_name_a_license_and_a_url()
    {
        Assert.NotEmpty(ThirdPartyNotices.All);
        foreach (var component in ThirdPartyNotices.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(component.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(component.License));
            Assert.StartsWith("https://", component.Url, StringComparison.Ordinal);
        }
    }

    /// <summary>Assembly adı verilen her kaydın sürümü GERÇEKTEN çözülür — yanlış yazılmış bir assembly adı,
    /// UI'da sessizce boş bir sürüm göstermek yerine burada kırar.</summary>
    [Fact]
    public void Each_managed_component_resolves_a_version_at_runtime()
    {
        foreach (var component in ThirdPartyNotices.All.Where(c => c.AssemblyName is not null))
            Assert.False(string.IsNullOrWhiteSpace(ThirdPartyNotices.ResolveVersion(component)),
                $"{component.DisplayName}: '{component.AssemblyName}' assembly'si yüklenemedi");
    }

    /// <summary>Sürüm metni build metadata (<c>+sha</c>) taşımaz — atıf satırı kısa kalmalı.</summary>
    [Fact]
    public void Resolved_versions_carry_no_build_metadata()
    {
        foreach (var component in ThirdPartyNotices.All.Where(c => c.AssemblyName is not null))
            Assert.DoesNotContain('+', ThirdPartyNotices.ResolveVersion(component)!);
    }

    /// <summary>Font kaydının assembly'si YOKTUR (gömülü OTF) — sürüm alanı boş kalır, kırmaz.</summary>
    [Fact]
    public void The_font_component_has_no_assembly_and_no_version()
    {
        var font = ThirdPartyNotices.All.Single(c => c.AssemblyName is null);
        Assert.Null(ThirdPartyNotices.ResolveVersion(font));
        Assert.Contains("Open Font License", font.License, StringComparison.Ordinal);
    }

    /// <summary>csproj'un her <c>PackageReference</c>'ı tabloda görünür — bir paket eklenip atıfı unutulursa
    /// burada yakalanır (OSS uyumluluğu "unutulabilir" bir iş değildir).</summary>
    [Fact]
    public void Every_package_reference_is_attributed()
    {
        var referenced = AppCsproj().Descendants(None + "PackageReference")
            .Select(e => (string)e.Attribute("Include")!)
            .ToList();
        var attributed = ThirdPartyNotices.All
            .Select(c => c.DisplayName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(referenced);
        Assert.All(referenced, package => Assert.Contains(package, attributed));
    }

    [Fact]
    public void The_font_note_points_at_the_licence_file_that_actually_ships()
    {
        Assert.Contains("GEIST-LICENSE.txt", ThirdPartyNotices.FontLicenseNote, StringComparison.Ordinal);
        // Dosya gerçekten var; csproj'un onu çıktıya/publish'e kopyaladığını PublishLayoutTests pinler.
        Assert.True(File.Exists(Path.Combine(RepoPaths.AppSrcRoot, "Assets", "GEIST-LICENSE.txt")));
    }
}
