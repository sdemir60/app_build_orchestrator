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
        {
            if (!inWorkspace(root)) return ConditionalRebuildVerdict.Build;

            // Bu koşuda DERLENDİ: sonucu koşudan. Skipped "derlenmedi" demektir — sonucu defterden okunur.
            if (completedThisRun.TryGetValue(root, out var result) && result != BuildResult.Skipped)
            {
                if (result == BuildResult.Succeeded) return ConditionalRebuildVerdict.Build;
                continue;
            }

            if (ledgerAtRunStart is null || !ledgerAtRunStart.TryGetValue(root, out var recorded)
                || recorded.LastResult == BuildResult.Succeeded)
                return ConditionalRebuildVerdict.Build;
        }
        return ConditionalRebuildVerdict.DependencyStillFailing;
    }

    /// <summary>
    /// Kayıtlı köklerin GÖRÜNEN adları (etiket/tooltip ve decision.log satırı için) — yalnız proje
    /// <see cref="WillBuildReason.WaitingForDependency"/> iken dolu. Ad sıralı, tekil; planda olmayan kök için
    /// dosya adı kullanılır.
    /// </summary>
    public static IReadOnlyList<string>? RootNames(BuildState? state, Func<string, string?> nameOf)
    {
        ArgumentNullException.ThrowIfNull(nameOf);
        if (state?.DepIssueRoots is not { Count: > 0 } roots) return null;
        return [.. roots
            .Select(id => nameOf(id) ?? Path.GetFileNameWithoutExtension(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)];
    }
}
