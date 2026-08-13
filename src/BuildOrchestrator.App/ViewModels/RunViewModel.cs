using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Formatting;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Scheduling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
// [T20-b] VM'in KENDİ `PerfMode` string property'si, Core'daki aynı adlı enum'u basit-ad çözümlemesinde gölgeler
// (sınıf üyesi, namespace'ten gelen türü yener) — bu yüzden enum'a bu alias'la erişilir.
using CorePerfMode = BuildOrchestrator.Core.ProcessControl.PerfMode;

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

    /// <summary>[T53-UI][W1/It-5] SHA çiftinin sol yarısı: projenin SON BAŞARIYLA DERLENDİĞİ commit — prototip
    /// <c>st.curSha</c> (BuildApp.jsx:400). Kaynak <see cref="BuildPreviewItem.BuiltCommit"/>'tir (yani
    /// <c>BuildState.BuiltCommit</c>); hem Sync hem run-başı önizlemesinden gelir. Değer HAM'dır (40-hex) —
    /// 7 haneye kısaltma bir GÖRÜNTÜ kararıdır ve kartta (<c>ProjectRow.ApplySha</c>) yapılır. <b>Hiç
    /// derlenmemiş</b> proje ⇒ <c>null</c> (uydurulmaz): kart o satırda çift yerine YALNIZ hedefi basar.
    /// Kart yalnız <see cref="WillBuild"/>==true iken bu slotu gösterir.</summary>
    [ObservableProperty] private string? _currentSha;

    /// <summary>[W1/It-5] SHA çiftinin sağ yarısı: run-geneli hedef commit (<c>SyncCompletedEvent.TargetSha</c>),
    /// <see cref="RunViewModel.TargetSha"/>'dan her satıra İTİLİR (<see cref="IsRunActive"/>/<see cref="NamePrefix"/>
    /// deseni). <b>Neden satırda:</b> kart bunu eskiden render anında ata ağaçtaki <see cref="RunViewModel"/>'den
    /// ÇEKİYORDU; <c>buildPreview</c> deterministik olarak <c>syncCompleted</c>'dan ÖNCE geldiği için satır
    /// sha'sını TargetSha daha null'ken hesaplıyor ve bir daha tazelenmiyordu (ilk Sync'ten sonra slot boş
    /// kalırdı). Değer artık İTİLDİĞİ için iki event'in sırası ÖNEMSİZDİR — hangisi sonra gelirse satır kendi
    /// PropertyChanged'i üzerinden tazelenir (satır başına EK abone YOK). Değer HAM'dır (40-hex).</summary>
    [ObservableProperty] private string? _targetSha;

    /// <summary>[T53-UI · C1 debt] Satır seçili mi — <see cref="RunViewModel.SelectedProjectId"/> değiştiğinde
    /// (<see cref="RunViewModel.OnSelectedProjectIdChanged"/>) tüm satırlar için tazelenir. Kart bunu şerit
    /// genişliği (2→3), iç sarmalayıcı <c>TranslateX 4</c> ve <c>Brush.SurfaceRaised</c> zemini için okur.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>[D5] Kısa-ad öneki (ör. <c>"OSYS."</c>) — dep-issue tooltip'i tam proje adlarını gösterirken bu
    /// öneki atar. HARDCODE DEĞİL: <see cref="RunViewModel"/> topoloji adlarından türetip (tek otorite,
    /// <see cref="Graph.GraphNode.CommonDotPrefix"/>) her satıra iter (<see cref="IsRunActive"/> deseni). Önek
    /// yoksa boş — kırpma yapılmaz.</summary>
    [ObservableProperty] private string _namePrefix = "";

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

    /// <summary>[cycle rounds/Task 8] Bu satır bir SCC üyesidir ve grup TUR TAVANINA dayanarak bitti (iki
    /// ardışık yeşil tur hiç olmadı) — <see cref="ProjectSucceededEvent.CycleUnsettled"/>'tan AYNEN taşınır.
    /// Derleme başarılı ama çıktı bir kuşak geride OLABİLİR. RENDER Task 9'undur — burası yalnız veri taşır.</summary>
    [ObservableProperty] private bool _cycleUnsettled;

    /// <summary>[cycle rounds/I2] Bu satır bir SCC üyesidir, grubu ŞU AN koşuyor ama SIRASI KENDİSİNDE DEĞİL:
    /// motor grubun üyelerini sıralı invoke eder ve ara tur sonuçlarını yayınlamaz, bu yüzden üye grubun tüm
    /// ömrü boyunca <see cref="ProjectRowState.Started"/>'ta kalır — o an gerçekten derlenen tek üye, grubun EN
    /// SON <c>projectStarted</c> alanıdır.
    /// <para><b>[DEĞİŞEN KURAL]</b> Bayrak önce yalnız <see cref="RunCounters"/>'ın <c>Building</c>'ini
    /// sınırlıyordu ve <see cref="Status"/>'a BİLEREK dokunmuyordu. Ölçülen sonuç: 15 üyeli bir grupta listede
    /// 15, grafta 15 dönen spinner — sayaç chip'i "1 building" derken. Ekran, aracın aynı anda on beş iş
    /// yaptığını söylüyordu; bir iş yapıyordu. Bayrak artık <see cref="Status"/>'u da sürer, böylece satır ile
    /// sayaç AYNI soruyu aynı şekilde cevaplar (tek kural, iki tüketici).</para></summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(IsCompiling))]
    private bool _cycleWaiting;

    /// <summary>
    /// Bu satır ŞU AN gerçekten derleniyor mu. <b>"Motor durumu <see cref="ProjectRowState.Started"/>" ile
    /// AYNI ŞEY DEĞİLDİR</b>: bir SCC'nin üyeleri tek tek invoke edilir ve ara tur sonuçları yayılmadığı için
    /// grup bitene kadar HEPSİ Started'ta kalır (bkz. <see cref="CycleWaiting"/>).
    ///
    /// <para>Predicate TEK yerdedir ve ALTI yüzey onu okur: <see cref="Status"/>'un Building dalı,
    /// <see cref="RunCounters"/>'ın Building kovası, sticky şeridin building chip'leri, kartın nefes katmanı,
    /// canlı süre sütunu (<c>ProjectRow.ApplyBreathing</c>/<c>ApplyDuration</c>) ve listenin frontier takibi
    /// (<c>MainWindow.FollowFrontier</c>). Yüzeyler ayrı yazıldığında
    /// sessizce ayrıştılar ve ölçüldü: 15 üyeli bir grupta listede 15 spinner, şeritte 4 chip + "+11", sayaçta
    /// "1 building" — aynı anda; sonra da bekleyen üye saat gösterirken nefes alıp süre sayıyordu. Yeni bir
    /// tüketici de buradan okumalıdır.</para></summary>
    public bool IsCompiling => State == ProjectRowState.Started && !CycleWaiting;

    /// <summary>[design v1.7.0 §2.4] Bu satırın döngüsünün YOLU (<c>A → B → C → A</c>) — üye değilse boş.
    /// Metin <see cref="CycleText.Path"/>'ten gelir ve satıra topolojiyle birlikte itilir; nokta ile uyarı
    /// üçgeni onu buradan okur (iki yüzey kendi yolunu KURMAZ).</summary>
    [ObservableProperty] private string _cyclePath = "";

    /// <summary>[cycle rounds/Task 8] Bu satır bir SCC üyesidir ve grup ÖNCEKİ bir Build'de yakınsamadığı için
    /// bu run'da hiç invoke edilmeden pre-skip edildi — <see cref="ProjectSkippedEvent.CycleUnconverged"/>'tan
    /// AYNEN taşınır. Kalıcı kırık bir döngü, sıradan "güncel" skip'iyle karışmasın diye ayrı bir alandır
    /// (<see cref="Status"/> bunu OKUMAZ — ikisi de motor tarafında <c>Skipped</c>'tır). RENDER Task 9'undur.</summary>
    [ObservableProperty] private bool _cycleUnconverged;

    /// <summary>[Fix wave 1 · D1 review Finding 1] Satırın GÖRSEL statüsü — <c>ProjectRowState</c> (motor durumu) +
    /// <see cref="InCycle"/> + <see cref="WillBuild"/> + <see cref="IsRunActive"/> sinyallerinin TEK eşleme yeri
    /// (kart yalnız bunu okur; eşleme mantığı kontrolde kopyalanmaz). <c>cycle</c> ve <c>queued</c> ayrı IPC
    /// alanları TAŞIMAZ — ikisi de eldeki topoloji/run sinyallerinden TÜRETİLİR:
    /// <list type="bullet">
    /// <item><b>cycle</b>: <see cref="InCycle"/>=true olan, bu koşu hakkında HENÜZ BİR ŞEY SÖYLENMEMİŞ satır.
    /// Bkz. aşağıdaki "döngü glyph'i koşu-öncesidir" notu.</item>
    /// <item><b>queued</b>: bir run uçuştayken (<see cref="IsRunActive"/>) planlanmış (<see cref="WillBuild"/>==true)
    /// ama henüz başlamamış (Pending) satır. Run bitince <see cref="IsRunActive"/> düşer → yine Discovered.</item>
    /// </list>
    ///
    /// <para><b>Döngü glyph'i bir KOŞU-ÖNCESİ ifadedir.</b> Motor bu satır hakkında konuştuğu anda —
    /// başladı, bitti, atlandı ya da bu koşuda derlenmek üzere planlandı — statü glyph'i MOTORUN cevabını
    /// gösterir, döngü üyeliğini değil; üyelik dep-slotundaki turuncu rozete taşınır (<c>ProjectRow.ApplyDep</c>).
    /// Sonuç: soldaki ikon "bu koşuda ne oldu", sağdaki rozet "bu proje bir döngüde" der ve ikisi birbirini
    /// gizlemez.</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> <see cref="InCycle"/> eskiden <c>Skipped</c> alt-durumunu da EZİYORDU. Bunun
    /// bedeli ölçüldü: bir Build'den sonra döngüdeki her satır Sync'ten hemen sonraki hâliyle BİREBİR aynı
    /// görünüyordu, yani "bu koşu onları atladı" ile "bunlar bir döngüde" ayırt edilemiyordu — ve döngüleri
    /// gerçekten derleyen koşu (<c>RunMode.Cycles</c>) geldiğinde aynı satırlar sonuçlarını da gizlerdi.</para></summary>
    public Controls.GraphStatus Status => State switch
    {
        // Grubu koşuyor ama SIRASI kendisinde değil: gerçekten derlenen tek üye vardır (bkz. IsCompiling).
        ProjectRowState.Started => IsCompiling ? Controls.GraphStatus.Building : Controls.GraphStatus.Queued,
        ProjectRowState.Succeeded => Controls.GraphStatus.Succeeded,
        ProjectRowState.Failed => Controls.GraphStatus.Failed,
        ProjectRowState.Skipped => Controls.GraphStatus.Skipped,
        // Buradan aşağısı YALNIZ Pending'dir: koşu bu satırı planladıysa kuyruk, planlamadıysa (ya da koşu
        // yoksa) döngü üyeliği — o da yoksa ölü envanter.
        _ when IsRunActive && WillBuild == true => Controls.GraphStatus.Queued,
        // [design v1.7.0 §5] Döngü ÜYELİĞİ bir statü DEĞİLDİR: kalıcı bir yapısal özelliktir ve kendi
        // kanalında (nokta + uyarı üçgeni + graf çekirdeği) yaşar. Statü kanalı yalnız "bu koşuda ne oldu"yu
        // söyler; üyelik onu asla ezmez — eskiden Pending bir üye Cycle statüsüne düşüyor ve satır
        // "derlenmedi mi, atlandı mı, hiç görülmedi mi" sorusuna cevap veremiyordu.
        _ => Controls.GraphStatus.Discovered,
    };

    public ProjectRowViewModel(string id, string name, ProjectRowState state, string? solutionName = null)
    {
        Id = id;
        Name = name;
        _state = state;
        _solutionName = solutionName;
    }
}

