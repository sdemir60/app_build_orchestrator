using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [D6/T40+T12+T43-UI] design-v1 alt aksiyon barı (BuildApp.jsx:1543-1615). DataContext bir <see cref="RunViewModel"/>
/// (ShellRoot'tan miras). GÖRÜNÜM + kablaj; iş mantığı VM'dedir (<see cref="RunViewModel.ToggleFilter"/>/
/// <see cref="RunViewModel.SelectBranch"/>/<see cref="RunViewModel.SetConfiguration"/>/<see cref="RunViewModel.CyclePerfAsync"/>).
///
/// <para><b>Enable kuralları:</b> repo yokken (<see cref="RunViewModel.HasWorkspace"/>=false) Sync/Build + TÜM chip'ler
/// disabled (README §3.1; prototipin canlı sayaç chip'leri gözden kaçmadır). Koşarken (<see cref="RunViewModel.IsMidRunLocked"/>)
/// branch/Debug|Release görünür şekilde disabled; <b>perf CANLI kalır</b> (T12). Build split-button ayrıca
/// Syncing'de disabled (BuildApp.jsx:1594).</para>
///
/// <para><b>Motion:</b> popover/menü pop-in'i <see cref="PopIn"/> (kod-tarafı, AnimationsEnabled taze). Chip renk
/// geçişleri DS (Ds.Chip → DsTransition). Hardcoded hex/ms/px YOK.</para>
/// </summary>
public partial class ActionBar : UserControl
{
    private const double ChipIconSize = 12;     // BuildApp.jsx:1553 sayaç chip ikonları 12px
    private const double LabelIconSize = 14;    // branch/tree/sync/stop/play ikonları ~14px
    private const double ChevronSize = 12;
    private const double DotSizePx = 8;         // BuildApp.jsx:1553 boş building noktası 8px
    private const double GitDotSizePx = 6;      // [spec 2026-09-18 §6.4] branch chip'inin git-işlemi noktası 6px
    private const double ChipContentGap = 6;    // _ds_bundle.js:166 chip gap 6
    private const double ChipStripGap = 8;      // BuildApp.jsx:1544 bar gap 8

    private RunViewModel? _vm;
    private bool _built;
    private bool _syncingCfg; // segment'i programatik güncellerken Checked geri-tetiklemesini engeller

    /// <summary>Sync düğmesinin dinlenme içeriği (ikon + etiket) — iş koşarken ikonun yerine spinner konduğu
    /// için saklanır (bakım kutusunun deseni).</summary>
    private StackPanel _syncIcon = null!;

    // sayaç chip'leri + değer TextBlock'ları (StickyRibbon deseni — kod-tarafı kurulur, refresh'te güncellenir)
    private ToggleButton _sigmaChip = null!, _buildingChip = null!, _currentChip = null!, _failedChip = null!, _warnChip = null!;
    private TextBlock _behindValue = null!;
    private TextBlock _sigmaValue = null!, _buildingValue = null!, _currentValue = null!, _failedValue = null!, _warnValue = null!;
    private BuildingSpinner _buildingSpinner = null!;
    private Ellipse _buildingDot = null!;
    /// <summary>[spec 2026-09-18 §6.4] Branch chip'indeki amber nokta — yarıda bir git işlemi varken görünür.</summary>
    private Ellipse _gitOperationDot = null!;
    private Path _warnTriangle = null!;
    private TextBlock _branchValue = null!, _perfValue = null!;

    public ActionBar()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;

