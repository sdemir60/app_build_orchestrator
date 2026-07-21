using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Formatting;
using BuildOrchestrator.Core.Incremental;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>Proje listesindeki tek satır — tam kart görselleri (state renkleri, ▲/depIssue, ETA) It-4'te; burada
/// yalnız gözlemlenebilir VM-state [Task 17].</summary>
public sealed partial class ProjectRowViewModel : ObservableObject
{
    public string Id { get; }
    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private ProjectRowState _state;

    /// <summary>[Fix wave 1 · D1 review Finding 1] Bu proje topolojide bir cycle (SCC) üyesi mi —
    /// <see cref="ProjectNode.InCycle"/>'dan topoloji uzlaştırmasında (<see cref="RunViewModel.OnWorkspaceTopology"/>)
    /// taşınır (tıpkı <see cref="SolutionName"/> gibi). Cycle üyeleri motor tarafından pre-skip edilir; tasarım
    /// onları <c>skipped</c> değil <c>cycle</c> gösterir — bu bayrak <see cref="Status"/>'ta Pending/Skipped
    /// alt-durumunu EZER.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private bool _inCycle;

    /// <summary>[Fix wave 1 · D1 review Finding 1] Bir run uçuşta mı (<see cref="RunViewModel.IsRunning"/> ||
    /// <see cref="RunViewModel.IsStarting"/>) — <see cref="RunViewModel"/> her satıra iter (IsSelected deseni).
    /// <c>queued</c> = "planlanmış ama henüz başlamamış" YALNIZ bir run uçuştayken görünür bir durumdur; bu bayrak
    /// olmadan Pending bir satır ölü envanterden (Discovered) ayırt edilemez.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private bool _isRunActive;

    /// <summary>[T53-UI] Kartın soluk ikinci satırı — projenin ait olduğu solution'ın adı (prototip
    /// <c>p.sln</c>, BuildApp.jsx:384). Kaynak: <see cref="ProjectNode.SolutionNames"/> (ilk eleman); topoloji
    /// kurulurken atanır. Bir projeyi birden çok .sln içerebilir — kart tek (ilk) adı gösterir.</summary>
    [ObservableProperty] private string? _solutionName;

    /// <summary>[T53-UI] SHA çiftinin sol yarısı: projenin MEVCUT (derlenmiş) commit'i — prototip <c>st.curSha</c>
    /// (BuildApp.jsx:400). <b>Şu an hiçbir IPC event'i per-proje sha taşımaz</b> (yalnız run-geneli
    /// <see cref="RunViewModel.TargetSha"/> = SyncCompletedEvent.TargetSha vardır); bu alan bir sonraki
    /// per-proje-sha kablajına dek boş kalır (uydurulmaz). Kart yalnız <see cref="WillBuild"/>==true iken bu
    /// çifti gösterir.</summary>
    [ObservableProperty] private string? _currentSha;

    /// <summary>[T53-UI · C1 debt] Satır seçili mi — <see cref="RunViewModel.SelectedProjectId"/> değiştiğinde
    /// (<see cref="RunViewModel.OnSelectedProjectIdChanged"/>) tüm satırlar için tazelenir. Kart bunu şerit
    /// genişliği (2→3), iç sarmalayıcı <c>TranslateX 4</c> ve <c>Brush.SurfaceRaised</c> zemini için okur.</summary>
    [ObservableProperty] private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationMsText))]
    private long _durationMs;

    /// <summary>[Minor/Fix wave 1 · C2] Görüntü metni — <see cref="DurationFormat.Duration"/> ile (fmtDur portu,
    /// InvariantCulture): <c>4.2s</c> / <c>1m 12s</c>. Henüz derlenmemiş/skipped satırlar (<c>DurationMs == 0</c>)
    /// prototiple tutarlı biçimde <c>"—"</c> gösterir (null süre = bilinmiyor) — ham <c>0</c> ("0.0s") değil.</summary>
    public string DurationMsText => DurationFormat.Duration(DurationMs == 0 ? null : DurationMs);

    /// <summary>[Task 17][T53/v7Δ8] dirty=true, güncel(clean)=false, imza-yok/pre-Sync(hollow)=null.
    /// <see cref="BuildPreviewEvent"/> ile pre-populate edilir; proje succeeded olduğu ANDA (run içinde canlı)
    /// <c>false</c>'a döner — bkz. <see cref="RunViewModel.OnProjectDone"/> ("succeeded→clean" geçişi).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private bool? _willBuild;

    /// <summary>[Task 17] Bu proje için tespit edilen dependency-uyarısı kök adları (ör. "B", "C") — boşsa/hiç
    /// gelmediyse null. <see cref="ProjectSucceededEvent.DepIssues"/>/<see cref="ProjectFailedEvent.DepIssues"/>'tan
    /// doğrudan taşınır.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDepIssue))]
    private IReadOnlyList<string>? _depIssues;

    /// <summary>[Task 17] ▲ sinyali: <see cref="DepIssues"/> boş değilse true.</summary>
    public bool HasDepIssue => DepIssues is { Count: > 0 };

    /// <summary>[Fix wave 1 · D1 review Finding 1] Satırın GÖRSEL statüsü — <c>ProjectRowState</c> (motor durumu) +
    /// <see cref="InCycle"/> + <see cref="WillBuild"/> + <see cref="IsRunActive"/> sinyallerinin TEK eşleme yeri
    /// (kart yalnız bunu okur; eşleme mantığı kontrolde kopyalanmaz). <c>cycle</c> ve <c>queued</c> ayrı IPC
    /// alanları TAŞIMAZ — ikisi de eldeki topoloji/run sinyallerinden TÜRETİLİR:
    /// <list type="bullet">
    /// <item><b>cycle</b>: <see cref="InCycle"/>=true olan bir satır (Pending ya da pre-skipped Skipped) — tasarım
    /// onu skipped değil cycle gösterir (alt-durumu ezer).</item>
    /// <item><b>queued</b>: bir run uçuştayken (<see cref="IsRunActive"/>) planlanmış (<see cref="WillBuild"/>==true)
    /// ama henüz başlamamış (Pending) satır. Run bitince <see cref="IsRunActive"/> düşer → yine Discovered.</item>
    /// </list></summary>
    public Controls.GraphStatus Status
    {
        get
        {
            if (InCycle && State is ProjectRowState.Pending or ProjectRowState.Skipped)
                return Controls.GraphStatus.Cycle;
            return State switch
            {
                ProjectRowState.Started => Controls.GraphStatus.Building,
                ProjectRowState.Succeeded => Controls.GraphStatus.Succeeded,
                ProjectRowState.Failed => Controls.GraphStatus.Failed,
                ProjectRowState.Skipped => Controls.GraphStatus.Skipped,
                _ => IsRunActive && WillBuild == true ? Controls.GraphStatus.Queued : Controls.GraphStatus.Discovered,
            };
        }
    }

    public ProjectRowViewModel(string id, string name, ProjectRowState state, string? solutionName = null)
    {
        Id = id;
        Name = name;
        _state = state;
        _solutionName = solutionName;
    }
}

public enum ProjectRowState { Pending, Started, Succeeded, Failed, Skipped }

