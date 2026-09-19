using BuildOrchestrator.App.Services;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [Faz 2/T7 · spec 2026-09-18 §6.1 · §6.2 · karar 11] <see cref="RunViewModel"/>'in <b>kendiliğinden Sync</b>
/// yüzeyi: HEAD izleyicisi ve pencereye dönüş <see cref="AutoSyncCoordinator"/>'da karar verir, VM ona dar bir
/// port (<see cref="IAutoSyncPort"/>) açar.
///
/// <para><b>Koordinatörün sahibi VM'dir</b> (MainWindow değil): kararın okuduğu her şey (son Sync'in HEAD'i ve
/// anı, meşguliyet, kök) VM'dedir; kök değişimi (<c>OnRootPathChanged</c>) ve meşguliyet bitişi
/// (<see cref="NotifyAutoSyncGate"/>) VM'in içinden bildirilir — kabuk sahibi olsaydı bu iki olayı
/// PropertyChanged dinleyerek dolaylı yakalamak zorunda kalırdı. Kabuğun tek katkısı UI thread'ine taşıma
/// temsilcisidir (<see cref="EnableAutoSync"/>): VM Dispatcher TÜRÜ taşımaz — motor olaylarının
/// <c>Dispatcher.InvokeAsync</c> ile taşınmasıyla AYNI bölüşüm.</para>
///
/// <para>Kabuk <see cref="EnableAutoSync"/> çağırmadıkça koordinatör YOKTUR: VM testleri izleyici kurmaz, konsola
/// "watcher unavailable" satırı düşmez.</para>
/// </summary>
public sealed partial class RunViewModel : IAutoSyncPort
{
    private AutoSyncCoordinator? _autoSync;

    /// <summary>[Task 8/9 dikişi] Koordinatör (etkinse) — koşu kesme ve git-işlemi kapısı buradan bağlanır.</summary>
    internal AutoSyncCoordinator? AutoSync => _autoSync;

    /// <summary>Kendiliğinden Sync'i açar: koordinatör kurulur ve izleyici bugünkü köke bağlanır. Kabuk bir kez
    /// çağırır; <paramref name="postToUi"/> izleyicinin thread-pool geri çağrısını UI thread'ine taşır.</summary>
    /// <param name="readHead">Test dikişi: HEAD okuyucusu (verilmezse diskten, <see cref="Core.Git.HeadReader"/>).</param>
    /// <param name="newWatcher">Test dikişi: izleyici fabrikası (verilmezse gerçek <see cref="Core.Git.HeadWatcher"/>).</param>
    internal void EnableAutoSync(Action<Action> postToUi, Func<string?, Core.Git.HeadState?>? readHead = null,
        Func<Core.Git.IHeadWatcher>? newWatcher = null)
    {
        _autoSync ??= new AutoSyncCoordinator(this, postToUi, readHead, newWatcher);
        _autoSync.Attach(RootPath);
    }

    /// <summary>Kabuk kapanıyor: izleyici bırakılır.</summary>
    internal void DisableAutoSync()
    {
        _autoSync?.Dispose();
        _autoSync = null;
    }

    /// <summary>Pencere etkinleşti (tepsiden dönüş dahil — <c>ShowFromTray</c> <c>Activate</c> çağırır).</summary>
    internal void OnWindowActivated()
    {
        RefreshGitOperation(); // [spec §6.4] dönüşte chip'in noktası ve kilitleri koordinatörsüz de tazelenir
        if (_autoSync is { } autoSync) _ = autoSync.WindowActivatedAsync();
    }

    /// <summary>Kök değişti: izleyici yeni köke taşınır (<c>OnRootPathChanged</c>).</summary>
    private void AttachAutoSync(string root) => _autoSync?.Attach(root);

