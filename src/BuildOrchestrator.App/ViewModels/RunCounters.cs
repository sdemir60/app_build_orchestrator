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
///
/// <para>[cycle rounds/I2] <c>Building</c> "ŞU AN derlenen" demektir, "Started durumundaki satır" değil: bir
/// SCC'nin üyeleri sıralı invoke edilir ve ara tur sonuçları yayılmadığı için grup bitene kadar HEPSİ
/// <see cref="ProjectRowState.Started"/>'ta durur. Sırasını bekleyen üye (<see
/// cref="ProjectRowViewModel.CycleWaiting"/>) <c>Queued</c>'a taşınır — bölme değil TAŞIMA, toplam korunur.
/// Aksi halde 32 üyeli bir SCC 4 worker'lı bir run'da "32 building" raporlardı ve şerit "finishing 32 in
/// flight" derdi.</para>
/// </summary>
public readonly record struct RunCounters(int Total, int Building, int Queued, int Succeeded,
                                          int Failed, int Skipped, int DepAffected, int StuckCycles,
                                          int Cycle = 0)
{
    public static RunCounters From(IEnumerable<ProjectRowViewModel> rows)
    {
        int total = 0, building = 0, queued = 0, succeeded = 0, failed = 0, skipped = 0, dep = 0, stuck = 0, cycle = 0;
        foreach (var r in rows)
        {
            total++;
            if (r.HasDepIssue) dep++;  // [v1.5.1] statüden BAĞIMSIZ
            if (r.InCycle) cycle++;    // [v1.7.0 §5] kalıcı üyelik
            switch (r.State)
            {
                // "Started" ile "şu an derleniyor" AYNI ŞEY DEĞİL — tek predicate ProjectRowViewModel.IsCompiling.
                case ProjectRowState.Started: if (r.IsCompiling) building++; else queued++; break;
                case ProjectRowState.Pending: queued++; break;
                case ProjectRowState.Succeeded: succeeded++; break;
                case ProjectRowState.Failed: failed++; break;
                case ProjectRowState.Skipped:
                    skipped++;
                    if (r.CycleUnconverged) stuck++; // [cycle rounds/Task 8] yalnız skipped + yakınsamama bayrağı
                    break;
            }
        }
        return new RunCounters(total, building, queued, succeeded, failed, skipped, dep, stuck, cycle);
    }
}
