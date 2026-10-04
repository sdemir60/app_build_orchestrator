using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace BuildOrchestrator.Core.MsBuild;

/// <summary>
/// [PERF Faz E3] Bir packages.config projesinin restore'u atlanabilir mi — kararın TEK sahibi (saf; UI ve process
/// bağımsız test edilir). Kanıt yalnız İÇERİKTİR: <c>packages.config</c>'in SHA-256 özeti son başarıda deftere
/// yazılan özetle (<c>BuildState.PackagesConfigHash</c>) aynı olmalı ve dosyanın listelediği her paketin klasöründe
/// .nupkg'si bulunmalıdır: <c>&lt;solutionDir&gt;\packages\&lt;id&gt;.&lt;version&gt;\&lt;id&gt;.&lt;version&gt;.nupkg</c>.
/// NuGet packages.config düzeninde kurduğu her paketin klasöründe bu .nupkg'yi tutar (çalışma alanındaki gerçek paket
/// klasörlerinin hepsinde var). .nupkg'siz bir klasör — yarıda kesilen bir çıkarmanın (Stop, zaman aşımı, dosya kilidi)
/// bırakabileceği gibi — kanıt sayılmaz; sonraki koşu ya da tur yine restore eder. Tarih/mtime karara GİRMEZ. Kanıtın eksik kaldığı her durumda — özet
/// kayıtsız ya da farklı, dosya okunamıyor, XML bozuk ya da DTD taşıyor, bir klasör ya da .nupkg eksik — cevap
/// "tatmin edilmedi"dir ve restore koşar (güvenli taraf). Sınır: <c>nuget.config</c> okunmaz; <c>repositoryPath</c>
/// depoyu başka yere taşıdıysa ve çözümün <c>packages</c> klasöründe eski kopyalar kaldıysa kanıt onları görür
/// (toparlanma: Rebuild ya da HintPath hedefi eksikse Optimize — ARCHITECTURE §9.3). Restore'un atlandığını anlatan
/// decision.log satırının metni de burada durur; Supervisor yalnız çağırır (kopya YASAK).
/// </summary>
public static class RestoreEvidence
{
    // Savunma derinliği: packages.config'in DTD'ye ve dış kaynağa ihtiyacı yoktur, ikisi de kapalı — DOCTYPE taşıyan
    // ya da bozuk bir dosya XmlException verir ve karar restore'a düşer.
    private static readonly XmlReaderSettings SafeXml = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };

    /// <summary>packages.config içerik özeti (SHA-256 hex); dosya yoksa null. Okunamayan dosya da null döner —
    /// kanıt üretilemez, restore koşar.</summary>
    public static string? HashOf(string packagesConfigPath) =>
        ReadContent(packagesConfigPath) is { } content ? Hash(content) : null;

    /// <summary>Kayıtlı özet bugünküyle aynı VE packages.config'teki her &lt;package id= version=&gt; için
    /// &lt;solutionDir&gt;\packages\&lt;id&gt;.&lt;version&gt;\ klasörü ve içinde NuGet'in kurulu işareti
    /// &lt;id&gt;.&lt;version&gt;.nupkg varsa true. XML bozuksa false (restore koşar — güvenli taraf).</summary>
    public static bool IsSatisfied(string packagesConfigPath, string solutionDir, string? recordedHash) =>
        IsSatisfied(packagesConfigPath, solutionDir, recordedHash, out _);

    /// <summary>Üç argümanlı biçimle AYNI karar — o buna devreder, karar tek yerde kalır. Tatmin edildiğinde
    /// <paramref name="presentPackages"/> listelenen (kurulu) paket sayısıdır: decision.log satırının sayısı
    /// (<see cref="SkippedLine"/>). Özet ve paket listesi dosyanın TEK okumasından gelir — iki okuma arasında değişen
    /// bir dosya eski içeriğin özetiyle yeni içeriğin listesini eşleştiremez.</summary>
    public static bool IsSatisfied(string packagesConfigPath, string solutionDir, string? recordedHash,
        out int presentPackages)
    {
        presentPackages = 0;
        if (recordedHash is null || ReadContent(packagesConfigPath) is not { } content
            || !string.Equals(Hash(content), recordedHash, StringComparison.Ordinal))
            return false;

        XElement? root;
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(content), SafeXml);
            root = XDocument.Load(reader).Root;
        }
        catch (XmlException) { return false; }
        if (root is null || root.Name.LocalName != "packages") return false;

        int present = 0;
        foreach (XElement package in root.Elements().Where(e => e.Name.LocalName == "package"))
        {
            string? id = (string?)package.Attribute("id");
            string? version = (string?)package.Attribute("version");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version)) return false;
            // Klasör ile .nupkg AYNI kimlikten adlanır; .nupkg yoksa paket kurulu sayılmaz.
            string identity = id + "." + version;
            if (!File.Exists(Path.Combine(solutionDir, "packages", identity, identity + ".nupkg"))) return false;
            present++;
        }
        presentPackages = present;
        return true;
    }

    /// <summary>Restore bu kanıtla atlandığında decision.log'a giden satır:
    /// <c>A: restore skipped — packages.config unchanged, 3 packages present</c>.</summary>
    public static string SkippedLine(string project, int presentPackages) =>
        string.Format(CultureInfo.InvariantCulture, "{0}: restore skipped — packages.config unchanged, {1} packages present",
            project, presentPackages);

    private static byte[]? ReadContent(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));
}