    /// <summary>
    /// "Workspace meşguliyeti değişti" bildiriminin TEK noktası — Sync/Clean/Optimize/checkout/pull bayraklarının her
    /// geçişi (<see cref="NotifySyncGatedCommands"/>) ve koşu kilidinin her geçişi (<see cref="PropagateRunLock"/>)
    /// buraya iner. Koordinatör bekleyen tetiği meşguliyet bitince yeniden değerlendirir.
    /// </summary>
    private void NotifyAutoSyncGate() => _autoSync?.OnWorkspaceIdle();

    /// <summary>[spec 2026-09-18 §6.2] Dışarıdan gelen branch değişimi: checkout'un cevabıyla AYNI yol
    /// (<see cref="SyncMode.BranchChange"/> + bölümün ilk satırı), kapı <see cref="SyncSilentlyAsync"/>'inkiyle aynı.</summary>
    internal async Task<bool> SyncAfterExternalBranchChangeAsync(params IReadOnlyList<string> sectionLines) =>
        HasWorkspace && CanSync() && await SyncCoreAsync(SyncMode.BranchChange, sectionLines: sectionLines);

    // ---------------------------------------------------------------- IAutoSyncPort

    bool IAutoSyncPort.HasWorkspace => HasWorkspace;

    bool IAutoSyncPort.IsWorkspaceBusy => WorkspaceBusy || IsRunInFlight;

    (string? Branch, string? HeadSha)? IAutoSyncPort.LastSyncHead => LastSyncHead;

    bool IAutoSyncPort.LastSyncOpenedSection => LastSyncOpenedSection;

    long? IAutoSyncPort.LastSyncAtMs => LastSyncAtMs;

    long IAutoSyncPort.NowMs() => _nowMs();

    Task<bool> IAutoSyncPort.SyncSilentlyAsync(SilentSyncReason reason) => SyncSilentlyAsync(reason);

    Task<bool> IAutoSyncPort.SyncAfterExternalBranchChangeAsync(IReadOnlyList<string> sectionLines) =>
        SyncAfterExternalBranchChangeAsync(sectionLines);

    void IAutoSyncPort.AppendConsoleLine(string line) => AppendRunLine(line);

    bool IAutoSyncPort.IsRunInFlight => IsRunInFlight;

    Task IAutoSyncPort.RequestInterruptAsync() => RequestInterruptAsync();

    string? IAutoSyncPort.TakeInterruptedRunSummary() => TakeInterruptedRunSummary();

    void IAutoSyncPort.AppendStreamLine(string line) => PushStream(StreamKind.Info, null, line);

    bool IAutoSyncPort.WaitForGitOperation() => WaitForGitOperation();

    // ---------------------------------------------------------------- [T8] koşu sırasında branch değişimi

    /// <summary>Branch değişimiyle kesilen koşunun id'si — özet (<see cref="TakeInterruptedRunSummary"/>) bir kez
    /// alınınca ya da başka bir koşu başlayınca düşer.</summary>
    private string? _interruptedRunId;

    /// <summary>Bu koşuda güvenilir biten projeler (<c>ProjectSucceededEvent.Trusted</c>) — özetin "N built"i. Kesmeden
    /// sonra biten başarı motor tarafında güvenilmezdir (Trusted=false), yani sayılmaz.</summary>
    private readonly HashSet<string> _trustedBuiltIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Kesme anında sabitlenen koşu kapsamı (<see cref="InterruptScope"/>) — özetin paydası.</summary>
    private readonly HashSet<string> _interruptScopeIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bu koşunun disk log klasörü (<see cref="RunStartedEvent.LogDirectory"/>).</summary>
    private string? _runLogDirectory;

    /// <summary><c>runStarted</c>'ta koşunun özet kaydı sıfırlanır. Planlama penceresinde kesilmiş koşunun kaydı
    /// (id ve kapsam) korunur: kesme isteği aynı id'yle gitti, runStarted onun ardından gelebilir.</summary>
    private void BeginInterruptRecord(RunStartedEvent e)
    {
        _trustedBuiltIds.Clear();
        _runLogDirectory = e.LogDirectory;
        if (!string.Equals(_interruptedRunId, e.RunId, StringComparison.Ordinal)) _interruptedRunId = null;
    }

