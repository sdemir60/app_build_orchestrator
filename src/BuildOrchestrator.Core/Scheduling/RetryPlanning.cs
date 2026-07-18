namespace BuildOrchestrator.Core.Scheduling;

using BuildOrchestrator.Contracts.Model;

/// <summary>
/// [Task-13/T55] Continue/RetryFailed segment sınırını aşarken bir <see cref="RunSnapshot"/>'ı dönüştüren SAF
/// yardımcılar — hangi projelerin Completed'tan çıkıp yeniden Queued'a (dispatch edilebilir) döneceğini hesaplar.
/// I/O, process, saat YOK [D3]. Çıktı doğrudan <see cref="ReadySetScheduler(BuildPlan, RunSnapshot)"/> resume
/// ctor'una beslenir — o ctor yalnız <c>Completed</c>'ı okur (bkz. o ctor'un dokümantasyonu), bu yüzden burada
/// asıl iş her iki metodun da Completed'tan ilgili id'leri ÇIKARMASIDIR; Queued alanı yalnız RunSnapshot'ın
/// partisyon invaryantını (her id ya Completed ya Queued'dadır) korumak için ayrıca güncellenir.
/// </summary>
public static class RetryPlanning
{
    /// <summary>
    /// [Continue re-queue / torn-DLL guard] <paramref name="stoppedFailedIds"/> içindeki, snapshot'ta HÂLÂ
    /// <see cref="BuildResult.Failed"/> olan projeleri Completed'tan çıkarıp Queued'a taşır: bir hard Stop
    /// bir projeyi mid-build yarıda bıraktığında (reason="stopped") o projenin DLL'i TORN kalmış olabilir;
    /// Continue'da bu proje yeniden derlenmezse dependent'ları torn DLL'e referans verirdi. Başka bir reason'la
    /// (ör. "exit 1") Failed olan projeler DOKUNULMAZ — yalnız reason="stopped" olanlar bu kümede olur
    /// (<paramref name="stoppedFailedIds"/>'in kendisi, reason bilgisini TAŞIMAYAN <see cref="RunSnapshot"/>'ın
    /// dışında, çağıran — RunCoordinator — tarafından ayrıca izlenir).
    /// </summary>
    public static RunSnapshot RequeueStoppedFailed(RunSnapshot snapshot, IReadOnlySet<string> stoppedFailedIds)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(stoppedFailedIds);
        if (stoppedFailedIds.Count == 0) return snapshot;

        var completed = new Dictionary<string, BuildResult>(snapshot.Completed, StringComparer.OrdinalIgnoreCase);
        var requeued = new List<string>();
        foreach (string id in stoppedFailedIds)
            // Yalnız HÂLÂ Failed olanlar taşınır: id belki daha önce farklı bir yoldan zaten Queued/Succeeded
            // olmuş olabilir (savunmacı) — bu durumda dokunmaya gerek yok.
            if (completed.TryGetValue(id, out var result) && result == BuildResult.Failed)
            {
                completed.Remove(id);
                requeued.Add(id);
            }

        if (requeued.Count == 0) return snapshot;
        return snapshot with { Completed = completed, Queued = [.. snapshot.Queued, .. requeued] };
    }

    /// <summary>
    /// [RetryFailed willBuild kümesi] snapshot'taki <see cref="BuildResult.Failed"/> projeler + <paramref
    /// name="plan"/> üzerindeki TRANSITIVE dependent'ları Completed'tan çıkarıp Queued'a taşır — Succeeded/Skipped
    /// projeler (failed bir kökün altında OLMAYANLar) DOKUNULMAZ, yeniden derlenmez. <paramref name="plan"/>.Nodes
    /// build-order'da (bağımlılıklar önce) olduğu için TEK geçişte hesaplanır: bir düğüm "etkilenmiş" sayılır
    /// eğer (a) kendisi Failed'sa VEYA (b) bağımlılıklarından biri zaten etkilenmiş sayıldıysa — aynı desen
    /// <see cref="BuildOrchestrator.Core.Incremental.IncrementalPlanner"/>'ın Safe/topological-memoization
    /// propagation'ı ile (ayrı bir "reverse dependents" grafı KURULMAZ).
    /// </summary>
    public static RunSnapshot RequeueFailedAndDependents(BuildPlan plan, RunSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);

        var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in plan.Nodes)
        {
            bool isAffected =
                (snapshot.Completed.TryGetValue(node.Id, out var result) && result == BuildResult.Failed)
                || node.Dependencies.Any(affected.Contains);
            if (isAffected) affected.Add(node.Id);
        }

        var completed = new Dictionary<string, BuildResult>(snapshot.Completed, StringComparer.OrdinalIgnoreCase);
        var requeued = new List<string>();
        foreach (string id in affected)
            // Zaten Completed'ta OLMAYAN (ör. önceki segmentten kalma Queued) bir id burada sessizce atlanır —
            // resume ctor'u zaten onu Completed'ta bulamayacağı için "queued" sayar, ekstra işlem gerekmez.
            if (completed.Remove(id))
                requeued.Add(id);

        if (requeued.Count == 0) return snapshot;
        return snapshot with { Completed = completed, Queued = [.. snapshot.Queued, .. requeued] };
    }
}