        // XAML öğelerine bağlı statik kablaj (InitializeComponent sonrası hazır).
        PART_CfgDebug.Checked += OnConfigChecked;
        PART_CfgRelease.Checked += OnConfigChecked;
        PART_BranchPopover.BranchPicked += () => PART_BranchChip.IsChecked = false; // seçince popover kapanır
        // [E5/T46] Esc popover içinde → kapat + odağı tetikleyici chip'e döndür (return-to-trigger).
        PART_BranchPopover.CloseRequested += () => { PART_BranchChip.IsChecked = false; PART_BranchChip.Focus(); };
        PART_BuildMenu.ItemInvoked += () => PART_Split.IsMenuOpen = false;
        // Açık bir popover'ın chip'ine basmak onu KAPATIR (BuildApp.jsx:2399/:2404 `set…(!…)`); WPF'in
        // StaysOpen=False capture yolu tek başına bırakılırsa aynı jest onu yeniden açardı. Kapı tek yerde.
        PopoverToggle.Bind(PART_BranchChip, PART_BranchPopup);
        // perf momentary; [T20-b] chip artık koşan run'a setPerfMode gönderdiği için VM tarafı async —
        // gönderim hataları VM içinde run dokümanına düşer (TrySendAsync), bu yüzden fire-and-forget güvenli.
        PART_PerfChip.Click += (_, _) => { _ = _vm?.CyclePerfAsync(); PART_PerfChip.IsChecked = false; };
        DependencyPropertyDescriptor.FromProperty(SplitButton.IsMenuOpenProperty, typeof(SplitButton))
            .AddValueChanged(PART_Split, (_, _) => { if (PART_Split.IsMenuOpen) PART_BuildMenu.PlayPopIn(); });
        // [design v1.17.0 §9 fix round 1 · I-2] Sync koşarken disabled'dır — kendi IsMouseOver'ı asla true
        // olmaz (WPF disabled öğeleri hit-test'ten dışlar). ActionBar.xaml'in onu saran HER ZAMAN etkin
        // Border'ı gerçek hover sinyalini taşır (bkz. DsChrome.IsHoverProxyProperty'nin XML doc'u).
        DsChrome.WireHoverProxy((Border)PART_Sync.Parent, PART_Sync);
    }

    // ---------------------------------------------------------------- test yüzeyi
    internal ToggleButton SigmaChip => _sigmaChip;
    internal ToggleButton BuildingChip => _buildingChip;
    internal ToggleButton CurrentChip => _currentChip;
    internal ToggleButton FailedChip => _failedChip;
    /// <summary>[design v1.11.0 §2.7-4] Birleşik uyarı chip'i (döngü ∪ dep-issue) — eski ⚠ cycle ve ▲ dep
    /// chip'lerinin yerini alır.</summary>
    internal ToggleButton WarnChip => _warnChip;
    internal ToggleButton BranchChip => PART_BranchChip;
    internal Button BehindChip => PART_BehindChip;
    /// <summary>[spec 2026-09-18 §6.4] Branch chip'inin amber git-işlemi noktası.</summary>
    internal Ellipse GitOperationDot => _gitOperationDot;
    internal ToggleButton PerfChip => PART_PerfChip;
    internal ItemsControl Segment => PART_Segment;
    /// <summary>[design v1.11.0 §2.7-5a] Branch chip'inin solundaki mono workspace etiketi.</summary>
    internal TextBlock WorkspaceLabel => PART_Workspace;
    internal Button SyncButton => PART_Sync;
    internal MaintenanceBox MaintenanceBoxControl => PART_Maintenance;
    internal Button StopButton => PART_Stop;
    internal SplitButton Split => PART_Split;
    internal BuildMenu BuildMenuControl => PART_BuildMenu;
    internal BranchPopover BranchPopoverControl => PART_BranchPopover;
    /// <summary>[A13/T4 · m6] Branch popover kabuğunun <c>Popup</c>'ı — README §2.8/BuildApp.jsx:821
    /// (<c>bottom: calc(100% + 8px)</c>) 8px boşluğunun test yüzeyi (<c>ActionBar.xaml VerticalOffset="-8"</c>).</summary>
    internal Popup BranchPopup => PART_BranchPopup;

    // ---------------------------------------------------------------- [E5/T46] Esc zinciri: popover katmanı
    /// <summary>Açık bir branch popover'ı ya da build menüsü var mı (Esc'in popover katmanı,
    /// BuildApp.jsx:1313 <c>branchPop || buildMenu</c>).</summary>
    public bool AnyPopoverOpen =>
        PART_BranchChip.IsChecked == true || PART_Split.IsMenuOpen;

    /// <summary>Açık tüm popover/menüleri kapatır (BuildApp.jsx:1313 <c>setBranchPop(false);
    /// setBuildMenu(false)</c>). Chip'in IsChecked'ı popup'ın IsOpen'ına iki-yönlü bağlı → false yapmak kapatır.
    /// [E5/T47] Kapanınca odak TETİKLEYİCİYE döner (açık olan chip / build split-button'a).</summary>
    public void CloseAllPopovers()
    {
        Control? trigger = PART_BranchChip.IsChecked == true ? PART_BranchChip
            : PART_Split.IsMenuOpen ? PART_Split
            : null;
        PART_BranchChip.IsChecked = false;
        PART_Split.IsMenuOpen = false;
        trigger?.Focus();
    }

    // ---------------------------------------------------------------- lifecycle
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_built) { RefreshAll(); return; }
        BuildCounterChips();
        BuildBranchChips();
        BuildPerfChip();
        BuildButtons();
        _built = true;
        RefreshAll();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Chip değeri yalnız vm.Branch'i okur ve o PropertyChanged yayınlar — envanter aboneliği gerekmez.
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.PullRepositoryCommand.CanExecuteChanged -= OnPullGateChanged;
        }
        _vm = e.NewValue as RunViewModel;
        // Popup içerikleri (görsel ağaç dışı) DataContext'i güvenilir MİRAS ALMAZ → açıkça bağla.
        PART_BranchPopover.DataContext = _vm;
        PART_BuildMenu.DataContext = _vm;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            // [T9 fix round 1 · M4] Behind chip'inin kapısı pull komutunun kapısıdır — her geçişi buradan gelir.
            _vm.PullRepositoryCommand.CanExecuteChanged += OnPullGateChanged;
        }
        RefreshAll();
    }

    private void OnPullGateChanged(object? sender, EventArgs e) => RefreshBehindGate();

    /// <summary>[T9 fix round 1 · M4] Behind chip'inin tıklanabilirliği = pull komutunun kapısı (CanPullRepository:
    /// koşu, workspace işi, motor, git kilidi) — bar kapıyı yeniden türetmez.</summary>
    private void RefreshBehindGate()
    {
        if (_built) PART_BehindChip.IsEnabled = _vm?.PullRepositoryCommand.CanExecute(null) ?? false;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RunViewModel.Counters):
            case nameof(RunViewModel.ActiveFilters):
                RefreshChips();
                break;
            case nameof(RunViewModel.HasWorkspace):
            case nameof(RunViewModel.RootPath):
                RefreshEnabled();
                RefreshChips();
                RefreshWorkspaceLabel();
                break;
            case nameof(RunViewModel.IsRunning):
            case nameof(RunViewModel.IsStarting):
            case nameof(RunViewModel.Phase):
                RefreshEnabled();
                RefreshBuildArea();
                break;
            // Koşan Sync'in göstergesi bir KOMUT değil bir DURUM okur (bakım kutusunun deseni).
            case nameof(RunViewModel.SyncBusy):
                RefreshSyncBusy();
                break;
            // [spec 2026-09-18 §6.3] Branch chip'inin kapısı: VM tüm girdilerinin değişimini (koşu, repo, motor,
            // meşgul yüzeyler, uçuştaki checkout) TEK bu bildirimle duyurur.
            case nameof(RunViewModel.CanSwitchBranch):
                RefreshEnabled();
                break;
            case nameof(RunViewModel.Branch):
                RefreshBranch();
                RefreshBehindChip();   // [v1.16.0] chip'in tooltip'i branch adını söyler
                break;
            case nameof(RunViewModel.Behind):
            case nameof(RunViewModel.CanShowBehind):
                RefreshBehindChip();
                break;
            // [spec 2026-09-18 §6.4] Yarıdaki git işlemi: nokta + tooltip; behind chip'inin kilidi ve tooltip'i
            // (RefreshEnabled → RefreshBehindChip). Branch chip'inin kapısı CanSwitchBranch bildirimiyle gelir.
            case nameof(RunViewModel.GitOperationTooltip):
                RefreshGitOperation();
                RefreshEnabled();
                break;
            case nameof(RunViewModel.Configuration):
                RefreshConfig();
                break;
            case nameof(RunViewModel.PerfMode):
                RefreshPerf();
                break;
        }
    }

    private void RefreshAll()
    {
        if (!_built) return;
        RefreshChips();
        RefreshWorkspaceLabel();
        RefreshBranch();
        RefreshGitOperation();
        RefreshPerf();
        RefreshConfig();
        RefreshBuildArea();
        RefreshEnabled();
        RefreshSyncBusy(); // DataContext sonradan gelirse uçuştaki Sync yine boyanır
    }

    // ---------------------------------------------------------------- sayaç chip'leri
    private void BuildCounterChips()
    {
        // [design v1.17.0 §9 fix round 1 · I-3] Σ ikonu artık hover'a KENDİ (chip'in Foreground'undan AYRI)
        // kanalıyla tepki verir: DsChrome.IconForeground, Ds.Bar.Chip'in nötr-hover MultiTrigger'ında
        // rest=text-dim → hover=text-primary olarak sürülür. Chip'in KENDİ Foreground'u (rest'te text-secondary)
        // bağlanmadı — Σ'nin ikonu tasarımda BİR TIK DAHA SOLUKTUR (bkz. DsChrome.IconForegroundProperty'nin
        // XML doc'u); doğrudan Foreground bağı bu rest farkını KAYBEDERdi.
        _sigmaChip = AddCounterChip(chip => IconVisual.BoundToIconForeground(chip, "Icon.Sigma", ChipIconSize),
            out _sigmaValue, AccessibilityNames.FilterAll, first: true);
        _sigmaChip.Click += (_, _) => { _vm?.ToggleFilter(null); _sigmaChip.IsChecked = false; }; // Σ HER ZAMAN temizler (ActiveFilter zaten null'sa ToggleFilter no-op'tur → PropertyChanged gelmez → burada zorla)

        _buildingChip = AddCounterChip(_ => BuildingIcon(), out _buildingValue, AccessibilityNames.FilterBuilding);
        _buildingChip.Click += (_, _) => _vm?.ToggleFilter(ProjectFilter.Building);

        // [design v1.20.0 §2.7 · §1.4] İki DURUM chip'i: güncel ✓ · bozuk ✗ — satırın kendi glyph'leri.
        // [DEĞİŞEN KURAL] Eskiden koşu sonucu chip'leriydi (succeeded ✓ · failed ✗ · skipped —); "atlanmak" bir
        // durum değildir ve — yalnız run-story yüzeylerinin glyph'idir, bu yüzden o chip kalktı.
        // [DEĞİŞEN KURAL — kullanıcı kararı 2026-09-20] Aralarında bir üçüncüsü vardı: ○ (kesikli daire)
        // "To build". O da kalktı — listenin kendisi zaten derlenecek satırı karar etiketiyle ve gri glyph'iyle
        // söylüyordu. Gri kova modelde durur (RunCounters.Stale / ProjectFilter.Stale), barda karşılığı YOKTUR.
        _currentChip = AddStateChip(VisualStatus.Current, out _currentValue, AccessibilityNames.FilterCurrent, ProjectFilter.Current);
        _failedChip = AddStateChip(VisualStatus.Failed, out _failedValue, AccessibilityNames.FilterFailed, ProjectFilter.Failed);

        // [design v1.11.0 §2.7-4] Son chip İSTİSNAİ durumu anlatır ve YALNIZ listede karşılığı varken görünür —
        // boş/gri hâliyle barda durması sinyali zayıflatıyordu (v1.5.2 kararı).
        // [DEĞİŞEN KURAL] Eskiden burada İKİ chip vardı: turuncu ⚠ (cycle) ve kırmızı ▲ (dep-affected).
        // v1.11.0 turuncuyu UI'dan çıkardı ve döngü ile dep-issue'yu TEK amber uyarı üçgeninde birleştirdi;
        // iki ayrı filtre iki ayrı renk ima ediyordu. Chip artık tek ve amberdir.
        _warnChip = AddCounterChip(_ => WarnIcon(), out _warnValue, AccessibilityNames.FilterWarn);
        _warnChip.Click += (_, _) => _vm?.ToggleFilter(ProjectFilter.Warn);
    }

    /// <summary>[design v1.20.0 §2.7] Durum chip'i: satırın glyph'i + rozet; tık o durumun filtresini açıp kapar.</summary>
    private ToggleButton AddStateChip(VisualStatus shown, out TextBlock value, string label, string filter)
    {
        var chip = AddCounterChip(_ => new StatusGlyph { Status = shown, Size = ChipIconSize, VerticalAlignment = VerticalAlignment.Center },
            out value, label);
        chip.Click += (_, _) => _vm?.ToggleFilter(filter);
        return chip;
    }

    // [E5/T47] AYNI metin hem tooltip hem UIA-adı (ikon-yalnız chip'in görsel içeriği ekran okuyucuya bir şey
    // söylemez) — tek kaynak AccessibilityNames.
    // [design v1.17.0 §9 fix round 1 · I-3] İkon artık ÖNCEDEN kurulup PARAMETRE olarak gelmez — bir FACTORY
    // alır ve chip'i ÖNCE kurup SONRA çağırır, çünkü Σ'nin ikonu (IconVisual.BoundToIconForeground) chip'in
    // KENDİSİNE bağlanmak zorundadır.
    private ToggleButton AddCounterChip(Func<ToggleButton, UIElement> iconFactory, out TextBlock value, string label, bool first = false)
    {
        var chip = new ToggleButton { ToolTip = label, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(chip, label);
        if (TryFindResource("Ds.Bar.Chip") is Style s) chip.Style = s;

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(iconFactory(chip));
        value = CounterValue();
        content.Children.Add(value);
        chip.Content = content;

        if (!first) chip.Margin = new Thickness(ChipStripGap, 0, 0, 0); // bar gap 8 (ilk chip HARİÇ)
        PART_CounterChips.Children.Add(chip);
        return chip;
    }

    private static TextBlock CounterValue()
    {
        var tb = new TextBlock
        {
            Margin = new Thickness(ChipContentGap, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = AppFonts.Mono,
        };
        Typography.SetNumeralAlignment(tb, FontNumeralAlignment.Tabular);
        tb.SetResourceReference(FontSizeProperty, "FontSize.Xs");
        return tb;
    }

    private Grid BuildingIcon()
    {
        // building>0 → spinner (amber); boşken 8px gri nokta (BuildApp.jsx:1553 neutral-600 = Brush.DotClean).
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        _buildingSpinner = new BuildingSpinner { Size = ChipIconSize, VerticalAlignment = VerticalAlignment.Center };
        _buildingDot = new Ellipse { Width = DotSizePx, Height = DotSizePx, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        _buildingDot.SetResourceReference(Shape.FillProperty, "Brush.DotClean");
        grid.Children.Add(_buildingDot);
        grid.Children.Add(_buildingSpinner);
        return grid;
    }

    private Viewbox WarnIcon()
    {
        // [design v1.11.0 §2.7-4] ⚠ üçgen — satırdaki uyarı üçgeniyle AYNI çizim ve AYNI renk (amber).
        // Chip zaten yalnız sayı>0 iken görünür, bu yüzden ikinci bir "boş" tonu yoktur.
        _warnTriangle = new Path
        {
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        IconPaint.Apply(_warnTriangle, this, "Icon.AlertTri", "Brush.AmberText");
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(_warnTriangle);
        return new Viewbox { Width = ChipIconSize, Height = ChipIconSize, Stretch = Stretch.Uniform, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
    }

    private void RefreshChips()
    {
        if (!_built) return;
        var c = _vm?.Counters ?? default;
        _sigmaValue.Text = Inv(c.Total);
        _buildingValue.Text = Inv(c.Building);
        // [design v1.20.0 §2.7] DURUM kovaları — koşu tablosu (Succeeded/Failed/Skipped) şeridindir.
        _currentValue.Text = Inv(c.Current);
        _failedValue.Text = Inv(c.Broken);
        _warnValue.Text = Inv(c.Warn);

        // İstisnai chip: sayı 0 ise chip HİÇ YOKTUR (gri/boş hâli taşınmaz).
        _warnChip.Visibility = c.Warn > 0 ? Visibility.Visible : Visibility.Collapsed;

        _buildingSpinner.Visibility = c.Building > 0 ? Visibility.Visible : Visibility.Collapsed;
        _buildingDot.Visibility = c.Building > 0 ? Visibility.Collapsed : Visibility.Visible;

        var f = _vm?.ActiveFilters ?? ProjectFilter.None;
        _sigmaChip.IsChecked = false; // Σ hiç aktif olmaz (her zaman temizler)
        SetChipActive(_buildingChip, _buildingValue, ProjectFilter.Building, f);
        SetChipActive(_currentChip, _currentValue, ProjectFilter.Current, f);
        SetChipActive(_failedChip, _failedValue, ProjectFilter.Failed, f);
        SetChipActive(_warnChip, _warnValue, ProjectFilter.Warn, f);
        _sigmaValue.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
    }

    /// <summary>[design v1.11.0 §2.7-4] "Aktif çip KENDİ statü renginde yanar" — hangi renk olduğunu
    /// <see cref="ProjectFilter.ActiveBrushKey"/> söyler (bar kendi eşlemesini KURMAZ).</summary>
    private static void SetChipActive(ToggleButton chip, TextBlock value, string filter, IReadOnlySet<string> active)
    {
        bool on = active.Contains(filter);
        chip.IsChecked = on;
        value.SetResourceReference(TextBlock.ForegroundProperty,
            on ? ProjectFilter.ActiveBrushKey(filter) : "Brush.TextPrimary");
    }

    // ---------------------------------------------------------------- branch / behind / perf chip'leri
    private void BuildBranchChips()
    {
        _branchValue = LabelChipContent(PART_BranchChip, "Icon.Branch", "branch", chevron: true);
        BuildGitOperationDot();
        BuildBehindChip();
        AutomationProperties.SetName(PART_BranchChip, AccessibilityNames.BranchChip);
        // [spec 2026-09-18 §6.4] Git kilidinde iki chip de pasiftir ve nedeni tooltip'lerindedir — WPF pasif bir
        // öğenin tooltip'ini varsayılan olarak saklar.
        ToolTipService.SetShowOnDisabled(PART_BranchChip, true);
        ToolTipService.SetShowOnDisabled(PART_BehindChip, true);
    }

    /// <summary>
    /// [spec 2026-09-18 §6.4 · karar 22] Branch chip'inin içeriğine eklenen amber nokta: çalışma ağacında yarıda bir git
    /// işlemi varken görünür. Renk mevcut <c>Brush.Amber</c> token'ı (yeni renk yok); boyu <see cref="GitDotSizePx"/>.
    /// </summary>
    private void BuildGitOperationDot()
    {
        _gitOperationDot = new Ellipse
        {
            Width = GitDotSizePx,
            Height = GitDotSizePx,
            Margin = new Thickness(ChipContentGap, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _gitOperationDot.SetResourceReference(Shape.FillProperty, "Brush.Amber");
        ((StackPanel)PART_BranchChip.Content).Children.Add(_gitOperationDot);
    }

    /// <summary>[spec §6.4] Noktanın görünürlüğü ve branch chip'inin tooltip'i — ikisi de VM'in tek kararından
    /// (<see cref="RunViewModel.GitOperationTooltip"/>): işlem yoksa nokta yok, tooltip yok.</summary>
    private void RefreshGitOperation()
    {
        if (!_built) return;
        string? tooltip = _vm?.GitOperationTooltip;
        _gitOperationDot.Visibility = tooltip is null ? Visibility.Collapsed : Visibility.Visible;
        PART_BranchChip.ToolTip = tooltip;
    }

    /// <summary>
    /// [design v1.16.0 §2.7-6a] <c>N behind</c> chip'i: ikon + mono sayı + <c>behind</c>. Sayaç chip'leriyle
    /// aynı ölçüler, ama NÖTR: amber yok, çünkü bu bir statü değil bir DAVETTİR (tıkla ve ilerlet).
    /// </summary>
    private void BuildBehindChip()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(IconVisual.BoundToForeground(PART_BehindChip, "Icon.ArrowDownToLine", ChipIconSize, 24));
        _behindValue = new TextBlock
        {
            Margin = new Thickness(ChipContentGap, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = AppFonts.Mono,
        };
        _behindValue.SetBinding(TextBlock.ForegroundProperty,
            new Binding(nameof(Control.Foreground)) { Source = PART_BehindChip });
        content.Children.Add(_behindValue);
        content.Children.Add(ChipLabel("behind"));
        PART_BehindChip.Content = content;
        AutomationProperties.SetName(PART_BehindChip, AccessibilityNames.BehindChip);
        PART_BehindChip.Click += (_, _) => _vm?.PullRepositoryCommand.Execute(null);
    }

    /// <summary>
    /// Chip'in görünürlüğü, sayısı ve tooltip'i. <b>Görünme kuralı motorun olgusudur:</b> sayı biliniyor
    /// (fetch başarılı) ve sıfırdan büyük. Çevrimdışıyken sayı bilinmez ⇒ chip HİÇ
    /// çizilmez — uydurma bir sayı göstermektense susmak doğrudur.
    /// </summary>
    private void RefreshBehindChip()
    {
        if (!_built) return;
        bool show = _vm?.CanShowBehind ?? false;
        PART_BehindChip.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        int behind = _vm!.Behind ?? 0;
        _behindValue.Text = Inv(behind);
        AutomationProperties.SetName(PART_BehindChip, Inv(behind) + " behind");
        // [spec 2026-09-18 §6.4] Git kilidinde chip pasiftir; tooltip daveti değil kilidin nedenini söyler.
        PART_BehindChip.ToolTip = _vm.GitOperationTooltip ?? InteractionText.BehindChipTooltip(behind, _vm.Branch);
    }

    private void BuildPerfChip()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(ChipLabel("perf"));
        _perfValue = ChipMonoValue(PART_PerfChip);
        content.Children.Add(_perfValue);
        PART_PerfChip.Content = content;
        AutomationProperties.SetName(PART_PerfChip, AccessibilityNames.PerfChip);
    }

    private TextBlock LabelChipContent(ToggleButton chip, string iconKey, string label, bool chevron)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(BoundChipIcon(chip, iconKey, LabelIconSize, 24));
        content.Children.Add(ChipLabel(label));
        var value = ChipMonoValue(chip);
        content.Children.Add(value);
        if (chevron) content.Children.Add(BoundChipIcon(chip, "Icon.Chevron", ChevronSize, 16));
        chip.Content = content;
        return value;
    }

    // Chip içeriği (ikon/değer/chevron) chip'in ANİMASYONLU Foreground'unu izler (aktif → amber; SplitButton chevron deseni).
    // [T2 fix-1 · I-B] Gövde IconVisual.BoundToForeground'a taşındı — ShellRoot'un filtre chip'i ikinci çağıran
    // oldu (kopya YASAK). Burada yalnız bu barın chip-arası boşluğu (Margin) kalır.
    private static Viewbox BoundChipIcon(ToggleButton chip, string iconKey, double size, double viewBox)
    {
        var icon = IconVisual.BoundToForeground(chip, iconKey, size, viewBox);
        icon.Margin = new Thickness(ChipContentGap, 0, 0, 0);
        return icon;
    }

    private static TextBlock ChipLabel(string text)
    {
        var tb = new TextBlock { Text = text, Margin = new Thickness(ChipContentGap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        return tb; // renk chip Foreground'undan miras (text-secondary → amber)
    }

    private static TextBlock ChipMonoValue(ToggleButton chip)
    {
        var tb = new TextBlock { Margin = new Thickness(ChipContentGap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontFamily = AppFonts.Mono };
        tb.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Control.Foreground)) { Source = chip });
        return tb;
    }

    /// <summary>[design v1.11.0 §2.7-5a] Workspace etiketi: kökün klasör adı (karar SAF
    /// <see cref="TitleBarContext.RepositoryName"/>'de — burada YALNIZ uygulanır), tooltip kökün kendisi.
    /// Ad YOKSA öğe <c>Collapsed</c> olur: prototipte etiket <c>{workspace &amp;&amp; …}</c> ile koşulludur ve
    /// boş bir metin bırakmak sağ marjını yine de ödetirdi (branch chip'i kayardı).</summary>
    private void RefreshWorkspaceLabel()
    {
        if (!_built) return;
        string name = TitleBarContext.RepositoryName(_vm?.RootPath ?? "");
        PART_Workspace.Text = name;
        PART_Workspace.ToolTip = _vm?.RootPath;
        PART_Workspace.Visibility = name.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshBranch()
    {
        if (!_built) return;
        _branchValue.Text = _vm?.Branch ?? "";
    }

    private void RefreshPerf()
    {
        if (!_built) return;
        _perfValue.Text = _vm?.PerfMode ?? "";
    }

    // ---------------------------------------------------------------- Debug | Release
    private void RefreshConfig()
    {
        if (!_built) return;
        _syncingCfg = true;
        PART_CfgDebug.IsChecked = _vm?.Configuration == "Debug";
        PART_CfgRelease.IsChecked = _vm?.Configuration == "Release";
        _syncingCfg = false;
    }

    private void OnConfigChecked(object sender, RoutedEventArgs e)
    {
        if (_syncingCfg || _vm is null) return;
        if (ReferenceEquals(sender, PART_CfgDebug)) _vm.SetConfiguration("Debug");
        else if (ReferenceEquals(sender, PART_CfgRelease)) _vm.SetConfiguration("Release");
    }

    // ---------------------------------------------------------------- Sync / Stop / Build split-button
    private void BuildButtons()
    {
        // [design v1.17.0 §9 "Alt barda tek hover dili"] Sync ikonu artık SABİT bir fırça değil, düğmenin
        // ANİMASYONLU Foreground'unu izler (IconVisual.BoundToForeground) — nötr hover'da metin VE ikon
        // BİRLİKTE text-primary'ye geçer. Rest değeri (Ds.Bar.Button.Secondary.Sm'in REST Foreground'u da
        // TextPrimary'dir) DEĞİŞMEZ — yalnız mekanizma sabitten bağlıya döner.
        _syncIcon = new StackPanel { Orientation = Orientation.Horizontal };
        _syncIcon.Children.Add(IconVisual.BoundToForeground(PART_Sync, "Icon.Sync", LabelIconSize, 24));
        _syncIcon.Children.Add(new TextBlock { Text = "Sync", Margin = new Thickness(IconVisual.LabelGap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        PART_Sync.Content = _syncIcon;
        // [Stopping] Stop'un İÇERİĞİ artık duruma bağlı (Stop / Stopping…) — tek yazıcısı RefreshBuildArea'dır.
        // UIA adı burada ve SABİT kalır: buton kimliği değişmiyor, yalnız durumu değişiyor.
        AutomationProperties.SetName(PART_Sync, AccessibilityNames.SyncButton);
        AutomationProperties.SetName(PART_Stop, AccessibilityNames.StopButton);
    }

    private void RefreshBuildArea()
    {
        if (!_built) return;
        // Kilit penceresinin TAMAMINDA (running VEYA planlama/starting) Stop göster — StopCommand da o pencerede
        // etkindir (CanStop = IsRunning || IsStarting). Aksi halde split-button (Build/Continue).
        bool locked = _vm?.IsMidRunLocked ?? false;
        PART_Stop.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        PART_Split.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        if (locked)
        {
            // [Stopping] Kilit SÜRERKEN Stop'un iki hâli var: istenmeden önce "Stop", istendikten sonra
            // "Stopping…". Pasifleşmeyi bu metot YAZMAZ — buton Command'ına bağlı olduğundan IsEnabled
            // StopCommand.CanExecute'tan (faz kapısı) gelir; iki ayrı yerden yazılan bir enable hâli olmaz.
            PART_Stop.Content = ButtonContent("Icon.Stop",
                _vm?.Phase == AppPhase.Stopping ? "Stopping…" : "Stop", "Brush.StatusFailText", 24);
            return;
        }

        // [B4] Birincil aksiyon HER fazda Build. Eskiden stopped'ta Continue'ya dönüşürdü; o yüzey kaldırıldı —
        // Stop'tan sonra Build baştan koşar (öldürülenler yeniden derlenir, bitenler "up to date" atlanır).
        PART_Split.PrimaryContent = ButtonContent("Icon.Play", "Build", "Brush.TextOnAccent", 24);
        PART_Split.PrimaryCommand = _vm?.BuildCommand;
    }

    private StackPanel ButtonContent(string iconKey, string text, string iconBrushKey, double viewBox)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(IconVisual.Make(this, iconKey, iconBrushKey, LabelIconSize, viewBox));
        var tb = new TextBlock { Text = text, Margin = new Thickness(IconVisual.LabelGap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(tb); // metin buton Foreground'undan miras
        return panel;
    }

    /// <summary>
    /// [kullanıcı kararı 2026-09-12 · tasarımdan BİLİNÇLİ sapma] Sync koşarken düğmesi bakım kutusundaki
    /// Clean ile AYNI dili konuşur: amber-soft zemin, ikonun yerinde ikonla AYNI boyda amber spinner. Etiket
    /// ("Sync") yerinde kalır — düğme etiketlidir, kimliğini kaybetmemeli.
    ///
    /// <para><b>Sapmanın kaydı:</b> prototip Sync düğmesine spinner KOYMAZ (<c>BuildApp.jsx:2612</c> — yalnız
    /// <c>disabled</c>) ve koşan işi yalnız şeridin işlem pill'i anlatır. Kullanıcı iki yüzeyin aynı dili
    /// konuşmasını istedi: aynı bardaki iki eş iş, biri dönerken öteki durgun görünüyordu. Pill'in anlatısı
    /// DEĞİŞMEDİ, bu ona EK bir göstergedir.</para>
    ///
    /// <para>Komut kapısına DOKUNULMAZ — düğme uçuşta zaten pasiftir; değişen yalnız boyamadır. Üç şey birlikte
    /// gider: zemin (<c>Ds.IconButton.Toggle</c>'ın <c>IsChecked</c> tetikleyicisiyle AYNI token), içerik
    /// (<see cref="BuildingSpinner"/> — kendi stili amber boyar, azaltılmış harekette döndürmez) ve opaklık
    /// (<c>Ds.Button.Base</c> pasifi 0.45'e söndürür, koşan iş sönük görünmemeli). Çağrı idempotenttir.</para>
    /// </summary>
    private void RefreshSyncBusy()
    {
        if (!_built) return;
        bool busy = _vm?.SyncBusy == true;
        bool spinning = _syncIcon.Children[0] is BuildingSpinner;
        if (busy == spinning) return;

        if (busy)
        {
            _syncIcon.Children.RemoveAt(0);
            _syncIcon.Children.Insert(0, new BuildingSpinner { Size = LabelIconSize, VerticalAlignment = VerticalAlignment.Center });
            // [design v1.17.0 §9] Amber-soft yüzey artık Ds.Bar.Button.Secondary.Sm'in IsActive tetikleyicisinden
            // gelir (DsChrome.IsActive) — bar'ın tek hover diliyle AYNI mekanizma (MaintenanceBox.SetBusy'nin
            // deseni), manuel AnimatedBackground ataması YAPILMAZ.
            DsChrome.SetIsActive(PART_Sync, true);
            PART_Sync.Opacity = 1;
        }
        else
        {
            _syncIcon.Children.RemoveAt(0);
            _syncIcon.Children.Insert(0, IconVisual.BoundToForeground(PART_Sync, "Icon.Sync", LabelIconSize, 24));
            DsChrome.SetIsActive(PART_Sync, false);
            PART_Sync.ClearValue(OpacityProperty);
        }
    }

    // ---------------------------------------------------------------- enable (repo yok / mid-run)
    private void RefreshEnabled()
    {
        if (!_built) return;
        bool hasWs = _vm?.HasWorkspace ?? false;
        bool midRun = _vm?.IsMidRunLocked ?? false;
        bool syncing = _vm?.Phase == AppPhase.Syncing;

        // repo yokken sayaç chip'leri de disabled (README §3.1 — prototip hatası düzeltilir).
        foreach (var chip in new[] { _sigmaChip, _buildingChip, _currentChip, _failedChip, _warnChip })
            chip.IsEnabled = hasWs;

        // T12: koşarken branch/Debug|Release görünür şekilde disabled; perf CANLI. [spec 2026-09-18 §6.3] Branch
        // chip'i artık checkout eder: kapısı VM'in TEK predicate'idir (koşu + Sync/Clean/Optimize + motor + uçuştaki checkout).
        PART_BranchChip.IsEnabled = _vm?.CanSwitchBranch ?? false;
        PART_Segment.IsEnabled = hasWs && !midRun;
        PART_PerfChip.IsEnabled = hasWs; // mid-run'da da canlı
        // [design v1.16.0 §2.7-6a] Chip koşu/bakım görevi sürerken diğer bar kontrolleriyle AYNI kilitte.
        RefreshBehindGate(); // [T9 fix round 1 · M4] komutun kapısı; geçişleri CanExecuteChanged aboneliği de duyurur
        RefreshBehindChip();

        // Sync: buton IsEnabled=hasWs, komut CanExecute'i ButtonBase AND'ler → hasWs && !running.
        PART_Sync.IsEnabled = hasWs;
        // Build split-button: repo + !syncing (BuildApp.jsx:1594); primary komut running'i ayrıca kısar.
        PART_Split.IsEnabled = hasWs && !syncing;
    }

    private static string Inv(int n) => n.ToString(CultureInfo.InvariantCulture);
}
