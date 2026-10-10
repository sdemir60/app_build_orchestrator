using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [D3/T?] <see cref="RunViewModel"/>'in event-stream yüzeyi (ayrı partial — Workspace.cs deseni). IPC
/// event'lerinden (UI-thread <see cref="RunViewModel.OnEvent"/> dalı — marshal-free ProjectLogEvent hot-path'e
/// DOKUNULMAZ) tampon anlatı satırları + canlı aktif satır türetir. Karar mantığı SAF çekirdekte
/// (<see cref="StreamComposer"/>/<see cref="StreamText"/>); burada yalnız kablaj + gözlemlenebilir yüzey.
/// </summary>
public sealed partial class RunViewModel
{
    private readonly StreamComposer _stream = new();
    // BuildApp.jsx:677 — ilk newest satır daktilo ETMEZ (prevNewest==null); sonrakiler (fırtına/hata değilse) eder.
    private bool _streamHadNewest;
    // [D3 §2] koşunun başlangıç anlatı satırı ("Build started" ailesi) RunStarted'dan BuildPreview'a ERTELENİR — will-build sayısı
    // (RunStartedEvent.TotalProjects DEĞİL, o skip'leri de sayar) ancak BuildPreview işlendikten SONRA hazırdır.
    // RunStarted mode'u burada tutulur; BuildPreview satırı yayıp bunu TEMİZLER (satır koşu başına bir kez yazılır) —
    // koşu önizlemeye varmadan biterse MarkRunEnded da temizler (ikisi de ForgetPendingRunStart üzerinden).
    private RunMode? _pendingRunStartMode;
    // [PERF Faz D / karar 10 · kırpma notu görünür] Motor işçi sayısını kırptıysa akıştaki "workers reduced to …" satırı
    // (WorkersReducedNote) — başlangıç satırıyla BİRLİKTE ertelenir ve onun HEMEN ardından yayılır. _pendingRunStartMode
    // ile aynı yerde yazılır (her runStarted'da, kırpma yoksa null — önceki koşunun bekleyen satırı taşınmaz) ve aynı
    // yerde temizlenir (ForgetPendingRunStart).
    private string? _pendingWorkersReducedNote;
    // [koşu başı uyarıları görünür] Motorun koşu başı uyarıları (runStarted.Warnings — bayat obj, ters katman): akışa Warn
    // satırı olarak başlangıç satırının (ve varsa kırpma satırının) ardından yayılır — eşiği aşan liste tek sayan satıra
    // katlanır (StreamText.RunStartWarningLines) — tek projelik koşuda da. Kırpma
    // satırıyla aynı yerde yazılır (her runStarted'da, uyarı yoksa null) ve aynı yerde temizlenir (ForgetPendingRunStart).
    private IReadOnlyList<string>? _pendingRunStartWarnings;

    /// <summary>[kırpma notu görünür · review M1 · koşu başı uyarıları] Bekleyen koşu-başlangıç akış durumunu (başlangıç
    /// satırının kipi, kırpma satırı ve koşu başı uyarıları) BİRLİKTE bırakır; üçünün sıfırlandığı TEK yer burasıdır. İki
    /// çağıran var: <see cref="BuildPreviewEvent"/> dalı (satırlar yayıldı) ve <see cref="MarkRunEnded"/> (koşu önizlemesine
    /// varmadan bitti: motor <c>runStarted</c>'tan sonra, önizlemeden ÖNCE öldü ya da koşu-bitiren hata geldi). İkincisi
    /// olmazsa Restart sonrası Appended Sync'in önizlemesi (<see cref="BuildPreviewEvent"/>'in tek diğer üreticisi) ölü
    /// koşunun "Build started", "workers reduced" ve uyarı satırlarını yeni akışa basardı.</summary>
    private void ForgetPendingRunStart()
    {
        _pendingRunStartMode = null;
        _pendingWorkersReducedNote = null;
        _pendingRunStartWarnings = null;
    }

    // [Task 2/cycles · review fix M-2] Bu run'ın modu artık BURADA TUTULMAZ (kopya YASAK) — tek yazıcı
    // RunViewModel.cs'in `_currentRunMode` alanı (OnRunStarted). Eskiden burada AYRI bir `_streamRunMode` vardı
    // ve bu partial'ın kendi AppendStreamFor'unda (OnEvent'in OnRunStarted'dan SONRA çağırdığı ikinci dal)
    // yazılıyordu — satır kararı (InRunQueueFor, OnProjectSkipped) o alanı okuyunca DOĞRU sonucu stream'in
    // işleme SIRASINA borçlu kalıyordu (yeni bir event tipi/sıra değişikliği sessizce kırabilirdi).

