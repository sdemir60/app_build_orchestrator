namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [spec 2026-09-18 §6.2] Bir Sync'in konsol ve olay akışıyla ilişkisi — kim tetikledi, bölüm açar mı, ağa çıkar mı.
/// İlke: konsol bir <b>bölüm</b> anlatır; yeni bölümü yalnız kullanıcının başlattığı işlem ya da branch değişimi
/// açar. Aynı dünyada kendiliğinden olan tazelemeler bölüm açmaz.
/// <list type="table">
/// <listheader><term>Kip</term><description>Temizlik · fetch · transkript</description></listheader>
/// <item><term><see cref="Manual"/></term><description>Sync düğmesi: konsol + akış + liste + graf tıklamada
/// temizlenir (liste ve graf topolojiyle reveal'le döner), fetch, tam transkript, kalıcı işlem pill'i, seçim düşer.</description></item>
/// <item><term><see cref="Appended"/></term><description>Açılış, pull, Clean/Optimize devri, Settings Save / kök
/// değişimi: temizlik YOK (önceki işlemin ya da çağıranın notu kalır), fetch, transkript altına eklenir.</description></item>
/// <item><term><see cref="BranchChange"/></term><description>Konsol + akış + liste + graf temizlenir, yeni bölümün ilk satırları
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

/// <summary>[spec 2026-09-18 §6.2] <see cref="SyncMode"/> kurallarının TEK kaynağı — çağıran yerler kipi
/// karşılaştırmaz, bu soruları sorar (kopya YASAK).</summary>
public static class SyncModeRules
{
    /// <summary>Konsol + olay akışı istek anında temizlenir mi (yeni bölüm): Manual ve BranchChange.</summary>
    public static bool ClearsConsole(this SyncMode mode) => mode is SyncMode.Manual or SyncMode.BranchChange;

    /// <summary>[kullanıcı kararı 2026-09-19] Plan yüzeyi (liste + graf) baştan başlar mı: istek anında ekranda
    /// boşalır, Sync'in topolojisi gelince yapı aynı olsa da standart açılışla (reveal, graf fit) geri gelir —
    /// Manual ve BranchChange. Appended ve Silent yerinde tazeler, kamerayı oynatmaz.</summary>
    public static bool RestartsPlanSurface(this SyncMode mode) => mode is SyncMode.Manual or SyncMode.BranchChange;

    /// <summary>Motor ağa çıkıp fetch eder mi: Manual ve Appended. BranchChange ve Silent son bilinen uzak uca bakar.</summary>
    public static bool Fetches(this SyncMode mode) => mode is SyncMode.Manual or SyncMode.Appended;

    /// <summary>Transkriptin dim/info/cmd satırları konsola yazılır mı (warn/error her kipte yazılır).</summary>
    public static bool ShowsTranscript(this SyncMode mode) => mode != SyncMode.Silent;

    /// <summary>Sync kendini ekranda bir İŞLEM olarak gösterir mi: kalıcı işlem pill'i, seçimin düşmesi, faz
    /// <c>Syncing</c> (şerit + pill canlı), önceki koşunun hata metninin ve bindirmesinin silinmesi. Yalnız sessiz
    /// kip göstermez — kendiliğinden Sync kötü haberi silmez, ekranda iz bırakmaz.</summary>
    public static bool IsVisible(this SyncMode mode) => mode != SyncMode.Silent;
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
