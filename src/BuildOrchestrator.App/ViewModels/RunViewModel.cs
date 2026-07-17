using System.Collections.ObjectModel;
using System.Diagnostics;
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
    [ObservableProperty] private long _durationMs;

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
    // beklememeli [Kısıt 3]: planFailed/msbuildNotFound/noResumableRun.
    private static readonly HashSet<string> RunEndingErrorCodes =
        new(StringComparer.Ordinal) { "planFailed", "msbuildNotFound", "noResumableRun" };

    private readonly EngineHost _engine;
    private readonly ConsoleBatcher _console;
    private readonly Func<string> _newRunId;

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
    private Stopwatch? _elapsedStopwatch;

    public ObservableCollection<ProjectRowViewModel> Projects { get; } = [];

    [ObservableProperty] private string _rootPath = "";
    [ObservableProperty] private string _configuration = "Debug";
    [ObservableProperty] private int _parallelism = Math.Max(1, Environment.ProcessorCount);
    [ObservableProperty] private long _elapsedMs;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _canContinue;
    [ObservableProperty] private string? _activeProjectId; // null = run dokümanı gösteriliyor

    public RunViewModel(EngineHost engine, ConsoleBatcher console, Func<string> newRunId)
    {
        _engine = engine;
        _console = console;
        _newRunId = newRunId;
    }

    // ---------------------------------------------------------------- komutlar

    [RelayCommand(CanExecute = nameof(CanRebuild))]
    private async Task RebuildAsync()
    {
        string runId = _newRunId();
        _currentRunId = runId;
        _sawRunStarted = false;
        ActiveProjectId = null;
        await TrySendAsync(new StartRunCommand(runId, RunMode.Rebuild, RootPath, Configuration, Parallelism), "rebuild");
    }
    private bool CanRebuild() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        if (_currentRunId is null) return;
        await TrySendAsync(new StopRunCommand(_currentRunId, StopKind.Graceful), "stop");
    }
    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanContinueRun))]
    private async Task ContinueAsync()
    {
        string runId = _newRunId();
        _currentRunId = runId;
        _sawRunStarted = false;
        ActiveProjectId = null;
        await TrySendAsync(new StartRunCommand(runId, RunMode.Continue, RootPath, Configuration, Parallelism), "continue");
    }
    private bool CanContinueRun() => !IsRunning && CanContinue;

    /// <summary>Engine hazır değilken (henüz başlamadı/çöktü) SendAsync SENKRON fırlar — UI tıklaması bu
    /// yüzden çökmemeli; hata run dokümanına düşürülür, sessizce yutulmaz.</summary>
    private async Task TrySendAsync(IpcCommand cmd, string what)
    {
        try { await _engine.SendAsync(cmd); }
        catch (Exception ex) { AppendRunLine($"[hata] {what} gönderilemedi: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- elapsed

    /// <summary>MainWindow'un DispatcherTimer'ı UI thread'inde periyodik çağırır. VM Dispatcher/Timer TÜRÜ
    /// TAŞIMAZ — test edilebilirlik için saat kaynağı yalnız <see cref="Stopwatch"/> (plain BCL).</summary>
    public void TickElapsed()
    {
        if (IsRunning && _elapsedStopwatch is not null)
            ElapsedMs = _elapsedBaseMs + _elapsedStopwatch.ElapsedMilliseconds;
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
        _elapsedBaseMs = e.ElapsedMsAtStart;
        _elapsedStopwatch = Stopwatch.StartNew();
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
        CanContinue = false;
    }

    private void OnError(ErrorEvent e)
    {
        AppendRunLine($"[hata] {e.Code}: {e.Message}");
        if (!RunEndingErrorCodes.Contains(e.Code)) return; // runInProgress/logNotFound/... aktif run'ı ETKİLEMEZ
        IsRunning = false;
        CanContinue = false;
        _sawRunStarted = false;
    }

    // ---------------------------------------------------------------- konsol/log

    /// <summary>[A13.2] MainWindow bu event'i MARSHAL ETMEDEN doğrudan arka plan (IPC okuma) thread'inden
    /// çağırır — bu yüzden burada YALNIZ thread-safe işlemler yapılır: kilitli arabellek yazımı +
    /// <see cref="ConsoleBatcher.Post"/> (kilitsiz). ObservableProperty/ObservableCollection'a ASLA dokunulmaz.</summary>
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
        }
        // Post kilit DIŞINDA: ConsoleBatcher zaten kilitsiz/thread-safe, kilidi gereksiz yere uzatmaz.
        if (ActiveProjectId is null || string.Equals(ActiveProjectId, e.ProjectId, StringComparison.OrdinalIgnoreCase))
            _console.Post(e.Text);
    }

    private void AppendProjectTextLocked(string projectId, string text)
    {
        if (!_projectText.TryGetValue(projectId, out var sb))
            _projectText[projectId] = sb = new StringBuilder();
        sb.Append(text).Append('\n');
    }

    private void AppendRunLine(string text)
    {
        lock (_gate) _runText.Append(text).Append('\n');
        if (ActiveProjectId is null) _console.Post(text);
    }

    public string GetRunDocumentText() { lock (_gate) return _runText.ToString(); }
    public string GetProjectDocumentText(string projectId)
    {
        lock (_gate) return _projectText.TryGetValue(projectId, out var sb) ? sb.ToString() : "";
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
        var pending = new PendingLoad(projectId);
        _pendingLoad = pending;
        try { await _engine.SendAsync(new GetProjectLogCommand(projectId)); }
        catch (Exception ex) { AppendRunLine($"[hata] proje logu istenemedi: {ex.Message}"); }
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

        lock (_gate)
        {
            var stitched = new StringBuilder(pending.Assembly.ToString());
            if (_liveLines.TryGetValue(e.ProjectId, out var buffered))
                foreach (var line in buffered.Where(l => l.LineNumber > e.ThroughLineNumber).OrderBy(l => l.LineNumber))
                    stitched.Append(line.Text).Append('\n');
            _projectText[e.ProjectId] = stitched;
        }
        ActiveProjectId = e.ProjectId;
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