/// <summary>
/// [Task 12] Event → proje satırı/elapsed/log durumu. **UI-thread-agnostic çekirdek:** hiçbir yerde
/// Dispatcher/AvalonEdit türü kullanılmaz — <see cref="OnEvent"/> HANGİ THREAD'DEN çağrılırsa çağrılsın
/// güvenlidir; test thread'inden doğrudan çağrılabilir (D8: sleep-poll yok, event'ler doğrudan sürülür).
///
/// <para><b>Thread sınırı (MainWindow'un sorumluluğu):</b> <see cref="EngineHost.EventReceived"/> arka plan
/// thread'inde ateşlenir. YALNIZ <c>ProjectLogEvent</c> (MSBuild çıktısının HER satırı — potansiyel binlerce/sn)
/// için <see cref="OnEvent"/> DOĞRUDAN (marshal YOK) çağrılabilir: o dal yalnız <see cref="ConsoleBatcher.Post"/>
/// (kilitsiz) + kilitli (<c>_gate</c>) düz arabelleklere yazar, ObservableProperty/ObservableCollection'a ASLA
/// dokunmaz. DİĞER TÜM event tipleri — <c>ProjectLogChunkEvent</c> DAHİL (proje başına yalnız birkaç adet,
/// SON'da <see cref="ActiveProjectId"/>'yi mutasyona uğratır) — <c>Dispatcher.InvokeAsync</c> ile UI thread'ine
/// taşınmalıdır; bu marshal PER-EVENT değil PER-DURUM-DEĞİŞİKLİĞİ'dir (proje/run başına birkaç adet, akan log
/// satırları GİBİ binlerce DEĞİL), bu yüzden A13.2'nin "satır başına Dispatcher yasak" kuralını ihlal etmez.
/// İki thread'in ORTAK dokunduğu düz arabellekler (<c>_runText</c>/<c>_projectText</c>/<c>_liveLines</c>)
/// <c>_gate</c> kilidiyle korunur.</para>
///
/// <para><b>Log dikişi [T28]:</b> <see cref="LoadProjectLogAsync"/> bir proje için diskteki snapshot'ı ister;
/// gelen <c>ProjectLogChunkEvent</c>'ler sırayla biriktirilir, SON chunk'ta (<c>IsLast</c>) o ana kadar
/// tamponlanmış canlı <c>projectLog</c> satırlarından yalnız <c>LineNumber &gt; ThroughLineNumber</c> olanlar
/// (tekrar YOK) eklenir ve konsol proje moduna geçer.</para>
/// </summary>
public sealed partial class RunViewModel : ObservableObject
{
    // Bu kodlarda çalışan run'ın slotu serbest kalır ama runCompleted ASLA gelmez — App sonsuza dek
    // beklememeli [Kısıt 3]: planFailed/msbuildNotFound/noResumableRun/runFailed.
    // [Fix wave 3] runFailed: RunCoordinator.ExecuteRunAsync'in dış catch'i planlama SIRASINDA (runStarted'dan
    // ÖNCE) beklenmedik bir istisnada da bu kodu yayınlar — eklenmezse IsStarting kalıcı true kalır (aynı
    // wedge sınıfı, farklı tetikleyici). Küme BİLEREK genişletilmedi (ör. "tanınmayan her kod run-ending"
    // yapılmadı): badCommand/unknownCommand gibi run'ı bitirmeyen per-command hatalar da vardır.
    private static readonly HashSet<string> RunEndingErrorCodes =
        new(StringComparer.Ordinal) { "planFailed", "msbuildNotFound", "noResumableRun", "runFailed" };

    private readonly EngineHost _engine;
    private readonly ConsoleBatcher _console;
    private readonly Func<string> _newRunId;
    private readonly Func<long> _nowMs; // [Minor/Fix wave 1] elapsed hesap kaynağı — testte deterministik saat enjekte edilir (D8)

    // [Kısıt 4] _runText/_projectText/_liveLines HEM arka plan thread'inden (OnProjectLog — marshal YOK,
    // A13.2) HEM UI thread'inden (chunk/Get*DocumentText) dokunulur — düz Dictionary/StringBuilder thread-safe
    // DEĞİLDİR, bu yüzden tüm erişimler _gate altındadır. ActiveProjectId'nin kendisi (WPF binding'e bağlı
    // [ObservableProperty]) SADECE UI thread'inde yazılır (OnProjectLogChunk marshallı) — kilide gerek yok,
    // yalnız OKUNURKEN arka plandan (benign race: referans türü ataması atomiktir, en kötü tek satır yanlış
    // hedefe gider — kabul edilebilir ölçek [It-2 iskelesi]).
    private readonly object _gate = new();
    private readonly StringBuilder _runText = new();
    private readonly Dictionary<string, StringBuilder> _projectText = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ProjectLogEvent>> _liveLines = new(StringComparer.OrdinalIgnoreCase);
    // [T56/3a] "N lines" TAM tampon sayacı (render dilimi DEĞİL, Ek A #23). _gate altında O(1) artırılır — her
    // append tam bir satır ('\n' sonekli) eklediğinden konsol başlığı (ConsoleHeader) bunu okur. Marshal-free
    // OnProjectLog yolundan yazıldığı için ObservableProperty DEĞİL; UI thread'i _gate altında okur (GetActiveLineCount).
    private int _runLineCount;
    private readonly Dictionary<string, int> _projectLineCount = new(StringComparer.OrdinalIgnoreCase);
    private PendingLoad? _pendingLoad; // yalnız UI thread'inde dokunulur (LoadProjectLogAsync + OnProjectLogChunk)

    private string? _currentRunId;
    private bool _sawRunStarted; // bu run denemesinde runStarted görüldü mü — runStopped'ın runCompleted'sız gelip gelmeyeceğini ayırt eder
    private long _elapsedBaseMs;
    private long? _elapsedStartMs; // run başladığında _nowMs() — null iken hiç run başlamamış/durmuş

    // [Task 17] ETA: EtaCalculator saf/stateless'tir (D3 — hiçbir alan/saat tutmaz) — EMA'nın önceki (smoothed)
    // değerini VM burada taşır. _totalProjects/_runParallelism runStarted'dan gelir; _projectStartedAtMs, şu an
    // building olan her projenin (_nowMs() ile ölçülen) elapsed'ini hesaplamak için ProjectStarted'da kaydedilir,
    // proje tamamlanınca silinir. App'te BuildState.LastDurationMs YOK — tahmin kaynağı bu run içinde GÖZLEMLENEN
    // (Succeeded/Failed) süphelerin ortalamasıdır (brief'te açıkça belirtilen kasıtlı basitleştirme).
    private long? _previousEtaMs;
    private int? _totalProjects;
    private int? _runParallelism;
    private readonly Dictionary<string, long> _projectStartedAtMs = new(StringComparer.OrdinalIgnoreCase);

    // [D2/T38] Sticky şeridin "wb/fin/allClean"i için SABİT willBuild kümesi: prototipte (BuildApp.jsx) willBuild
    // koşu boyunca değişmez (eng.willBuild). VM'de satırların WillBuild bayrağı succeeded olunca false'a döndüğü
    // için CANLI sayılamaz — bu yüzden run başında BuildPreviewEvent'ten (WillBuild==true olanlar) DONDURULUR.
    // NOT (wire gap): BuildPreviewEvent yalnız RUN başında gelir (RunCoordinator), Sync sonrası Idle'da GELMEZ —
    // bu yüzden pre-build Idle'da bu küme boştur (allClean=true varsayılır). Bkz. task-D2-report.md.
    private readonly HashSet<string> _willBuildIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>[Fix wave 1, Finding 2 regression testi] YALNIZ testler için: <see cref="OnProjectLogChunk"/>
    /// dikiş kilidinden çıkar çıkmaz (kilit ne zaman kapansa, kapandığı ANDA) senkron tetiklenir. Üretimde
    /// hep null — sıfır maliyet. Testte, kilit içinde <c>ActiveProjectId</c> atamasının GERÇEKTEN kilitle
    /// birlikte kapandığını (eskiden kilit DIŞINDAYDI — bkz. Finding 2) tek thread'de, sleep/poll OLMADAN
    /// deterministik biçimde kanıtlamak için kullanılır: kanca içinden enjekte edilen bir canlı
    /// <c>ProjectLogEvent</c>, ancak <c>ActiveProjectId</c> zaten güncellenmişse projeye düşer.</summary>
    internal Action? DebugAfterStitchLockExited;

    public ObservableCollection<ProjectRowViewModel> Projects { get; } = [];

