using BuildOrchestrator.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [design v1.23.0 §2.12 · plan U1/U3] <see cref="RunViewModel"/>'in <b>güncelleme yüzeyi</b>: kuruluma hazır teklif
/// (title bar'daki hap ve kartı onu gösterir), <c>Restart to update</c>'in kilidi ve Restart isteği.
///
/// <para>Teklifin tek yeri <see cref="AvailableUpdate"/>'tir ve uygulama teklifsiz açılır (<c>null</c> = hap yok).
/// Teklifi <c>UpdateService</c> yazar (UI thread'inde) — yalnız indirilmiş, kuruluma hazır bir paket için
/// (tasarım §2.12); <c>null</c> → teklif geçişinde hap girişini oynatır (kabuk).</para>
///
/// <para><b>Kilit (U3):</b> bir iş sürerken Restart kapalıdır ve kartın açıklama satırı nedeni söyler
/// (<see cref="UpdateRestartBlockedReason"/>). Karar meşguliyet bildiriminin tek noktasında
/// (<see cref="OnWorkspaceBusyChanged"/>), Resolve'un kendi bildiriminde ve Stop'un aşaması değiştiğinde
/// (<see cref="RefreshStopStage"/>) yeniden sorulur, yalnız DEĞİŞTİĞİNDE duyurulur.</para>
/// </summary>
public sealed partial class RunViewModel
{
    /// <summary>Kuruluma hazır güncelleme. Teklifi <c>UpdateService</c> yazar (UI thread'inde); <c>null</c> = hap yok.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestartToUpdateCommand))]
    private UpdateOffer? _availableUpdate;

    /// <summary>
    /// <c>Restart to update</c> neden kapalı — <c>null</c> = açık. Sıra (plan U3): (1) Clean / Optimize / Resolve /
    /// checkout / pull → görev, (2) herhangi bir Sync (sessizi dahil: kurulum onu da yarıda keserdi) → Sync, (3) koşu
    /// kilidi (<see cref="IsMidRunLocked"/> — koşu ya da işaretleme koreografisi) → koşu; koşu cümlesi Stop'un aşamasını
    /// izler (<see cref="StopStage"/>: Stopping'de bir sonraki Esc hard stop'tur ve kart bunu söyler). Resolve bir
    /// koşudur ama görev gibi okunur, bu yüzden koşu kilidinden ÖNCE sorulur. Metin ve sıra <see cref="UpdateText"/>'tedir.
    /// Görev kovasının workspace üyeliği <see cref="NonSyncWorkspaceBusy"/>'dir (<see cref="WorkspaceBusy"/> ile tek liste).
    /// </summary>
    public string? UpdateRestartBlockedReason => UpdateText.RestartBlockedReason(
        taskRunning: NonSyncWorkspaceBusy || IsResolvingCycles,
        syncRunning: SyncBusy,
        buildRunning: IsMidRunLocked,
        stopStage: StopStage);

    /// <summary>Son duyurulan neden — bildirim yalnız değişimde gider (meşguliyet noktası sık tetiklenir).</summary>
    private string? _announcedRestartBlockedReason;

    /// <summary>Kullanıcı <c>Restart to update</c>'e bastı. VM build motoruna (Supervisor) hiçbir komut göndermez, yalnız
    /// isteği yayar; yanıtı iki abone verir: kabuk (<c>MainWindow</c>) kartı kapatır, restart ekranını oynatır ve çubuk
    /// dolunca güvenli tam çıkışı ister; <c>App.xaml.cs</c> olayı <c>UpdateService.RequestRestart</c>'a bağlar — çıkışta
    /// kurulum yeniden açmayı da söyler.</summary>
    public event EventHandler? RestartToUpdateRequested;

    /// <summary>İsteğin kapısı BURADADIR, tek yerde: kapıdan geçmeden gelen bir çağrı (doğrudan <c>Execute</c> —
    /// komut kapısını yalnız düğme ve kısayol onurlandırır) da kilitliyken ya da teklif yokken istek yaymaz.</summary>
    [RelayCommand(CanExecute = nameof(CanRestartToUpdate))]
    private void RestartToUpdate()
    {
        if (!CanRestartToUpdate()) return;
        RestartToUpdateRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Kurulacak bir teklif var ve hiçbir iş onu beklemiyor.</summary>
    private bool CanRestartToUpdate() => AvailableUpdate is not null && UpdateRestartBlockedReason is null;

    /// <summary>Kilidi yeniden sorar; neden değiştiyse duyurur ve komutun kapısını tazeler. Çağıranlar:
    /// <see cref="OnWorkspaceBusyChanged"/> (Sync/Clean/Optimize/checkout/pull bayrakları ve koşu kilidi),
    /// <c>runStarted</c>'ın Resolve bildirimi ve <see cref="RefreshStopStage"/> (Stop'un aşaması değişince).</summary>
    private void NotifyUpdateRestartGate()
    {
        string? reason = UpdateRestartBlockedReason;
        if (string.Equals(reason, _announcedRestartBlockedReason, StringComparison.Ordinal)) return;
        _announcedRestartBlockedReason = reason;
        OnPropertyChanged(nameof(UpdateRestartBlockedReason));
        RestartToUpdateCommand.NotifyCanExecuteChanged();
    }
}
