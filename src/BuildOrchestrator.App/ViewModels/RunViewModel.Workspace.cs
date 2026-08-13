using System.Collections.ObjectModel;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Scheduling;
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
    /// (syncCompleted) geçişlerini sürer; kalan geçişler (boot/running/stopping/done/stopped) sonraki UI
    /// task'larınındır.
    /// <para>[Stopping] <see cref="StopCommand"/>'ın CanExecute'u fazı OKUR (<c>Stopping</c>'te pasifleşir) —
    /// bildirim olmadan buton, Stop'a basıldıktan sonra da tıklanabilir kalır ve her tıklama yeni bir
    /// <c>stopRun</c> üretirdi.</para></summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private AppPhase _phase = AppPhase.Empty;

    /// <summary>[Stopping] Koşmayan/dinlenen faz: elde topoloji varsa <c>Idle</c> (önceki Sync'in sonucu hâlâ
    /// geçerli), yoksa <c>Boot</c> (repo henüz hiç sync'lenmemiş). Bir run/Sync penceresi SONUÇSUZ kapandığında
    /// (engine ölümü, run-bitiren hata, planlama sırasında stop) geri düşülecek fazın TEK tanımı — kural üç
    /// ayrı yerde yazılıydı ve biri güncellenirken diğerlerinin sessizce ayrışması işten değildi.</summary>
    private AppPhase RestingPhase => HasTopology ? AppPhase.Idle : AppPhase.Boot;

    /// <summary>Elde bu workspace'in topolojisi VAR MI — yani bir Sync gerçekten koştu ve iş üretti.
    ///
    /// <para><b>Neden run komutlarının kapısı budur:</b> tam analiz YALNIZ Sync'te koşar (ARCHITECTURE §6) ve
    /// App'e topolojiyi yalnız <c>workspaceTopology</c> taşır; bir run yalnız <c>buildPreview</c> yayınlar.
    /// Dolayısıyla Sync'siz bir Build motoru gerçekten derletir ama ekranda liste, graf ve sayaç BOŞ kalır —
    /// kullanıcı ne derlendiğini göremeden koşan bir run'a bakar. Kapı bunu baştan keser: önce Sync.</para>
    ///
    /// <para>Boş topoloji (klasörün altında hiç proje yok) da kapalıdır — derlenecek bir şey yoktur.
    /// <see cref="RestingPhase"/> ile AYNI soruyu sorar, bu yüzden soru TEK yerde durur (kopya YASAK).</para></summary>
    public bool HasTopology => Topology.Count > 0;

    /// <summary>[cycles] Son topolojide en az bir dairesel bağımlılık (SCC) var mı — <see cref="RunViewModel.BuildCyclesCommand"/>'ın
    /// kapısı. Soru üyelik haritasına sorulur (<c>_cycleGroups</c>), satırların <c>InCycle</c> bayrağına DEĞİL:
    /// harita motorun grubu sürdüğü gövdenin aynısıdır, satır bayrağı ise onun bir türevi — kapıyı türevden
    /// sormak iki tarafın sessizce ayrışabileceği ikinci bir cevap yaratırdı.</summary>
    public bool HasCycles => _cycleGroups is { Count: > 0 };

    /// <summary>[design v1.7.0 §2.2] Her döngünün yolu (<c>A → B → C → A</c>), topolojideki sırayla. Şeridin
    /// döngü kümesi tooltip'ini bundan kurar; satırlar kendi yollarını <see cref="ProjectRowViewModel.CyclePath"/>
    /// üzerinden alır (ikisi de <see cref="CycleText.Path"/>'ten gelir).</summary>
    public IReadOnlyList<string> CyclePaths { get; private set; } = [];

    /// <summary>[Task 6] Son topolojideki SCC (döngü grubu) SAYISI — Cycles buton tooltip'inin ilk sayısı
    /// (<see cref="Views.ActionBar.RefreshCyclesTooltip"/>'in okuduğu iki sayıdan biri). <c>_cycleGroups</c>'un
    /// YALIN yansımasıdır, ikinci bir sayaç TUTULMAZ (kopya YASAK).</summary>
    public int CycleGroupCount => _cycleGroups?.Count ?? 0;

    /// <summary>[Task 6] Son topolojideki TÜM döngü üyelerinin TOPLAMI — tooltip'in ikinci sayısı.
    /// <see cref="CycleGroups"/>'a üye API'si EKLENMEDİ (brief kısıtı, kopya YASAK): topoloji olayının ham
    /// <c>Cycles</c> listesi zaten <see cref="OnWorkspaceTopology"/>'ye gelir, toplam ORADA sayılıp burada
    /// saklanır.</summary>
    public int CycleMemberCount => _cycleMemberCount;
    private int _cycleMemberCount;

    /// <summary>Stop istendi: motor bundan sonra <c>runStopped</c>/<c>runCompleted</c> ile cevap vermelidir —
    /// sessizlik saati burada da kurulur (<see cref="OnIsStartingChanged"/> ile aynı gerekçe). Faz set eden
    /// HER yol buradan geçtiği için kurma noktası tek yerdedir.</summary>
    partial void OnPhaseChanged(AppPhase value)
    {
        if (value == AppPhase.Stopping) ArmEngineWatchdog();
    }

    /// <summary>[N10] Sync'in çözdüğü hedef commit — remote ulaşılamadıysa yerel HEAD (bkz. <see cref="FetchDegraded"/>).</summary>
    [ObservableProperty] private string? _targetSha;

    /// <summary>[W1] Hedef sha her satıra İTİLİR (<c>IsRunActive</c>/<c>NamePrefix</c> deseni) — kart onu render
    /// anında ata ağaçtan ÇEKMEZ. <c>buildPreview</c> deterministik olarak <c>syncCompleted</c>'dan ÖNCE gelir;
    /// çekme modelinde satır sha'sını TargetSha daha null'ken hesaplayıp bir daha tazelemiyordu (ilk Sync'ten
    /// sonra slot BOŞ kalırdı). Tazeleme sinyali TEK ve VM tarafındadır: satır başına ek bir PropertyChanged
    /// abonesi AÇILMAZ (satır zaten yalnız kendi VM'ini dinler) — L1'in satır-realize bütçesi korunur.</summary>
    partial void OnTargetShaChanged(string? value)
    {
        foreach (var row in Projects) row.TargetSha = value;
    }

    /// <summary>true ⇒ son Sync'te fetch başarısız oldu ve akış yerel HEAD ile devam etti (offline degrade).</summary>
    [ObservableProperty] private bool _fetchDegraded;

    /// <summary>[Fix wave 1 — Finding 2] <c>syncStarted</c> geldi ama <c>syncCompleted</c> (ya da Sync'i bitiren
    /// bir hata) HENÜZ gelmedi. Bir run-bitiren hata kodunun KAYNAĞINI ayırt etmek için gerekir — bkz.
    /// <see cref="TryConsumeSyncFailure"/>. Faz (<see cref="Phase"/>) bu iş için yeterli DEĞİLDİR: sonraki UI
    /// task'ları koşan bir run'da fazı <c>Running</c>'e çekecek ve Syncing damgası kaybolacaktır.</summary>
    private bool _syncInFlight;

    /// <summary>Branch envanteri. <see cref="SnapshotCollection{T}"/>: yayın başına EN ÇOK bir bildirim, içerik
    /// değişmemişse HİÇ — gerekçesi (ölçülen O(n²) donma) o tipin özetindedir.</summary>
    public SnapshotCollection<BranchRef> Branches { get; } = [];

    /// <summary>Worktree envanteri — <see cref="Branches"/> ile birebir aynı yayın sözleşmesi.</summary>
    public SnapshotCollection<Worktree> Worktrees { get; } = [];

    /// <summary>Son <c>workspaceTopology</c>'nin düğümleri (build-order) — bağımlılık, katman ve solution
    /// bilgisinin TEK kaynağı; graf paneli (D5) ve katman gruplaması (D1) bunu okur.</summary>
    public IReadOnlyList<ProjectNode> Topology { get; private set; } = [];

    /// <summary>[D5] Topoloji adlarından türetilen kısa-ad öneki (tek otorite, <see cref="GraphNode.CommonDotPrefix"/>) —
    /// her <see cref="ProjectRowViewModel.NamePrefix"/>'e itilir (şerit chip'i + dep-tooltip bunu okur; graf tarafı
    /// aynı öneki <see cref="GraphBinder"/> içinde kendi türetir).</summary>
    private string _graphNamePrefix = "";

    /// <summary>Workspace'teki .sln'ler (ad + tam yol) — Open-in-VS (E1) <see cref="ProjectNode.SolutionNames"/>'i buradan çözer.</summary>
    public IReadOnlyList<SolutionRef> Solutions { get; private set; } = [];

    /// <summary>Topoloji DEĞİŞTİĞİNDE (her statü güncellemesinde DEĞİL) tetiklenir — D5 grafı yalnız bunda yeniden kurar.</summary>
    public event EventHandler? TopologyChanged;

    /// <summary>[E2/§5-b verify-then-fix] Son yayınlanan topolojinin YAPI imzası (düğüm Id/Ad/katman + kenarlar) —
    /// <see cref="OnWorkspaceTopology"/> her <c>workspaceTopology</c>'de değil, YALNIZ bu imza değiştiğinde
    /// <see cref="TopologyChanged"/> ateşler. Aksi halde mid-run bir Sync (node seti değişmese de) koşan grafı
    /// yeniden-reveal edip kamerayı re-home ediyordu (SetGraph = tam inşa + stagger). Statü değişimleri (InCycle/
    /// WillBuild) BURAYA girmez — onlar zaten <c>UpdateStatuses</c> (PushGraphStatuses) yoluyla akar. <c>null</c> =
    /// henüz hiç topoloji gelmedi → ilk topoloji her zaman ateşler (graf ilk kez kurulur).
    ///
    /// <para><b>[A13/B3 · E4 — KARAR KAYDI, davranış DEĞİŞMEDİ]</b> Bu guard'ın <b>liste tarafındaki</b> sonucu
    /// bugüne dek hiçbir yerde yazılı değildi: <c>TopologyChanged</c> ateşlemeyen bir Sync (yani "no changes" —
    /// aynı yapının yeniden yayınlanması) <c>MainWindow.RefreshProjectGroups</c>'u <b>hiç çağırmaz</b>, dolayısıyla
    /// <c>StickyLayerList.SetGroups</c> koşmaz ve <b>o Sync'te kademeli beliriş (bo-reveal) OYNAMAZ</b> — kartlar
    /// yeniden belirmez.
    /// <list type="bullet">
    ///   <item><b>Bu KASITLIDIR — gerekçe "gereksiz churn"dür.</b> <c>SetGroups</c> <c>ItemsSource</c> ataması
    ///   yapar; bu, <c>ItemsControl</c> için TAM reset'tir (container teardown + yeniden üretim). <b>Otoritenin
    ///   metni niteliksizdir</b> (plan v7, A13.2 Motion maddesi: "koleksiyon reset'i YASAK"); bu repo kuralı
    ///   <b>DAR okur</b> — yasağın koruduğu şeyler zarar görmedikçe reset meşru sayılır. <b>Bu okuma repoya
    ///   aittir, otoritenin metni DEĞİLDİR.</b> Gerekçesi <c>StickyLayerList.SetGroups</c>'un XML doc'unda
    ///   ("Reset semantiği BİLEREK KORUNDU") yazılıdır ve zararsızlık iki şarta bağlanır: (a) seçim satır VM'lerinde yaşar,
    ///   (b) <b>gereksiz churn ÇAĞIRAN tarafta kapatılır</b>. <b>İşte bu guard, (b)'nin uygulanışıdır</b> —
    ///   iki doc çelişmez, biri ötekinin şartını sağlar. Guard olmasaydı değişmemiş bir liste her Sync'te tam
    ///   reset yer ve kullanıcıya sebepsiz bir "kartlar yeniden belirdi" flaşı olarak görünürdü.</item>
    ///   <item><b>OTORİTE BURADA AYRIŞIYOR (ölçüldü, A13/B3 fix round 1).</b> Prototipte <c>doSync()</c>
    ///   (<c>BuildApp.jsx:1186-1193</c>) <c>revealKey</c>'i <b>HER Sync'te KOŞULSUZ</b> artırır — topoloji
    ///   değişti mi diye BAKMAZ; yani otoritede "no changes" bir Sync'te de kartlar yeniden belirir. Üretim
    ///   bilerek AYRILIR: gerekçe yukarıdaki churn maddesidir. (<c>BuildApp.jsx:1378</c> Sync yolu DEĞİL,
    ///   <c>pickFolder()</c>'dır — eski kayıt bu satıra dayanarak otoriteyi yanlışlıkla "destekleyici"
    ///   gösteriyordu.) Ayrışma task-B3-report.md <c>## Concerns</c>'te de kayıtlıdır.</item>
    ///   <item><b>Bedeli (E5 ile bağı):</b> "bir sonraki reveal eksik kalanı yakalar" gerekçesi bu yüzden
    ///   GEÇERSİZDİR — bir sonraki reveal HİÇ gelmeyebilir. Reveal'in kapsamı bu nedenle kendi içinde eksiksiz
    ///   olmak zorundadır (bkz. <c>StickyLayerList.PlayRevealStagger</c>'ın layout zorlaması).</item>
    /// </list>
    /// Karakterizasyon testi: <c>ProjectListFilterTests.A_no_changes_sync_neither_resets_the_list_nor_replays_the_reveal</c>.</para></summary>
    private string? _lastTopologySignature;

    /// <summary>[A5/T69] Sync başladı: faz <c>Syncing</c>'e geçer ve akış "uçuşta" işaretlenir.
    /// <para>[Fix wave 1, C2 review Finding 1] <see cref="RunViewModel.RebuildCommand"/>
    /// artık <c>_syncInFlight</c>'a da bakıyor (<see cref="RunViewModel.CanRebuildOrRetry"/>) — bu geçişte CanExecuteChanged
    /// elle tetiklenmezse [NotifyCanExecuteChangedFor] zinciri (yalnız IsRunning/IsStarting'e bağlı) bu iki
    /// butonun gerçek pencerede Sync başlar başlamaz disabled görünmesini SAĞLAMAZ.</para>
    /// <para>[D2 review fix, Finding 1] <see cref="RunViewModel._willBuildIds"/> BURADA temizlenir: küme ADD-ONLY
    /// olduğundan (yalnız <see cref="RunViewModel.OnBuildPreview"/> ekler) ve önceki Clear noktası yalnız
    /// <see cref="RunViewModel.OnRunStarted"/> olduğundan, ikinci (run'sız) bir Sync kendi <c>BuildPreviewEvent</c>'ini
    /// (A5 amendment — her Sync bunu yayınlar) hiç temizlik olmadan üzerine yazardı: dirty=false gelen satırlar
    /// kümeden ÇIKARILMAZ (yalnız true eklenir), bu yüzden önceki Sync'in willBuild'i BİRİKİRDİ ve Idle şeridi
    /// bayat "N to build" gösterirdi.</para></summary>
    private void OnSyncStarted()
    {
        _syncInFlight = true;
        SyncErrorMessage = null; // [E2/T10] retry başladı — önceki Sync hatası şeritten kalkar
        // [runFailed] Önceki run'ın hata gerekçesi de kalkar: aksi halde KIRMIZI "Run failed — …" satırı, Sync
        // ilerlemesini ("▸ Sync — git fetch origin…") kullanıcı yeni bir run başlatana kadar gizlerdi.
        RunErrorMessage = null;
        Phase = AppPhase.Syncing;
        _willBuildIds.Clear(); // [D2 review fix] her Sync başında taze — hemen ardından gelen BuildPreviewEvent yeniden doldurur
        RebuildCommand.NotifyCanExecuteChanged();
        BuildCyclesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>[A5/T69] Sync bitti: hedef commit + degrade bayrağı kaydedilir, faz <c>Idle</c>'a geçer
    /// (proje durumları artık bilinir — hollow değil).</summary>
    private void OnSyncCompleted(SyncCompletedEvent e)
    {
        TargetSha = e.TargetSha;
        FetchDegraded = e.FetchDegraded;
        SyncErrorMessage = null; // [E2/T10] Sync başarıyla bitti — varsa önceki hata metni temizlenir
        ReleaseSyncPhase();    // [C2 fold] uçuş bayrağını normal yoldan da BURADAN temizle (tek yer)
        Phase = AppPhase.Idle; // Sync başarıyla bitti: durumlar kesin bilinir (degrade dahil)
    }

    /// <summary>[C2 fold — A5 review] Uçuştaki Sync'i serbest bırakır: <see cref="_syncInFlight"/> temizlenir ve
    /// faz <c>Syncing</c>'de ASILI kaldıysa elde topoloji varsa <c>Idle</c>, yoksa <c>Boot</c>'a düşürülür.
    /// İki yerden çağrılır: (1) normal <see cref="OnSyncCompleted"/> (yalnız bayrağı temizler — çağıran fazı
    /// zaten Idle yapar) ve (2) <see cref="OnEngineExited"/> — engine Sync ORTASINDA ölürse (ne syncCompleted
    /// ne Sync-bitiren hata gelir) faz Syncing'de sonsuza dek asılı kalamaz ve bayrak sızmaz.</summary>
    private void ReleaseSyncPhase()
    {
        _syncInFlight = false;
        if (Phase == AppPhase.Syncing) Phase = RestingPhase;
        // [Fix wave 1, C2 review Finding 1] OnSyncStarted'ın simetriği: hem normal syncCompleted hem
        // engine-ölümü-mid-sync (OnEngineExited) yolu BURADAN geçer — Rebuild/Cycles'ı tek yerden geri açar.
        RebuildCommand.NotifyCanExecuteChanged();
        BuildCyclesCommand.NotifyCanExecuteChanged();
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
    private bool TryConsumeSyncFailure(string code, string message)
    {
        if (!SyncErrorCodes.Contains(code) || !_syncInFlight) return false;
        // Faz her iki dalda da bırakılır: hata Sync'e aitse syncCompleted GELMEYECEK (asılı kalırdı); run'a
        // aitse uçuştaki Sync zaten kendi syncCompleted'ıyla fazı tazeleyecek.
        if (Phase == AppPhase.Syncing) Phase = RestingPhase;
        if (IsStarting) return false; // çakışan pencere → run tarafı seçilir (yukarıdaki gerekçe)
        _syncInFlight = false;        // hata Sync'e ait: bu Sync bitti, run state'ine DOKUNULMAZ
        SyncErrorMessage = message;   // [E2/T10] şerit KIRMIZI "Sync failed — {reason}" gösterir (retry = Sync)
        // [re-review C2, Finding 4] OnSyncStarted'ın simetriği burada da gerekir: bu, _syncInFlight'ı false'a
        // çeken 4. geçiştir (diğer üçü OnSyncStarted/ReleaseSyncPhase'in iki çağrı yeri) — Fix wave 1 bunu
        // kaçırmıştı, butonlar bir sonraki ilgisiz bildirime kadar stale-disabled kalıyordu.
        RebuildCommand.NotifyCanExecuteChanged();
        BuildCyclesCommand.NotifyCanExecuteChanged();
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
        // [cycle rounds/I2] SCC üyelik haritası topolojinin İKİNCİ yarısından (Cycles) kurulur — motorun grubu
        // sürdüğü haritanın AYNI gövdesiyle (kopya YASAK). App'in tek sorusu üyeliktir: koşan bir grupta hangi
        // Started üye sırasını bekliyor (bkz. OnProjectStarted).
        _cycleGroups = CycleGroups.From(e.Nodes, e.Cycles);
        // [Task 6] Cycles düğmesinin tooltip'i grup + üye toplamını okur (CycleGroupCount/CycleMemberCount).
        // Değer AÇIKÇA duyurulur: HasCycles boole'u aynı kalsa da (ör. 2→3 döngü) sayılar değişmiş olabilir —
        // türetilmiş özelliğin kendi bildirimi yok (OnBranchList'in ActiveBranchName deseniyle AYNI).
        _cycleMemberCount = e.Cycles.Sum(scc => scc.Count);
        OnPropertyChanged(nameof(HasCycles));
        // [D5] Kısa-ad öneki topoloji adlarından türetilir (tek otorite) — aşağıda her satıra itilir.
        _graphNamePrefix = GraphNode.CommonDotPrefix(e.Nodes.Select(n => n.Name).ToList());

        var wanted = e.Nodes.Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = Projects.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Projects[i].Id)) Projects.RemoveAt(i);

        for (int i = 0; i < e.Nodes.Count; i++)
        {
            var node = e.Nodes[i];
            // [T53-UI] Kart soluk sln adı — bir proje birden çok .sln içerebilir; kart ilk adı gösterir.
            string? solutionName = node.SolutionNames.Count > 0 ? node.SolutionNames[0] : null;
            int existing = IndexOf(node.Id);
            if (existing < 0)
                Projects.Insert(i, new ProjectRowViewModel(node.Id, node.Name, ProjectRowState.Pending, solutionName)
                {
                    // [Fix wave 1 · D1 review Finding 1] cycle üyeliği topolojinin TEK kaynağıdır (SolutionName gibi
                    // taşınır); Status bunu cycle görsel statüsüne çevirir. IsRunActive queued türetimi için.
                    InCycle = node.InCycle,
                    IsRunActive = RunActive,
                    TargetSha = TargetSha, // [W1] hedef sha satıra İTİLİR (kart onu atalardan çekmez)
                });
            else
            {
                Projects[existing].SolutionName = solutionName; // topoloji sln atamasını değiştirmiş olabilir
                Projects[existing].InCycle = node.InCycle;       // cycle üyeliği topolojiyle değişmiş olabilir
                if (existing != i) Projects.Move(existing, i);
            }
        }

        if (!IsRunning)
            foreach (var row in Projects)
            {
                row.State = ProjectRowState.Pending; // Sync = yeni taban: önceki run'ın sonuçları artık geçmiştir
                row.DepIssues = null;
                row.DurationMs = 0;
            }

        // [D5] Kısa-ad öneki her satıra itilir (IsRunActive deseni) — koşarken de: mid-run Sync öneki değiştirmiş olabilir.
        foreach (var row in Projects) row.NamePrefix = _graphNamePrefix;

        // [design v1.7.0 §2.4] Döngü YOLU (A → B → C → A) satırlara itilir: nokta ve uyarı üçgeni onu
        // buradan okur. Yol topolojinin kendi sırasıyla kurulur (motorun grup sırası) ve TEK yerde
        // biçimlenir (CycleText.Path). Ad çözümü topolojidendir — satır adı henüz kurulmamış olabilir.
        var nameById = e.Nodes.ToDictionary(n => n.Id, n => n.Name, StringComparer.OrdinalIgnoreCase);
        CyclePaths = [.. e.Cycles.Select(scc =>
            CycleText.Path([.. scc.Select(id => nameById.TryGetValue(id, out var n) ? n : id)]))];
        var pathByMember = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int c = 0; c < e.Cycles.Count; c++)
            foreach (string id in e.Cycles[c]) pathByMember[id] = CyclePaths[c];
        foreach (var row in Projects)
            row.CyclePath = pathByMember.TryGetValue(row.Id, out var path) ? path : "";

        // [E2/§5-b] TopologyChanged (→ D5 SetGraph = tam inşa + reveal stagger + kamera re-home) YALNIZ graf YAPISI
        // (düğüm Id/Ad/katman + kenar seti) değiştiğinde ateşlenir. Aynı yapının yeniden yayınlanması (ör. mid-run
        // Sync) koşan grafı yeniden-reveal ETMEZ; statü/dep tikleri zaten UpdateStatuses'tan (PushGraphStatuses) akar.
        //
        // [T2 fix-1 · I-C] SIRA ÖNEMLİ: TopologyChanged ARTIK RefreshRunSurface'ten ÖNCE ateşlenir.
        // Ölçülen kusur: ters sırada her topoloji değişimi listeyi İKİ KEZ tam reset ediyordu —
        // RefreshRunSurface → OnPropertyChanged(VisibleProjects) → RefreshVisibleRows (imza değişti, guard
        // tutmaz) → ApplyProjectGroups(reveal:false) [1. reset, tamamen çöp], hemen ardından TopologyChanged →
        // RefreshProjectGroups → ApplyProjectGroups(reveal:true) [2. reset]. "Churn imza guard'ıyla kesiliyor"
        // savunması bu yol için geçerli DEĞİLDİ: guard yalnız reveal:false dalındadır.
        // Yeni sırada reveal:true dalı listeyi kurar ve İMZAYI yazar; hemen sonra gelen RefreshRunSurface'in
        // VisibleProjects bildirimi guard'a çarpıp NO-OP olur. Sayaç tüketicileri sıradan etkilenmez:
        // TopologyChanged abonelerinden (RefreshProjectGroups/RebuildGraph) hiçbiri Counters okumaz.
        string signature = TopologySignature(e.Nodes);
        if (signature != _lastTopologySignature)
        {
            _lastTopologySignature = signature;
            TopologyChanged?.Invoke(this, EventArgs.Empty);
        }

        // [topoloji kapısı] Run komutlarının kapısı <see cref="HasTopology"/>'dir ve o BU olayda açılır/kapanır
        // (boş bir topoloji onu geri kapatır). RelayCommand CommandManager.RequerySuggested'a abone OLMADIĞINDAN
        // bildirim elle tetiklenmezse Build gerçek pencerede Sync bittikten sonra da PASİF görünürdü.
        BuildCommand.NotifyCanExecuteChanged();
        RebuildCommand.NotifyCanExecuteChanged();
        BuildCyclesCommand.NotifyCanExecuteChanged();

        RefreshRunSurface(); // [C2] liste yeniden kuruldu → sayaç/görünür-liste tazelensin
    }

    /// <summary>[E2/§5-b] Grafın GEOMETRİSİNİ belirleyen alanların (düğüm Id/Ad/<b>LayerIndex</b>/LayerName + kenarlar)
    /// sıralı imzası. <see cref="ProjectNode.LayerIndex"/> grafın satır/sütun yerleşiminin GERÇEK sürücüsüdür
    /// (<c>GraphBinder.LayerOf = node.LayerIndex ?? topoDepth</c>, <c>GraphLayout</c> Y = TopMargin + Layer*RowHeight,
    /// mutlak satırlar) ve <see cref="ProjectNode.LayerName"/> onun VEKİLİ DEĞİLDİR (D7 katman düzeni LayerName aynıyken
    /// LayerIndex'i kaydırabilir — ör. ortaya boş katman ekleme / Other kovasını iten pattern). İmzaya girmezse
    /// böyle bir kayma kaçırılıp graf bayat satırlarda kalırdı ([E2/FIX2]). Statü alanları (InCycle/WillBuild)
    /// BİLEREK dışarıda — onlar geometri değil renk/rozet, ayrı yoldan (UpdateStatuses) akar; imzaya girseler her
    /// mid-run Sync gereksiz bir tam yeniden-inşa tetiklerdi. LayerIndex aynıyken token da aynıdır → sahte
    /// yeniden-inşa OLMAZ.</summary>
    private static string TopologySignature(IReadOnlyList<ProjectNode> nodes) =>
        string.Join(";", nodes.Select(n =>
            $"{n.Id}|{n.Name}|{n.LayerIndex}|{n.LayerName}|{string.Join(",", n.Dependencies)}"));

    private int IndexOf(string projectId)
    {
        for (int i = 0; i < Projects.Count; i++)
            if (string.Equals(Projects[i].Id, projectId, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>
    /// [A13/T2 · 2.2] Branch envanteri geldi: liste tazelenir ve <see cref="Branch"/> HENÜZ BOŞSA aktif
    /// branch'e SEED edilir.
    ///
    /// <para><b>Neden seed gerekli:</b> <see cref="Branch"/> boş başlar ve ona yazan yalnız iki yol vardır —
    /// kullanıcının popover seçimi (<see cref="SelectBranch"/>) ve diskteki UiState seed'i. <c>syncCompleted</c>
    /// yazmaz ve zaten bir ECHO'dur (App ne gönderdiyse o döner). Dolayısıyla ilk kurulumda branch chip'i
    /// SONSUZA DEK boş kalıyordu.</para>
    ///
    /// <para><b>Neden YALNIZ boşken:</b> seed bir varsayılan doldurmadır, bir kullanıcı kararı DEĞİL. Kullanıcı
    /// aktif-olmayan bir branch seçtiyse (ya da UiState'ten öyle geldiyse), her Sync'in envanteri onu aktif
    /// branch'e geri çekerdi — seçim kaybolur, worktree zorlaması sessizce düşerdi.</para>
    ///
    /// <para><b>Aktif branch yoksa</b> (detached HEAD / boş envanter) hiçbir şey yazılmaz: uydurma değer YOK.</para>
    /// </summary>
    private void OnBranchList(BranchListEvent e)
    {
        Branches.ReplaceAll(e.Branches);
        ReconcileBranchWithInventory();
        // [T2 fix-1 · C1/I-G] Aktif branch DEĞİŞMİŞ olabilir (kullanıcı terminalde `git checkout` yaptı) →
        // IsWorktreeForced/EffectiveUseWorktree TÜRETİLMİŞ değerleri de değişmiştir ama kendi bildirimlerini
        // yayınlamazlar. Bağlı görünümler (açık bir WorktreePopover, ActionBar chip'i) tazelensin diye
        // AÇIKÇA duyurulur.
        OnPropertyChanged(nameof(ActiveBranchName));
        OnPropertyChanged(nameof(IsWorktreeForced));
        OnPropertyChanged(nameof(EffectiveUseWorktree));
    }

    /// <summary>
    /// [T2 fix-1 · C1] Envanter geldi: <see cref="Branch"/>'i gerçekle uzlaştırır.
    ///
    /// <para><b>Kullanıcının AÇIK seçimi korunur</b> — o bir niyettir, bir varsayılan değil. TEK istisna:
    /// seçilen branch envanterde ARTIK YOKSA (silinmiş/yeniden adlandırılmış) seçim düşürülür ve değer aktif
    /// branch'e döner; aksi halde uygulama var olmayan bir branch'i hedeflemeye çalışır ve build zorunlu
    /// worktree yolunda "no commit could be resolved" ile ölürdü.</para>
    ///
    /// <para><b>Açık olmayan her değer TAZELENİR</b> (boş olsun, diskteki bayat <c>UiState</c> seed'i olsun,
    /// önceki bir envanter seed'i olsun) → aktif branch. C1'in tam olarak kapattığı yol budur: kullanıcı
    /// terminalde branch değiştirince bir sonraki Sync uygulamayı kendiliğinden hizaya sokar.</para>
    /// </summary>
    private void ReconcileBranchWithInventory()
    {
        if (ActiveBranchName is not { } active) return; // detached HEAD / boş envanter → uydurma değer YOK

        if (_branchChosenByUser)
        {
            bool stillExists = Branches.Any(b => string.Equals(b.Name, Branch, StringComparison.Ordinal));
            if (stillExists) return;    // açık seçim GEÇERLİ → dokunma
            _branchChosenByUser = false; // seçilen branch yok olmuş → seçim düşer, aşağıda aktife dönülür
        }

        Branch = active;
    }

}