    // [A5/T69 · Fix wave 1 — Finding 6] Sync / branch / worktree / topoloji yüzeyi AYRI partial dosyada:
    // RunViewModel.Workspace.cs (faz, hedef commit, envanter, topoloji uzlaştırma).

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkspace))]
    private string _rootPath = "";
    [ObservableProperty] private string _configuration = "Debug";

    // [Fix wave 1, C2 review Finding 2] Parallelism artık PerfMode'un varsayılanından tohumlanır — eski
    // Environment.ProcessorCount varsayılanı PerfMode'dan (It-2) ÖNCEYDİ ve _perfMode="Balanced"→4 (v7 plan
    // K11: perf mode SABİT 6/4/2 tablosudur) ile çelişiyordu. TEK kaynak: her ikisi de aynı sabiti kullanır.
    [ObservableProperty] private int _parallelism = ParallelismFor(DefaultPerfMode);
    [ObservableProperty] private long _elapsedMs;

    /// <summary>[Task 17] Run genelinde (RunCompletedEvent'ten) dependency-affected proje sayısı özeti.</summary>
    [ObservableProperty] private int _depIssueCount;

    /// <summary>[Task 17][T70/A6-Δ8] EtaCalculator'ın gösterim metni — "~Ns left" / "· almost done" /
    /// "{completed}/{total} · {elapsed}" (ilk-koşu/bilinmeyen-süre fallback'i). Her proje tamamlanışında
    /// (<see cref="OnProjectDone"/>/ProjectSkipped) ve runStarted'da (X/N fallback ile) güncellenir.</summary>
    [ObservableProperty] private string _etaText = "";

    /// <summary>[D2/T70] Yumuşatılmış ETA (ms) — <see cref="RibbonText.EtaSuffix"/> bunu okuyup " · ~35s left"/
    /// " · almost done" ekini üretir. <see cref="UpdateEta"/>'da set edilir; ETA hesaplanamıyorsa (no-history)
    /// <c>null</c>. <see cref="EtaText"/> (string) ayrı kalır (başka tüketiciler için); şerit numeric <c>EtaMs</c>'i kullanır.</summary>
    [ObservableProperty] private long? _etaMs;

    /// <summary>[D2/T38] Bu koşuda derlenecek proje YOK (SABİT willBuild kümesi boş) — şerit faz-metni ve progress
    /// kolu bunu okur (prototip <c>eng.allClean</c>). Bkz. <see cref="RecomputeWillBuildSurface"/>.</summary>
    [ObservableProperty] private bool _allClean = true;

    /// <summary>[D2/T38] Derlenecek (willBuild) proje sayısı — koşu boyunca SABİT (prototip <c>wb</c>).</summary>
    [ObservableProperty] private int _willBuildCount;

    /// <summary>[D2/T38] willBuild kümesinden tamamlanan (succeeded/failed/skipped) sayısı (prototip <c>fin</c>).</summary>
    [ObservableProperty] private int _finishedOfWillBuild;

    /// <summary>[D2/T38] Repo seçili mi (prototip <c>workspace</c>) — şerit "Not ready — no repository selected"
    /// davetini bununla ayırt eder.</summary>
    public bool HasWorkspace => RootPath.Length > 0;

    // [Fix wave 1, Finding 1] RelayCommand'ların CanExecuteChanged'ı YALNIZ NotifyCanExecuteChangedFor
    // (veya elle NotifyCanExecuteChanged()) ile ateşlenir — CommunityToolkit CommandManager.RequerySuggested'a
    // ABONE OLMAZ. Bu olmadan Stop/Continue butonları gerçek pencerede İLK bind sonrası ASLA yeniden
    // sorgulanmaz (StopCommand hep disabled kalır, ContinueCommand hep ölü kalır) — Kısıt 3'ü bozar.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryFailedCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    [NotifyPropertyChangedFor(nameof(IsMidRunLocked))] // [T12] branch/worktree/config kilidi bundan türetilir
    private bool _isRunning;

    // [Fix wave 1(It-3), Finding 3] Supervisor runStarted'dan ÖNCE planlama yapar (scan/graph/topo — 177
    // projeli OSYS'te saniyeler sürebilir) ve stop-during-planning'i AÇIKÇA destekler (ack-debt yolu,
    // RunCoordinator'da test edilmiş). IsRunning yalnız runStarted ile true olduğundan, planlama sırasında
    // Stop erişilemez kalıyordu ve çift Rebuild tıklaması runInProgress'e neden olabiliyordu. IsStarting,
    // komut gönderilir gönderilmez (runStarted/runStopped/run-bitiren ErrorEvent'e kadar) bu boşluğu kapatır.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryFailedCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    [NotifyPropertyChangedFor(nameof(IsMidRunLocked))]
    private bool _isStarting;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _canContinue;

    [ObservableProperty] private string? _activeProjectId; // null = run dokümanı gösteriliyor

    /// <summary>[Task 16 — It-2 devir §8] Engine process öldüğünde (<see cref="OnEngineExited"/>) kullanıcıya
    /// gösterilecek metin — sticky şerit kalıcı hata modunun PIXEL karşılığı It-4'te; burada yalnız VM-state.
    /// [Review fix] Kalıcı DEĞİLDİR: bir sonraki run'ın <see cref="OnRunStarted"/>'ı (engine'in CANLI ve IPC
    /// round-trip yaptığının ilk somut kanıtı) bu mesajı temizler — aksi halde tek bir ölümden sonra sonsuza
    /// dek stale kalıp, tamamen başarılı sonraki run'larda bile güncel engine sağlığını yanlış yansıtırdı.</summary>
    [ObservableProperty] private string? _engineDiedMessage;

    // ---------------------------------------------------------------- [C2] seçim / filtre / workspace hedefi / perf

    /// <summary>[C2] Proje listesinde seçili satırın Id'si (yol) — null = seçim yok. <see cref="SelectProject"/>
    /// ile yönetilir (aynı projeye tekrar tıklama = deselect).</summary>
    [ObservableProperty] private string? _selectedProjectId;

    /// <summary>[C2] Aktif statü chip'i (<see cref="ProjectFilter"/> sabitleri) — null = filtre yok.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleProjects))]
    private string? _activeFilter;

    /// <summary>[C2] Serbest metin proje sorgusu (ada göre alt-dize).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleProjects))]
    private string _projectQuery = "";

    /// <summary>[C2] Sync/build hedefi branch. Koşarken UI'da kilitli (<see cref="IsMidRunLocked"/>).</summary>
    [ObservableProperty] private string _branch = "";

    /// <summary>[C2] true ⇒ derleme ayrı bir git worktree üzerinde. Koşarken UI'da kilitli.</summary>
    [ObservableProperty] private bool _useWorktree;

    /// <summary>[C2] <see cref="UseWorktree"/>=true iken worktree adı; null ⇒ Supervisor varsayılan ad türetir.</summary>
    [ObservableProperty] private string? _worktreeName;

    // [Fix wave 1, C2 review Finding 2] PerfMode/Parallelism alan başlatıcılarının TEK ortak kaynağı (derleme
    // zamanı sabiti — alan başlatma SIRASINDAN bağımsız, yukarıdaki Parallelism başlatıcısından da güvenle
    // kullanılabilir).
    private const string DefaultPerfMode = "Balanced";

    /// <summary>[C2] Perf profili: Full/Balanced/Light. <see cref="CyclePerf"/> döngüsü paralelliği de günceller.</summary>
    [ObservableProperty] private string _perfMode = DefaultPerfMode;

    /// <summary>[C2] Proje listesi durum sayaçları — satır değişimlerinde yeniden hesaplanır.</summary>
    [ObservableProperty] private RunCounters _counters;

    /// <summary>[C2] Katman ataması pattern'leri (StartRunCommand/SyncWorkspaceCommand'a geçer). Store (D6/D7)
    /// tarafından seed edilecek — C2 yalnız GÖNDERİR; ObservableProperty gerekmez (UI'dan iki-yönlü bağlanmaz).</summary>
    public IReadOnlyList<LayerPattern>? LayerPatterns { get; set; }

    /// <summary>[T12] Koşarken (veya planlama penceresinde) branch/worktree/configuration kontrolleri kilitli;
    /// perf chip'i CANLI kalır. UI <c>IsEnabled</c> bunu okur.</summary>
    public bool IsMidRunLocked => IsRunning || IsStarting;

    /// <summary>[C2] Sorgu + aktif filtre altında görünen satırlar (BuildApp.jsx:465-470).</summary>
    public IReadOnlyList<ProjectRowViewModel> VisibleProjects =>
        Projects.Where(r => ProjectFilter.Matches(r, ProjectQuery, ActiveFilter)).ToList();

    /// <summary>[C2 fold testi] YALNIZ testler: uçuştaki Sync bayrağının gözlemlenebilir hali (bkz.
    /// <see cref="OnEngineExited"/> fold'u — engine ölümü bu bayrağı bırakmalı).</summary>
    internal bool SyncInFlight => _syncInFlight;

    // [C2] Boot geçişi: repo seçilir seçilmez (RootPath dolunca) Empty → Boot. Sonraki fazları engine event'leri sürer.
    partial void OnRootPathChanged(string value)
    {
        if (Phase == AppPhase.Empty && !string.IsNullOrEmpty(value)) Phase = AppPhase.Boot;
    }

    public RunViewModel(EngineHost engine, ConsoleBatcher console, Func<string> newRunId, Func<long>? nowMs = null)
    {
        _engine = engine;
        _console = console;
        _newRunId = newRunId;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
    }

    // ---------------------------------------------------------------- komutlar

    /// <summary>[C2] Ortak run başlatma yolu (Rebuild/Build/Continue/RetryFailed) — tek yerde toplanır:
    /// runId üret, konsolu run dokümanına al, <see cref="IsStarting"/>'i aç ve <see cref="StartRunCommand"/>'ı
    /// workspace hedefiyle (branch/worktree/layer patterns — Supervisor tarafı A1-A4'te bağlı) gönder.
    /// <para>[Fix wave 1(It-3), Finding 1] <paramref name="clearBuffers"/>=true iken önceki run'ın
    /// <c>_liveLines/_projectText/_runText</c> tortusu temizlenir: aksi halde İKİNCİ run'da kart tıklamasında
    /// dikiş filtresi (LineNumber &gt; ThroughLineNumber) eski run'ın kuyruk satırlarını da geçirir ve
    /// OrderBy(LineNumber) eski+yeni'yi karıştırır (bozuk "tam log"). runStarted'ı BEKLEMEDEN burada temizlenir:
    /// ProjectLogEvent marshal'sız işlendiğinden yeni run'ın ilk satırları, marshal'lı runStarted UI thread'ine
    /// düşmeden ÖNCE varabilir. <b>Continue temizlemez</b> (önceki segmentin log/proje sonuçlarını korur).</para>
    /// <para>[Fix wave 2, Finding 1] Gönderim SENKRON başarısız olursa (engine hiç başlamadı/öldü) IsStarting
    /// geri açılır — aksi halde hiçbir engine event'i gelmeyeceğinden buton kalıcı kilitli kalırdı.</para></summary>
    private async Task BeginRunAsync(RunMode mode, bool clearBuffers)
    {
        string runId = _newRunId();
        _currentRunId = runId;
        _sawRunStarted = false;
        ActiveProjectId = null;
        IsStarting = true;
        if (clearBuffers)
            lock (_gate)
            {
                _liveLines.Clear();
                _projectText.Clear();
                _runText.Clear();
                _runLineCount = 0;
                _projectLineCount.Clear();
            }
        var cmd = new StartRunCommand(runId, mode, RootPath, Configuration, Parallelism,
            Branch, UseWorktree, WorktreeName, DependentMode.Safe, LayerPatterns);
        if (!await TrySendAsync(cmd, RunModeLabel(mode)))
            IsStarting = false;
    }

    private static string RunModeLabel(RunMode mode) => mode switch
    {
        RunMode.Rebuild => "rebuild",
        RunMode.Build => "build",
        RunMode.Continue => "continue",
        RunMode.RetryFailed => "retry",
        _ => "run",
    };

    [RelayCommand(CanExecute = nameof(CanRebuildOrRetry))]
    private Task RebuildAsync()
    {
        ClearSelectionAndFilter(); // [design doRebuild→doBuild] tam run: seçim + filtre sıfırlanır
        return BeginRunAsync(RunMode.Rebuild, clearBuffers: true);
    }
    private bool CanStartRun() => !IsRunning && !IsStarting;

    // [Fix wave 1, C2 review Finding 1] Rebuild/RetryFailed, Sync uçuştayken (_syncInFlight) EK OLARAK
    // engellenir — mid-Sync BeginRunAsync(clearBuffers:true) _runText/_liveLines/_projectText'i temizler,
    // ama SyncProgressEvent hâlâ _runText'e satır ekliyor olabilir (canlı Sync transkriptini bozar).
    // Build BİLEREK bu guard'a dahil DEĞİL (prototip doBuild'in kasıtlı asimetrisi, BuildApp.jsx:1194 —
    // doRebuild/doRetry'nin aksine phase==='syncing' erken-dönüşü yoktur).
    private bool CanRebuildOrRetry() => CanStartRun() && !_syncInFlight;

    [RelayCommand(CanExecute = nameof(CanStartRun))]
    private Task BuildAsync()
    {
        ClearSelectionAndFilter(); // BuildApp.jsx:1199-1200
        return BeginRunAsync(RunMode.Build, clearBuffers: true);
    }

    [RelayCommand(CanExecute = nameof(CanRetryFailed))]
    private Task RetryFailedAsync()
    {
        ClearSelectionAndFilter(); // BuildApp.jsx:1221-1222
        return BeginRunAsync(RunMode.RetryFailed, clearBuffers: true);
    }
    // [C2] Yalnız önceki koşuda bir failure varsa etkin — CanExecute her çağrıda taze değerlendirilir; ayrıca
    // OnProjectDone/OnRunCompleted NotifyCanExecuteChanged tetikler (canlı UI için).
    private bool CanRetryFailed() => CanRebuildOrRetry() && Projects.Any(p => p.State == ProjectRowState.Failed);

    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncAsync()
    {
        SelectedProjectId = null; // [design doSync] seçim temizlenir, filtre KORUNUR
        await TrySendAsync(new SyncWorkspaceCommand(RootPath, Branch, LayerPatterns, Configuration), "sync");
    }
    private bool CanSync() => !IsRunning && !IsStarting;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        if (_currentRunId is null) return;
        await TrySendAsync(new StopRunCommand(_currentRunId, StopKind.Graceful), "stop");
    }
    private bool CanStop() => IsRunning || IsStarting;

    [RelayCommand(CanExecute = nameof(CanContinueRun))]
    private Task ContinueAsync() => BeginRunAsync(RunMode.Continue, clearBuffers: false); // [design doContinue] seçim/filtre KORUNUR
    private bool CanContinueRun() => !IsRunning && !IsStarting && CanContinue;

    /// <summary>[C2] Aynı projeye tekrar tıklamak seçimi kaldırır (kanonik deselect, BuildApp.jsx). Proje
    /// Id'leri Windows dosya yollarıdır → <see cref="StringComparison.OrdinalIgnoreCase"/>.</summary>
    public void SelectProject(string? id) =>
        SelectedProjectId = string.Equals(SelectedProjectId, id, StringComparison.OrdinalIgnoreCase) ? null : id;

    /// <summary>[T53-UI · C1 debt] Seçim değişince her satırın <see cref="ProjectRowViewModel.IsSelected"/>'ını
    /// tazeler — kartın görsel seçili durumu (şerit 2→3, iç sarmalayıcı TranslateX, <c>Brush.SurfaceRaised</c>
    /// zemin) satır VM'inin INotifyPropertyChanged'inden akar (konsol/log geçişi D4'ün işi — burada YOK).</summary>
    partial void OnSelectedProjectIdChanged(string? value)
    {
        foreach (var row in Projects)
            row.IsSelected = string.Equals(row.Id, value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>[Fix wave 1 · D1 review Finding 1] Bir run uçuşta mı — <see cref="ProjectRowViewModel.Status"/>'un
    /// <c>queued</c> türetimi için her satıra iter (IsSelected akışının eşi). <see cref="IsRunning"/>/
    /// <see cref="IsStarting"/> değiştiğinde tazelenir; yeni doğan satırlar (<see cref="EnsureRow"/>/topoloji)
    /// da mevcut değeri alır.</summary>
    private bool RunActive => IsRunning || IsStarting;
    partial void OnIsRunningChanged(bool value) => PropagateRunActive();
    partial void OnIsStartingChanged(bool value) => PropagateRunActive();
    private void PropagateRunActive()
    {
        bool active = RunActive;
        foreach (var row in Projects) row.IsRunActive = active;
    }

    /// <summary>[T53/T54-UI] Proje listesini katman gruplarına böler — gruplama YALNIZ topolojiden
    /// (<see cref="ProjectNode.LayerName"/>/<see cref="ProjectNode.LayerIndex"/>) gelir; App'te regex YOKTUR
    /// (mimari kural, test pinler). <see cref="Projects"/> zaten build-order'dadır (topoloji sırası). Hiçbir
    /// düğümün <c>LayerName</c>'i yoksa tek isimsiz grup = düz build-order.</summary>
    public IReadOnlyList<LayerGrouping.Group> BuildLayerGroups() =>
        LayerGrouping.Build(Projects, Topology);

    private void ClearSelectionAndFilter()
    {
        SelectedProjectId = null;
        ActiveFilter = null;
    }

    /// <summary>[T43] Debug/Release değiştir (BuildApp.jsx:1355-1363). Koşarken KİLİTLİ (no-op) ve aynı değere
    /// no-op. Workspace varsa ve faz Boot/Empty değilse: her proje dirty işaretlenir ve uyarı satırı yazılır.</summary>
    public void SetConfiguration(string value)
    {
        if (IsMidRunLocked || value == Configuration) return;
        Configuration = value;
        if (RootPath.Length == 0 || Phase is AppPhase.Boot or AppPhase.Empty) return;
        foreach (var row in Projects) row.WillBuild = true; // her şey dirty
        AppendRunLine($"Configuration → {value} — all projects will rebuild");
    }

    // [T43] Perf profili → paralellik TEK sabit eşleme (prototipteki 3 kopya taşınmaz).
    private static int ParallelismFor(string perfMode) => perfMode switch
    {
        "Full" => 6, "Balanced" => 4, "Light" => 2, _ => 4,
    };

    /// <summary>[T43] Perf chip: Full → Balanced → Light → Full döngüsü; paralelliği de günceller. Koşarken de
    /// CANLI (kilitlenmez) — yalnız koşarken konsola "parallelism: N (Mode)" notu yazılır (BuildApp.jsx:1366-1372).</summary>
    public void CyclePerf()
    {
        PerfMode = PerfMode switch { "Full" => "Balanced", "Balanced" => "Light", "Light" => "Full", _ => "Balanced" };
        Parallelism = ParallelismFor(PerfMode);
        if (IsRunning) AppendRunLine($"parallelism: {Parallelism} ({PerfMode})");
    }

    /// <summary>[C2] Satır durumu değişimlerinde türev yüzeyi tazeler: sayaçlar, görünür liste, RetryFailed
    /// etkinliği.</summary>
    private void RefreshRunSurface()
    {
        Counters = RunCounters.From(Projects);
        RecomputeWillBuildSurface(); // [D2] wb/fin/allClean — sayaçlarla aynı tetikleyicide tazelenir
        OnPropertyChanged(nameof(VisibleProjects));
        RetryFailedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>[D2/T38] Şeridin SABİT willBuild yüzeyini (wb/fin/allClean) <see cref="_willBuildIds"/>'ten türetir —
    /// canlı satır bayraklarından DEĞİL (succeeded olunca WillBuild false'a döner; küme donduğu için wb sabit kalır).</summary>
    private void RecomputeWillBuildSurface()
    {
        WillBuildCount = _willBuildIds.Count;
        AllClean = _willBuildIds.Count == 0;
        int fin = 0;
        foreach (var row in Projects)
            if (_willBuildIds.Contains(row.Id) &&
                row.State is ProjectRowState.Succeeded or ProjectRowState.Failed or ProjectRowState.Skipped)
                fin++;
        FinishedOfWillBuild = fin;
    }

    /// <summary>Engine hazır değilken (henüz başlamadı/çöktü) SendAsync SENKRON fırlar — UI tıklaması bu
    /// yüzden çökmemeli; hata run dokümanına düşürülür, sessizce yutulmaz. Dönen <c>bool</c>, çağıranın
    /// gönderim BAŞARISIZ olduğunda kendi "starting" durumunu geri açabilmesi içindir — bu metot kendi başına
    /// hiçbir bound-state'e dokunmaz. <see cref="DebugOnCommandSent"/> yalnız testler içindir.</summary>
    private async Task<bool> TrySendAsync(IpcCommand cmd, string what)
    {
        DebugOnCommandSent?.Invoke(cmd);
        try { await _engine.SendAsync(cmd); return true; }
        catch (Exception ex) { AppendRunLine($"[hata] {what} gönderilemedi: {ex.Message}"); return false; }
    }

    /// <summary>[C2 testleri] YALNIZ testler ayarlar (bkz. <see cref="DebugAfterStitchLockExited"/> deseni):
    /// bir komut gönderilmeden hemen ÖNCE senkron tetiklenir; gönderilen <see cref="StartRunCommand"/>'ın
    /// workspace argümanlarını (Mode/Branch/UseWorktree/WorktreeName/LayerPatterns) gerçek Supervisor'a
    /// ihtiyaç duymadan gözlemlemeye yarar. Üretimde hep null — sıfır maliyet.</summary>
    internal Action<IpcCommand>? DebugOnCommandSent;

    // ---------------------------------------------------------------- elapsed

    /// <summary>MainWindow'un DispatcherTimer'ı UI thread'inde periyodik çağırır. VM Dispatcher/Timer TÜRÜ
    /// TAŞIMAZ — test edilebilirlik için saat kaynağı enjekte edilen <see cref="_nowMs"/> (constructor'da
    /// verilmezse <c>Environment.TickCount64</c>; testte deterministik bir <c>Func&lt;long&gt;</c> geçilir,
    /// D8: sleep/poll yok) [Minor/Fix wave 1].</summary>
    public void TickElapsed()
    {
        if (IsRunning && _elapsedStartMs is { } startMs)
        {
            ElapsedMs = _elapsedBaseMs + (_nowMs() - startMs);
            // [T53-UI] Building satırların CANLI süresi (kart süre kolonu + glyph tooltip) — done olunca
            // OnProjectDone kesin DurationMs'i ezer. Kaynak: _projectStartedAtMs (ETA ile AYNI, ekstra state yok).
            long now = _nowMs();
            foreach (var row in Projects)
                if (row.State == ProjectRowState.Started && _projectStartedAtMs.TryGetValue(row.Id, out long at))
                    row.DurationMs = Math.Max(0, now - at);
            // [D2/T70 — It-4 canlı tick] It-3'te ETA yalnız COMPLETION'da yeniden hesaplanıyordu ("live tick It-4"
            // devir notu); şerit canlı "~Ns left"i ve building'in azalan kalanını görebilsin diye her tick'te tazelenir.
            UpdateEta();
        }
    }

    // ---------------------------------------------------------------- event → durum

    public void OnEvent(IpcEvent ev)
    {
        switch (ev)
        {
            case RunStartedEvent e: OnRunStarted(e); break;
            case BuildPreviewEvent e: OnBuildPreview(e); break;
            case ProjectStartedEvent e: OnProjectStarted(e); break;
            case ProjectLogEvent e: OnProjectLog(e); break;
            case ProjectLogChunkEvent e: OnProjectLogChunk(e); break;
            case ProjectSucceededEvent e: OnProjectDone(e.ProjectId, ProjectRowState.Succeeded, e.DurationMs, e.DepIssues); break;
            case ProjectFailedEvent e: OnProjectDone(e.ProjectId, ProjectRowState.Failed, e.DurationMs, e.DepIssues); break;
            case ProjectSkippedEvent e: OnProjectSkipped(e); break;
            case RunCompletedEvent e: OnRunCompleted(e); break;
            case RunStoppedEvent: OnRunStopped(); break;
            case ErrorEvent e: OnError(e); break;
            // [A5/T69] Sync yüzeyi — handler'lar RunViewModel.Workspace.cs'te
            case SyncStartedEvent: OnSyncStarted(); break;
            case SyncProgressEvent e: AppendRunLine(e.Line); break;
            case SyncCompletedEvent e: OnSyncCompleted(e); break;
            case WorkspaceTopologyEvent e: OnWorkspaceTopology(e); break;
            case BranchListEvent e: Replace(Branches, e.Branches); break;
            case WorktreeListEvent e: Replace(Worktrees, e.Worktrees); break;
        }
    }

    private void OnRunStarted(RunStartedEvent e)
    {
        _currentRunId = e.RunId;
        _sawRunStarted = true;
        IsRunning = true;
        Phase = AppPhase.Running; // [C2] Idle → Running
        IsStarting = false; // [Fix wave 1(It-3), Finding 3] planlama bitti — Stop artık IsRunning üzerinden erişilebilir
        // [Review fix, Task 16] EngineDiedMessage burada temizlenir: runStarted, VM'in CANLI engine instance'ıyla
        // IPC round-trip yaptığının ilk somut kanıtıdır — RebuildAsync/ContinueAsync'de ERKEN temizlemek YANLIŞ
        // olurdu (gönderim henüz round-trip olmadan "iyimser" temizlik, engine hâlâ ölüyken bile mesajı silerdi).
        // Temizlenmezse EngineDiedMessage tek bir ölümden sonra SONSUZA DEK stale kalır — sıradaki N run tamamen
        // başarılı olsa bile "engine öldü" mesajı güncel engine sağlığını YANLIŞ yansıtmaya devam eder.
        EngineDiedMessage = null;
        _elapsedBaseMs = e.ElapsedMsAtStart;
        _elapsedStartMs = _nowMs();
        ElapsedMs = e.ElapsedMsAtStart;
        if (e.Mode == RunMode.Rebuild) Projects.Clear(); // Continue'da liste (önceki segmentin sonuçları) korunur
        _willBuildIds.Clear(); // [D2] SABİT willBuild kümesi bu run için taze — hemen ardından BuildPreviewEvent doldurur
        // [Task 17] ETA state bu run/segment için taze başlar — bkz. _previousEtaMs alanının XML yorumu.
        _previousEtaMs = null;
        _totalProjects = e.TotalProjects;
        _runParallelism = e.Parallelism;
        _projectStartedAtMs.Clear();
        UpdateEta(); // runStarted anında henüz hiçbir completion yok → X/N fallback (ETA numarası YOK)
        RefreshRunSurface();
    }

    /// <summary>[Task 17] <see cref="BuildPreviewEvent"/> — run başlar başlamaz, ilk proje-başına event'ten ÖNCE
    /// gelir: <see cref="Projects"/>'i willBuild bilgisiyle PRE-POPULATE eder (dirty=true/güncel=false/hollow=null).
    /// [Review fix, Task 17] Satır zaten varsa bu ARTIK normal Continue/RetryFailed şeklidir (savunmacı bir
    /// edge case DEĞİL): <see cref="RunCoordinator"/> her segmentin başında AYNI (dondurulmuş, segment-1
    /// zamanlı) plan'dan türetilmiş <see cref="BuildPreviewEvent"/>'i YENİDEN yayınlar, ve <see cref="Projects"/>
    /// Continue'da temizlenmez (bkz. <see cref="OnRunStarted"/>). Satır bu VM instance'ında zaten TERMİNAL
    /// (Succeeded/Failed/Skipped) ise WillBuild GÜNCELLENMEZ — aksi halde segment 1'de gerçekleşen
    /// succeeded→clean canlı geçişi (bkz. <see cref="OnProjectDone"/>), segment 2'nin (bilerek bayat) preview
    /// değeriyle sessizce EZİLİRDİ.</summary>
    private void OnBuildPreview(BuildPreviewEvent e)
    {
        foreach (var item in e.Items)
        {
            var row = EnsureRow(item.ProjectId, item.Name, ProjectRowState.Pending);
            if (item.WillBuild == true) _willBuildIds.Add(item.ProjectId); // [D2] SABİT willBuild kümesini doldur
            if (row.State is ProjectRowState.Succeeded or ProjectRowState.Failed or ProjectRowState.Skipped) continue;
            row.WillBuild = item.WillBuild;
        }
        RefreshRunSurface();
    }

    /// <summary>[Task 17] buildPreview'ın önceden oluşturduğu bir satır varsa (Pending) onu Started'a TAŞIR —
    /// EnsureRow yalnız YENİ satırlar için initialState uygular, var olan satırın State'ini DEĞİŞTİRMEZ, bu
    /// yüzden burada AYRICA atanır (ProjectSkipped'in zaten yaptığı gibi).</summary>
    private void OnProjectStarted(ProjectStartedEvent e)
    {
        EnsureRow(e.ProjectId, e.Name, ProjectRowState.Started).State = ProjectRowState.Started;
        _projectStartedAtMs[e.ProjectId] = _nowMs();
        RefreshRunSurface();
    }

    private void OnProjectSkipped(ProjectSkippedEvent e)
    {
        EnsureRow(e.ProjectId, Path.GetFileNameWithoutExtension(e.ProjectId), ProjectRowState.Skipped).State = ProjectRowState.Skipped;
        _projectStartedAtMs.Remove(e.ProjectId);
        UpdateEta(); // [Task 17] skip de bir "tamamlanma" — kalan sayaç değişir
        RefreshRunSurface();
    }

    private void OnProjectDone(string projectId, ProjectRowState state, long durationMs, IReadOnlyList<string>? depIssues)
    {
        var row = Projects.FirstOrDefault(p => p.Id == projectId);
        if (row is null) return; // protokole göre Started her zaman önce gelir — savunmacı no-op
        row.State = state;
        row.DurationMs = durationMs;
        row.DepIssues = depIssues; // [Task 17] ▲ sinyali — HasDepIssue bundan türetilir
        // [Task 17][v7Δ8] "succeeded→clean" CANLI geçiş: proje bu run içinde başarıyla derlendiği ANDA artık
        // güncel (clean) sayılır — preview'ın dirty=true'sunu (ya da hollow=null'ını) burada EZER.
        if (state == ProjectRowState.Succeeded) row.WillBuild = false;
        _projectStartedAtMs.Remove(projectId);
        UpdateEta(); // [Task 17] her proje tamamlanışında ETA'yı yeniden hesapla
        RefreshRunSurface();
    }

    private ProjectRowViewModel EnsureRow(string id, string name, ProjectRowState initialState)
    {
        var existing = Projects.FirstOrDefault(p => p.Id == id);
        if (existing is not null) return existing;
        var row = new ProjectRowViewModel(id, name, initialState) { IsRunActive = RunActive };
        Projects.Add(row);
        return row;
    }

    /// <summary>
    /// [Task 17][T70/A6-Δ8] <see cref="EtaCalculator"/>'ı bu run'ın GÖZLEMLENEN (Succeeded/Failed) süreleriyle
    /// besler — App'te <c>BuildState.LastDurationMs</c> (Core/Supervisor tarafı geçmiş) YOK, bu yüzden kasıtlı
    /// basitleştirme: hem queued (henüz hiç başlamamış) hem building projeler için tahmin kaynağı, bu run
    /// içinde ŞİMDİYE KADAR tamamlanmış projelerin süre ORTALAMASIdır (EtaCalculator'ın kendi "bilinmeyen süre"
    /// ortalama-fallback'i zaten bunu queued/building arasında ayrıca uygular — burada yalnız per-proje "bilinen
    /// süre" KAYNAĞI, engine'in kalıcı geçmişi yerine run-içi gözlemdir). Building'in elapsed'i
    /// <see cref="_projectStartedAtMs"/> + enjekte edilen <see cref="_nowMs"/> ile ölçülür (D8: sleep/poll yok,
    /// testte deterministik saat).
    /// </summary>
    private void UpdateEta()
    {
        int total = _totalProjects ?? Projects.Count;
        if (total <= 0) { EtaText = ""; return; }

        int completed = Projects.Count(p => p.State is ProjectRowState.Succeeded or ProjectRowState.Failed or ProjectRowState.Skipped);
        var buildingRows = Projects.Where(p => p.State == ProjectRowState.Started).ToList();
        int remaining = Math.Max(0, total - completed);
        int queuedCount = Math.Max(0, remaining - buildingRows.Count);

        var knownDurations = Projects
            .Where(p => p.State is ProjectRowState.Succeeded or ProjectRowState.Failed)
            .Select(p => p.DurationMs)
            .ToList();
        long? observedAverageMs = knownDurations.Count > 0 ? (long)knownDurations.Average() : null;

        var queuedEstimatesMs = Enumerable.Repeat(observedAverageMs, queuedCount).ToList();
        long now = _nowMs();
        var building = buildingRows
            .Select(p => new EtaCalculator.BuildingProject(
                ElapsedMs: _projectStartedAtMs.TryGetValue(p.Id, out long startedAt) ? Math.Max(0, now - startedAt) : 0,
                LastDurationMs: observedAverageMs))
            .ToList();

        long? rawEstimateMs = EtaCalculator.ComputeRawEstimateMs(queuedEstimatesMs, building, _runParallelism ?? Parallelism);
        long? smoothedEtaMs = rawEstimateMs is { } raw ? EtaCalculator.Smooth(_previousEtaMs, raw) : null;
        _previousEtaMs = smoothedEtaMs;
        EtaMs = smoothedEtaMs; // [D2/T70] şeridin numeric ETA kaynağı (RibbonText.EtaSuffix)
        EtaText = EtaCalculator.FormatDisplay(smoothedEtaMs, completed, total, ElapsedMs);
    }

    private void OnRunCompleted(RunCompletedEvent e)
    {
        ElapsedMs = e.DurationMs; // yerel Stopwatch'tan değil, engine'in kesin süresinden — clock drift yok
        IsRunning = false;
        Phase = e.Outcome == RunOutcome.Stopped ? AppPhase.Stopped : AppPhase.Done; // [C2] Running → Done/Stopped
        CanContinue = e.Outcome == RunOutcome.Stopped;
        DepIssueCount = e.DepIssueCount; // [Task 17] run genelinde (Continue segmentleri dahil) kümülatif özet
        _sawRunStarted = false;
        RefreshRunSurface();
    }

    private void OnRunStopped()
    {
        if (_sawRunStarted) return; // normal akış: runCompleted az sonra gelecek, slot orada serbest kalır
        // [Kısıt 3] Planlama sırasında stop — runStarted hiç gelmedi, runCompleted de ASLA gelmeyecek.
        IsRunning = false;
        IsStarting = false; // [Fix wave 1(It-3), Finding 3] planlama-sırasında-stop ack'i — Rebuild'i geri aç
        CanContinue = false;
    }

    private void OnError(ErrorEvent e)
    {
        AppendRunLine($"[hata] {e.Code}: {e.Message}");
        // [Fix wave 1(It-3), Finding 2] logNotFound (ör. Skipped/cycle üyesi proje kartına tıklama — hiç log
        // dosyası yok) OnError'da hiç ele alınmıyordu: bekleyen LoadProjectLogAsync'in Completion'ı asla
        // tamamlanmaz, await SONSUZA DEK asılı kalırdı. ErrorEvent'te ProjectId yok (kontrat sabit) — eldeki
        // TEK bekleyen yüklemeyi (varsa) burada çözüyoruz; proje modu hiç kurulmadığından ActiveProjectId
        // dokunulmadan kalır (run dokümanı gösterilmeye devam eder).
        if (e.Code == "logNotFound" && _pendingLoad is { } pending)
        {
            _pendingLoad = null;
            pending.Completion.TrySetResult();
        }
        if (!RunEndingErrorCodes.Contains(e.Code)) return; // runInProgress/logNotFound/... aktif run'ı ETKİLEMEZ
        // [A5/T69 · Fix wave 1, Finding 2] Sync fazını bırakır ve hatanın KAYNAĞINI ayırt eder: kod Sync'ten
        // geldiyse (uçuşta bir Sync var ve run planlama penceresinde DEĞİL) run state'ine DOKUNULMAZ — Sync
        // salt-okurdur ve koşan bir run sırasında da tetiklenebilir. Gerekçe: RunViewModel.Workspace.cs.
        if (TryConsumeSyncFailure(e.Code)) return;
        IsRunning = false;
        IsStarting = false; // [Fix wave 1(It-3), Finding 3] planFailed/msbuildNotFound/noResumableRun — Rebuild'i geri aç
        CanContinue = false;
        _sawRunStarted = false;
    }

    /// <summary>[Task 16 — It-2 devir §8, kama düzeltmesi] <see cref="EngineHost.EngineExited"/> eskiden VM'e
    /// hiç BAĞLI DEĞİLDİ: engine process startRun sonrası runStarted'dan ÖNCE ya da run ORTASINDA ölürse,
    /// hiçbir IPC event'i asla gelmeyeceğinden (ne runCompleted ne runStopped ne run-bitiren ErrorEvent)
    /// IsStarting/IsRunning/CanContinue SONSUZA DEK kilitli kalırdı — "Restart Engine" MainWindow'daki
    /// banner'ı güncelliyordu ama VM'e hiç dokunmadığından butonlar açılmıyordu. <see cref="RunEndingErrorCodes"/>
    /// deseniyle TUTARLI: aynı üç run-state alanı sıfırlanır; ayrıca bir sonraki run/Restart'ın _currentRunId/
    /// _sawRunStarted bakiyesiyle karışmaması için o bakiye de temizlenir (StopAsync zaten CanStop=false
    /// olduğundan tıklanamaz, ama temiz başlangıç için bilerek sıfırlanır).
    ///
    /// <para><b>Idempotent:</b> [ObservableProperty] setter'ları CommunityToolkit'in eşitlik kontrolüyle
    /// çalışır (false→false / null→null hiçbir PropertyChanged/CanExecuteChanged YAYINLAMAZ) — bu yüzden
    /// hiçbir run aktif değilken (zaten temiz durum) ya da normal <c>runCompleted</c> SONRASI çağrılırsa
    /// no-op'tur, ayrı bir guard GEREKMEZ.</para>
    ///
    /// <para><b>Thread/marshal:</b> <see cref="EngineHost.EngineExited"/> arka plan thread'inde (exit-watcher
    /// ya da framing-hatası dalı) ateşlenir; bu metot ObservableProperty/CanExecuteChanged'a dokunduğundan
    /// <see cref="OnEvent"/>'in Dispatcher-gerektiren dalları GİBİ UI thread'ine marshal edilerek çağrılmalıdır
    /// — çağıran (MainWindow) bu sorumluluğu taşır, VM'in kendisi Dispatcher TÜRÜ TAŞIMAZ (test edilebilirlik).</para></summary>
    public void OnEngineExited(int? exitCode)
    {
        EngineDiedMessage = exitCode is { } code
            ? $"engine öldü (exit {code})"
            : "engine öldü (framing hatası)";
        IsRunning = false;
        IsStarting = false;
        CanContinue = false;
        _sawRunStarted = false;
        _currentRunId = null;
        // [C2 fold — A5 review] Engine Sync ortasında ölürse hiçbir syncCompleted/Sync-hatası gelmez; faz
        // Syncing'de asılı kalır ve _syncInFlight sızardı. RunEndingErrorCodes deseniyle simetrik olarak burada
        // da uçuştaki Sync serbest bırakılır.
        ReleaseSyncPhase();
    }

    // ---------------------------------------------------------------- konsol/log

    /// <summary>[A13.2] MainWindow bu event'i MARSHAL ETMEDEN doğrudan arka plan (IPC okuma) thread'inden
    /// çağırır — bu yüzden burada YALNIZ thread-safe işlemler yapılır: kilitli arabellek yazımı +
    /// <see cref="ConsoleBatcher.Post"/> (kilitsiz, ama artık AYNI kilit altında — bkz. Fix wave 1, Finding 3).
    /// ObservableProperty/ObservableCollection'a ASLA dokunulmaz.</summary>
    private void OnProjectLog(ProjectLogEvent e)
    {
        lock (_gate)
        {
            if (!_liveLines.TryGetValue(e.ProjectId, out var list))
                _liveLines[e.ProjectId] = list = [];
            list.Add(e);

            // Run dokümanı proje modunda bile birikmeye devam eder — ekranda görünmese de.
            _runText.Append(e.Text).Append('\n');
            _runLineCount++;
            if (string.Equals(ActiveProjectId, e.ProjectId, StringComparison.OrdinalIgnoreCase))
                AppendProjectTextLocked(e.ProjectId, e.Text);

            // [Fix wave 1, Finding 3] Post ARTIK AYNI kilit altında: eskiden kilit DIŞINDaydı, bu da
            // "buffer'a yazıldı ama kanala henüz post edilmedi" aralığını SeedRunDocument/SeedProjectDocument'ın
            // (kendi _gate kilidiyle) atomik biçimde kapatmasını engelliyordu (mod değişiminde kopya satır —
            // bkz. task-12-report.md Fix wave 1). Post kilitsiz/hızlı (Channel.Writer.TryWrite) olduğundan
            // kilidi gereksiz uzatmaz.
            if (ActiveProjectId is null || string.Equals(ActiveProjectId, e.ProjectId, StringComparison.OrdinalIgnoreCase))
                _console.Post(e.Text);
        }
    }

    private void AppendProjectTextLocked(string projectId, string text)
    {
        if (!_projectText.TryGetValue(projectId, out var sb))
            _projectText[projectId] = sb = new StringBuilder();
        sb.Append(text).Append('\n');
        _projectLineCount[projectId] = (_projectLineCount.TryGetValue(projectId, out var n) ? n : 0) + 1;
    }

    private void AppendRunLine(string text)
    {
        // [Fix wave 1, Finding 3] OnProjectLog ile aynı gerekçeyle Post kilit İÇİNE alındı.
        lock (_gate)
        {
            _runText.Append(text).Append('\n');
            _runLineCount++;
            if (ActiveProjectId is null) _console.Post(text);
        }
    }

    /// <summary>[T56/3a] Konsol başlığındaki "N lines" için AKTİF tampon (run ya da seçili proje) satır sayısı —
    /// TAM tampon uzunluğu (render dilimi DEĞİL, Ek A #23). UI thread'inde çağrılır; sayaçlar arka plandan
    /// (marshal-free OnProjectLog) yazıldığından okuma _gate altındadır.</summary>
    public int GetActiveLineCount()
    {
        lock (_gate)
            return ActiveProjectId is null
                ? _runLineCount
                : _projectLineCount.TryGetValue(ActiveProjectId, out var n) ? n : 0;
    }

    private static int CountLines(StringBuilder sb)
    {
        int n = 0;
        for (int i = 0; i < sb.Length; i++)
            if (sb[i] == '\n') n++;
        return n;
    }

    public string GetRunDocumentText() { lock (_gate) return _runText.ToString(); }
    public string GetProjectDocumentText(string projectId)
    {
        lock (_gate) return _projectText.TryGetValue(projectId, out var sb) ? sb.ToString() : "";
    }

    /// <summary>[Fix wave 1, Finding 3 → 3b: tek-okuyucudan geçen reseed] MainWindow'un "Back" akışının
    /// kullandığı tohumlama metodu: run dokümanının metnini AYNI _gate kilidi altında okur ve
    /// <see cref="ConsoleBatcher.PostReseed"/> ile kanala bir reseed sentinel'i yazar. <paramref name="apply"/>,
    /// pump tarafından (sentinel'e uğrayınca) çağrılır ve TAZE snapshot'ı UI dokümanına kurar (marshal ETME
    /// çağıranın işi).
    ///
    /// <para><b>Neden kilit altında snapshot + PostReseed birlikte:</b> OnProjectLog da _runText yazımını ve
    /// <c>_console.Post</c>'unu AYNI _gate altında yapar. Bu yüzden snapshot okunurken bir OnProjectLog araya
    /// giremez: snapshot'a giren her satır, sentinel'den ÖNCE kanala girmiştir; snapshot'a girmeyen her satır
    /// sentinel'den SONRA girer. Pump tek okuyucudur → sentinel'e kadarki satırları (snapshot'ta zaten var) atar,
    /// sonrakileri yeni dokümana akıtır. Eski <c>DiscardPending</c>'in bıraktığı "pump satırı TryRead'le çekti ama
    /// henüz flush etmedi" Dispatcher-gecikmesi artık penceresi KAPANIR (It-4 backlog kalemi — task-12 Fix wave 1
    /// residual'ı).</para></summary>
    public void SeedRunDocument(Action<string> apply)
    {
        lock (_gate)
            _console.PostReseed(_runText.ToString(), apply);
    }

    /// <summary>[3b] Proje kartına tıklama akışının tohumlama metodu — bkz. <see cref="SeedRunDocument"/>'ın XML
    /// yorumu (aynı gerekçe, proje dokümanı için). Log yoksa boş snapshot ile çağrılır (çağıran boş-durum metnini
    /// uygular).</summary>
    public void SeedProjectDocument(string projectId, Action<string> apply)
    {
        lock (_gate)
            _console.PostReseed(_projectText.TryGetValue(projectId, out var sb) ? sb.ToString() : "", apply);
    }

    /// <summary>Konsolu run dokümanına döndürür (MainWindow'daki "Back").</summary>
    public void ShowRun() => ActiveProjectId = null;

    /// <summary>
    /// [T28 dikişi] <c>getProjectLog</c> gönderir; gelen chunk'lar sırayla biriktirilir. SON chunk'ta
    /// (<c>IsLast</c>) o ana kadar tamponlanmış canlı <c>projectLog</c> satırlarından yalnız
    /// <c>LineNumber &gt; ThroughLineNumber</c> olanlar (tekrar YOK, kayıp YOK) chunk geçmişinin ardına
    /// eklenir ve konsol proje moduna geçer. SendAsync engine hazır değilken senkron fırlarsa yutulur — UI
    /// tıklaması çökmemeli; dikiş yine de tamamen yerel arabellekten üretilebilir.
    /// </summary>
    public async Task LoadProjectLogAsync(string projectId)
    {
        // [Fix wave 1(It-3), Finding 2] Yeni bir yükleme, henüz tamamlanmamış eski bir _pendingLoad'ın yerini
        // alırsa eskisini burada çözüyoruz — aksi halde eski awaiter'ın Completion'ı ASLA tamamlanmaz (leak).
        _pendingLoad?.Completion.TrySetResult();
        var pending = new PendingLoad(projectId);
        _pendingLoad = pending;
        try { await _engine.SendAsync(new GetProjectLogCommand(projectId)); }
        catch (Exception ex)
        {
            AppendRunLine($"[hata] proje logu istenemedi: {ex.Message}");
            // [Fix wave 2, Finding 2] SendAsync engine ölüyken/hiç başlamamışken SENKRON fırlar (writer null) —
            // bu catch `await` HİÇBİR suspension olmadan senkron çalışır. Önceden Completion burada asla
            // tamamlanmıyordu: hiçbir yanıt/event gelmeyeceğinden aşağıdaki `await pending.Completion.Task`
            // SONSUZA DEK asılı kalırdı (kart tıklaması hang). BİLEREK `_pendingLoad` null'LANMIYOR (OnError'ın
            // logNotFound dalının aksine): mevcut testler (ör. stitch testleri) engine hiç başlatılmadan aynı
            // senkron fırlamaya dayanır ve sonrasında gelen bir ProjectLogChunkEvent'in _pendingLoad üzerinden
            // OnProjectLogChunk'ta hâlâ eşleşip dikişi tamamlamasını bekler — burada null'lamak o akışı kırardı.
            pending.Completion.TrySetResult();
        }
        await pending.Completion.Task;
    }

    /// <summary>[Kısıt 4] MainWindow bu event'i (diğer tüm state event'leri gibi) UI thread'ine MARSHAL EDER —
    /// hem <see cref="ActiveProjectId"/> (WPF binding'e bağlı) yazdığı için, hem de proje başına yalnız birkaç
    /// chunk geldiğinden (LogChunker parça sayısı) marshal maliyeti A13.2'nin önlemeye çalıştığı "satır başına
    /// Dispatcher" akışıyla KIYASLANAMAZ ölçüde küçüktür.</summary>
    private void OnProjectLogChunk(ProjectLogChunkEvent e)
    {
        if (_pendingLoad is not { } pending || !string.Equals(pending.ProjectId, e.ProjectId, StringComparison.OrdinalIgnoreCase))
            return; // bekleyen bir yükleme yok ya da başka bir projeye ait gecikmiş chunk — yok say
        pending.Assembly.Append(e.Text);
        if (!e.IsLast) return;

        // [Fix wave 1, Finding 2] ActiveProjectId ataması dikiş snapshot'ıyla AYNI kilit altında olmalı:
        // aksi halde kilit kapandıktan (_projectText yazıldıktan) ama ActiveProjectId GÜNCELLENMEDEN önceki
        // dar aralıkta arka plandan gelen bir OnProjectLog, _liveLines'a eklenir AMA (ActiveProjectId hâlâ
        // eski değeri taşıdığından) _projectText'e YAZILMAZ — snapshot da o satırı zaten kapatmış olur; satır
        // kalıcı olarak kaybolur. Atama kilit içine alınınca OnProjectLog (kendi _gate kilidiyle) ya bu
        // bloktan ÖNCE (satır snapshot'ta) ya da SONRA (ActiveProjectId zaten güncel, canlı ekleme yapar)
        // çalışır — üçüncü bir aralık yok.
        lock (_gate)
        {
            var stitched = new StringBuilder(pending.Assembly.ToString());
            if (_liveLines.TryGetValue(e.ProjectId, out var buffered))
                foreach (var line in buffered.Where(l => l.LineNumber > e.ThroughLineNumber).OrderBy(l => l.LineNumber))
                    stitched.Append(line.Text).Append('\n');
            _projectText[e.ProjectId] = stitched;
            _projectLineCount[e.ProjectId] = CountLines(stitched); // [T56/3a] dikilmiş tam log satır sayısı
            ActiveProjectId = e.ProjectId;
        }
        DebugAfterStitchLockExited?.Invoke(); // yalnız testler ayarlar — bkz. alan tanımı
        _pendingLoad = null;
        pending.Completion.TrySetResult();
    }

    private sealed class PendingLoad(string projectId)
    {
        public string ProjectId { get; } = projectId;
        public StringBuilder Assembly { get; } = new();
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
