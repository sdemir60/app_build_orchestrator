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
    /// (sahte girdi → ihlaller gerçekten raporlanıyor mu) giriş noktası. Satır satır çalışır: satır numarası
    /// böyle üretilir ve yorum satırları (<c>//</c>, <c>///</c>, <c>*</c>, <c>&lt;!--</c>) istenirse elenir —
    /// bir yasağı ANLATAN doküman satırı, o yasağı İHLAL eden kod değildir.
    /// </summary>
    public static IReadOnlyList<string> ScanText(string relative, string text, Regex rule, bool skipCommentLines = false)
    {
        var offenders = new List<string>();
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (skipCommentLines && IsCommentLine(line)) continue;
            foreach (Match match in rule.Matches(line))          // TÜMÜ — ilk eşleşme DEĞİL (A2)
                offenders.Add($"{relative}:{i + 1}: {match.Value.Trim()}");
        }
        return offenders;
    }

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
}