    // [Task 2/cycles] Cycles koşusunda SkipReasons.OutOfCycleScope gerekçeli skip'ler burada BİRİKİR (satır
    // YAZILMAZ) — PushStream'in başında tek Info satırına flush edilir (bkz. PushStream).
    private int _outOfScopeSkips;

    // [Task 4] Cycle round ilerleme takibi — şeridin tur satırının ve aktif satırdaki "member i/N · round r/cap"
    // detayının kaynağı. Turun dışındaki projeler — Cycles'ta bayat upstream, düz Build'de sıradan projeler (turla
    // EŞZAMANLI da derlenebilirler) — detay almaz. RunStarted/RunCompleted'ta sıfırlanır (ResetCycleRoundCounters).
    // [Review fix — Finding 1] Motor (RunCoordinator) eşzamanlı SCC'leri SERİLEŞTİRMEZ ve Cycles modunda paralellik
    // kelepçelemez — ≥2 grup aynı anda koşabilir, bu yüzden "tek aktif grup" varsayımı YANLIŞTIR. Her grubun kendi
    // sayacı vardır: ProjectStartedEvent yalnız üyesi olduğu grubun sayacını ilerletir (yanlış "member 3/2" yok).
    // [DEĞİŞEN KURAL — final inceleme, iki grup] Eskiden yalnız EKRANDAKİ grubun sayaçları tutulurdu ve o grup bitince
    // sıfırlanırdı: aynı anda turda olan ikinci grup varken şerit, onun bir sonraki turu başlayana kadar "Building"
    // yazıyordu. Artık turu süren her grup _roundsInFlight'tadır; ekrandaki grup EN SON tur başlatandır, o bitince
    // turu süren en son başka grup ekrana geçer, kimse kalmazsa şerit kendi satırına döner.
    private readonly Dictionary<string, CycleRoundState> _roundsInFlight = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Turu süren grupların liderleri, tur başlangıç sırasıyla (en yenisi sonda) — ekrandaki grup bitince
    /// sıradaki ekrana buradan geçer.</summary>
    private readonly List<string> _roundOrder = [];

    /// <summary>Bir grubun uçuştaki turu: motorun kararı (<c>CycleRoundStartedEvent</c>) ve bu turda başlayan üye sayısı.</summary>
    private sealed class CycleRoundState(int round, int cap, int memberCount)
    {
        public int Round { get; } = round;
        public int Cap { get; } = cap;
        public int MemberCount { get; } = memberCount;
        public int MemberIndex { get; set; }
    }

    private CycleRoundState? RoundOnScreen =>
        _cycleRoundLeaderId is { } leader && _roundsInFlight.TryGetValue(leader, out var state) ? state : null;

    /// <summary>[tek proje · Clean] Uçuştaki koşu bir <b>Clean</b> mi — <see cref="OnProjectDone"/> bunu okur
    /// ("başarı" orada güncelliğe değil, çıktının SİLİNMİŞ olmasına karşılık gelir). Kaynak, koşunun modunu
    /// zaten tutan <see cref="RunViewModel._currentRunMode"/>'dur: ikinci bir alan tutulmaz (kopya YASAK).</summary>
    private bool RunIsClean => _currentRunMode == RunMode.Clean;

    /// <summary>[design v1.7.0 §3.7] Şu an bir <b>Resolve cycles</b> koşusu mu sürüyor — bakım kutusu Resolve
    /// düğmesini buna göre amber zemin + spinner'a çevirir, Restart kilidi onu görev sayar ve şerit turlardan önceki
    /// pencerede "preparing dependencies" yazar. [Build cycle derler] Şeridin TUR satırı buna değil, uçuştaki tura
    /// bağlıdır (<see cref="RibbonText"/>, <c>cycleRound &gt; 0</c>): düz Build'in grubu da turlarını koşarken yazar.
    /// <para>İki terimi de bildirimlidir: <c>IsRunning</c> (yani <c>RunActive</c>) attribute zinciriyle,
    /// <see cref="RunViewModel._currentRunMode"/> ise <see cref="RunViewModel.OnRunStarted"/>'da AÇIKÇA
    /// yayınlar — türetilmiş özellikler kendiliğinden <c>PropertyChanged</c> üretmez ve kutu, şerit gibi başka
    /// bir bildirimin sırtına binemez (bkz. aşağıdaki <c>OnPropertyChanged(nameof(IsResolvingCycles))</c>
    /// çağrısı, RunStartedEvent'in kendi dalında).</para></summary>
    public bool IsResolvingCycles => _currentRunMode == RunMode.Cycles && RunActive;

