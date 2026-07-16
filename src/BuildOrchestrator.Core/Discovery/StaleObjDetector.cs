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

    public static StaleObjDiagnosis Inspect(string csprojPath, string expectedTfm)
    {
        csprojPath = Path.GetFullPath(csprojPath);
        string assets = Path.Combine(Path.GetDirectoryName(csprojPath)!, "obj", "project.assets.json");
        if (!File.Exists(assets)) return new StaleObjDiagnosis(csprojPath, false, null); // obj yok → temiz

        string text = File.ReadAllText(assets);
        if (text.Contains(expectedTfm, StringComparison.OrdinalIgnoreCase))
            return new StaleObjDiagnosis(csprojPath, false, null); // beklenen TFM zaten mevcut → temiz

        var m = ForeignTfm().Match(text);
        return m.Success
            ? new StaleObjDiagnosis(csprojPath, true, $"obj/project.assets.json yabancı TFM içeriyor: {m.Value} (beklenen {expectedTfm}) — dokunulmadı, build kırılabilir")
            : new StaleObjDiagnosis(csprojPath, false, null);
    }
}
