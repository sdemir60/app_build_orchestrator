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
/// <param name="Stale">[tek proje] Bu koşuda DERLENMEYEN bayat bağımlılıklar (görünen adla) — hedef onların son
/// bilinen çıktısına karşı derlendi. <see cref="All"/>'a girerler (event, ▲ sayacı, defter notu) ama
/// <see cref="Direct"/>/<see cref="Indirect"/>'e DEĞİL: onlar bu koşuda PATLAYAN kökleri anlatır ve uyarı
/// satırları başka bir cümleyle yazılır.</param>
/// <param name="RootIds"><see cref="All"/>'ın KİMLİK karşılığı (doğrudan + miras + bayat; tekil, sıralı): dependent'ların
/// miras birikimi ve defter notunun kökleri (<c>BuildState.DepIssueRoots</c>) bundan yazılır.</param>
public sealed record DepIssueResult(IReadOnlyList<string> All, IReadOnlyList<string> Direct, IReadOnlyList<string> Indirect,
    IReadOnlyList<StaleRoot> Stale, IReadOnlyList<string> RootIds)
{
    public static readonly DepIssueResult Empty = new([], [], [], [], []);
}

/// <summary>
/// [tek proje · design v1.11.0 §3.8] Bir koşuda derlenmeyecek BAYAT bağımlılık — <c>ProjectRunScope</c>
/// üretir, <see cref="DepIssueTracker.Compute"/> tüketir. <paramref name="InCycle"/> uyarı cümlesini seçer:
/// döngü üyesi "turlar koşmadı", diğeri "bekleyen değişiklikleri var" diye anlatılır.
/// </summary>
/// <param name="Id">Bağımlılığın proje kimliği (tam csproj yolu).</param>
/// <param name="Name">Görünen adı — kapsamlı koşuda düğüm haritası yalnız hedefi taşır, bu yüzden ad
/// kapsamı üreten tarafın (tam planı gören) elinden gelir; koordinatörün ad çözümü onu bulamazdı.</param>
public sealed record StaleDependency(string Id, string Name, bool InCycle);

/// <summary><see cref="StaleDependency"/>'nin sonuç tarafı: görünen AD (ham id değil) + aynı döngü bayrağı.</summary>
public sealed record StaleRoot(string Name, bool InCycle);

