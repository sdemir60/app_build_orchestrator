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
/// [D6/T40] Worktree popover (BuildApp.jsx:872-901). DataContext bir <see cref="RunViewModel"/>'dir. "Build in
/// worktree" switch'i <see cref="RunViewModel.UseWorktree"/>'ye bağlıdır ve aktif-olmayan branch seçiliyken
/// (<see cref="RunViewModel.IsWorktreeForced"/>) ZORUNLU/disabled'dır. Üç durum açıklaması + source satırı BİREBİR
/// design metnidir. Hedef listesi auto ("{ad} (new)") + mevcut worktree'lerdir; hover'da çöp kutusu siler
/// (<see cref="RunViewModel.DeleteWorktreeAsync"/>).
///
/// <para><b>[W2]</b> <see cref="IsOpen"/> DP'si + açılış (tazele → pop-in → odağı içeri) + Esc →
/// <c>CloseRequested</c> + VM takası <see cref="PopoverBase"/>'e taşındı (BranchPopover ile birebir kopyaydı);
/// burada yalnız WORKTREE'ye özel olan kalır (forced/disabled switch, üç durum metni, hover'da çöp kutusu).</para>
/// </summary>
public partial class WorktreePopover : PopoverBase
{
    private const double RowHeight = 28;
    private const double RowGap = 8;
    private const double IconSlot = 12;

    private bool _syncing; // switch'i programatik güncellerken Checked/Unchecked geri-tetiklemesini engeller

    public WorktreePopover()
    {
        InitializeComponent();
        PART_Switch.Checked += OnSwitchToggled;
        PART_Switch.Unchecked += OnSwitchToggled;
        AutomationProperties.SetName(PART_Switch, AccessibilityNames.WorktreeSwitch);
    }

    /// <summary>[E5/T47] Açılışta odak: ilk etkileşimli öğe = "Build in worktree" switch'i.</summary>
    protected override UIElement InitialFocusTarget => PART_Switch;

    protected override void SubscribeVm(RunViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        vm.PropertyChanged += OnVmPropertyChanged;
        vm.Worktrees.CollectionChanged += OnWorktreesChanged;
        vm.Branches.CollectionChanged += OnBranchesChanged; // [T2 fix-1 · I-G] forced'ın ikinci terimi
    }

    protected override void UnsubscribeVm(RunViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        vm.PropertyChanged -= OnVmPropertyChanged;
        vm.Worktrees.CollectionChanged -= OnWorktreesChanged;
        vm.Branches.CollectionChanged -= OnBranchesChanged;
    }

