namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

/// <summary>Koşullu bir projenin sırası geldiğinde verilen karar.</summary>
public enum ConditionalRebuildVerdict
{
    /// <summary>Normal şekilde derlenir.</summary>
    Build,
    /// <summary>Kayıtlı köklerin HEPSİ hâlâ hatalı — atlanır (<see cref="SkipReasons.DependencyStillFailing"/>).</summary>
    DependencyStillFailing,
}

/// <summary>
/// Koşullu yeniden derlemenin saf kararları. Planlama <see cref="WillBuildEvaluator"/>'da
/// <see cref="WillBuildReason.WaitingForDependency"/> der; bu sınıf (1) bir koşunun o projeyi GERÇEKTEN koşullu
/// değerlendirip değerlendirmediğini ve (2) sırası geldiğinde derlenip derlenmeyeceğini söyler. Koordinatör
/// yalnız uygular: I/O, process, scheduler mutasyonu YOK.
///
/// <para><b>Neden koşullu.</b> Başarısız bir bağımlılığa rağmen başarıyla derlenen proje, o bağımlılığın SON
/// BAŞARILI çıktısına link'lidir. Bağımlılık hâlâ patlıyorsa projeyi yeniden derlemek hiçbir şey kazandırmaz:
/// aynı bayat çıktıya yeniden link'lenir. Ama bağımlılık bir gün KAYNAK DEĞİŞMEDEN düzelirse projenin imzası da
/// değişmez — DLL/bin timestamp'i okunmadığı için onu yeniden derlemeye götürecek tek sinyal defterdeki nottur.
/// Tetik bu yüzden "her Build'de" değil "kök düzeldiğinde"dir.</para>
///
/// <para><b>Kök sonucu nereden okunur.</b> Kararın anı projenin hazır olduğu andır: tüm bağımlılıkları (ve
/// dolayısıyla onların üstündeki kökler) bu koşuda terminaldir. Bu koşuda DERLENEN kökün sonucu koşudan okunur;
/// derlenmeyen (up to date / kapsam dışı / döngüde atlanan ya da koşuda hiç görünmeyen) kökün sonucu koşu
/// başındaki defterden okunur. Belirsizlikte yön DERLEMEdir: kök bilinmiyorsa, projede artık yoksa ya da
/// defterde kaydı yoksa proje derlenir.</para>
/// </summary>
public static class ConditionalRebuild
{
    /// <summary>
    /// Bu koşu <paramref name="node"/>'u koşullu mu değerlendirir. Yalnız Build ve Cycles koşuları; satırdan
    /// tetiklenen tek proje koşusunun hedefi koşulsuz derlenir (kullanıcının açık komutu), Rebuild her şeyi
    /// derler. Bir SCC grubunun üyesi de koşulsuz derlenir: grup tek iş kalemidir ve bir üyeyi atlayıp
    /// diğerlerini derlemek grubu yarım bırakırdı — güvenli yön.
    /// </summary>
    public static bool AppliesTo(ProjectNode node, RunMode mode, bool scopedRun, bool cycleGroupMember) =>
        mode is RunMode.Build or RunMode.Cycles
        && !scopedRun
        && !cycleGroupMember
        && node.WillBuild == true
        && node.WillBuildReason == WillBuildReason.WaitingForDependency;

    /// <summary>
    /// Koşullu projenin sırası geldiğinde kararı: köklerden EN AZ BİRİ başarılıysa (bu koşuda başarıyla
    /// derlendi, ya da bu koşuda derlenmedi ama defterdeki son sonucu başarı) derlenir; hepsi hâlâ hatalıysa
    /// (bu koşuda patladı, ya da derlenmedi ve defterdeki son sonucu hata) atlanır.
    /// </summary>
    /// <param name="rootIds">Defterdeki kök proje kimlikleri (<see cref="BuildState.DepIssueRoots"/>). Boş/null ⇒
    /// kök bilinmiyor ⇒ derlenir.</param>
    /// <param name="completedThisRun">Bu koşunun terminal sonuçları (pre-skip tohumları dahil).</param>
    /// <param name="inWorkspace">Kimlik bu koşunun planında var mı. Yoksa ⇒ derlenir.</param>
    /// <param name="ledgerAtRunStart">Koşu başında okunan defter. Kök kaydı yoksa ⇒ derlenir.</param>
    public static ConditionalRebuildVerdict Decide(IReadOnlyList<string>? rootIds,
        IReadOnlyDictionary<string, BuildResult> completedThisRun, Func<string, bool> inWorkspace,
        IReadOnlyDictionary<string, BuildState>? ledgerAtRunStart)
    {
        ArgumentNullException.ThrowIfNull(completedThisRun);
        ArgumentNullException.ThrowIfNull(inWorkspace);
        if (rootIds is not { Count: > 0 }) return ConditionalRebuildVerdict.Build;

        foreach (string root in rootIds)
            if (ClassifyRoot(root, completedThisRun, inWorkspace, ledgerAtRunStart) == RootEvidence.Cleared)
                return ConditionalRebuildVerdict.Build;
        return ConditionalRebuildVerdict.DependencyStillFailing;
    }