/// <summary>
/// [T54] Saf hesaplama — I/O, process, scheduler-state mutasyonu YOK [D3]. <see cref="ReadySetScheduler"/>'ın
/// resolved semantiği (<c>IsResolvedLocked</c>) DEĞİŞMEZ: succeeded/failed/skipped bir bağımlılık dependent'i
/// bloklamaz (A3) — bu class yalnız SONUCU (hangi kök hataların zincir boyunca taşındığını) hesaplar.
///
/// Çağıran (Supervisor.RunCoordinator), bir projeyi dispatch ederken bu metodu çağırır: resolved-gate sayesinde
/// (<c>ReadySetScheduler.IsReadyLocked</c>) o projenin TÜM bağımlılıkları o anda zaten terminaldir (Completed'ta) —
/// bu yüzden hem <paramref name="completed"/> hem <paramref name="depIssuesById"/> sorguları tutarlıdır (dependency
/// hâlâ koşuyor olamaz). <paramref name="depIssuesById"/>, ÇAĞIRANIN her proje tamamlandığında (bu metodun
/// döndürdüğü <see cref="DepIssueResult.RootIds"/> ile) doldurduğu bir birikimdir — burada yalnız OKUNUR.
/// </summary>
public static class DepIssueTracker
{
    /// <param name="dependencyIds">Hesaplanan projenin <see cref="ProjectNode.Dependencies"/>'i (üretici projectId'ler).</param>
    /// <param name="completed">Scheduler'ın tamamlanmış sonuçları (<c>ReadySetScheduler.Completed</c>) — projectId → BuildResult.</param>
    /// <param name="depIssuesById">Şimdiye kadar tamamlanmış projelerin ÖNCEDEN hesaplanmış kökleri (projectId →
    /// kök proje KİMLİKLERİ, yani <see cref="DepIssueResult.RootIds"/>). Bir bağımlılık bu sözlükte yoksa (ör. henüz hiç depIssue taşımadı, ya da cycle nedeniyle
    /// construction'da pre-skip edildiği için hiç dispatch edilmedi) miras edilecek bir şey yok sayılır.</param>
    /// <param name="nameOf">projectId → görünen ad (warn satırları ve DepIssues'a YAZILAN, ham id DEĞİL).</param>
    /// <param name="stale">[tek proje] Bu koşuda derlenmeyen bayat bağımlılıklar (<c>ProjectRunScope</c>'tan);
    /// null/boş ⇒ sonuç şekli bugünküyle birebir aynı. Failed bir kök aynı anda bayat listedeyse
    /// <see cref="DepIssueResult.All"/>'da bir kez sayılır.</param>
    public static DepIssueResult Compute(
        IEnumerable<string> dependencyIds,
        IReadOnlyDictionary<string, BuildResult> completed,
        IReadOnlyDictionary<string, IReadOnlyList<string>> depIssuesById,
        Func<string, string> nameOf,
        IReadOnlyList<StaleDependency>? stale = null)
    {
        ArgumentNullException.ThrowIfNull(dependencyIds);
        ArgumentNullException.ThrowIfNull(completed);
        ArgumentNullException.ThrowIfNull(depIssuesById);
        ArgumentNullException.ThrowIfNull(nameOf);

        // Hesap KİMLİKLERLE yapılır, adlar en sonda türetilir: birikim kök kimliklerini taşır (defter notu ve
        // koşullu yeniden derleme kökü kimlikle arar; ad tekil değildir).
        var directIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var inheritedIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string depId in dependencyIds)
        {
            // Yalnız FAILED kökler taşınır — Skipped/Succeeded bir bağımlılık depIssue ÜRETMEZ (v7 A6).
            if (completed.TryGetValue(depId, out var result) && result == BuildResult.Failed)
                directIds.Add(depId);

            if (depIssuesById.TryGetValue(depId, out var inheritedFromDep))
                inheritedIds.UnionWith(inheritedFromDep);
        }

        // Bayat kökler ad sıralı (D8) — aynı ad iki kez listelenmişse bir kez.
        var staleRoots = stale is { Count: > 0 }
            ? stale.Select(s => new StaleRoot(s.Name, s.InCycle))
                .DistinctBy(s => s.Name, StringComparer.Ordinal)
                .OrderBy(s => s.Name, StringComparer.Ordinal)
                .ToList()
            : null;

        if (directIds.Count == 0 && inheritedIds.Count == 0 && staleRoots is null) return DepIssueResult.Empty;

        var direct = new SortedSet<string>(directIds.Select(nameOf), StringComparer.Ordinal);
        // Indirect = inherited EKSİ direct: bir kök hem doğrudan hem zincirden geliyorsa (diamond + doğrudan
        // bağımlılık aynı anda) yalnız Direct'te sayılır — warn satırı iki kez yazılmaz.
        var indirectOnly = new SortedSet<string>(
            inheritedIds.Where(id => !directIds.Contains(id)).Select(nameOf), StringComparer.Ordinal);
        indirectOnly.ExceptWith(direct);

        var all = new SortedSet<string>(direct, StringComparer.Ordinal);
        all.UnionWith(indirectOnly);
        if (staleRoots is not null) all.UnionWith(staleRoots.Select(s => s.Name));

        var rootIds = new SortedSet<string>(directIds, StringComparer.OrdinalIgnoreCase);
        rootIds.UnionWith(inheritedIds);
        if (stale is { Count: > 0 }) rootIds.UnionWith(stale.Select(s => s.Id));

        return new DepIssueResult(
            All: [.. all],
            Direct: [.. direct],
            Indirect: [.. indirectOnly],
            Stale: staleRoots ?? [],
            RootIds: [.. rootIds]);
    }
}
