using System.IO;
using BuildOrchestrator.Core.MsBuild;

namespace BuildOrchestrator.Tests.MsBuild;

/// <summary>
/// [PERF Faz E3] packages.config restore kanıtı: içerik özeti (SHA-256) + listelenen her paketin NuGet'in kendi
/// "kurulu" işaretiyle yerinde olması — <c>&lt;solutionDir&gt;\packages\&lt;id&gt;.&lt;version&gt;\&lt;id&gt;.&lt;version&gt;.nupkg</c>.
/// Karar yalnız İÇERİĞE bakar — dosyanın tarihi değişse de özet aynı kalır; kanıt eksik kaldığında (özet farklı ya da
/// kayıtsız, klasör ya da .nupkg eksik, XML bozuk ya da DTD taşıyor) cevap "tatmin edilmedi"dir ve restore koşar
/// (güvenli taraf).
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

    /// <summary>NuGet'in packages.config düzeninde KURULU bir paket: <c>packages\&lt;kimlik&gt;\&lt;kimlik&gt;.nupkg</c>
    /// (kimlik = <c>id.version</c>; NuGet .nupkg'yi çıkarmanın EN SONUNDA yazar). .nupkg'nin yolunu döner — silinirse
    /// geriye yarıda kesilmiş bir restore'un bıraktığı klasör kalır. Paket fixture'ının TEK yeri: RunCoordinatorTests de
    /// bunu kullanır (kopya YASAK).</summary>
    internal static string InstallPackage(string solutionDir, string identity)
    {
        string folder = Path.Combine(solutionDir, "packages", identity);
        Directory.CreateDirectory(folder);
        string nupkg = Path.Combine(folder, identity + ".nupkg");
        File.WriteAllBytes(nupkg, []);
        return nupkg;
    }

    /// <summary>Proje dizinine packages.config yazar, verilen paketleri çözüm dizinine kurar
    /// (<see cref="InstallPackage"/>) ve dosyanın bugünkü özetini (deftere yazılacak değeri) döner.</summary>
    private string Arrange(string content, params string[] installed)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PackagesConfig)!);
        File.WriteAllText(PackagesConfig, content);
        foreach (string identity in installed)
            InstallPackage(_solutionDir, identity);
        return RestoreEvidence.HashOf(PackagesConfig)!;
    }

    /// <summary>Özet aynı ve her paket kurulu → tatmin edildi; sayı listelenen paket sayısıdır. Dosyanın tarihi ileri
    /// alınsa da karar değişmez (tarih karara girmez).</summary>
    [Fact]
    public void Same_content_and_every_package_installed_is_satisfied_whatever_the_file_date()
    {
        string recorded = Arrange(TwoPackages, "Newtonsoft.Json.13.0.3", "Dapper.2.1.35");
        File.SetLastWriteTimeUtc(PackagesConfig, DateTime.UtcNow.AddDays(1));

        Assert.True(RestoreEvidence.IsSatisfied(PackagesConfig, _solutionDir, recorded, out int present));
        Assert.Equal(2, present);
        Assert.True(RestoreEvidence.IsSatisfied(PackagesConfig, _solutionDir, recorded)); // üç argümanlı biçim aynı kararı verir
    }

    /// <summary>İçerik değişti (yeni sürüm bile kurulu) → tatmin edilmedi: yalnız özet farkı karar verir.</summary>
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

    /// <summary>Yarıda kesilen restore (Stop, zaman aşımı, dosya kilidi) paket klasörünü açmış ama .nupkg'yi
    /// yazamamıştır → tatmin edilmedi: NuGet de paketi kurulu saymaz; sonraki koşu restore eder.</summary>
    [Fact]
    public void A_package_folder_without_its_nupkg_is_not_satisfied()
    {
        string recorded = Arrange(TwoPackages, "Newtonsoft.Json.13.0.3");
        File.Delete(InstallPackage(_solutionDir, "Dapper.2.1.35"));

        Assert.False(RestoreEvidence.IsSatisfied(PackagesConfig, _solutionDir, recorded));
    }

    /// <summary>Özet aynı ama XML bozuk → tatmin edilmedi (paket listesi okunamıyor; restore koşar).</summary>
    [Fact]
    public void Malformed_xml_is_not_satisfied()
    {
        string recorded = Arrange("<packages><package id=\"Dapper\" version=", "Dapper.2.1.35");

        Assert.False(RestoreEvidence.IsSatisfied(PackagesConfig, _solutionDir, recorded));
    }

    /// <summary>DTD yasak: DOCTYPE taşıyan packages.config → tatmin edilmedi. Varlık sürümü kurulu paketin sürümüne
    /// açar — DTD işlenseydi kanıt tatmin edilirdi; yasak dosyayı restore'a düşürür.</summary>
    [Fact]
    public void A_doctype_is_not_satisfied()
    {
        string recorded = Arrange(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE packages [ <!ENTITY dapperVersion "2.1.35"> ]>
            <packages>
              <package id="Dapper" version="&dapperVersion;" />
            </packages>
            """, "Dapper.2.1.35");

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
