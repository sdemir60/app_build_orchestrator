using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Core.State;

/// <summary>
/// [T70] Bir proje BAŞARIYLA derlendiğinde ölçülen süreyi (ms) <see cref="BuildStateStore"/>'a kalıcı hale
/// getiren ince yardımcı — <see cref="Incremental.EtaCalculator"/>'ın gelecek tick'lerinin queued/building
/// tahminlerini besleyen <c>BuildState.LastDurationMs</c>'i yazar.
/// <para>
/// <b>Partial merge:</b> <see cref="BuildStateStore.Upsert"/> TÜM <see cref="BuildState"/> kaydını değiştirir
/// (partial-patch API'si yok) — bu yüzden burada önce mevcut kayıt <see cref="BuildStateStore.Load"/> ile
/// okunur, yalnız <see cref="BuildState.LastDurationMs"/>/<see cref="BuildState.LastResult"/>/
/// <see cref="BuildState.LastRunAt"/> güncellenir; <see cref="BuildState.BuiltSignature"/>/
/// <see cref="BuildState.BuiltCommit"/>/<see cref="BuildState.LastBranch"/> DOKUNULMADAN korunur. Kayıt hiç
/// yoksa (projenin ilk başarılı derlemesi) minimal bir <see cref="BuildState"/> (BuiltSignature=null) taban
/// alınır.
/// </para>
/// <para>
/// <b>Wiring notu [Task 12/13]:</b> Supervisor'ın <c>RunCoordinator</c>'ı henüz bu yardımcıyı bir proje
/// <c>projectSucceeded</c> olduğunda ÇAĞIRMIYOR — gerçek çağrı noktası (süre ölçümünün nereden geldiği,
/// hangi <see cref="BuildStateStore"/> örneğinin enjekte edileceği) sonraki bir wiring görevine bırakıldı
/// (Task 7/10'un obj-isolation resolver seam'iyle aynı desen — bkz. task-11-brief.md). Bu tip/metot PURE ve
/// TEST EDİLEBİLİR halde hazır; kompozisyon kökü (Program.cs) yalnız <c>ProjectSucceededEvent</c> geldiğinde
/// <c>PersistSucceeded(store, e.ProjectId, e.DurationMs, DateTimeOffset.UtcNow)</c> çağırmalı.
/// </para>
/// </summary>
public static class BuildDurationPersister
{
    /// <summary>
    /// <paramref name="projectId"/> için mevcut kaydı (varsa) korur, <see cref="BuildState.LastDurationMs"/>'i
    /// <paramref name="durationMs"/> ile, <see cref="BuildState.LastResult"/>'ı <see cref="BuildResult.Succeeded"/>
    /// ile, <see cref="BuildState.LastRunAt"/>'ı <paramref name="runAt"/> ile günceller ve
    /// <see cref="BuildStateStore.Upsert"/> ile diske yazar.
    /// </summary>
    public static void PersistSucceeded(BuildStateStore store, string projectId, long durationMs, DateTimeOffset runAt)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(projectId);

        BuildState? existing = store.Load().TryGetValue(projectId, out var found) ? found : null;
        BuildState updated = (existing ?? new BuildState(projectId, BuiltSignature: null)) with
        {
            ProjectId = projectId,
            LastResult = BuildResult.Succeeded,
            LastRunAt = runAt,
            LastDurationMs = durationMs,
        };

        store.Upsert(updated);
    }
}
