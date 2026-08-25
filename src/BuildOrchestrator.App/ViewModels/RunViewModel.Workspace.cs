using System.Collections.ObjectModel;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Scheduling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

    /// <summary>Stop istendi (<see cref="AppPhase.Stopping"/>) ya da Sync başladı (<see cref="AppPhase.Syncing"/>):
    /// motor bundan sonra <c>runStopped</c>/<c>runCompleted</c> ya da <c>syncCompleted</c> ile cevap
    /// vermelidir — sessizlik saati burada kurulur (<see cref="OnIsStartingChanged"/> ile aynı gerekçe).
    /// Faz set eden HER yol buradan geçtiği için kurma noktası tek yerdedir.</summary>
    partial void OnPhaseChanged(AppPhase value)
    {
        if (value is AppPhase.Stopping or AppPhase.Syncing) ArmEngineWatchdog();
    }

    /// <summary>[N10] Sync'in çözdüğü hedef commit — remote ulaşılamadıysa yerel HEAD (bkz. <see cref="FetchDegraded"/>).</summary>
    [ObservableProperty] private string? _targetSha;

    /// <summary>true ⇒ son Sync'te fetch başarısız oldu ve akış yerel HEAD ile devam etti (offline degrade).</summary>
    [ObservableProperty] private bool _fetchDegraded;

    /// <summary>
    /// [design v1.16.0 §2.7-6a] Yerel HEAD'in <c>origin/&lt;branch&gt;</c>'ten kaç commit geride olduğu —
    /// alt bardaki <c>N behind</c> chip'inin sayısı. <c>null</c> ⇒ MESAFE BİLİNMİYOR (fetch degrade oldu ya da
    /// aktif olmayan bir branch seçili): chip çizilmez, uydurma sayı gösterilmez.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowBehind))]
    [NotifyCanExecuteChangedFor(nameof(PullRepositoryCommand))]
    private int? _behind;

    /// <summary>
    /// Chip GÖRÜNÜR mü: geride ve mesafe biliniyor <b>ve</b> aktif branch seçili. Worktree modunda chip YOK —
    /// derleme worktree'den yapılıyor, ana ağacı ilerletmenin o koşuya bir etkisi olmazdı.
    /// </summary>
    public bool CanShowBehind => Behind is > 0 && !IsWorktreeForced;

    /// <summary>[Fix wave 1 — Finding 2] <c>syncStarted</c> geldi ama <c>syncCompleted</c> (ya da Sync'i bitiren
    /// bir hata) HENÜZ gelmedi. Bir run-bitiren hata kodunun KAYNAĞINI ayırt etmek için gerekir — bkz.
    /// <see cref="TryConsumeSyncFailure"/>. Faz (<see cref="Phase"/>) bu iş için yeterli DEĞİLDİR: sonraki UI
    /// task'ları koşan bir run'da fazı <c>Running</c>'e çekecek ve Syncing damgası kaybolacaktır.</summary>
    private bool _syncInFlight;

    /// <summary>[Sync guard] Sync İSTENDİ ama motor henüz <c>syncStarted</c> ile cevap vermedi.
    /// <see cref="RunViewModel.SyncAsync"/> bunu GÖNDERİMDEN ÖNCE kurar — <see cref="BeginRunAsync"/>'in
    /// <see cref="IsStarting"/> deseninin birebir simetriği.
    ///
    /// <para><b>Neden <see cref="_syncInFlight"/> yetmiyor:</b> o bayrak yalnız <c>syncStarted</c> ile
    /// kurulur, oysa tıklamanın gönderimi milisaniyeler içinde biter ve motor o sırada HENÜZ Sync'e
    /// başlamamıştır (komut kuyrukta bekler). Aradaki pencerede düğme yeniden etkin görünüyordu: ikinci bir
    /// basış motora ikinci bir TAM analiz (tarama + graf + topo + iki incremental geçiş) kuyruklatıyor,
    /// konsolda aynı transkript iki kez akıyor ve şerit Syncing → Idle → Syncing yapıyordu.</para></summary>
    private bool _syncRequested;

    /// <summary>[Sync guard] Sync yüzeyi MEŞGUL mü: istek uçuşta (<see cref="_syncRequested"/>) YA DA
    /// <c>syncStarted</c> görüldü (<see cref="_syncInFlight"/>). Sync/Rebuild/Build/Cycles kapılarının
    /// TEK predicate'idir — soru dört yerde ayrı ayrı yazılmaz (kopya YASAK).</summary>
    private bool SyncBusy => _syncRequested || _syncInFlight;

    /// <summary>[Sync guard testi] YALNIZ testler için — <see cref="SyncInFlight"/> seam'inin ikizi: istek
    /// penceresinin gözlemlenebilir hali (gönderimden önce kurulur, gönderim senkron düşerse geri açılır).</summary>
    internal bool SyncRequested => _syncRequested;

    /// <summary>[clean guard] <c>cleanStarted</c> geldi ama <c>cleanCompleted</c> (ya da Clean'i bitiren bir
    /// hata) HENÜZ gelmedi. Sync guard'ının birebir simetriğidir ve BİLEREK ayrı bir bayraktır: Clean, Sync
    /// yüzeyine ait değildir (kendi event kanalı, kendi hata kodları) ve ikisi aynı bayrağı paylaşsaydı
    /// birinin bırakılması ötekini de açardı.</summary>
    private bool _cleanInFlight;

    /// <summary>[clean guard] Clean İSTENDİ ama motor henüz <c>cleanStarted</c> ile cevap vermedi —
    /// <see cref="_syncRequested"/>'ın gerekçesi aynen geçerli: gönderim milisaniyeler içinde biter, motor
    /// ise sırası gelince başlar; arada düğme etkin kalırsa ikinci basış ikinci bir silme kuyruklatırdı.</summary>
    private bool _cleanRequested;

    /// <summary>[clean guard] Clean yüzeyi MEŞGUL mü — istek uçuşta YA DA <c>cleanStarted</c> görüldü.
    /// Clean/Sync/Build/Rebuild/Cycles kapılarının TEK predicate'i (kopya YASAK).</summary>
    private bool CleanBusy => _cleanRequested || _cleanInFlight;

    /// <summary>[clean guard testi] YALNIZ testler için — istek ve uçuş pencerelerinin gözlemlenebilir hâli.</summary>
    internal bool CleanRequested => _cleanRequested;
    internal bool CleanInFlight => _cleanInFlight;

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

    /// <summary>[E2/§5-b] Son yayınlanan topolojinin YAPI imzası (düğüm Id/Ad/katman + kenarlar).
    /// <see cref="OnWorkspaceTopology"/> <see cref="TopologyChanged"/>'ı iki durumda ateşler: imza değiştiğinde
    /// ya da yayın bir <b>Sync'e</b> aitse (<see cref="_syncInFlight"/>) — imza aynı olsa bile. <c>null</c> =
    /// henüz hiç topoloji gelmedi → ilk topoloji her zaman ateşler (graf ilk kez kurulur). Statü değişimleri
    /// (InCycle/WillBuild) imzaya GİRMEZ — onlar <c>UpdateStatuses</c> (PushGraphStatuses) yoluyla akar.
    ///
    /// <para><b>Sync = "sıfırdan listelendi" (design v1.13.2 §2.4 · §9).</b> Prototipte <c>doSync()</c>
    /// (<c>BuildApp.jsx:1186-1193</c>) <c>revealKey</c>'i HER Sync'te KOŞULSUZ artırır: graf reveal'ini
    /// yeniden oynar, liste kademeli belirir ve — seçim yokken — başa döner (<c>StickyLayerList.PlayRevealStagger</c>).
    /// <c>TopologyChanged</c> bu yolu sürer (<c>MainWindow.RefreshProjectGroups</c> + <c>RebuildGraph</c>), bu
    /// yüzden bir Sync'in topolojisi imzadan bağımsız ateşler. Bedeli <c>SetGroups</c>'un <c>ItemsSource</c>
    /// ataması (tam reset) — Sync bilinçli bir "yeniden listele" olduğundan bu reset gereksiz churn değil,
    /// tasarımın istediği belirişin kendisidir (A13.2'nin dar okuması: seçim satır VM'lerinde yaşar ve Sync
    /// seçimi zaten düşürür).</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL — v1.13.2, ölçüldü]</b> Guard eskiden HER yayın için imzaya bakıyordu; aynı
    /// repoda ikinci bir Sync ("no changes") <c>TopologyChanged</c> ateşlemiyor, reveal oynamıyor ve liste
    /// başa DÖNMÜYORDU — kullanıcı testinde "Sync'te scroll başa gelmiyor" diye görülen buydu. Gerekçe "gereksiz
    /// churn" idi (mid-run bir Sync koşan grafı yeniden-reveal etmesin); ama Sync koşarken zaten kilitlidir
    /// (<c>SyncCommand</c> CanExecute, <c>ApplySettingsAsync</c>/<c>ChangeRepositoryAsync</c>'in
    /// <c>IsMidRunLocked</c> kapıları) ve motor topolojiyi YALNIZ Sync içinde yayınlar
    /// (<c>SyncWorkspaceService</c>) — guard'ın koruduğu durum ulaşılabilir değildi. İmza karşılaştırması
    /// Sync dışı bir yayın için savunma olarak durur.
    /// Karakterizasyon testi: <c>ProjectListFilterTests.A_no_changes_sync_replays_the_reveal</c> ve
    /// <c>StickyRevealTriggerTests.A_no_changes_sync_returns_the_list_to_the_top</c>.</para></summary>
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
        // [Sync guard] Motor cevap verdi: nöbet istek bayrağından uçuş bayrağına GEÇER. İkisi birden açık
        // bırakılsaydı kapıyı kapatan iki ayrı bayrak olurdu ve biri sızdığında ötekinin temizlenmesi
        // yetmezdi (SyncBusy tek predicate, ama bayrakların ömrü ayrık olmalı).
        _syncRequested = false;
        NotifySyncGatedCommands();
    }

    /// <summary>[Sync guard] <see cref="SyncBusy"/>'nin değiştiği HER geçişte ona bağlı komutları TEK yerden
    /// yeniden sorgulatır: CommunityToolkit'in <c>RelayCommand</c>'ı <c>CommandManager.RequerySuggested</c>'a
    /// ABONE OLMAZ, yani bildirim elle gelmezse düğmeler gerçek pencerede stale (kalıcı pasif/aktif) kalır.
    /// Çağrı yerleri: istek kurulumu/bırakılışı (<see cref="RunViewModel.SyncAsync"/>/
    /// <see cref="ReleaseSyncRequest"/>), <see cref="OnSyncStarted"/>, <see cref="ReleaseSyncPhase"/> ve
    /// <see cref="TryConsumeSyncFailure"/>. Liste burada tek yerde durur — her geçişte tek tek sayılmaz.</summary>
    private void NotifySyncGatedCommands()
    {
        SyncCommand.NotifyCanExecuteChanged();
        BuildCommand.NotifyCanExecuteChanged(); // [DEĞİŞEN KURAL] Build de Sync penceresinde bekler
        RebuildCommand.NotifyCanExecuteChanged();
        BuildCyclesCommand.NotifyCanExecuteChanged();
        BuildProjectCommand.NotifyCanExecuteChanged();   // [tek proje] satır komutları da aynı kapıdadır
        RebuildProjectCommand.NotifyCanExecuteChanged();
        CleanProjectCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged(); // [clean] aynı kapıdan geçer — İKİNCİ bir liste açılmaz
        PullRepositoryCommand.NotifyCanExecuteChanged(); // [v1.16.0] chip de SyncBusy/CleanBusy'ye bağlıdır (CanPullRepository)
    }

    /// <summary>[clean guard] Motor cevap verdi: nöbet istek bayrağından uçuş bayrağına GEÇER. Faz
    /// DEĞİŞMEZ — Clean için yeni bir <see cref="AppPhase"/> AÇILMAZ, anlatı konsol satırlarıyla taşınır
    /// (şerit bu iş boyunca dinlenme fazını göstermeye devam eder; kullanıcının baktığı yer konsoldur).</summary>
    private void OnCleanStarted()
    {
        _cleanInFlight = true;
        _cleanRequested = false;
        NotifySyncGatedCommands();
    }

    /// <summary>[clean guard] Clean bitti — yüzey serbest.</summary>
    private void OnCleanCompleted() => ReleaseCleanSurface();

    /// <summary>[clean guard] Uçuştaki Clean'i serbest bırakır: İKİ bayrak da temizlenir (motor Clean'e HİÇ
    /// başlayamadan ölmüş olabilir, o hâlde uçuş bayrağı hiç kurulmamıştır) ve kapılar tek yerden açılır.
    /// Çağıranlar: <see cref="OnCleanCompleted"/>, <see cref="TryConsumeCleanFailure"/> ve
    /// <see cref="RunViewModel.ReleaseAfterEngineLoss"/> (motor mid-clean ölürse kapı sızmaz).</summary>
    private void ReleaseCleanSurface()
    {
        _cleanInFlight = false;
        _cleanRequested = false;
        NotifySyncGatedCommands();
    }

    /// <summary>[clean guard] İstek penceresini kapatır: gönderim SENKRON düştüğünde (motor hazır değil/ölü)
    /// çağrılır — o yolda hiçbir <c>cleanStarted</c> gelmeyeceği için kapı başka hiçbir yerde açılmazdı.</summary>
    private void ReleaseCleanRequest()
    {
        _cleanRequested = false;
        NotifySyncGatedCommands();
    }

    /// <summary>[clean guard] Dönüş değeri = "bu hata Clean'e aittir, run/Sync state'ine DOKUNMA".
    /// <see cref="TryConsumeSyncFailure"/>'ın BASİT hâlidir: Clean'in kodları
    /// (<see cref="CleanErrorCodes"/>) hiçbir başka yüzeyle paylaşılmaz, bu yüzden kaynak ayırt etmek için
    /// pencere karşılaştırmasına gerek yoktur — kod yeterlidir. Faz dalı da yoktur (Clean faz değiştirmez).
    /// <para>Metin şeride taşınmaz: bu işin anlatısı konsoldadır ve hata satırı oraya zaten düşer
    /// (<see cref="RunViewModel.OnError"/>'ın ilk satırı).</para></summary>
    private bool TryConsumeCleanFailure(string code, string message)
    {
        if (!CleanErrorCodes.Contains(code) || !CleanBusy) return false;
        ReleaseCleanSurface();
        return true;
    }

    /// <summary>[clean guard] Clean'in yayınlayabildiği hata kodları: <c>cleanFailed</c> (beklenmeyen hata /
    /// bozuk kök) ve <c>cleanRejected</c> (Supervisor'da bir koşu uçuşta). Run-bitiren kodlarla KESİŞMEZ.</summary>
    private static readonly HashSet<string> CleanErrorCodes = new(StringComparer.Ordinal) { "cleanFailed", "cleanRejected" };

    /// <summary>[Sync guard] İstek penceresini kapatır: gönderim SENKRON düştüğünde (motor hazır değil/ölü)
    /// çağrılır — o yolda hiçbir <c>syncStarted</c> gelmeyeceği için kapı başka hiçbir yerde açılmazdı.</summary>
    private void ReleaseSyncRequest()
    {
        _syncRequested = false;
        NotifySyncGatedCommands();
    }

    /// <summary>
    /// [design v1.16.0 §3.9] <c>N behind</c> chip'i: ana repoyu uzak ucuna ff-only ilerlet.
    ///
    /// <para><b>Kuralın bilinçli güncellemesi.</b> Araç KENDİLİĞİNDEN asla pull yapmaz — bu komutun tek
    /// tetikleyicisi kullanıcının chip'e basmasıdır. Motor tarafı yalnız fast-forward uygular; kirli ya da
    /// ayrışmış bir ağaç reddedilir ve gerekçe konsola yazılır (satırlar <c>syncProgress</c> olarak akar).</para>
    ///
    /// <para>Konsol TIKLAMA ANINDA TEMİZLENMEZ: kullanıcının kendi tetiklediği işin sonucunu görmesi gerekir
    /// ve pull sonrası otomatik Sync de aynı gerekçeyle geçmişi korur (<c>clearBuffers: false</c>).</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPullRepository))]
    private async Task PullRepositoryAsync()
    {
        CurrentOperation = OperationLabel.Sync;   // ilerletme + ardından gelen Sync tek bir işlemdir
        ArmEngineWatchdog();
        await TrySendAsync(new PullRepositoryCommand(RootPath, Branch), "pullRepository");
    }

    /// <summary>Chip'in tıklanabilirliği: görünür olmasıyla aynı koşullar + bar kilidi (koşu/bakım görevi —
    /// uçuştaki bir Clean de bakım görevidir: başarılı pull'un otomatik Sync'i silinmekte olan bin/obj'i okurdu).</summary>
    private bool CanPullRepository() =>
        CanShowBehind && !IsRunning && !IsStarting && !IsEngineUnavailable && !SyncBusy && !CleanBusy;

    /// <summary>
    /// [design v1.16.0 §3.9] Pull bitti. Başarılıysa chip düşer ve plan yeniden hesaplanır (yeni HEAD'in
    /// kararları); başarısızsa hiçbir şey değişmez — gerekçe zaten konsolda.
    /// </summary>
    private async Task OnPullCompletedAsync(PullCompletedEvent e)
    {
        if (!e.Succeeded) { CurrentOperation = null; return; }

        Behind = 0;                          // ff sonrası yerel HEAD uzak uca eşitlendi
        await SyncCoreAsync(clearBuffers: false);
    }

    /// <summary>[A5/T69] Sync bitti: hedef commit + degrade bayrağı kaydedilir, faz <c>Idle</c>'a geçer
    /// (proje durumları artık bilinir — hollow değil).</summary>
    private void OnSyncCompleted(SyncCompletedEvent e)
    {
        TargetSha = e.TargetSha;
        FetchDegraded = e.FetchDegraded;
        Behind = e.Behind;      // [v1.16.0] chip'in sayısı; null ⇒ mesafe bilinmiyor → chip yok
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
        // [Sync guard] İstek bayrağı da bırakılır: motor Sync'e HİÇ başlayamadan ölmüş olabilir (istek
        // penceresinde), o hâlde uçuş bayrağı hiç kurulmamıştır ve yalnız onu temizlemek kapıyı açmazdı.
        _syncRequested = false;
        if (Phase == AppPhase.Syncing) Phase = RestingPhase;
        // [Fix wave 1, C2 review Finding 1] OnSyncStarted'ın simetriği: hem normal syncCompleted hem
        // engine-ölümü-mid-sync (OnEngineExited) yolu BURADAN geçer — Sync/Rebuild/Cycles'ı tek yerden geri açar.
        NotifySyncGatedCommands();
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
    /// (2) uçuşta bir Sync var (<see cref="SyncBusy"/> — istek penceresi DAHİL: bir Sync <c>syncStarted</c>
    /// yayınlamadan da düşebilir, ve o hâlde hata Sync'e atfedilmezse istek bayrağı sonsuza dek asılı
    /// kalır, yani Sync düğmesi kalıcı pasifleşirdi),
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
        if (!SyncErrorCodes.Contains(code) || !SyncBusy) return false;
        // Faz her iki dalda da bırakılır: hata Sync'e aitse syncCompleted GELMEYECEK (asılı kalırdı); run'a
        // aitse uçuştaki Sync zaten kendi syncCompleted'ıyla fazı tazeleyecek.
        if (Phase == AppPhase.Syncing) Phase = RestingPhase;
        if (IsStarting) return false; // çakışan pencere → run tarafı seçilir (yukarıdaki gerekçe)
        _syncInFlight = false;        // hata Sync'e ait: bu Sync bitti, run state'ine DOKUNULMAZ
        _syncRequested = false;       // [Sync guard] istek penceresinde düşen Sync de kapıyı geri açar
        SyncErrorMessage = message;   // [E2/T10] şerit KIRMIZI "Sync failed — {reason}" gösterir (retry = Sync)
        // [re-review C2, Finding 4] OnSyncStarted'ın simetriği burada da gerekir: bu, Sync yüzeyini serbest
        // bırakan 4. geçiştir (diğer üçü OnSyncStarted/ReleaseSyncPhase'in iki çağrı yeri) — Fix wave 1 bunu
        // kaçırmıştı, butonlar bir sonraki ilgisiz bildirime kadar stale-disabled kalıyordu.
        NotifySyncGatedCommands();
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
                    IsRunLocked = IsMidRunLocked, // [tek proje] kilit + hedef aynı itme deseninden
                    IsRunTarget = string.Equals(node.Id, RunTargetId, StringComparison.OrdinalIgnoreCase),
                    // [Harici projeler] Rozet topolojiden gelir; satır ömrü boyunca değişmez.
                    IsExternal = node.ExternalVcs is not null,
                    // [design v1.11.0 §3.1] Yeni doğan satır BAŞLANGIÇ MODUNDADIR: bir koşu ortasında gelen
                    // topoloji hariç (orada koşan işlem zaten renk yazıyor).
                    Fresh = !IsRunning,
                });
            else
            {
                Projects[existing].SolutionName = solutionName; // topoloji sln atamasını değiştirmiş olabilir
                Projects[existing].InCycle = node.InCycle;       // cycle üyeliği topolojiyle değişmiş olabilir
                if (existing != i) Projects.Move(existing, i);
            }
        }

        // Sync = yeni taban: önceki run'ın sonuçları artık geçmiştir. Sıfırlama bir İŞLEMİN nötrlemesiyle
        // AYNIdir (bkz. NeutralizeRows) — ayrıştıkları tek nokta inilen zemindir.
        // [design v1.11.0 §3.1 · §9-3] BAŞLANGIÇ MODU: Sync ve açılış hiçbir şeyi RENKLENDİRMEZ — hangi
        // işlemin geleceği belli değildir, bu yüzden plan da gösterilmez. Satır kesikli griye, graf node'u
        // kesikli çerçeveye döner; neyin bayat olduğu çift SHA metninden okunur.
        if (!IsRunning) NeutralizeRows(fresh: true);

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
        // [design v1.13.2 §2.4 · §9] Bir Sync'in yayınladığı topoloji imza AYNI olsa da TopologyChanged
        // ateşler: Sync = "sıfırdan listelendi" (prototipte revealKey her Sync'te artar) — graf reveal'ini
        // yeniden oynar, liste kademeli belirir ve başa döner. İmza guard'ı yalnız Sync DIŞI yayınlar için
        // kalır (bkz. _lastTopologySignature).
        string signature = TopologySignature(e.Nodes);
        if (signature != _lastTopologySignature || _syncInFlight)
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
        BuildProjectCommand.NotifyCanExecuteChanged();   // [tek proje] satır komutları da topoloji kapısındadır
        RebuildProjectCommand.NotifyCanExecuteChanged();
        CleanProjectCommand.NotifyCanExecuteChanged();

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
        NotifyBehindChip();   // [v1.16.0] chip worktree modunda çizilmez — o karar da buradan tazelenir
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
