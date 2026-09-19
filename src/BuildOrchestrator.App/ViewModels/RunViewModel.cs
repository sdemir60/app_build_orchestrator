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
using BuildOrchestrator.Core.Paths;
using BuildOrchestrator.Core.Planning;
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
    [NotifyPropertyChangedFor(nameof(VisualStatus))]
    private ProjectRowState _state;

    /// <summary>[Fix wave 1 · D1 review Finding 1] Bu proje topolojide bir cycle (SCC) üyesi mi —
    /// <see cref="ProjectNode.InCycle"/>'dan topoloji uzlaştırmasında (<see cref="RunViewModel.OnWorkspaceTopology"/>)
    /// taşınır (tıpkı <see cref="SolutionName"/> gibi). Cycle üyeleri motor tarafından pre-skip edilir.
    /// <para>[design v1.12.0] Bayrak <see cref="Status"/>'u EZMEZ (v1.11.0 o ezmeyi kaldırdı): statü yalnız
    /// "bu koşuda ne oldu"yu söyler. Üyelik iki yerde görünür — listede tek amber uyarı üçgeni, grafta
    /// amber küp (design v1.20.0 §2.3: üyede HER durumda; <see cref="VisualStatus"/>'u değiştirmez,
    /// <see cref="GraphBinder"/> düğüme ayrı taşır).</para></summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private bool _inCycle;

    /// <summary>[Fix wave 1 · D1 review Finding 1] Bir run uçuşta mı (<see cref="RunViewModel.IsRunning"/> ||
    /// <see cref="RunViewModel.IsStarting"/>) — <see cref="RunViewModel"/> her satıra iter (IsSelected deseni).
    /// <c>queued</c> = "planlanmış ama henüz başlamamış" YALNIZ bir run uçuştayken görünür bir durumdur; bu bayrak
    /// olmadan Pending bir satır ölü envanterden (Discovered) ayırt edilemez.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(VisualStatus))]
    private bool _isRunActive;

    /// <summary>[tek proje · design §3.8] Bir koşu KİLİTLİ mi (<see cref="RunViewModel.IsMidRunLocked"/> —
    /// planlama penceresi DAHİL) — <see cref="RunViewModel"/> her satıra iter (<see cref="IsRunActive"/>
    /// deseni). Kart bunu play düğmesinin tooltip'i için okur: kilitliyken <c>Build in progress — wait or
    /// stop it first</c>. <see cref="IsRunActive"/>'den AYRIDIR: o yalnız <c>runStarted</c>'dan sonra true olur,
    /// oysa ikinci bir koşu daha tıklama anından itibaren başlatılamaz.</summary>
    [ObservableProperty] private bool _isRunLocked;

    /// <summary>[tek proje · design §3.8] Bu satır, uçuştaki kapsamlı koşunun HEDEFİ mi —
    /// <see cref="RunViewModel.RunTargetId"/>'den her satıra itilir. Hedef satırda play ikonu kırmızı
    /// <b>Stop</b>'a döner ve hover olmadan da görünür kalır; koşu bitince (her çıkış yolundan) düşer.</summary>
    [ObservableProperty] private bool _isRunTarget;

    /// <summary>[Harici projeler] Bu satır ana repo DIŞINDAN gelen bir projeyi mi anlatıyor —
    /// <see cref="ProjectNode.IsExternal"/>'den topoloji uzlaştırmasında taşınır ve satır ömrü boyunca
    /// değişmez (kimlik gibi).
    /// <para>Tek görünür sonucu şudur: ana reponun hedef commit'i bu satıra İTİLMEZ. O sha başka bir repoyu
    /// anlatır ve harici satırın yanında duran bir yalan olurdu.</para></summary>
    public bool IsExternal { get; init; }

    /// <summary>[T53-UI] Kartın soluk ikinci satırı — projenin ait olduğu solution'ın adı (prototip
    /// <c>p.sln</c>, BuildApp.jsx:384). Kaynak: <see cref="ProjectNode.SolutionNames"/> (ilk eleman); topoloji
    /// kurulurken atanır. Bir projeyi birden çok .sln içerebilir — kart tek (ilk) adı gösterir.</summary>
    [ObservableProperty] private string? _solutionName;

    /// <summary>[T53-UI][W1/It-5] Projenin SON BAŞARIYLA DERLENDİĞİ revizyon. Kaynak
    /// <see cref="BuildPreviewItem.BuiltCommit"/>'tir (yani <c>BuildState.BuiltCommit</c>); hem Sync hem
    /// run-başı önizlemesinden gelir. Değer HAM'dır (40-hex sha) — kısaltma bir GÖRÜNTÜ
    /// kararıdır. <b>Hiç derlenmemiş</b> proje ⇒ <c>null</c> (uydurulmaz).
    ///
    /// <para><b>[DEĞİŞEN KURAL — v1.16.0]</b> Bu değer artık SATIRDA GÖSTERİLMEZ; satırın sağ yuvasında
    /// kararın gerekçesi durur (bkz. <see cref="DecisionLabel"/>). Revizyon yalnız proje logu başlığındaki
    /// "Last successful build" satırını besler. Eski çift (<c>a3f81c2 → b7e91d4</c>) kararı ANLATMIYORDU:
    /// sağ yarı pull edilmemiş bir UZAK commit'ti, sol yarı ise projeye değil repoya aitti — ikisi de "bu
    /// proje neden derlenecek" sorusunu cevaplamıyordu. Bu yüzden satırın <c>TargetSha</c> alanı da
    /// KALDIRILDI: hedef commit motorda kalır (konsol satırı ve pull için), satıra itilmez.</para></summary>
    [ObservableProperty] private string? _currentSha;

    /// <summary>[v1.16.0] Son BAŞARILI derlemenin zamanı — satırın <c>up to date · 2h</c> etiketindeki göreli
    /// yaş ve proje logunun "Last successful build" satırı buradan. Kaynak
    /// <see cref="BuildPreviewItem.LastBuiltAt"/>; hiç başarıyla derlenmemiş projede <c>null</c>.</summary>
    [ObservableProperty] private DateTimeOffset? _lastBuiltAt;

    /// <summary>[v1.16.0] Projenin KENDİ girdi dosyaları son derlemeden bu yana değişti mi — etiketin
    /// <c>modified</c> (kendi dosyası) / <c>affected</c> (yalnız bağımlılığı) ayrımı. Kaynak
    /// <see cref="BuildPreviewItem.OwnFilesChanged"/>; bilinmiyorsa <c>null</c>.</summary>
    [ObservableProperty] private bool? _ownFilesChanged;

    /// <summary>[Faz 3 — spec 2026-09-18 §5, P8, Task 7] Proje bu araç dışında derlenmiş ve çıktısı güncelse
    /// (<see cref="WillBuildReason.BuiltOutside"/>) derleme kanıtının zamanı — etiketin <c>up to date · built
    /// outside this tool 5m ago</c> yaşı ve proje sayfasının kanıt satırı buradan. Kaynak
    /// <see cref="BuildPreviewItem.OutputBuiltAt"/>; diğer her gerekçede <c>null</c>. Bu araç projeyi
    /// başarıyla derlediği an eski kanıt geçersizleşir ve <c>null</c>'a çekilir (artık aracın kendi çıktısı).</summary>
    [ObservableProperty] private DateTimeOffset? _outputBuiltAt;

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
    [NotifyPropertyChangedFor(nameof(Standing))]
    [NotifyPropertyChangedFor(nameof(VisualStatus))]
    private bool? _willBuild;

    /// <summary><see cref="WillBuild"/>'in GEREKÇESİ — will-build noktasının tooltip'i bunu söyler.
    /// <see cref="BuildPreviewEvent"/> ile gelir; bilinmiyorsa null (yüzey jenerik metne düşer).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Standing))]
    [NotifyPropertyChangedFor(nameof(VisualStatus))]
    [NotifyPropertyChangedFor(nameof(WarningRoots))] // defter notu üçgeni (spec 2026-09-18 §1-15)
    [NotifyPropertyChangedFor(nameof(HasDepIssue))]
    private WillBuildReason? _willBuildReason;

    /// <summary>[design v1.20.0 §2.3] Çıktının durumu — görsel durumun taban katmanı. Önizleme kararından
    /// (<see cref="WillBuild"/> + <see cref="WillBuildReason"/>) TEK eşleme yerinde türetilir
    /// (<see cref="Controls.StandingStatuses.From"/>); karar yoksa <see cref="Controls.StandingStatus.Unknown"/>.</summary>
    public Controls.StandingStatus Standing => Controls.StandingStatuses.From(WillBuild, WillBuildReason);

    /// <summary>[Task 4 · koşullu yeniden derleme] Bu KOŞU bu satırı GERÇEKTEN koşullu mu değerlendiriyor —
    /// <see cref="BuildPreviewItem.Conditional"/>'dan AYNEN (<see cref="RunViewModel.OnBuildPreview"/>). <c>true</c>
    /// yalnız Build/Cycles'ta, kapsam zorlanmamışken (satırdan Build DEĞİL) ve bir SCC üyesi değilken —
    /// <see cref="BuildOrchestrator.Core.Planning.ConditionalRebuild.AppliesTo"/> (Core) kararı.
    /// <see cref="RunViewModel.ScopeFor"/>'un dalgası
    /// ve <see cref="RunViewModel.InRunQueueFor"/>'un kuyruğu AYNI bayrağı okur (tek doğruluk kaynağı, kopya
    /// YASAK): koşullu proje ne dalgada ne kuyruktadır — WillBuild=true olsa da KESİN değildir.
    /// <see cref="ViewModels.DecisionLabel"/> da bunu okur: <see cref="WillBuildReason.WaitingForDependency"/>
    /// TEK BAŞINA "bekliyor" demez, bu koşu GERÇEKTEN bekletiyorsa der.</summary>
    [ObservableProperty] private bool _conditional;

    /// <summary>[Task 4 · koşullu yeniden derleme] <see cref="WillBuildReason.WaitingForDependency"/> iken
    /// defterdeki kök bağımlılıkların GÖRÜNEN adları — <see cref="ViewModels.DecisionLabel"/>'in tooltip'i
    /// bunları yazar (<see cref="BuildPreviewItem.DependencyRoots"/>'tan AYNEN). Diğer gerekçelerde null.
    /// Koşu listesi boşken uyarı üçgeninin kökleri de bunlardır (<see cref="WarningRoots"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WarningRoots))]
    [NotifyPropertyChangedFor(nameof(HasDepIssue))]
    private IReadOnlyList<string>? _dependencyRoots;

    /// <summary>[Task 1 — kök neden A] Bu satır ŞU AN KOŞAN run'ın KENDİ kuyruğunda mı — <see cref="Status"/>'un
    /// <c>Queued</c> dalı bunu okur, <see cref="WillBuild"/>'i DEĞİL. YALNIZ bu koşunun
    /// <see cref="BuildPreviewEvent"/>'inden yazılır (<see cref="RunViewModel.OnBuildPreview"/>,
    /// <see cref="RunViewModel.InRunQueueFor"/> — kuyruk üyeliğinin TEK karar yeri). Sıfırlanmanın TEK başlangıç
    /// noktası <see cref="RunViewModel.OnRunStarted"/>'ın kendi (moddan bağımsız) döngüsüdür, TEK bitiş noktası
    /// <see cref="RunViewModel.PropagateRunActive"/> (<see cref="IsRunActive"/> düşerken) — <see cref="RunViewModel.NeutralizeRows"/>
    /// buna BİLEREK DOKUNMAZ (review fix M-2: iki nokta zaten kopya olurdu; bkz. o metodun yorumu).
    /// <para><b>[DEĞİŞEN KURAL — Task 1]</b> Eskiden <see cref="Status"/>'un Queued dalı doğrudan
    /// <see cref="WillBuild"/>'i okurdu — genel plan bayrağı, ait olduğu koşuyu BİLMEZ. Tek proje koşusunda
    /// motorun önizlemesi yalnız hedefi taşır (§8.1, <c>ProjectRunScope</c>); diğer satırların WillBuild'i
    /// Sync'ten kalan bayat değerdi ve nötrleme onu KASITLI korurdu (plan, kapsam hesabı için ayrı yaşamalı) —
    /// sonuç, koşu boyunca ilgisiz satırların da amber yanması ve koşu bitince griye dönmesiydi (ölçülen kusur,
    /// bkz. <c>.claude/outputs/2026-09-15-16-06-run-scope-and-queued-colour-investigation.md</c> §2.1).
    /// <c>InRunQueue</c> ayrı bir kanaldır: <see cref="WillBuild"/> kapsam hesabı (<see cref="RunViewModel.ScopeFor"/>)
    /// ve karar etiketi için YAŞAMAYA devam eder, kuyruk rengi artık yalnız BU koşunun kendi cevabını
    /// okur.</para>
    /// <para><b>[DEĞİŞEN KURAL — Task 2]</b> Cycles modunda kapsam İÇİNDE olmak (WillBuild=true, motor gerçekten
    /// derleyecek) kuyruğa girmek için YETMEZ: yalnız döngü üyeleri (<see cref="InCycle"/>) kuyruktadır, bayat
    /// bir kapsam-içi upstream bağımlılık gri bekler. Bkz. <see cref="RunViewModel.InRunQueueFor"/>.</para></summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(VisualStatus))]
    private bool _inRunQueue;

    /// <summary>[Task 17] BU KOŞUDA tespit edilen dependency-uyarısı kök adları (ör. "B", "C") — boşsa/hiç
    /// gelmediyse null. <see cref="ProjectSucceededEvent.DepIssues"/>/<see cref="ProjectFailedEvent.DepIssues"/>'tan
    /// doğrudan taşınır. Bir koşu alanıdır: nötrleme onu siler (defter notu <see cref="WarningRoots"/>'ta
    /// yaşamaya devam eder).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRunDepIssue))]
    [NotifyPropertyChangedFor(nameof(WarningRoots))]
    [NotifyPropertyChangedFor(nameof(HasDepIssue))]
    private IReadOnlyList<string>? _depIssues;

    /// <summary>[R-D144] Koşu hikâyesinin sorusu: BU koşu bu satırda bir bağımlılık sorunu gördü mü. Yalnız
    /// şeridin koşu özeti ("(N dependency-affected)", <see cref="RunCounters.DepAffected"/>) bunu okur.</summary>
    public bool HasRunDepIssue => DepIssues is { Count: > 0 };

    /// <summary>[design v1.20.0 §2.4-6 · spec 2026-09-18 §1-15] Uyarı üçgeninin KÖK adları — TEK kaynak (satır
    /// tooltip'i <see cref="RowWarning.For"/>'a bunu verir, sayaç/filtre <see cref="HasDepIssue"/> üzerinden
    /// bunu sayar). Bu koşunun listesi varsa o (en taze kanıt); yoksa defterdeki bağımlılık notunun kökleri
    /// (<see cref="WillBuildReason.WaitingForDependency"/> iken <see cref="DependencyRoots"/>). Üçgen bu yüzden
    /// KÜMÜLATİFTİR: bir sonraki koşunun nötrlemesi koşu listesini siler, defter notu durdukça üçgen kalır.</summary>
    public IReadOnlyList<string>? WarningRoots =>
        HasRunDepIssue ? DepIssues
        : WillBuildReason == Contracts.Model.WillBuildReason.WaitingForDependency ? DependencyRoots
        : null;

    /// <summary>[Task 17 · spec 2026-09-18 §1-15] ▲ sinyali — DURUM yüzeyleri (⚠ chip'i, <c>warn</c> filtresi)
    /// bunu okur: <see cref="WarningRoots"/> boş değilse true.
    /// <para><b>[DEĞİŞEN KURAL — spec 2026-09-18 §1-15]</b> Eski tanım yalnız bu koşunun
    /// <see cref="DepIssues"/>'ıydı: bir sonraki işlemin nötrlemesi üçgeni silerdi, defterde duran "hatalı
    /// bağımlılığa karşı derlendi" notu ise Sync'ten sonra hiç görünmezdi. Üçgen artık defter notunu da
    /// taşır; koşu özetinin sorusu <see cref="HasRunDepIssue"/>'ya ayrıldı (R-D144).</para></summary>
    public bool HasDepIssue => WarningRoots is { Count: > 0 };

    /// <summary>[spec 2026-09-18 §1-14] Kanıtlı son hatanın zamanı — <c>failed · 2h</c> etiketinin yaşı.
    /// Kaynak önizlemedir (<see cref="BuildPreviewItem.FailedAt"/>, <c>BuildStateStore.FailedAtOf</c>); koşu
    /// içinde kanıtlı hata onu ŞİMDİ'ye, başarı ve kanıtsız hata <c>null</c>'a çeker
    /// (<see cref="RunViewModel.OnProjectDone"/>). Çıktı durumunun parçasıdır: nötrleme dokunmaz.</summary>
    [ObservableProperty] private DateTimeOffset? _failedAt;

    /// <summary>[spec 2026-09-18 §4 <c>local</c>] Projenin girdilerinden en az biri <c>git status</c>'ta kirli
    /// mi. YALNIZ koşu dışındaki önizlemeden yazılır (Sync; koşu önizlemesi alanı hep <c>false</c> gönderir —
    /// bkz. <see cref="RunViewModel.OnBuildPreview"/>). Nötrleme dokunmaz.</summary>
    [ObservableProperty] private bool _localEdits;

    /// <summary>Motor bu projeyi bu koşunda ATLADIYSA gerekçesi (<see cref="SkipReasons"/> — tek doğruluk
    /// kaynağı); atlanmadıysa null. <b>Neden satırda tutuluyor:</b> proje sayfası logu olmayan bir projede "neden
    /// boş" sorusunu cevaplamak zorundadır ve atlanmış bir projenin log dosyası HiÇ yoktur — gerekçe yalnız
    /// event'te geçip atılıyordu (bkz. <see cref="Console.ConsoleEmptyState.ForEmptyLog"/>).</summary>
    [ObservableProperty] private string? _skipReason;

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
    [NotifyPropertyChangedFor(nameof(VisualStatus))]
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
    /// <see cref="InCycle"/> + <see cref="InRunQueue"/> + <see cref="IsRunActive"/> sinyallerinin TEK eşleme yeri
    /// (kart yalnız bunu okur; eşleme mantığı kontrolde kopyalanmaz). <c>cycle</c> ve <c>queued</c> ayrı IPC
    /// alanları TAŞIMAZ — ikisi de eldeki topoloji/run sinyallerinden TÜRETİLİR:
    /// <list type="bullet">
    /// <item><b>cycle</b>: <see cref="InCycle"/>=true olan, bu koşu hakkında HENÜZ BİR ŞEY SÖYLENMEMİŞ satır.
    /// Bkz. aşağıdaki "döngü glyph'i koşu-öncesidir" notu.</item>
    /// <item><b>queued</b>: bir run uçuştayken (<see cref="IsRunActive"/>) BU koşunun kendi buildPreview'inin
    /// planladığı (<see cref="InRunQueue"/>==true — <see cref="WillBuild"/> DEĞİL, bkz. o alanın XML yorumu)
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
        // [Task 1 — DEĞİŞEN KURAL] WillBuild==true DEĞİL: o genel plan bayrağıdır ve BU koşuyu bilmez (bkz.
        // InRunQueue'nun XML yorumu). Kuyruk artık yalnız bu koşunun kendi buildPreview'inden gelir.
        _ when IsRunActive && InRunQueue => Controls.GraphStatus.Queued,
        // [design v1.7.0 §5] Döngü ÜYELİĞİ bir statü DEĞİLDİR: kalıcı bir yapısal özelliktir ve kendi
        // kanalında (nokta + uyarı üçgeni + graf çekirdeği) yaşar. Statü kanalı yalnız "bu koşuda ne oldu"yu
        // söyler; üyelik onu asla ezmez — eskiden Pending bir üye Cycle statüsüne düşüyor ve satır
        // "derlenmedi mi, atlandı mı, hiç görülmedi mi" sorusuna cevap veremiyordu.
        _ => Controls.GraphStatus.Discovered,
    };

    /// <summary>[design v1.11.0 §9-4] Bu satır YÜRÜYEN işlemin kapsamında mı — açılış koreografisinin
    /// dalgasında amber'a yanan küme. Koşu başlayınca statü kanalı devralır (queued/building/sonuç), bu yüzden
    /// bayrak yalnız <c>discovered</c> satırlarda görünür bir fark yaratır.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisualStatus))]
    private bool _marked;

    /// <summary>[design v1.20.0 §2.3] Satırın TEK görsel durumu — şerit, nokta, glyph, ad vurgusu ve graf
    /// node'u hepsi bunu okur: çıktı durumu (<see cref="Standing"/>) + koşu bindirmesi (<see cref="Status"/>) +
    /// işaretlilik. Eşleme <see cref="Controls.VisualStatuses.For"/>'dadır; kart kendi tablosunu KURMAZ.
    /// <para><b>[DEĞİŞEN KURAL — design v1.20.0 §2.3]</b> Eski girdiler <c>Fresh</c> (başlangıç modu) ve
    /// <see cref="InCycle"/> (amber küp) idi. Başlangıç modu artık kararın yokluğudur (<see cref="Standing"/>),
    /// döngü küpü ise durumdan bağımsızdır ve düğüme ayrı taşınır. <c>Fresh</c> bayrağı bu yüzden tamamen
    /// kalktı: başlangıç modunu okuyan her yüzey <see cref="Controls.VisualStatuses.IsStartMode"/>'u bu
    /// durumdan sorar.</para></summary>
    public Controls.VisualStatus VisualStatus => Controls.VisualStatuses.For(Status, Standing, Marked);

    /// <summary>[design v1.13.2 §9-4 · §2.4 · §3.2] Açılış koreografisinin satıra düşen payı: hedef opaklık +
    /// o opaklığa giden geçişin süresi. <b>Değer koreografi boyunca <see cref="RowFade.None"/>'da SABİTTİR</b>
    /// — <see cref="Services.OperationChoreographer"/> her adımda bunu yazar, satır opaklığı hiç oynamaz.
    /// <para><b>[DEĞİŞEN KURAL — v1.13.2, ölçüm]</b> "Koşu zaten başlamış olduğu için listede ikinci bir
    /// sönme okunmuyordu." Eski kural: satırlar node'larla SENKRON sönerdi (kapsam 0.45'e 440ms'de, kapsam
    /// dışı 0.3'e 1120ms'de — ikisi aynı anda biter) ve koşu başlayınca tam opaklığa dönerlerdi. Sönme/geri
    /// gelme artık YALNIZ graf node'larında yaşıyor.</para>
    /// <para>Değer satıra İTİLİR (<see cref="NamePrefix"/> deseni): 200 satırın
    /// <c>RunViewModel</c>'e tek tek abone olması yerine sürücü tek tek yazar — satır başına EK abone YOK.</para>
    /// <para><b>Bitiş koreografisi (neon) satırlara UYGULANMAZ</b> (kullanıcı kararı, DEĞİŞMEDİ): liste koşu
    /// bitiminde sabit kalır, koreografi yalnız grafta yaşar.</para></summary>
    [ObservableProperty] private RowFade _fade = RowFade.None;

    public ProjectRowViewModel(string id, string name, ProjectRowState state, string? solutionName = null)
    {
        Id = id;
        Name = name;
        _state = state;
        _solutionName = solutionName;
    }
}

