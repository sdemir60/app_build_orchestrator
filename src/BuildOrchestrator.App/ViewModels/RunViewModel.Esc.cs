using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [kullanıcı kararı 2026-09-29] Esc zincirinin koşu katmanının VM yüzü: zincirin girdisi (<see cref="EscRunState"/>) ve
/// durdurulamayan bir işte konsola düşen tek satır (<see cref="NoteEscCannotStop"/>). Karar SAF
/// <see cref="KeyboardShortcuts.ResolveEsc"/>'tedir; burada yalnız girdi ve etkiler durur. Durdurma sürerken tekrar basılan
/// Esc'in etkisi (hard stop) Stop komutunun kendisindedir (<c>StopAsync</c>'in <c>Stopping</c> dalı) — burada ayrı bir
/// geri bildirim yoktur.
/// </summary>
public partial class RunViewModel
{
    /// <summary>Esc zincirinin koşu katmanı için o anki durum. Faz ÖNCE sorulur: Stop zaten istendiyse (Stopping) durum
    /// <see cref="EscRunState.Stopping"/>'tir — kapı hard stop'a kadar AÇIK olsa da ikinci Esc "yeniden Stop" değil hard
    /// stop'tur. Stop'un kapısı açıksa (koşu uçuşta ya da işaretleniyor, henüz durdurulmadı)
    /// <see cref="EscRunState.Stoppable"/>; kullanıcıya görünen, durdurulamayan bir workspace işi sürüyorsa
    /// <see cref="EscRunState.Unstoppable"/>.</summary>
    internal EscRunState EscRunState =>
        Phase == AppPhase.Stopping ? EscRunState.Stopping
        : StopCommand.CanExecute(null) ? EscRunState.Stoppable
        : UnstoppableOperation is not null ? EscRunState.Unstoppable
        : EscRunState.Idle;

    /// <summary>Durdurulamayan ve kullanıcıya GÖRÜNEN workspace işinin pill etiketi (<see cref="OperationLabel"/>); yoksa
    /// <c>null</c>. Sessiz Sync görünmez — Esc kullanıcının başlatmadığı bir işi anlatmasın diye sayılmaz. Pull, pill'de
    /// zincirlendiği Sync'le tek işlemdir ve <see cref="OperationLabel.Sync"/> okunur.</summary>
    internal string? UnstoppableOperation =>
        IsMidRunLocked ? null
        : CleanBusy ? OperationLabel.DeepClean
        : OptimizeBusy ? OperationLabel.Optimize
        : CheckoutBusy ? OperationLabel.Checkout
        : PullBusy ? OperationLabel.Sync
        : SyncBusy && _syncMode.IsVisible() ? OperationLabel.Sync
        : null;

    /// <summary>Bu meşguliyet döneminde "durdurulamaz" satırı yazıldı mı — workspace boşalınca
    /// (<see cref="ResetEscNoteWhenIdle"/>) sıfırlanır, yani satır aynı işte yalnız ilk basışta düşer.</summary>
    private bool _escCannotStopNoted;

    /// <summary>Durdurulamayan bir iş sürerken Esc: nedenini konsola bir kez yazar.</summary>
    internal void NoteEscCannotStop()
    {
        if (_escCannotStopNoted || UnstoppableOperation is not { } operation) return;
        _escCannotStopNoted = true;
        AppendRunLine(EscCannotStopLine(operation));
    }

    /// <summary><see cref="OnWorkspaceBusyChanged"/>'in üçüncü tüketicisi: workspace işi kalmadıysa bir sonraki işin
    /// ilk Esc'i satırını yeniden yazar.</summary>
    private void ResetEscNoteWhenIdle()
    {
        if (!WorkspaceBusy) _escCannotStopNoted = false;
    }

    /// <summary>Konsol satırı — işin pill sözcüğü küçük harfle, konsolun diğer not satırlarıyla aynı üslupta
    /// (<c>stop requested — …</c>, <c>clean requested</c>).</summary>
    internal static string EscCannotStopLine(string operation) =>
        operation.ToLowerInvariant() + " can't be stopped — it will finish on its own";
}
