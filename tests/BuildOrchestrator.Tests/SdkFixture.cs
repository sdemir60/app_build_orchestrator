using System.IO;

namespace BuildOrchestrator.Tests;

/// <summary>
/// [A1] SDK-style test fixture'larının ortak parçası. SDK-style proje, düzeni oynatan hiçbir ayar yokken SDK'nın varsayılan
/// çıktı yolunu alır (<c>CsprojEvaluator</c>, ARCHITECTURE §6.2) ve kanıt mekanizmasına (§7.6) girer: kaydı olsa da diskte
/// çıktısı olmayan proje <c>output missing</c> okur. İmza ve içerik kararını sınayan Sync testleri diske çıktı yazmaz; onların
/// SDK projeleri bu ayarı taşır ve bilerek KANITSIZ kalır — yazıldıkları rejim budur. Kanıt mekanizması legacy fixture'la
/// (<c>LegacyFixture</c>) ve varsayılan düzenli SDK projesiyle ayrı testlerde sınanır.
/// </summary>
internal static class SdkFixture
{
    /// <summary>SDK'nın varsayılan düzenini oynatan, gerçek projelerde de görülen bir ayar: çıktı <c>bin\&lt;Configuration&gt;\</c>
    /// altına, hedef framework klasörü olmadan yazılır. Değerlendirici bu düzende yolu yaklaşık TÜRETMEZ.</summary>
    public const string EvidenceNeutralLayout = "<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>";

    /// <summary><paramref name="directory"/>'ye boş <c>Directory.Build.props</c> ve <c>Directory.Build.targets</c> yazar: yukarı arama
    /// (MSBuild'inki de değerlendiricininki de) orada durur — gerçek repoların deseni; sonuç makinenin üst klasörlerine bağlı
    /// kalmaz.</summary>
    public static void WriteSearchStoppers(string directory)
    {
        File.WriteAllText(Path.Combine(directory, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(directory, "Directory.Build.targets"), "<Project />");
    }
}
