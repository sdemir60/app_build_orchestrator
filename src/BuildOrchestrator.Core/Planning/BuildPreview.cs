namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Incremental;

/// <summary>
/// [T53][A6][v7Δ-8] Bir BuildPlan'daki her ProjectNode için WillBuildEvaluator kararını uygular ve
/// WillBuild alanı dolu YENİ bir BuildPlan döner (kaynak plan değiştirilmez).
/// [Faz 3 — spec 2026-09-18 §5] <c>outputOf</c> projenin çıktı kanıtı kontrolünü değerlendiriciye YALNIZ aktarır;
/// verilmezse (ya da proje için <c>null</c> ise) karar bugünküdür.
///
/// <para><b>İkinci geçiş: zaman kipindeki bağımlılık notu</b> (<see cref="WaitForTroubledRoots"/>). Değerlendirici
/// tek bir projeye bakar ve defterdeki notu zaman kipinde okuyamaz; notun hâlâ geçerli olup olmadığı yalnız
/// PLANIN tamamı görünürken söylenebilir. Bu yüzden karar burada, herkesin hükmü belli olduktan SONRA
/// tamamlanır.</para>
/// </summary>
public static class BuildPreview
{
    public static BuildPlan ComputeWillBuild(BuildPlan plan,
        Func<ProjectNode, string?> currentSignature, Func<string, BuildState?> stateLookup, bool buildCycles,
        Func<string, OutputCheck?>? outputOf = null)
    {
        var nodes = plan.Nodes.Select(n =>
        {
            var (willBuild, reason) =
                WillBuildEvaluator.EvaluateWithReason(
                    n.InCycle, currentSignature(n), stateLookup(n.Id), buildCycles, outputOf?.Invoke(n.Id));
            return n with { WillBuild = willBuild, WillBuildReason = reason };
        }).ToList();
        return plan with { Nodes = WaitForTroubledRoots(nodes, stateLookup, buildCycles) };
    }

    /// <summary>
    /// [kullanıcı kararı 2026-09-20] Dışarıda derlenmiş (<see cref="WillBuildReason.BuiltOutside"/>) bir
    /// projenin kaydında kökleri BİLİNEN bir bağımlılık notu (<see cref="BuildState.DepIssue"/> +
    /// <see cref="BuildState.DepIssueRoots"/>) varsa ve o köklerden EN AZ BİRİ bu planda hâlâ dertliyse hüküm
    /// <see cref="WillBuildReason.WaitingForDependency"/>'ye yükseltilir — defter kipindeki bağımlıyla AYNI
    /// cevap: satır yeşil kalır (<c>StandingStatus.Current</c>), uyarı üçgeni kökleri söyler ve koşu projeyi
    /// <see cref="ConditionalRebuild"/> ile değerlendirir.
    ///
    /// <para><b>Notun TEK BAŞINA hükmü yoktur.</b> Taze bir zaman hükmü "hiçbir HintPath hedefim benden yeni
    /// değil" demekten fazlasını KANITLAMAZ; kök dışarıda düzeltilip yeniden derlendiyse (ve aynı derlemede
    /// bağımlı da tazelendiyse) not bayattır. Kökün bugünkü hâline bakılmasaydı böyle bir bağımlı Sync'te
    /// sonsuza dek yanlış bir üçgen taşır, koşuda ise <c>dependency still failing</c> ile atlanırdı: aracın
    /// kendisi kökü derleyene kadar kilitli bir durum. Bu yüzden kökün DERTLİ olması yüklemin şartıdır.</para>
    ///
    /// <para><b>Dertli</b>, koşu tarafının kök sınıflandırmasıyla AYNI yüklemdir (kopya YASAK):
    /// <see cref="WillBuildEvaluator.OutputIsCurrent"/> "hayır" diyorsa kök dertlidir. Planda hiç olmayan kök
    /// dertli DEĞİLDİR — <see cref="ConditionalRebuild"/> de çalışma alanından düşmüş kökü temiz sayar.</para>
    ///
    /// <para>Kökleri BİLİNMEYEN not burada da okunmaz: söyleyebileceği tek şey koşulsuz "derle" olurdu ve
    /// dışarıda tazelenmiş bir çıktıyı her koşuda yeniden derletirdi.</para>
    /// </summary>
    private static IReadOnlyList<ProjectNode> WaitForTroubledRoots(
        List<ProjectNode> decided, Func<string, BuildState?> stateLookup, bool buildCycles)
    {
        // Harita bu geçişten ÖNCEKİ hükümlerin DONMUŞ anlık görüntüsüdür (kayıtlar değişmez; aşağıdaki
        // yükseltme yalnız listenin gözünü değiştirir). Kasıtlı: bir düğümün yükseltilmesi bir sonrakinin
        // cevabını değiştirseydi sonuç ziyaret SIRASINA bağlı olurdu — determinizm [D8].
        var byId = decided.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        bool Troubled(string rootId) =>
            byId.TryGetValue(rootId, out var root) && !WillBuildEvaluator.OutputIsCurrent(root.WillBuildReason);

        for (int i = 0; i < decided.Count; i++)
        {
            if (decided[i].WillBuildReason != WillBuildReason.BuiltOutside) continue;
            if (stateLookup(decided[i].Id) is not { DepIssue: true, DepIssueRoots: { Count: > 0 } roots }) continue;
            if (!roots.Any(Troubled)) continue;
            // Kapsam dışı döngü üyesi yine derlenmez — değerlendiricinin kısa devresi, tek yerden (kopya YASAK).
            decided[i] = decided[i] with
            {
                WillBuild = !WillBuildEvaluator.OutOfScope(decided[i].InCycle, buildCycles),
                WillBuildReason = WillBuildReason.WaitingForDependency,
            };
        }
        return decided;
    }
}