public enum ProjectRowState { Pending, Started, Succeeded, Failed, Skipped }

/// <summary>[design v1.11.0 §9-4] Bir satırın koreografi opaklığı ve ona giden geçişin süresi — TEK
/// bildirimde taşınırlar, çünkü ikisi ayrı yazıldığında satır iki kez animasyon kurardı (ilk yazımda eski
/// süreyle, ikincisinde yeni süreyle).</summary>
/// <param name="Opacity">Hedef opaklık (1 = koreografi yok).</param>
/// <param name="DurationMs">O opaklığa giden geçişin süresi.</param>
public readonly record struct RowFade(double Opacity, double DurationMs)
{
    /// <summary>Koreografi oynamıyor — tam opak, normal geçiş süresi.</summary>
    public static readonly RowFade None = new(1.0, Controls.MarkingChoreography.IdleGlideMs);
}

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
    // [Task 2 review fix M-2] Bu run'ın modu — TEK yazıcı OnRunStarted'dır (koşulsuz, moddan bağımsız döngüyle
    // AYNI noktada). Eskiden Stream.cs partial'ının kendi `_streamRunMode`'u OKUNUYORDU: o alan
    // AppendStreamFor'da (OnEvent'in OnRunStarted'dan SONRA çağırdığı ikinci dal) yazılıyordu — bugün
    // doğruydu (tek çağıranlı test sırası RunStartedEvent→BuildPreviewEvent bunu garantiliyordu) ama satır
    // kararının (InRunQueueFor, OnProjectSkipped) doğruluğu STREAM'in işleme sırasına bağlı kalıyordu; yeni bir
    // event tipi ya da sıra değişikliği sessizce kırabilirdi. Artık TEK alan burada yazılır, Stream.cs kendi
    // `_streamRunMode`'unu SİLİP bunu okur (kopya YASAK).
    private RunMode? _currentRunMode;
    // [Task 2 review fix M-1/I-2] Bu run boyunca (Cycles modunda) SkipReasons.OutOfCycleScope ile bastırılan
    // satır sayısı — _willBuildIds ile AYNI noktada (OnRunStarted) sıfırlanır, run'ın SONUNA kadar birikir
    // (Stream.cs'in KENDİ `_outOfScopeSkips`'i gibi ara ara FLUSH edilmez — o alan yalnız stream'in toplu
    // satırının görüntü tamponudur, kümülatif bir toplam DEĞİLDİR, bu yüzden burada YENİDEN KULLANILAMAZ).
    // İki tüketicisi var: <see cref="UpdateEta"/> (kapsam dışı satırlar hiç terminal olmadığı için "completed"
    // sayısını bunlarla düzeltir) ve run'ın kapanış satırı (motorun kendi <c>Skipped</c> sayısından bunu düşer
    // — bkz. RunViewModel.Stream.cs'in RunCompletedEvent dalı).
    private int _outOfScopeSkipCount;
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
    // Küme her Sync başında temizlenir ve hemen ardından gelen BuildPreviewEvent'ten yeniden dolar: A5
    // amendment'ından beri Sync DE önizleme yayınlar (SyncWorkspaceService), yalnız run başı değil. (Eski bir
    // yorum burada "Sync sonrası GELMEZ" diyordu — bayattı ve grafın besleme boşluğunu araştırırken yanıltıcı
    // oldu.)
    private readonly HashSet<string> _willBuildIds = new(StringComparer.OrdinalIgnoreCase);

    // [final review — C1] Önizlemenin KİRLİ gördüğü her proje (WillBuild==true), KOŞULLU olanlar DAHİL —
    // _willBuildIds'in üst kümesi. İki soru Task 4'ten beri ayrıdır ve ayrı kaynak isterler: "bu koşuda KESİN
    // ne derlenecek" (payda/kuyruk/dalga → _willBuildIds, koşullu HARİÇ) ile "ortada derlenecek bir şey var mı"
    // (AllClean → bu küme). İkisi tek kümeden okunduğunda, dirty kümesi tamamen koşullu olan bir koşu "her şey
    // güncel" raporluyordu: koşullu proje WillBuild=true kalır ve motor sırası geldiğinde kökü sağlıklıysa onu
    // GERÇEKTEN derler (ConditionalRebuild.Decide) — yani MSBuild derlerken şerit "Checking…", bitişte yeşil
    // "Everything up to date" diyordu. Küme _willBuildIds ile AYNI noktalarda (ClearPreviewSets) tazelenir.
    private readonly HashSet<string> _dirtyIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Önizlemeden türeyen İKİ kümeyi birlikte tazeler — ayrı ayrı temizlenebilselerdi biri bayat
    /// kalır ve <see cref="AllClean"/> ile <see cref="WillBuildCount"/> sessizce ayrışırdı. Dört çağıranı da
    /// "eldeki plan artık geçerli değil" demenin bir biçimidir: yeni run, yeni Sync, branch/repo değişimi
    /// (<c>ResetRowsToHollow</c>) ve Clean (<c>ClearPlanSurface</c>).</summary>
    private void ClearPreviewSets()
    {
        _willBuildIds.Clear();
        _dirtyIds.Clear();
    }

    /// <summary>Bir kararı iki kümeye yazmanın TEK yeri — önizleme (<see cref="OnBuildPreview"/>) ve
    /// configuration değişimi (<see cref="SetConfiguration"/>) buradan geçer. <see cref="_dirtyIds"/> "ortada iş
    /// var mı" (koşullu DAHİL), <see cref="_willBuildIds"/> SABİT kesin küme (koşullu HARİÇ — köküyle birlikte
    /// atlanabilir; <see cref="InRunQueueFor"/>'un Build/Rebuild dalıyla AYNI bayrak).</summary>
    private void NotePreviewDecision(string projectId, bool? willBuild, bool conditional)
    {
        if (willBuild != true) return;
        _dirtyIds.Add(projectId);
        if (!conditional) _willBuildIds.Add(projectId);
    }

    /// <summary>
    /// Bir <c>BuildPreviewEvent</c> uygulandı — plan kanalı (<see cref="ProjectRowViewModel.WillBuild"/>)
    /// tazelendi.
    ///
    /// <para>Grafın buna ihtiyacı var ve <see cref="Counters"/> onu TAŞIYAMAZ: sayaç demeti bir
    /// <c>readonly record struct</c>'tır, <see cref="RunCounters.From"/> <c>WillBuild</c>'i hiç okumaz ve
    /// önizleme sonrası değeri birebir aynı kaldığı için <c>PropertyChanged</c> yutulur — graf hiç
    /// uyarılmazdı. Sayaç kanalını "plan da değişti" diye genişletmek de yanlış olurdu: iki proje ters yönde
    /// takas ettiğinde (biri temizlendi, biri kirlendi) sayı yine aynı kalır. Bu yüzden AÇIK bir sinyal.</para>
    /// <para>[Task 4 review I-1] Grafın itişi artık <see cref="RowDecisionsChanged"/>'dedir (hemen önce yayılır);
    /// bu olayın kalan tüketicisi kapsam işaretinin kuyruğa devridir (<c>MainWindow</c>).</para>
    /// </summary>
    public event EventHandler? BuildPreviewApplied;

    /// <summary>[design v1.20.0 §2.3 · Task 4 review I-1] Satırların RENK GİRDİSİ olan karar (<see
    /// cref="ProjectRowViewModel.WillBuild"/> + <see cref="ProjectRowViewModel.WillBuildReason"/> →
    /// <see cref="ProjectRowViewModel.Standing"/>) toplu olarak değişti — graf yeniden beslenmelidir.
    /// <para><b>TEK sinyal:</b> kararları toplu yazan/düşüren HER yol (<see cref="RaiseRowDecisionsChanged"/>'i
    /// çağıranlar: önizleme ve hollow reset) bunu yayar; kabuk yalnız buna abone olur. Ayrı ayrı sinyaller
    /// ölçüldü ve ayrıştı: branch değişimi satırları başlangıç moduna düşürürken graf eski renkte kalıyordu,
    /// çünkü sayaçlar değişmiyordu ve önizleme sinyali o yolda hiç çıkmıyordu.</para></summary>
    public event EventHandler? RowDecisionsChanged;

    /// <summary>Kararlar toplu yazıldıktan/düşürüldükten SONRA çağrılır (kopya YASAK — sinyalin tek yayıcısı).</summary>
    private void RaiseRowDecisionsChanged() => RowDecisionsChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>[Fix wave 1, Finding 2 regression testi] YALNIZ testler için: <see cref="OnProjectLogChunk"/>
    /// dikiş kilidinden çıkar çıkmaz (kilit ne zaman kapansa, kapandığı ANDA) senkron tetiklenir. Üretimde
    /// hep null — sıfır maliyet. Testte, kilit içinde <c>ActiveProjectId</c> atamasının GERÇEKTEN kilitle
    /// birlikte kapandığını (eskiden kilit DIŞINDAYDI — bkz. Finding 2) tek thread'de, sleep/poll OLMADAN
    /// deterministik biçimde kanıtlamak için kullanılır: kanca içinden enjekte edilen bir canlı
    /// <c>ProjectLogEvent</c>, ancak <c>ActiveProjectId</c> zaten güncellenmişse projeye düşer.</summary>
    internal Action? DebugAfterStitchLockExited;

    public ObservableCollection<ProjectRowViewModel> Projects { get; } = [];

    // [A5/T69 · Fix wave 1 — Finding 6] Sync / branch / topoloji yüzeyi AYRI partial dosyada:
    // RunViewModel.Workspace.cs (faz, hedef commit, envanter, topoloji uzlaştırma).

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkspace))]
    // [clean] Clean'in repo kapısı KOMUTTADIR (CanClean → HasWorkspace): bakım kutusu kendi enable'ını
    // yönetmez, tek yazıcı komuttur. Repo değişince buton hâlâ pasif görünmesin diye bildirim buradan gider.
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(OptimizeCommand))]
    [NotifyPropertyChangedFor(nameof(CanSwitchBranch))] // [§6.3] chip kapısının TEK bildirim kaynağı (bkz. CanSwitchBranch)
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

    /// <summary>[D2/T38] Önizleme KİRLİ tek bir proje bile görmedi — şerit faz-metni ve progress kolu bunu okur
    /// (prototip <c>eng.allClean</c>). Bkz. <see cref="RecomputeWillBuildSurface"/>.
    /// <para><b>[final review — C1 · DEĞİŞEN KURAL]</b> Kaynak <see cref="WillBuildCount"/> (KESİN küme) DEĞİL
    /// <see cref="_dirtyIds"/>'tir: koşullu bir proje kesin kümeye girmez ama motor sırası geldiğinde kökü
    /// sağlıklıysa onu derler, yani "kesin küme boş" ile "yapacak iş yok" AYNI SORU DEĞİLDİR. Eski hâlde dirty
    /// kümesi tamamen koşullu olan bir koşu derlerken "Checking…", biterken yeşil "Everything up to date"
    /// diyordu.</para></summary>
    [ObservableProperty] private bool _allClean = true;

    /// <summary>[D2/T38] Derlenecek (willBuild) proje sayısı — koşu boyunca SABİT (prototip <c>wb</c>).</summary>
    [ObservableProperty] private int _willBuildCount;

    /// <summary>[D2/T38] willBuild kümesinden tamamlanan (succeeded/failed/skipped) sayısı (prototip <c>fin</c>).</summary>
    [ObservableProperty] private int _finishedOfWillBuild;

    /// <summary>[D2/T38] Repo seçili mi (prototip <c>workspace</c>) — şerit "Not ready — no repository selected"
    /// davetini bununla ayırt eder.</summary>
    public bool HasWorkspace => RootPath.Length > 0;

    /// <summary>
    /// [D2/T38 · tray indicator/K-5] Şeridin O ANKİ tek satırı: metin + brush anahtarı + statü glyph'i.
    ///
    /// <para><b>Neden VM'de:</b> bu satırın İKİ tüketicisi var — ekrandaki şerit ve (uygulama tepsideyken)
    /// koşu bitişini duyuran OS bildirimi. İkincisi için metni yeniden derlemek, aynı cümlenin iki ayrı yerde
    /// üretilmesi olurdu: biri "3 failed · 24 succeeded" derken diğeri sessizce başka bir şey diyebilirdi.
    /// Girdilerin hepsi zaten buradaki property'lerdir; satırı da burada üretmek tek doğruluk kaynağı bırakır.
    /// (Önceden ifade görünümün içinde, <c>StickyRibbon</c>'da yaşıyordu.)</para>
    ///
    /// <para><b>PropertyChanged YAYMAZ</b> ve yaymamalıdır: değeri besleyen on kadar property'nin her biri zaten
    /// kendi bildirimini yapar; tüketiciler onları dinleyip bunu okur. Ayrı bir bildirim, aynı değişimin ikinci
    /// kez duyurulması olurdu.</para>
    ///
    /// <para><b>Bilinen boşluk (kablo):</b> <c>warnings: 0</c> — App derleyici-warning sayısını izlemiyor
    /// (<c>RunCompletedEvent</c> taşımıyor). Satır bu yüzden " · N warnings" ekini hiç üretmez.</para></summary>
    public RibbonLine RibbonLine => RibbonText.Compose(
        Phase, HasWorkspace, AllClean, Counters,
        WillBuildCount, FinishedOfWillBuild, Counters.Total,
        ElapsedMs, EtaMs, checkDurMs: ElapsedMs, warnings: 0,
        engineDiedMessage: EngineDiedMessage, syncError: SyncErrorMessage,
        runError: RunErrorMessage, engineOverdue: EngineOverdueMessage, syncFetches: _syncMode.Fetches(),
        resolvingCycles: IsResolvingCycles, cycleRound: CycleRound, cycleRoundCap: CycleRoundCap);

    // [Fix wave 1, Finding 1] RelayCommand'ların CanExecuteChanged'ı YALNIZ NotifyCanExecuteChangedFor
    // (veya elle NotifyCanExecuteChanged()) ile ateşlenir — CommunityToolkit CommandManager.RequerySuggested'a
    // ABONE OLMAZ. Bu olmadan Stop/Continue butonları gerçek pencerede İLK bind sonrası ASLA yeniden
    // sorgulanmaz (StopCommand hep disabled kalırdı) — Kısıt 3'ü bozar.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(RebuildProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCyclesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(OptimizeCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(IsMidRunLocked))] // [T12] branch/config kilidi bundan türetilir
    [NotifyPropertyChangedFor(nameof(IsResolvingCycles))] // bakım kutusunun Resolve spinner'ı: koşu bitince iner
    [NotifyPropertyChangedFor(nameof(CanSwitchBranch))] // [§6.3] chip kapısının TEK bildirim kaynağı (bkz. CanSwitchBranch)
    private bool _isRunning;

    // [Fix wave 1(It-3), Finding 3] Supervisor runStarted'dan ÖNCE planlama yapar (scan/graph/topo — 177
    // projeli OSYS'te saniyeler sürebilir) ve stop-during-planning'i AÇIKÇA destekler (ack-debt yolu,
    // RunCoordinator'da test edilmiş). IsRunning yalnız runStarted ile true olduğundan, planlama sırasında
    // Stop erişilemez kalıyordu ve çift Rebuild tıklaması runInProgress'e neden olabiliyordu. IsStarting,
    // komut gönderilir gönderilmez (runStarted/runStopped/run-bitiren ErrorEvent'e kadar) bu boşluğu kapatır.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(RebuildProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCyclesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(OptimizeCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(IsMidRunLocked))]
    [NotifyPropertyChangedFor(nameof(CanSwitchBranch))] // [§6.3] chip kapısının TEK bildirim kaynağı (bkz. CanSwitchBranch)
    private bool _isStarting;

    /// <summary>[tek proje · design §3.8] Uçuştaki KAPSAMLI koşunun hedefi (proje kimliği); <c>null</c> = tam
    /// koşu ya da koşu yok. Tıklama anında yazılır (gönderim penceresi dahil — hedef satır o an Stop'a döner)
    /// ve kilidin düştüğü HER yolda (<c>runCompleted</c>/<c>runStopped</c>, run-bitiren hata, motor ölümü,
    /// iptal, senkron düşen gönderim) tek yerden bırakılır: <see cref="PropagateRunLock"/>. Satırlara
    /// <see cref="ProjectRowViewModel.IsRunTarget"/> olarak itilir.</summary>
    [ObservableProperty] private string? _runTargetId;

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
    [NotifyCanExecuteChangedFor(nameof(BuildProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(RebuildProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCyclesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(OptimizeCommand))]
    [NotifyPropertyChangedFor(nameof(CanSwitchBranch))] // [§6.3] chip kapısının TEK bildirim kaynağı (bkz. CanSwitchBranch)
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
    [NotifyCanExecuteChangedFor(nameof(BuildProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(RebuildProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCyclesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(OptimizeCommand))]
    [NotifyPropertyChangedFor(nameof(CanSwitchBranch))] // [§6.3] chip kapısının TEK bildirim kaynağı (bkz. CanSwitchBranch)
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

    /// <summary>[design v1.11.0 §2.7-4] Aktif statü chip'lerinin KÜMESİ (<see cref="ProjectFilter"/> sabitleri) —
    /// boş küme = filtre yok. Chip'ler bağımsız açılıp kapanır ve seçili küme <b>VEYA</b> ile birleşir.
    /// <para>Değer HER ZAMAN yeni bir küme örneğiyle DEĞİŞTİRİLİR (mutasyon YOK): <c>ObservableProperty</c>
    /// referans eşitliğine bakar, yerinde değiştirilen bir küme <c>PropertyChanged</c> yaymaz ve
    /// <see cref="VisibleProjects"/> bayat kalırdı.</para></summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleProjects))]
    private IReadOnlySet<string> _activeFilters = ProjectFilter.None;

    /// <summary>[design v1.11.0 §2.2 · §9-9] Sticky şeridin KALICI işlem pill'inin metni — son tetiklenen
    /// işlemin kimliği (<see cref="OperationLabel"/>). Koşu bitince SİLİNMEZ: bir sonraki işleme kadar durur;
    /// hiç işlem yapılmadıysa (açılış) <c>null</c> ve pill hiç çizilmez.</summary>
    [ObservableProperty] private string? _currentOperation;

    /// <summary>[C2] Serbest metin proje sorgusu (ada göre alt-dize).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleProjects))]
    private string _projectQuery = "";

    /// <summary>[spec 2026-09-18 §1-7] Çalışma ağacında checkout edilmiş branch — bir tercih değil, okunan bir
    /// GERÇEK. Tek yazıcısı branch envanteridir (<see cref="OnBranchList"/>); detached HEAD'de son değer durur.</summary>
    [ObservableProperty] private string _branch = "";

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

    /// <summary>[design v1.14.0 §9 · externals] Harici proje listesi (yalnız yol) — Store tarafından seed edilir,
    /// Settings Save'de yeniden yazılır (<see cref="RunViewModel.ApplySettingsAsync"/>) ve HER Sync/Build
    /// komutuyla motora GÖNDERİLİR (sıralamayı Ayarlar editörü kurar, kararı Core verir — SIRA çalışma kopyalarının güncellenme
    /// sırasıdır; harici projeler arasındaki build sırası bağımlılık kenarlarından gelir).
    /// <see cref="LayerPatterns"/>'ın aksine <c>null</c> ayrımı GEREKMEZ (motor tarafında "yok" ile "boş"
    /// arasında bir fark YOK) — bu yüzden hep boş listeyle başlar; tel üzerine boş liste <c>null</c> olarak
    /// çıkar (<see cref="ExternalProjectsForWire"/>) ki eski NDJSON şekli bayt-bayt korunsun.</summary>
    public IReadOnlyList<ExternalProject> ExternalProjects { get; set; } = [];

    /// <summary>Komutlara giden hâli: boş liste → <c>null</c> (özellik kapalı, alan hiç yazılmaz).</summary>
    private IReadOnlyList<ExternalProject>? ExternalProjectsForWire => ExternalProjects.Count > 0 ? ExternalProjects : null;

    /// <summary>[design v1.14.0 §9] Build, harici çalışma kopyalarını derlemeden ÖNCE kendi sürüm
    /// kontrolünden güncellesin mi (git <c>fetch</c> + <c>merge --ff-only</c> / <c>tf vc get</c>).
    /// <b>Varsayılan: evet.</b>
    /// <para>Kapalıyken tek bir VCS komutu bile çalışmaz ve kir kapısı da yoktur — harici projeler ana repo
    /// gibi, oldukları hâliyle derlenir. Karar doğruluğu bundan etkilenmez: harici projelerin imzası çalışma
    /// kopyasının İÇERİĞİNDEN hesaplanır.</para>
    /// <para><see cref="ObservablePropertyAttribute"/>: kalıcılık bu bildirimden sürer ve bir aç/kapa
    /// kontrolü doğrudan buna bağlanabilir.</para></summary>
    [ObservableProperty] private bool _updateExternals = true;

    /// <summary>[spec 2026-09-18 §6.3] Settings → General "Stash and switch branches": branch chip'inden checkout'ta
    /// ağaç kirliyse değişiklikler (izlenmeyenler dahil) stash'lenip geçilsin mi. <b>Varsayılan: hayır</b> — checkout
    /// durur ve kullanıcıdan önce commit/stash ister. Değer her <see cref="CheckoutBranchCommand"/> ile motora gider.
    /// <para><see cref="ObservablePropertyAttribute"/>: kalıcılık bu bildirimden sürer (MainWindow).</para></summary>
    [ObservableProperty] private bool _stashOnBranchSwitch;

    /// <summary>[T12] Koşarken (veya planlama penceresinde) branch/configuration kontrolleri kilitli;
    /// perf chip'i CANLI kalır. UI <c>IsEnabled</c> bunu okur.</summary>
    public bool IsMidRunLocked => IsRunning || IsStarting;

    /// <summary>[C2] Sorgu + aktif filtre altında görünen satırlar (BuildApp.jsx:465-470).</summary>
    public IReadOnlyList<ProjectRowViewModel> VisibleProjects =>
        Projects.Where(r => ProjectFilter.Matches(r, ProjectQuery, ActiveFilters)).ToList();

    /// <summary>[C2 fold testi] YALNIZ testler: uçuştaki Sync bayrağının gözlemlenebilir hali (bkz.
    /// <see cref="OnEngineExited"/> fold'u — engine ölümü bu bayrağı bırakmalı).</summary>
    internal bool SyncInFlight => _syncInFlight;

    // [C2] Boot geçişi: repo seçilir seçilmez (RootPath dolunca) Empty → Boot. Sonraki fazları engine event'leri sürer.
    partial void OnRootPathChanged(string value)
    {
        if (Phase == AppPhase.Empty && !string.IsNullOrEmpty(value)) Phase = AppPhase.Boot;
        AttachAutoSync(value); // [spec 2026-09-18 §6.1] HEAD izleyicisi kökü izler
        RefreshGitOperation(); // [spec 2026-09-18 §6.4] eski kökün git işlemi yeni kökte anlamsız
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
    /// workspace hedefiyle (kök/configuration/layer patterns) gönder.
    /// <para>[Fix wave 1(It-3), Finding 1] <paramref name="clearBuffers"/>=true iken önceki run'ın
    /// <c>_liveLines/_projectText/_runText</c> tortusu temizlenir: aksi halde İKİNCİ run'da kart tıklamasında
    /// dikiş filtresi (LineNumber &gt; ThroughLineNumber) eski run'ın kuyruk satırlarını da geçirir ve
    /// OrderBy(LineNumber) eski+yeni'yi karıştırır (bozuk "tam log"). runStarted'ı BEKLEMEDEN burada temizlenir:
    /// ProjectLogEvent marshal'sız işlendiğinden yeni run'ın ilk satırları, marshal'lı runStarted UI thread'ine
    /// düşmeden ÖNCE varabilir. <b>Continue temizlemez</b> (önceki segmentin log/proje sonuçlarını korur).</para>
    /// <para>[Fix wave 2, Finding 1] Gönderim SENKRON başarısız olursa (engine hiç başlamadı/öldü) IsStarting
    /// geri açılır — aksi halde hiçbir engine event'i gelmeyeceğinden buton kalıcı kilitli kalırdı.</para></summary>
    /// <param name="scopeProjectId">[tek proje · design §3.8] Satırdan tetiklenen koşunun hedefi; <c>null</c> =
    /// tam koşu. Dolu iken kapsam yalnız o satırdır (koreografi de yalnız onu işaretler), komut
    /// <see cref="StartRunCommand.ScopeProjectId"/> taşır ve <see cref="RunTargetId"/> tıklama anında yazılır.
    /// Satırdan tetiklemek satıra tıklamak DEĞİLDİR: seçim tam koşudaki gibi düşer — graf odaktan fit
    /// görünüme, konsol ana loga döner; filtre korunur (kullanıcı kararı 2026-09-19); pill hedef adı taşımaz
    /// (v1.13.2).</param>
    private async Task BeginRunAsync(RunMode mode, bool clearBuffers, string? scopeProjectId = null)
    {
        string runId = _newRunId();
        _currentRunId = runId;
        // [design v1.11.0 §9-4 `_beginOp`] Konsol VE event stream temizlenir — ekrandaki her şey artık
        // yürüyen işlemin hikâyesidir. İkisi de <c>clearBuffers</c> dalında, birbirinin eşi iki adlandırılmış
        // metotla (kopya YASAK) — <see cref="ClearConsoleForNewOperation"/> ile SyncCoreAsync AYNI metodu paylaşır.
        if (clearBuffers) ClearStreamForNewOperation();
        // [design v1.11.0 §9-4 `_neutralize`] Kapsam ÖNCE okunur, sonra nötrleme yapılır — prototipteki sıra
        // da budur (build-data.js:541-547: önce `st.will` yazılır, sonra `_neutralize()`).
        var target = scopeProjectId is null ? null : FindRow(scopeProjectId);
        var scope = target is null ? ScopeFor(mode) : [target];
        NeutralizeRows();
        RefreshRunSurface(); // sayaclar/serit notrlenmis listeden yeniden turer
        // [design v1.11.0 §2.2] İşlem pill'i TIKLAMA ANINDA yazılır (motorun cevabı beklenmez): pill "ne
        // yapmıştım?" sorusunu cevaplar ve o soru gönderim gecikmesi boyunca da geçerlidir.
        //
        // Yazım NÖTRLEMEDEN SONRAdir: etiketin değişmesi, kabuğun grafa "yeni bir işlem başladı, statüleri
        // yeniden oku" dediği sinyaldir — nötrleme görsel durumu sayıları değiştirmeden de değiştirebilir, bu
        // yüzden sayaca bakan kapı onu kaçırabilir. Sinyal erken çıkarsa graf önceki koşunun renkleriyle tazelenir.
        CurrentOperation = OperationLabel.ForRunMode(mode);
        ActiveProjectId = null;
        // [tek proje] Hedef, kilitten ÖNCE yazılır: kilit düşerken (PropagateRunLock) bırakılır, dolayısıyla
        // sıra ters olsaydı hedef daha tıklama anında silinirdi. Tam koşuda açıkça null'dır.
        RunTargetId = scopeProjectId;
        IsStarting = true;
        if (clearBuffers) ClearConsoleForNewOperation();
        // [design doBuild — BuildApp.jsx:1199-1200] Seçim sıfırlanır. SIRA ÖNEMLİ: konsol temizliğinden SONRA
        // — seçim düşünce kabuk anlatı belgesini yeniden kurar (ShowRunConsole → SeedRunDocument); temizlik
        // ondan sonra gelseydi o kurulum bir önceki koşunun metnini tilt'le getirir, temizlik onu hemen silerdi
        // (görünür bir kırpışma). SyncCoreAsync aynı sırayı izler.
        // [kullanıcı kararı 2026-09-19] Filtre (chip'ler + arama) artık DÜŞMEZ: liste koşu boyunca filtreli
        // kalır. Eskiden prototip gibi filtre de sıfırlanıyordu; grafın koşu boyunca filtreyi yok sayması
        // kabuğun işidir (GraphView.IsFilterSuspended), VM'in değil.
        SelectedProjectId = null;
        // [planlama görünürlüğü] StopAsync'in simetriği: faz gönderimden ÖNCE yazılır ve konsola tek satırlık
        // bir not düşer. Motor runStarted'a kadar (taze segmentte: tarama → graf → topo →
        // incremental) saniyeler harcayabilir; o pencerede ekranın tek kanıtı budur. Konsol notu buffer
        // temizliğinden SONRA yazılır — aksi halde ilk iş olarak silinirdi.
        var previousPhase = Phase;
        Phase = AppPhase.Starting;
        AppendRunLine(RunRequestedLine(mode, target?.Name));
        // [spec 2026-09-18 §6.4] Yarıda bir merge/rebase/cherry-pick/revert koşuyu ENGELLEMEZ; planlamanın başında tek
        // uyarı satırı düşer. Clean derlemez — çakışma işaretli dosya onu ilgilendirmez.
        if (mode != RunMode.Clean && Core.Git.GitOperationText.BuildWarning(RefreshGitOperation()) is { } gitWarning)
            AppendRunLine(gitWarning);

        // [design v1.11.0 §9-4 `_mark`] AÇILIŞ KOREOGRAFİSİ — koşu ondan SONRA başlar (prototipte de:
        // `_mark(scope, () => startRun())`). Kapsamı VM bilir, zamanlamayı kabuk; bu yüzden kapı bir
        // delegedir ve VM tek bir şey yapar: bitmesini bekler.
        //
        // Neden koşu beklenir: koreografi motorun planlama penceresiyle ÖRTÜŞTÜRÜLMÜŞTÜ ve bedeli ölçüldü —
        // planlama koreografiden kısa sürdüğünde `runStarted` dalgayı ortasında kesiyordu, uzun sürdüğünde
        // kesmiyordu: aynı tıklama bazen animasyonlu bazen anında açılıyordu. Bir koreografi ya her zaman
        // oynar ya hiç. İşlem yine de İLK KAREDE başlar (pill, Stop, konsol satırı) — bekleyen yalnız komut.
        if (OperationChoreography is { } playChoreography)
        {
            _pendingRunId = runId;
            await playChoreography(scope);
            // Koreografi sırasında Stop'a basıldıysa (ya da başka bir işlem devraldıysa) komut GİTMEZ.
            if (!string.Equals(_pendingRunId, runId, StringComparison.Ordinal)) return;
            _pendingRunId = null;
        }
        // [T20-b/K11] PerfMode de gider: paralellik (Parallelism) ve cap/priority (PerfMode) AYNI profil
        // satırının iki yarısıdır — Supervisor cap'i o addan çözer, worker sayısını YENİDEN türetmez.
        // [spec 2026-09-18 §1-1] Koşu daima RootPath'teki çalışma ağacında derlenir: branch/worktree gitmez.
        var cmd = new StartRunCommand(runId, mode, RootPath, Configuration, Parallelism,
            DependentMode.Safe, LayerPatterns, PerfMode,
            ExternalProjectsForWire, UpdateExternals, scopeProjectId);
        if (!await TrySendAsync(cmd, RunModeLabel(mode)))
        {
            IsStarting = false;
            Phase = previousPhase; // hiçbir engine event'i gelmeyecek — faz Starting'te asılı bırakılamaz
        }
    }

    /// <summary>
    /// [design v1.11.0 §9-4] <b>Açılış koreografisinin kapısı.</b> Kabuk (<c>MainWindow</c>) buraya kendi
    /// oynatıcısını takar; VM koreografiyi İSTER ve bitmesini BEKLER — zamanlama, süre ve görsel bilgisi
    /// VM'e hiç sızmaz. Kapı takılı değilse (çıplak VM testleri) komut doğrudan gider.
    ///
    /// <para>Argüman işlemin KAPSAMIdir (<see cref="ScopeFor"/>): dalgada amber'a yanan küme.</para>
    /// </summary>
    public Func<IReadOnlyList<ProjectRowViewModel>, Task>? OperationChoreography { get; set; }

    /// <summary>
    /// [clean · kullanıcı kararı 2026-09-12] "Şu kadar milisaniye bekle" kapısı — <see cref="OperationChoreography"/>
    /// ile AYNI bölüşüm: <b>diziyi VM bilir, zamanı kabuk sayar</b> (kabuk bunu bir <c>DispatcherTimer</c> ile
    /// karşılar; VM timer türü TAŞIMAZ ve üretimde <c>Task.Delay</c> de yasaktır — D8). <c>null</c> ise bekleme
    /// YOKTUR: testler ve azaltılmış hareket kipi bu yoldan hiç geçmez.
    /// </summary>
    public Func<double, Task>? OperationHold { get; set; }

    /// <summary>[clean/optimize] Bakım adımının (Clean ya da Optimize) EN AZ görünür süresi. Tasarımın nötr vuruşu (<c>MarkingChoreography</c>) —
    /// yeni bir sayı uydurulmaz. Gerekçe: küçük bir workspace'te silme milisaniyeler sürer ve spinner görünmeye
    /// fırsat bulamaz; adım her zaman aynı sürede oynamalıdır (bkz. <see cref="BeginRunAsync"/>'in koreografi
    /// kapısındaki "ya her zaman oynar ya hiç" kararı).</summary>
    internal static double MaintenanceMinStepMs => Controls.MarkingChoreography.NeutralMs;

    /// <summary>[clean/optimize] Bakım işi bitip Sync başlamadan önceki hafif boşluk — tasarımın kısa vuruşu. İki işlem iki
    /// adım gibi okunsun diye vardır: ardı ardına başlayan iki animasyon dizisi tek bir bulanıklığa dönüşüyordu.</summary>
    internal static double MaintenanceStepGapMs => Controls.MarkingChoreography.LightMs;

    /// <summary>[clean] Kabuk bir bekleme kapısı verdiyse <paramref name="ms"/> kadar bekler; vermediyse ya da
    /// süre pozitif değilse ANINDA döner. Tek çağıranı Clean'in bitiş dizisidir.</summary>
    private Task HoldAsync(double ms) =>
        ms > 0 && OperationHold is { } hold ? hold(ms) : Task.CompletedTask;

    /// <summary>Koreografisi oynarken henüz GÖNDERİLMEMİŞ koşunun id'si; <c>null</c> = bekleyen koşu yok.
    /// Stop bu pencerede komutu değil <b>isteği</b> iptal eder (bkz. <see cref="CancelPendingRun"/>).</summary>
    private string? _pendingRunId;

    /// <summary>
    /// [design v1.11.0 §9-4] Bir işlemin KAPSAMI — dalgada amber'a yanan küme.
    /// <list type="bullet">
    ///   <item><b>Build</b>: stale set (önizlemenin <c>WillBuild</c>'i true olan satırlar).</item>
    ///   <item><b>Rebuild</b>: döngü dışı TÜM projeler (döngü üyeleri standart koşuya girmez — §3.2).</item>
    ///   <item><b>Resolve cycles</b>: döngü üyeleri.</item>
    /// </list>
    /// Kapsam bir TAHMİN değildir: üçü de motorun aynı koşuda derleyeceği kümedir (motor kapsamı daraltırsa
    /// koreografi zaten koşu başlarken biter ve statü kanalı devralır).
    /// </summary>
    /// <summary>
    /// [design v1.11.0 §9-4 <c>_neutralize</c> · design v1.20.0 §2.3] <b>Önceki koşunun KOŞU alanlarını
    /// siler</b> — ve yalnız onları: statü (<c>Pending</c>), süre, bu koşunun dependency listesi, döngü tur
    /// bayrakları, atlama gerekçesi ve koreografi işareti. Satırın ÇIKTI DURUMU (önizleme kararı
    /// <see cref="ProjectRowViewModel.WillBuild"/>/<see cref="ProjectRowViewModel.WillBuildReason"/>,
    /// <see cref="ProjectRowViewModel.LastBuiltAt"/>, <see cref="ProjectRowViewModel.OwnFilesChanged"/>,
    /// <see cref="ProjectRowViewModel.FailedAt"/>, <see cref="ProjectRowViewModel.LocalEdits"/>, defter notunun
    /// kökleri) ve yapısal bilgi (döngü üyeliği, katman) DOKUNULMAZ: renk kümülatiftir ve kapsam plandan
    /// okunur.
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.20.0 §2.3]</b> Eski hâl bir <c>fresh</c> parametresi taşırdı: Sync
    /// herkesi başlangıç moduna (<c>Fresh=true</c>, renksiz), bir işlem herkesi düz nötr griye indirirdi —
    /// "renk yalnız son işlemin hikâyesini anlatır". Değişme gerekçesi (kullanıcı ölçümü): Sync sonrası neyin
    /// güncel olduğu renkten okunmuyordu. Renk artık çıktının durumudur; iki çağıran (Sync ve bir İŞLEMin
    /// başlangıcı) aynı zemine iner ve ayrıştıkları bir nokta kalmadı.</para>
    /// </summary>
    /// <param name="clearMarks">
    /// [Task 1 review fix — I-1] <c>true</c> (varsayılan) → işaret (<see cref="ProjectRowViewModel.Marked"/>)
    /// de düşer — <c>BeginRunAsync</c>'in tıklama anı (bir ÖNCEKİ işlemin izini siler, YENİ dalga henüz
    /// yanmadı) ve Sync'in nötrlemesi (bir işlem bile değil) için doğru olan budur.
    /// <c>false</c> → işaret KORUNUR: <see cref="OnRunStarted"/>'ın Rebuild'e özel çağrısı için — o an, İSTEK
    /// tıklama anında zaten dalga yanmış ve satır <c>Marked=true</c> olmuş OLABİLİR (koreografi
    /// <c>BeginRunAsync</c>'te, bu çağrıdan ÖNCE oynar); burası tekrar <c>false</c> yazarsa dalganın amberi
    /// runStarted'ın KENDİ anında söner ve I-1'in kapattığı boşluk (runStarted → buildPreview arası bir kare
    /// gri) Rebuild'de YENİDEN açılır. İşaretin gerçek düşüş noktası <c>BuildPreviewApplied</c>'dır
    /// (<c>MainWindow</c>), moddan bağımsız.
    /// </param>
    private void NeutralizeRows(bool clearMarks = true)
    {
        foreach (var row in Projects)
        {
            row.State = ProjectRowState.Pending;
            row.DepIssues = null;
            row.DurationMs = 0;
            // Uyari ucgeninin metnini secen oncelik sirasinda (RowWarning) bu iki hukum DepIssues'in
            // USTUNDEDIR: temizlenmezlerse yeni islemin ilk karesinde ucgen hala gecen kosuyu anlatir.
            row.CycleUnconverged = false;
            row.CycleUnsettled = false;
            row.CycleWaiting = false;
            row.SkipReason = null;
            if (clearMarks) row.Marked = false;
            // [Task 1 review fix — M-2] InRunQueue BİLEREK burada sıfırlanmaz: tek başlangıç noktası
            // OnRunStarted'ın kendi (moddan bağımsız, koşulsuz) döngüsüdür — üç çağıranın ikisinde
            // (BeginRunAsync'in tıklama anı, Sync'in nötrlemesi) bu run henüz runStarted'a ULAŞMAMIŞTIR
            // ve IsRunActive zaten false'tur (Status'un Queued dalı onu okumaz), üçüncüsünde (Rebuild'in
            // runStarted'ı) OnRunStarted zaten AYNI satırları bir satır yukarıda sıfırlamıştır — burada
            // TEKRARLAMAK kopya (CLAUDE.md) olurdu. Tek bitiş noktası PropagateRunActive'dir (IsRunActive
            // düşerken).
        }
    }

    /// <summary>[Task 4 — kök neden C] Build dalgası yalnız KESİN derlenecekleri yakar — koşullu (<see
    /// cref="ProjectRowViewModel.Conditional"/>) bir proje kökü hâlâ hatalıysa atlanabilir, dolayısıyla dalgada
    /// amber'a yanmaz. Bu, motorun kesin kuyruğuyla (<see cref="InRunQueueFor"/>'un Build dalı) AYNI bayraktan
    /// türer — tek doğruluk kaynağı (kopya YASAK). Yalnız tam (kapsamsız) Build'te anlamlıdır: satırdan
    /// tetiklenen hedef bu metoda hiç uğramaz (<see cref="BeginRunAsync"/> tek elemanlı bir liste kurar).</summary>
    public IReadOnlyList<ProjectRowViewModel> ScopeFor(RunMode mode) => mode switch
    {
        RunMode.Rebuild => [.. Projects.Where(r => !r.InCycle)],
        RunMode.Cycles => [.. Projects.Where(r => r.InCycle)],
        _ => [.. Projects.Where(r => r.WillBuild == true && !r.Conditional)],
    };

    /// <summary>[design v1.11.0 §9-5] Bu koşuda GERÇEKTEN derlenen projeler (succeeded ∪ failed) — bitiş
    /// koreografisinin ("neon tutuşma") kapsamı. Atlananlar ve dokunulmayanlar BURADA DEĞİLDİR: onlar
    /// koreografinin son adımında hep birlikte belirginleşir.</summary>
    public IReadOnlyList<string> BuiltInThisRun() =>
        [.. Projects.Where(r => r.State is ProjectRowState.Succeeded or ProjectRowState.Failed).Select(r => r.Id)];

    /// <summary>[planlama görünürlüğü] Run dokümanına düşen tek satırlık not: konsol, tıklamanın KALICI
    /// kaydıdır (şerit bir sonraki faz değişiminde üzerine yazar). Motorun planlama adımları hemen ardından
    /// akar. Mod adı <see cref="RunModeLabel"/>'dan gelir — gönderim hata satırıyla AYNI kaynak.</summary>
    /// <param name="targetName">[tek proje] Kapsamlı koşuda hedefin adı — satır "neyi" sorusunu da cevaplar
    /// (pill cevaplamaz); <c>null</c> = tam koşu.</param>
    internal static string RunRequestedLine(RunMode mode, string? targetName = null) =>
        targetName is null
            ? RunModeLabel(mode) + " requested"
            : RunModeLabel(mode) + " requested — " + targetName + " (single project)";

    private static string RunModeLabel(RunMode mode) => mode switch
    {
        RunMode.Rebuild => "rebuild",
        RunMode.Build => "build",
        RunMode.Cycles => "cycles",
        RunMode.Clean => "clean",
        _ => "run",
    };

    [RelayCommand(CanExecute = nameof(CanRebuildOrRetry))]
    private Task RebuildAsync() => BeginRunAsync(RunMode.Rebuild, clearBuffers: true); // seçim orada düşer (filtre korunur)
    // [D1 review · A3] Motor erişilemezken (hiç doğamadı) run başlatmak anlamsız — bkz. IsEngineUnavailable.
    // [topoloji kapısı] Sync'siz (topolojisiz) run da anlamsızdır: motor derler ama ekran boş kalır — bkz. HasTopology.
    private bool CanStartRun() => HasTopology && !IsRunning && !IsStarting && !IsEngineUnavailable;

    // [Fix wave 1, C2 review Finding 1] Sync uçuştayken (bkz. SyncBusy) hiçbir run başlatılamaz: mid-Sync
    // BeginRunAsync(clearBuffers:true) _runText/_liveLines/_projectText'i temizler, ama SyncProgressEvent
    // hâlâ _runText'e satır ekliyor olabilir (canlı Sync transkriptini bozar).
    //
    // [DEĞİŞEN KURAL] Build eskiden bu guard'ın DIŞINDAYDI — prototip doBuild'in kasıtlı asimetrisi
    // (BuildApp.jsx:1194: doRebuild/doRetry'nin aksine phase==='syncing' erken-dönüşü yoktur). Ölçülen bedel:
    // Supervisor Sync boyunca komut döngüsünü BLOKLAR (SupervisorHost.SyncWorkspaceAsync), yani mid-Sync
    // basılan Build kuyruğa girmekle kalmıyor, başkasının transkriptinin ORTASINA düşüyordu — konsol anında
    // temizlenip "build requested" yazılıyor, ardından Sync'in kalan satırları AYNI dokümana akıyordu.
    // Üç run komutu artık aynı kapıdan geçer; kapı Sync bitince tek yerden (NotifySyncGatedCommands) açılır.
    // [clean] Clean uçuştayken de hiçbir run başlatılamaz: silme, MSBuild'in yazdığı bin/obj ile yarışırdı.
    private bool CanRebuildOrRetry() => CanStartRun() && !WorkspaceBusy;

    // [DEĞİŞEN KURAL] Kapı CanStartRun DEĞİL CanRebuildOrRetry'dır: Build de Sync penceresinde bekler
    // (gerekçe CanRebuildOrRetry'ın yorumundadır).
    [RelayCommand(CanExecute = nameof(CanRebuildOrRetry))]
    private Task BuildAsync() => BeginRunAsync(RunMode.Build, clearBuffers: true); // seçim orada düşer (filtre korunur)

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
    private Task BuildCyclesAsync() => BeginRunAsync(RunMode.Cycles, clearBuffers: true); // seçim orada düşer (filtre korunur)

    /// <summary>[tek proje · design v1.11.0 §3.8] Satırın play düğmesi ve ⋯ menüsünün <i>Build</i> maddesi:
    /// YALNIZ o projeyi derler — bağımlılıklar derlenmez, kapsam dışına dokunulmaz. Hedef tam koşuyla aynı
    /// motor yolundan geçer (güncelse <c>up to date</c> atlanır; koşulsuz derlemek <see cref="RebuildProjectCommand"/>'ın
    /// işidir). Parametre satırın kimliğidir; kapı tam koşununkiyle AYNI (<see cref="CanRebuildOrRetry"/>) +
    /// bir hedef: uçuşta bir koşu varken hiçbir satırdan ikinci bir koşu başlatılamaz.</summary>
    [RelayCommand(CanExecute = nameof(CanRunProject))]
    private Task BuildProjectAsync(string? projectId) => BeginRunAsync(RunMode.Build, clearBuffers: true, projectId);

    /// <summary>[tek proje] ⋯ menüsünün <i>Rebuild</i> maddesi: aynı kapsam, cache yok sayılır (tam Rebuild ile
    /// aynı anlam) — hedef güncel olsa da derlenir.</summary>
    [RelayCommand(CanExecute = nameof(CanRunProject))]
    private Task RebuildProjectAsync(string? projectId) => BeginRunAsync(RunMode.Rebuild, clearBuffers: true, projectId);

    /// <summary>[tek proje · design §3.8] ⋯ menüsünün <i>Clean</i> maddesi — Visual Studio'nun proje
    /// Clean'i: yalnız o projede <c>msbuild /t:Clean</c>. Hiçbir şey derlenmez, başka hiçbir projeye
    /// dokunulmaz; çıktılar gittiği için projenin defter kaydı silinir ve bir sonraki <i>Build</i> onu
    /// baştan derler. Kapısı Build/Rebuild ile AYNIdır.</summary>
    [RelayCommand(CanExecute = nameof(CanRunProject))]
    private Task CleanProjectAsync(string? projectId) => BeginRunAsync(RunMode.Clean, clearBuffers: true, projectId);

    private bool CanRunProject(string? projectId) => projectId is not null && CanRebuildOrRetry();

    /// <summary>[cycles] Düğme YALNIZ elde döngü VARKEN etkindir (<see cref="HasCycles"/>): döngüsüz bir
    /// workspace'te bu koşunun kapsamı BOŞTUR (bkz. <c>CycleRunScope</c>) ve her projeyi atlar — pasif
    /// düğme kullanıcıya bunu tıklamadan ÖNCE söyler.</summary>
    private bool CanBuildCycles() => CanRebuildOrRetry() && HasCycles;

    /// <summary>Action bar'daki <c>Sync</c> düğmesi — kullanıcının DOĞRUDAN tetiklediği, yeni bir konsol bölümü
    /// açan Sync (<see cref="SyncMode.Manual"/>). [spec 2026-09-18 §6.4] Yarıda bir git işlemi varken de koşar; bölümün
    /// ilk satırı ağacın yarım olduğunu söyler (<see cref="MidOperationSyncLines"/>).</summary>
    [RelayCommand(CanExecute = nameof(CanSync))]
    private Task SyncAsync() => SyncCoreAsync(SyncMode.Manual, sectionLines: MidOperationSyncLines());

    /// <summary>
    /// Sync'in ortak gövdesi. Kipi (<see cref="SyncMode"/>) çağıran seçer: Sync düğmesi <see cref="SyncMode.Manual"/>;
    /// açılış (<see cref="OnEngineReady"/>), pull, Clean/Optimize devri ve Settings Save / kök değişimi
    /// <see cref="SyncMode.Appended"/>; checkout <see cref="SyncMode.BranchChange"/>; kendiliğinden Sync
    /// <see cref="SyncMode.Silent"/> (<see cref="SyncSilentlyAsync"/>). Kiplerin tablosu <see cref="SyncMode"/>'un
    /// özetindedir (spec 2026-09-18 §6.2).
    ///
    /// <para><b>Temizlik:</b> Manual ve BranchChange konsolu + event stream'i temizler — [design v1.13.2 §9]
    /// BeginRunAsync(clearBuffers:true) ile AYNI iki metot (kopya YASAK), TIKLAMA ANINDA. Appended temizlemez:
    /// çağıranlar bu Sync'ten HEMEN ÖNCE KENDİ notunu yazar (<c>"Layer definitions updated — N layers"</c>,
    /// <c>"Repository root → … — Sync required"</c>) ya da önceki işlemin transkripti (pull, Clean, açılışın
    /// boot satırı) görünür kalmalıdır
    /// (<see cref="SettingsDialogTests.Applying_settings_sends_one_sync_that_carries_the_new_layer_patterns"/>).
    /// BranchChange'te yeni bölümün ilk satırları <paramref name="sectionLines"/>'tır ("temizlik önce, not
    /// sonra"): çağıran (checkout) onları yazmaz, burada temizlikten SONRA yazılır. Sync düğmesi (Manual) de aynı
    /// yolu kullanır: yarıda bir git işlemi varsa bölümün ilk satırı onu söyler.</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL — spec 2026-09-18 §1-13]</b> Plan yüzeyi (liste + graf) artık Sync'te BOŞALMAZ
    /// (eskiden her Sync tıklamada <see cref="ClearPlanSurface"/> çağırırdı — kullanıcı kararı 2026-09-12). Yapı
    /// aynıysa topoloji satırları yerinde uzlaştırır; yapısal imza değişirse reveal oynar
    /// (<see cref="OnWorkspaceTopology"/>). Boşaltma yalnız Clean/Optimize tıklamasında ve gerçek bir kök
    /// değişiminde kalır (<see cref="SyncAfterRootChangeAsync"/>).</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-19 · task 3]</b> Yukarıdaki "yerinde" kuralı artık yalnız
    /// Silent ve Appended kiplerde geçerlidir. Sync düğmesi (Manual) ve branch değişimi (BranchChange) ekranı baştan
    /// başlatır (<see cref="SyncModeRules.RestartsPlanSurface"/>): konsol ve akışla AYNI ANDA liste ve graf da
    /// ekranda boşalır (<see cref="BeginPlanSurfaceRestart"/> — VM'e dokunmaz; ClearPlanSurface'in faz Boot'u,
    /// boş-durum daveti ve grafın "Sync'ten sonra" etiketi YOK), topoloji gelince yapı aynı olsa da reveal'le ve
    /// graf fit hâlde geri gelir. Gerekçe: kullanıcı bu iki eylemi "baştan başla" olarak okuyor; ekranın yerinde
    /// kalması Sync'in bir şey yapıp yapmadığını belirsiz bırakıyordu. Sync topoloji getirmezse (gönderim
    /// düştü, <c>planFailed</c>, motor kaybı) önceki yüzey geri gelir (<see cref="EndPlanSurfaceRestart"/>).</para>
    /// </summary>
    /// <param name="mode">Konsol ilişkisi, fetch ve pill kararı.</param>
    /// <param name="silentReason">Yalnız <see cref="SyncMode.Silent"/>: bitişteki akış satırını seçer.</param>
    /// <param name="sectionLines">Konsolu temizleyen kiplerde (<see cref="SyncMode.BranchChange"/>, <see cref="SyncMode.Manual"/>):
    /// temizlikten sonra yazılan ilk satırlar.</param>
    /// <returns>Sync komutu motora gitti mi — düşen gönderimde <c>false</c> (kendiliğinden Sync tetiği bekletir).</returns>
    private async Task<bool> SyncCoreAsync(SyncMode mode, SilentSyncReason silentReason = SilentSyncReason.Refresh,
        IReadOnlyList<string>? sectionLines = null)
    {
        // Sıra ÖNEMLİ: temizlik SEÇİMDEN ÖNCE gelir. Seçim düşünce kabuk anlatı belgesini yeniden kurar
        // (ShowRunConsole → SeedRunDocument); temizlik sonra gelseydi o kurulum bir önceki işlemin metnini
        // tilt'le getirir, temizlik onu hemen silerdi (görünür bir kırpışma). Aşağıdaki `_syncRequested`/
        // gönderim ne olursa olsun (senkron başarısız dahil) ekran zaten burada sıfırlanmış olur; bir sonraki
        // syncProgress bir öncekinin tortusunun ÜZERİNE yazılmaz (bkz. ClearConsoleForNewOperation).
        if (mode.ClearsConsole())
        {
            ClearConsoleForNewOperation();
            ClearStreamForNewOperation();
        }
        // [task 3] Liste + graf konsolla AYNI anda ekranda boşalır (Manual, BranchChange) — topoloji geri getirir.
        if (mode.RestartsPlanSurface()) BeginPlanSurfaceRestart();
        foreach (string line in sectionLines ?? []) AppendRunLine(line);
        BeginSyncMode(mode, silentReason);
        if (mode.IsVisible())
        {
            SelectedProjectId = null; // [design doSync] seçim temizlenir, filtre KORUNUR
            CurrentOperation = OperationLabel.Sync; // [design v1.11.0 §2.2] kalıcı işlem pill'i
        }
        // [Sync guard] Kapı GÖNDERİMDEN ÖNCE kapanır — BeginRunAsync'in IsStarting deseninin simetriği.
        // Gönderim milisaniyeler içinde biter ama motor Sync'e ancak sırası gelince başlar; arada düğme
        // etkin kalırsa ikinci basış ikinci bir TAM analiz kuyruklatır (bkz. _syncRequested).
        _syncRequested = true;
        SyncCommand.NotifyCanExecuteChanged();
        // Bekleyiş TAM BURADA başlar: sessizlik saati kurulmazsa, uzun süre boşta duran bir uygulamada
        // basılan ilk Sync anında "cevap vermiyor" derdi (saat son motor event'inden beri bayattır).
        // OnIsStartingChanged'in ve OnPhaseChanged'in aynı satırı.
        ArmEngineWatchdog();
        bool sent = await TrySendAsync(
            new SyncWorkspaceCommand(RootPath, Branch, LayerPatterns, Configuration, ExternalProjectsForWire,
                Fetch: mode.Fetches()), "sync");
        // Gönderim SENKRON düştüyse (engine hazır değil/ölü) hiçbir syncStarted GELMEYECEK — kapı burada
        // açılmazsa Sync düğmesi kalıcı pasif kalırdı. Envanter komutları yine de GÖNDERİLİR: onlar Sync'in
        // event akışından bağımsızdır ve tek huni buradan geçer (bkz. aşağıdaki gerekçeler).
        if (sent) LastSyncStartedAtMs = _nowMs(); // [review M1] yalnız motora giden istek bir Sync başlatır
        else ReleaseSyncRequest();
        // [A13/T2 · 2.2] Branch envanteri BURADAN istenir — TEK huni. Gerekçe: (a) branch chip'inin tek gerçek
        // kaynağı <see cref="Branches"/>'tir ve o yalnız BranchListEvent ile dolar; (b) repo değişince liste
        // BAYATLAR, ve repo'yu değiştiren HER yol (ilk klasör seçimi / Choose Folder → ChangeRepositoryAsync,
        // Settings→Save → ApplySettingsAsync) zaten buraya iner; (c) Sync salt-okurdur, tekrarı zararsızdır.
        // Ayrı bir komut olarak GİDER (Sync'in kendi event akışına karışmaz): Supervisor sıradaki komut olarak
        // işler ve hatası AYRI bir kodla döner ("branchListFailed", SupervisorHost.cs:138) — RunEndingErrorCodes'ta
        // ve SyncErrorCodes'ta OLMADIĞI için bir Sync hatası gibi yanlış atfedilemez.
        await TrySendAsync(new ListBranchesCommand(RootPath), "listBranches");
        return sent;
    }
    // [D1 review · A3] Motor erişilemezken gönderim anlamsız.
    // [Sync guard] Uçuşta bir Sync varken (istek penceresi dahil — bkz. SyncBusy) ikinci bir Sync
    // ANLAMSIZDIR: motor aynı analizi baştan koşar, konsolda aynı transkript iki kez akar ve şerit
    // Syncing → Idle → Syncing yapar. Rebuild/Cycles zaten AYNI predicate'e tabidir.
    // [clean] Clean uçuştayken Sync de beklemelidir: Sync'in tam analizi tam o sırada silinen bin/obj'i okur.
    // [final review O1] Soru tek yerde: WorkspaceGateOpen.
    private bool CanSync() => WorkspaceGateOpen;

    /// <summary>
    /// [clean] Bakım kutusundaki <b>Clean</b>: aktif workspace'in keşfedilen projelerinin <c>bin</c>/<c>obj</c>
    /// klasörlerini ve o workspace'e ait build-state kayıtlarını siler. <b>Onay dialogu YOKTUR</b> — iş
    /// tıklar tıklamaz başlar; geri alınamayan tek şey zaten yeniden üretilebilen derleme çıktısıdır.
    /// <para><see cref="SyncCoreAsync"/>'in (<see cref="SyncMode.Manual"/>) simetriğidir ve AYNI sırayı izler: konsol +
    /// event stream TIKLAMA ANINDA temizlenir ([design v1.13.2 §9] "her işlemde temizlenir" — BeginRunAsync ve
    /// Sync ile AYNI iki metot, kopya YASAK), temizlik SEÇİMDEN ÖNCE gelir (kırpışma gerekçesi orada), seçim
    /// temizlenir, filtre KORUNUR, işlem pill'i yazılır, kapı GÖNDERİMDEN ÖNCE kapanır (istek penceresi), tek
    /// satırlık not düşer ve sessizlik saati kurulur. Gönderim SENKRON düşerse kapı geri açılır.</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        ClearConsoleForNewOperation();
        ClearStreamForNewOperation();
        // [kullanıcı kararı 2026-09-12] Liste ve graf da AYNI karede boşalır: çıktılar siliniyor, ekranda duran
        // kararlar/statüler/düğümler o an geçersizdir. Farklı bir anda düşerlerse tek işlem iki sarsıntı gibi
        // görünür. Geri getiren şey bitişteki Sync'tir (OnCleanCompletedAsync). AYNI kural Sync'te de geçerli
        // (SyncCoreAsync) — iki işlem tek yüzey davranışını paylaşır.
        ClearPlanSurface();
        _cleanStartedAtMs = _nowMs(); // adımın görünür süresi BURADAN sayılır (bkz. MaintenanceMinStepMs)
        SelectedProjectId = null; // seçim temizlenir, filtre KORUNUR (Sync ile aynı davranış)
        // [design v1.11.0 §2.2] Kalıcı işlem pill'i. Sözcük DEEP CLEAN: menüdeki Clean (yalnız /t:Clean, CLEAN)
        // ile karıştırılmasın — bkz. OperationLabel.DeepClean.
        CurrentOperation = OperationLabel.DeepClean;
        // Not, temizlikten SONRA yazılır — aksi halde ilk iş olarak silinirdi.
        AppendRunLine(CleanRequestedLine);
        _cleanRequested = true;
        CleanCommand.NotifyCanExecuteChanged();
        NotifySyncGatedCommands(); // run/Sync kapıları da AYNI anda kapanır (yarış penceresi bırakma)
        ArmEngineWatchdog();
        // [harici projeler] Kartlar da gider: harici proje sıradan bir projedir, çıktısı da bu workspace'in
        // çıktısıdır. Liste Sync/Build ile AYNI huniden geçer — ikinci bir kaynak açılmaz (kopya YASAK).
        bool sent = await TrySendAsync(new CleanWorkspaceCommand(RootPath, ExternalProjectsForWire), "clean");
        if (!sent) ReleaseCleanRequest();
    }

    /// <summary>[clean] Run dokümanına düşen tek satırlık not — <see cref="RunRequestedLine"/> deseni: konsol,
    /// tıklamanın KALICI kaydıdır ve motorun ilk satırı gelene kadar ekrandaki tek kanıttır.</summary>
    internal static string CleanRequestedLine => "clean requested";

    /// <summary>[clean] Clean yalnız bir repo seçiliyken anlamlıdır (<see cref="HasWorkspace"/>) — topoloji
    /// GEREKMEZ: servis kendi taramasını yapar, hiç Sync yapılmamış bir workspace'te de çalışır. Uçuştaki bir
    /// run/Sync/Clean ise onu kapatır (karşılıklı dışlama).</summary>
    private bool CanClean() => HasWorkspace && WorkspaceGateOpen;

    /// <summary>
    /// [optimize] Workspace doktoru: eksik NuGet paketlerini restore eder, restore'un çözemediği kırık
    /// referansları isim isim raporlar, build-kırıcı stale <c>obj</c> artıklarını siler, ölü defter girdilerini
    /// budar. Onay dialogu YOKTUR — <see cref="CleanAsync"/> ile aynı karar: iş geri alınamaz bir şey silmez.
    ///
    /// <para><b>Akış Clean ile AYNIDIR:</b> tıklamada liste ve graf boşalır, adım en az
    /// <see cref="MaintenanceMinStepMs"/> görünür, bitişte konsol korunarak Sync zincirlenir.</para>
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-14]</b> Eskiden Clean'den iki farkı vardı: liste
    /// boşaltılmazdı ve Sync zincirlenmezdi (gerekçe: Optimize hiçbir projeyi dirty yapmaz). Kullanıcı iki bakım
    /// işinin aynı akışı izlemesini istedi.</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOptimize))]
    private async Task OptimizeAsync()
    {
        ClearConsoleForNewOperation();
        ClearStreamForNewOperation();
        // [kullanıcı kararı 2026-09-14] Liste ve graf da AYNI karede boşalır — Clean ve Sync ile tek yüzey
        // davranışı. Geri getiren şey bitişte zincirlenen Sync'tir (OnOptimizeCompletedAsync).
        ClearPlanSurface();
        _optimizeStartedAtMs = _nowMs(); // adımın görünür süresi BURADAN sayılır (bkz. MaintenanceMinStepMs)
        SelectedProjectId = null; // seçim temizlenir, filtre KORUNUR (Sync/Clean ile aynı davranış)
        CurrentOperation = OperationLabel.Optimize; // [design v1.11.0 §2.2] kalıcı işlem pill'i
        // Not, temizlikten SONRA yazılır — aksi halde ilk iş olarak silinirdi.
        AppendRunLine(OptimizeRequestedLine);
        _optimizeRequested = true;
        NotifySyncGatedCommands(); // OptimizeCommand de o listededir; run/Sync/Clean kapıları AYNI anda kapanır
        ArmEngineWatchdog();
        // [harici projeler] Kartlar da gider: harici proje sıradan bir projedir, onarımı da bu workspace'in
        // işidir. Liste Sync/Clean/Build ile AYNI huniden geçer — ikinci bir kaynak açılmaz (kopya YASAK).
        bool sent = await TrySendAsync(new OptimizeWorkspaceCommand(RootPath, ExternalProjectsForWire), "optimize");
        if (!sent) ReleaseOptimizeRequest();
    }

    /// <summary>[optimize] Run dokümanına düşen tek satırlık not — <see cref="CleanRequestedLine"/> deseni.</summary>
    internal static string OptimizeRequestedLine => "optimize requested";

    /// <summary>[optimize] <see cref="CanClean"/>'in birebir simetriği: repo şart, topoloji DEĞİL (servis kendi
    /// taramasını yapar). Uçuştaki bir run/Sync/Clean/Optimize kapıyı kapatır.</summary>
    private bool CanOptimize() => HasWorkspace && WorkspaceGateOpen;

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
    /// <see cref="IsMidRunLocked"/> sürer (branch/configuration kilidi kalkmaz, split-button geri
    /// gelmez). Fazdan çıkış motorun sonucuna aittir — bkz. <see cref="OnRunCompleted"/>/
    /// <see cref="OnRunStopped"/>/<see cref="OnError"/>/<see cref="OnEngineExited"/>.</para></summary>
    /// <summary>[design v1.11.0 §3.1 "Stop"] Marking fazında Stop: komut henüz gönderilmediği için
    /// durdurulacak bir şey de yoktur — uygulama kendi isteğini geri alır. Motora ne <c>startRun</c> ne
    /// <c>stopRun</c> gider; koreografiyi ve işaretleri kabuk <see cref="IsStarting"/> düşüşünde temizler.</summary>
    internal static string RunCancelledLine => "Cancelled — build not started";

    private void CancelPendingRun()
    {
        _pendingRunId = null;
        IsStarting = false;
        Phase = AppPhase.Idle;
        AppendRunLine(RunCancelledLine);
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        // [design v1.11.0 §3.1] Marking fazı: komut henüz gönderilmedi — durdurulacak bir koşu yok, geri
        // alınacak bir İSTEK var. Motora hiçbir şey gitmez.
        if (_pendingRunId is not null) { CancelPendingRun(); return; }
        if (_currentRunId is null) return;
        AppendRunLine(StopRequestedLine(Counters.Building));
        await SendStopAsync(_currentRunId, StopKind.Graceful);
    }

    /// <summary>Stop'un gönderimi — kullanıcının Stop'u ve branch kesmesi (<see cref="RequestInterruptAsync"/>) AYNI
    /// kapıdan geçer: faz gönderimden ÖNCE <see cref="AppPhase.Stopping"/>'e yazılır, gönderim senkron düşerse geri
    /// alınır (gerekçe <see cref="StopAsync"/>'in özetinde).</summary>
    private async Task SendStopAsync(string runId, StopKind kind)
    {
        var previous = Phase;
        Phase = AppPhase.Stopping;
        if (!await TrySendAsync(new StopRunCommand(runId, kind), "stop"))
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
    /// değişmezi de ("EngineDiedMessage'ı yazan HER yol bunu da yazar") bozulurdu.</para>
    /// <para>[final review I1] Başarılı yeniden başlatma ilk açılışla AYNI hazır yolundan geçer
    /// (<see cref="OnEngineReady"/>): "Engine ready" satırı, PID/sürüm ve — bir workspace açıkken — tek bir Appended
    /// Sync. Sıra ZORUNLUdur: önce eski motorun pencereleri bırakılır (<see cref="ReleaseAfterEngineLoss"/>), SONRA
    /// hazır yolu — ters sırada bırakma yeni Sync'in istek bayrağını da silerdi.</para></summary>
    [RelayCommand]
    private async Task RestartEngineAsync()
    {
        // Eski process (ve tüm MSBuild child'ları) her koşulda gitti — o motorun asla göndermeyeceği event'leri
        // bekleyen hiçbir durum kalmamalı (ReleaseAfterEngineLoss). Yeni motor başlatılamadıysa da geçerlidir: orada
        // da bekleyecek bir şey yoktur (bkz. OnEngineUnavailable, komutlar zaten kapanır).
        EngineReadyEvent ready;
        try
        {
            ready = await _engine.RestartAsync();
            EngineDiedMessage = null;
        }
        catch (Services.EngineUnavailableException ex)
        {
            OnEngineUnavailable(ex.ExePath, ex.Reason); // [final review I-2] D1'in "engine yok" durumu
            ReleaseAfterEngineLoss();
            return;
        }
        catch (Exception ex)
        {
            AppendRunLine($"[error] engine restart failed: {ex.Message}");
            ReleaseAfterEngineLoss();
            return;
        }
        ReleaseAfterEngineLoss();
        OnEngineReady(ready.EngineVersion, ready.Pid);
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

    /// <summary>Bir run GERÇEKTEN koşuyor mu — <see cref="ProjectRowViewModel.Status"/>'un <c>queued</c>
    /// türetimi için her satıra iter (IsSelected akışının eşi). Yeni doğan satırlar
    /// (<see cref="EnsureRow"/>/topoloji) da mevcut değeri alır.
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Eskiden <c>IsRunning || IsStarting</c>'di (Fix wave 1 · D1 review
    /// Finding 1): planlama penceresinde ekran sessiz kalmasın diye kapsam daha TIKLAMA ANINDA kuyruk
    /// amber'ına düşerdi. design v1.11.0'da o pencereyi açılış koreografisi doldurur ve kapsamı TAM OLARAK
    /// aynı kümedir — bilgi kaybolmaz, yalnız anında değil dalga hâlinde belirir. Eski kural sürseydi
    /// koreografinin ilk iki adımı (nötr an + dalga) hiç görünmezdi: kapsam zaten amber olurdu.</para></summary>
    private bool RunActive => IsRunning;
    partial void OnIsRunningChanged(bool value)
    {
        PropagateRunActive();
        PropagateRunLock();
    }
    partial void OnIsStartingChanged(bool value)
    {
        if (value) ArmEngineWatchdog(); // run istendi — motor bundan sonra konuşmalı
        PropagateRunLock();
    }
    private void PropagateRunActive()
    {
        bool active = RunActive;
        foreach (var row in Projects)
        {
            row.IsRunActive = active;
            // [Task 1] Koşu biterken (IsRunActive düşerken) kuyruk da düşer — bir sonraki koşuya stale bayrak
            // taşınmaz (bkz. ProjectRowViewModel.InRunQueue'nun XML yorumu). Koşu sürerken dokunulmaz: bu run'ın
            // KENDİ buildPreview'i tek üreticidir.
            if (!active) row.InRunQueue = false;
        }
    }

    /// <summary>[tek proje] Kilit (<see cref="IsMidRunLocked"/>) her satıra itilir ve kilit düşerken hedef
    /// TEK yerden bırakılır — <c>runCompleted</c>/<c>runStopped</c>, run-bitiren hata, motor ölümü, iptal ve
    /// senkron düşen gönderim <see cref="IsRunning"/>/<see cref="IsStarting"/>'i zaten düşürür; hedefi ayrıca
    /// hatırlamak gerekmez ve unutulamaz.</summary>
    private void PropagateRunLock()
    {
        bool locked = IsMidRunLocked;
        foreach (var row in Projects) row.IsRunLocked = locked;
        if (!locked) RunTargetId = null;
        NotifyAutoSyncGate(); // [spec 2026-09-18 §6.1] koşu bitti → bekleyen kendiliğinden Sync tetiği
    }

    partial void OnRunTargetIdChanged(string? value)
    {
        foreach (var row in Projects)
            row.IsRunTarget = value is not null && string.Equals(row.Id, value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>[T53/T54-UI] Proje listesini katman gruplarına böler — gruplama YALNIZ topolojiden
    /// (<see cref="ProjectNode.LayerName"/>/<see cref="ProjectNode.LayerIndex"/>) gelir; App'te regex YOKTUR
    /// (mimari kural, test pinler). <see cref="Projects"/> zaten build-order'dadır (topoloji sırası). Hiçbir
    /// düğümün <c>LayerName</c>'i yoksa tek isimsiz grup = düz build-order.</summary>
    public IReadOnlyList<LayerGrouping.Group> BuildLayerGroups() =>
        LayerGrouping.Build(VisibleProjects, Topology);

    /// <summary>[T43] Debug/Release değiştir (BuildApp.jsx:1355-1363). Koşarken KİLİTLİ (no-op) ve aynı değere
    /// no-op. Workspace varsa ve faz Boot/Empty değilse: her proje dirty işaretlenir ve uyarı satırı yazılır.
    /// <para>[T4 review ledger (a) · design v1.20.0 §2.3] Satırın ÇIKTI DURUMU da düşer, yalnız planı değil:
    /// configuration imzaya girer (<c>BuildSignature</c>), yani motorun bir sonraki önizlemesi kaydı olan her
    /// projeye <see cref="WillBuildReason.SignatureChanged"/> diyecektir — satır aynı cevabı şimdiden verir
    /// (gri). Hiç başarısı olmayan <see cref="WillBuildReason.NeverBuilt"/> olur/kalır, kararı olmayan satır
    /// kararsız kalır (bilinmiyor). Defter notu (bekleyen bağımlılık) imza değişince karar terimi olmaktan
    /// çıkar: satır artık kesin derlenir (<c>Conditional=false</c>) ve üçgen düşer. Eskiden yalnız
    /// <c>WillBuild=true</c> yazılıyordu — güncel satır yeşil kalırken konsol "all projects will rebuild" diyordu.</para>
    /// <para>[R-Config] Koşu alanları da silinir (<see cref="NeutralizeRows"/> — aynı metot): az önce başarıyla
    /// biten satır koşunun yeşilinde kalmaz, herkes gibi yeni bayat durumuna iner. Bitmiş ya da durdurulmuş
    /// koşunun özeti de artık bir şey anlatmaz (sayaçları silindi; durdurulan koşunun planı ESKİ configuration'a
    /// aittir, yeni configuration altında sürdürülemez), bu yüzden <c>Done</c> ve <c>Stopped</c> fazları
    /// <c>Idle</c>'a döner ve şerit yeni planı ("N to build") okur; önizleme kümeleri satırların yeni kararından
    /// yeniden kurulur.</para></summary>
    public void SetConfiguration(string value)
    {
        if (IsMidRunLocked || value == Configuration) return;
        Configuration = value;
        if (RootPath.Length == 0 || Phase is AppPhase.Boot or AppPhase.Empty) return;
        NeutralizeRows();
        ClearPreviewSets();
        foreach (var row in Projects)
        {
            row.WillBuild = true; // her şey dirty
            if (row.WillBuildReason is { } reason && reason != WillBuildReason.NeverBuilt)
            {
                row.WillBuildReason = NextPreview.AfterConfigurationChange(reason, row.CurrentSha);
                row.Conditional = false;
                row.DependencyRoots = null;
            }
            NotePreviewDecision(row.Id, row.WillBuild, row.Conditional);
        }
        if (Phase is AppPhase.Done or AppPhase.Stopped) Phase = AppPhase.Idle; // koşunun hikâyesi kapandı
        RefreshRunSurface();        // sayaçlar/şerit nötrlenmiş listeden ve yeni plandan yeniden türer
        RaiseRowDecisionsChanged(); // graf da aynı anda griye iner
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

    /// <summary>[D2/T38] Şeridin SABİT willBuild yüzeyini (wb/fin/allClean) önizleme kümelerinden türetir —
    /// canlı satır bayraklarından DEĞİL (succeeded olunca WillBuild false'a döner; küme donduğu için wb sabit kalır).
    /// <para>[final review — C1] <c>wb</c>/<c>fin</c> KESİN kümeden (<see cref="_willBuildIds"/>), <c>allClean</c>
    /// ise önizlemenin gördüğü TÜM kirlilikten (<see cref="_dirtyIds"/>, koşullu dahil) gelir — bkz. o alanın
    /// yorumu.</para></summary>
    private void RecomputeWillBuildSurface()
    {
        WillBuildCount = _willBuildIds.Count;
        AllClean = _dirtyIds.Count == 0;
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
        try { await (DebugSendOverride?.Invoke(cmd) ?? _engine.SendAsync(cmd)); return true; }
        catch (Exception ex) { AppendRunLine($"[error] failed to send {what}: {ex.Message}"); return false; }
    }

    /// <summary>[C2 testleri] YALNIZ testler ayarlar (bkz. <see cref="DebugAfterStitchLockExited"/> deseni):
    /// bir komut gönderilmeden hemen ÖNCE senkron tetiklenir; gönderilen <see cref="StartRunCommand"/>'ın
    /// workspace argümanlarını (Mode/RootPath/Configuration/LayerPatterns) gerçek Supervisor'a
    /// ihtiyaç duymadan gözlemlemeye yarar. Üretimde hep null — sıfır maliyet.</summary>
    internal Action<IpcCommand>? DebugOnCommandSent;

    /// <summary>[task 3 testleri] YALNIZ testler ayarlar: gönderimi motorun yerine bu yapar. Başlatılmamış bir
    /// motorla gönderim hep düşer; gerçek bir Supervisor ise kendi cevabını pencereye ASENKRON akıtıp testle
    /// yarışır. Bu seam "gönderim başarılı, cevabı test verir" yolunu motorsuz kurar. Üretimde hep null.</summary>
    internal Func<IpcCommand, Task>? DebugSendOverride;

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
    /// stop istendi ama <c>runStopped</c> gelmedi (faz <see cref="AppPhase.Stopping"/>), ya da Sync istendi/
    /// koşuyor ama <c>syncCompleted</c> gelmedi (<see cref="SyncBusy"/> — istek penceresi ve
    /// <see cref="AppPhase.Syncing"/> fazının ikisi de). Watchdog YALNIZ bu bekleyiş pencerelerinde
    /// kuruludur — koşan bir run'da tek bir projenin sessizce dakikalarca derlenmesi meşrudur ve orada
    /// uyarmak kullanıcıyı sağlıklı bir build'i öldürmeye davet ederdi.
    ///
    /// <para><b>Sync neden dahil:</b> o da tam olarak bir bekleyiştir ve motor orada donduğunda şerit
    /// sonsuza dek "▸ Sync — git fetch origin…" gösteriyor, "Restart engine" kapısı HİÇ görünmüyordu — tek
    /// çıkış uygulamayı kapatmaktı. Üstelik Sync düğmesi artık uçuşta bir Sync varken kilitli olduğundan
    /// (<see cref="CanSync"/>) donmuş bir Sync'ten çıkışın TEK yolu bu kapıdır. Yanlış alarm riski yok:
    /// Sync'in git çağrıları 30 sn'de zaman aşımına uğrar (<c>GitService.CommandTimeout</c>), yani
    /// <see cref="EngineSilenceThresholdMs"/>'e takılan motor "yavaş" değil GERÇEKTEN susmuştur — ve motorun
    /// HERHANGİ bir event'i (<c>syncProgress</c> dahil) saati sıfırlar.</para></summary>
    /// <para>[clean/optimize] İki bakım işi de aynı gerekçeyle dahildir: uçuşta biri varken düğmeleri açan
    /// başka kapı yoktur (bkz. <see cref="CanClean"/>, <see cref="CanOptimize"/>), yani donmuş bir motordan
    /// çıkışın TEK yolu "Restart engine"dir. Optimize'da bu daha da keskindir: onun bir iptal komutu YOKTUR,
    /// uzun bir restore dizisinden tek kaçış budur.</para>
    private bool WaitingOnEngine =>
        IsStarting || Phase is AppPhase.Stopping or AppPhase.Syncing || WorkspaceBusy;

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
        if (IsStaleRunEnd(ev)) return; // [T8 fix round 1 · I1] faz ve akış bu koşuya ait değil
        switch (ev)
        {
            case RunStartedEvent e: OnRunStarted(e); break;
            case BuildPreviewEvent e: OnBuildPreview(e); break;
            case ProjectStartedEvent e: OnProjectStarted(e); break;
            case ProjectLogEvent e: OnProjectLog(e); break;
            case ProjectLogChunkEvent e: OnProjectLogChunk(e); break;
            case ProjectSucceededEvent e: OnProjectDone(e.ProjectId, ProjectRowState.Succeeded, e.DurationMs, e.DepIssues, e.CycleUnsettled, trusted: e.Trusted); break;
            case ProjectFailedEvent e: OnProjectDone(e.ProjectId, ProjectRowState.Failed, e.DurationMs, e.DepIssues, evidence: e.Evidence); break;
            case ProjectSkippedEvent e: OnProjectSkipped(e); break;
            case CycleCompletedEvent e: OnCycleCompleted(e); break;
            case RunCompletedEvent e: OnRunCompleted(e); break;
            case RunStoppedEvent: OnRunStopped(); break;
            case ErrorEvent e: OnError(e); break;
            // [A5/T69] Sync yüzeyi — handler'lar RunViewModel.Workspace.cs'te
            case SyncStartedEvent: OnSyncStarted(); break;
            case SyncProgressEvent e: OnSyncProgress(e); break; // [spec §6.2] sessiz kip transkripti gizler
            // [planlama görünürlüğü] Motorun planlama adımları. AppendRunLine DIŞINDA hiçbir şeye dokunmaz:
            // faz zaten Starting'tir (BeginRunAsync yazdı) ve bu satırlar Sync yüzeyine (_syncInFlight) AİT
            // DEĞİLDİR — oraya bağlanırsa Rebuild/Cycles planlama boyunca sessizce kilitlenirdi.
            case PlanProgressEvent e: AppendRunLine(e.Line); break;
            case SyncCompletedEvent e: OnSyncCompleted(e); break;
            // [v1.16.0] Pull sonucu: başarıysa chip düşer + otomatik Sync (konsol KORUNUR). Sync'in kendisi
            // async'tir ve bu dal onu BEKLEMEZ — event pompası bloklanmaz (gönderim zaten milisaniyeler).
            case PullCompletedEvent e: _ = OnPullCompletedAsync(e); break;
            // [spec 2026-09-18 §6.3] Checkout sonucu: başarıda yeni bölüm + Sync zinciri (beklenmez — pompa bloklanmaz).
            case CheckoutCompletedEvent e: _ = OnCheckoutCompletedAsync(e); break;
            // [clean] Clean yüzeyi — handler'lar RunViewModel.Workspace.cs'te (Sync guard'ın yanında).
            // Satırlar Sync yüzeyine AİT DEĞİLDİR: ayrı bayrak, ayrı kanal.
            case CleanStartedEvent: OnCleanStarted(); break;
            case CleanProgressEvent e: AppendRunLine(e.Line); break;
            // Bitişte konsol korunarak bir Sync zincirlenir; dal onu BEKLEMEZ — event pompası bloklanmaz
            // (pullCompleted dalının aynı gerekçesi; gönderim zaten milisaniyeler).
            case CleanCompletedEvent: _ = OnCleanCompletedAsync(); break;
            case OptimizeStartedEvent: OnOptimizeStarted(); break;
            case OptimizeProgressEvent e: AppendRunLine(e.Line); break;
            case OptimizeCompletedEvent: _ = OnOptimizeCompletedAsync(); break;
            case WorkspaceTopologyEvent e: OnWorkspaceTopology(e); break;
            case BranchListEvent e: OnBranchList(e); break;
            case EngineReadyEvent e: OnEngineRecovered(e.InterruptedProjects); break;
        }

        // [D3] Event stream (tampon anlatı + aktif satır) — proje satırları/sayaçlar YUKARIDA güncellendikten
        // SONRA türetilir (ad çözümü + done-glyph'in Counters.Failed'i doğru okunsun). Marshal-free ProjectLogEvent
        // hot-path'ine (Ek A13.2) DOKUNMAZ: yalnız zaten UI-thread'inde olan OnEvent dalından çağrılır.
        AppendStreamFor(ev);
    }

    private void OnRunStarted(RunStartedEvent e)
    {
        _currentRunId = e.RunId;
        _awaitingRunCompleted = true;
        BeginInterruptRecord(e);
        // [Task 2 review fix M-2] Mod'un TEK yazım noktası — InRunQueueFor/OnProjectSkipped bunu okur, hangi
        // sırada hangi partial'ın çalıştığına bağlı KALMADAN (bkz. alanın kendi XML yorumu).
        _currentRunMode = e.Mode;
        // [design v1.11.0 §2.2] İşlem pill'i motorun CEVABINDAN da yazılır, yalnız tıklamadan değil: koşuyu
        // hangi yol başlatmış olursa olsun (komut, ileride bir kısayol ya da dışarıdan gelen bir run) pill
        // gerçekte KOŞAN işi söyler. Komut tarafındaki yazım (BeginRunAsync) yalnız gönderim penceresini
        // kapatır; ikisi aynı değeri üretir (OperationLabel.ForRunMode — tek eşleme yeri).
        CurrentOperation = OperationLabel.ForRunMode(e.Mode);
        // [Task 1 — kök neden A · review fix M-2] Kuyruğun TEK başlangıç noktası BURASIDIR — KOŞULSUZ (moddan
        // bağımsız) sıfırlanır. NeutralizeRows'un aşağıdaki (Rebuild) çağrısı InRunQueue'ya DOKUNMAZ (kopya
        // olurdu, bkz. NeutralizeRows'un yorumu): Build/Cycles'ta runStarted NeutralizeRows'suz da gelebilir
        // (bkz. bu event'in XML yorumu) ve önizleme HENÜZ gelmedi — "runStarted anında hiçbir satır kuyruk
        // değildir" değişmezi moddan bağımsız burada garanti edilir. Hemen ardından gelen BuildPreviewEvent
        // gerçek kuyruğu doldurur. (Başlangıç modu burada DÜŞÜRÜLMEZ — design v1.20.0 §2.3: o yalnız kararın
        // yokluğudur ve önizlemenin kararıyla kalkar.)
        foreach (var row in Projects) row.InRunQueue = false;
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
        // [design v1.11.0 §9-4] Rebuild yeni bir tabana döner — ama listeyi BOŞALTARAK değil, YERİNDE
        // nötrleyerek. [DEĞİŞEN KURAL] Burada eskiden <c>Projects.Clear()</c> vardı; o, açılış
        // koreografisinin işaretlediği satır nesnelerini ortasında yok ediyor ve listeyi remount ediyordu
        // (design v1.10.0 §3.8: "liste yerinden oynamaz"). Komut yolundan gelen bir Rebuild burayı zaten
        // nötrlenmiş bulur — çağrı, koşuyu başka bir yol başlattığında da tabanın temiz olmasını garanti eder.
        // Build/Cycles'ta liste (önceki segmentin sonuçları) olduğu gibi korunur.
        // [Task 1 review fix — I-1] clearMarks: false — bu an itibariyle (IsRunning=true'nun property-changed
        // kaskadı YUKARIDA çoktan bitti) dalganın işaretlediği kapsam MainWindow tarafından BİLEREK KORUNMUŞTUR
        // (bkz. NeutralizeRows'un clearMarks parametresinin yorumu); burada tekrar silersek I-1'in kapattığı
        // runStarted→buildPreview boşluğu Rebuild'de yeniden açılır.
        if (e.Mode == RunMode.Rebuild) NeutralizeRows(clearMarks: false);
        ClearPreviewSets(); // [D2] önizleme kümeleri bu run için taze — hemen ardından BuildPreviewEvent doldurur
        _outOfScopeSkipCount = 0; // [Task 2 review fix M-1] AYNI noktada taze — bu run'ın kendi kümesi
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
    /// (Succeeded/Failed/Skipped) ise ve koşu HÂLÂ sürüyorsa (<see cref="RunActive"/>) WillBuild GÜNCELLENMEZ
    /// — aksi halde segment 1'de gerçekleşen succeeded→clean canlı geçişi (bkz. <see cref="OnProjectDone"/>),
    /// segment 2'nin (bilerek bayat) preview değeriyle sessizce EZİLİRDİ.
    /// <para>[Task 1 — sessiz Sync tazeleme] Koşu BİTTİKTEN sonra gelen her önizleme (pencereye dönüşün
    /// tetiklediği sessiz Sync dahil) terminal satırların kararını da yazar: bu VM instance'ının hayatı
    /// boyunca RunActive tekrar true olmadan gelen ikinci bir preview artık "segment 2" değil, kararı bugüne
    /// taşıyan bağımsız bir tazelemedir — korumanın kapsamı yalnız koşu içidir.</para></summary>
    private void OnBuildPreview(BuildPreviewEvent e)
    {
        foreach (var item in e.Items)
        {
            var row = EnsureRow(item.ProjectId, item.Name, ProjectRowState.Pending);
            // [Task 4 — carried item 1] Koşullu proje (WaitingForDependency, bu koşu gerçekten bekletiyor)
            // KESİN derlenecekler kümesine GİRMEZ: köküyle birlikte atlanabilir. Paydaş TEK yerden okur —
            // InRunQueueFor'un Build/Rebuild dalıyla AYNI bayrak (kopya YASAK).
            NotePreviewDecision(item.ProjectId, item.WillBuild, item.Conditional); // [D2 · final review — C1]
            // [W1] CurrentSha ataması, aşağıdaki terminal-satır guard'ından ÖNCE ve ondan BAĞIMSIZ yapılır: o
            // guard yalnız WillBuild'i korumak içindir (segment 1'in canlı succeeded→clean geçişi ezilmesin).
            // Sha'nın böyle bir koruma İHTİYACI YOKTUR — tersine, segment 2'nin okuduğu değer segment 1'in
            // persist'ini içerdiği için terminal satırların sol yarısı ancak burada TAZELENİR.
            row.CurrentSha = item.BuiltCommit;
            row.LastBuiltAt = item.LastBuiltAt;              // [v1.16.0] "up to date · 2h" kuyruğu
            row.OwnFilesChanged = item.OwnFilesChanged;      // [v1.16.0] modified ↔ affected ayrımı
            row.FailedAt = item.FailedAt;                    // [spec 2026-09-18 §1-14] "failed · 2h" kuyruğu — defterden, LastBuiltAt gibi
            row.OutputBuiltAt = item.OutputBuiltAt;          // [Faz 3 — Task 7] "built outside this tool 2h" kuyruğu
            // [R-M3] LocalEdits yalnız KOŞU DIŞINDAKİ önizlemeden yazılır: Sync'in (ve Clean/Optimize'ın ardından
            // zincirlenen Sync'in) önizlemesi `git status`'u okur, koşu önizlemesi ise alanı hep false gönderir —
            // o yazılsaydı her koşu Sync'in "local" işaretini silerdi. Ayrım olayın geldiği ANDAKİ koşu
            // durumundandır (RunActive); `_currentRunId` bunu söylemez (yalnız motor ölümünde null'lanır).
            if (!RunActive) row.LocalEdits = item.LocalEdits;
            // [Task 1 — sessiz Sync tazeleme] Guard YALNIZ koşu sürerken geçerlidir: segment 1'in canlı
            // succeeded→clean geçişi segment 2'nin bayat önizlemesiyle ezilmesin. Koşu bittiğinde (RunActive
            // false) — ör. pencereye dönüşün tetiklediği sessiz Sync — terminal satırın kararı da her
            // önizlemeyle TAZELENİR: aksi halde arka planda değişen bir proje (özellikle döngü üyesi) yeşil/
            // "up to date" kalır, yalnız elle Sync düzeltir.
            if (RunActive && row.State is ProjectRowState.Succeeded or ProjectRowState.Failed or ProjectRowState.Skipped) continue;
            row.WillBuild = item.WillBuild;
            row.WillBuildReason = item.Reason; // gerekçe planla AYNI guard'ın içinde — ikisi ayrışamaz
            row.Conditional = item.Conditional;         // [Task 4] dalga/kuyruk/etiket AYNI bayrağı okur
            row.DependencyRoots = item.DependencyRoots; // [Task 4] etiketin tooltip'i — WillBuild/Reason'la AYNI guard
            // [Task 1 review fix round 1] InRunQueue AYRI bir kanaldır (run-scoped) ve yukarıdaki karar
            // alanlarıyla (WillBuild/Reason/Conditional/DependencyRoots) AYNI guard'ı PAYLAŞAMAZ: onlar koşu
            // bittikten sonra da tazelenir (bu task), InRunQueue ise YALNIZ koşu sürerken yükselir — belgelenen
            // değişmez (bkz. alanın kendi XML yorumu + PropagateRunActive'in "koşu biterken kuyruk da düşer"
            // satırı) budur. RunActive burada AYRICA sorulmazsa koşu bittikten sonra gelen bir önizleme (sessiz
            // Sync) InRunQueue'yu sessizce yeniden yükseltir — bugün gözlemlenemez (TEK okuyucu Status'un
            // IsRunActive && InRunQueue dalı, terminal State ondan önce eşleşir) ama alanın kendi doğruluğu
            // okuyucudan BAĞIMSIZ korunmalı.
            row.InRunQueue = RunActive && InRunQueueFor(item, _currentRunMode, row.InCycle); // [Task 1/2] kuyruk YALNIZ bu event'ten VE YALNIZ koşu sürerken
        }
        RaiseRowDecisionsChanged();                          // graf renk girdisini buradan öğrenir
        BuildPreviewApplied?.Invoke(this, EventArgs.Empty); // işaretin kuyruğa devri (MainWindow)
        // [design v1.20.0 §2.7 · Task 7 review 4] Sayaç ve görünür liste işaretin devrinden SONRA türer: durum
        // kovası satırın gösterdiğinden okunur ve kuyruğa girmeyen işaretli satır ancak işaret silinince kendi
        // durumunu gösterir. Önce türeseydi o satır bir sonraki olaya kadar hiçbir kovada sayılmazdı.
        RefreshRunSurface();
    }

    /// <summary>[Task 1/2] Kuyruk üyeliğinin TEK karar yeri — <see cref="OnBuildPreview"/>'ın TEK çağıranı.
    /// Modun DIŞINDA (Build/Rebuild) <see cref="BuildPreviewItem.WillBuild"/>'e eşittir — koşullu proje hariç
    /// (aşağıda). <b>[Task 4 — DEĞİŞEN KURAL]</b> <c>_willBuildIds</c> de ARTIK aynı bayrağı okur ve koşullu
    /// projeyi İÇERMEZ — eski hâlde ikisi ayrışıyordu (kuyruk dışlar, ilerleme paydası sayardı), Task 4
    /// <c>OnBuildPreview</c>'daki tek ekleme noktasını (<c>!item.Conditional</c>) AYNI kaynağa bağladı.
    /// <b>[Task 2 — kök neden B] Cycles modunda kuyruk YALNIZ döngü üyelerine yazılır</b>
    /// (<paramref name="inCycle"/>): motorun bu run'daki kapsamı üyeler + transitif upstream'dir
    /// (<c>CycleRunScope</c>), ama kapsam İÇİNDEKİ bayat bir upstream bağımlılık WillBuild=true olsa da bu
    /// run'ın "kuyruğu" DEĞİLDİR — gri bekler, <c>projectStarted</c> geldiğinde normal yoldan Building'e geçer.
    /// <c>mode</c> <see cref="_currentRunMode"/>'dan okunur — <see cref="OnRunStarted"/>'ın TEK yazdığı alan
    /// (review fix M-2: eskiden Stream.cs partial'ının kendi alanı okunuyordu, bu satır kararını stream'in
    /// işleme SIRASINA bağımlı kılıyordu; bkz. alanın kendi XML yorumu).
    /// <para><b>[koşullu yeniden derleme]</b> Motorun <see cref="BuildPreviewItem.Conditional"/> dediği proje
    /// kuyrukta DEĞİLDİR: WillBuild=true olsa da kesin derlenecek değildir — kökü hâlâ hatalıysa atlanır. Karar
    /// motorundur; burada yalnız okunur.</para></summary>
    private static bool InRunQueueFor(BuildPreviewItem item, RunMode? mode, bool inCycle) =>
        mode == RunMode.Cycles ? inCycle : item.WillBuild == true && !item.Conditional;

    /// <summary>[Task 17] buildPreview'ın önceden oluşturduğu bir satır varsa (Pending) onu Started'a TAŞIR —
    /// EnsureRow yalnız YENİ satırlar için initialState uygular, var olan satırın State'ini DEĞİŞTİRMEZ, bu
    /// yüzden burada AYRICA atanır (ProjectSkipped'in zaten yaptığı gibi).</summary>
    private void OnProjectStarted(ProjectStartedEvent e)
    {
        var row = EnsureRow(e.ProjectId, e.Name, ProjectRowState.Started);
        row.State = ProjectRowState.Started;
        row.SkipReason = null;    // bu koşuda GERÇEKTEN derleniyor — önceki segmentin atlama gerekçesi geçersiz
        row.CycleWaiting = false; // sıra ONDA: bu event'in anlamı tam olarak budur
        _projectStartedAtMs[e.ProjectId] = _nowMs();
        // [cycle rounds/I2] Bir SCC üyesi başladıysa, KARDEŞLERİ artık beklemededir: grup içinde eşzamanlı
        // invoke YOKTUR (biri diğerinin az önce yazdığı DLL'i okur). Ara tur sonuçları yayılmadığı için
        // kardeşler Started'ta KALIR — bayrak, "Started" ile "şu an derleniyor"u ayıran tek şeydir.
        foreach (string sibling in _cycleGroups?.MembersOf(e.ProjectId) ?? [])
            if (!string.Equals(sibling, e.ProjectId, StringComparison.OrdinalIgnoreCase)
                && FindRow(sibling) is { State: ProjectRowState.Started } waiting)
                waiting.CycleWaiting = true;
        RefreshRunSurface();
    }

    private void OnProjectSkipped(ProjectSkippedEvent e)
    {
        // [Task 2/cycles — kök neden B · review fix I-1] Kapsam-dışı pre-skip bu run'ın parçası DEĞİLDİR: motor
        // kapsam dışı her projeyi kendiliğinden atlar (SkipReasons.OutOfCycleScope, RunCoordinator.cs) ama
        // kullanıcı bu projeyi hiç istemedi — satır motorun "atladım" STATÜSÜNÜ TAŞIMAZ, nötr (Pending/
        // Discovered) kalır: State dokunulmaz, koşu tablosunun atlandı sayacı (RunCounters.Skipped)
        // bu projeyi hiç GÖRMEZ; stream zaten bu gerekçeyi toplu tek satırda
        // birikiyordu (RunViewModel.Stream.cs, DEĞİŞMEDİ). [DEĞİŞEN KURAL — review fix I-1] SkipReason'a YİNE
        // DE yazılır: motor bu run için WillBuild'i her pre-skip'te (kapsam dışı da GERÇEKTEN kirli de) false
        // ZORLAR (RunCoordinator.cs — "amber 'derlenecek' noktası hemen ardından 'skipped' geçen satırda yalan
        // söylemesin"), yani State Pending'de kalınca satırın TEK kanıtı bu alandır — yazılmazsa
        // ConsoleEmptyState.Pending() elde kalan tek bilgiden ("WillBuild=false") "Up to date" der, kapsam dışı
        // ama GERÇEKTEN kirli bir proje için YALAN olurdu. SkipReason'ın App'teki TEK tüketicisi
        // ConsoleEmptyState'tir (bkz. ConsoleEmptyState.Pending/Reason) — sayaç/filtre State okur, bundan
        // ETKİLENMEZ. Bir sonraki run'ın NeutralizeRows'u bunu zaten temizliyor (kopya sıfırlama YOK). Kapsam
        // İÇİ gerçek bir "up to date" skip (SkipReasons.UpToDate) bu dalın DIŞINDA kalır ve aşağıdaki normal
        // yoldan Skipped'a geçmeye devam eder.
        if (_currentRunMode == RunMode.Cycles && e.Reason == SkipReasons.OutOfCycleScope)
        {
            EnsureRow(e.ProjectId, Path.GetFileNameWithoutExtension(e.ProjectId), ProjectRowState.Pending).SkipReason = e.Reason;
            _outOfScopeSkipCount++; // [Task 2 review fix M-1/I-2] bkz. alanın kendi XML yorumu
            UpdateEta(); // [Task 17 deseni — Task 2 review fix M-1] bu da motor açısından bir "tamamlanma"dır
            return;
        }

        var row = EnsureRow(e.ProjectId, Path.GetFileNameWithoutExtension(e.ProjectId), ProjectRowState.Skipped);
        row.State = ProjectRowState.Skipped;
        row.SkipReason = e.Reason; // proje sayfası "neden boş" sorusunu bundan cevaplar
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
            if (FindRow(member) is { } row)
                row.CycleUnconverged = true;
        RefreshRunSurface();
    }

    /// <param name="evidence"><see cref="ProjectFailedEvent.Evidence"/> — yalnız Failed'da anlamlı: motorun, defter
    /// yazımıyla AYNI kapıdan verdiği kanıt kararı.</param>
    /// <param name="trusted"><see cref="ProjectSucceededEvent.Trusted"/> — yalnız Succeeded'da anlamlı: motor bu
    /// başarıyı defterine başarı olarak yazdı mı (AYNI yerden: <c>ReportProjectResult</c>'ın <c>invalidates</c>'i).</param>
    private void OnProjectDone(string projectId, ProjectRowState state, long durationMs, IReadOnlyList<string>? depIssues,
        bool cycleUnsettled = false, bool evidence = false, bool trusted = true)
    {
        var row = FindRow(projectId);
        if (row is null) return; // protokole göre Started her zaman önce gelir — savunmacı no-op
        row.State = state;
        row.DurationMs = durationMs;
        row.DepIssues = depIssues; // [Task 17] BU koşunun listesi — HasRunDepIssue ve (defter notuyla birlikte) WarningRoots bundan
        row.SkipReason = null;     // atlanmadı, derlendi
        row.CycleUnsettled = cycleUnsettled; // [cycle rounds/Task 8] ProjectFailedEvent bu alanı taşımaz → varsayılan false
        // [cycle rounds/Task 9 review fix 1] Proje bu run'da GERÇEKTEN invoke edildi (Succeeded ya da Failed
        // fark etmez) — önceki bir segmentten kalma "hiç invoke edilmeden pre-skip edildi" bayrağı artık
        // YANLIŞ; satır nesneleri segmentler arası hayatta kaldığı için (Projects.Clear() yalnız Rebuild'de)
        // burada temizlenmezse "az önce düzelen proje" render katmanında kalıcı-kırık gibi görünürdü.
        row.CycleUnconverged = false;
        row.CycleWaiting = false; // [cycle rounds/I2] terminal satır hiçbir grubun sırasını beklemez
        // [Task 17][v7Δ8] "succeeded→clean" CANLI geçiş: proje bu run içinde başarıyla derlendiği ANDA artık
        // güncel (clean) sayılır — preview'ın dirty=true'sunu (ya da hollow=null'ını) burada EZER.
        // [tek proje · Clean] Bir Clean koşusunda bu geçiş YAPILMAZ ve bu bir istisna değil aynı kuralın kendisi:
        // orada başarı "derlendi" demek değil "çıktıları silindi" demektir, yani proje güncel DEĞİL, tam tersine
        // derlenmesi gereken hâle gelmiştir. Motor da aynı anda defter kaydını siler (BuildStateStore.Remove).
        // [Task 4 — kök neden C · DEĞİŞEN KURAL] Eskiden HER başarı (dep-issue'lu dahil) buradan koşulsuz
        // WillBuild=false olurdu — motorun kendi kuralıyla (WillBuildEvaluator: DepIssueRoots biliniyorsa
        // WaitingForDependency) ÇELİŞİYORDU. Bu run içinde dep-issue'lu biten bir tekil proje artık "dirty"
        // (Conditional=true) kalır: kesin derlenecekler kümesine (dalga/kuyruk/_willBuildIds) GİRMEZ ama bir
        // sonraki Build'de kökü düzelirse yine derlenmesi gerekir. Bir döngü üyesi de aynı gerekçeyi
        // (WaitingForDependency) taşır ama Conditional=false kalır — TEK BAŞINA asla koşullu değildir (bkz.
        // aşağıdaki AfterSuccess çağrısının yorumu).
        // [Task 4 review round 2 — I1] Üçlü (WillBuild/Reason/Conditional) App'te TÜRETİLMEZ — motorun bir
        // sonraki önizlemesinin (WillBuildEvaluator + ConditionalRebuild.AppliesTo) AYNEN kendisi TEK yerden
        // sorulur (NextPreview.AfterSuccess). Round 1'in kendi kopyası (yalnız bool) bir SCC üyesi için
        // yanlış "koşullu değil" demekle YETİNİYORDU ama etiketi UpToDate'e düşürerek bir sonraki Sync'te
        // (gerçek WaitingForDependency) FLİP ETMESİNE yol açıyordu — üçünün BİRLİKTE, motorla AYNI kaynaktan
        // gelmesi bu boşluğu kapatır.
        if (state == ProjectRowState.Succeeded && trusted) NoteTrustedBuilt(projectId); // [T8] kesilen koşunun özeti
        if (state == ProjectRowState.Succeeded && !RunIsClean)
        {
            // [final review I1] Motorun arkasında durmadığı başarı (trusted=false: yakınsamayan bir SCC'nin
            // yeşil üyesi) defterde kanıtsız hatadır — satır Sync'in okuyacağı NeverBuilt'i şimdiden der.
            var after = NextPreview.AfterSuccess(row.InCycle, trusted, depIssues);
            row.WillBuild = after.WillBuild;
            row.Conditional = after.Conditional;
            row.DependencyRoots = after.Reason == WillBuildReason.WaitingForDependency ? depIssues : null;
            row.WillBuildReason = after.Reason;
        }
        else if (state == ProjectRowState.Succeeded) // Clean
        {
            row.Conditional = false;
            row.DependencyRoots = null;
            // Clean'in başarısı "derlendi" değil "çıktıları silindi"dir: motor defter kaydını da siler, yani
            // proje gerçekten "hiç derlenmemiş" hâline döner (bkz. BuildStateStore.Remove).
            row.WillBuildReason = NextPreview.AfterClean;
        }
        else // Failed
        {
            // Önceki bir preview'dan kalmış olabilecek koşullu bayrak/kökler bu satır için artık ANLAMSIZ —
            // LastFailed gerekçesi kendi tooltip'ini yazar, "bekliyor" olgusu taşımaz.
            row.Conditional = false;
            row.DependencyRoots = null;
            // [R-M4b · spec 2026-09-18 §1-14] Kanıt kararı MOTORUNDUR ve olayla gelir (ProjectFailedEvent.Evidence):
            // defter yazımıyla AYNI kapıdan (RunCoordinator.FailureEvidenceSignature) verilir. Kanıt kırmızıdır
            // ("failed · just now"); kanıt olmayan hata — timeout, Stop, invoke hatası, yakınsamayan bir SCC'nin
            // exit N ile biten üyesi — defterde "hiç başarı yok"tur (WillBuildEvaluator: NeverBuilt), satır da
            // hemen griye iner. App reason metnini YENİDEN sınıflandırmaz: metin SCC üyesinde kanıt gibi görünür.
            // [DEĞİŞEN KURAL — design v1.20.0 §5] Eskiden her hata LastFailed yazardı; timeout'lu satır bir
            // sonraki Sync'e kadar kırmızı durur, Sync onu griye çevirirdi — aynı proje iki farklı renk.
            row.WillBuildReason = NextPreview.AfterFailure(evidence);
            row.FailedAt = evidence ? DateTimeOffset.Now : null;
        }
        if (state == ProjectRowState.Succeeded)
        {
            row.LastBuiltAt = RunIsClean ? null : DateTimeOffset.Now;
            row.OwnFilesChanged = RunIsClean ? null : false;   // az önce derlendi: kendi dosyası artık güncel
            row.FailedAt = null; // başarı eski kanıtı düşürür (defter de FailedSignature'ı siler)
            // [Faz 3 — Task 7] Bu araç projeyi az önce derlediyse "bu araç dışında derlendi" kanıtı ARTIK
            // GEÇERSİZDİR — çıktı şimdi aracın kendi eseri, FailedAt'le AYNI kural (kopya YASAK).
            row.OutputBuiltAt = null;
        }
        _projectStartedAtMs.Remove(projectId);
        UpdateEta(); // [Task 17] her proje tamamlanışında ETA'yı yeniden hesapla
        RefreshRunSurface();
    }

    /// <summary>Satır aramasının TEK kuralı. Proje Id'leri Windows DOSYA YOLLARIDIR, dolayısıyla
    /// karşılaştırma <see cref="StringComparison.OrdinalIgnoreCase"/>'dir — bu dosyadaki sözlükler
    /// (<c>_projectStartedAtMs</c>, <c>_willBuildIds</c>, <c>_liveLines</c>…) zaten aynı karşılaştırıcıyla
    /// kurulur.
    ///
    /// <para><b>Neden tek yerde:</b> aynı arama beş yerde inline kopyalanmıştı ve ikisi (satır tamamlanması
    /// ile satır yaratımı) düz <c>==</c> ile, yani HARF-DUYARLI kalmıştı. Ayrışmanın bedeli sessizdir:
    /// tamamlanma satırı bulamaz (savunmacı no-op) ve satır sonsuza dek "building" görünür; satır yaratımı
    /// ise aynı projeye ikinci bir satır açar. Yeni bir çağıran da buradan geçmelidir.</para></summary>
    private ProjectRowViewModel? FindRow(string id) =>
        Projects.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    private ProjectRowViewModel EnsureRow(string id, string name, ProjectRowState initialState)
    {
        var existing = FindRow(id);
        if (existing is not null) return existing;
        var row = new ProjectRowViewModel(id, name, initialState)
        {
            IsRunActive = RunActive, NamePrefix = _graphNamePrefix,
            // [tek proje] kilit + hedef de aynı itme deseninden gelir (koşu ortasında doğan satır bilir)
            IsRunLocked = IsMidRunLocked,
            IsRunTarget = string.Equals(id, RunTargetId, StringComparison.OrdinalIgnoreCase),
        };
        // Not: bu yol yalnız run ortasında, topolojide OLMAYAN bir id için satır doğurur — harici projeler
        // topolojiden gelir, o yüzden burada harici bir satır oluşamaz.
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
        // [Task 2 review fix M-1] total motorun bu run'a özel plan boyutudur (_totalProjects — tek-proje run'da
        // zaten 1'e kesilir, DEĞİŞMEDİ); _willBuildIds/WillBuildCount'a geçmek burada ÇALIŞMAZ, çünkü bazı
        // testler (ör. RunViewModelTests.EtaText_shows_XofN_fallback_before_any_completion_no_bogus_number)
        // hiç BuildPreviewEvent göndermeden runStarted'ın kendi X/N fallback'ini pinler — o an _willBuildIds hep
        // boştur ve "total<=0" erken dönüşü ETA'yı tamamen susturur.
        int total = _totalProjects ?? Projects.Count;
        if (total <= 0) { EtaText = ""; return; }

        // [Task 2 review fix M-1] Cycles'ta kapsam dışı satırlar artık HİÇBİR ZAMAN terminal olmuyor (bkz.
        // OnProjectSkipped) — düzeltilmezse "completed" workspace'teki HER kapsam dışı proje kadar geride
        // kalır ve "remaining"/queuedCount'u (aşağıda) kalıcı olarak şişirip ETA'yı abartırdı.
        // _outOfScopeSkipCount TEK bu amaç için (bkz. alanın kendi yorumu) — Stream.cs'in kendi
        // `_outOfScopeSkips`'i BURADA KULLANILAMAZ: o alan görüntü tamponudur, her PushStream'de FLUSH edilip
        // sıfırlanır (kümülatif bir toplam değildir).
        int completed = Projects.Count(p => p.State is ProjectRowState.Succeeded or ProjectRowState.Failed or ProjectRowState.Skipped)
            + _outOfScopeSkipCount;
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
        _awaitingRunCompleted = false;
        ElapsedMs = e.DurationMs; // yerel Stopwatch'tan değil, engine'in kesin süresinden — clock drift yok
        IsRunning = false;
        Phase = e.Outcome == RunOutcome.Stopped ? AppPhase.Stopped : AppPhase.Done; // [C2] Running → Done/Stopped
        DepIssueCount = e.DepIssueCount; // [Task 17] run genelinde (Continue segmentleri dahil) kümülatif özet
        RefreshRunSurface();
        // [T8 fix round 1 · I1] Koşunun kendiliğinden Sync için bitişi BURASIDIR (runStopped değil): faz ve akış
        // yazıldıktan SONRA bildirilir — bekleyen tetiğin açacağı yeni bölüm bu koşunun satırlarını taşımaz.
        NotifyAutoSyncGate();
    }

    /// <summary>
    /// [T8 fix round 1 · I1] <c>runStarted</c> görüldü, <c>runCompleted</c> henüz gelmedi. Motor ikisini ayrı
    /// flush'larla yazar, App ayrı UI kuyruğu işleriyle işler: arada <c>runStopped</c> kilidi düşürür ama koşu
    /// kendiliğinden Sync için hâlâ uçuştadır (<see cref="IsRunInFlight"/>). <c>runCompleted</c>, motor kaybı ve
    /// koşu-bitiren hata yolları bırakır.
    /// </summary>
    private bool _awaitingRunCompleted;

    /// <summary>Kendiliğinden Sync'in gördüğü koşu: kilit (<see cref="IsMidRunLocked"/>) ya da henüz
    /// <c>runCompleted</c>'ı gelmemiş başlamış koşu.</summary>
    internal bool IsRunInFlight => IsMidRunLocked || _awaitingRunCompleted;

    /// <summary>[T8 fix round 1 · I1] Bu koşuya ait olmayan bir koşu-sonu olayı: başka bir koşunun id'si, ya da
    /// koşu çoktan bittikten sonra gelen <c>runStopped</c> (host, sahiplenemediği bir Stop'u — ör. koşu kapanırken
    /// giden kesmeyi — anında onaylar). Faza ve akışa dokunmaz; o an başlamış olan işlemin (yeni bölümün Sync'i)
    /// fazını ezerdi. Bekleyen bir Stop (<see cref="AppPhase.Stopping"/>) onayı her zaman kabul edilir — fazı
    /// çözecek başka olay yoktur.</summary>
    private bool IsStaleRunEnd(IpcEvent ev) => ev switch
    {
        RunStoppedEvent e => !IsCurrentRun(e.RunId) || !(IsRunInFlight || Phase == AppPhase.Stopping),
        RunCompletedEvent e => !IsCurrentRun(e.RunId),
        _ => false,
    };

    private bool IsCurrentRun(string runId) =>
        _currentRunId is null || string.Equals(_currentRunId, runId, StringComparison.Ordinal);

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
            // [her projenin sayfası var] Log YOKSA da proje moduna geçilir: sayfa boş kalmaz, o projenin O ANKi
            // durumunu anlatan metni gösterir (Console.ConsoleEmptyState.ForEmptyLog). Eskiden mod hiç kurulmuyor,
            // kullanıcı run anlatısına bakıyordu — tıklama "hiçbir şey yapmıyor" gibi görünüyordu.
            EnterProjectMode(pending.ProjectId);
            pending.Completion.TrySetResult();
        }
        // [B1] REDDEDİLEN bir başlatma isteği (runInProgress) kendi "starting" bayrağını BIRAKMALIDIR. Motor run
        // slotunu (_runActive) tüm event'ler yazıldıktan SONRA bırakır (ExecuteRunAsync'in finally'si), yani
        // runCompleted App'e ulaştıktan sonra kısa bir pencere boyunca slot HÂLÂ doludur — butonlar tam o anda
        // açıldığı için hızlı bir tıklama bu reddi alır. Bayrak temizlenmezse UI kilit penceresinde SONSUZA DEK
        // donardı: Build/Rebuild disabled, Stop görünür ama arkada durdurulacak bir şey yok. Koşan run'a
        // DOKUNULMAZ (aşağıdaki erken dönüş) — kilit gerçekten koşuyorsa IsRunning üzerinden zaten sürer.
        // [clean] Clean'in kendi hata kodları (cleanFailed/cleanRejected) AYRIK bir kümedir ve yalnız Clean
        // yüzeyini bırakır — run/Sync state'ine DOKUNMAZ. Sıra kritik değil (kodlar çakışmaz), ama erken
        // dönüş RunEndingErrorCodes kapısından önce gelmelidir: bu kodlar orada YOKTUR.
        if (TryConsumeCleanFailure(e.Code, e.Message)) return;
        if (TryConsumeOptimizeFailure(e.Code, e.Message)) return;
        if (TryConsumeCheckoutFailure(e.Code)) return;
        if (e.Code == RunInProgressCode) IsStarting = false;
        if (!RunEndingErrorCodes.Contains(e.Code)) return; // runInProgress/logNotFound/... aktif run'ı ETKİLEMEZ
        // [A5/T69 · Fix wave 1, Finding 2] Sync fazını bırakır ve hatanın KAYNAĞINI ayırt eder: kod Sync'ten
        // geldiyse (uçuşta bir Sync var ve run planlama penceresinde DEĞİL) run state'ine DOKUNULMAZ — Sync
        // salt-okurdur ve koşan bir run sırasında da tetiklenebilir. Gerekçe: RunViewModel.Workspace.cs.
        if (TryConsumeSyncFailure(e.Code, e.Message)) return;
        _awaitingRunCompleted = false; // runCompleted gelmeyecek — kilit düşüşü koşunun bitişidir
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
        _awaitingRunCompleted = false; // motor gitti — runCompleted gelmeyecek
        IsRunning = false;
        IsStarting = false;
        _currentRunId = null;
        // [C2 fold — A5 review] Engine Sync ortasında ölürse hiçbir syncCompleted/Sync-hatası gelmez; faz
        // Syncing'de asılı kalır ve _syncInFlight sızardı. RunEndingErrorCodes deseniyle simetrik olarak burada
        // da uçuştaki Sync serbest bırakılır.
        ReleaseSyncPhase();
        // [clean] Aynı gerekçe: motor Clean ORTASINDA ölürse hiçbir cleanCompleted/clean-hatası gelmez ve
        // bayrak sızarsa yeniden başlatılan motorda da düğmeler kilitli kalırdı.
        ReleaseCleanSurface();
        ReleaseOptimizeSurface();
        SetCheckoutBusy(false); // [§6.3] motor checkout ortasında öldüyse cevap gelmez — chip kilidi sızmaz
        SetPullBusy(false); // [§6.1] motor pull ortasında öldüyse pullCompleted gelmez — kapı sızmaz
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
    /// <para>[About] Sürüm ve PID ayrıca SAKLANIR (Environment sekmesi okur); boot satırı DEĞİŞMEDİ.</para>
    /// <para>[spec 2026-09-18 §6.2 "Uygulama açılışı"] Motor hazır olduğunda, bir workspace varsa ve Sync'e izin
    /// varsa, fetch'li bir Sync başlar (<see cref="SyncMode.Appended"/>: boot satırları kalır, transkript altına akar).
    /// İki çağıran: kabuğun ilk açılışı (<c>MainWindow.StartEngineAsync</c>) ve <see cref="RestartEngineAsync"/>.
    /// Yalnız eski havuz ipucu oturum başına BİR kez yazılır (<see cref="_engineWasReady"/>).</para>
    /// <para><b>[DEĞİŞEN KURAL — final review I1]</b> Eskiden Sync yalnız ilk hazır oluşta giderdi ("restart dünyayı
    /// değiştirmez"). Oysa çökmeden sonra yeniden başlayan motor uçuştaki projeleri kurtarır (spec §5.5): ekranın
    /// kararları artık yanlıştır ve kurtarılan satırlar ancak bir Sync'le gri "never built" okunur.</para>
    /// <para><b>[DEĞİŞEN KURAL — spec 2026-09-18 §6.2]</b> Eskiden açılış "seed-but-idle"dı (kayıtlı repo bilinir
    /// ama Sync kullanıcıya kalır — <c>MainWindow</c> kök seed'i). Seed'in kendisi hâlâ komut göndermez
    /// (<c>RunViewModelStateTests.Seeding_the_root_path_directly_lands_in_boot_without_starting_a_sync</c>);
    /// açılışın Sync'i motor hazır olunca buradan gider.</para></summary>
    public void OnEngineReady(string engineVersion, int pid)
    {
        EngineVersion = engineVersion;
        EnginePid = pid;
        AppendRunLine($"Engine ready — v{engineVersion}");
        if (!_engineWasReady)
        {
            _engineWasReady = true;
            string? legacyPoolHint = LegacyWorktreePool.Hint(LegacyWorktreePoolRoot);
            if (legacyPoolHint is not null) AppendRunLine(legacyPoolHint);
        }
        if (HasWorkspace && CanSync()) _ = SyncCoreAsync(SyncMode.Appended);
    }

    /// <summary>[Task 11 · test injection] Eski worktree havuzunun kökü — üretimde
    /// <see cref="LegacyWorktreePool.DefaultRoot"/>, testlerde gerçek %LOCALAPPDATA%'a bakmasın diye
    /// override edilir.</summary>
    internal string LegacyWorktreePoolRoot { get; set; } = LegacyWorktreePool.DefaultRoot;

    /// <summary>[spec 2026-09-18 §5.5 · karar 12] Motor açılışta kesilmiş bir koşu kurtardı: konsola kaç projenin
    /// yeniden derleneceği yazılır (metin Core'daki tek kaynaktan). Olay akışından (<see cref="OnEvent"/>) gelir,
    /// <see cref="OnEngineReady"/>'nin çağıranından DEĞİL: <c>engineReady</c> hem ilk açılışta hem
    /// <see cref="RestartEngineAsync"/>'te <c>EventReceived</c>'dan geçer — çökmeden sonra yeniden başlatılan motor
    /// tam da kurtarmanın gerektiği durumdur, ve tek dal iki yolu birden kapsar.</summary>
    private void OnEngineRecovered(int interruptedProjects)
    {
        if (interruptedProjects > 0) AppendRunLine(PlanProgressLines.PreviousRunInterrupted(interruptedProjects));
    }

    /// <summary>Motor bu oturumda en az bir kez hazır oldu mu — eski havuz ipucu yalnız ilk hazır oluşta yazılır.</summary>
    private bool _engineWasReady;

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

    /// <summary>[design v1.11.0 §9-4 `_beginOp` · D3/T5 v1.13.2] Yeni bir işlem başlıyor: konsol tamponları
    /// (run dokümanı + tüm proje logları) TEMİZLENİR — ekrandaki her şey artık yürüyen işlemin hikâyesidir.
    /// <see cref="ClearStreamForNewOperation"/>'ın konsol eşi; ikisi birlikte "her işlemde temizlenir" kuralını
    /// oluşturur. <see cref="BeginRunAsync"/> (Build/Rebuild/Cycles) VE <see cref="SyncCoreAsync"/> (Sync)
    /// AYNI metodu paylaşır — inline kopya YASAK. <b>Koşulsuz "her işlemde" OKUMA:</b> <see cref="SyncCoreAsync"/> bu metodu
    /// yalnız bölüm açan kiplerde (<see cref="SyncMode.Manual"/>, <see cref="SyncMode.BranchChange"/>) çağırır —
    /// nüans (Appended/Silent'ın geçmişi korumak için atlaması) <see cref="SyncCoreAsync"/>'in kendi XML
    /// doc'undadır.
    /// <para><b>[DEĞİŞEN KURAL — v1.13.2]</b> Bu gövde önceden yalnız <see cref="BeginRunAsync"/>'in İÇİNDE,
    /// adsız bir <c>if (clearBuffers) lock (_gate) { … }</c> bloğuydu; Sync bu bloğa hiç uğramadığından
    /// motorun <c>syncProgress</c> satırları bir önceki işlemin tortusunun ÜZERİNE yazılıyordu (kanıt:
    /// <see cref="RunViewModelStateTests.Sync_clears_the_console_and_stream_left_over_from_the_previous_operation"/>).
    /// Tasarım v1.13.2 "Konsol + event stream her işlemde temizlenir" kuralını Sync'i de kapsayacak şekilde
    /// netleştirdi; blok burada adlandırılıp <see cref="SyncCoreAsync"/>'e de bağlandı.</para></summary>
    private void ClearConsoleForNewOperation()
    {
        lock (_gate)
        {
            _liveLines.Clear();
            _projectText.Clear();
            _runText.Clear();
            _runLineCount = 0;
            _projectLineCount.Clear();
            // Uçuştaki bayat batch'ler düşer (SeedRunDocument ile AYNI sentinel): temizlikten ÖNCE pompaya girmiş
            // bir önceki işlemin satırı, temizlikten SONRA ekrana sızamaz (nesil damgası — ConsoleBatcher).
            _console.PostReseedDrop();
        }
        ConsoleCleared?.Invoke(this, EventArgs.Empty); // kilit DIŞINDA: kabuk WPF belgesini kurar
    }

    /// <summary>
    /// [design v1.13.2 §2.5 · §9] Konsol tamponu bir işlem başlangıcında SİLİNDİĞİNDE ateşler
    /// (<see cref="ClearConsoleForNewOperation"/>) — kabuk ekrandaki AvalonEdit belgesini de boşaltır
    /// (<c>ConsoleView.ClearRunDocument</c>). VM tamponu ile ekran ayrı iki kopyadır ve ikincisi yalnız
    /// mod geçişinde yeniden kuruluyordu; bu olay o kopyayı işlem başlangıcında da hizalar.
    /// </summary>
    public event EventHandler? ConsoleCleared;

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
        var row = FindRow(projectId);
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
            // [her projenin sayfası var] Proje moduna BURADA GEÇİLMEZ — <c>logNotFound</c> dalının aksine.
            // Fark cevabın kendisindedir: <c>logNotFound</c> motorun KESİN cevabıdır ("log yok"), gönderim
            // hatası ise hiç cevap DEĞİLDİR — istek yola bile çıkmadı ve `_pendingLoad` bilerek yaşatılıyor,
            // çünkü gecikmiş bir chunk hâlâ dikişi tamamlayabilir. Burada modu kurmak, kullanıcı arada kartı
            // bıraksa bile ActiveProjectId'yi o projede TAKILI bırakır ve run konsolu sessizce donar (ölçüldü:
            // RunViewModelTests.Deselecting_before_the_project_log_chunk_arrives_does_not_freeze_the_run_console).
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
            EnterProjectMode(e.ProjectId);
        }
        DebugAfterStitchLockExited?.Invoke(); // yalnız testler ayarlar — bkz. alan tanımı
        _pendingLoad = null;
        pending.Completion.TrySetResult();
    }

    /// <summary>
    /// Konsolu proje moduna alan TEK kapı. Üç çağıranı vardır ve üçü de aynı soruyu sorar: dikiş tamamlandı,
    /// motor "log yok" dedi, ya da gönderim hiç gitmedi — <b>her üçünde de o projenin sayfası açılır</b>.
    ///
    /// <para><b>Kapı:</b> yükleme HÂLÂ isteniyorsa, yani kart hâlâ seçiliyse. Hızlı select→deselect'te gecikmiş
    /// bir cevap <see cref="ActiveProjectId"/>'yi kurup TAKILI bırakıyordu ve <see cref="AppendRunLine"/>
    /// (ActiveProjectId null kapısı) sonrasında hiçbir anlatı satırını post edemiyordu — run konsolu sessizce
    /// donardı.</para>
    ///
    /// <para><b>Neden kilit altında:</b> <see cref="OnProjectLog"/> arka plan thread'inden AYNI kilitle
    /// <c>_projectText</c>'e yazar. Atama kilit dışında kalsaydı, kilidin kapanmasıyla atama arasındaki dar
    /// aralıkta gelen bir satır ne tampona ne ekrana düşerdi (bkz. <see cref="OnProjectLogChunk"/>'ın dikiş
    /// gerekçesi). Monitor reentrant'tır — dikiş yolu bu metodu zaten kilidin İÇİNDEN çağırır.</para></summary>
    private void EnterProjectMode(string projectId)
    {
        lock (_gate)
            if (string.Equals(SelectedProjectId, projectId, StringComparison.OrdinalIgnoreCase))
                ActiveProjectId = projectId;
    }

    private sealed class PendingLoad(string projectId)
    {
        public string ProjectId { get; } = projectId;
        public StringBuilder Assembly { get; } = new();
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
