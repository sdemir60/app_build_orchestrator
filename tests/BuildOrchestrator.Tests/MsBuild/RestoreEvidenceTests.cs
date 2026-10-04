using System.IO;
using BuildOrchestrator.Core.MsBuild;

namespace BuildOrchestrator.Tests.MsBuild;

/// <summary>
/// [PERF Faz E3] packages.config restore kanıtı: içerik özeti (SHA-256) + listelenen paketlerin
/// <c>&lt;solutionDir&gt;\packages\&lt;id&gt;.&lt;version&gt;\</c> klasörleri. Karar yalnız İÇERİĞE bakar — dosyanın
/// tarihi değişse de özet aynı kalır; kanıt eksik kaldığında (özet farklı ya da kayıtsız, klasör eksik, XML bozuk)
/// cevap "tatmin edilmedi"dir ve restore koşar (güvenli taraf).
/// </summary>
public sealed class RestoreEvidenceTests : IDisposable
{
    private const string TwoPackages =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <packages>
          <package id="Newtonsoft.Json" version="13.0.3" targetFramework="net48" />
          <package id="Dapper" version="2.1.35" targetFramework="net48" />
        </packages>
        """;

    private readonly string _solutionDir = Directory.CreateTempSubdirectory("bo-restore-evidence-").FullName;

    private string PackagesConfig => Path.Combine(_solutionDir, "App", "packages.config");

    public void Dispose()
    {
        try { Directory.Delete(_solutionDir, recursive: true); } catch (IOException) { /* test temizliği */ }
    }

    /// <summary>Proje dizinine packages.config yazar, verilen paket klasörlerini çözüm dizininin <c>packages\</c>
    /// altına açar ve dosyanın bugünkü özetini (deftere yazılacak değeri) döner.</summary>
    private string Arrange(string content, params string[] presentFolders)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PackagesConfig)!);
        File.WriteAllText(PackagesConfig, content);
        foreach (string folder in presentFolders)
            Directory.CreateDirectory(Path.Combine(_solutionDir, "packages", folder));
        return RestoreEvidence.HashOf(PackagesConfig)!;
    }

    /// <summary>Özet aynı ve her paketin klasörü yerinde → tatmin edildi; sayı listelenen paket sayısıdır. Dosyanın
    /// tarihi ileri alınsa da karar değişmez (tarih karara girmez).</summary>
    [Fact]
    public void Same_content_and_every_package_folder_present_is_satisfied_whatever_the_file_date()
    {
        string recorded = Arrange(TwoPackages, "Newtonsoft.Json.13.0.3", "Dapper.2.1.35");
        File.SetLastWriteTimeUtc(PackagesConfig, DateTime.UtcNow.AddDays(1));

        Assert.True(RestoreEvidence.IsSatisfied(PackagesConfig, _solutionDir, recorded, out int present));
        Assert.Equal(2, present);
        Assert.True(RestoreEvidence.IsSatisfied(PackagesConfig, _solutionDir, recorded)); // üç argümanlı biçim aynı kararı verir
    }

    /// <summary>İçerik değişti (yeni sürümün klasörü bile yerinde) → tatmin edilmedi: yalnız özet farkı karar verir.</summary>
    [Fact]
    public void Changed_content_is_not_satisfied()
    {
        string recorded = Arrange(TwoPackages, "Newtonsoft.Json.13.0.3", "Dapper.2.1.35", "Dapper.2.1.66");
        File.WriteAllText(PackagesConfig, TwoPackages.Replace("2.1.35", "2.1.66"));

        Assert.False(RestoreEvidence.IsSatisfied(PackagesConfig, _solutionDir, recorded));
    }

    /// <summary>Özet aynı ama bir paketin klasörü yok → tatmin edilmedi.</summary>
    [Fact]
    public void A_missing_package_folder_is_not_satisfied()
    {
        string recorded = Arrange(TwoPackages, "Newtonsoft.Json.13.0.3");

        Assert.False(RestoreEvidence.IsSatisfied(PackagesConfig, _solutionDir, recorded));
    }

    /// <summary>Özet aynı ama XML bozuk → tatmin edilmedi (paket listesi okunamıyor; restore koşar).</summary>
    [Fact]
    public void Malformed_xml_is_not_satisfied()
    {
        string recorded = Arrange("<packages><package id=\"Dapper\" version=", "Dapper.2.1.35");

        Assert.False(RestoreEvidence.IsSatisfied(PackagesConfig, _solutionDir, recorded));
    }

    /// <summary>Özeti olmayan kayıt (alan eklenmeden önce yazılmış ya da hiç derlenmemiş) → tatmin edilmedi.</summary>
    [Fact]
    public void A_record_without_a_hash_is_not_satisfied()
    {
        Arrange(TwoPackages, "Newtonsoft.Json.13.0.3", "Dapper.2.1.35");

        Assert.False(RestoreEvidence.IsSatisfied(PackagesConfig, _solutionDir, recordedHash: null));
    }

    /// <summary>Özet yalnız içeriktir: dosya yoksa null, tarih değişince aynı, içerik değişince farklı.</summary>
    [Fact]
    public void Hash_is_content_only_and_null_without_a_file()
    {
        Assert.Null(RestoreEvidence.HashOf(PackagesConfig));

        string first = Arrange(TwoPackages);
        File.SetLastWriteTimeUtc(PackagesConfig, DateTime.UtcNow.AddDays(-3));
        Assert.Equal(first, RestoreEvidence.HashOf(PackagesConfig));

        File.WriteAllText(PackagesConfig, TwoPackages + " ");
        Assert.NotEqual(first, RestoreEvidence.HashOf(PackagesConfig));
    }
}