    /// <summary>[design v1.7.0 §3.7] Ekrandaki grubun turu ve tavanı — motorun kararı (<c>CycleRoundStartedEvent</c>);
    /// turu süren grup yoksa (upstream/prerequisite aşaması ya da gruplar bitti) <c>0</c>.</summary>
    public int CycleRound => RoundOnScreen?.Round ?? 0;
    public int CycleRoundCap => RoundOnScreen?.Cap ?? 0;

    /// <summary>Ekrandaki grubun lideri: en son tur başlatan, turu süren grup.</summary>
    private string? _cycleRoundLeaderId;

    /// <summary>[D8] Duvar-saati zaman damgası kaynağı (stream satırı "HH:mm:ss") — testte deterministik enjekte
    /// edilebilir; üretimde <see cref="DateTimeOffset.Now"/>. Fırtına/elapsed saati (<c>_nowMs</c>) AYRIDIR
    /// (monoton ms; duvar-saati DEĞİL).</summary>
    internal Func<DateTimeOffset> WallClock { get; set; } = () => DateTimeOffset.Now;

    /// <summary>Görünen dilim (≤150) — <see cref="Views.EventStreamView"/> bunu bağlar. Tam tampon sayacı
    /// <see cref="StreamEventCount"/> AYRIDIR (Ek A #23 ile aynı ilke).</summary>
    public ObservableCollection<StreamEventViewModel> StreamEvents { get; } = [];

    /// <summary>"{n} events" sayacı — TAM tampon (≤260), render dilimi DEĞİL.</summary>
    [ObservableProperty] private int _streamEventCount;

    /// <summary>[design v1.11.0 §9-4 <c>_beginOp</c>] Yeni bir işlem başlıyor: event stream de konsol gibi
    /// TEMİZLENİR — ekrandaki her şey artık yürüyen işlemin hikâyesidir.
    /// <para><b>[DEĞİŞEN KURAL — v1.13.2]</b> Bu satır eskiden "Sync bu yoldan GEÇMEZ" diyordu — <see cref="SyncAsync"/>
    /// yalnız faz metnini/pill'i güncelliyordu, tamponlara dokunmuyordu. Tasarım v1.13.2 "her işlemde
    /// temizlenir" kuralını Sync'i de kapsayacak netleştirdi; Sync düğmesi artık <see cref="SyncCoreAsync"/>
    /// üzerinden BeginRunAsync ile AYNI iki metodu (bunu ve konsol eşi <see cref="ClearConsoleForNewOperation"/>'ı)
    /// TIKLAMA ANINDA çağırır; yalnız bölüm açan kipler (<see cref="SyncMode.Manual"/>,
    /// <see cref="SyncMode.BranchChange"/>) temizler — Appended/Silent bu ikisini atlar (bkz.
    /// <see cref="SyncCoreAsync"/> XML doc'u).</para>
    /// <para>Silme <c>RemoveAt</c> ile sondan yapılır: <c>Clear()</c> bir <c>Reset</c> bildirimidir ve koşan
    /// satır animasyonlarını yıkar (A13.2 — koleksiyon reset'i YASAK).</para></summary>
    private void ClearStreamForNewOperation()
    {
        _stream.BeginOperation();
        for (int i = StreamEvents.Count - 1; i >= 0; i--) StreamEvents.RemoveAt(i);
        StreamEventCount = 0;
        _streamHadNewest = false; // ilk satır daktilosuz basılır (temiz sayfanın ilk satırıdır)
        SyncActiveLine();
    }