    /// <summary>Güvenilir bir başarı — <see cref="OnProjectDone"/> bildirir.</summary>
    private void NoteTrustedBuilt(string projectId) => _trustedBuiltIds.Add(projectId);

    /// <summary>
    /// [T8 fix round 1 · M3] Özetin kapsamının TEK tanımı: satırdan tetiklenen koşuda hedef satır; aksi hâlde
    /// önizlemenin derlenecek gördüğü her proje, koşullular DAHİL (<see cref="_dirtyIds"/> — koşullu proje kökü
    /// sağlıklıysa gerçekten derlenir, derlenmediyse "not built"tur); önizleme yoksa tüm satırlar.
    /// </summary>
    private IEnumerable<string> InterruptScope() =>
        RunTargetId is { } target ? [target]
        : _dirtyIds.Count > 0 ? _dirtyIds
        : Projects.Select(p => p.Id);

    /// <summary>[spec 2026-09-18 §6.1 · karar 10] Uçuştaki koşuyu branch değişimi yüzünden nazikçe keser: akışa
    /// <see cref="StreamText.InterruptedByBranchChange"/>, motora <see cref="StopKind.Interrupt"/> (yeni proje
    /// başlamaz, uçuştakiler biter ama sonuçları deftere yazılmaz). Koşu başına BİR kez; kapsam bu anda sabitlenir.
    /// <para>[fix round 1 · M5] Açılış koreografisi sırasında komut henüz gitmediyse istek Stop'taki gibi geri alınır
    /// ve kayıt yine açılır: yeni bölümün ilk satırı "0 built, N not built" der (log klasörü yok) — Build tıklamasının
    /// neden sonuçsuz kaldığı açıklanır.</para></summary>
    internal async Task RequestInterruptAsync()
    {
        if (_pendingRunId is { } pendingId)
        {
            // Başlamamış koşu: önceki koşunun başarıları ve log klasörü bu koşunun değildir.
            _trustedBuiltIds.Clear();
            _runLogDirectory = null;
            OpenInterruptRecord(pendingId);
            CancelPendingRun();
            return;
        }
        if (_currentRunId is not { } runId || _interruptedRunId == runId) return;
        // [final review M4] Kullanıcının Stop'u onaylandı (runStopped kilidi düşürdü, runCompleted bekleniyor): koşu
        // branch değişimiyle değil kullanıcının isteğiyle durdu — kesme gitmez, akış satırı ve özet yazılmaz. Tetik
        // koordinatörde bekler ve koşu bitince normal yoldan değerlendirilir.
        if (!IsMidRunLocked) return;
        OpenInterruptRecord(runId);
        PushStream(StreamKind.Info, null, StreamText.InterruptedByBranchChange);
        await SendStopAsync(runId, StopKind.Interrupt);
    }

    private void OpenInterruptRecord(string runId)
    {
        _interruptedRunId = runId;
        _interruptScopeIds.Clear();
        _interruptScopeIds.UnionWith(InterruptScope());
    }

    /// <summary>[spec 2026-09-18 §6.2] Kesilen koşu bittiyse özeti — bir kez: <c>N built</c> güvenilir başarılar,
    /// <c>M not built</c> kesme anındaki kapsamın (<see cref="InterruptScope"/>) güvenilir bitmeyen kısmı, ardından log
    /// klasörü. Koşu hâlâ uçuştaysa (<see cref="IsRunInFlight"/>) ya da kesilen koşu yoksa <c>null</c>.</summary>
    internal string? TakeInterruptedRunSummary()
    {
        if (_interruptedRunId is null || IsRunInFlight) return null;
        _interruptedRunId = null;
        int notBuilt = _interruptScopeIds.Count(id => !_trustedBuiltIds.Contains(id));
        return Core.Planning.PlanProgressLines.RunInterruptedByBranchChange(
            _trustedBuiltIds.Count, notBuilt, _runLogDirectory);
    }
}

