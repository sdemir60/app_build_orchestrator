using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T49 fix round 1 · A2/B4] Kaynak-tarayan guard'ların (ham renk · ham süre · sleep yasağı) ORTAK iskeleti.
/// Üç guard aynı üç şeyi yapıyordu — dosyaları gez, kuralı uygula, ihlalleri topla — ve üçü de aynı iki hatayı
/// taşıyordu: tekli <see cref="Regex.Match(string)"/> yüzünden <b>dosya başına yalnız İLK ihlal</b> raporlanıyor
/// (5 ihlalin 4'ü görünmez; düzeltip yeniden koşan "temizlendi" sanır) ve ihlalin SATIRI yazılmıyordu.
/// Tek yer, tek davranış (kopya YASAK, CLAUDE.md).
///
/// <para>Rapor biçimi <c>göreli/yol.cs:SATIR: eşleşen metin</c> — CLAUDE.md'nin "her bulguda tam konum" kuralı.</para>
/// </summary>
internal static class SourceGuard
{
    /// <summary>App kaynak ağacını tarar (<see cref="RepoPaths.AppSourceFiles"/>).</summary>
    public static IReadOnlyList<string> ScanApp(
        string searchPattern, Regex rule,
        IReadOnlyCollection<string>? allowedFiles = null, bool skipCommentLines = false)
        => Scan(RepoPaths.AppSourceFiles(searchPattern), RepoPaths.AppSrcRoot, rule, allowedFiles, skipCommentLines);

    /// <summary>TÜM üretim projelerini tarar — App + Core + Supervisor + Contracts.</summary>
    public static IReadOnlyList<string> ScanSrc(
        string searchPattern, Regex rule,
        IReadOnlyCollection<string>? allowedFiles = null, bool skipCommentLines = false)
        => Scan(RepoPaths.SrcSourceFiles(searchPattern), RepoPaths.SrcRoot, rule, allowedFiles, skipCommentLines);

    /// <summary>[fix round 2] TÜM test ağacını tarar — D8 "testte gerçek zaman beklenmez" der, yani yasak
    /// testleri de bağlar.</summary>
    public static IReadOnlyList<string> ScanTests(
        string searchPattern, Regex rule,
        IReadOnlyCollection<string>? allowedFiles = null, bool skipCommentLines = false)
        => Scan(RepoPaths.TestSourceFiles(searchPattern), RepoPaths.TestsRoot, rule, allowedFiles, skipCommentLines);

    private static IReadOnlyList<string> Scan(
        IEnumerable<string> files, string root, Regex rule,
        IReadOnlyCollection<string>? allowedFiles, bool skipCommentLines)
    {
        var offenders = new List<string>();
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(root, file);
            if (allowedFiles is not null && allowedFiles.Contains(relative, StringComparer.OrdinalIgnoreCase)) continue;
            offenders.AddRange(ScanText(relative, File.ReadAllText(file), rule, skipCommentLines));
        }
        return offenders;
    }

    /// <summary>
    /// Kuralı TEK bir metne uygular — hem dosya taramasının çekirdeği hem de guard'ların KENDİ kanıt testlerinin
    /// (sahte girdi → ihlaller gerçekten raporlanıyor mu) giriş noktası.
    ///
    /// <para>[fix round 2] Tarama artık dosyayı BÜTÜN olarak değerlendirir. Round 1'in satır-bazlı hâli yeni bir
    /// kör nokta açmıştı: satıra bölünmüş bir ihlal (<c>Color.FromArgb(\n 58, 58, 66)</c>, property-element
    /// <c>&lt;Border.Background&gt;\n#0e0e10</c>) HİÇBİR satırda tam eşleşmediği için üç guard'da birden
    /// görünmez oluyordu. Kurallardaki <c>\s*</c> satır sonlarını da yer aldığından bütün metin üzerinde
    /// doğal olarak çalışırlar; "dosya başına TÜM ihlaller + satır numarası" kazanımı korunur (satır no
    /// eşleşmenin BAŞLADIĞI ofsetten hesaplanır).</para>
    ///
    /// <para>Yorum elemesi de eşleşmenin BAŞLADIĞI satıra bakar (<c>//</c>, <c>///</c>, <c>*</c>, <c>&lt;!--</c>):
    /// bir yasağı ANLATAN doküman satırı, o yasağı İHLAL eden kod değildir.</para>
    /// </summary>
    public static IReadOnlyList<string> ScanText(string relative, string text, Regex rule, bool skipCommentLines = false)
    {
        var offenders = new List<string>();
        foreach (Match match in rule.Matches(text))              // TÜMÜ — ilk eşleşme DEĞİL (A2)
        {
            int line = text.AsSpan(0, match.Index).Count('\n') + 1;
            if (skipCommentLines && IsCommentLine(LineAt(text, match.Index))) continue;
            offenders.Add($"{relative}:{line}: {Flatten(match.Value)}");
        }
        return offenders;
    }

    /// <summary>Eşleşmenin başladığı satırın tam metni (yorum elemesi bunun üzerinden karar verir).</summary>
    private static string LineAt(string text, int index)
    {
        int start = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        int end = text.IndexOf('\n', index);
        return end < 0 ? text[start..] : text[start..end];
    }

    /// <summary>Çok satırlı bir eşleşme rapora tek satır olarak girer — aksi halde ihlal listesi okunmaz olur.</summary>
    private static string Flatten(string value)
        => string.Join(' ', value.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(part => part.Trim())
                                 .Where(part => part.Length > 0));

    private static bool IsCommentLine(string line)
    {
        string trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal)
            || trimmed.StartsWith("<!--", StringComparison.Ordinal);
    }

    /// <summary>Taramanın GERÇEKTEN dosya gördüğünü doğrulayan meta-testlerin ortak yardımcısı: boş bir tarama
    /// her guard'ı sessizce yeşil bırakırdı.</summary>
    public static IReadOnlyList<string> ScannedAppFiles(string searchPattern) =>
        RepoPaths.AppSourceFiles(searchPattern)
                 .Select(f => Path.GetRelativePath(RepoPaths.AppSrcRoot, f)).ToList();

    public static IReadOnlyList<string> ScannedSrcFiles(string searchPattern) =>
        RepoPaths.SrcSourceFiles(searchPattern)
                 .Select(f => Path.GetRelativePath(RepoPaths.SrcRoot, f)).ToList();

    public static IReadOnlyList<string> ScannedTestFiles(string searchPattern) =>
        RepoPaths.TestSourceFiles(searchPattern)
                 .Select(f => Path.GetRelativePath(RepoPaths.TestsRoot, f)).ToList();
}