    /// <summary>Aktif satırın projesi (tıklama → <see cref="SelectProject"/>); hiç building yoksa null.</summary>
    [ObservableProperty] private string? _activeLineProjectId;
    /// <summary>Aktif satır metni "<c>{name} building…</c>" ya da null (satır gizli). [Task 4] Proje bir cycle
    /// round üyesiyse (round aktifken) sona "<c>· member {i}/{N} · round {r}/{cap}</c>" eki eklenir — bkz.
    /// <see cref="StreamText.CycleMemberDetail"/>; upstream/prerequisite projelerde ek YOKTUR.</summary>
    [ObservableProperty] private string? _activeLineText;
    /// <summary>Aktif proje her DEĞİŞTİĞİNDE artar — görünüm daktiloyu yeniden başlatır (prototip activeLine.id).</summary>
    [ObservableProperty] private long _activeLineGeneration;

    /// <summary>[OnEvent kablajı] Bir UI-thread IPC event'inden stream tampon satırı + aktif satır türetir.
    /// <see cref="OnEvent"/>'in SONUNDA çağrılır — proje satırları/sayaçlar (Counters) o an zaten güncellenmiştir,
    /// bu yüzden ad çözümü ve done-glyph'in yeşil/kırmızı kararı (Counters.Failed) doğru okunur.</summary>
    private void AppendStreamFor(IpcEvent ev)
    {
        // [D3 §4] Marshal-free ProjectLogEvent (saniyede binlerce) + ProjectLogChunkEvent akışı stream'e HİÇBİR
        // satır katmaz — 7-yollu type-switch'i boşuna koşturup no-op'a düşme; switch'ten ÖNCE erken dön.
        if (ev is ProjectLogEvent or ProjectLogChunkEvent) return;

        switch (ev)
        {
            case RunStartedEvent e:
                _stream.EndRun(); // yeni koşu: aktif + building sıfırlanır (tampon sayacı KORUNUR)
                SyncActiveLine();
                // [D3 §2] başlangıç satırını ("Build started" ailesi) BuildPreviewEvent'e ERTELE — will-build sayısı orada
                // hazır (BuildPreview RunStarted'ı hemen izler — yayın sırası: BuildPreviewEvent'in doc'u). Burada
                // YAYMA; yalnız mode'u işaretle.
                _pendingRunStartMode = e.Mode;
                _pendingWorkersReducedNote = WorkersReducedNote(e);
                _pendingRunStartWarnings = e.Warnings; // [koşu başı uyarıları görünür] tek proje kapısı YOK (alanın yorumu)
                // [Task 2 review fix M-2] `_currentRunMode` BURADA YAZILMAZ — OnEvent bu case'e gelmeden ÖNCE
                // RunViewModel.cs'in OnRunStarted'ı onu zaten yazmıştır (tek yazıcı). Bildirim yine BURADA: o
                // metodun bildirimsiz bir alanı, IsResolvingCycles'ın değeri değişti diye UI'a haber vermesi
                // gerekir.
                OnPropertyChanged(nameof(IsResolvingCycles)); // bakım kutusunun Resolve spinner'ı bunu okur
                NotifyUpdateRestartGate(); // [design v1.23.0 §2.12] Resolve, Restart kilidinde görev gibi okunur
                // [Task 4] Yeni run: önceki koşunun round ilerlemesi bu run'ı ETKİLEMEZ.
                ResetCycleRoundCounters();
                // [Review fix — Finding 2] _outOfScopeSkips YALNIZ RunCompletedEvent'te sıfırlanıyordu; motor bir
                // Cycles koşusu ORTASINDA ölürse (RunCompletedEvent hiç gelmez) sayaç asılı kalır ve BİR SONRAKİ
                // run'ın ilk PushStream'ine eski bir "N outside cycle scope — skipped" satırı sızdırırdı. Her yeni
                // run temiz başlasın diye burada da sıfırlanır.
                _outOfScopeSkips = 0;
                break;

            case BuildPreviewEvent:
                // [D3 §2] Ertelenen run-start satırını burada yay — OnBuildPreview (OnEvent'te BUNDAN ÖNCE) hem
                // önizleme kümelerini doldurdu hem RefreshRunSurface ile FinishedOfWillBuild'i tazeledi. Pending'i
                // TEMİZLE ki koşudan SONRA gelen bir Sync önizlemesi (BuildPreviewEvent'in tek diğer üreticisi,
                // SyncWorkspaceService) başlangıç satırını ikinci kez yaymasın.
                // [final review — C1 · DEĞİŞEN KURAL] Açılış satırının sayısı KESİN kümeden (_willBuildIds)
                // DEĞİL, koşunun PLANINDAN (_dirtyIds — koşullu projeler dahil) gelir: kesin küme Task 4'ten beri
                // koşulluyu dışlıyor ve dirty kümesi tamamen koşullu olan bir koşu konsolu "Build started — 0
                // projects" diye açıyordu — hemen ardından o projeyi derlerken. Satır koşunun ne kadar iş
                // DEĞERLENDİRECEĞİNİ söyler; kaçının kesin olduğunu şerit zaten ayrı sayar.
                if (_pendingRunStartMode is { } mode)
                {
                    int parallelism = _runParallelism ?? Parallelism;
                    PushStream(StreamKind.Info, null, mode switch
                    {
                        // [tek proje] Kapsamlı koşu hedefi söyler — "1 projects, parallelism N" tek bir proje
                        // için hem gramer hem anlam olarak yanlıştı (paralellik onu tarif etmez).
                        _ when RunTargetId is { } targetId => StreamText.SingleProjectStarted(mode, ResolveName(targetId)),
                        // [cycles/Task 4] Bu koşu bir build DEĞİLDİR ve paralellik onu tarif etmez: bir SCC'nin
                        // turları, genişliği grubun iç şekline bağlı dalgalarla koşar. Kırılım dirty ∩ üyelik'ten
                        // (_cycleGroups.IsMember) — kalan upstream/prerequisite'tir; kullanıcı "neden bu kadar proje
                        // derleniyor"u burada okur.
                        RunMode.Cycles => StreamText.CyclesStarted(
                            members: _dirtyIds.Count(id => _cycleGroups?.IsMember(id) == true),
                            prerequisites: _dirtyIds.Count(id => _cycleGroups?.IsMember(id) != true)),
                        // [Clean] Tam Clean hiçbir şey derlemez: satır işi kendi fiiliyle söyler (satır Clean'inin
                        // "Clean started — a (single project)" satırıyla aynı dil).
                        RunMode.Clean => StreamText.CleanStarted(_dirtyIds.Count, parallelism),
                        _ => StreamText.BuildStarted(_dirtyIds.Count, parallelism),
                    });
                    // [kırpma notu görünür] Kırpma satırı başlangıç satırının HEMEN ardından — konsolu kalabalık bir
                    // koşuda da gözden kaçmasın. Info: başlangıç satırının anlatı tonu; motor isteği makineye uydurdu,
                    // hiçbir şey reddedilmedi (Warn bir reddin ya da bir uyarınındır).
                    if (_pendingWorkersReducedNote is { } reductionNote)
                        PushStream(StreamKind.Info, null, reductionNote);
                    // [koşu başı uyarıları görünür] Koşu başı uyarıları onların ardından, motorun sırasıyla (bayat obj, ters
                    // katman). Warn: kullanıcının bakması gereken bir sorun. Akışın Warn satırları önek taşımaz; öneki
                    // StreamText düşürür (konsol satırı AYNEN kalır). [akış seli] Çok sayıda uyarı akışın sınırlı tamponunu
                    // doldurmasın diye tavanı aşan uyarılar TEK özet satırına iner (StreamText.RunStartWarningLines);
                    // konsol her satırı yazmaya devam eder.
                    foreach (string runStartWarning in StreamText.RunStartWarningLines(_pendingRunStartWarnings))
                        PushStream(StreamKind.Warn, null, runStartWarning);
                    ForgetPendingRunStart();
                }
                break;

            case ProjectStartedEvent e:
                // [Task 4] Bu proje turu süren bir grubun üyesiyse o GRUBUN sayacı ilerler; grup ekrandaysa aktif satır
                // "member i/N · round r/cap" detayını taşır. Değilse (Cycles'ta upstream, düz Build'de turla eşzamanlı
                // derlenen sıradan proje, ya da ekranda olmayan grubun üyesi) detay YOK — düz "{name} building…".
                // [Review fix — Finding 1] Kapı GLOBAL _cycleGroups.IsMember DEĞİL — turu başlatan LİDERİN GRUBUNA
                // (MembersOf(lider)) üyelik: B grubunun üyesi A grubunun sayacını TÜKETMEZ (ör. "member 3/2"). Ekranda
                // olmayan grubun sayacı da ilerler ki o grup ekrana geçince doğru sayıdan devam etsin. MembersOf
                // build-order LİSTESİ döner; id karşılaştırması kod tabanının kimlik kuralıyla (OrdinalIgnoreCase).
                string? detail = null;
                foreach (var (leaderId, round) in _roundsInFlight)
                {
                    if (!(_cycleGroups?.MembersOf(leaderId).Contains(e.ProjectId, StringComparer.OrdinalIgnoreCase) ?? false))
                        continue;
                    round.MemberIndex++;
                    if (string.Equals(leaderId, _cycleRoundLeaderId, StringComparison.OrdinalIgnoreCase))
                        detail = StreamText.CycleMemberDetail(round.MemberIndex, round.MemberCount, round.Round, round.Cap);
                    break; // bir proje en çok bir SCC'nin üyesidir
                }
                _stream.StartBuilding(e.ProjectId, e.Name, _nowMs(), detail); // building → yalnız aktif satırda görünür (tampon satırı YOK)
                SyncActiveLine();
                break;

            // [dalga görünürlüğü] Üyenin turdaki derlemesi bitti: aktif satır ondan çıkar — hâlâ derlenen en son
            // başlayana geçer ya da boşalır. Tampon satırı YAZILMAZ: ara tur sonucu yayılmaz, grubun hükmünü
            // üye sonuçları ve CycleCompletedEvent söyler.
            case CycleMemberHeldEvent e:
                _stream.FinishBuilding(e.ProjectId, _nowMs());
                SyncActiveLine();
                break;

            case ProjectSucceededEvent e:
                PushStream(StreamKind.Ok, e.ProjectId,
                    e.DepIssues is { Count: > 0 }
                        ? StreamText.BuiltDependencyIssue(ResolveName(e.ProjectId), e.DurationMs)
                        : StreamText.Built(ResolveName(e.ProjectId), e.DurationMs));
                _stream.FinishBuilding(e.ProjectId, _nowMs());
                SyncActiveLine();
                break;

            case ProjectFailedEvent e:
                PushStream(StreamKind.Fail, e.ProjectId, StreamText.Failed(ResolveName(e.ProjectId), e.Reason, e.DurationMs));
                _stream.FinishBuilding(e.ProjectId, _nowMs());
                SyncActiveLine();
                break;

            case ProjectSkippedEvent e:
                // [Task 2/cycles] Kapsam-dışı skip proje başına satır YAZMAZ — sayaç birikir, sonraki
                // PushStream'in başında tek toplu satıra flush edilir (ör. bir sonraki skip/built/completed).
                if (_currentRunMode == RunMode.Cycles && e.Reason == SkipReasons.OutOfCycleScope)
                {
                    _outOfScopeSkips++; // görüntü tamponu — PushStream'de FLUSH edilir (bkz. alanın kendi yorumu)
                    break;
                }
                PushStream(StreamKind.Skip, e.ProjectId, StreamText.Skipped(ResolveName(e.ProjectId), e.Reason));
                break;

            // [cycle rounds/Task 8] Bir SCC'nin turu başladı — grubun tek ilerleme sinyali. ProjectId LİDERİN
            // id'sidir (satır ona bağlı/tıklanabilir, ok/fail/skip satırlarıyla AYNI desen). Kind=Info: ne
            // başarı ne hata, BuildStarted satırıyla AYNI amber ▸ anlatı tonu.
            case CycleRoundStartedEvent e:
                // [Task 4] Yeni turun ilerleme takibi kurulur — sayaç 0'dan başlar, grubun İLK ProjectStartedEvent'i
                // (round-order'daki ilk üye) onu 1'e taşır. [Review fix — Finding 1] Takip lidere bağlıdır —
                // ProjectStartedEvent üyenin hangi grubun sayacını ilerlettiğini oradan bulur. Turu en son başlayan
                // grup ekrana gelir.
                _roundsInFlight[e.ProjectId] = new CycleRoundState(e.Round, e.RoundCap, e.MemberCount);
                _roundOrder.RemoveAll(id => string.Equals(id, e.ProjectId, StringComparison.OrdinalIgnoreCase));
                _roundOrder.Add(e.ProjectId);
                _cycleRoundLeaderId = e.ProjectId;
                PushStream(StreamKind.Info, e.ProjectId, StreamText.CycleRound(e.Round, e.RoundCap, e.MemberCount));
                break;

            // [Task 3/cycles] Grubun NİHAİ kararı — decision.log'un ekrandaki karşılığı. ProjectId LİDERİN
            // id'sidir (CycleRoundStartedEvent ile AYNI desen) — satır tıklanabilir kalır. Kind outcome'a göre
            // değişir: Converged yeşil, NoProgress kırmızı, CapReached amber/info (bilgi, hata DEĞİL).
            case CycleCompletedEvent e:
                PushStream(e.Outcome switch
                {
                    CycleOutcome.Converged => StreamKind.Ok,
                    CycleOutcome.NoProgress => StreamKind.Fail,
                    _ => StreamKind.Info, // CapReached
                }, e.ProjectId, StreamText.CycleCompleted(e.Outcome, e.MemberCount, e.Rounds, e.FailedCount, e.DurationMs,
                    e.CompiledCount));
                // [Build cycle derler] Grup bitti: takipten düşer. Ekrandaki grupsa turu süren en son BAŞKA grup ekrana
                // geçer; kimse kalmadıysa şerit "Building"e döner (RibbonText: cycleRound > 0 kapısı) ve düz Build'de
                // sıradan projeler devam eder. Ekranda olmayan grubun bitişi ekrandaki turu silmez.
                FinishCycleRound(e.ProjectId);
                break;

            // [spec 2026-09-18 §6.2] Satır kipe göre OnSyncCompleted'ta seçildi (sessiz Sync'te tek satır ya da hiç).
            case SyncCompletedEvent:
                if (_syncStreamLine is { } syncLine) PushStream(StreamKind.Sync, null, syncLine);
                _syncStreamLine = null;
                break;

            // [clean] Clean stream'e TEK satır düşer (ilerleme konsolda akar). Ton Sync'inkiyle aynıdır:
            // ikisi de bir koşu değil, workspace'in durumunu değiştiren bir bakım anıdır.
            case CleanCompletedEvent e:
                PushStream(StreamKind.Sync, null,
                    StreamText.CleanCompleted(e.ProjectCount, e.FoldersRemoved, e.BytesRemoved, e.LockedFileCount));
                break;

            // [optimize] Optimize da stream'e TEK satır düşer ve tonu Clean'inkiyle AYNIDIR: iki bakım işi
            // aynı kefededir, birini Info'ya almak aynı anlamı iki renkte anlatmak olurdu.
            case OptimizeCompletedEvent e:
                PushStream(StreamKind.Sync, null, StreamText.Optimize(e));
                break;

            case RunCompletedEvent e:
                if (e.Outcome == RunOutcome.Stopped)
                    PushStream(StreamKind.Info, null, StreamText.Stopped(e.Queued)); // stopped → info (parıltı YOK)
                else
                {
                    // [Task 2 review fix I-2] e.Skipped motorun KENDİ toplamıdır ve kapsam dışı pre-skip'leri de
                    // sayar (RunCoordinator.cs'in seed'i, ReadySetScheduler'ın Skipped bütçesi) — Ribbon'un
                    // c.Skipped'i (RunCounters, satır State'inden türer) bunları artık hiç saymadığı için
                    // (bkz. RunViewModel.OnProjectSkipped) ikisi aynı run için FARKLI sayı gösterirdi ("348
                    // skipped" vs "1 skipped"). _outOfScopeSkipCount BU run'ın kümülatif toplamıdır (Stream'in
                    // KENDİ `_outOfScopeSkips`'i DEĞİL — o bir görüntü tamponudur, flush'ta sıfırlanır ve run
                    // sonuna kadar TOPLAM tutmaz); "N outside cycle scope" satırı (yukarıdaki flush) AYRI kalır,
                    // burada yalnız kapanış satırının sayısı düzeltilir.
                    PushStream(StreamKind.Done, null,
                        StreamText.Completed(e.Failed, e.Succeeded, e.Skipped - _outOfScopeSkipCount, e.DepIssueCount, e.DurationMs));
                }
                // [DEĞİŞEN KURAL — Build cycle derler] Kapanış satırının ardından döngü ipucu YAZILMAZ. Eskiden iki satır
                // vardı: Build bitince "N cycle projects have pending changes — run Cycles", Clean bitince "N cycle
                // projects cleaned — run Resolve cycles before Build" — ikisi de düz Build'in döngüyü derlemediği
                // kuralına dayanıyordu. Build kirli grubu kendisi derler (ARCHITECTURE §8.1); hâlâ kirli üyeyi satırın
                // kendi etiketi ve üçgeni söyler, temizlenen grubu bir sonraki Build derler.
                _stream.EndRun();
                SyncActiveLine();
                // [Task 4] Koşu bitti — round ilerleme takibi bir sonraki run için sıfırlanır.
                ResetCycleRoundCounters();
                break;
        }
    }

