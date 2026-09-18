namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [spec 2026-09-18 §6.2] Bir Sync'in konsol ve olay akışıyla ilişkisi — kim tetikledi, bölüm açar mı, ağa çıkar mı.
/// İlke: konsol bir <b>bölüm</b> anlatır; yeni bölümü yalnız kullanıcının başlattığı işlem ya da branch değişimi
/// açar. Aynı dünyada kendiliğinden olan tazelemeler bölüm açmaz.
/// <list type="table">
/// <listheader><term>Kip</term><description>Temizlik · fetch · transkript</description></listheader>
/// <item><term><see cref="Manual"/></term><description>Sync düğmesi: konsol + akış tıklamada temizlenir, fetch,
/// tam transkript, kalıcı işlem pill'i, seçim düşer.</description></item>
/// <item><term><see cref="Appended"/></term><description>Açılış, pull, Clean/Optimize devri, Settings Save / kök
/// değişimi: temizlik YOK (önceki işlemin ya da çağıranın notu kalır), fetch, transkript altına eklenir.</description></item>
/// <item><term><see cref="BranchChange"/></term><description>Konsol + akış temizlenir, yeni bölümün ilk satırları
/// çağıranın verdiği satırlardır (yeni branch), ardından transkript; fetch YOK.</description></item>
/// <item><term><see cref="Silent"/></term><description>Kendiliğinden Sync (commit, pencereye dönüş, HEAD'in branch
/// değişimi dışındaki hareketi): temizlik yok, fetch yok, pill yok, seçim korunur; transkript gizlenir ama
/// warn/error satırları yine yazılır; akışa tek satır (<see cref="SilentSyncReason"/>).</description></item>
/// </list>
/// </summary>
public enum SyncMode
{
    Manual,
    Appended,
    BranchChange,
    Silent,
}

/// <summary>[spec 2026-09-18 §6.2] Sessiz Sync'in nedeni — bitişte olay akışına düşen TEK satırı seçer
/// (metinler <see cref="StreamText"/>'te).</summary>
public enum SilentSyncReason
{
    /// <summary>Bir commit görüldü: satır her zaman yazılır (<see cref="StreamText.SyncedAfterCommit"/>).</summary>
    Commit,

    /// <summary>Pencereye dönüş ya da HEAD'in branch değişimi dışındaki bir hareketi: satır yalnız kararı değişen
    /// satır varsa yazılır (<see cref="StreamText.SyncedProjectsChanged"/>).</summary>
    Refresh,
}
