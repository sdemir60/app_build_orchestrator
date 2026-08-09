namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [C2] Proje listesinin durum sayaçları — üst şerit/chip rozetleri bunu okur. <c>Queued</c> = henüz
/// başlamamış (<see cref="ProjectRowState.Pending"/>) satırlar. <c>DepAffected</c> yalnız <b>succeeded</b> +
/// dep-issue taşıyan satırları sayar (build-data.js:524-528) — filtre chip'i "dep" (statüden bağımsız,
/// bkz. <see cref="ProjectFilter"/>) ile bilerek FARKLIDIR: özet "kaç proje başarıyla derlendi ama yine de
/// bir bağımlılık uyarısı taşıyor" sorusunu yanıtlar. <c>StuckCycles</c> [cycle rounds/Task 8] yalnız
/// <b>skipped</b> + <see cref="ProjectRowViewModel.CycleUnconverged"/> taşıyan satırları sayar — kalıcı kırık
/// bir SCC'nin pre-skip'i, sıradan "güncel" skip'iyle GÖRÜNÜRDE aynıdır (ikisi de plain <c>Skipped</c>); bu
/// sayaç ikisini ayırt eden TEK yerdir.
/// </summary>
public readonly record struct RunCounters(int Total, int Building, int Queued, int Succeeded,
                                          int Failed, int Skipped, int DepAffected, int StuckCycles)
{
    public static RunCounters From(IEnumerable<ProjectRowViewModel> rows)
    {
        int total = 0, building = 0, queued = 0, succeeded = 0, failed = 0, skipped = 0, dep = 0, stuck = 0;
        foreach (var r in rows)
        {
            total++;
            switch (r.State)
            {
                case ProjectRowState.Started: building++; break;
                case ProjectRowState.Pending: queued++; break;
                case ProjectRowState.Succeeded:
                    succeeded++;
                    if (r.HasDepIssue) dep++; // yalnız succeeded + dep-issue
                    break;
                case ProjectRowState.Failed: failed++; break;
                case ProjectRowState.Skipped:
                    skipped++;
                    if (r.CycleUnconverged) stuck++; // [cycle rounds/Task 8] yalnız skipped + yakınsamama bayrağı
                    break;
            }
        }
        return new RunCounters(total, building, queued, succeeded, failed, skipped, dep, stuck);
    }
}
