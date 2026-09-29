using BuildOrchestrator.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [design v1.23.0 §2.12 · plan U1/U3] <see cref="RunViewModel"/>'in <b>güncelleme yüzeyi</b>: kuruluma hazır teklif
/// (title bar'daki hap ve kartı onu gösterir), <c>Restart to update</c>'in kilidi ve Restart isteği.
///
/// <para><b>Güncelleme motoru henüz YOK.</b> Teklifin tek yeri <see cref="AvailableUpdate"/>'tir ve açılışta
/// <see cref="UpdateOffer.Sample"/> (placeholder) taşır — hap bu yüzden şimdilik her zaman görünür. Motor yazıldığında
/// teklifi buraya o yazar; <c>null</c> → teklif geçişinde hap girişini oynatır (kabuk).</para>
///
/// <para><b>Kilit (U3):</b> bir iş sürerken Restart kapalıdır ve kartın açıklama satırı nedeni söyler
/// (<see cref="UpdateRestartBlockedReason"/>). Karar meşguliyet bildiriminin tek noktasında
/// (<see cref="OnWorkspaceBusyChanged"/>) ve Resolve'un kendi bildiriminde yeniden sorulur, yalnız DEĞİŞTİĞİNDE
/// duyurulur.</para>
/// </summary>
public sealed partial class RunViewModel
{
    /// <summary>Kuruluma hazır güncelleme; <c>null</c> = hap yok. Şimdilik örnek kayıt — bkz. sınıf özeti.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestartToUpdateCommand))]
    private UpdateOffer? _availableUpdate = UpdateOffer.Sample;

    /// <summary>
    /// <c>Restart to update</c> neden kapalı — <c>null</c> = açık. Sıra (plan U3): (1) Clean / Optimize / Resolve /
    /// checkout / pull → görev, (2) herhangi bir Sync (sessizi dahil: kurulum onu da yarıda keserdi) → Sync, (3) koşu
    /// kilidi (<see cref="IsMidRunLocked"/> — koşu, işaretleme koreografisi, bekleyen Build isteği) → koşu. Resolve bir
    /// koşudur ama görev gibi okunur, bu yüzden koşu kilidinden ÖNCE sorulur. Metin ve sıra <see cref="UpdateText"/>'tedir.
    /// </summary>
    public string? UpdateRestartBlockedReason => UpdateText.RestartBlockedReason(
        taskRunning: CleanBusy || OptimizeBusy || IsResolvingCycles || CheckoutBusy || PullBusy,
        syncRunning: SyncBusy,
        buildRunning: IsMidRunLocked);

    /// <summary>Son duyurulan neden — bildirim yalnız değişimde gider (meşguliyet noktası sık tetiklenir).</summary>
    private string? _announcedRestartBlockedReason;

    /// <summary>[restart ekranı dikişi] Kullanıcı <c>Restart to update</c>'e bastı. Kabuk kartı kapatır; restart ekranı
    /// bu isteğe bağlanır. VM başka hiçbir şey yapmaz — motor yok, komut gitmez.</summary>
    public event EventHandler? RestartToUpdateRequested;

    [RelayCommand(CanExecute = nameof(CanRestartToUpdate))]
    private void RestartToUpdate() => RestartToUpdateRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Kurulacak bir teklif var ve hiçbir iş onu beklemiyor.</summary>
    private bool CanRestartToUpdate() => AvailableUpdate is not null && UpdateRestartBlockedReason is null;

    /// <summary>Kilidi yeniden sorar; neden değiştiyse duyurur ve komutun kapısını tazeler. Çağıranlar:
    /// <see cref="OnWorkspaceBusyChanged"/> (Sync/Clean/Optimize/checkout/pull bayrakları ve koşu kilidi) ve
    /// <c>runStarted</c>'ın Resolve bildirimi.</summary>
    private void NotifyUpdateRestartGate()
    {
        string? reason = UpdateRestartBlockedReason;
        if (string.Equals(reason, _announcedRestartBlockedReason, StringComparison.Ordinal)) return;
        _announcedRestartBlockedReason = reason;
        OnPropertyChanged(nameof(UpdateRestartBlockedReason));
        RestartToUpdateCommand.NotifyCanExecuteChanged();
    }
}
