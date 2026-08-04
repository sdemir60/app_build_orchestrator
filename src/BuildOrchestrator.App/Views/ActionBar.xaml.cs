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
/// branch/worktree/Debug|Release görünür şekilde disabled; <b>perf CANLI kalır</b> (T12). Build split-button ayrıca
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
    private const double ChipContentGap = 6;    // _ds_bundle.js:166 chip gap 6
    private const double ButtonGap = 6;         // _ds_bundle.js:104 button gap 6
    private const double ChipStripGap = 8;      // BuildApp.jsx:1544 bar gap 8

    private RunViewModel? _vm;
    private bool _built;
    private bool _syncingCfg; // segment'i programatik güncellerken Checked geri-tetiklemesini engeller

    // sayaç chip'leri + değer TextBlock'ları (StickyRibbon deseni — kod-tarafı kurulur, refresh'te güncellenir)
    private ToggleButton _sigmaChip = null!, _buildingChip = null!, _succeededChip = null!, _failedChip = null!, _skippedChip = null!, _depChip = null!;
    private TextBlock _sigmaValue = null!, _buildingValue = null!, _succeededValue = null!, _failedValue = null!, _skippedValue = null!, _depValue = null!;
    private BuildingSpinner _buildingSpinner = null!;
    private Ellipse _buildingDot = null!;
    private Path _depTriangle = null!;
    private TextBlock _branchValue = null!, _worktreeValue = null!, _perfValue = null!;

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
        PART_WorktreePopover.CloseRequested += () => { PART_WorktreeChip.IsChecked = false; PART_WorktreeChip.Focus(); };
        PART_BuildMenu.ItemInvoked += () => PART_Split.IsMenuOpen = false;
        // perf momentary; [T20-b] chip artık koşan run'a setPerfMode gönderdiği için VM tarafı async —
        // gönderim hataları VM içinde run dokümanına düşer (TrySendAsync), bu yüzden fire-and-forget güvenli
        // (WorktreePopover'ın `_ = _vm.DeleteWorktreeAsync(...)` deseniyle aynı).
        PART_PerfChip.Click += (_, _) => { _ = _vm?.CyclePerfAsync(); PART_PerfChip.IsChecked = false; };
        DependencyPropertyDescriptor.FromProperty(SplitButton.IsMenuOpenProperty, typeof(SplitButton))
            .AddValueChanged(PART_Split, (_, _) => { if (PART_Split.IsMenuOpen) PART_BuildMenu.PlayPopIn(); });
    }

    // ---------------------------------------------------------------- test yüzeyi
    internal ToggleButton SigmaChip => _sigmaChip;
    internal ToggleButton BuildingChip => _buildingChip;
    internal ToggleButton SucceededChip => _succeededChip;
    internal ToggleButton FailedChip => _failedChip;
    internal ToggleButton SkippedChip => _skippedChip;
    internal ToggleButton DepChip => _depChip;
    internal ToggleButton BranchChip => PART_BranchChip;
    internal ToggleButton WorktreeChip => PART_WorktreeChip;
    internal ToggleButton PerfChip => PART_PerfChip;
    internal ItemsControl Segment => PART_Segment;
    internal Button SyncButton => PART_Sync;
    internal Button StopButton => PART_Stop;
    internal SplitButton Split => PART_Split;
    internal BuildMenu BuildMenuControl => PART_BuildMenu;
    internal BranchPopover BranchPopoverControl => PART_BranchPopover;
    internal WorktreePopover WorktreePopoverControl => PART_WorktreePopover;
    /// <summary>[A13/T4 · m6] Branch/worktree popover kabuklarının <c>Popup</c>'ı — README §2.8/BuildApp.jsx:821
    /// (<c>bottom: calc(100% + 8px)</c>) 8px boşluğunun test yüzeyi (<c>ActionBar.xaml:27,:40 VerticalOffset="-8"</c>).</summary>
    internal Popup BranchPopup => PART_BranchPopup;
    internal Popup WorktreePopup => PART_WorktreePopup;

    // ---------------------------------------------------------------- [E5/T46] Esc zinciri: popover katmanı
    /// <summary>Açık bir branch/worktree popover'ı ya da build menüsü var mı (Esc'in popover katmanı,
    /// BuildApp.jsx:1313 <c>branchPop || wtPop || buildMenu</c>).</summary>
    public bool AnyPopoverOpen =>
        PART_BranchChip.IsChecked == true || PART_WorktreeChip.IsChecked == true || PART_Split.IsMenuOpen;

    /// <summary>Açık tüm popover/menüleri kapatır (BuildApp.jsx:1313 <c>setBranchPop(false); setWtPop(false);
    /// setBuildMenu(false)</c>). Chip'lerin IsChecked'ı popup'ların IsOpen'ına iki-yönlü bağlı → false yapmak kapatır.
    /// [E5/T47] Kapanınca odak TETİKLEYİCİYE döner (açık olan chip / build split-button'a).</summary>
    public void CloseAllPopovers()
    {
        Control? trigger = PART_BranchChip.IsChecked == true ? PART_BranchChip
            : PART_WorktreeChip.IsChecked == true ? PART_WorktreeChip
            : PART_Split.IsMenuOpen ? PART_Split
            : null;
        PART_BranchChip.IsChecked = false;
        PART_WorktreeChip.IsChecked = false;
        PART_Split.IsMenuOpen = false;
        trigger?.Focus();
    }

    // ---------------------------------------------------------------- lifecycle
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_built) { RefreshAll(); return; }
        BuildCounterChips();
        BuildBranchWorktreeChips();
        BuildPerfChip();
        BuildButtons();
        _built = true;
        RefreshAll();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.Branches.CollectionChanged -= OnBranchesChanged;
            _vm.Worktrees.CollectionChanged -= OnWorktreesChanged;
        }
        _vm = e.NewValue as RunViewModel;
        // Popup içerikleri (görsel ağaç dışı) DataContext'i güvenilir MİRAS ALMAZ → açıkça bağla.
        PART_BranchPopover.DataContext = _vm;
        PART_WorktreePopover.DataContext = _vm;
        PART_BuildMenu.DataContext = _vm;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.Branches.CollectionChanged += OnBranchesChanged;
            _vm.Worktrees.CollectionChanged += OnWorktreesChanged;
        }
        RefreshAll();
    }

    private void OnBranchesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => RefreshBranchWorktree();

    /// <summary>[T2 fix-3 · round-3 bulgu 1] <c>EffectiveWorktreeName</c>'in auto-ad dalı (<c>AutoWorktreeName</c>)
    /// mevcut worktree SAYISINI sayar (<see cref="RunViewModel.Worktrees"/>'ten) — envanter I-G ile canlı
    /// doldurulduğundan (<c>ListWorktreesCommand</c>) gösterilen ad envanter gelince değişebilir
    /// (<c>main-1</c> → <c>main-2</c>). <see cref="OnBranchesChanged"/> ile BİREBİR aynı desen: bu abonelik
    /// olmadan chip bayat adı göstermeye devam ediyordu (title bar ve <c>WorktreePopover</c> zaten
    /// dinliyordu — üç yüzey iki farklı ad söylüyordu).</summary>
    private void OnWorktreesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => RefreshBranchWorktree();

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RunViewModel.Counters):
            case nameof(RunViewModel.ActiveFilter):
                RefreshChips();
                break;
            case nameof(RunViewModel.HasWorkspace):
            case nameof(RunViewModel.RootPath):
                RefreshEnabled();
                RefreshChips();
                break;
            case nameof(RunViewModel.IsRunning):
            case nameof(RunViewModel.IsStarting):
            case nameof(RunViewModel.Phase):
            case nameof(RunViewModel.CanContinue):
                RefreshEnabled();
                RefreshBuildArea();
                break;
            case nameof(RunViewModel.Branch):
            case nameof(RunViewModel.UseWorktree):
            case nameof(RunViewModel.WorktreeName):
                RefreshBranchWorktree();
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
        RefreshBranchWorktree();
        RefreshPerf();
        RefreshConfig();
        RefreshBuildArea();
        RefreshEnabled();
    }

    // ---------------------------------------------------------------- sayaç chip'leri
    private void BuildCounterChips()
    {
        _sigmaChip = AddCounterChip(IconVisual.Make(this, "Icon.Sigma", "Brush.TextDim", ChipIconSize), out _sigmaValue,
            AccessibilityNames.FilterAll, first: true);
        _sigmaChip.Click += (_, _) => { _vm?.ToggleFilter(null); _sigmaChip.IsChecked = false; }; // Σ HER ZAMAN temizler (ActiveFilter zaten null'sa ToggleFilter no-op'tur → PropertyChanged gelmez → burada zorla)

        _buildingChip = AddCounterChip(BuildingIcon(), out _buildingValue, AccessibilityNames.FilterBuilding);
        _buildingChip.Click += (_, _) => _vm?.ToggleFilter(ProjectFilter.Building);

        _succeededChip = AddCounterChip(new StatusGlyph { Status = GraphStatus.Succeeded, Size = ChipIconSize, VerticalAlignment = VerticalAlignment.Center },
            out _succeededValue, AccessibilityNames.FilterSucceeded);
        _succeededChip.Click += (_, _) => _vm?.ToggleFilter(ProjectFilter.Succeeded);

        _failedChip = AddCounterChip(new StatusGlyph { Status = GraphStatus.Failed, Size = ChipIconSize, VerticalAlignment = VerticalAlignment.Center },
            out _failedValue, AccessibilityNames.FilterFailed);
        _failedChip.Click += (_, _) => _vm?.ToggleFilter(ProjectFilter.Failed);

        _skippedChip = AddCounterChip(new StatusGlyph { Status = GraphStatus.Skipped, Size = ChipIconSize, VerticalAlignment = VerticalAlignment.Center },
            out _skippedValue, AccessibilityNames.FilterSkipped);
        _skippedChip.Click += (_, _) => _vm?.ToggleFilter(ProjectFilter.Skipped);

        _depChip = AddCounterChip(DepIcon(), out _depValue, AccessibilityNames.FilterDep);
        _depChip.Click += (_, _) => _vm?.ToggleFilter(ProjectFilter.Dep);
    }

    // [E5/T47] AYNI metin hem tooltip hem UIA-adı (ikon-yalnız chip'in görsel içeriği ekran okuyucuya bir şey
    // söylemez) — tek kaynak AccessibilityNames.
    private ToggleButton AddCounterChip(UIElement icon, out TextBlock value, string label, bool first = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(icon);
        value = CounterValue();
        content.Children.Add(value);

        var chip = new ToggleButton { Content = content, ToolTip = label, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(chip, label);
        if (TryFindResource("Ds.Chip") is Style s) chip.Style = s;
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

    private Viewbox DepIcon()
    {
        // ▲ üçgen: sayı>0 ise StatusFailText, yoksa TextFaint (BuildApp.jsx:1566). Path ref RefreshChips'te renklenir.
        _depTriangle = new Path
        {
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        IconPaint.Apply(_depTriangle, this, "Icon.AlertTri", "Brush.TextFaint");
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(_depTriangle);
        return new Viewbox { Width = ChipIconSize, Height = ChipIconSize, Stretch = Stretch.Uniform, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
    }

    private void RefreshChips()
    {
        if (!_built) return;
        var c = _vm?.Counters ?? default;
        _sigmaValue.Text = Inv(c.Total);
        _buildingValue.Text = Inv(c.Building);
        _succeededValue.Text = Inv(c.Succeeded);
        _failedValue.Text = Inv(c.Failed);
        _skippedValue.Text = Inv(c.Skipped);
        _depValue.Text = Inv(c.DepAffected);

        _buildingSpinner.Visibility = c.Building > 0 ? Visibility.Visible : Visibility.Collapsed;
        _buildingDot.Visibility = c.Building > 0 ? Visibility.Collapsed : Visibility.Visible;
        _depTriangle.SetResourceReference(Shape.StrokeProperty, c.DepAffected > 0 ? "Brush.StatusFailText" : "Brush.TextFaint");

        string? f = _vm?.ActiveFilter;
        _sigmaChip.IsChecked = false; // Σ hiç aktif olmaz (her zaman temizler)
        SetChipActive(_buildingChip, _buildingValue, f == ProjectFilter.Building);
        SetChipActive(_succeededChip, _succeededValue, f == ProjectFilter.Succeeded);
        SetChipActive(_failedChip, _failedValue, f == ProjectFilter.Failed);
        SetChipActive(_skippedChip, _skippedValue, f == ProjectFilter.Skipped);
        SetChipActive(_depChip, _depValue, f == ProjectFilter.Dep);
        _sigmaValue.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
    }

    private static void SetChipActive(ToggleButton chip, TextBlock value, bool active)
    {
        chip.IsChecked = active;
        value.SetResourceReference(TextBlock.ForegroundProperty, active ? "Brush.AmberText" : "Brush.TextPrimary");
    }

    // ---------------------------------------------------------------- branch / worktree / perf chip'leri
    private void BuildBranchWorktreeChips()
    {
        _branchValue = LabelChipContent(PART_BranchChip, "Icon.Branch", "branch", chevron: true);
        _worktreeValue = LabelChipContent(PART_WorktreeChip, "Icon.Tree", "worktree", chevron: true);
        AutomationProperties.SetName(PART_BranchChip, AccessibilityNames.BranchChip);
        AutomationProperties.SetName(PART_WorktreeChip, AccessibilityNames.WorktreeChip);
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

    private void RefreshBranchWorktree()
    {
        if (!_built) return;
        _branchValue.Text = _vm?.Branch ?? "";
        // [T2 fix-1 · C1] ETKİN değer (forced || kullanıcı toggle'ı) — ham UseWorktree DEĞİL. Aksi halde
        // zorunlu worktree ile derlenirken chip "off" gösteriyordu.
        bool on = _vm?.EffectiveUseWorktree ?? false;
        _worktreeValue.Text = on ? (_vm?.EffectiveWorktreeName ?? "") : "off";
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
        PART_Sync.Content = ButtonContent("Icon.Sync", "Sync", "Brush.TextPrimary", 24);
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
        bool stopped = _vm?.Phase == AppPhase.Stopped;
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

        // stopped → sol yarı Continue (F5 menüde oraya taşınır); aksi halde Build (BuildApp.jsx:1592-1593).
        PART_Split.PrimaryContent = ButtonContent("Icon.Play", stopped ? "Continue" : "Build", "Brush.TextOnAccent", 24);
        PART_Split.PrimaryCommand = stopped ? _vm?.ContinueCommand : _vm?.BuildCommand;
    }

    private StackPanel ButtonContent(string iconKey, string text, string iconBrushKey, double viewBox)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(IconVisual.Make(this, iconKey, iconBrushKey, LabelIconSize, viewBox));
        var tb = new TextBlock { Text = text, Margin = new Thickness(ButtonGap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(tb); // metin buton Foreground'undan miras
        return panel;
    }

    // ---------------------------------------------------------------- enable (repo yok / mid-run)
    private void RefreshEnabled()
    {
        if (!_built) return;
        bool hasWs = _vm?.HasWorkspace ?? false;
        bool midRun = _vm?.IsMidRunLocked ?? false;
        bool syncing = _vm?.Phase == AppPhase.Syncing;

        // repo yokken sayaç chip'leri de disabled (README §3.1 — prototip hatası düzeltilir).
        foreach (var chip in new[] { _sigmaChip, _buildingChip, _succeededChip, _failedChip, _skippedChip, _depChip })
            chip.IsEnabled = hasWs;

        // T12: koşarken branch/worktree/Debug|Release görünür şekilde disabled; perf CANLI.
        PART_BranchChip.IsEnabled = hasWs && !midRun;
        PART_WorktreeChip.IsEnabled = hasWs && !midRun;
        PART_Segment.IsEnabled = hasWs && !midRun;
        PART_PerfChip.IsEnabled = hasWs; // mid-run'da da canlı

        // Sync: buton IsEnabled=hasWs, komut CanExecute'i ButtonBase AND'ler → hasWs && !running.
        PART_Sync.IsEnabled = hasWs;
        // Build split-button: repo + !syncing (BuildApp.jsx:1594); primary komut running'i ayrıca kısar.
        PART_Split.IsEnabled = hasWs && !syncing;
    }

    private static string Inv(int n) => n.ToString(CultureInfo.InvariantCulture);
}
