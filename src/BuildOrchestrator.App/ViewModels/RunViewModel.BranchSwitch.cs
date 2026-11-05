using System.Globalization;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>[worktree] Branch kartının kararı.</summary>
public enum BranchDecision
{
    /// <summary>Vazgeç — hiçbir şey gönderilmez.</summary>
    Cancel,
    /// <summary>Aynı checkout bu kopyada, kirli ağaç stash'lenerek.</summary>
    Stash,
    /// <summary>Checkout diğer kopyada (repo ↔ worktree); başarıda uygulama o kopyaya geçer.</summary>
    OtherCopy,
}

/// <summary>[worktree] Kartın konusu: hedef branch, geçişin durduğu (kirli) kopya ve kirli yol sayısı.</summary>
public sealed record PendingBranchSwitch(BranchRef Target, WorkingCopy Copy, int DirtyCount);

/// <summary>[worktree] Branch kartının metinleri — TEK kaynak.</summary>
public static class BranchSwitchText
{
    /// <summary>Kartın caps durum etiketi.</summary>
    public const string Heading = "Uncommitted changes";

    /// <summary>Stash düğmesi (birincil).</summary>
    public const string Stash = "Stash and switch";

    /// <summary>Vazgeç düğmesi.</summary>
    public const string Cancel = "Cancel";

    /// <summary>Kartın tek cümlesi: kaç dosya, hangi kopyada.</summary>
    public static string Sentence(int files, WorkingCopy copy) => files == 1
        ? $"1 file has uncommitted changes in {WorkingCopyText.Name(copy)}."
        : string.Create(CultureInfo.InvariantCulture, $"{files} files have uncommitted changes in {WorkingCopyText.Name(copy)}.");

    /// <summary>Kararın sonuç satırı: iki seçeneğin ne yaptığı.</summary>
    public static string Outcome(string branch, WorkingCopy copy) =>
        $"Stash them and switch here, or open {branch} in {WorkingCopyText.Name(Other(copy))} and leave this copy as it is.";

    /// <summary>Diğer kopya düğmesi: repo'dayken worktree (tanımsızsa kurulum), worktree'deyken repo.</summary>
    public static string OtherCopy(WorkingCopy active, bool worktreeReady) =>
        active == WorkingCopy.Worktree ? "Use repository" : worktreeReady ? "Use worktree" : "Set up worktree…";

    /// <summary>Repo ↔ worktree.</summary>
    public static WorkingCopy Other(WorkingCopy copy) =>
        copy == WorkingCopy.Worktree ? WorkingCopy.Repository : WorkingCopy.Worktree;
}

/// <summary>
/// [worktree] <see cref="RunViewModel"/>'in <b>branch kartı</b>: branch chip'inden seçilen branch'e geçiş kirli ağaçta durunca
/// (<see cref="CheckoutStatus.Dirty"/> — Settings → General "Stash and switch branches" kapalıyken; açıkken motor sessizce stash'ler ve
/// kart hiç açılmaz) kullanıcı karar verir: stash'le ve geç, diğer kopyada aç ya da vazgeç.
///
/// <para><b>Diğer kopya:</b> checkout DİĞER kökte gönderilir (<see cref="RequestCheckoutAsync"/>); cevap geçiş ya da "zaten orada" ise
/// uygulama o kopyaya geçer (<see cref="OnCheckoutCompletedAsync"/> — kopya geçişi + branch satırı tek bölümde); o kopya da kirliyse
/// uygulama yine oraya geçer ve kart orada yeniden açılır. Worktree tanımlı değilse kurulum istenir.</para>
/// </summary>
public sealed partial class RunViewModel
{
    /// <summary>Açık kartın konusu; kart kapalıyken <c>null</c>. Yazıcısı <see cref="SetPendingBranchSwitch"/>'tir.</summary>
    public PendingBranchSwitch? PendingBranchSwitch { get; private set; }

    /// <summary>Uçuştaki checkout'un hedefi ve kopyası — gönderimde kurulur, cevapta okunur (<see cref="OnCheckoutCompletedAsync"/>).</summary>
    private (BranchRef Target, WorkingCopy Copy)? _checkoutRequest;

    private void SetPendingBranchSwitch(PendingBranchSwitch? pending)
    {
        if (Equals(PendingBranchSwitch, pending)) return;
        PendingBranchSwitch = pending;
        OnPropertyChanged(nameof(PendingBranchSwitch));
    }

    /// <summary>Kartın kararı. Kapı branch chip'ininkidir (<see cref="CanSwitchBranch"/>): kapalıyken (ör. geçişin Sync'i uçuşta) karar
    /// beklemeye alınmaz ama kart da kapanmaz — kullanıcı iş bitince yeniden basar. Vazgeçmek her zaman kapatır.</summary>
    public async Task DecideBranchSwitchAsync(BranchDecision decision)
    {
        if (PendingBranchSwitch is not { } pending) return;
        if (decision == BranchDecision.Cancel) { SetPendingBranchSwitch(null); return; }
        if (!CanSwitchBranch) return;

        var other = BranchSwitchText.Other(ActiveCopy);
        if (decision == BranchDecision.OtherCopy && other == WorkingCopy.Worktree && !WorktreeReady)
        {
            SetPendingBranchSwitch(null);
            WorktreeSetupRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        SetPendingBranchSwitch(null);
        if (decision == BranchDecision.Stash) await RequestCheckoutAsync(pending.Target, ActiveCopy, stash: true);
        else await RequestCheckoutAsync(pending.Target, other, StashOnBranchSwitch);
    }

    /// <summary>Checkout isteğinin TEK gövdesi — chip seçimi ve kartın kararları buradan geçer. <paramref name="copy"/> checkout'un gideceği
    /// kopyadır (aktif ya da diğer); kökü <see cref="CopyRoot"/>'tan. Kapı ve yan etkiler bugünkü seçimin aynısıdır.</summary>
    private async Task RequestCheckoutAsync(BranchRef branch, WorkingCopy copy, bool stash)
    {
        if (!RefreshGitWritesAllowed() || !CanSwitchBranch) return; // [spec §6.4] yoklama bayat olabilir

        _checkoutRequest = (branch, copy);
        CurrentOperation = OperationLabel.Checkout;
        SetCheckoutBusy(true); // kapı GÖNDERİMDEN ÖNCE kapanır — ikinci tık ikinci bir checkout kuyruklatırdı
        ArmEngineWatchdog();
        bool sent = await TrySendAsync(
            new CheckoutBranchCommand(CopyRoot(copy), branch.Name, branch.IsRemoteTracking, stash), "checkoutBranch");
        // Gönderim SENKRON düştüyse (motor hazır değil/ölü) hiçbir cevap GELMEYECEK — kilit burada açılmazsa
        // chip kalıcı pasif kalırdı.
        if (!sent)
        {
            _checkoutRequest = null;
            SetCheckoutBusy(false);
            CurrentOperation = null; // pill "SWITCHING BRANCH"ta asılı kalmasın
        }
    }

    /// <summary>Bir kopyanın kökü: aktif kopya için <see cref="ActiveRoot"/>, diğeri için ayardaki kökü.</summary>
    private string CopyRoot(WorkingCopy copy) =>
        copy == ActiveCopy ? ActiveRoot : copy == WorkingCopy.Worktree ? WorktreeFolder : RootPath;
}
