namespace BuildOrchestrator.Core.Graph;

using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Contracts.Model;

/// <summary>
/// [T71] Sınıflandırma raporu: her (proje, HintPath) çifti için sınıf + sayaçlar + repo-resolve oranı.
/// RepoResolveRatio = Edge / (Edge + Unclassified) — external sınıflar (ThirdParty/OsysPlatform) meşru
/// dış girdi kabul edilip paydadan HARİÇ tutulur [SPIKE S3 metriği].
/// </summary>
public sealed record ClassificationReport(
    IReadOnlyList<HintPathRef> Classified, int EdgeCount, int ThirdPartyCount,
    int OsysPlatformCount, int UnclassifiedCount, double RepoResolveRatio, IReadOnlyList<string> Warnings);

/// <summary>
/// [T71] SPIKE S3 fallback sınıflandırıcısı: HintPath'leri producer map'e göre 4 sınıfa ayırır
/// (Edge / ExternalThirdParty / ExternalOsysPlatform / Unclassified) ve repo-resolve metriğini hesaplar.
/// </summary>
public static class HintPathClassifier
{
    public static ClassificationReport Classify(IReadOnlyList<EvaluatedProject> projects, ProducerMap producers)
    {
        var classified = new List<HintPathRef>();
        var warnings = new List<string>();
        int edge = 0, third = 0, plat = 0, unc = 0;

        // Determinizm [D8]: proje sırası OrdinalIgnoreCase Path'e göre.
        foreach (var p in projects.OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase))
            foreach (var h in p.HintPaths)
            {
                HintPathClass cls;
                string? prod = null;
                if (producers.DllToProducer.TryGetValue(h.BaseName, out prod)) { cls = HintPathClass.Edge; edge++; }
                else if (IsThirdParty(h.Raw)) { cls = HintPathClass.ExternalThirdParty; third++; }
                else if (IsUnderBin(h.Raw)) { cls = HintPathClass.ExternalOsysPlatform; plat++; }
                else
                {
                    cls = HintPathClass.Unclassified;
                    unc++;
                    warnings.Add($"{p.Path}: sınıflandırılamayan HintPath → {h.Raw}");
                }
                classified.Add(new HintPathRef(h.Raw, h.BaseName, cls, prod));
            }

        double ratio = (edge + unc) == 0 ? 1.0 : (double)edge / (edge + unc);
        return new ClassificationReport(classified, edge, third, plat, unc, ratio, warnings);
    }

    // Ham yol '\packages\' içeriyorsa (NuGet legacy) veya "Program Files" altındaysa third-party kabul edilir.
    // '/'→'\' normalize edilir; gerçek HintPath'ler ayraç/case karışımı içerebilir.
    private static bool IsThirdParty(string raw)
    {
        string n = raw.Replace('/', '\\');
        return n.Contains("\\packages\\", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Program Files", StringComparison.OrdinalIgnoreCase);
    }

    // '\bin\' segmenti + producer YOK → repo-dışı OSYS platform DLL'i (ExternalOsysPlatform).
    private static bool IsUnderBin(string raw) =>
        raw.Replace('/', '\\').Contains("\\bin\\", StringComparison.OrdinalIgnoreCase);
}
