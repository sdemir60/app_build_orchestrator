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
    internal async Task<bool> SyncAfterExternalBranchChangeAsync(params IReadOnlyList<string> sectionLines)
    {
        if (!HasWorkspace || !CanSync()) return false;
        await SyncCoreAsync(SyncMode.BranchChange, sectionLines: sectionLines);
        return true;
    }

    // ---------------------------------------------------------------- IAutoSyncPort

    bool IAutoSyncPort.HasWorkspace => HasWorkspace;

    bool IAutoSyncPort.IsWorkspaceBusy => WorkspaceBusy || IsMidRunLocked;

    (string? Branch, string? HeadSha)? IAutoSyncPort.LastSyncHead => LastSyncHead;

    long? IAutoSyncPort.LastSyncAtMs => LastSyncAtMs;

    long IAutoSyncPort.NowMs() => _nowMs();

    Task<bool> IAutoSyncPort.SyncSilentlyAsync(SilentSyncReason reason) => SyncSilentlyAsync(reason);

    Task<bool> IAutoSyncPort.SyncAfterExternalBranchChangeAsync(IReadOnlyList<string> sectionLines) =>
        SyncAfterExternalBranchChangeAsync(sectionLines);

    void IAutoSyncPort.AppendConsoleLine(string line) => AppendRunLine(line);

    bool IAutoSyncPort.IsRunInFlight => IsMidRunLocked;

    Task IAutoSyncPort.RequestInterruptAsync() => RequestInterruptAsync();

    string? IAutoSyncPort.TakeInterruptedRunSummary() => TakeInterruptedRunSummary();

    void IAutoSyncPort.AppendStreamLine(string line) => PushStream(StreamKind.Info, null, line);

    // ---------------------------------------------------------------- [T8] koşu sırasında branch değişimi

    /// <summary>Branch değişimiyle kesilen koşunun id'si — özet (<see cref="TakeInterruptedRunSummary"/>) bir kez
    /// alınınca ya da başka bir koşu başlayınca düşer.</summary>
    private string? _interruptedRunId;

    /// <summary>Bu koşunun güvenilir başarıları (<c>ProjectSucceededEvent.Trusted</c>) — özetin "N built"i. Kesmeden
    /// sonra biten başarı motor tarafında güvenilmezdir (Trusted=false), yani sayılmaz.</summary>
    private int _trustedBuiltThisRun;

    /// <summary>Bu koşunun disk log klasörü (<see cref="RunStartedEvent.LogDirectory"/>).</summary>
    private string? _runLogDirectory;

    /// <summary><c>runStarted</c>'ta koşunun özet kaydı sıfırlanır. Planlama penceresinde kesilmiş koşunun kaydı
    /// korunur: kesme isteği aynı id'yle gitti, runStarted onun ardından gelebilir.</summary>
    private void BeginInterruptRecord(RunStartedEvent e)
    {
        _trustedBuiltThisRun = 0;
        _runLogDirectory = e.LogDirectory;
        if (!string.Equals(_interruptedRunId, e.RunId, StringComparison.Ordinal)) _interruptedRunId = null;
    }

    /// <summary>[spec 2026-09-18 §6.1 · karar 10] Uçuştaki koşuyu branch değişimi yüzünden nazikçe keser: akışa
    /// <see cref="StreamText.InterruptedByBranchChange"/>, motora <see cref="StopKind.Interrupt"/> (yeni proje
    /// başlamaz, uçuştakiler biter ama sonuçları deftere yazılmaz). Koşu başına BİR kez. Açılış koreografisi
    /// sırasında komut henüz gitmediyse istek Stop'taki gibi geri alınır — derlenmiş hiçbir şey yoktur.</summary>
    internal async Task RequestInterruptAsync()
    {
        if (_pendingRunId is not null) { CancelPendingRun(); return; }
        if (_currentRunId is not { } runId || _interruptedRunId == runId) return;
        _interruptedRunId = runId;
        PushStream(StreamKind.Info, null, StreamText.InterruptedByBranchChange);
        await SendStopAsync(runId, StopKind.Interrupt);
    }

    /// <summary>[spec 2026-09-18 §6.2] Kesilen koşu bittiyse özeti — bir kez: <c>N built</c> güvenilir başarılar,
    /// <c>M not built</c> koşunun kesin kapsamının (önizlemenin derlenecekler kümesi; yoksa motorun plan boyutu)
    /// kalanı, ardından log klasörü. Koşu hâlâ uçuştaysa ya da kesilen koşu yoksa <c>null</c>.</summary>
    internal string? TakeInterruptedRunSummary()
    {
        if (_interruptedRunId is null || IsMidRunLocked) return null;
        _interruptedRunId = null;
        int scope = _willBuildIds.Count > 0 ? _willBuildIds.Count : _totalProjects ?? 0;
        return Core.Planning.PlanProgressLines.RunInterruptedByBranchChange(
            _trustedBuiltThisRun, Math.Max(0, scope - _trustedBuiltThisRun), _runLogDirectory);
    }
}