public enum ProjectRowState { Pending, Started, Succeeded, Failed, Skipped }

/// <summary>[D4 review §3] Kart seçimi değişiminde konsolun izleyeceği aksiyon (<see cref="RunViewModel.NextConsoleSelection"/>
/// kararı) — MainWindow yalnız uygular.</summary>
public enum ConsoleSelection { ShowRun, LoadProjectLog }

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
    // beklememeli [Kısıt 3]: planFailed/msbuildNotFound/runFailed.
    // [Fix wave 3] runFailed: RunCoordinator.ExecuteRunAsync'in dış catch'i planlama SIRASINDA (runStarted'dan
    // ÖNCE) beklenmedik bir istisnada da bu kodu yayınlar — eklenmezse IsStarting kalıcı true kalır (aynı
    // wedge sınıfı, farklı tetikleyici). Küme BİLEREK genişletilmedi (ör. "tanınmayan her kod run-ending"
    // yapılmadı): badCommand/unknownCommand gibi run'ı bitirmeyen per-command hatalar da vardır.
    // [B1] Run-bitiren kod DEĞİL (koşan run'ı yıkmaz) ama reddedilen isteğin "starting" bayrağını bırakır.
    private const string RunInProgressCode = "runInProgress";

    private static readonly HashSet<string> RunEndingErrorCodes =
        new(StringComparer.Ordinal) { "planFailed", "msbuildNotFound", "runFailed" };

    private readonly EngineHost _engine;
    private readonly ConsoleBatcher _console;
    private readonly Func<string> _newRunId;
    private readonly Func<long> _nowMs; // [Minor/Fix wave 1] elapsed hesap kaynağı — testte deterministik saat enjekte edilir (D8)
    private readonly Services.IOsActions? _osActions; // [E1/T67] satır hover ikonlarının OS eylemleri (Reveal/Open-in-VS); üretimde daima enjekte, testte null default = güvenli no-op

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

    // [cycle rounds/I2] SCC üyelik haritası — topolojiden (WorkspaceTopologyEvent.Cycles) kurulur, Core'un
    // AYNI gövdesiyle (CycleGroups) çünkü motor da grubu ondan sürer. Tek tüketicisi "bu Started üye grubunun
    // SIRASINI mı bekliyor" sorusudur: üyeler sıralı invoke edildiği için grubun EN SON projectStarted alanı
    // dışındaki her üyesi beklemededir. Topoloji hiç gelmediyse null — o hâlde InCycle satır da yoktur.
    private CycleGroups? _cycleGroups;

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
    // [T20-b] O sabit artık Core'un PerfProfile tablosudur (App'in kendi kopyası KALDIRILDI).
    [ObservableProperty] private int _parallelism = ProfileFor(DefaultPerfMode).Parallelism;
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
    // sorgulanmaz (StopCommand hep disabled kalırdı) — Kısıt 3'ü bozar.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCyclesCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(BuildCyclesCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(IsMidRunLocked))]
    private bool _isStarting;

    [ObservableProperty] private string? _activeProjectId; // null = run dokümanı gösteriliyor

    /// <summary>[Task 16 — It-2 devir §8] Engine process öldüğünde (<see cref="OnEngineExited"/>) kullanıcıya
    /// gösterilecek metin — sticky şerit kalıcı hata modunun PIXEL karşılığı It-4'te; burada yalnız VM-state.
    /// [Review fix] Kalıcı DEĞİLDİR: bir sonraki run'ın <see cref="OnRunStarted"/>'ı (engine'in CANLI ve IPC
    /// round-trip yaptığının ilk somut kanıtı) bu mesajı temizler — aksi halde tek bir ölümden sonra sonsuza
    /// dek stale kalıp, tamamen başarılı sonraki run'larda bile güncel engine sağlığını yanlış yansıtırdı.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEngineUnavailable))]
    [NotifyCanExecuteChangedFor(nameof(RebuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCyclesCommand))]
    private string? _engineDiedMessage;

    /// <summary>[D1] Şeridin kalıcı hata modundaki "Restart engine" aksiyonu ANLAMLI mı? Normal bir motor ölümü
    /// yeniden başlatılabilir (true); Supervisor çıktısı hiç bulunamadığında (<see cref="OnEngineUnavailable"/>)
    /// yeniden başlatmak eksik dosyayı geri getirmeyeceği için aksiyon GİZLENİR ve kullanıcı yalnız ne yapması
    /// gerektiğini anlatan metni görür. <b>Değişmez:</b> <see cref="EngineDiedMessage"/>'ı yazan HER yol bunu da
    /// yazar (ölüm → true, kurulum eksik → false); mesaj temizlendiğinde değerin önemi kalmaz.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEngineUnavailable))]
    [NotifyCanExecuteChangedFor(nameof(RebuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCyclesCommand))]
    private bool _engineRestartable = true;

    /// <summary>[D1 review · A3] Motor ERİŞİLEMEZ: hiç doğamadı (supervisor yok ya da başlatılamıyor) —
    /// <see cref="OnEngineUnavailable"/> bu durumu kurar. Sync/Build/Rebuild/Retry/Continue bu durumda
    /// ANLAMSIZDIR: gönderim zaten hataya düşer ve şeritteki kalıcı mesajla ÇELİŞEN ikinci bir hata satırı
    /// üretirdi — bu yüzden komutlar devre dışıdır ("Restart engine"in gizlenmesiyle aynı mantık).
    /// <para>Normal (doğmuş) motor ölümü BU DURUM DEĞİLDİR: orada "Restart engine" sunulur ve komutlar açık
    /// kalır — E2/T37 davranışı korunur.</para></summary>
    public bool IsEngineUnavailable => EngineDiedMessage is { Length: > 0 } && !EngineRestartable;

    /// <summary>[D1] Supervisor çıktısı uygulamanın yanında bulunamadığında şeritte gösterilen KALICI satır.
    /// design-v1 §"Ton" (sakin, kesin, mühendisçe; ünlem yok) ve mevcut hata satırlarının em-dash/`·` dili.
    /// Ham exception dump'ı DEĞİL — tam yol konsol anlatısına düşer.</summary>
    public const string EngineMissingMessage =
        "Engine missing — supervisor was not found next to the app · reinstall required";

    /// <summary>[D1 review · A2] Supervisor dosyası VAR ama başlatılamadı (bozuk/geçersiz exe, erişim reddi,
    /// TOCTOU). Nedeni <see cref="EngineMissingMessage"/>'dan AYIRT EDER; ham exception metni gösterilmez.</summary>
    public const string EngineCannotStartMessage =
        "Engine could not start — the supervisor next to the app would not launch · reinstall required";

    /// <summary>Motorun bir GEÇİŞ beklenirken susabileceği en uzun süre. Aşılırsa kullanıcıya çıkış kapısı
    /// gösterilir (<see cref="EngineOverdueMessage"/> + şeritteki "Restart engine").
    /// <para><b>Neden bu kadar uzun:</b> planlama 177 projelik bir workspace'te on saniyelerce sürebilir ve
    /// graceful drain uçuştaki en uzun <c>MSBuild.exe</c> kadar sürer; ikisi de MEŞRUdur. Eşik "yavaş"ı
    /// değil "hiç konuşmuyor"u yakalamalıdır — motorun HERHANGİ bir event'i saati sıfırlar
    /// (<see cref="OnEvent"/>), yani bu süre boyunca TEK satır bile gelmemiş olması gerekir.</para></summary>
    internal const long EngineSilenceThresholdMs = 90_000;

    /// <summary>Motor bir geçiş beklenirken <see cref="EngineSilenceThresholdMs"/> boyunca hiç konuşmadı.
    /// Şerit bunu AMBER gösterir (kırmızı değil: bu bir başarısızlık değil, bir bekleyiştir) ve "Restart
    /// engine" aksiyonunu açar.
    ///
    /// <para><b>Neden gerekiyor:</b> ölçülen üretim vakasında motor planlamanın ortasında dondu — App
    /// <c>IsStarting</c>'te, ardından <c>Stopping</c>'te SONSUZA DEK kilitli kaldı ve tek çıkış uygulamayı
    /// kapatmaktı. "Restart engine" aksiyonu ZATEN vardı ama görünürlüğü <see cref="EngineDiedMessage"/>'a,
    /// yani process'in GERÇEKTEN ölmesine bağlıydı; yaşayan-ama-donmuş motorda hiç görünmüyordu.</para>
    ///
    /// <para><b>Neden ping/pong değil:</b> o vakada motorun komut döngüsü canlıydı (run arka plan task'ında
    /// koşar), yani bir ping'e pong dönerdi. Doğru sinyal "yaşıyor mu" değil "beklenen cevabı veriyor mu".</para>
    ///
    /// <para><b>Otomatik kurtarma YOK:</b> bu bayrak hiçbir kilidi kendiliğinden açmaz — graceful drain
    /// dakikalarca sürebilir ve kilidi açmak, hâlâ koşan bir motora ikinci bir run başlatmaya izin vermek
    /// olurdu. Yalnız kapı gösterilir; açıp açmamak kullanıcının kararıdır.</para></summary>
    [ObservableProperty] private string? _engineOverdueMessage;

    /// <summary>Motordan gelen SON event'in zamanı (monotonik). <see cref="OnEvent"/> HER event'te yazar —
    /// <c>ProjectLogEvent</c> dalı UI thread'ine marshal EDİLMEDİĞİ için erişim <see cref="Volatile"/>'dir;
    /// değerlendirme (ve tek gözlemlenebilir alanın yazımı) yalnız UI thread'indeki tick'te yapılır.</summary>
    private long _lastEngineSignalMs;

    /// <summary>[E2/T10] Son Sync başarısız olduysa hata gerekçesi (ErrorEvent.Message) — şerit bunu KIRMIZI
    /// <c>Sync failed — {reason}</c> faz-metnine çevirir (<see cref="RibbonText.Compose"/>). Bir sonraki Sync
    /// (<see cref="OnSyncStarted"/> retry) ya da başarılı tamamlanma (<see cref="OnSyncCompleted"/>) temizler.
    /// Sync SALT-OKUR olduğundan Sync ile retry her zaman mümkündür (butonlar kilitlenmez).</summary>
    [ObservableProperty] private string? _syncErrorMessage;

    /// <summary>[runFailed] Koşan bir run motor tarafında beklenmeyen bir istisnayla düştüğünde
    /// (<c>error(runFailed)</c>) gerekçe — şerit bunu KIRMIZI <c>Run failed — {reason}</c> faz-metnine çevirir.
    /// <see cref="SyncErrorMessage"/>'ın ikizidir; farkı yalnız temizleme kapılarıdır (yeni run ya da yeni Sync).</summary>
    [ObservableProperty] private string? _runErrorMessage;

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

    /// <summary>[C2] Perf profili: Full/Balanced/Light. <see cref="CyclePerfAsync"/> döngüsü paralelliği de günceller.</summary>
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

    public RunViewModel(EngineHost engine, ConsoleBatcher console, Func<string> newRunId, Func<long>? nowMs = null,
        Services.IOsActions? osActions = null)
    {
        _engine = engine;
        _console = console;
        _newRunId = newRunId;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
        _osActions = osActions; // [E1/T67] null ise OS eylemleri güvenle no-op (test default'u); üretimde enjekte edilir
    }

    // ---------------------------------------------------------------- komutlar

    /// <summary>[C2] Ortak run başlatma yolu (Rebuild/Build/Cycles) — tek yerde toplanır:
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
        // [planlama görünürlüğü] StopAsync'in simetriği: faz gönderimden ÖNCE yazılır ve konsola tek satırlık
        // bir not düşer. Motor runStarted'a kadar (taze segmentte: worktree hazırlığı → tarama → graf → topo →
        // incremental) saniyeler harcayabilir; o pencerede ekranın tek kanıtı budur. Konsol notu buffer
        // temizliğinden SONRA yazılır — aksi halde ilk iş olarak silinirdi.
        var previousPhase = Phase;
        Phase = AppPhase.Starting;
        AppendRunLine(RunRequestedLine(mode));
        // [T20-b/K11] PerfMode de gider: paralellik (Parallelism) ve cap/priority (PerfMode) AYNI profil
        // satırının iki yarısıdır — Supervisor cap'i o addan çözer, worker sayısını YENİDEN türetmez.
        // [T2 fix-1 · C1/I4] Branch DEĞİL, RunBranchIntent gider — gerekçe RunBranchIntent'te (görüntüleme
        // değeri ≠ niyet; seed'i niyet diye göndermek worktree'yi zorunlu kılıyor ve detached HEAD'de run'ı
        // hiç başlatmıyordu).
        var cmd = new StartRunCommand(runId, mode, RootPath, Configuration, Parallelism,
            RunBranchIntent, EffectiveUseWorktree, WorktreeName, DependentMode.Safe, LayerPatterns, PerfMode);
        if (!await TrySendAsync(cmd, RunModeLabel(mode)))
        {
            IsStarting = false;
            Phase = previousPhase; // hiçbir engine event'i gelmeyecek — faz Starting'te asılı bırakılamaz
        }
    }

    /// <summary>[planlama görünürlüğü] Run dokümanına düşen tek satırlık not: konsol, tıklamanın KALICI
    /// kaydıdır (şerit bir sonraki faz değişiminde üzerine yazar). Motorun planlama adımları hemen ardından
    /// akar. Mod adı <see cref="RunModeLabel"/>'dan gelir — gönderim hata satırıyla AYNI kaynak.</summary>
    internal static string RunRequestedLine(RunMode mode) => RunModeLabel(mode) + " requested";

    private static string RunModeLabel(RunMode mode) => mode switch
    {
        RunMode.Rebuild => "rebuild",
        RunMode.Build => "build",
        RunMode.Cycles => "cycles",
        _ => "run",
    };

    [RelayCommand(CanExecute = nameof(CanRebuildOrRetry))]
    private Task RebuildAsync()
    {
        ClearSelectionAndFilter(); // [design doRebuild→doBuild] tam run: seçim + filtre sıfırlanır
        return BeginRunAsync(RunMode.Rebuild, clearBuffers: true);
    }
    // [D1 review · A3] Motor erişilemezken (hiç doğamadı) run başlatmak anlamsız — bkz. IsEngineUnavailable.
    // [topoloji kapısı] Sync'siz (topolojisiz) run da anlamsızdır: motor derler ama ekran boş kalır — bkz. HasTopology.
    private bool CanStartRun() => HasTopology && !IsRunning && !IsStarting && !IsEngineUnavailable;

    // [Fix wave 1, C2 review Finding 1] Rebuild/Cycles, Sync uçuştayken (_syncInFlight) EK OLARAK
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

    /// <summary>[cycles] Sync'in yanındaki <b>Cycles</b> düğmesi: YALNIZ dairesel bağımlılık (SCC) oluşturan
    /// projeleri, sıralı turlarla derler. Build'in yerine geçmez, ONDAN ÖNCE gelir — Build bir SCC'yi asla
    /// derlemez, bu koşu ise sadece onları derler.
    ///
    /// <para><b>Neden ayrı bir düğme:</b> bir SCC'yi turlarla derlemenin bedeli üye sayısı × tur sayısıdır ve
    /// normal bir Build'in yanında ölçülemeyecek kadar büyüyebilir. Build'in içine katlandığında kullanıcı,
    /// istemediği ve göremediği bir işin arkasında bekliyordu. Ayrı düğme kararı kullanıcıya verir: ne zaman,
    /// ne kadar.</para>
    ///
    /// <para><see cref="RebuildCommand"/> ile AYNI guard'a tabidir
    /// (<see cref="CanRebuildOrRetry"/>) — bu da tam bir run'dır ve mid-Sync başlatılması aynı transkript
    /// bozulmasını üretirdi.</para></summary>
    [RelayCommand(CanExecute = nameof(CanBuildCycles))]
    private Task BuildCyclesAsync()
    {
        ClearSelectionAndFilter();
        return BeginRunAsync(RunMode.Cycles, clearBuffers: true);
    }

    /// <summary>[cycles] Düğme YALNIZ elde döngü VARKEN etkindir (<see cref="HasCycles"/>): döngüsüz bir
    /// workspace'te bu koşunun kapsamı BOŞTUR (bkz. <c>CycleRunScope</c>) ve her projeyi atlar — pasif
    /// düğme kullanıcıya bunu tıklamadan ÖNCE söyler.</summary>
    private bool CanBuildCycles() => CanRebuildOrRetry() && HasCycles;

    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncAsync()
    {
        SelectedProjectId = null; // [design doSync] seçim temizlenir, filtre KORUNUR
        await TrySendAsync(
            new SyncWorkspaceCommand(RootPath, Branch, LayerPatterns, Configuration), "sync");
        // [A13/T2 · 2.2] Branch envanteri BURADAN istenir — TEK huni. Gerekçe: (a) branch chip'inin tek gerçek
        // kaynağı <see cref="Branches"/>'tir ve o yalnız BranchListEvent ile dolar; (b) repo değişince liste
        // BAYATLAR, ve repo'yu değiştiren HER yol (ilk klasör seçimi / Choose Folder → ChangeRepositoryAsync,
        // Settings→Save → ApplySettingsAsync) zaten buraya iner; (c) Sync salt-okurdur, tekrarı zararsızdır.
        // Ayrı bir komut olarak GİDER (Sync'in kendi event akışına karışmaz): Supervisor sıradaki komut olarak
        // işler ve hatası AYRI bir kodla döner ("branchListFailed", SupervisorHost.cs:138) — RunEndingErrorCodes'ta
        // ve SyncErrorCodes'ta OLMADIĞI için bir Sync hatası gibi yanlış atfedilemez.
        await TrySendAsync(new ListBranchesCommand(RootPath), "listBranches");
        // [T2 fix-1 · I-G] Worktree envanteri de BURADAN istenir — branch'in birebir simetriği ve AYNI
        // gerekçelerle. Gönderilmediği sürece <see cref="Worktrees"/> boş kalıyordu; sonucu yalnız boş bir
        // popover listesi değil, ÜRETİLEN AD'ın kendisiydi: AutoWorktreeName "aynı slug önekiyle başlayan
        // mevcut worktree sayısı"nı hep 0 sayıp her seferinde `-1` son ekini veriyor, yani var olan bir
        // worktree ile ÇAKIŞAN bir ad öneriyordu. Hatası ayrı kodla döner ("worktreeListFailed").
        await TrySendAsync(new ListWorktreesCommand(RootPath), "listWorktrees");
    }
    private bool CanSync() => !IsRunning && !IsStarting && !IsEngineUnavailable; // [D1 review · A3]

    /// <summary>Graceful stop: yeni proje dispatch EDİLMEZ, uçuştaki <c>MSBuild.exe</c> child'ları post-build
    /// copy dahil kendi tamamlanmalarını yapar (ortak çıktı dizininde yarım yazılmış DLL kalmaz — ARCHITECTURE
    /// §4.5).
    /// <para><b>Continue kalktıktan sonra da graceful:</b> tek toparlanma yolu Build olduğu için seçim artık
    /// "kaç projelik iş çöpe gidiyor" sorusudur. Drain'de biten projeler <c>PersistBuildStateOnSuccess</c> ile
    /// bankaya girer ve bir sonraki Build onları ATLAR — yani Stop'un bedeli SIFIRDIR. Hard kill ise uçuştaki
    /// projeleri <c>failed("stopped")</c> yapıp stored state'lerini geçersizleştirir: paralellik kadar yarım
    /// derleme çöpe gider, kullanıcının kendi Stop'u listede KIRMIZI satırlar bırakır ve o projeler bir sonraki
    /// Build'de baştan derlenir. Hard yolu kontratta/motorda durur, App'ten GÖNDERİLMEZ.</para>
    /// <para>Drain, uçuştaki en yavaş projenin kalan süresi kadar sürebilir; uygulamanın tıklamayı ALDIĞINI o
    /// pencerede göstermesi bu yüzden davranışın kendisi kadar önemlidir.</para>
    /// <para><b>Faz gönderimden ÖNCE yazılır:</b> yavaş/tıkalı bir engine'de gönderimin dönmesini beklemek
    /// butonu saniyelerce "Stop" bırakır ve kullanıcı — haklı olarak — tıklamanın kaybolduğunu düşünüp tekrar
    /// basar. Gönderim SENKRON başarısız olursa (engine hazır değil) faz geri alınır: hiçbir runStopped/
    /// runCompleted gelmeyeceği için aksi halde <c>Stopping</c>'te sonsuza dek asılı kalırdı. Bu,
    /// <see cref="BeginRunAsync"/>'in "gönderim başarısız → IsStarting geri açılır" kapısının ikizidir.</para>
    /// <para><see cref="IsRunning"/>/<see cref="IsStarting"/>'e DOKUNULMAZ: motor hâlâ koşuyor, dolayısıyla
    /// <see cref="IsMidRunLocked"/> sürer (branch/worktree/configuration kilidi kalkmaz, split-button geri
    /// gelmez). Fazdan çıkış motorun sonucuna aittir — bkz. <see cref="OnRunCompleted"/>/
    /// <see cref="OnRunStopped"/>/<see cref="OnError"/>/<see cref="OnEngineExited"/>.</para></summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        if (_currentRunId is null) return;
        var previous = Phase;
        Phase = AppPhase.Stopping;
        AppendRunLine(StopRequestedLine(Counters.Building));
        if (!await TrySendAsync(new StopRunCommand(_currentRunId, StopKind.Graceful), "stop"))
            Phase = previous;
    }

    /// <summary>[Stopping] Run dokümanına düşen tek satırlık not — konsol, tıklamanın kalıcı kaydıdır (şerit
    /// yalnız ANLIK durumu gösterir). Beklentiyi de kurar: kuyruk durdu ama uçuştakiler bitecek.</summary>
    internal static string StopRequestedLine(int inFlight) => string.Format(CultureInfo.InvariantCulture,
        "stop requested — no new projects will start; {0} in flight will finish", inFlight);

    // [Stopping] Faz kapısı: Stop bir kez sahiplenilir. İkinci bir tıklama (ya da koşarken F5) ikinci bir
    // stopRun ÜRETMEZ — motor tarafında zararsız olurdu ama buton "sanki hiçbir şey olmuyor" hissini sürdürürdü.
    private bool CanStop() => (IsRunning || IsStarting) && Phase != AppPhase.Stopping;

    // [design v1.7.0 §3.1] Sürdürme ve yeniden deneme AYRI birer komut DEĞİLDİR: Stop'tan sonra da hata
    // sonrasında da kullanıcı Build'e basar. Öldürülen ve başarısız projelerin stored BuildState'i
    // geçersizleştiği için yeniden derlenirler; yeşil bitenler "up to date" atlanır; hata etkilenmiş
    // bağımlılar imzalarını hiç persist etmedikleri için kümeye kendiliğinden girer. Motor tarafında da
    // karşılıkları yoktur (RunMode üç değerlidir).

    /// <summary>[E2/T37] Şeridin kalıcı hata modundaki "Restart engine" aksiyonu: ölmüş engine process'ini yeniden
    /// başlatır (<see cref="EngineHost.RestartAsync"/>). Başarılıysa <see cref="EngineDiedMessage"/> temizlenir
    /// (engine geri geldi — bir sonraki runStarted'ı beklemeden, çünkü Restart tek başına da engine sağlığını
    /// kanıtlar). MainWindow'un <c>_engine.EventReceived</c>/<c>EngineExited</c> abonelikleri AYNI EngineHost
    /// instance'ında kaldığından yeniden kablolama gerekmez. Gönderim başarısız olursa gerekçe konsola düşer ve
    /// hata modu KALIR (kullanıcı tekrar deneyebilir).
    /// <para>[final review I-2] <see cref="Services.EngineUnavailableException"/> AYRI yakalanır: yeniden
    /// başlatma preflight'ta (dosya yok / başlatılamıyor) düşerse bu bir "tekrar dene" hatası DEĞİLDİR —
    /// <see cref="OnEngineUnavailable"/> ile D1'in "motor erişilemez" durumuna geçilir (aksiyon gizlenir,
    /// komutlar kapanır, şeritte TEK ve doğru mesaj kalır). Aksi halde generic catch bu türü ayırt etmediği
    /// için <see cref="EngineRestartable"/> true kalır ve <see cref="EngineDiedMessage"/> eski "unexpectedly
    /// stopped" metniyle donar: kullanıcıya sonsuza dek "Restart engine" sunulur, komutlar açık kalır ve her
    /// tıklama şeritteki mesajla ÇELİŞEN ikinci bir hata satırı üretir — <see cref="EngineRestartable"/>'ın
    /// değişmezi de ("EngineDiedMessage'ı yazan HER yol bunu da yazar") bozulurdu.</para></summary>
    [RelayCommand]
    private async Task RestartEngineAsync()
    {
        try
        {
            await _engine.RestartAsync();
            EngineDiedMessage = null;
        }
        catch (Services.EngineUnavailableException ex)
        {
            OnEngineUnavailable(ex.ExePath, ex.Reason); // [final review I-2] D1'in "engine yok" durumu
        }
        catch (Exception ex)
        {
            AppendRunLine($"[error] engine restart failed: {ex.Message}");
        }
        finally
        {
            // Eski process (ve tüm MSBuild child'ları) her koşulda gitti — o motorun asla göndermeyeceği
            // event'leri bekleyen hiçbir durum kalmamalı. Yeni motor başlatılamadıysa da geçerlidir:
            // orada da bekleyecek bir şey yoktur (bkz. OnEngineUnavailable, komutlar zaten kapanır).
            ReleaseAfterEngineLoss();
        }
    }

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
        PropagateSelectionToStream(value); // [D3] stream satırları da tek seçim kaynağından tazelenir
    }

    /// <summary>[Fix wave 1 · D1 review Finding 1] Bir run uçuşta mı — <see cref="ProjectRowViewModel.Status"/>'un
    /// <c>queued</c> türetimi için her satıra iter (IsSelected akışının eşi). <see cref="IsRunning"/>/
    /// <see cref="IsStarting"/> değiştiğinde tazelenir; yeni doğan satırlar (<see cref="EnsureRow"/>/topoloji)
    /// da mevcut değeri alır.</summary>
    private bool RunActive => IsRunning || IsStarting;
    partial void OnIsRunningChanged(bool value) => PropagateRunActive();
    partial void OnIsStartingChanged(bool value)
    {
        PropagateRunActive();
        if (value) ArmEngineWatchdog(); // run istendi — motor bundan sonra konuşmalı
    }
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
        LayerGrouping.Build(VisibleProjects, Topology);

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

    /// <summary>
    /// [T20-b/K11] Perf profilini App tarafında TÜRETMENİN tek kapısı — üç tüketicisi de buradan geçer:
    /// <see cref="Parallelism"/> alan başlatıcısı, <see cref="CyclePerfAsync"/> ve <see cref="SetPerfMode"/>.
    /// Tablo App'te DEĞİL Core'dadır (<see cref="PerfProfile"/>): paralellik + CPU cap + priority üçlüsünü
    /// Supervisor da AYNI satırdan okur, iki tablo tutulmaz.
    /// <para><b>Türetme ≠ geçerlilik.</b> Tanınmayan metin (ör. bayat bir UiState değeri)
    /// <see cref="PerfProfile.TryParse"/>'ta <c>null</c> olur; burada App'in ESKİ davranışına (Balanced/4)
    /// düşülür — kaldırılan <c>ParallelismFor</c> tablosunun <c>_ =&gt; 4</c> dalının birebir karşılığı.
    /// <see cref="SetPerfMode"/> ise geçersiz bir SEED'i kabul etmez (no-op) — o kapı ayrıdır ve bu fallback'i
    /// KULLANMAZ. Bu yüzden fallback dalı üretimden erişilemez, yalnız savunma amaçlıdır ve
    /// <c>internal</c>'dır: onu pinleyen test doğrudan çağırır.</para>
    /// </summary>
    internal static PerfProfile ProfileFor(string perfMode) =>
        PerfProfile.TryParse(perfMode) ?? PerfProfile.For(CorePerfMode.Balanced);

    /// <summary>
    /// [T43 · T20-b/K11] Perf chip: Full → Balanced → Light → Full döngüsü; paralelliği de günceller. Koşarken de
    /// CANLI (kilitlenmez) — ve artık koşan run'a GERÇEKTEN etki eder: <see cref="SetPerfModeCommand"/> gönderilir.
    /// <para><b>Canlı değişen YALNIZ CPU cap + priority'dir.</b> Worker'lar run başında bir kez yaratılır
    /// (Supervisor'ın <c>RunCoordinator</c>'ı) ve dinamik bir slot mekanizması yoktur, bu yüzden yeni profilin
    /// PARALELLİĞİ ancak BİR SONRAKİ run'da geçerli olur. Bu, K11'in "çalışırken değiştirilebilir" ifadesinin
    /// dürüst yorumudur. Konsola yalnız K11'in kendi satırı (<see cref="PerfNoteText.Note"/>) yazılır —
    /// açıklayıcı ikinci bir cümle YOK (design-v1'in konsol dili: sakin, kesin, tekrarsız).</para>
    /// <para>Aynı sebeple ETA de dokunulmadan bırakılır: ETA modeli run'ın BAŞLANGIÇ paralelliğini
    /// <c>runStarted</c>'dan dondurur (<c>_runParallelism</c>) ve canlı cap değişimi onu BİLİNÇLİ olarak
    /// güncellemez (tahmin gürültüsü &lt; karmaşıklık maliyeti).</para>
    /// <para>[Fix round 1 — KÖK 1] Kapı <see cref="IsRunning"/> DEĞİL <see cref="IsMidRunLocked"/>'dır: Build'e
    /// basıldıktan sonra <c>runStarted</c> gelene kadar geçen PLANLAMA PENCERESİ (177 projede saniyeler) de
    /// run'ın uçuşta olduğu bir aralıktır ve chip o sırada canlıdır. <c>IsRunning</c> kapısı o pencerede gelen
    /// değişimi sessizce yutuyordu (run tüm ömrü boyunca eski cap'le koşardı). Supervisor tarafı komutu
    /// planlama bitene kadar bekletir ve run başlarken uygular.</para>
    /// <para>Hiç run uçuşta değilken ne komut ne not vardır: profil zaten bir sonraki
    /// <see cref="StartRunCommand.PerfMode"/> ile motora gider (bkz. <see cref="BeginRunAsync"/>).</para>
    /// </summary>
    public async Task CyclePerfAsync()
    {
        PerfMode = PerfMode switch { "Full" => "Balanced", "Balanced" => "Light", "Light" => "Full", _ => "Balanced" };
        var profile = ProfileFor(PerfMode);
        Parallelism = profile.Parallelism;
        if (!IsMidRunLocked) return;
        AppendRunLine(PerfNoteText.Note(profile)); // BuildApp.jsx:1366-1372'nin K11 karşılığı (kopya metin Core'da)
        await TrySendAsync(new SetPerfModeCommand(PerfMode), "setPerfMode");
    }

    /// <summary>[C2] Satır durumu değişimlerinde türev yüzeyi tazeler: sayaçlar, görünür liste, run
    /// etkinliği.</summary>
    private void RefreshRunSurface()
    {
        Counters = RunCounters.From(Projects);
        RecomputeWillBuildSurface(); // [D2] wb/fin/allClean — sayaçlarla aynı tetikleyicide tazelenir
        OnPropertyChanged(nameof(VisibleProjects));
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
        catch (Exception ex) { AppendRunLine($"[error] failed to send {what}: {ex.Message}"); return false; }
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
        EvaluateEngineSilence();
    }

    // ---------------------------------------------------------------- motor sessizlik watchdog'u

    /// <summary>Bir GEÇİŞ bekleniyor mu: run istendi ama <c>runStarted</c> gelmedi (<see cref="IsStarting"/>),
    /// ya da stop istendi ama <c>runStopped</c> gelmedi (faz <see cref="AppPhase.Stopping"/>). Watchdog YALNIZ
    /// bu iki pencerede kuruludur — koşan bir run'da tek bir projenin sessizce dakikalarca derlenmesi meşrudur
    /// ve orada uyarmak kullanıcıyı sağlıklı bir build'i öldürmeye davet ederdi.</summary>
    private bool WaitingOnEngine => IsStarting || Phase == AppPhase.Stopping;

    /// <summary>Sessizlik saatini şimdiye alır: bekleyiş TAM BURADA başlar. Kurulmasaydı, uzun süre boşta
    /// duran bir uygulamada basılan ilk Build anında "cevap vermiyor" derdi.</summary>
    private void ArmEngineWatchdog() => Volatile.Write(ref _lastEngineSignalMs, _nowMs());

    /// <summary>UI thread'inde, <see cref="TickElapsed"/> ile 200ms'de bir. Gözlemlenebilir alanın TEK yazıcısı
    /// burasıdır: <see cref="OnEvent"/> (arka plan thread'inde de koşabilir) yalnız zaman damgasını yazar.
    /// Bekleyiş bittiğinde ya da motor yeniden konuştuğunda uyarı KENDİLİĞİNDEN kalkar.</summary>
    private void EvaluateEngineSilence()
    {
        bool overdue = WaitingOnEngine
            && _nowMs() - Volatile.Read(ref _lastEngineSignalMs) >= EngineSilenceThresholdMs;
        EngineOverdueMessage = overdue ? EngineSilentMessage : null;
    }

    /// <summary>Şeridin amber satırı. Süre TEK kaynaktan (<see cref="EngineSilenceThresholdMs"/>) biçimlenir —
    /// eşik değişirse metin de değişir. Dil, mevcut kalıcı satırların (<see cref="EngineMissingMessage"/>)
    /// em-dash + <c>·</c> idiomuyla aynı; sakin ve kesin (design-v1 §"Ton").</summary>
    internal static string EngineSilentMessage { get; } = string.Format(CultureInfo.InvariantCulture,
        "Engine has stopped responding — no reply for {0} · you can restart it",
        DurationFormat.Elapsed(EngineSilenceThresholdMs));

    // ---------------------------------------------------------------- event → durum

    public void OnEvent(IpcEvent ev)
    {
        // Motor KONUŞTU: sessizlik saati sıfırlanır. Event TÜRÜ önemsizdir — sinyal "yaşıyor mu" değil
        // "susuyor mu". Uyarının kendisi BURADA temizlenmez: bu metot ProjectLogEvent için arka plan
        // thread'inden de çağrılır ve gözlemlenebilir alanların tek yazıcısı UI thread'indeki tick'tir.
        Volatile.Write(ref _lastEngineSignalMs, _nowMs());
        switch (ev)
        {
            case RunStartedEvent e: OnRunStarted(e); break;
            case BuildPreviewEvent e: OnBuildPreview(e); break;
            case ProjectStartedEvent e: OnProjectStarted(e); break;
            case ProjectLogEvent e: OnProjectLog(e); break;
            case ProjectLogChunkEvent e: OnProjectLogChunk(e); break;
            case ProjectSucceededEvent e: OnProjectDone(e.ProjectId, ProjectRowState.Succeeded, e.DurationMs, e.DepIssues, e.CycleUnsettled); break;
            case ProjectFailedEvent e: OnProjectDone(e.ProjectId, ProjectRowState.Failed, e.DurationMs, e.DepIssues); break;
            case ProjectSkippedEvent e: OnProjectSkipped(e); break;
            case CycleCompletedEvent e: OnCycleCompleted(e); break;
            case RunCompletedEvent e: OnRunCompleted(e); break;
            case RunStoppedEvent: OnRunStopped(); break;
            case ErrorEvent e: OnError(e); break;
            // [A5/T69] Sync yüzeyi — handler'lar RunViewModel.Workspace.cs'te
            case SyncStartedEvent: OnSyncStarted(); break;
            case SyncProgressEvent e: AppendRunLine(e.Line); break;
            // [planlama görünürlüğü] Motorun planlama adımları. AppendRunLine DIŞINDA hiçbir şeye dokunmaz:
            // faz zaten Starting'tir (BeginRunAsync yazdı) ve bu satırlar Sync yüzeyine (_syncInFlight) AİT
            // DEĞİLDİR — oraya bağlanırsa Rebuild/Cycles planlama boyunca sessizce kilitlenirdi.
            case PlanProgressEvent e: AppendRunLine(e.Line); break;
            case SyncCompletedEvent e: OnSyncCompleted(e); break;
            case WorkspaceTopologyEvent e: OnWorkspaceTopology(e); break;
            case BranchListEvent e: OnBranchList(e); break;
            case WorktreeListEvent e: Worktrees.ReplaceAll(e.Worktrees); break;
        }

        // [D3] Event stream (tampon anlatı + aktif satır) — proje satırları/sayaçlar YUKARIDA güncellendikten
        // SONRA türetilir (ad çözümü + done-glyph'in Counters.Failed'i doğru okunsun). Marshal-free ProjectLogEvent
        // hot-path'ine (Ek A13.2) DOKUNMAZ: yalnız zaten UI-thread'inde olan OnEvent dalından çağrılır.
        AppendStreamFor(ev);
    }

    private void OnRunStarted(RunStartedEvent e)
    {
        _currentRunId = e.RunId;
        IsRunning = true;
        Phase = AppPhase.Running; // [C2] Idle → Running
        IsStarting = false; // [Fix wave 1(It-3), Finding 3] planlama bitti — Stop artık IsRunning üzerinden erişilebilir
        // [Review fix, Task 16] EngineDiedMessage burada temizlenir: runStarted, VM'in CANLI engine instance'ıyla
        // IPC round-trip yaptığının ilk somut kanıtıdır — RebuildAsync/ContinueAsync'de ERKEN temizlemek YANLIŞ
        // olurdu (gönderim henüz round-trip olmadan "iyimser" temizlik, engine hâlâ ölüyken bile mesajı silerdi).
        // Temizlenmezse EngineDiedMessage tek bir ölümden sonra SONSUZA DEK stale kalır — sıradaki N run tamamen
        // başarılı olsa bile "engine öldü" mesajı güncel engine sağlığını YANLIŞ yansıtmaya devam eder.
        EngineDiedMessage = null;
        // [runFailed] Aynı gerekçe: yeni bir run GERÇEKTEN başladı (round-trip kanıtı) — önceki run'ın hata
        // gerekçesi artık geçmiştir ve şeridi bu run'ın ilerlemesine bırakmalıdır.
        RunErrorMessage = null;
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
    /// [Review fix, Task 17] Satır zaten varsa bu savunmacı bir
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
            // [W1] CurrentSha ataması, aşağıdaki terminal-satır guard'ından ÖNCE ve ondan BAĞIMSIZ yapılır: o
            // guard yalnız WillBuild'i korumak içindir (segment 1'in canlı succeeded→clean geçişi ezilmesin).
            // Sha'nın böyle bir koruma İHTİYACI YOKTUR — tersine, segment 2'nin okuduğu değer segment 1'in
            // persist'ini içerdiği için terminal satırların sol yarısı ancak burada TAZELENİR.
            row.CurrentSha = item.BuiltCommit;
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
        var row = EnsureRow(e.ProjectId, e.Name, ProjectRowState.Started);
        row.State = ProjectRowState.Started;
        row.CycleWaiting = false; // sıra ONDA: bu event'in anlamı tam olarak budur
        _projectStartedAtMs[e.ProjectId] = _nowMs();
        // [cycle rounds/I2] Bir SCC üyesi başladıysa, KARDEŞLERİ artık beklemededir: grup içinde eşzamanlı
        // invoke YOKTUR (biri diğerinin az önce yazdığı DLL'i okur). Ara tur sonuçları yayılmadığı için
        // kardeşler Started'ta KALIR — bayrak, "Started" ile "şu an derleniyor"u ayıran tek şeydir.
        foreach (string sibling in _cycleGroups?.MembersOf(e.ProjectId) ?? [])
            if (!string.Equals(sibling, e.ProjectId, StringComparison.OrdinalIgnoreCase)
                && Projects.FirstOrDefault(p => string.Equals(p.Id, sibling, StringComparison.OrdinalIgnoreCase))
                    is { State: ProjectRowState.Started } waiting)
                waiting.CycleWaiting = true;
        RefreshRunSurface();
    }

    private void OnProjectSkipped(ProjectSkippedEvent e)
    {
        var row = EnsureRow(e.ProjectId, Path.GetFileNameWithoutExtension(e.ProjectId), ProjectRowState.Skipped);
        row.State = ProjectRowState.Skipped;
        row.CycleUnconverged = e.CycleUnconverged; // [cycle rounds/Task 8] kalıcı kırık döngü — render Task 9'undur
        row.CycleWaiting = false; // [cycle rounds/I2] terminal satır hiçbir grubun sırasını beklemez
        _projectStartedAtMs.Remove(e.ProjectId);
        UpdateEta(); // [Task 17] skip de bir "tamamlanma" — kalan sayaç değişir
        RefreshRunSurface();
    }

    /// <summary>
    /// [cycles] Bir SCC turlarını bitirdi. Grup YAKINSAMADIYSA üyeleri "kalıcı kırık döngü" olarak işaretlenir:
    /// bu koşu kanıtladı ki turlar bu kaynaklarla grubu güncel hâle getiremiyor.
    ///
    /// <para>Bayrağın kaynağı DEĞİŞTİ. Eskiden motor, önceki bir koşuda yakınsamamış grubu hiç denemeden
    /// pre-skip eder ve bayrağı o skip'e iliştirirdi; o pre-skip kalktığı için (açık Resolve basışı artık her
    /// zaman taze bir deneme yapar) bayrağın tek üreticisi de kalkmıştı. Yeni kaynak daha dürüst: hatırlanan
    /// bir geçmiş değil, ŞU koşunun kanıtı — ve kullanıcı bunu düğmeye ikinci kez basmadan, tam da denemenin
    /// bittiği koşuda görür.</para>
    ///
    /// <para>Sıra güvenlidir: motor önce üye sonuçlarını, sonra bu olayı yayınlar — yani
    /// <see cref="OnProjectDone"/>'ın bayrağı temizleyen satırı bundan ÖNCE koşar.</para>
    /// </summary>
    private void OnCycleCompleted(CycleCompletedEvent e)
    {
        if (e.Outcome != CycleOutcome.NoProgress) return;
        foreach (string member in _cycleGroups?.MembersOf(e.ProjectId) ?? [e.ProjectId])
            if (Projects.FirstOrDefault(p => string.Equals(p.Id, member, StringComparison.OrdinalIgnoreCase)) is { } row)
                row.CycleUnconverged = true;
        RefreshRunSurface();
    }

    private void OnProjectDone(string projectId, ProjectRowState state, long durationMs, IReadOnlyList<string>? depIssues,
        bool cycleUnsettled = false)
    {
        var row = Projects.FirstOrDefault(p => p.Id == projectId);
        if (row is null) return; // protokole göre Started her zaman önce gelir — savunmacı no-op
        row.State = state;
        row.DurationMs = durationMs;
        row.DepIssues = depIssues; // [Task 17] ▲ sinyali — HasDepIssue bundan türetilir
        row.CycleUnsettled = cycleUnsettled; // [cycle rounds/Task 8] ProjectFailedEvent bu alanı taşımaz → varsayılan false
        // [cycle rounds/Task 9 review fix 1] Proje bu run'da GERÇEKTEN invoke edildi (Succeeded ya da Failed
        // fark etmez) — önceki bir segmentten kalma "hiç invoke edilmeden pre-skip edildi" bayrağı artık
        // YANLIŞ; satır nesneleri segmentler arası hayatta kaldığı için (Projects.Clear() yalnız Rebuild'de)
        // burada temizlenmezse "az önce düzelen proje" render katmanında kalıcı-kırık gibi görünürdü.
        row.CycleUnconverged = false;
        row.CycleWaiting = false; // [cycle rounds/I2] terminal satır hiçbir grubun sırasını beklemez
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
        // [W1] TargetSha da IsRunActive/NamePrefix ile AYNI itme deseninden gelir: run ortasında doğan bir satır
        // (ör. topolojide olmayan bir projectStarted) hedef sha'yı yeni bir syncCompleted beklemeden alır.
        var row = new ProjectRowViewModel(id, name, initialState)
        { IsRunActive = RunActive, NamePrefix = _graphNamePrefix, TargetSha = TargetSha };
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
        // [cycle rounds/I2] "building" kovası PARALEL çalışan işler içindir (toplamı paralelliğe bölünür) —
        // bir SCC üyesi oraya AİT DEĞİLDİR, koşuyor olsa bile: grubun üyeleri sıralı invoke edilir ve grup en
        // az BaselineRounds tur çalışır. Started bir üyeyi buraya koymak, tam da işin yapıldığı pencerede tur
        // çarpanını YOK EDİYORDU (üye Pending'den çıktığı an cycle kovasından da düşüyordu).
        var buildingRows = Projects.Where(p => p.State == ProjectRowState.Started && !p.InCycle).ToList();
        int remaining = Math.Max(0, total - completed);
        int queuedCount = Math.Max(0, remaining - buildingRows.Count);

        // [cycle rounds/Task 10] cycle (SCC) üyeleri EtaCalculator'a AYRI listeyle beslenir — onlar paralel
        // değil sıralı invoke edilir ve küme en az iki tur çalışır (EtaCalculator kendi içinde
        // CycleRoundPolicy.BaselineRounds ile çarpar, paralelliğe bölmez). InCycle bayrağı satırda zaten var
        // (topoloji uzlaştırmasından, bkz. ProjectRowViewModel.InCycle) — burada YENİDEN türetilmez.
        // [I2] Henüz BAŞLAMAMIŞ (Pending) üyeler kadar KOŞAN (Started) üyeler de bu kovadadır: ikisinin de
        // kalan maliyeti "grup, turlarıyla birlikte" terimidir. Geçen süre kasıtlı olarak DÜŞÜLMEZ — tur
        // döngüsünde her üyenin kendi başlangıcı her turda sıfırlanır, tek bir turun elapsed'i grubun kalanı
        // hakkında bir şey söylemez; tahmin bu yönde bilerek KARAMSARDIR (bkz. ARCHITECTURE.md §8.4).
        int cycleQueuedCount = Projects.Count(
            p => p.InCycle && p.State is ProjectRowState.Pending or ProjectRowState.Started);
        cycleQueuedCount = Math.Min(cycleQueuedCount, queuedCount); // savunmacı — total henüz satırlaşmamış projeler içerebilir
        int ordinaryQueuedCount = queuedCount - cycleQueuedCount;

        var knownDurations = Projects
            .Where(p => p.State is ProjectRowState.Succeeded or ProjectRowState.Failed)
            .Select(p => p.DurationMs)
            .ToList();
        long? observedAverageMs = knownDurations.Count > 0 ? (long)knownDurations.Average() : null;

        var queuedEstimatesMs = Enumerable.Repeat(observedAverageMs, ordinaryQueuedCount).ToList();
        var cycleQueuedEstimatesMs = Enumerable.Repeat(observedAverageMs, cycleQueuedCount).ToList();
        long now = _nowMs();
        var building = buildingRows
            .Select(p => new EtaCalculator.BuildingProject(
                ElapsedMs: _projectStartedAtMs.TryGetValue(p.Id, out long startedAt) ? Math.Max(0, now - startedAt) : 0,
                LastDurationMs: observedAverageMs))
            .ToList();

        long? rawEstimateMs = EtaCalculator.ComputeRawEstimateMs(queuedEstimatesMs, building, _runParallelism ?? Parallelism, cycleQueuedEstimatesMs);
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
        DepIssueCount = e.DepIssueCount; // [Task 17] run genelinde (Continue segmentleri dahil) kümülatif özet
        RefreshRunSurface();
    }

    /// <summary>[B2] <c>runStopped</c> TEK DALLIDIR — run başlamış olsun ya da olmasın, faz
    /// <see cref="AppPhase.Stopped"/> ve run state serbest.
    /// <para>Eskiden runStarted görülmüşse burada ERKEN DÖNÜLÜR, faz <c>runCompleted</c>'a bırakılırdı. Bu,
    /// fazın çözülmesini bir OLAY SIRALAMASI varsayımına bağlıyordu ve <c>runCompleted</c>'ın gelmediği her
    /// durumda <c>Stopping</c> asılı kalıyordu (kullanıcı bildirimi). Varsayıma gerek YOKTUR: koordinatör
    /// <c>runStopped</c>'ı zaten TÜM in-flight sonuçlarını raporladıktan sonra yazar (<c>RunSegmentAsync</c>
    /// finally + <c>_finishing</c> kapısı; sahiplenemediği durumda ise host anında yazar) — bu olay görüldüğünde
    /// koşan bir şey KALMAMIŞTIR. Arkadan gelen <c>runCompleted</c> aynı fazı yazdığı için ara görüntü oluşmaz;
    /// gelmezse de faz doğru yerde kalır.</para></summary>
    private void OnRunStopped()
    {
        IsRunning = false;
        IsStarting = false; // planlama sırasında stop ack'i de buradan geçer — Build'i geri aç
        Phase = AppPhase.Stopped;
    }

    private void OnError(ErrorEvent e)
    {
        AppendRunLine($"[error] {e.Code}: {e.Message}");
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
        // [B1] REDDEDİLEN bir başlatma isteği (runInProgress) kendi "starting" bayrağını BIRAKMALIDIR. Motor run
        // slotunu (_runActive) tüm event'ler yazıldıktan SONRA bırakır (ExecuteRunAsync'in finally'si), yani
        // runCompleted App'e ulaştıktan sonra kısa bir pencere boyunca slot HÂLÂ doludur — butonlar tam o anda
        // açıldığı için hızlı bir tıklama bu reddi alır. Bayrak temizlenmezse UI kilit penceresinde SONSUZA DEK
        // donardı: Build/Rebuild disabled, Stop görünür ama arkada durdurulacak bir şey yok. Koşan run'a
        // DOKUNULMAZ (aşağıdaki erken dönüş) — kilit gerçekten koşuyorsa IsRunning üzerinden zaten sürer.
        if (e.Code == RunInProgressCode) IsStarting = false;
        if (!RunEndingErrorCodes.Contains(e.Code)) return; // runInProgress/logNotFound/... aktif run'ı ETKİLEMEZ
        // [A5/T69 · Fix wave 1, Finding 2] Sync fazını bırakır ve hatanın KAYNAĞINI ayırt eder: kod Sync'ten
        // geldiyse (uçuşta bir Sync var ve run planlama penceresinde DEĞİL) run state'ine DOKUNULMAZ — Sync
        // salt-okurdur ve koşan bir run sırasında da tetiklenebilir. Gerekçe: RunViewModel.Workspace.cs.
        if (TryConsumeSyncFailure(e.Code, e.Message)) return;
        IsRunning = false;
        IsStarting = false; // [Fix wave 1(It-3), Finding 3] planFailed/msbuildNotFound — Rebuild'i geri aç
        // Run-bitiren bir hata geldiğinde runCompleted ASLA gelmez — fazı bırakan başka kapı yoktur.
        // [Stopping] Stop penceresinde gelirse buton sonsuza dek pasif, şerit sonsuza dek "Stopping" kalırdı.
        // [runFailed] Running'de gelirse (yalnız runFailed bunu yapabilir — kümedeki diğer üç kod runStarted'dan
        // ÖNCE üretilir) şerit donmuş bir "▸ Building 3/10" gösterirdi: derleme bitmiş, uygulama "derliyorum"
        // diyor. Stopped'a DEĞİL dinlenme fazına düşülür — durmadı, düştü; gerekçeyi RunErrorMessage anlatır.
        // [planlama görünürlüğü] Starting de aynı kapıdan geçer: planFailed/msbuildNotFound TAM OLARAK bu
        // pencerede üretilir ve runStarted asla gelmez — faz bırakılmazsa şerit sonsuza dek "▸ Starting" der.
        if (Phase is AppPhase.Running or AppPhase.Stopping or AppPhase.Starting) Phase = RestingPhase;
        // [runFailed] Gerekçe şeride taşınır — konsol satırı tek başına yeterli değil (kullanıcı konsola
        // bakmıyor olabilir ve şerit o ana kadar aksini söylüyordu).
        RunErrorMessage = e.Message;
    }

    /// <summary>[Task 16 — It-2 devir §8, kama düzeltmesi] <see cref="EngineHost.EngineExited"/> eskiden VM'e
    /// hiç BAĞLI DEĞİLDİ: engine process startRun sonrası runStarted'dan ÖNCE ya da run ORTASINDA ölürse,
    /// hiçbir IPC event'i asla gelmeyeceğinden (ne runCompleted ne runStopped ne run-bitiren ErrorEvent)
    /// IsStarting/IsRunning SONSUZA DEK kilitli kalırdı — "Restart Engine" MainWindow'daki
    /// banner'ı güncelliyordu ama VM'e hiç dokunmadığından butonlar açılmıyordu. <see cref="RunEndingErrorCodes"/>
    /// deseniyle TUTARLI: aynı run-state alanları sıfırlanır; ayrıca bir sonraki run/Restart'ın
    /// <c>_currentRunId</c> bakiyesiyle karışmaması için o bakiye de temizlenir (StopAsync zaten CanStop=false
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
        // [E2/T37 · İngilizce sweep] Şerit kalıcı-hata modu bu metni GÖSTERİR → İngilizce (tüm UI/konsol metni).
        // Exit kodu (varsa) KORUNUR — test bu sayıyı pinler.
        // [D1] Doğmuş bir motorun ölümü YENİDEN BAŞLATILABİLİR — şerit "Restart engine" aksiyonunu gösterir
        // (EngineDiedMessage yazan her yol bu bayrağı da yazar; bkz. EngineRestartable).
        EngineRestartable = true;
        EngineDiedMessage = exitCode is { } code
            ? $"Engine stopped unexpectedly (exit {code})"
            : "Engine stopped unexpectedly (protocol error)";
        ReleaseAfterEngineLoss();
    }

    /// <summary>Motor GİTTİ — beklenmedik ölümle (<see cref="OnEngineExited"/>) ya da kullanıcının kasıtlı
    /// yeniden başlatmasıyla (<see cref="RestartEngineAsync"/>). Her iki yolda da uçuştaki run/Sync pencereleri
    /// serbest bırakılır: o motorun asla göndermeyeceği event'leri bekleyen hiçbir durum kalmamalıdır.
    ///
    /// <para><b>Neden iki çağıran:</b> <see cref="EngineHost.RestartAsync"/> önce <c>_generation</c>'ı artırır
    /// (eski exit-watcher susturulur) ve ancak sonra child'ı öldürür — yani YAŞAYAN bir motoru yeniden
    /// başlatmak <c>EngineExited</c> ATEŞLEMEZ. Serbest bırakma yalnız ölüm yolunda kalsaydı, donmuş (ama
    /// yaşayan) bir motorda Restart'a basmak şeridi temizler, kilidi AÇMAZDI — kapının varlık sebebi de
    /// tam olarak o kilidi açmaktır.</para>
    ///
    /// <para><b>Idempotent:</b> [ObservableProperty] setter'ları eşitlik kontrolüyle çalışır, yani hiçbir run
    /// aktif değilken çağrılırsa no-op'tur.</para></summary>
    private void ReleaseAfterEngineLoss()
    {
        // [E2/F3 fold] Terminal Phase kararı: engine run ORTASINDA (Phase=Running) ölürse Phase Running'de asılı
        // kalıp IsRunning=false ile çelişik bir resting state bırakıyordu. Şerit kalıcı-hata modu EngineDiedMessage
        // != null ÖNCELİĞİYLE Phase'i YOK SAYAR (bkz. RibbonText.Compose), yani terminal Phase seçimi KOZMETİKtir;
        // yine de tutarlılık için Running → Stopped'a çekilir. YALNIZ Running'e dokunulur: Idle/Boot/Empty gibi
        // resting fazlar (engine repo seçili değilken de ölebilir) Stopped'a çekilmez — yanıltıcı olurdu.
        // [Stopping] Stop penceresi de aynı gerekçeye girer: motor öldüyse drain'i bitirecek kimse kalmadı,
        // faz Stopping'de asılı bırakılamaz. Kullanıcı zaten durmayı istemişti — terminal faz Stopped'tır.
        if (Phase is AppPhase.Running or AppPhase.Stopping) Phase = AppPhase.Stopped;
        // [planlama görünürlüğü] Starting AYRI: orada hiçbir proje derlenmedi, "▸ Stopped — 0/0 · 0 not built"
        // olmayan bir koşuyu anlatırdı. Dinlenme fazı dürüst tabandır (şerit zaten engine-died önceliğiyle
        // kırmızı metni gösterir; bu, o metin temizlendikten SONRA görülecek durumdur).
        else if (Phase == AppPhase.Starting) Phase = RestingPhase;
        IsRunning = false;
        IsStarting = false;
        _currentRunId = null;
        // [C2 fold — A5 review] Engine Sync ortasında ölürse hiçbir syncCompleted/Sync-hatası gelmez; faz
        // Syncing'de asılı kalır ve _syncInFlight sızardı. RunEndingErrorCodes deseniyle simetrik olarak burada
        // da uçuştaki Sync serbest bırakılır.
        ReleaseSyncPhase();
        // Beklenen geçiş kalmadı → sessizlik uyarısının konusu da kalmadı. (Tick zaten aynı sonuca varırdı;
        // burada YAZILMASININ sebebi, kullanıcının Restart'a bastığı KAREde amber satırın kalkmasıdır.)
        EngineOverdueMessage = null;
    }

    /// <summary>
    /// [D1] Motor HİÇ başlatılamadı: Supervisor çalıştırılabiliri uygulamanın yanında yok (eksik/bozuk kurulum —
    /// tipik olarak publish çıktısına <c>supervisor\</c> klasörü girmemiş). Kullanıcı SESSİZ kalmaz: şerit
    /// kalıcı hata moduna girer (engine-died ile AYNI görsel yol) ama "Restart engine" GİZLENİR — yeniden
    /// başlatmak eksik dosyayı geri getirmez. Tam yol yalnız konsol anlatısına düşer (şerit tek satır kalır).
    /// <para><b>Tek sinyal:</b> child process hiç doğmadığı için <see cref="EngineHost.EngineExited"/> ateşlenmez;
    /// bu yol <see cref="OnEngineExited"/> ile ASLA çakışmaz (çağıran yalnız
    /// <see cref="Services.EngineUnavailableException"/> dalında buraya girer).</para>
    /// </summary>
    /// <param name="exePath">Aranan Supervisor exe yolu (konsol satırında gösterilir).</param>
    /// <param name="reason">[D1 review · A2] Dosya yok mu, yoksa var ama başlatılamadı mı — şerit metnini ayırır.</param>
    public void OnEngineUnavailable(string exePath,
        Services.EngineUnavailableReason reason = Services.EngineUnavailableReason.NotFound)
    {
        EngineRestartable = false;
        EngineDiedMessage = reason == Services.EngineUnavailableReason.NotFound
            ? EngineMissingMessage
            : EngineCannotStartMessage;
        // Konsola tam yol + kültürden bağımsız bir tanı kodu; OS'in (yerelleştirilmiş) Win32 metni ASLA
        // gösterilmez — uygulama İngilizce-only [A3].
        AppendRunLine(reason == Services.EngineUnavailableReason.NotFound
            ? $"[error] engine not found: {exePath}"
            : $"[error] engine could not start: {exePath}");
    }

    /// <summary>[About] Motorun bildirdiği sürüm — About'un Environment sekmesi bunu gösterir. Motor
    /// doğmadan önce <c>null</c>'dır.</summary>
    public string? EngineVersion { get; private set; }

    /// <summary>[About] Motor process'inin PID'i (<c>EngineReadyEvent.Pid</c>). Bu değer olaydan okunup
    /// ATILIYORDU; artık tanı raporunda görünür.</summary>
    public int? EnginePid { get; private set; }

    /// <summary>[D1 review · C5] Motor hazır: konsolun boot satırında sürüm gösterilir (design-v1 §2.5 anlatı
    /// dili — "Build started — 14 projects, parallelism 4" ile aynı kalıp). Sürüm kimliği TEK kaynaktan gelir:
    /// <c>Directory.Build.props</c> → Supervisor assembly'sinin InformationalVersion'ı → <c>engineReady</c>.
    /// <para>[About] Sürüm ve PID ayrıca SAKLANIR (Environment sekmesi okur); boot satırı DEĞİŞMEDİ.</para></summary>
    public void OnEngineReady(string engineVersion, int pid)
    {
        EngineVersion = engineVersion;
        EnginePid = pid;
        AppendRunLine($"Engine ready — v{engineVersion}");
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
        // [design v1.7.0 §2.5] Anlatı satırı YALNIZ METİNDİR. Duvar saati kaldırıldı: gerçek bir koşuda
        // saniyede yüzlerce satır akar ve her satırın başındaki damga bilgi taşımıyordu — zaman tek yerde
        // durur (event stream + şeritteki geçen süre). Satır türünü yalnız RENK ayırır; ham MSBuild
        // (OnProjectLog) zaten bu yoldan geçmez.
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

    /// <summary>[D4/Solution B — reseed flicker] MainWindow'un "Back" akışının kullandığı tohumlama metodu.
    /// Doküman TIKLAMA ANINDA UI thread'inde SENKRON kurulur (başlık ve gövde AYNI karede değişir): bu metot UI
    /// thread'inde çağrılır ve <paramref name="applyNow"/>'ı run dokümanının TAZE snapshot'ıyla senkron çağırır
    /// (çağıran doğrudan <c>ConsoleView.ShowRunDocument</c>'a verir). Ayrıca <see cref="ConsoleBatcher.PostReseedDrop"/>
    /// ile bir drop-only sentinel yazar: pump, snapshot'a zaten dahil olan uçuştaki satırları atar (doküman-set
    /// yapmaz — o zaten senkron olduğundan).
    ///
    /// <para><b>Sıralama (atomiklik):</b> snapshot okuma + <c>PostReseedDrop</c> (nesli ilerletir → sentinel) TEK
    /// _gate kilidi altında ATOMİKTİR — OnProjectLog da _runText yazımını ve <c>_console.Post</c>'unu AYNI _gate
    /// altında yaptığından, snapshot'a giren her satır sentinel'den ÖNCE kanaldadır. <b>[D4 review §1]</b>
    /// <paramref name="applyNow"/> (WPF doküman rebuild'i) artık _gate DIŞINDA çağrılır: generation guard,
    /// correctness'i "applyNow boyunca kilidi tutup post'ları bloke etme"den AYIRDIĞINDAN marshal-free OnProjectLog
    /// hot-path'i WPF rebuild süresince bloklanmaz. Güvenlik: reseed'den ÖNCE drenajlanan (eski nesil) satırlar,
    /// koşullarına bakılmaksızın MainWindow.AppendConsoleBatch'te <c>batchGen &lt; CurrentReseedGen</c> ile ATILIR;
    /// applyNow sonrası Post edilen (yeni nesil) satırların flush'ı UI thread'inde applyNow'ın ARDINA sıralanır
    /// (Dispatcher.InvokeAsync tek-thread FIFO — applyNow'ı ÖNceleyemez) → taze dokümana akar (dup/kayıp yok).</para></summary>
    public void SeedRunDocument(Action<string> applyNow)
    {
        string snapshot;
        lock (_gate)
        {
            snapshot = _runText.ToString();
            _console.PostReseedDrop();
        }
        applyNow(snapshot); // [D4 review §1] _gate DIŞINDA — hot-path'i WPF rebuild boyunca bloklamaz (guard güvenli kılar)
    }

    /// <summary>[D4/Solution B] Proje kartına tıklama akışının tohumlama metodu — bkz. <see cref="SeedRunDocument"/>'ın
    /// XML yorumu (aynı senkron doküman-set + drop-only sentinel + generation guard gerekçesi, proje dokümanı için).
    /// Log yoksa boş snapshot ile çağrılır (çağıran boş-durum metnini uygular).</summary>
    public void SeedProjectDocument(string projectId, Action<string> applyNow)
    {
        string snapshot;
        lock (_gate)
        {
            snapshot = _projectText.TryGetValue(projectId, out var sb) ? sb.ToString() : "";
            _console.PostReseedDrop();
        }
        applyNow(snapshot); // [D4 review §1] _gate DIŞINDA — bkz. SeedRunDocument gerekçesi
    }

    /// <summary>Konsolu run dokümanına döndürür (MainWindow'daki "Back").</summary>
    public void ShowRun() => ActiveProjectId = null;

    /// <summary>[D4 review §3] Kart seçimi değiştiğinde konsolun izleyeceği aksiyonun SAF kararı — MainWindow'un
    /// <c>OnSelectedProjectChangedAsync</c> orkestrasyonundan çıkarılan test edilebilir seam (Window DI olmadan
    /// kurulamaz). Seçim yoksa run anlatısına dön; varsa o projenin logunu yükle.</summary>
    public ConsoleSelection NextConsoleSelection(out string? projectId)
    {
        projectId = SelectedProjectId;
        return projectId is null ? ConsoleSelection.ShowRun : ConsoleSelection.LoadProjectLog;
    }

    // ---------------------------------------------------------------- [E1/T67] OS eylemleri (Reveal / Open-in-VS)

    /// <summary>[E1/T67] Bir projenin Open-in-VS adaylarını çözer: kart yalnız İLK sln adını saklar (T32), Open-in-VS
    /// ise projenin TÜM <see cref="ProjectNode.SolutionNames"/>'ini topolojiden çözüp eşleşen <see cref="Solutions"/>
    /// girdilerini döndürür. Bilinmeyen proje / sln'i olmayan proje → boş.</summary>
    public IReadOnlyList<SolutionRef> SolutionCandidatesFor(string projectId)
    {
        var node = Topology.FirstOrDefault(n => string.Equals(n.Id, projectId, StringComparison.OrdinalIgnoreCase));
        if (node is null || node.SolutionNames.Count == 0) return [];
        var names = new HashSet<string>(node.SolutionNames, StringComparer.OrdinalIgnoreCase);
        return Solutions.Where(s => names.Contains(s.Name)).ToList();
    }

    /// <summary>[E1/T67] Satırın klasör ikonunun eylemi: dosyayı Explorer'da seçili açar (satır Id'si = csproj yolu)
    /// + verbatim dim not. <see cref="_osActions"/> null ise güvenle no-op.</summary>
    public void RevealProjectInExplorer(string projectId)
    {
        if (_osActions is null) return;
        _osActions.RevealInExplorer(projectId);
        AppendRunLine($"{ShortNameFor(projectId)}.csproj revealed in Explorer"); // BİREBİR (brief §4)
    }

    /// <summary>[E1/T67] Satırın VS ikonunun eylemi. Adaylar çözülüp <see cref="IOsActions.OpenInVisualStudio"/>'ya
    /// delege edilir:
    /// <list type="bullet">
    /// <item><b>Opened</b> → verbatim opened not, <c>null</c> döner (chooser gerekmez).</item>
    /// <item><b>NeedsChoice</b> → chooser adayları döner (ProjectRow popover'ı açar), not YAZILMAZ.</item>
    /// <item><b>NoSolution</b> → <c>null</c>, not YAZILMAZ.</item>
    /// <item><b>VisualStudioNotFound</b> → <c>null</c>, opened notu YAZILMAZ; makul bir dim başarısızlık notu (pinlenmemiş).</item>
    /// </list></summary>
    public async Task<IReadOnlyList<SolutionRef>?> OpenProjectInVisualStudioAsync(string projectId)
    {
        if (_osActions is null) return null;
        var result = await _osActions.OpenInVisualStudioAsync(SolutionCandidatesFor(projectId));
        switch (result.Outcome)
        {
            case Services.OpenInVsOutcome.Opened:
                AppendOpenedNote(projectId);
                return null;
            case Services.OpenInVsOutcome.NeedsChoice:
                return result.Candidates;
            case Services.OpenInVsOutcome.VisualStudioNotFound:
                AppendRunLine($"Visual Studio not found — install it to open {ShortNameFor(projectId)}");
                return null;
            default: // NoSolution
                return null;
        }
    }

    /// <summary>[E1/T67] Chooser'dan seçim: seçilen tek solution VS'de açılır + Opened ise verbatim opened not.</summary>
    public async Task OpenSolutionInVisualStudioAsync(string projectId, SolutionRef chosen)
    {
        if (_osActions is null) return;
        var result = await _osActions.OpenInVisualStudioAsync([chosen]);
        if (result.Outcome == Services.OpenInVsOutcome.Opened)
            AppendOpenedNote(projectId);
        else if (result.Outcome == Services.OpenInVsOutcome.VisualStudioNotFound)
            AppendRunLine($"Visual Studio not found — install it to open {ShortNameFor(projectId)}");
    }

    private void AppendOpenedNote(string projectId) =>
        AppendRunLine($"{ShortNameFor(projectId)} opened in Visual Studio"); // BİREBİR (brief §4 — .csproj YOK)

    /// <summary>Projenin kısa adı (kart <c>Name</c>'i): önce açık satır, sonra topoloji düğümü, yoksa dosya adı.</summary>
    private string ShortNameFor(string projectId)
    {
        var row = Projects.FirstOrDefault(p => string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase));
        if (row is not null) return row.Name;
        var node = Topology.FirstOrDefault(n => string.Equals(n.Id, projectId, StringComparison.OrdinalIgnoreCase));
        return node?.Name ?? Path.GetFileNameWithoutExtension(projectId);
    }

    /// <summary>[D4 review §2/§3] <see cref="LoadProjectLogAsync"/> bittikten sonra proje-log gösterilmeli mi:
    /// yalnız (guard1) yükleme proje modunu GERÇEKTEN kurduysa — <see cref="ActiveProjectId"/>==<paramref name="projectId"/>,
    /// yani log vardı ve §2 koşullu-set'i modu kurdu (no-log/skipped/deselect-mid-load'da kurulmaz) — VE (guard2)
    /// seçim HÂLÂ o projede ise (arada başka karta/deselect'e geçilmedi). §2 donma yarışı: deselect'te ActiveProjectId
    /// zaten null kaldığından guard1 tek başına da yakalar; iki guard MainWindow'daki üretim sırasını birebir yansıtır
    /// (guard'lar burada toplanır — caller yalnız kararı uygular).</summary>
    public bool ShouldShowLoadedProject(string projectId)
        => string.Equals(ActiveProjectId, projectId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(SelectedProjectId, projectId, StringComparison.OrdinalIgnoreCase);

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
            AppendRunLine($"[error] could not load project log: {ex.Message}");
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
            // Dikiş HER ZAMAN yapılır (log re-select için hazır kalsın — deselect edilmiş olsa bile).
            _projectText[e.ProjectId] = stitched;
            _projectLineCount[e.ProjectId] = CountLines(stitched); // [T56/3a] dikilmiş tam log satır sayısı
            // [D4 review §2] ActiveProjectId (mod) YALNIZCA yükleme HÂLÂ isteniyorsa — kart hâlâ seçiliyse — kurulur.
            // Hızlı select→deselect'te (kart A seç → IPC dönmeden bırak/geri) gecikmiş chunk eskiden ActiveProjectId'yi
            // "A"ya set edip TAKILI bırakıyordu; AppendRunLine (ActiveProjectId null gate'i, ~satır 879) sonrasında
            // HİÇBİR anlatı satırını post edemez → run konsolu SESSİZCE DONARDI. SelectedProjectId UI-thread değeri,
            // OnProjectLogChunk da (marshal'lı) UI thread'inde → _gate altında okumak tutarlı (concurrency yok). Set
            // HÂLÂ aynı _gate + _projectText snapshot'ıyla birlikte → T3b üçüncü-aralık atomikliği KORUNUR (OnProjectLog
            // ya bu bloktan ÖNCE [satır snapshot'ta] ya da SONRA [ActiveProjectId güncel] koşar — üçüncü aralık yok).
            if (string.Equals(SelectedProjectId, e.ProjectId, StringComparison.OrdinalIgnoreCase))
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