    /// <summary>Tur takibinin TEK sıfırlama yeri (kopya YASAK): koşu başında ve sonunda bütün takip temizlenir — şerit
    /// tur satırını bırakır, üye-detay kapısı kapanır. Yarıda kesilen bir grubun kararı hiç gelmez; onu da bu temizler.</summary>
    private void ResetCycleRoundCounters()
    {
        _roundsInFlight.Clear();
        _roundOrder.Clear();
        _cycleRoundLeaderId = null;
    }

    /// <summary>Bir grubun kararı geldi (<see cref="CycleCompletedEvent"/>): grup takipten düşer. Ekrandaki grupsa turu
    /// süren en son başka grup ekrana geçer; kimse kalmazsa ekranda grup yoktur.</summary>
    private void FinishCycleRound(string leaderId)
    {
        if (!_roundsInFlight.Remove(leaderId)) return; // takipte olmayan grup (ör. turu hiç başlamadı): ekrana dokunmaz
        _roundOrder.RemoveAll(id => string.Equals(id, leaderId, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(_cycleRoundLeaderId, leaderId, StringComparison.OrdinalIgnoreCase))
            _cycleRoundLeaderId = _roundOrder.Count > 0 ? _roundOrder[^1] : null;
    }

    private void PushStream(StreamKind kind, string? projectId, string text)
    {
        // [Task 2/cycles] Toplu kapsam-dışı satırı BURADA flush et — sayaç ÖNCE sıfırlanır (recursion guard'ı:
        // aşağıdaki yinelenen PushStream çağrısı 0 görüp tekrar girmez). RunCompletedEvent her koşuda gelir,
        // bu yüzden sayaç asla asılı kalmaz.
        if (_outOfScopeSkips > 0)
        {
            int n = _outOfScopeSkips;
            _outOfScopeSkips = 0;
            PushStream(StreamKind.Info, null, StreamText.OutsideCycleScope(n));
        }

        bool anyFailed = Counters.Failed > 0; // done glyph/renk yeşil↔kırmızı (prototip c.failed)
        var emission = _stream.Push(isFail: kind == StreamKind.Fail, _nowMs());
        string time = Console.WallClockFormat.Of(WallClock());
        // [karakter kilitlenmesi] İlk satır açılmaz (prototip <c>prevNewest==null</c>) ve fırtına/hata zaten
        // StreamComposer tarafında instant'tır. <c>done</c> de açılmaz: koşunun kapanış satırı bir ÖZETTİR ve
        // beklemeden okunmalıdır — üstelik o an ekrandaki tek hareket olduğu için açılışı gereğinden fazla
        // dikkat çekiyordu.
        bool shouldType = _streamHadNewest && !emission.Instant && kind != StreamKind.Done;
        _streamHadNewest = true;
        bool isSelected = projectId is not null &&
            string.Equals(projectId, SelectedProjectId, StringComparison.OrdinalIgnoreCase);

        StreamEvents.Add(new StreamEventViewModel(emission, time, kind, projectId, text, anyFailed, shouldType, isSelected));
        while (StreamEvents.Count > StreamComposer.RenderSlice) StreamEvents.RemoveAt(0); // render dilimi 150 (front-trim)
        StreamEventCount = _stream.Count; // "{n} events" = tam tampon (≤260)
    }

    private void SyncActiveLine()
    {
        // Metni generation'DAN ÖNCE yaz — görünüm generation değişimini izleyip taze metinle daktilo koşar.
        ActiveLineProjectId = _stream.ActiveProjectId;
        ActiveLineText = _stream.ActiveText;
        ActiveLineGeneration = _stream.ActiveGeneration;
    }

    private string ResolveName(string projectId) =>
        FindRow(projectId)?.Name
            ?? Path.GetFileNameWithoutExtension(projectId);

    /// <summary>[Selection deseni] Seçim değişince her stream satırının <see cref="StreamEventViewModel.IsSelected"/>'ını
    /// tazeler (ProjectRow/Projects akışının eşi — TEK seçim kaynağı <see cref="SelectedProjectId"/>).</summary>
    private void PropagateSelectionToStream(string? value)
    {
        foreach (var s in StreamEvents)
            s.IsSelected = s.ProjectId is not null &&
                string.Equals(s.ProjectId, value, StringComparison.OrdinalIgnoreCase);
    }
}
