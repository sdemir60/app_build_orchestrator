using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [worktree] Branch karar kartı: branch chip'inden seçilen branch'e geçiş kirli ağaçta durunca (Settings → General "Stash and switch
/// branches" kapalıyken) kullanıcıya sorar — <c>Stash and switch</c>, diğer kopyada aç (<c>Use worktree</c> / <c>Use repository</c>; worktree
/// tanımsızsa <c>Set up worktree…</c>) ya da <c>Cancel</c>. Popover iskeleti <see cref="PopoverBase"/>'tir (IsOpen, açılışta tazeleme +
/// giriş + odak içeri, Esc → <see cref="PopoverBase.CloseRequested"/>); kabuk (<see cref="ActionBar"/>) kartı
/// <see cref="RunViewModel.PendingBranchSwitch"/>'e göre açar ve dışarı tıklamayı vazgeçme sayar.
///
/// <para><b>Karar VM'dedir</b> (<see cref="RunViewModel.DecideBranchSwitchAsync"/>): kart yalnız iletir. Eylem düğmelerinin kapısı branch
/// chip'ininkidir (<see cref="RunViewModel.CanSwitchBranch"/>) — diğer kopyaya geçişin Sync'i sürerken açılan kart, iş bitene dek bekler;
/// <c>Cancel</c> her zaman etkindir.</para>
/// </summary>
public partial class BranchSwitchCard : PopoverBase
{
    public BranchSwitchCard()
    {
        InitializeComponent();
        PART_Cancel.Click += (_, _) => Decide(BranchDecision.Cancel);
        PART_OtherCopy.Click += (_, _) => Decide(BranchDecision.OtherCopy);
        PART_Stash.Click += (_, _) => Decide(BranchDecision.Stash);
    }

    // ---------------------------------------------------------------- test yüzeyi
    internal System.Windows.Controls.TextBlock Sentence => PART_Sentence;
    internal System.Windows.Controls.TextBlock Outcome => PART_Outcome;
    internal System.Windows.Controls.Button CancelButton => PART_Cancel;
    internal System.Windows.Controls.Button OtherCopyButton => PART_OtherCopy;
    internal System.Windows.Controls.Button StashButton => PART_Stash;

    private void Decide(BranchDecision decision)
    {
        if (Vm is { } vm) _ = vm.DecideBranchSwitchAsync(decision);
    }

    /// <summary>Açılışta odak <c>Cancel</c>'a gider — kartın yıkıcı olmayan seçeneği (Exit kartının kuralı).</summary>
    protected override UIElement InitialFocusTarget => PART_Cancel;

    protected override void SubscribeVm(RunViewModel vm) => vm.PropertyChanged += OnVmPropertyChanged;

    protected override void UnsubscribeVm(RunViewModel vm) => vm.PropertyChanged -= OnVmPropertyChanged;

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RunViewModel.PendingBranchSwitch) or nameof(RunViewModel.CanSwitchBranch)) RefreshContent();
    }

    /// <summary>Konuyu karta yazar: kaç dosya hangi kopyada, iki seçeneğin ne yaptığı, diğer kopya düğmesinin adı ve eylemlerin kapısı.
    /// Kart kapalıyken (konu yok) dokunulmaz.</summary>
    protected override void RefreshContent()
    {
        if (Vm is not { PendingBranchSwitch: { } pending } vm) return;
        PART_Sentence.Text = BranchSwitchText.Sentence(pending.DirtyCount, pending.Copy);
        PART_Outcome.Text = BranchSwitchText.Outcome(pending.Target.Name, pending.Copy);
        string other = BranchSwitchText.OtherCopy(pending.Copy, vm.WorktreeReady);
        PART_OtherCopy.Content = other;
        AutomationProperties.SetName(PART_OtherCopy, other);
        bool open = vm.CanSwitchBranch;
        PART_OtherCopy.IsEnabled = open;
        PART_Stash.IsEnabled = open;
    }
}
