using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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
/// </summary>
public partial class BranchPopover : UserControl
{
    // BuildApp.jsx:859 satır ölçüleri (token DEĞİL — bileşenin kendi değerleri).
    private const double RowHeight = 28;
    private const double RowGap = 8;
    private const double IconSlot = 12;

    private RunViewModel? _vm;

    /// <summary>Bir branch seçildiğinde — ActionBar popover'ı kapatır.</summary>
    public event Action? BranchPicked;

    /// <summary>[E5/T46] Popover içinde Esc — ActionBar popover'ı kapatır + odağı tetikleyici chip'e döndürür.
    /// (Popover ayrı bir HWND olduğundan pencere-seviyesi Esc zinciri buraya ULAŞMAZ; odak açılışta içeri
    /// taşındığından Esc'i popover'ın KENDİSİ yakalamalı.)</summary>
    public event Action? CloseRequested;

    public BranchPopover()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        PART_Search.TextChanged += (_, _) => RefreshRows();
        AutomationProperties.SetName(PART_Search, AccessibilityNames.BranchFilter);
        Loaded += (_, _) => RefreshRows();
    }

    // [E5/final fold — latent] Popover AÇIKKEN Branches envanteri değişirse liste bayat kalmasın: WorktreePopover
    // deseniyle CollectionChanged'e (+ Branch değişimine, seçili ✓ için) abone ol; eski VM'den çöz. Böylece açık/
    // kapalı fark etmeksizin CANLI kalır (fetch tamamlanınca gelen yeni branch listesi anında yansır).
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.Branches.CollectionChanged -= OnBranchesChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = e.NewValue as RunViewModel;
        if (_vm is not null)
        {
            _vm.Branches.CollectionChanged += OnBranchesChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
        RefreshRows();
    }

    private void OnBranchesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshRows();

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RunViewModel.Branch)) RefreshRows(); // seçili branch ✓ tazelensin
    }

    /// <summary>[E5/T46] Esc → popover'ı kapat (arama kutusundan bubble eder; TextBox Esc'i yemez). Prototip
    /// BuildApp.jsx:1313 gibi popover katmanı: Esc branch aramasını TEMİZLEMEZ, popover'ı KAPATIR.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CloseRequested?.Invoke(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    /// <summary>[D6] Popover açık mı — ActionBar, Popup.IsOpen ile iki-yönlü bağlar. false'a düşünce sorgu sıfırlanır
    /// (BuildApp.jsx:833 <c>if (!open) setQ('')</c>); true olunca satırlar tazelenir ve 140ms pop-in oynar.</summary>
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(BranchPopover),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsOpenChanged));

    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var popover = (BranchPopover)d;
        if (e.NewValue is true)
        {
            popover.RefreshRows();
            PopIn.Play(popover);
            // [E5/T47] Açılınca odak İÇERİ (ilk etkileşimli öğe = arama kutusu). Popup içeriği bu an henüz
            // realize olmamış olabilir → layout tamamlanınca odakla (Input önceliği).
            popover.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => popover.PART_Search.Focus()));
        }
        else
        {
            popover.PART_Search.Text = ""; // kapanınca arama sıfırlanır → TextChanged RefreshRows'u sürer
        }
    }

    // ---------------------------------------------------------------- test yüzeyi
    internal TextBox SearchBox => PART_Search;
    internal IReadOnlyList<BranchRef> VisibleBranches { get; private set; } = [];
    internal bool IsEmptyState => PART_Empty.Visibility == Visibility.Visible;

    private void RefreshRows()
    {
        string query = PART_Search.Text.Trim();
        var branches = _vm?.Branches ?? (IReadOnlyList<BranchRef>)[];
        var list = branches
            .Where(b => query.Length == 0 || b.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        VisibleBranches = list;

        PART_Rows.Children.Clear(); // minik non-virtualized liste (StickyRibbon chip deseni)
        foreach (var branch in list) PART_Rows.Children.Add(BuildRow(branch));

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

    private Border BuildRow(BranchRef branch)
    {
        bool selected = string.Equals(branch.Name, _vm?.Branch, StringComparison.Ordinal);
        bool active = branch.IsActive;

        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        // ikon: seçilide amber ✓, değilse branch ikonu (BuildApp.jsx:863).
        var icon = IconVisual.Make(this, selected ? "Icon.Check" : "Icon.Branch",
            selected ? "Brush.AmberText" : "Brush.TextDim", IconSlot);
        icon.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(icon);

        var name = new TextBlock
        {
            Text = branch.Name,
            Margin = new Thickness(RowGap, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = AppFonts.Mono,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        name.SetResourceReference(FontSizeProperty, "FontSize.Xs");
        name.SetResourceReference(TextBlock.ForegroundProperty, selected ? "Brush.TextPrimary" : "Brush.TextSecondary");
        panel.Children.Add(name);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(panel, 0);
        grid.Children.Add(panel);

        // aktif branch → "active" rozeti; diğerleri → mono 7-hane SHA (BuildApp.jsx:865-867).
        FrameworkElement trailing = active ? ActiveBadge() : ShaText(branch.Sha);
        trailing.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(trailing, 1);
        grid.Children.Add(trailing);

        var row = new Border
        {
            Height = RowHeight,
            Padding = new Thickness(6, 0, 6, 0),
            Cursor = Cursors.Hand,
            Child = grid,
        };
        row.SetResourceReference(Border.CornerRadiusProperty, "Radius.Sm");
        HoverBackground.Attach(row);
        row.MouseLeftButtonUp += (_, _) => Pick(branch);
        return row;
    }

    private void Pick(BranchRef branch)
    {
        BranchPicked?.Invoke();  // popover'ı kapat (BuildApp.jsx:1337 setBranchPop(false))
        _vm?.SelectBranch(branch);
    }

    private Border ActiveBadge()
    {
        var text = new TextBlock { Text = "active", Margin = new Thickness(5, 1, 5, 1) };
        text.SetResourceReference(FontSizeProperty, "FontSize.2xs");
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AmberText");
        var badge = new Border { BorderThickness = new Thickness(1), Child = text };
        badge.SetResourceReference(Border.BackgroundProperty, "Brush.AmberSoft");
        badge.SetResourceReference(Border.BorderBrushProperty, "Brush.AmberBorder");
        badge.SetResourceReference(Border.CornerRadiusProperty, "Radius.Xs");
        return badge;
    }

    private TextBlock ShaText(string sha)
    {
        var text = new TextBlock { Text = RunViewModel.Short7(sha), FontFamily = AppFonts.Mono };
        text.SetResourceReference(FontSizeProperty, "FontSize.2xs");
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextFaint");
        return text;
    }
}