    private void OnWorktreesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    /// <summary>[T2 fix-1 · I-G] Branch envanteri <see cref="RunViewModel.IsWorktreeForced"/>'ın İKİNCİ
    /// terimidir (aktif branch oradan gelir) ama türetilmiş bir özellik olduğu için kendi PropertyChanged'ini
    /// yayınlamaz. Bu abonelik olmadan AÇIK duran bir popover, envanter geldiğinde forced/disabled durumunu
    /// tazelemiyordu — <c>ActionBar</c>'ın <c>Branches.CollectionChanged</c> aboneliğiyle AYNI desen.</summary>
    private void OnBranchesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RunViewModel.Branch):
            case nameof(RunViewModel.UseWorktree):
            case nameof(RunViewModel.WorktreeName):
                Refresh();
                break;
        }
    }

    private void OnSwitchToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing || Vm is not { } vm) return;
        if (vm.IsWorktreeForced) { _syncing = true; PART_Switch.IsChecked = true; _syncing = false; return; } // zorunlu → kapatılamaz
        vm.UseWorktree = PART_Switch.IsChecked == true;
    }

    protected override void RefreshContent() => Refresh();

    private void Refresh()
    {
        if (Vm is not { } vm) return;
        bool forced = vm.IsWorktreeForced;
        // [T2 fix-1 · C1] ETKİN değer: forced iken switch İŞARETLİ ve disabled olur (eskiden işaretsiz ve
        // disabled görünüp source satırı "working directory" diyordu — motor ise worktree açıyordu).
        bool on = vm.EffectiveUseWorktree;

        _syncing = true;
        PART_Switch.IsChecked = on;
        PART_Switch.IsEnabled = !forced; // zorunluysa disabled (BuildApp.jsx:878)
        _syncing = false;

        // Üç durum açıklaması — BİREBİR (BuildApp.jsx:880-884).
        PART_Desc.Text = forced
            ? "Different branch selected — worktree required. The committed HEAD is built; active branch and local changes stay untouched."
            : on
                ? "The committed HEAD builds in a separate worktree; local changes excluded."
                : "Off: in-place build — local changes included.";

        // source satırı (BuildApp.jsx:1157).
        PART_Source.Text = !on
            ? "working directory — local changes included"
            : string.Create(CultureInfo.InvariantCulture, $"committed HEAD ({vm.Branch}) → {vm.EffectiveWorktreeName}");

        PART_TargetSection.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on) BuildTargetRows();
    }

    private void BuildTargetRows()
    {
        if (Vm is not { } vm) return;
        PART_TargetRows.Children.Clear();

        // auto satırı: "{autoName} (new)" · note "auto" · seçili = WorktreeName==null (BuildApp.jsx:890).
        string autoName = RunViewModel.AutoWorktreeName(vm.Branch, vm.Worktrees);
        PART_TargetRows.Children.Add(TargetRow(
            string.Create(CultureInfo.InvariantCulture, $"{autoName} (new)"), "auto",
            selected: vm.WorktreeName is null, onPick: () => Choose(null), onDelete: null));

        foreach (var wt in vm.Worktrees)
        {
            string name = wt.Name;
            PART_TargetRows.Children.Add(TargetRow(
                name, wt.Branch, // not = worktree'nin branch'i (Worktree kaydında ayrı "note" yok)
                selected: string.Equals(vm.WorktreeName, name, StringComparison.Ordinal),
                onPick: () => Choose(name), onDelete: () => _ = vm.DeleteWorktreeAsync(name)));
        }
    }

    private void Choose(string? name) => Vm!.WorktreeName = name; // null = auto

    private Border TargetRow(string name, string note, bool selected, Action onPick, Action? onDelete)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var icon = IconVisual.Make(this, selected ? "Icon.Check" : "Icon.Tree",
            selected ? "Brush.AmberText" : "Brush.TextFaint", IconSlot);
        icon.VerticalAlignment = VerticalAlignment.Center;
        left.Children.Add(icon);
        var nameText = new TextBlock
        {
            Text = name, Margin = new Thickness(RowGap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            FontFamily = AppFonts.Mono, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        nameText.SetResourceReference(FontSizeProperty, "FontSize.Xs");
        nameText.SetResourceReference(TextBlock.ForegroundProperty, selected ? "Brush.TextPrimary" : "Brush.TextSecondary");
        left.Children.Add(nameText);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var noteText = new TextBlock { Text = note, VerticalAlignment = VerticalAlignment.Center };
        noteText.SetResourceReference(FontSizeProperty, "FontSize.2xs");
        noteText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextFaint");
        Grid.SetColumn(noteText, 1);
        grid.Children.Add(noteText);

        Button? trash = null;
        if (onDelete is not null)
        {
            trash = new Button { Margin = new Thickness(6, 0, 0, 0), Visibility = Visibility.Hidden, ToolTip = "Delete worktree" };
            if (TryFindResource("Ds.IconButton") is Style s) trash.Style = s;
            trash.Content = IconVisual.Make(this, "Icon.Trash", "Brush.TextSecondary", 14);
            trash.Click += (_, ev) => { ev.Handled = true; onDelete(); };
            Grid.SetColumn(trash, 2);
            grid.Children.Add(trash);
        }

        var row = new Border { Height = RowHeight, Padding = new Thickness(6, 0, 6, 0), Cursor = Cursors.Hand, Child = grid };
        row.SetResourceReference(Border.CornerRadiusProperty, "Radius.Sm");
        HoverBackground.Attach(row);
        if (trash is not null)
        {
            row.MouseEnter += (_, _) => trash.Visibility = Visibility.Visible; // hover'da çöp kutusu (BuildApp.jsx:916)
            row.MouseLeave += (_, _) => trash.Visibility = Visibility.Hidden;
        }
        row.MouseLeftButtonUp += (_, _) => onPick();
        return row;
    }
}
