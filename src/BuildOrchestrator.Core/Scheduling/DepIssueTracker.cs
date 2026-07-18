namespace BuildOrchestrator.Core.Scheduling;

using BuildOrchestrator.Contracts.Model;

/// <summary>
/// [T54] Bir dispatch edilmiş projenin depIssues kümesi: <b>DOĞRUDAN</b> (kendi <c>Dependencies</c>'i içinde
/// FAILED olanların kök adı) + <b>DOLAYLI</b> (bu bağımlılıkların KENDİ önceden hesaplanmış depIssues'undan miras
/// alınan, ama bu projenin doğrudan bağımlılığı OLMAYAN kökler). <see cref="All"/> ikisinin birleşimidir (dedup +
/// alfabetik sıralı — determinizm, D8). <see cref="Direct"/>/<see cref="Indirect"/> ayrımı yalnız Supervisor'ın
/// log-başı uyarı satırlarını seçmesi içindir (bkz. RunCoordinator.DepIssueWarnLines); <c>ProjectSucceeded/
/// FailedEvent.DepIssues</c>'a yalnız <see cref="All"/> yazılır.
/// </summary>
public sealed record DepIssueResult(IReadOnlyList<string> All, IReadOnlyList<string> Direct, IReadOnlyList<string> Indirect)
{
    public static readonly DepIssueResult Empty = new([], [], []);
}

/// <summary>
/// [T54] Saf hesaplama — I/O, process, scheduler-state mutasyonu YOK [D3]. <see cref="ReadySetScheduler"/>'ın
/// resolved semantiği (<c>IsResolvedLocked</c>) DEĞİŞMEZ: succeeded/failed/skipped bir bağımlılık dependent'i
/// bloklamaz (A3) — bu class yalnız SONUCU (hangi kök hataların zincir boyunca taşındığını) hesaplar.
///
/// Çağıran (Supervisor.RunCoordinator), bir projeyi dispatch ederken bu metodu çağırır: resolved-gate sayesinde
/// (<c>ReadySetScheduler.IsReadyLocked</c>) o projenin TÜM bağımlılıkları o anda zaten terminaldir (Completed'ta) —
/// bu yüzden hem <paramref name="completed"/> hem <paramref name="depIssuesById"/> sorguları tutarlıdır (dependency
/// hâlâ koşuyor olamaz). <paramref name="depIssuesById"/>, ÇAĞIRANIN her proje tamamlandığında (bu metodun
/// döndürdüğü <see cref="DepIssueResult.All"/> ile) doldurduğu bir birikimdir — burada yalnız OKUNUR.
/// </summary>
public static class DepIssueTracker
{
    /// <param name="dependencyIds">Hesaplanan projenin <see cref="ProjectNode.Dependencies"/>'i (üretici projectId'ler).</param>
    /// <param name="completed">Scheduler'ın tamamlanmış sonuçları (<c>ReadySetScheduler.Completed</c>) — projectId → BuildResult.</param>
    /// <param name="depIssuesById">Şimdiye kadar tamamlanmış projelerin ÖNCEDEN hesaplanmış depIssues'u (projectId →
    /// kök adlar). Bir bağımlılık bu sözlükte yoksa (ör. henüz hiç depIssue taşımadı, ya da cycle nedeniyle
    /// construction'da pre-skip edildiği için hiç dispatch edilmedi) miras edilecek bir şey yok sayılır.</param>
    /// <param name="nameOf">projectId → görünen ad (warn satırları ve DepIssues'a YAZILAN, ham id DEĞİL).</param>
    public static DepIssueResult Compute(
        IEnumerable<string> dependencyIds,
        IReadOnlyDictionary<string, BuildResult> completed,
        IReadOnlyDictionary<string, IReadOnlyList<string>> depIssuesById,
        Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(dependencyIds);
        ArgumentNullException.ThrowIfNull(completed);
        ArgumentNullException.ThrowIfNull(depIssuesById);
        ArgumentNullException.ThrowIfNull(nameOf);

        SortedSet<string>? direct = null;
        SortedSet<string>? inherited = null;

        foreach (string depId in dependencyIds)
        {
            // Yalnız FAILED kökler taşınır — Skipped/Succeeded bir bağımlılık depIssue ÜRETMEZ (v7 A6).
            if (completed.TryGetValue(depId, out var result) && result == BuildResult.Failed)
                (direct ??= new(StringComparer.Ordinal)).Add(nameOf(depId));

            if (depIssuesById.TryGetValue(depId, out var inheritedFromDep))
                foreach (string root in inheritedFromDep)
                    (inherited ??= new(StringComparer.Ordinal)).Add(root);
        }

        if (direct is null && inherited is null) return DepIssueResult.Empty;

        // Indirect = inherited EKSİ direct: bir kök hem doğrudan hem zincirden geliyorsa (diamond + doğrudan
        // bağımlılık aynı anda) yalnız Direct'te sayılır — warn satırı iki kez yazılmaz.
        var indirectOnly = inherited is null ? new SortedSet<string>(StringComparer.Ordinal)
            : direct is null ? inherited
            : new SortedSet<string>(inherited.Except(direct), StringComparer.Ordinal);

        var all = new SortedSet<string>(StringComparer.Ordinal);
        if (direct is not null) all.UnionWith(direct);
        if (inherited is not null) all.UnionWith(inherited);

        return new DepIssueResult(
            All: [.. all],
            Direct: direct is null ? [] : [.. direct],
            Indirect: indirectOnly.Count == 0 ? [] : [.. indirectOnly]);
    }
}
