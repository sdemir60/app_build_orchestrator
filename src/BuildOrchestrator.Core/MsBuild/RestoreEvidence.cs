using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace BuildOrchestrator.Core.MsBuild;

/// <summary>
/// [PERF Faz E3] Bir packages.config projesinin restore'u atlanabilir mi — kararın TEK sahibi (saf; UI ve process
/// bağımsız test edilir). Kanıt yalnız İÇERİKTİR: <c>packages.config</c>'in SHA-256 özeti son başarıda deftere
/// yazılan özetle (<c>BuildState.PackagesConfigHash</c>) aynı olmalı ve dosyanın listelediği her paketin
/// <c>&lt;solutionDir&gt;\packages\&lt;id&gt;.&lt;version&gt;\</c> klasörü bulunmalıdır. Tarih/mtime karara GİRMEZ.
/// Kanıtın eksik kaldığı her durumda — özet kayıtsız ya da farklı, dosya okunamıyor, XML bozuk, bir klasör eksik,
/// NuGet'in <c>repositoryPath</c>'i paketleri çözümün <c>packages</c> klasörü dışında tutuyor — cevap "tatmin
/// edilmedi"dir ve restore koşar (güvenli taraf). Restore'un atlandığını anlatan decision.log satırının metni de
/// burada durur; Supervisor yalnız çağırır (kopya YASAK).
/// </summary>
public static class RestoreEvidence
{
    // packages.config güvenilmeyen girdidir (ARCHITECTURE §21): DTD yasak, dış kaynak çözülmez.
    private static readonly XmlReaderSettings SafeXml = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };

    /// <summary>packages.config içerik özeti (SHA-256 hex); dosya yoksa null. Okunamayan dosya da null döner —
    /// kanıt üretilemez, restore koşar.</summary>
    public static string? HashOf(string packagesConfigPath) =>
        ReadContent(packagesConfigPath) is { } content ? Hash(content) : null;

    /// <summary>Kayıtlı özet bugünküyle aynı VE packages.config'teki her &lt;package id= version=&gt; için
    /// &lt;solutionDir&gt;\packages\&lt;id&gt;.&lt;version&gt;\ klasörü varsa true. XML bozuksa false (restore koşar —
    /// güvenli taraf).</summary>
    public static bool IsSatisfied(string packagesConfigPath, string solutionDir, string? recordedHash) =>
        IsSatisfied(packagesConfigPath, solutionDir, recordedHash, out _);

    /// <summary>Üç argümanlı biçimle AYNI karar — o buna devreder, karar tek yerde kalır. Tatmin edildiğinde
    /// <paramref name="presentPackages"/> listelenen (klasörü yerinde olan) paket sayısıdır: decision.log satırının
    /// sayısı (<see cref="SkippedLine"/>). Özet ve paket listesi dosyanın TEK okumasından gelir — iki okuma arasında
    /// değişen bir dosya eski içeriğin özetiyle yeni içeriğin listesini eşleştiremez.</summary>
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
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version)
                || !Directory.Exists(Path.Combine(solutionDir, "packages", id + "." + version)))
                return false;
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