    /// <summary>Bir kökün tekil kanıtı — <see cref="Decide"/> ve <see cref="DescribeStillFailingRoots"/>'un
    /// PAYLAŞTIĞI TEK sınıflandırma (kopya YASAK): kararı verdiren mantık ile o kararı METNE döken mantık aynı
    /// kaynaktan okur, aksi halde ikisi sessizce ayrışabilirdi.</summary>
    private enum RootEvidence
    {
        /// <summary>Kök artık temiz (başarılı ya da projeden düştü) — proje derlenmeli.</summary>
        Cleared,
        /// <summary>Kök BU KOŞUDA patladı — kanıt taze.</summary>
        FailedThisRun,
        /// <summary>Kök bu koşuda hiç denenmedi (skip/yok); "hâlâ hatalı" iddiası yalnız koşu BAŞINDAKİ
        /// defterin son bilinen sonucundan geliyor.</summary>
        FailedInLedgerOnly,
    }

    private static RootEvidence ClassifyRoot(string root, IReadOnlyDictionary<string, BuildResult> completedThisRun,
        Func<string, bool> inWorkspace, IReadOnlyDictionary<string, BuildState>? ledgerAtRunStart)
    {
        if (!inWorkspace(root)) return RootEvidence.Cleared;

        // Bu koşuda DERLENDİ: sonucu koşudan. Skipped "derlenmedi" demektir — sonucu defterden okunur.
        if (completedThisRun.TryGetValue(root, out var result) && result != BuildResult.Skipped)
            return result == BuildResult.Succeeded ? RootEvidence.Cleared : RootEvidence.FailedThisRun;

        if (ledgerAtRunStart is null || !ledgerAtRunStart.TryGetValue(root, out var recorded)
            || recorded.LastResult == BuildResult.Succeeded)
            return RootEvidence.Cleared;
        return RootEvidence.FailedInLedgerOnly;
    }

    /// <summary>
    /// [Task 4 — carried item 3] <see cref="Decide"/> <c>DependencyStillFailing</c> derdiğinde, ATLAMA satırının
    /// ("dependency still failing (…)") kök listesini DOĞRU söyler: bir kök BU KOŞUDA gerçekten patladıysa çıplak
    /// adı yazılır (bugünkü davranış — "R failed in this run" iddiasıyla TUTARLI); kök bu koşuda hiç denenmediyse
    /// (ör. bir SCC üyesi Build modunda "in dependency cycle" ile pre-skip edilir) ve "hâlâ hatalı" iddiası
    /// yalnız koşu başındaki DEFTERDEN geliyorsa <c>" (last known failure)"</c> eki eklenir — aksi hâlde satır,
    /// hiç gözlemlenmemiş bir "şimdi de patladı" iddiası taşırdı. Ad sıralı, tekil (<see cref="RootNames"/> ile
    /// AYNI biçim); girdi <see cref="Decide"/>'ın Build dönmediği (yalnız <c>Cleared</c> OLMAYAN kökler) hâli
    /// varsayılır — bir <c>Cleared</c> kök burada görülürse (çağıran hatası) sessizce atlanır.
    /// </summary>
    public static IReadOnlyList<string> DescribeStillFailingRoots(
        IReadOnlyList<string>? rootIds, IReadOnlyDictionary<string, BuildResult> completedThisRun,
        Func<string, bool> inWorkspace, IReadOnlyDictionary<string, BuildState>? ledgerAtRunStart,
        Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(completedThisRun);
        ArgumentNullException.ThrowIfNull(inWorkspace);
        ArgumentNullException.ThrowIfNull(nameOf);
        if (rootIds is not { Count: > 0 }) return [];

        var entries = new List<(string Name, bool LedgerOnly)>();
        foreach (string root in rootIds)
        {
            var evidence = ClassifyRoot(root, completedThisRun, inWorkspace, ledgerAtRunStart);
            if (evidence == RootEvidence.Cleared) continue; // savunmacı: Decide zaten Build dönerdi
            entries.Add((nameOf(root), evidence == RootEvidence.FailedInLedgerOnly));
        }
        return [.. entries
            .DistinctBy(e => e.Name, StringComparer.Ordinal)
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .Select(e => e.LedgerOnly ? $"{e.Name} (last known failure)" : e.Name)];
    }

    /// <summary>
    /// Kayıtlı köklerin GÖRÜNEN adları (etiket/tooltip ve decision.log satırı için) — yalnız proje
    /// <see cref="WillBuildReason.WaitingForDependency"/> iken dolu. Ad sıralı, tekil; planda olmayan kök için
    /// dosya adı kullanılır.
    /// </summary>
    public static IReadOnlyList<string>? RootNames(WillBuildReason? reason, BuildState? state, Func<string, string?> nameOf)
    {
        ArgumentNullException.ThrowIfNull(nameOf);
        if (reason != WillBuildReason.WaitingForDependency || state?.DepIssueRoots is not { Count: > 0 } roots) return null;
        return [.. roots
            .Select(id => nameOf(id) ?? Path.GetFileNameWithoutExtension(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)];
    }
}
