using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [D6/T40] Branch popover (BuildApp.jsx:830-852). DataContext bir <see cref="RunViewModel"/>'dir; branch listesi
/// <see cref="RunViewModel.Branches"/>'ten okunur. Arama BÜYÜK/küçük harf duyarsız alt-dize filtreler; kapanınca
/// (<see cref="IsOpen"/>=false) sorgu SIFIRLANIR. Bir satıra tıklamak <see cref="RunViewModel.SelectBranch"/>'i
/// çağırır (K3: worktree zorlama + niyet satırı — <c>git switch</c> DEĞİL) ve <see cref="BranchPicked"/>'i yayar
/// (ActionBar popover'ı kapatır).
///
/// <para><b>[W2]</b> <see cref="IsOpen"/> DP'si + açılış (tazele → pop-in → odağı içeri) + Esc →
/// <c>CloseRequested</c> + VM takası <see cref="PopoverBase"/>'e taşındı (WorktreePopover ile birebir kopyaydı);
/// burada yalnız BRANCH'e özel olan kalır (arama filtresi, kapanışta sorgu sıfırlama, satır inşası).</para>
/// </summary>
public partial class BranchPopover : PopoverBase
{
    // BuildApp.jsx:859 satır ölçüleri (token DEĞİL — bileşenin kendi değerleri).
    private const double RowHeight = 28;
    private const double RowGap = 8;
    private const double IconSlot = 12;

    /// <summary>Bir branch seçildiğinde — ActionBar popover'ı kapatır.</summary>
    public event Action? BranchPicked;

    public BranchPopover()
    {
        InitializeComponent();
        PART_Search.TextChanged += (_, _) => RefreshRows();
        PART_Rows.AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnRowClicked), handledEventsToo: true);
        AutomationProperties.SetName(PART_Search, AccessibilityNames.BranchFilter);
    }

    /// <summary>[E5/T47] Açılışta odak: ilk etkileşimli öğe = arama kutusu.</summary>
    protected override UIElement InitialFocusTarget => PART_Search;

    // [E5/final fold — latent] Popover AÇIKKEN Branches envanteri değişirse liste bayat kalmasın: WorktreePopover
    // deseniyle CollectionChanged'e (+ Branch değişimine, seçili ✓ için) abone ol; eski VM'den çöz. Böylece açık/
    // kapalı fark etmeksizin CANLI kalır (fetch tamamlanınca gelen yeni branch listesi anında yansır).
    protected override void SubscribeVm(RunViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        vm.Branches.CollectionChanged += OnBranchesChanged;
        vm.PropertyChanged += OnVmPropertyChanged;
    }

    protected override void UnsubscribeVm(RunViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        vm.Branches.CollectionChanged -= OnBranchesChanged;
        vm.PropertyChanged -= OnVmPropertyChanged;
    }

    private void OnBranchesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshRows();

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RunViewModel.Branch)) RefreshRows(); // seçili branch ✓ tazelensin
    }

    /// <summary>Kapanınca arama sıfırlanır (BuildApp.jsx:833 <c>if (!open) setQ('')</c>) → TextChanged
    /// <see cref="RefreshRows"/>'u sürer.</summary>
    protected override void OnClosed() => PART_Search.Text = "";

    // ---------------------------------------------------------------- test yüzeyi
    internal TextBox SearchBox => PART_Search;
    internal IReadOnlyList<BranchRef> VisibleBranches { get; private set; } = [];
    internal bool IsEmptyState => PART_Empty.Visibility == Visibility.Visible;

    protected override void RefreshContent() => RefreshRows();

    private void RefreshRows()
    {
        string query = PART_Search.Text.Trim();
        var branches = Vm?.Branches ?? (IReadOnlyList<BranchRef>)[];
        var list = branches
            .Where(b => query.Length == 0 || b.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        VisibleBranches = list;

        // Seçim satırın DIŞINDA hesaplanır (BranchRow ata ağaçtan hiçbir şey çekmez). Atama O(1)'dir: hangi
        // satırın GERÇEKTEN kurulacağına sanallaştırılmış panel karar verir (yalnız görünür olanlar).
        PART_Rows.ItemsSource = list
            .Select(b => new BranchRowItem(b, string.Equals(b.Name, Vm?.Branch, StringComparison.Ordinal)))
            .ToList();

        if (list.Count == 0)
        {
            PART_Empty.Text = string.Create(CultureInfo.InvariantCulture, $"No branches match “{query}”.");
            PART_Empty.Visibility = Visibility.Visible;
        }
        else
        {
            PART_Empty.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Satır tıklaması TEK bir handler'dan geçer (satır başına abonelik YOK): sanallaştırılmış liste
    /// container'ları geri dönüştürür, satır başına kablo takmak geri dönüşümde sızıntı/çift-abonelik demekti.
    /// Kaynak öğe satırın herhangi bir çocuğu olabilir (ikon, ad, rozet) — hepsi item'ın DataContext'ini miras alır.</summary>
    private void OnRowClicked(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is BranchRowItem item) Pick(item.Branch);
    }

    private void Pick(BranchRef branch)
    {
        BranchPicked?.Invoke();  // popover'ı kapat (BuildApp.jsx:1337 setBranchPop(false))
        Vm?.SelectBranch(branch);
    }
}
