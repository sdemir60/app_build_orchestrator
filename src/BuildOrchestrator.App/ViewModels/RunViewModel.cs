using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Contracts.Ipc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>Proje listesindeki tek satır — tam kart görselleri (state renkleri, ▲/depIssue, ETA) It-4'te.</summary>
public sealed partial class ProjectRowViewModel : ObservableObject
{
    public string Id { get; }
    public string Name { get; }
    [ObservableProperty] private ProjectRowState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationMsText))]
    private long _durationMs;

    /// <summary>[Minor/Fix wave 1] XAML doğrudan <c>DurationMs</c>'e bağlanırsa current culture kullanılır
    /// (brief InvariantCulture ister) — bu yüzden görüntü için ayrı, invariant biçimli bir string.</summary>
    public string DurationMsText => DurationMs.ToString(CultureInfo.InvariantCulture);

    public ProjectRowViewModel(string id, string name, ProjectRowState state)
    {
        Id = id;
        Name = name;
        _state = state;
    }
}

public enum ProjectRowState { Started, Succeeded, Failed, Skipped }

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
    private PendingLoad? _pendingLoad; // yalnız UI thread'inde dokunulur (LoadProjectLogAsync + OnProjectLogChunk)

    private string? _currentRunId;
    private bool _sawRunStarted; // bu run denemesinde runStarted görüldü mü — runStopped'ın runCompleted'sız gelip gelmeyeceğini ayırt eder
    private long _elapsedBaseMs;
    private long? _elapsedStartMs; // run başladığında _nowMs() — null iken hiç run başlamamış/durmuş

    /// <summary>[Fix wave 1, Finding 2 regression testi] YALNIZ testler için: <see cref="OnProjectLogChunk"/>
    /// dikiş kilidinden çıkar çıkmaz (kilit ne zaman kapansa, kapandığı ANDA) senkron tetiklenir. Üretimde
    /// hep null — sıfır maliyet. Testte, kilit içinde <c>ActiveProjectId</c> atamasının GERÇEKTEN kilitle
    /// birlikte kapandığını (eskiden kilit DIŞINDAYDI — bkz. Finding 2) tek thread'de, sleep/poll OLMADAN
    /// deterministik biçimde kanıtlamak için kullanılır: kanca içinden enjekte edilen bir canlı
    /// <c>ProjectLogEvent</c>, ancak <c>ActiveProjectId</c> zaten güncellenmişse projeye düşer.</summary>
    internal Action? DebugAfterStitchLockExited;

    public ObservableCollection<ProjectRowViewModel> Projects { get; } = [];

    [ObservableProperty] private string _rootPath = "";
    [ObservableProperty] private string _configuration = "Debug";
    [ObservableProperty] private int _parallelism = Math.Max(1, Environment.ProcessorCount);
    [ObservableProperty] private long _elapsedMs;

    // [Fix wave 1, Finding 1] RelayCommand'ların CanExecuteChanged'ı YALNIZ NotifyCanExecuteChangedFor
    // (veya elle NotifyCanExecuteChanged()) ile ateşlenir — CommunityToolkit CommandManager.RequerySuggested'a
    // ABONE OLMAZ. Bu olmadan Stop/Continue butonları gerçek pencerede İLK bind sonrası ASLA yeniden
    // sorgulanmaz (StopCommand hep disabled kalır, ContinueCommand hep ölü kalır) — Kısıt 3'ü bozar.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _isRunning;

    // [Fix wave 1(It-3), Finding 3] Supervisor runStarted'dan ÖNCE planlama yapar (scan/graph/topo — 177
    // projeli OSYS'te saniyeler sürebilir) ve stop-during-planning'i AÇIKÇA destekler (ack-debt yolu,
    // RunCoordinator'da test edilmiş). IsRunning yalnız runStarted ile true olduğundan, planlama sırasında
    // Stop erişilemez kalıyordu ve çift Rebuild tıklaması runInProgress'e neden olabiliyordu. IsStarting,
    // komut gönderilir gönderilmez (runStarted/runStopped/run-bitiren ErrorEvent'e kadar) bu boşluğu kapatır.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _isStarting;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _canContinue;

    [ObservableProperty] private string? _activeProjectId; // null = run dokümanı gösteriliyor

    /// <summary>[Task 16 — It-2 devir §8] Engine process öldüğünde (<see cref="OnEngineExited"/>) kullanıcıya
    /// gösterilecek metin — sticky şerit kalıcı hata modunun PIXEL karşılığı It-4'te; burada yalnız VM-state.
    /// Bir sonraki başarılı run/Restart ile ilgisi yoktur (bilerek TEMİZLENMEZ) — kullanıcı en son ne olduğunu
    /// (engine öldü mü, hangi kodla) geriye dönük görebilsin diye kalıcıdır.</summary>
    [ObservableProperty] private string? _engineDiedMessage;

    public RunViewModel(EngineHost engine, ConsoleBatcher console, Func<string> newRunId, Func<long>? nowMs = null)
    {
        _engine = engine;
        _console = console;
        _newRunId = newRunId;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
    }

    // ---------------------------------------------------------------- komutlar

    [RelayCommand(CanExecute = nameof(CanRebuild))]
    private async Task RebuildAsync()
    {
        string runId = _newRunId();
        _currentRunId = runId;
        _sawRunStarted = false;
        ActiveProjectId = null;
        IsStarting = true; // [Fix wave 1(It-3), Finding 3] runStarted gelene kadar Stop'u erişilebilir tut
        // [Fix wave 1(It-3), Finding 1] İKİNCİ (veya sonraki) bir Rebuild'de proje log dosyaları diskte
        // sıfırdan yazılır (satır no'ları yeniden 1'den başlar). Önceki run'ın _liveLines/_projectText/_runText
        // tortusu temizlenmezse, bir sonraki kart tıklamasında dikiş filtresi (LineNumber > ThroughLineNumber)
        // eski run'ın kuyruk satırlarını da geçirir ve OrderBy(LineNumber) eski+yeni'yi birbirine karıştırır
        // (bozuk/tekrarlı "tam log"). runStarted'ı BEKLEMEDEN burada temizlenir: ProjectLogEvent marshal'sız
        // işlendiğinden yeni run'ın ilk satırları, marshal'lı runStarted UI thread'ine düşmeden ÖNCE buraya
        // varabilir — OnRunStarted'da temizlemek o satırları silerdi.
        lock (_gate)
        {
            _liveLines.Clear();
            _projectText.Clear();
            _runText.Clear();
        }
        // [Fix wave 2, Finding 1] gönderim SENKRON başarısız olursa (engine hiç başlamadı/öldü) IsStarting
        // burada geri açılmalı — aksi halde hiçbir engine event'i asla gelmeyeceğinden (ne runStarted ne
        // runStopped ne run-bitiren ErrorEvent) IsStarting kalıcı true kalır, Rebuild/Stop/Continue sonsuza
        // dek kilitli kalır (eskiden self-healing olan bir buton artık "Restart Engine" ile bile açılmıyordu).
        if (!await TrySendAsync(new StartRunCommand(runId, RunMode.Rebuild, RootPath, Configuration, Parallelism), "rebuild"))
            IsStarting = false;
    }
    private bool CanRebuild() => !IsRunning && !IsStarting;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        if (_currentRunId is null) return;
        await TrySendAsync(new StopRunCommand(_currentRunId, StopKind.Graceful), "stop");
    }
    private bool CanStop() => IsRunning || IsStarting;

    [RelayCommand(CanExecute = nameof(CanContinueRun))]
    private async Task ContinueAsync()
    {
        string runId = _newRunId();
        _currentRunId = runId;
        _sawRunStarted = false;
        ActiveProjectId = null;
        IsStarting = true; // [Fix wave 1(It-3), Finding 3] — bkz. RebuildAsync; Continue buffer'ları TEMİZLEMEZ
        // [Fix wave 2, Finding 1] — bkz. RebuildAsync'deki aynı gerekçe: gönderim senkron başarısız olursa
        // IsStarting geri açılmalı, yoksa hiçbir engine event'i gelmediğinden ContinueCommand kalıcı kilitlenir.
        if (!await TrySendAsync(new StartRunCommand(runId, RunMode.Continue, RootPath, Configuration, Parallelism), "continue"))
            IsStarting = false;
    }
    private bool CanContinueRun() => !IsRunning && !IsStarting && CanContinue;

    /// <summary>Engine hazır değilken (henüz başlamadı/çöktü) SendAsync SENKRON fırlar — UI tıklaması bu
    /// yüzden çökmemeli; hata run dokümanına düşürülür, sessizce yutulmaz. [Fix wave 2, Finding 1] Dönen
    /// <c>bool</c>, çağıranın (Rebuild/Continue) gönderim BAŞARISIZ olduğunda kendi "starting" durumunu geri
    /// açabilmesi içindir — bu metot kendi başına hiçbir bound-state'e dokunmaz.</summary>
    private async Task<bool> TrySendAsync(IpcCommand cmd, string what)
    {
        try { await _engine.SendAsync(cmd); return true; }
        catch (Exception ex) { AppendRunLine($"[hata] {what} gönderilemedi: {ex.Message}"); return false; }
    }

    // ---------------------------------------------------------------- elapsed

    /// <summary>MainWindow'un DispatcherTimer'ı UI thread'inde periyodik çağırır. VM Dispatcher/Timer TÜRÜ
    /// TAŞIMAZ — test edilebilirlik için saat kaynağı enjekte edilen <see cref="_nowMs"/> (constructor'da
    /// verilmezse <c>Environment.TickCount64</c>; testte deterministik bir <c>Func&lt;long&gt;</c> geçilir,
    /// D8: sleep/poll yok) [Minor/Fix wave 1].</summary>
    public void TickElapsed()
    {
        if (IsRunning && _elapsedStartMs is { } startMs)
            ElapsedMs = _elapsedBaseMs + (_nowMs() - startMs);
    }

    // ---------------------------------------------------------------- event → durum

    public void OnEvent(IpcEvent ev)
    {
        switch (ev)
        {
            case RunStartedEvent e: OnRunStarted(e); break;
            case ProjectStartedEvent e: EnsureRow(e.ProjectId, e.Name, ProjectRowState.Started); break;
            case ProjectLogEvent e: OnProjectLog(e); break;
            case ProjectLogChunkEvent e: OnProjectLogChunk(e); break;
            case ProjectSucceededEvent e: OnProjectDone(e.ProjectId, ProjectRowState.Succeeded, e.DurationMs); break;
            case ProjectFailedEvent e: OnProjectDone(e.ProjectId, ProjectRowState.Failed, e.DurationMs); break;
            case ProjectSkippedEvent e: EnsureRow(e.ProjectId, Path.GetFileNameWithoutExtension(e.ProjectId), ProjectRowState.Skipped).State = ProjectRowState.Skipped; break;
            case RunCompletedEvent e: OnRunCompleted(e); break;
            case RunStoppedEvent: OnRunStopped(); break;
            case ErrorEvent e: OnError(e); break;
        }
    }

    private void OnRunStarted(RunStartedEvent e)
    {
        _currentRunId = e.RunId;
        _sawRunStarted = true;
        IsRunning = true;
        IsStarting = false; // [Fix wave 1(It-3), Finding 3] planlama bitti — Stop artık IsRunning üzerinden erişilebilir
        _elapsedBaseMs = e.ElapsedMsAtStart;
        _elapsedStartMs = _nowMs();
        ElapsedMs = e.ElapsedMsAtStart;
        if (e.Mode == RunMode.Rebuild) Projects.Clear(); // Continue'da liste (önceki segmentin sonuçları) korunur
    }

    private void OnProjectDone(string projectId, ProjectRowState state, long durationMs)
    {
        var row = Projects.FirstOrDefault(p => p.Id == projectId);
        if (row is null) return; // protokole göre Started her zaman önce gelir — savunmacı no-op
        row.State = state;
        row.DurationMs = durationMs;
    }

    private ProjectRowViewModel EnsureRow(string id, string name, ProjectRowState initialState)
    {
        var existing = Projects.FirstOrDefault(p => p.Id == id);
        if (existing is not null) return existing;
        var row = new ProjectRowViewModel(id, name, initialState);
        Projects.Add(row);
        return row;
    }

    private void OnRunCompleted(RunCompletedEvent e)
    {
        ElapsedMs = e.DurationMs; // yerel Stopwatch'tan değil, engine'in kesin süresinden — clock drift yok
        IsRunning = false;
        CanContinue = e.Outcome == RunOutcome.Stopped;
        _sawRunStarted = false;
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
    }

    private void AppendRunLine(string text)
    {
        // [Fix wave 1, Finding 3] OnProjectLog ile aynı gerekçeyle Post kilit İÇİNE alındı.
        lock (_gate)
        {
            _runText.Append(text).Append('\n');
            if (ActiveProjectId is null) _console.Post(text);
        }
    }

    public string GetRunDocumentText() { lock (_gate) return _runText.ToString(); }
    public string GetProjectDocumentText(string projectId)
    {
        lock (_gate) return _projectText.TryGetValue(projectId, out var sb) ? sb.ToString() : "";
    }

    /// <summary>[Fix wave 1, Finding 3] MainWindow'un "Back" akışının kullanması gereken tohumlama metodu:
    /// run dokümanının metnini AYNI _gate kilidi altında okur VE ConsoleBatcher'daki bekleyen satırları atar.
    /// OnProjectLog'un Post'u da bu kilit altında yaptığı için (yukarı bakınız), bir satır ya TAMAMEN bu
    /// snapshot'a (ve dolayısıyla discard'a) girer ya da TAMAMEN girmez — üçüncü bir "kilit dışı post,
    /// kilit içi snapshot" aralığı YOKTUR. Kalan artık risk: ConsoleBatcher'ın kendi pump döngüsünün BU
    /// kilitten TAMAMEN bağımsız arka plan tick'i, bu metot çağrılmadan hemen önce bir satırı kanaldan çekip
    /// (henüz çalışmamış) bir <c>Dispatcher.InvokeAsync</c> kuyruğa almışsa — bu, tam tick periyodu (~50ms)
    /// yerine yalnız Dispatcher zamanlama gecikmesi kadar dar bir artık pencere; normal bir tıklamada
    /// gözlemlenmez. Tam kapanış (pump'ın tek okuyucu döngüsünden geçirme) Task 11 API değişikliği ister —
    /// It-4 için Minor olarak kayıtlı (bkz. task-12-report.md Fix wave 1).</summary>
    public string SeedRunDocument()
    {
        lock (_gate)
        {
            _console.DiscardPending();
            return _runText.ToString();
        }
    }

    /// <summary>[Fix wave 1, Finding 3] Proje kartına tıklama akışının kullanması gereken tohumlama metodu —
    /// bkz. <see cref="SeedRunDocument"/>'ın XML yorumu (aynı gerekçe, proje dokümanı için).</summary>
    public string SeedProjectDocument(string projectId)
    {
        lock (_gate)
        {
            _console.DiscardPending();
            return _projectText.TryGetValue(projectId, out var sb) ? sb.ToString() : "";
        }
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
