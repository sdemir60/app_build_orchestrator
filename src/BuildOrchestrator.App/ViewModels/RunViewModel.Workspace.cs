using System.Collections.ObjectModel;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.Scheduling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [A5/T69 · Fix wave 1 — Finding 6] <see cref="RunViewModel"/>'in <b>workspace yüzeyi</b>: Sync fazı, hedef
/// commit, branch envanteri ve topoloji. Ayrı bir partial dosyada, çünkü ana dosya run/log/ETA
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
    /// alt bardaki <c>N behind</c> chip'inin sayısı. <c>null</c> ⇒ MESAFE BİLİNMİYOR (fetch degrade oldu): chip
    /// çizilmez, uydurma sayı gösterilmez.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowBehind))]
    [NotifyCanExecuteChangedFor(nameof(PullRepositoryCommand))]
    private int? _behind;

    /// <summary>
    /// Chip GÖRÜNÜR mü: mesafe biliniyor ve sıfırdan büyük. [spec 2026-09-18 §6.5] Koşu her zaman çalışma
    /// ağacında derlendiği için chip, ağaç geride olduğu HER an görünür.
    /// </summary>
    public bool CanShowBehind => Behind is > 0;

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
    /// TEK predicate'idir — soru dört yerde ayrı ayrı yazılmaz (kopya YASAK).
    /// <para>Aksiyon barı da bunu okur (Sync düğmesi amber zemin + spinner olur), bu yüzden
    /// <see cref="CleanBusy"/> gibi BİLDİRİMLİDİR: değeri değiştiren her yol
    /// <see cref="NotifySyncGatedCommands"/>'dan geçer ve bildirim oradan atılır. İstek penceresi dahildir —
    /// gösterge tıklamada başlar, motorun cevabını beklemez.</para></summary>
    public bool SyncBusy => _syncRequested || _syncInFlight;

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
    /// Clean/Sync/Build/Rebuild/Cycles kapılarının TEK predicate'i (kopya YASAK).
    /// <para>Bakım kutusu da bunu okur (koşan düğme amber zemin + spinner olur), bu yüzden BİLDİRİMLİDİR:
    /// değeri değiştiren her yol <see cref="NotifySyncGatedCommands"/>'dan geçer ve bildirim oradan atılır.
    /// <b>İstek penceresi dahildir</b> — kullanıcı tıkladığı anda geri bildirim görmelidir, motorun cevabını
    /// beklemez; gönderim düşerse bayrak da düşer ve spinner kaybolur.</para></summary>
    public bool CleanBusy => _cleanRequested || _cleanInFlight;

    /// <summary>[clean guard testi] YALNIZ testler için — istek ve uçuş pencerelerinin gözlemlenebilir hâli.</summary>
    internal bool CleanRequested => _cleanRequested;
    internal bool CleanInFlight => _cleanInFlight;

    /// <summary>[clean] Tıklama anının saati — adımın EN AZ <see cref="RunViewModel.MaintenanceMinStepMs"/> görünmesi
    /// bu andan ölçülür. Kaynak enjekte edilen monoton saattir (D8: testte deterministik).</summary>
    private long _cleanStartedAtMs;

    /// <summary>[optimize guard] <c>optimizeStarted</c> görüldü, <c>optimizeCompleted</c> beklenıyor —
    /// <see cref="_cleanInFlight"/>'ın ikizi.</summary>
    private bool _optimizeInFlight;

    /// <summary>[optimize guard] Optimize İSTENDİ ama motor henüz cevap vermedi — istek penceresi kapalı
    /// tutulmazsa ikinci basış ikinci bir onarım kuyruklatırdı.</summary>
    private bool _optimizeRequested;

    /// <summary>[optimize guard] Optimize yüzeyi MEŞGUL mü — <see cref="CleanBusy"/>'nin birebir ikizi ve aynı
    /// sebeple PUBLIC + BİLDİRİMLİDİR: bakım kutusu koşan düğmeyi bundan boyar (amber zemin + spinner) ve
    /// değeri değiştiren her yol <see cref="NotifySyncGatedCommands"/>'dan geçer. İstek penceresi dahildir.</summary>
    public bool OptimizeBusy => _optimizeRequested || _optimizeInFlight;

    /// <summary>[optimize guard testi] YALNIZ testler için — istek ve uçuş pencerelerinin gözlemlenebilir hâli.</summary>
    internal bool OptimizeRequested => _optimizeRequested;
    internal bool OptimizeInFlight => _optimizeInFlight;

    /// <summary>[optimize] Tıklama anının saati — <see cref="_cleanStartedAtMs"/>'in ikizi: adımın EN AZ
    /// <see cref="RunViewModel.MaintenanceMinStepMs"/> görünmesi bu andan ölçülür.</summary>
    private long _optimizeStartedAtMs;

    /// <summary>Branch envanteri. <see cref="SnapshotCollection{T}"/>: yayın başına EN ÇOK bir bildirim, içerik
    /// değişmemişse HİÇ — gerekçesi (ölçülen O(n²) donma) o tipin özetindedir.</summary>
    public SnapshotCollection<BranchRef> Branches { get; } = [];

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
    /// <see cref="OnWorkspaceTopology"/> <see cref="TopologyChanged"/>'ı (→ graf reveal'i, liste kademeli beliriş
    /// ve — seçim yokken — başa dönüş) YALNIZ imza değiştiğinde ateşler. <c>null</c> = henüz hiç topoloji gelmedi
    /// ya da plan yüzeyi boşaltıldı (<see cref="ClearPlanSurface"/>) → sıradaki topoloji her zaman ateşler. Statü
    /// değişimleri (InCycle/WillBuild) imzaya GİRMEZ — onlar <c>UpdateStatuses</c> (PushGraphStatuses) yoluyla akar.
    ///
    /// <para><b>[DEĞİŞEN KURAL — spec 2026-09-18 §1-13]</b> Eskiden (design v1.13.2 §2.4 · §9) bir Sync'in
    /// yayını imzadan bağımsız ateşlerdi (<c>|| _syncInFlight</c>): prototipin <c>doSync()</c>'u
    /// <c>revealKey</c>'i her Sync'te artırır, "Sync = sıfırdan listelendi". Artık Sync kendiliğinden de koşar
    /// (commit, pencereye dönüş) ve her biri listeyi baştan kurup kullanıcının baktığı yeri başa sarıyordu. Yapı
    /// aynıysa satırlar yerinde tazelenir; proje eklendi/çıktıysa reveal oynar. Clean/Optimize tıklaması listeyi
    /// boşaltır ve imzayı da unutturur — zincirlenen Sync aynı yapıyı getirse de reveal oynar.
    /// Testler: <c>ProjectListFilterTests.A_sync_with_the_same_structure_updates_in_place</c> /
    /// <c>A_sync_that_adds_a_project_replays_the_reveal</c> / <c>A_clean_empties_the_list_and_its_sync_replays_the_reveal</c>,
    /// <c>StickyRevealTriggerTests.A_sync_with_the_same_structure_updates_in_place</c>.</para></summary>
    private string? _lastTopologySignature;

    /// <summary>[A5/T69] Sync başladı: faz <c>Syncing</c>'e geçer ve akış "uçuşta" işaretlenir.
    /// <para>[Fix wave 1, C2 review Finding 1] <see cref="RunViewModel.RebuildCommand"/>
    /// artık <c>_syncInFlight</c>'a da bakıyor (<see cref="RunViewModel.CanRebuildOrRetry"/>) — bu geçişte CanExecuteChanged
    /// elle tetiklenmezse [NotifyCanExecuteChangedFor] zinciri (yalnız IsRunning/IsStarting'e bağlı) bu iki
    /// butonun gerçek pencerede Sync başlar başlamaz disabled görünmesini SAĞLAMAZ.</para>
    /// <para>[D2 review fix, Finding 1] Önizleme kümeleri BURADA temizlenir (<c>ClearPreviewSets</c>): küme ADD-ONLY
    /// olduğundan (yalnız <see cref="RunViewModel.OnBuildPreview"/> ekler) ve önceki Clear noktası yalnız
    /// <see cref="RunViewModel.OnRunStarted"/> olduğundan, ikinci (run'sız) bir Sync kendi <c>BuildPreviewEvent</c>'ini
    /// (A5 amendment — her Sync bunu yayınlar) hiç temizlik olmadan üzerine yazardı: dirty=false gelen satırlar
    /// kümeden ÇIKARILMAZ (yalnız true eklenir), bu yüzden önceki Sync'in willBuild'i BİRİKİRDİ ve Idle şeridi
    /// bayat "N to build" gösterirdi.</para></summary>
    private void OnSyncStarted()
    {
        _syncInFlight = true;
        // [spec 2026-09-18 §6.2 · review I1/I2] Sessiz Sync ekranda bir işlem olarak GÖRÜNMEZ: faz Syncing'e
        // geçmez (şerit Sync ilerlemesi göstermez, önceki işlemin pill'i canlanmaz) ve kötü haber silinmez —
        // önceki koşunun "Run failed — …" metni ve Sync hatası durur (Sync hatası başarıyla bitince kalkar).
        if (_syncMode.IsVisible())
        {
            SyncErrorMessage = null; // [E2/T10] retry başladı — önceki Sync hatası şeritten kalkar
            // [runFailed] Önceki run'ın hata gerekçesi de kalkar: aksi halde KIRMIZI "Run failed — …" satırı, Sync
            // ilerlemesini ("▸ Sync — git fetch origin…") kullanıcı yeni bir run başlatana kadar gizlerdi.
            RunErrorMessage = null;
            Phase = AppPhase.Syncing;
        }
        ClearPreviewSets(); // [D2 review fix] her Sync başında taze — hemen ardından gelen BuildPreviewEvent yeniden doldurur
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
        OptimizeCommand.NotifyCanExecuteChanged(); // [optimize] aynı kapı, aynı liste
        PullRepositoryCommand.NotifyCanExecuteChanged(); // [v1.16.0] chip de SyncBusy/CleanBusy'ye bağlıdır (CanPullRepository)
        // [clean] Bakım kutusunun spinner'ı bir KOMUT değil bir DURUM okur. Bildirim buraya düşer çünkü
        // CleanBusy'yi değiştiren dört yolun (istek, cleanStarted, bırakma, istek iptali) hepsi zaten bu
        // metottan geçer — dört ayrı çağrı yazmak kopya olurdu. Sync'in meşgul yüzeyi (aksiyon barındaki
        // düğmenin amber zemin + spinner'ı) AYNI gerekçeyle aynı yerden duyurulur.
        OnPropertyChanged(nameof(CleanBusy));
        OnPropertyChanged(nameof(OptimizeBusy));
        OnPropertyChanged(nameof(SyncBusy));
        // [spec 2026-09-18 §6.3] Branch chip'inin kapısı da bu üç meşgul yüzeyi okur — aynı geçişte duyurulur.
        OnPropertyChanged(nameof(CanSwitchBranch));
        // [spec 2026-09-18 §6.1] Meşgulken bekletilen kendiliğinden Sync tetiği aynı geçişte yeniden sorulur.
        NotifyAutoSyncGate();
    }

    /// <summary>[clean guard] Motor cevap verdi: nöbet istek bayrağından uçuş bayrağına GEÇER. Faz
    /// DEĞİŞMEZ — Clean için yeni bir <see cref="AppPhase"/> AÇILMAZ, anlatı konsol satırlarıyla taşınır
    /// (şerit bu iş boyunca dinlenme fazını göstermeye devam eder; kullanıcının baktığı yer konsoldur).
    ///
    /// <para>Ekranı boşaltan yer burası DEĞİLDİR: liste, graf ve konsol TIKLAMA ANINDA temizlenir
    /// (<c>RunViewModel.CleanAsync</c> → <c>ClearPlanSurface</c>) — kullanıcı kararı, gerekçe orada.</para></summary>
    private void OnCleanStarted()
    {
        _cleanInFlight = true;
        _cleanRequested = false;
        NotifySyncGatedCommands();
    }

    /// <summary>
    /// [clean guard] Clean bitti: yüzey serbest bırakılır, sonra <b>konsol KORUNARAK bir Sync koşar</b>.
    ///
    /// <para>Otomatik Sync'in gerekçesi ekranın doğruyu söylemesidir, motorun ihtiyacı değil: bir sonraki
    /// <c>Build</c> zaten sıfırdan planlar (defter boş → her şey <c>NeverBuilt</c>). Ama
    /// <see cref="OnCleanStarted"/> satırların kararlarını düşürmüştür ve onları geri getirecek tek yer
    /// motorun kendi analizidir — kullanıcıya elle Sync bastırmak, uygulamanın zaten yapabildiği bir işi ona
    /// yüklemek olurdu. <see cref="SyncMode.Appended"/>: kullanıcı kendi tetiklediği Clean'in transkriptini görmeye
    /// devam etmeli (<see cref="OnPullCompletedAsync"/> ile AYNI gerekçe ve AYNI desen).</para>
    ///
    /// <para>Sıra: ÖNCE bırakma. <c>CleanBusy</c> açıkken Sync'in kapısı kapalıdır
    /// (<c>RunViewModel.CanSync</c>), yani ters sıra kendi zincirini bloklardı.</para>
    ///
    /// <para><b>Hata yolunda zincir YOKTUR</b> (<see cref="TryConsumeCleanFailure"/>): başarısız bir işin
    /// arkasına Sync takmak ikinci bir hata satırı üretirdi. Satırlar hollow kalır, Sync kullanıcıya kalır.</para>
    /// </summary>
    private Task OnCleanCompletedAsync() => HandOverToSyncAsync(_cleanStartedAtMs, ReleaseCleanSurface);

    /// <summary>
    /// [clean/optimize] Bakım işinden Sync'e DEVİR — iki bakım işi aynı diziyi oynar (kopya YASAK): adımın
    /// kalanı, iki işlem arasındaki boşluk, Sync kapıyı devralır, EN SON bakım yüzeyi bırakılır.
    /// </summary>
    /// <param name="startedAtMs">Bakım işinin tıklandığı an — adımın görünür süresi buradan ölçülür.</param>
    /// <param name="releaseSurface">İşin kendi yüzeyini bırakan metot (Clean ya da Optimize).</param>
    private async Task HandOverToSyncAsync(long startedAtMs, Action releaseSurface)
    {
        // [kullanıcı kararı 2026-09-12] Adım kalanını oynat: spinner DÖNMEYE DEVAM eder, çünkü kapı henüz
        // bırakılmadı. Küçük bir workspace'te iş milisaniyeler sürüyor ve adım hiç görünmüyordu.
        await HoldAsync(MaintenanceMinStepMs - (_nowMs() - startedAtMs));
        // Ardından iki işlem arasındaki hafif boşluk — ama kapı KAPALI kalır. Ardı ardına iki animasyon dizisi
        // tek bulanıklığa dönüşmesin diye beklenir, yoksa düğmeleri canlandırmak için değil.
        await HoldAsync(MaintenanceStepGapMs);
        // [ölçülen kusur] Yüzey burada, Sync kapıyı DEVRALDIKTAN SONRA bırakılır. Önce bırakılıyordu ve o
        // pencerede Sync/Clean tıklanabilir haldeydi, düğmeler de sönük → canlı → sönük kırpışıyordu; iki
        // işlem tek bir meşgul pencere olarak okunmalıdır. Devralma SENKRONDUR: SyncCoreAsync ilk await'ine
        // varmadan `_syncRequested`'ı kurar, yani Task'ı beklemeden başlatmak kapıyı kesintisiz tutar.
        // Gönderim senkron düşerse Sync kendi kapısını zaten bırakır ve aşağıdaki bırakma doğru sonucu verir.
        var sync = SyncCoreAsync(SyncMode.Appended);
        releaseSurface();
        await sync;
    }

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

    /// <summary>[optimize guard] Motor cevap verdi: nöbet istek bayrağından uçuş bayrağına GEÇER. Clean gibi
    /// Optimize de yeni bir <see cref="AppPhase"/> AÇMAZ — anlatı konsol satırlarıyla taşınır.</summary>
    private void OnOptimizeStarted()
    {
        _optimizeRequested = false;
        _optimizeInFlight = true;
        NotifySyncGatedCommands();
    }

    /// <summary>
    /// [optimize] Onarım bitti: Clean ile AYNI devir — adım tutulur, sonra <b>konsol KORUNARAK bir Sync
    /// koşar</b> (<see cref="HandOverToSyncAsync"/>). Liste tıklamada boşaltıldığı için onu geri getiren bu
    /// Sync'tir. Hata yolunda zincir YOKTUR (<see cref="TryConsumeOptimizeFailure"/>).
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-14]</b> Eskiden burada yalnız yüzey bırakılırdı ve
    /// gerekçe "Optimize hiçbir projeyi dirty yapmaz, yenilenecek karar yok" idi. Kullanıcı iki bakım işinin
    /// aynı akışı izlemesini istedi: temizle → Sync'i çalıştır → düğmelerde yükleme.</para>
    /// </summary>
    private Task OnOptimizeCompletedAsync() => HandOverToSyncAsync(_optimizeStartedAtMs, ReleaseOptimizeSurface);

    /// <summary>[optimize guard] Uçuştaki Optimize'ı serbest bırakır: İKİ bayrak da temizlenir (motor işe HİÇ
    /// başlayamadan ölmüş olabilir) ve kapılar tek yerden açılır. Çağıranlar: <see cref="OnOptimizeCompletedAsync"/>,
    /// <see cref="TryConsumeOptimizeFailure"/> ve <see cref="RunViewModel.ReleaseAfterEngineLoss"/>.</summary>
    private void ReleaseOptimizeSurface()
    {
        _optimizeInFlight = false;
        _optimizeRequested = false;
        NotifySyncGatedCommands();
    }

    /// <summary>[optimize guard] İstek penceresini kapatır: gönderim SENKRON düştüğünde çağrılır — o yolda
    /// hiçbir <c>optimizeStarted</c> gelmeyeceği için kapı başka hiçbir yerde açılmazdı.</summary>
    private void ReleaseOptimizeRequest()
    {
        _optimizeRequested = false;
        NotifySyncGatedCommands();
    }

    /// <summary>[optimize guard] Dönüş değeri = "bu hata Optimize'a aittir, run/Sync state'ine DOKUNMA".
    /// <see cref="TryConsumeCleanFailure"/>'ın birebir ikizi; özellikle <c>optimizeRejected</c> KOŞAN bir
    /// run'ın ortasında gelebilir (kullanıcı run başlarken Optimize'a bastı) ve o run'ı YIKMAMALIDIR.</summary>
    private bool TryConsumeOptimizeFailure(string code, string message)
    {
        if (!OptimizeErrorCodes.Contains(code) || !OptimizeBusy) return false;
        ReleaseOptimizeSurface();
        return true;
    }

    /// <summary>[optimize guard] Optimize'ın yayınlayabildiği hata kodları. Run-bitiren kodlarla KESİŞMEZ.</summary>
    private static readonly HashSet<string> OptimizeErrorCodes = new(StringComparer.Ordinal) { "optimizeFailed", "optimizeRejected" };

    /// <summary>[Sync guard] İstek penceresini kapatır: gönderim SENKRON düştüğünde (motor hazır değil/ölü)
    /// çağrılır — o yolda hiçbir <c>syncStarted</c> gelmeyeceği için kapı başka hiçbir yerde açılmazdı.</summary>
    private void ReleaseSyncRequest()
    {
        _syncRequested = false;
        EndSyncMode(); // [review M1] hiçbir Sync başlamadı — kip sonraki satırları yanlış süzmesin
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
    /// ve pull sonrası otomatik Sync de aynı gerekçeyle geçmişi korur (<see cref="SyncMode.Appended"/>).</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPullRepository))]
    private async Task PullRepositoryAsync()
    {
        CurrentOperation = OperationLabel.Sync;   // ilerletme + ardından gelen Sync tek bir işlemdir
        ArmEngineWatchdog();
        // Gönderim SENKRON düştüyse hiçbir pullCompleted gelmez — pill asılı kalmasın.
        if (!await TrySendAsync(new PullRepositoryCommand(RootPath, Branch), "pullRepository")) CurrentOperation = null;
    }

    /// <summary>Chip'in tıklanabilirliği: görünür olmasıyla aynı koşullar + bar kilidi (koşu/bakım görevi —
    /// uçuştaki bir Clean de bakım görevidir: başarılı pull'un otomatik Sync'i silinmekte olan bin/obj'i okurdu).</summary>
    private bool CanPullRepository() =>
        CanShowBehind && !IsRunning && !IsStarting && !IsEngineUnavailable && !WorkspaceBusy;

    /// <summary>
    /// [design v1.16.0 §3.9] Pull bitti. Başarılıysa chip düşer ve plan yeniden hesaplanır (yeni HEAD'in
    /// kararları); başarısızsa hiçbir şey değişmez — gerekçe zaten konsolda.
    /// </summary>
    private async Task OnPullCompletedAsync(PullCompletedEvent e)
    {
        if (!e.Succeeded) { CurrentOperation = null; return; }

        Behind = 0;                          // ff sonrası yerel HEAD uzak uca eşitlendi
        await SyncCoreAsync(SyncMode.Appended); // pull'un satırları kalır, Sync altına eklenir
    }

    /// <summary>[spec 2026-09-18 §6.3] Bir checkout motora GÖNDERİLDİ ama cevabı (<see cref="CheckoutCompletedEvent"/>
    /// ya da <see cref="CheckoutErrorCodes"/>'tan bir hata) HENÜZ gelmedi. Supervisor checkout boyunca komut
    /// döngüsünü bloklar ve başlangıç olayı yayınlamaz, bu yüzden istek ve uçuş penceresi TEK bayraktır.</summary>
    public bool CheckoutBusy { get; private set; }

    /// <summary>Workspace'e dokunan bir iş uçuşta mı: Sync, Clean, Optimize ya da checkout. Run/Sync/Clean/
    /// Optimize/Pull ve branch chip kapılarının ORTAK meşguliyet sorusu — liste TEK yerde durur (kopya YASAK).
    /// <para>[spec 2026-09-18 §6.3] Checkout da dahildir: Supervisor checkout boyunca komut döngüsünü bloklar;
    /// o sırada basılan bir Build yeni ağaçta başlar ve checkout cevabının temizliği onun konsolunu siler, bir
    /// Pull ise yanlış branch'i ilerletir.</para></summary>
    private bool WorkspaceBusy => SyncBusy || CleanBusy || OptimizeBusy || CheckoutBusy;

    /// <summary>[spec 2026-09-18 §6.3] <see cref="CheckoutBusy"/>'nin TEK yazıcısı: değer değiştiğinde chip'in
    /// kapısını (<see cref="CanSwitchBranch"/>) duyurur. Çağıranlar: gönderim (<see cref="SelectBranch"/>),
    /// cevap (<see cref="OnCheckoutCompletedAsync"/>, <see cref="TryConsumeCheckoutFailure"/>) ve motor kaybı
    /// (<see cref="RunViewModel.ReleaseAfterEngineLoss"/> — motor checkout ortasında ölürse kilit sızmaz).</summary>
    private void SetCheckoutBusy(bool busy)
    {
        if (CheckoutBusy == busy) return;
        CheckoutBusy = busy;
        OnPropertyChanged(nameof(CheckoutBusy));
        NotifySyncGatedCommands(); // run/Sync/Clean/Optimize/Pull kapıları + chip (WorkspaceBusy) tek listeden
    }

    /// <summary>
    /// [spec 2026-09-18 §6.2 · §6.3] Checkout'un cevabı — konsol satırlarını App kurar, metinler
    /// <see cref="PlanProgressLines"/>'tan (tek kaynak).
    ///
    /// <para><b>Başarı yeni bir BÖLÜM açar:</b> konsol ve olay akışı ÖNCE temizlenir, stash satırı (varsa) ve
    /// switch satırı SONRA yazılır — yeni bölümün ilk satırları onlardır ("temizlik önce, not sonra"). Üçünü de
    /// <see cref="SyncMode.BranchChange"/> Sync'i yapar (satırlar <c>sectionLines</c> ile gider): temizlik tek
    /// yerde kalır, ikinci bir temizlik bu satırları silemez; ardından fetch'siz transkript akar.</para>
    ///
    /// <para><b>Başarısız işlem bölüm açmaz:</b> kirli ağaç reddi ve stash/checkout hatası konsolu temizlemez,
    /// uyarı altına eklenir ve işlem biter. Checkout stash'ten SONRA düştüyse stash satırı yine yazılır —
    /// kullanıcının değişiklikleri stash'tedir ve konsol bunu söylemezse kaybolmuş gibi görünürdü.
    /// <see cref="CheckoutStatus.AlreadyOn"/> hiçbir şey yazmaz (App aktif branch'e zaten komut göndermez; bu
    /// yalnız envanterin bayat olduğu yarışta gelir).</para>
    /// </summary>
    private async Task OnCheckoutCompletedAsync(CheckoutCompletedEvent e)
    {
        if (e.Status != CheckoutStatus.Switched)
        {
            SetCheckoutBusy(false);
            CurrentOperation = null;
            if (e.Status == CheckoutStatus.Dirty) AppendRunLine(PlanProgressLines.SwitchRefusedDirty(e.DirtyCount));
            if (e.Status == CheckoutStatus.Failed && e.StashMessage is { } kept)
                AppendRunLine(PlanProgressLines.StashedBeforeSwitch(kept));
            if (e.Status is CheckoutStatus.Failed or CheckoutStatus.StashFailed)
                AppendRunLine(PlanProgressLines.SwitchFailed(e.Detail ?? "unknown error"));
            return;
        }

        // [spec 2026-09-18 §6.2] Yeni bölüm: temizliği ve bölümün ilk satırlarını (stash, switch) BranchChange
        // Sync'i yapar — temizlik önce, not sonra, tek yerde; ardından fetch'siz transkript.
        List<string> section = [];
        if (e.StashMessage is { } stashed) section.Add(PlanProgressLines.StashedBeforeSwitch(stashed));
        section.Add(PlanProgressLines.SwitchedBranch(e.FromBranch ?? "HEAD", e.Branch ?? "HEAD", ShortSha(e.Revision)));
        // Kilit, Sync kapıyı DEVRALDIKTAN SONRA bırakılır (HandOverToSyncAsync'in sırası): SyncCoreAsync ilk
        // await'inden önce `_syncRequested`'ı kurar, yani arada Build/Sync düğmeleri bir kare bile açılmaz.
        var sync = SyncCoreAsync(SyncMode.BranchChange, sectionLines: section);
        SetCheckoutBusy(false);
        await sync;
    }

    /// <summary>[spec 2026-09-18 §6.3] Dönüş değeri = "bu hata checkout'a aittir, run/Sync state'ine DOKUNMA".
    /// <see cref="TryConsumeCleanFailure"/>'ın ikizi: motorun reddi (koşu uçuşta) reddedilen bir checkout gibi
    /// ele alınır — hata satırı <see cref="RunViewModel.OnError"/>'ın ilk satırı olarak konsolun ALTINA düşer
    /// (temizlik yok), kilit açılır, işlem pill'i düşer. Reddetme KOŞAN bir run'ın ortasında gelebilir ve o
    /// run'ı YIKMAMALIDIR.</summary>
    private bool TryConsumeCheckoutFailure(string code)
    {
        if (!CheckoutErrorCodes.Contains(code) || !CheckoutBusy) return false;
        SetCheckoutBusy(false);
        CurrentOperation = null;
        return true;
    }

    /// <summary>[spec 2026-09-18 §6.3] Checkout'un yayınlayabildiği hata kodları: <c>checkoutRejected</c>
    /// (Supervisor'da bir koşu uçuşta) ve <c>checkoutFailed</c> (beklenmeyen hata). Run-bitiren kodlarla KESİŞMEZ.</summary>
    private static readonly HashSet<string> CheckoutErrorCodes = new(StringComparer.Ordinal) { "checkoutFailed", "checkoutRejected" };

    // ---------------------------------------------------------------- [spec 2026-09-18 §6.2] Sync kipleri

    /// <summary>Uçuştaki (ya da en son istenen) Sync'in kipi — <see cref="OnSyncProgress"/> ve
    /// <see cref="OnSyncCompleted"/> okur. Sync bitince (ya da düşünce) <see cref="SyncMode.Manual"/>'a döner:
    /// kipten bağımsız gelen bir <c>syncProgress</c> satırı sessizce yutulmasın.</summary>
    private SyncMode _syncMode = SyncMode.Manual;

    /// <summary>Sessiz Sync'in nedeni — bitişteki tek akış satırını seçer.</summary>
    private SilentSyncReason _silentReason;

    /// <summary>Sessiz Sync'in başındaki karar anlık görüntüsü (satır Id → karar anahtarı) ve o anki saat — bitişte
    /// aynı saatle alınan ikinci görüntüyle karşılaştırılır (<see cref="CountChangedDecisions"/>). Saat SABİT
    /// tutulur: etiketin yaş kuyruğu ("2h") iki görüntü arasında akmasın.</summary>
    private (Dictionary<string, string> Keys, DateTimeOffset Now)? _silentBaseline;

    /// <summary>Tamamlanan Sync'in olay akışı satırı — <see cref="OnSyncCompleted"/> kipe göre yazar,
    /// <see cref="AppendStreamFor"/> okur (<c>null</c> ⇒ satır yok: sessiz Sync'te değişen bir şey yoktu).</summary>
    private string? _syncStreamLine;

    /// <summary>[spec 2026-09-18 §6.1] Son tamamlanan Sync'in ölçtüğü HEAD: checkout edilmiş branch + yerel HEAD
    /// commit'i (<see cref="SyncCompletedEvent.ActiveBranch"/>/<see cref="SyncCompletedEvent.HeadSha"/>). Çift
    /// Sync kontrolü (Task 7 — HEAD izleyicisi) bunu okur: tetik anındaki HEAD bununla aynıysa Sync atlanır.</summary>
    internal (string? Branch, string? HeadSha)? LastSyncHead { get; private set; }

    /// <summary>Son Sync isteğinin motora GİTTİĞİ an (monoton ms, <see cref="_nowMs"/>) — kipten bağımsız, yalnız
    /// başarılı gönderimde yazılır (düşen gönderimde hiçbir Sync başlamadı).</summary>
    internal long? LastSyncStartedAtMs { get; private set; }

    /// <summary>Son Sync'in TAMAMLANDIĞI an (monoton ms). Pencereye dönüşün 5 s eşiği (spec §1-11) ikisinden
    /// yenisini okur (<see cref="LastSyncAtMs"/>).</summary>
    internal long? LastSyncCompletedAtMs { get; private set; }

    /// <summary>Son Sync'e ait en yeni an — başlangıç ya da tamamlanma, hangisi yeniyse; hiç Sync yoksa <c>null</c>.</summary>
    internal long? LastSyncAtMs =>
        LastSyncStartedAtMs is { } started && LastSyncCompletedAtMs is { } completed
            ? Math.Max(started, completed)
            : LastSyncStartedAtMs ?? LastSyncCompletedAtMs;

    /// <summary>
    /// [spec 2026-09-18 §6.2] <b>Kendiliğinden Sync'in TEK girişi</b> — commit, pencereye dönüş ve HEAD'in branch
    /// değişimi dışındaki hareketi buradan Sync'ler (<see cref="SyncMode.Silent"/>): konsol ve akış temizlenmez,
    /// fetch yapılmaz, kalıcı işlem pill'i yazılmaz, seçim korunur; transkript gizlenir ama warn/error satırları
    /// yazılır; bitişte akışa tek satır (<paramref name="reason"/>'a göre, metinler <see cref="StreamText"/>'te).
    /// <para>Kapı Sync düğmesininkiyle AYNIdır (<see cref="CanSync"/>) + bir workspace: koşu, planlama, başka bir
    /// workspace işi (Sync/Clean/Optimize/checkout) uçuştayken ya da motor erişilemezken hiçbir şey gönderilmez ve
    /// <c>false</c> döner — çağıran (izleyici) tetiği sonra yeniden deneyebilir.</para>
    /// </summary>
    /// <returns>Sync istendi mi.</returns>
    internal async Task<bool> SyncSilentlyAsync(SilentSyncReason reason)
    {
        if (!HasWorkspace || !CanSync()) return false;
        await SyncCoreAsync(SyncMode.Silent, reason);
        return true;
    }

    /// <summary>Bir Sync istenirken kipini kurar (<see cref="SyncCoreAsync"/>): sessiz kipte karar anlık görüntüsü
    /// ALINIR (istek anı — motorun cevabından önceki ekran), başlangıç saati her kipte yazılır.</summary>
    private void BeginSyncMode(SyncMode mode, SilentSyncReason reason)
    {
        _syncMode = mode;
        _silentReason = reason;
        _silentBaseline = null;
        if (mode == SyncMode.Silent)
        {
            var now = WallClock();
            _silentBaseline = (DecisionKeys(now), now);
        }
    }

    /// <summary>Uçuştaki Sync'in kipini bırakır — tamamlanma, Sync'e ait hata ve motor kaybı yolları.</summary>
    private void EndSyncMode()
    {
        _syncMode = SyncMode.Manual;
        _silentBaseline = null;
    }

    /// <summary>[spec 2026-09-18 §6.2] Sync transkripti satırı. Sessiz kipte yalnız sorun satırları (warn/error)
    /// konsola yazılır — sessizlik kötü haberi gizlemez; dim/info/cmd satırları yazılmaz.</summary>
    private void OnSyncProgress(SyncProgressEvent e)
    {
        if (!_syncMode.ShowsTranscript() && e.Level is not ("warn" or "error")) return;
        AppendRunLine(e.Line);
    }

    /// <summary>Satır başına karar anahtarı: çıktı durumu (<see cref="ProjectRowViewModel.Standing"/>) + karar
    /// etiketi (<see cref="DecisionLabel"/>, satırın gördüğü AYNI sözcük ve kuyruk). "Değişti" = bu anahtar farklı.</summary>
    private Dictionary<string, string> DecisionKeys(DateTimeOffset now)
    {
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Projects)
        {
            var d = DecisionLabel.For(row.WillBuild, row.WillBuildReason, row.OwnFilesChanged, row.LastBuiltAt,
                row.FailedAt, row.LocalEdits, now, row.InCycle);
            keys[row.Id] = $"{row.Standing}|{d.Word}|{d.Tail}";
        }
        return keys;
    }

    /// <summary>Sessiz Sync'in başındaki görüntüyle bugünkü arasında kararı değişen satır sayısı — eklenen ve
    /// çıkan satırlar da değişmiş sayılır.</summary>
    private int CountChangedDecisions((Dictionary<string, string> Keys, DateTimeOffset Now) baseline)
    {
        var after = DecisionKeys(baseline.Now);
        int changed = after.Count(kv => !baseline.Keys.TryGetValue(kv.Key, out var before) || before != kv.Value);
        changed += baseline.Keys.Keys.Count(id => !after.ContainsKey(id));
        return changed;
    }

    /// <summary>Tamamlanan Sync'in akış satırı: sessiz kipte commit → her zaman <see cref="StreamText.SyncedAfterCommit"/>,
    /// yenileme → kararı değişen satır varsa <see cref="StreamText.SyncedProjectsChanged"/>, yoksa hiç; diğer
    /// kiplerde bugünkü özet (<see cref="StreamText.Sync"/>).</summary>
    private string? SyncStreamLine(SyncCompletedEvent e)
    {
        if (_syncMode.IsVisible()) return StreamText.Sync(e.ToBuildCount, e.UpToDateCount);
        if (_silentReason == SilentSyncReason.Commit) return StreamText.SyncedAfterCommit;
        int changed = _silentBaseline is { } baseline ? CountChangedDecisions(baseline) : 0;
        return changed > 0 ? StreamText.SyncedProjectsChanged(changed) : null;
    }

    /// <summary>[A5/T69] Sync bitti: hedef commit + degrade bayrağı kaydedilir, faz <c>Idle</c>'a geçer
    /// (proje durumları artık bilinir — hollow değil).
    /// <para>[spec 2026-09-18 §1-7 · §6.1] <see cref="Branch"/> checkout edilmiş branch'e hizalanır (envanteri
    /// beklemeden — chip Sync biter bitmez doğru), son Sync'in HEAD'i ve tamamlanma anı kaydedilir, akış satırı
    /// kipe göre seçilir (<see cref="SyncStreamLine"/>).</para></summary>
    private void OnSyncCompleted(SyncCompletedEvent e)
    {
        bool visible = _syncMode.IsVisible(); // ReleaseSyncPhase kipi bırakmadan ÖNCE okunur
        TargetSha = e.TargetSha;
        FetchDegraded = e.FetchDegraded;
        Behind = e.Behind;      // [v1.16.0] chip'in sayısı; null ⇒ mesafe bilinmiyor → chip yok
        Branch = e.ActiveBranch ?? Branch; // detached HEAD'de son bilinen değer durur (OnBranchList ile aynı kural)
        LastSyncHead = (e.ActiveBranch, e.HeadSha);
        LastSyncCompletedAtMs = _nowMs();
        _syncStreamLine = SyncStreamLine(e);
        SyncErrorMessage = null; // [E2/T10] Sync başarıyla bitti — varsa önceki hata metni temizlenir
        ReleaseSyncPhase();    // [C2 fold] uçuş bayrağını normal yoldan da BURADAN temizle (tek yer)
        // Sync başarıyla bitti: durumlar kesin bilinir (degrade dahil). [review I2] Sessiz Sync fazı olduğu yerde
        // bırakır (Done/Stopped özeti "Ready"ye dönmez); yalnız Boot (henüz hiç Sync'lenmemiş) Idle'a çıkar.
        if (visible || Phase == AppPhase.Boot) Phase = AppPhase.Idle;
    }

    /// <summary>[C2 fold — A5 review] Uçuştaki Sync'i serbest bırakır: <see cref="_syncInFlight"/> temizlenir ve
    /// faz <c>Syncing</c>'de ASILI kaldıysa elde topoloji varsa <c>Idle</c>, yoksa <c>Boot</c>'a düşürülür.
    /// İki yerden çağrılır: (1) normal <see cref="OnSyncCompleted"/> (yalnız bayrağı temizler — çağıran fazı
    /// zaten Idle yapar) ve (2) <see cref="OnEngineExited"/> — engine Sync ORTASINDA ölürse (ne syncCompleted
    /// ne Sync-bitiren hata gelir) faz Syncing'de sonsuza dek asılı kalamaz ve bayrak sızmaz.</summary>
    private void ReleaseSyncPhase()
    {
        _syncInFlight = false;
        EndSyncMode(); // [spec 2026-09-18 §6.2] kip de bu Sync'le biter (motor kaybında da)
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
        EndSyncMode();
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
                    IsExternal = node.IsExternal,
                    // [design v1.20.0 §2.3] Yeni doğan satırın kararı YOKTUR — başlangıç modu bundan gelir;
                    // çıktı durumu hemen ardından gelen önizlemeyle yazılır (ayrı bir bayrak taşınmaz).
                });
            else
            {
                Projects[existing].SolutionName = solutionName; // topoloji sln atamasını değiştirmiş olabilir
                Projects[existing].InCycle = node.InCycle;       // cycle üyeliği topolojiyle değişmiş olabilir
                if (existing != i) Projects.Move(existing, i);
            }
        }

        // Sync = yeni taban: önceki run'ın KOŞU alanları artık geçmiştir. Sıfırlama bir İŞLEMİN nötrlemesiyle
        // AYNI metottur (bkz. NeutralizeRows) ve yalnız koşu alanlarını siler.
        // [DEĞİŞEN KURAL — design v1.20.0 §2.3] Eskiden Sync herkesi başlangıç moduna indirirdi (hiçbir şey
        // renklenmezdi). Artık satır kendi çıktı durumunda kalır ve önizleme onu tazeler — Sync renk verir.
        // [review I2] Sessiz Sync koşu bindirmesini SİLMEZ: bitmiş/düşmüş koşunun sonucu (şeridin özeti, satırların
        // statüsü) bir sonraki işleme kadar kalır; sessiz Sync yalnız çıktı durumunu (önizleme) tazeler.
        if (!IsRunning && _syncMode.IsVisible()) NeutralizeRows();

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
        // [spec 2026-09-18 §1-13] Reveal YALNIZ yapı değişince oynar — Sync'in yayını da bu kapıdan geçer
        // (bkz. _lastTopologySignature).
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
    /// [A13/T2 · 2.2 · spec 2026-09-18 §1-7] Branch envanteri geldi: liste tazelenir ve <see cref="Branch"/>
    /// checkout edilmiş branch'e eşitlenir — <c>Branch = ActiveBranchName ?? Branch</c>.
    ///
    /// <para><b>Değer bir tercih değil, okunan bir gerçektir.</b> Araç yalnız çalışma ağacında derler; kullanıcı
    /// terminalde <c>git checkout</c> yaparsa bir sonraki envanter değeri kendiliğinden hizaya sokar.
    /// <b>Aktif branch yoksa</b> (detached HEAD / boş envanter) son bilinen değer durur: uydurma değer YOK.</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL — spec 2026-09-18 §1-7/8]</b> Eskiden kullanıcının popover'dan yaptığı AÇIK seçim
    /// korunurdu (worktree'de derlenecek bir niyet olarak); worktree kalktığı için o dal da kalktı.</para>
    ///
    /// <para><c>origin/HEAD</c> listeye ALINMAZ (<see cref="IsRemoteHead"/>): bir branch değil, uzak deponun
    /// varsayılan branch'ine işaret eden sembolik bir ref'tir ve checkout hedefi olarak sunulmamalıdır.</para>
    /// </summary>
    private void OnBranchList(BranchListEvent e)
    {
        Branches.ReplaceAll([.. e.Branches.Where(b => !IsRemoteHead(b))]);
        // Türetilmiş değerin kendi bildirimi yok — bağlı görünümler tazelensin diye AÇIKÇA duyurulur.
        OnPropertyChanged(nameof(ActiveBranchName));
        Branch = ActiveBranchName ?? Branch;
    }

    /// <summary>Uzak deponun sembolik <c>HEAD</c> ref'i mi (<c>origin/HEAD</c>) — branch listesinde sunulmaz.</summary>
    private static bool IsRemoteHead(BranchRef branch) =>
        branch.IsRemoteTracking && branch.Name.EndsWith("/HEAD", StringComparison.Ordinal);

}
