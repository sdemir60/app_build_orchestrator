using BuildOrchestrator.App.Services;

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
    internal void EnableAutoSync(Action<Action> postToUi, Func<Core.Git.IHeadWatcher>? newWatcher = null)
    {
        _autoSync ??= new AutoSyncCoordinator(this, postToUi, newWatcher: newWatcher);
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
    /// "Workspace meşguliyeti değişti" bildiriminin TEK noktası — Sync/Clean/Optimize/checkout bayraklarının her
    /// geçişi (<see cref="NotifySyncGatedCommands"/>) ve koşu kilidinin her geçişi (<see cref="PropagateRunLock"/>)
    /// buraya iner. Koordinatör bekleyen tetiği meşguliyet bitince yeniden değerlendirir.
    /// </summary>
    private void NotifyAutoSyncGate() => _autoSync?.OnWorkspaceIdle();

    /// <summary>[spec 2026-09-18 §6.2] Dışarıdan gelen branch değişimi: checkout'un cevabıyla AYNI yol
    /// (<see cref="SyncMode.BranchChange"/> + bölümün ilk satırı), kapı <see cref="SyncSilentlyAsync"/>'inkiyle aynı.</summary>
    internal async Task<bool> SyncAfterExternalBranchChangeAsync(string sectionLine)
    {
        if (!HasWorkspace || !CanSync()) return false;
        await SyncCoreAsync(SyncMode.BranchChange, sectionLines: [sectionLine]);
        return true;
    }

    // ---------------------------------------------------------------- IAutoSyncPort

    bool IAutoSyncPort.HasWorkspace => HasWorkspace;

    bool IAutoSyncPort.IsWorkspaceBusy => WorkspaceBusy || IsMidRunLocked;

    (string? Branch, string? HeadSha)? IAutoSyncPort.LastSyncHead => LastSyncHead;

    long? IAutoSyncPort.LastSyncAtMs => LastSyncAtMs;

    long IAutoSyncPort.NowMs() => _nowMs();

    Task<bool> IAutoSyncPort.SyncSilentlyAsync(SilentSyncReason reason) => SyncSilentlyAsync(reason);

    Task<bool> IAutoSyncPort.SyncAfterExternalBranchChangeAsync(string sectionLine) =>
        SyncAfterExternalBranchChangeAsync(sectionLine);

    void IAutoSyncPort.AppendConsoleLine(string line) => AppendRunLine(line);
}
