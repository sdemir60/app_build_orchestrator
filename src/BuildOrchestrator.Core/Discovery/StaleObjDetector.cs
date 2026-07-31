using System.Text.Json;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Core.Discovery;

/// <summary>
/// [T72] SPIKE S2 (OSYS.Types.NewSales.Print vakası) — in-place build öncesi default obj
/// (proj\obj) altında yabancı-TFM artığı (ör. eski .NETStandard restore) var mı diye bakar.
/// Yalnız teşhis koyar; hiçbir dosyaya DOKUNMAZ/silmez — build kırılabileceğini warn eder.
/// </summary>
public sealed record StaleObjDiagnosis(string ProjectId, bool IsStale, string? Reason);

public static partial class StaleObjDetector
{
    // .NETStandard,Version=vX.Y (project.assets.json target key biçimi) ya da netstandardX.Y (kısa TFM biçimi)
    [GeneratedRegex(@"\.NETStandard,Version=v[\d.]+|netstandard[\d.]+", RegexOptions.IgnoreCase)]
    private static partial Regex ForeignTfm();

    /// <summary>
    /// obj/project.assets.json içindeki çözülmüş "targets" anahtarlarını beklenen TFM ile karşılaştırır.
    /// Teşhis-only: hiçbir dosyaya dokunmaz/silmez; bozuk/okunamaz JSON dahil hiçbir girdide fırlatmaz —
    /// belirsiz durumlarda sessizce "temiz" (IsStale=false) döner (warn-only detector, correctness dependency değil).
    /// </summary>
    /// <param name="csprojPath">İncelenecek .csproj tam yolu.</param>
    /// <param name="expectedTfm">
    /// TAM target-framework moniker'ı — project.assets.json "targets" anahtarlarının biçimiyle aynı
    /// (ör. <c>.NETFramework,Version=v4.6</c>). KISA TFM alias'ı (ör. <c>net46</c>) DEĞİL: kıyaslama
    /// substring-contains ile yapılır, kısa alias verilirse "beklenen TFM bulundu" eşleşmesi yanlışlıkla
    /// kaçabilir (bkz. yabancı-TFM regex fallback'i buna rağmen çoğu durumda doğru sonuç verir, ama
    /// pozitif-eşleşme yolu garanti değildir). Çağıran taraf tam moniker'ı proje discovery/graph katmanından
    /// (TargetFrameworkMoniker gibi) sağlamalıdır.
    /// </param>
    public static StaleObjDiagnosis Inspect(string csprojPath, string expectedTfm)
    {
        csprojPath = Path.GetFullPath(csprojPath);
        string assets = Path.Combine(Path.GetDirectoryName(csprojPath)!, "obj", "project.assets.json");
        if (!File.Exists(assets)) return new StaleObjDiagnosis(csprojPath, false, null); // obj yok → temiz

        // [T72] Fix: tüm dosya metnini taramak yerine yalnız "targets" anahtarlarını (çözülmüş TFM'ler)
        // oku. "libraries" bölümü referans edilen paketlerin TÜM TFM'lere ait nupkg dosya yollarını
        // (ör. lib/netstandard2.0/Newtonsoft.Json.dll) listeler — bunlar yabancı-TFM sinyali DEĞİLDİR,
        // whole-file substring scan bunları yanlış-pozitif olarak işaretliyordu.
        string[] targetKeys;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(assets));
            if (!doc.RootElement.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object)
                return new StaleObjDiagnosis(csprojPath, false, null); // targets yok → teşhis edilemez, warn yok
            targetKeys = targets.EnumerateObject().Select(p => p.Name).ToArray();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new StaleObjDiagnosis(csprojPath, false, null); // bozuk/okunamaz assets → warn-only detector ASLA patlamaz [T72]
        }

        if (targetKeys.Any(k => k.Contains(expectedTfm, StringComparison.OrdinalIgnoreCase)))
            return new StaleObjDiagnosis(csprojPath, false, null); // beklenen TFM çözülmüş → temiz

        var foreign = targetKeys.Select(k => ForeignTfm().Match(k)).FirstOrDefault(m => m.Success);
        return foreign is { Success: true }
            ? new StaleObjDiagnosis(csprojPath, true, $"obj/project.assets.json contains a resolved foreign TFM: {foreign.Value} (expected {expectedTfm}) — left untouched, the build may break")
            : new StaleObjDiagnosis(csprojPath, false, null);
    }
}
