using System.Collections.ObjectModel;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [A5/T69 · Fix wave 1 — Finding 6] <see cref="RunViewModel"/>'in <b>workspace yüzeyi</b>: Sync fazı, hedef
/// commit, branch/worktree envanteri ve topoloji. Ayrı bir partial dosyada, çünkü ana dosya run/log/ETA
/// yüzeyini taşır ve bu iki sorumluluk aynı dosyada birbirine karışıyordu (sonraki UI task'ları bu yüzeye
/// sayaç/filtre/seçim ekleyecek). <b>Davranış değişikliği YOKTUR</b> — kod aynen taşınmıştır; tek istisna
/// bu dosyada açıkça işaretlenen Finding 2 ayrımıdır.
/// </summary>
public sealed partial class RunViewModel
{
    /// <summary>[design-v1 §3.1] Faz makinesi. A5 yalnız <c>Syncing</c> (syncStarted) ve <c>Idle</c>
    /// (syncCompleted) geçişlerini sürer; kalan geçişler (boot/running/done/stopped) sonraki UI task'larınındır.</summary>
    [ObservableProperty] private AppPhase _phase = AppPhase.Empty;

    /// <summary>[N10] Sync'in çözdüğü hedef commit — remote ulaşılamadıysa yerel HEAD (bkz. <see cref="FetchDegraded"/>).</summary>
    [ObservableProperty] private string? _targetSha;

    /// <summary>true ⇒ son Sync'te fetch başarısız oldu ve akış yerel HEAD ile devam etti (offline degrade).</summary>
    [ObservableProperty] private bool _fetchDegraded;

    /// <summary>[Fix wave 1 — Finding 2] <c>syncStarted</c> geldi ama <c>syncCompleted</c> (ya da Sync'i bitiren
    /// bir hata) HENÜZ gelmedi. Bir run-bitiren hata kodunun KAYNAĞINI ayırt etmek için gerekir — bkz.
    /// <see cref="TryConsumeSyncFailure"/>. Faz (<see cref="Phase"/>) bu iş için yeterli DEĞİLDİR: sonraki UI
    /// task'ları koşan bir run'da fazı <c>Running</c>'e çekecek ve Syncing damgası kaybolacaktır.</summary>
    private bool _syncInFlight;

    public ObservableCollection<BranchRef> Branches { get; } = [];
    public ObservableCollection<Worktree> Worktrees { get; } = [];

    /// <summary>Son <c>workspaceTopology</c>'nin düğümleri (build-order) — bağımlılık, katman ve solution
    /// bilgisinin TEK kaynağı; graf paneli (D5) ve katman gruplaması (D1) bunu okur.</summary>
    public IReadOnlyList<ProjectNode> Topology { get; private set; } = [];

    /// <summary>Workspace'teki .sln'ler (ad + tam yol) — Open-in-VS (E1) <see cref="ProjectNode.SolutionNames"/>'i buradan çözer.</summary>
    public IReadOnlyList<SolutionRef> Solutions { get; private set; } = [];

    /// <summary>Topoloji DEĞİŞTİĞİNDE (her statü güncellemesinde DEĞİL) tetiklenir — D5 grafı yalnız bunda yeniden kurar.</summary>
    public event EventHandler? TopologyChanged;

    /// <summary>[A5/T69] Sync başladı: faz <c>Syncing</c>'e geçer ve akış "uçuşta" işaretlenir.</summary>
    private void OnSyncStarted()
    {
        _syncInFlight = true;
        Phase = AppPhase.Syncing;
    }

    /// <summary>[A5/T69] Sync bitti: hedef commit + degrade bayrağı kaydedilir, faz <c>Idle</c>'a geçer
    /// (proje durumları artık bilinir — hollow değil).</summary>
    private void OnSyncCompleted(SyncCompletedEvent e)
    {
        _syncInFlight = false;
        TargetSha = e.TargetSha;
        FetchDegraded = e.FetchDegraded;
        Phase = AppPhase.Idle;
    }

    /// <summary>
    /// [A5/T69] Başarısız bir Sync yalnız <c>planFailed</c> yayınlar — <c>syncCompleted</c> GELMEZ. Faz burada
    /// bırakılmazsa Syncing'de ASILI kalır (şerit sonsuza dek "▸ Sync — git fetch origin…" gösterir). Geri
    /// düşülecek faz, ELDE bir topoloji olup olmamasıdır: varsa önceki Sync'in sonucu hâlâ geçerlidir (Idle),
    /// yoksa repo henüz hiç sync'lenmemiştir (Boot).
    ///
    /// <para><b>[Fix wave 1 — Finding 2] Dönüş değeri = "bu hata Sync'e aittir, run state'e DOKUNMA".</b>
    /// A5 <c>planFailed</c>'ı hem run-planlamanın hem Sync'in kodu yaptı; Sync SALT-OKURDUR ve koşan bir run
    /// sırasında da tetiklenebilir (bkz. <see cref="OnWorkspaceTopology"/>'nin mid-run koruması). Ayrım
    /// yapılmazsa başarısız bir Sync, motor derlemeye DEVAM EDERKEN run'ı bitmiş gösterir; Stop erişilemez
    /// olur ve sonraki projectSucceeded/runCompleted yıkılmış bir state'e düşer.</para>
    ///
    /// <para><b>Ayırt edici — üç koşulun BİRLİKTE sağlanması:</b>
    /// (1) kod Sync'in üretebildiği bir kod (<see cref="SyncErrorCodes"/>; Sync'in TEK hata kanalı
    /// <c>planFailed</c>'dır — hem servisin girdi kapıları hem <c>SupervisorHost</c>'un catch-all'u onu yayınlar,
    /// bu yüzden ör. mid-run gelebilen <c>runFailed</c> ASLA Sync'e atfedilmez),
    /// (2) uçuşta bir Sync var (<see cref="_syncInFlight"/>),
    /// (3) run PLANLAMA penceresinde değil (<see cref="IsStarting"/> false). Run tarafında <c>planFailed</c>
    /// YALNIZCA o pencerede — <c>runStarted</c>'dan ÖNCE — üretilir (bkz. <c>RunCoordinator.ExecuteRunAsync</c>:
    /// planner çağrısı runStarted'dan öncedir), dolayısıyla pencere dışında gelen bir <c>planFailed</c>'ın
    /// kaynağı yalnızca Sync olabilir.</para>
    ///
    /// <para><b>Pencereler çakışırsa</b> (aynı anda hem Sync hem yeni bir run başlatılmış) run tarafı seçilir:
    /// orada "yıkım" YALNIZ <see cref="IsStarting"/>'i geri açar (henüz KOŞAN bir run yoktur) ve bunu yapmamak
    /// butonları kalıcı kilitlerdi — canlı bir run'ı yıkmakla kıyaslanamayacak kadar ucuz bir hata. O dalda
    /// <see cref="_syncInFlight"/> BİLEREK temizlenmez: hata gerçekte run'a aitse Sync hâlâ uçuştadır ve bir
    /// sonraki başarısızlığı yine Sync'e atfedilebilmelidir.</para>
    /// </summary>
    private bool TryConsumeSyncFailure(string code)
    {
        if (!SyncErrorCodes.Contains(code) || !_syncInFlight) return false;
        // Faz her iki dalda da bırakılır: hata Sync'e aitse syncCompleted GELMEYECEK (asılı kalırdı); run'a
        // aitse uçuştaki Sync zaten kendi syncCompleted'ıyla fazı tazeleyecek.
        if (Phase == AppPhase.Syncing) Phase = Topology.Count > 0 ? AppPhase.Idle : AppPhase.Boot;
        if (IsStarting) return false; // çakışan pencere → run tarafı seçilir (yukarıdaki gerekçe)
        _syncInFlight = false;        // hata Sync'e ait: bu Sync bitti, run state'ine DOKUNULMAZ
        return true;
    }

    /// <summary>Sync'in yayınlayabildiği hata kodları — <see cref="RunEndingErrorCodes"/>'un Sync'e de ait olan
    /// ALT KÜMESİ. Yeni bir kod icat edilmedi: <c>planFailed</c> zaten planlama-hatası kanalıdır ve Sync tam
    /// olarak planlama pipeline'ıdır (bkz. <c>SyncWorkspaceService</c>).</summary>
    private static readonly HashSet<string> SyncErrorCodes = new(StringComparer.Ordinal) { "planFailed" };

    /// <summary>
    /// [A5/T69] <see cref="Projects"/>'i topolojiden BUILD-ORDER'da yeniden kurar ve <see cref="TopologyChanged"/>'i
    /// tetikler. Will-dot'lar BURADA kurulmaz: hemen ardından gelen <see cref="BuildPreviewEvent"/> (mevcut
    /// handler) onları zaten kurar — ikinci bir will-build yolu açılmaz.
    /// <para>
    /// [A13.2] <b>Koleksiyon reset'i YASAK:</b> <c>Clear()</c> bir Reset bildirimi yayınlar (item container'ları
    /// ve seçim çöker). Bunun yerine liste YERİNDE uzlaştırılır — yalnız Remove/Move/Insert bildirimleri çıkar
    /// ve topolojide kalan satırlar (dolayısıyla seçim) korunur.
    /// </para>
    /// <para>
    /// <b>Mid-run koruma:</b> satır durumları YALNIZ hiçbir run koşmuyorken sıfırlanır. Sync salt-okurdur ve
    /// koşan bir run sırasında da tetiklenebilir; o durumda canlı sonuçları (Succeeded/Failed) silmek, ekranı
    /// motorun gerçek durumundan koparırdı.
    /// </para>
    /// </summary>
    private void OnWorkspaceTopology(WorkspaceTopologyEvent e)
    {
        Topology = e.Nodes;
        Solutions = e.Solutions;

        var wanted = e.Nodes.Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = Projects.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Projects[i].Id)) Projects.RemoveAt(i);

        for (int i = 0; i < e.Nodes.Count; i++)
        {
            var node = e.Nodes[i];
            int existing = IndexOf(node.Id);
            if (existing < 0) Projects.Insert(i, new ProjectRowViewModel(node.Id, node.Name, ProjectRowState.Pending));
            else if (existing != i) Projects.Move(existing, i);
        }

        if (!IsRunning)
            foreach (var row in Projects)
            {
                row.State = ProjectRowState.Pending; // Sync = yeni taban: önceki run'ın sonuçları artık geçmiştir
                row.DepIssues = null;
                row.DurationMs = 0;
            }

        TopologyChanged?.Invoke(this, EventArgs.Empty);
    }

    private int IndexOf(string projectId)
    {
        for (int i = 0; i < Projects.Count; i++)
            if (string.Equals(Projects[i].Id, projectId, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>Bir listeyi gelen anlık görüntüyle değiştirir. <see cref="ObservableCollection{T}.Clear"/>
    /// KULLANILMAZ ([A13.2] reset yasağı) — sondan silip yeniden eklemek yalnız Remove/Add bildirimleri üretir.</summary>
    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        for (int i = target.Count - 1; i >= 0; i--) target.RemoveAt(i);
        foreach (var item in source) target.Add(item);
    }
}
