using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [D2/T38+T39+T70] design-v1 sticky şerit (BuildApp.jsx:778-812). DataContext bir <see cref="RunViewModel"/>'dir
/// (ShellRoot'tan miras). Şerit yalnız GÖRÜNÜM: faz metnini/rengini/glyph'ini <see cref="RibbonText"/>'ten alır,
/// building/failed chip'lerini VM.Projects'ten kurar ve 2px global progress'i (dolgu rengi + genişlik geçişi +
/// belirsiz mod) sürer. Mantık kontrolde KOPYALANMAZ.
///
/// <para><b>Motion (bağlayıcı sözleşme):</b> tüm animasyonlar kod-tarafı (<see cref="MotionTokens"/>) — süre/eğri
/// ve <c>AnimationsEnabled</c> BAŞLATMA ANINDA taze okunur; template-trigger Storyboard İMKANSIZ. Determinate
/// genişlik geçişi <c>Duration.Base</c> + <c>KeySpline.EaseStandard</c> (prototip <c>transition: width
/// var(--duration-base) var(--ease-standard)</c>); belirsiz mod (<c>ds-progress-indet</c>, _ds_bundle.js:493-495)
/// 1.4s <c>KeySpline.EaseInOut</c> ile TranslateX -110%→320%, 30fps, YALNIZ <see cref="AppPhase.Syncing"/>.
/// İndikatör dolgusu per-instance brush (A13.2).</para>
///
/// <para><b>Chip stratejisi (A13.2):</b> building (≤4) ve failed (≤3 + "+N more") şeritleri KÜÇÜK ve
/// virtualization DIŞIdır; membership DEĞİŞTİĞİNDE (id-imzası) yeniden kurulur — canlı elapsed/ETA tick'inde
/// (ElapsedMs/EtaMs) DEĞİL. Bu, "koleksiyon reset YASAK" (virtualized listeler için) kuralını ihlal etmez:
/// panel minik, non-virtualized ve seçim şeritte tutulmaz (kartta tutulur).</para>
/// </summary>
public partial class StickyRibbon : UserControl
{
    // design-v1 kaynak sabitleri (inline magic number YASAK — ProjectRow/StatusGlyph deseni).
    private const double RibbonChipHeight = 20;      // BuildApp.jsx:786 chip height 20
    private const double ChipIconSize = 10;          // BuildApp.jsx:785 building spinner / failed glyph 10px
    private const double PhaseGlyphSize = 13;        // BuildApp.jsx:781 StatusGlyph size 13
    private const double FailureGlyphSize = 13;      // BuildApp.jsx:793
    private const int MaxBuildingChips = 4;          // BuildApp.jsx:784 building.slice(0,4)
    private const int MaxFailedChips = 3;            // BuildApp.jsx:797 failed.slice(0,3)
    private const double RibbonChipGap = 4;          // BuildApp.jsx:783/801 flex gap:4

    // [D2 review fix, Finding 5] glyph→faz-metni gap — glyph görünürken 10 (BuildApp.jsx content row gap:10),
    // glyph collapsed olunca 0 (metin ilk flex item olur, leading gap yok). RefreshText'in iki dalı da yazar.
    private static readonly Thickness PhaseTextMarginWithGlyph = new(10, 0, 0, 0);
    private static readonly Thickness PhaseTextMarginNoGlyph = new(0);

    // Belirsiz (indeterminate) sweep — _ds_bundle.js:493-495 (ds-progress-indet 1.4s ease-in-out; width 35%;
    // translateX -110%→320%). Süre/oran DS'ten birebir; token DEĞİL (bileşenin kendi ölçüsü) → kaynak satırıyla yazılır.
    private const double IndeterminateSweepMs = 1400;
    private const double IndeterminateWidthFraction = 0.35;
    private const double IndeterminateFromFactor = -1.10;
    private const double IndeterminateToFactor = 3.20;
    private const int DecorativeFrameRate = 30;      // dekoratif sonsuz sweep — tam kare hızı gereksiz (feasibility §3.4)

    private readonly SolidColorBrush _indicatorBrush = new(Colors.Transparent); // per-instance (A13.2)
    private RunViewModel? _vm;
    private bool _isIndeterminate;
    private double _lastFraction; // determinate hedef (0..1) — resize'da yeniden uygulanır
    private string? _lastBuildingSig;
    private string? _lastFailedSig;
    private AppPhase? _lastAnnouncedPhase; // [E5/T47] live-region: yalnız faz DEĞİŞİMİNDE duyur (elapsed tick'te değil)
    /// <summary>[W2] Provider + <c>MotionSettings</c> seam'i + subscribe-once kablajı TEK yerde
    /// (<see cref="Controls.MotionGate"/>) — latch'siz kip (ProjectRow ile aynı).</summary>
    private readonly Controls.MotionGate _motion;

    /// <summary>[ProjectRow deseni · D8] Motion sinyalinin TAZE okunduğu kapı — headless'ta <c>App.Motion</c> null
    /// (AnimationsEnabled=false); testler gerçek bir sweep saatini sürebilmek için bunu <c>() =&gt; true</c> ile enjekte eder.</summary>
    public Func<bool> AnimationsEnabledProvider
    {
        get => _motion.AnimationsEnabledProvider;
        set => _motion.AnimationsEnabledProvider = value;
    }

    /// <summary>[ProjectRow deseni] AnimationsEnabledChanged aboneliği; null ise <see cref="App.Motion"/>.</summary>
    public BuildOrchestrator.App.Services.IMotionSettings? MotionSettings
    {
        get => _motion.MotionSettings;
        set => _motion.MotionSettings = value;
    }

    public StickyRibbon()
    {
        _motion = new Controls.MotionGate(this);
        InitializeComponent();
        PART_ProgressIndicator.Background = _indicatorBrush;
        DataContextChanged += OnDataContextChanged;
        PART_ProgressTrack.SizeChanged += OnTrackSizeChanged;
        _motion.Changed += OnAnimationsEnabledChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ---------------------------------------------------------------- test yüzeyi
    internal FrameworkElement ContentRow => PART_ContentRow;
    internal Border ProgressTrack => PART_ProgressTrack;
    internal Border ProgressIndicator => PART_ProgressIndicator;
    internal TranslateTransform IndicatorTranslate => PART_IndicatorTranslate;
    internal StatusGlyph PhaseGlyph => PART_PhaseGlyph;
    internal TextBlock PhaseText => PART_PhaseText;
    internal bool IsIndeterminate => _isIndeterminate;
    internal IReadOnlyList<ToggleButton> BuildingChips { get; private set; } = [];
    internal TextBlock? BuildingOverflow { get; private set; }
    internal IReadOnlyList<ToggleButton> FailureChips { get; private set; } = [];
    internal ToggleButton? FailureMoreChip { get; private set; }
    internal StackPanel FailureCluster => PART_FailureCluster; // [6b fold] testler "N failed"/"dependency-affected" metnini buradan pinler

    // ---------------------------------------------------------------- lifecycle
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // [W2] Motion aboneliği MotionGate'te (ctor'da kablolandığı için bu handler'dan ÖNCE koşar — eski sıra birebir).
        // [E5/temizlik — L2 M2] Unloaded'da VM aboneliği bırakıldığından (leak fix), reload olursa (DataContext
        // değişmeden) burada geri kurulur — idempotent (ProjectRow subscribe-once deseni).
        if (_vm is { } vm) SubscribeVm(vm);
        RefreshAll();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // [W2] Motion aboneliğini MotionGate bırakır (bu handler'dan ÖNCE).
        // [E5/temizlik — L2 M2] LEAK FIX: OnUnloaded VM PropertyChanged/CollectionChanged aboneliğini de BIRAKIR
        // (önceden yalnız motion bırakılıyordu → şerit unload olsa bile VM'e asılı kalıyordu). E3 unsubscribe deseni.
        if (_vm is { } vm) UnsubscribeVm(vm);
        StopIndeterminate(); // clock serbest — sweep unload'da bırakılır
    }

    private void OnAnimationsEnabledChanged(object? sender, EventArgs e)
    {
        if (_isIndeterminate) ApplyIndeterminate(); // sweep başlat/durdur (taze sinyal)
        else RefreshProgress();                     // determinate genişlik: animate vs snap
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) UnsubscribeVm(_vm);
        _vm = e.NewValue as RunViewModel;
        _lastBuildingSig = _lastFailedSig = null; // yeni VM → chip imzalarını sıfırla (ilk kurulumda yeniden kur)
        if (_vm is not null) SubscribeVm(_vm);
        RefreshAll();
    }

    // [E5 fold] VM aboneliğinin TEK giriş/çıkış kapısı (idempotent -= sonra += — çift-abonelik birikmez).
    private void SubscribeVm(RunViewModel vm)
    {
        vm.PropertyChanged -= OnVmPropertyChanged;
        vm.PropertyChanged += OnVmPropertyChanged;
        vm.Projects.CollectionChanged -= OnProjectsChanged;
        vm.Projects.CollectionChanged += OnProjectsChanged;
    }

    private void UnsubscribeVm(RunViewModel vm)
    {
        vm.PropertyChanged -= OnVmPropertyChanged;
        vm.Projects.CollectionChanged -= OnProjectsChanged;
    }

    private void OnProjectsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildChipsIfChanged();

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RunViewModel.Phase):
            case nameof(RunViewModel.AllClean):
            case nameof(RunViewModel.WillBuildCount):
            case nameof(RunViewModel.FinishedOfWillBuild):
            case nameof(RunViewModel.HasWorkspace):
            case nameof(RunViewModel.RootPath):
            // [E2/T37+T10] Engine-died kalıcı hata modu ve Sync-failed KIRMIZI metni faz-metnini EZER (RibbonText
            // öncelik sırası) → değiştiklerinde metni yeniden kur (Restart engine butonu görünürlüğü de RefreshText'te).
            case nameof(RunViewModel.EngineDiedMessage):
            case nameof(RunViewModel.SyncErrorMessage):
                RefreshText();
                RefreshProgress();
                AnnouncePhaseIfChanged(); // [E5/T47] faz değişimini ekran okuyucuya duyur (live region)
                break;
            // [D2 review fix, Finding 2] ElapsedMs/EtaMs KENDİ case'inde: yalnız faz-metninde görünürler
            // (RibbonText.Progress/ProgressStatus girdileri elapsed/ETA İÇERMEZ) — koşarken 200ms'de bir tick
            // eden bu ikisi RefreshProgress'i çağırırsa AYNI hedefe gereksiz BeginAnimation churn'ü olurdu.
            case nameof(RunViewModel.ElapsedMs):
            case nameof(RunViewModel.EtaMs):
                RefreshText();
                break;
            case nameof(RunViewModel.Counters):
            case nameof(RunViewModel.DepIssueCount):
                // Counters = kompozisyon proxy'si: statü değişiminde (tick'te DEĞİL) tetiklenir → chip'ler + metin + progress.
                RefreshText();
                RefreshProgress();
                RebuildChipsIfChanged();
                break;
        }
    }

    private void RefreshAll()
    {
        RefreshText();
        RefreshProgress();
        _lastBuildingSig = _lastFailedSig = null;
        RebuildChipsIfChanged();
    }

    // ---------------------------------------------------------------- faz metni + glyph
    private void RefreshText()
    {
        if (_vm is null) return;
        var c = _vm.Counters;
        var line = RibbonText.Compose(_vm.Phase, _vm.HasWorkspace, _vm.AllClean, c,
            _vm.WillBuildCount, _vm.FinishedOfWillBuild, c.Total,
            _vm.ElapsedMs, _vm.EtaMs, checkDurMs: _vm.ElapsedMs, warnings: 0,
            engineDiedMessage: _vm.EngineDiedMessage, syncError: _vm.SyncErrorMessage);
        // NOT (wire gap): warnings=0 — App derleyici-warning sayısını izlemiyor (RunCompletedEvent'te yok). Bkz. report.

        // [E2/T37] "Restart engine" YALNIZ engine-died kalıcı hata modunda görünür (banner/toast YOK — şerit-içi).
        PART_RestartEngine.Visibility = string.IsNullOrEmpty(_vm.EngineDiedMessage) ? Visibility.Collapsed : Visibility.Visible;

        PART_PhaseText.Text = line.Text;
        PART_PhaseText.SetResourceReference(TextBlock.ForegroundProperty, line.BrushKey);

        if (line.Glyph is { } g && GlyphStatus(g) is { } status)
        {
            PART_PhaseGlyph.Status = status;
            PART_PhaseGlyph.Visibility = Visibility.Visible;
            PART_PhaseText.Margin = PhaseTextMarginWithGlyph; // glyph→metin gap:10 (BuildApp.jsx content row gap:10)
        }
        else
        {
            PART_PhaseGlyph.Visibility = Visibility.Collapsed;
            PART_PhaseText.Margin = PhaseTextMarginNoGlyph; // glyph yok → metin ilk flex item, leading gap yok
        }
    }

    private static GraphStatus? GlyphStatus(string glyph) => glyph switch
    {
        "succeeded" => GraphStatus.Succeeded,
        "failed" => GraphStatus.Failed,
        _ => null,
    };

    /// <summary>[E5/T47] Faz metni bir live region'dır: faz ENUM'u DEĞİŞTİĞİNDE (elapsed/ETA tick'inde DEĞİL)
    /// ekran okuyucuya <c>LiveRegionChanged</c> yükselt — SR yeni faz metnini duyurur. Peer yoksa (henüz realize
    /// olmamış) sessizce atlanır; dinleyici yoksa raise güvenli (no-op).</summary>
    private void AnnouncePhaseIfChanged()
    {
        if (_vm is null || _vm.Phase == _lastAnnouncedPhase) return;
        _lastAnnouncedPhase = _vm.Phase;
        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(PART_PhaseText)
                   ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(PART_PhaseText);
        peer?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }

    // ---------------------------------------------------------------- progress
    private void RefreshProgress()
    {
        if (_vm is null) return;

        if (_vm.Phase == AppPhase.Syncing)
        {
            if (!_isIndeterminate) { _isIndeterminate = true; ApplyIndeterminate(); }
            return;
        }
        if (_isIndeterminate) { _isIndeterminate = false; StopIndeterminate(); }

        string status = RibbonText.ProgressStatus(_vm.Phase, _vm.Counters);
        SetIndicatorColor(RibbonText.FillBrushKeyFor(status)); // "anında kırmızı": renk snap (prototip inline, geçiş YOK)

        var c = _vm.Counters;
        double pct = RibbonText.Progress(_vm.Phase, _vm.AllClean, c, _vm.WillBuildCount, _vm.FinishedOfWillBuild, c.Total);
        _lastFraction = Math.Clamp(pct / 100.0, 0.0, 1.0);
        AnimateIndicatorWidth(_lastFraction * PART_ProgressTrack.ActualWidth);
    }

    private void OnTrackSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isIndeterminate) ApplyIndeterminate();
        else
        {
            PART_ProgressIndicator.BeginAnimation(WidthProperty, null); // resize'da genişliği anime etme — anında oturt
            PART_ProgressIndicator.Width = _lastFraction * PART_ProgressTrack.ActualWidth;
        }
    }

    /// <summary>[Determinate genişlik geçişi] Duration.Base + EaseStandard (prototip transition: width
    /// var(--duration-base) var(--ease-standard)). Süre/eğri + AnimationsEnabled BAŞLATMA ANINDA taze okunur.</summary>
    private void AnimateIndicatorWidth(double to)
    {
        to = Math.Max(0, to);
        var duration = MotionTokens.ResolveDuration(this, "Duration.Base", 180);
        var spline = MotionTokens.ResolveKeySpline(this, "KeySpline.EaseStandard", new KeySpline(0.4, 0, 0.2, 1));
        if (!AnimationsEnabledProvider() || duration.TimeSpan <= TimeSpan.Zero)
        {
            PART_ProgressIndicator.BeginAnimation(WidthProperty, null);
            PART_ProgressIndicator.Width = to;
            return;
        }
        PART_ProgressIndicator.BeginAnimation(WidthProperty,
            MotionTokens.SplineTo(to, duration.TimeSpan, spline), HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>[Belirsiz mod] 35% genişlikte amber bir indikatörü TranslateX -110%→320% ile 1.4s EaseInOut,
    /// 30fps, sonsuz süpürür (yalnız Syncing). Reduced-motion'da sweep kurulmaz — statik 35% bar kalır.</summary>
    private void ApplyIndeterminate()
    {
        double trackW = PART_ProgressTrack.ActualWidth;
        if (trackW <= 0) return; // ölçü henüz gelmedi — SizeChanged yeniden çağırır

        double indW = trackW * IndeterminateWidthFraction;
        PART_ProgressIndicator.BeginAnimation(WidthProperty, null);
        PART_ProgressIndicator.Width = indW;
        SetIndicatorColor("Brush.Amber"); // FILL.building (_ds_bundle.js:499)

        if (!AnimationsEnabledProvider())
        {
            PART_IndicatorTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            PART_IndicatorTranslate.X = 0;
            return;
        }

        var spline = MotionTokens.ResolveKeySpline(this, "KeySpline.EaseInOut", new KeySpline(0.65, 0, 0.35, 1));
        var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(IndeterminateFromFactor * indW, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(IndeterminateToFactor * indW,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(IndeterminateSweepMs)), spline));
        Timeline.SetDesiredFrameRate(anim, DecorativeFrameRate);
        PART_IndicatorTranslate.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void StopIndeterminate()
    {
        PART_IndicatorTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        PART_IndicatorTranslate.X = 0;
    }

    private void SetIndicatorColor(string brushKey)
    {
        if (TryFindResource(brushKey) is SolidColorBrush b) _indicatorBrush.Color = b.Color;
    }

    // ---------------------------------------------------------------- chip'ler
    private void RebuildChipsIfChanged()
    {
        if (_vm is null) return;

        var building = _vm.Projects.Where(p => p.State == ProjectRowState.Started).ToList();
        var failed = _vm.Projects.Where(p => p.State == ProjectRowState.Failed).ToList();
        string bSig = string.Join("|", building.Select(p => p.Id));
        string fSig = string.Join("|", failed.Select(p => p.Id)) + "#" + _vm.Counters.DepAffected;

        if (bSig != _lastBuildingSig) { _lastBuildingSig = bSig; BuildBuildingChips(building); }
        if (fSig != _lastFailedSig) { _lastFailedSig = fSig; BuildFailureCluster(failed); }
    }

    private void BuildBuildingChips(IReadOnlyList<ProjectRowViewModel> building)
    {
        PART_BuildingChips.Children.Clear(); // minik non-virtualized şerit (A13.2 istisnası — bkz. sınıf notu)
        var chips = new List<ToggleButton>();
        foreach (var row in building.Take(MaxBuildingChips))
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new BuildingSpinner { Size = ChipIconSize, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = GraphNode.ShortLabel(row.Name, row.NamePrefix), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var chip = MakeChip(content, brushKey: null);
            if (chips.Count > 0) chip.Margin = new Thickness(RibbonChipGap, 0, 0, 0); // BuildApp.jsx:783 flex gap:4 — ilk chip HARİÇ
            string id = row.Id;
            chip.Click += (_, _) => { _vm?.SelectProject(id); ResetChip(chip); };
            PART_BuildingChips.Children.Add(chip);
            chips.Add(chip);
        }
        BuildingChips = chips;

        BuildingOverflow = null;
        if (building.Count > MaxBuildingChips)
        {
            // Taşan: DÜZ metin "+N", tıklanamaz (BuildApp.jsx:788).
            var overflow = new TextBlock
            {
                Text = "+" + (building.Count - MaxBuildingChips).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = AppFonts.Mono,
            };
            overflow.SetResourceReference(FontSizeProperty, "FontSize.2xs");
            overflow.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextFaint");
            PART_BuildingChips.Children.Add(overflow);
            BuildingOverflow = overflow;
        }
    }

    private void BuildFailureCluster(IReadOnlyList<ProjectRowViewModel> failed)
    {
        PART_FailureCluster.Children.Clear();
        FailureChips = [];
        FailureMoreChip = null;
        if (failed.Count == 0) { PART_FailureCluster.Visibility = Visibility.Collapsed; return; }
        PART_FailureCluster.Visibility = Visibility.Visible;

        // 13px failed glyph + "{n} failed" (Xs, StatusFailText, Medium)
        PART_FailureCluster.Children.Add(new StatusGlyph { Status = GraphStatus.Failed, Size = FailureGlyphSize, VerticalAlignment = VerticalAlignment.Center });
        var nFailed = new TextBlock
        {
            Text = failed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " failed",
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        nFailed.SetResourceReference(FontSizeProperty, "FontSize.Xs");
        nFailed.SetResourceReference(TextBlock.ForegroundProperty, "Brush.StatusFailText");
        nFailed.SetResourceReference(FontWeightProperty, "FontWeight.Emphasis"); // Medium (500)
        PART_FailureCluster.Children.Add(nFailed);

        int di = _vm?.Counters.DepAffected ?? 0;
        if (di > 0)
        {
            var dep = new TextBlock
            {
                Text = "· " + di.ToString(System.Globalization.CultureInfo.InvariantCulture) + " dependency-affected",
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            dep.SetResourceReference(FontSizeProperty, "FontSize.2xs");
            dep.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextDim");
            PART_FailureCluster.Children.Add(dep);
        }

        // İlk 3 hatalı chip (tıkla→seç) + varsa "+{n-3} more" (tıkla→Failed filtresi).
        var chipStrip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var chips = new List<ToggleButton>();
        foreach (var row in failed.Take(MaxFailedChips))
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new StatusGlyph { Status = GraphStatus.Failed, Size = ChipIconSize, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = GraphNode.ShortLabel(row.Name, row.NamePrefix), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var chip = MakeChip(content, brushKey: null);
            if (chipStrip.Children.Count > 0) chip.Margin = new Thickness(RibbonChipGap, 0, 0, 0); // BuildApp.jsx:801 flex gap:4 — ilk chip HARİÇ
            string id = row.Id;
            chip.Click += (_, _) => { _vm?.SelectProject(id); ResetChip(chip); };
            chipStrip.Children.Add(chip);
            chips.Add(chip);
        }
        FailureChips = chips;

        if (failed.Count > MaxFailedChips)
        {
            var moreText = new TextBlock
            {
                Text = "+" + (failed.Count - MaxFailedChips).ToString(System.Globalization.CultureInfo.InvariantCulture) + " more",
                VerticalAlignment = VerticalAlignment.Center,
            };
            var more = MakeChip(moreText, brushKey: "Brush.StatusFailText"); // "+N more" StatusFailText renkli (BuildApp.jsx:803)
            if (chipStrip.Children.Count > 0) more.Margin = new Thickness(RibbonChipGap, 0, 0, 0); // BuildApp.jsx:801 flex gap:4
            more.Click += (_, _) => { if (_vm is not null) _vm.ActiveFilter = ProjectFilter.Failed; ResetChip(more); };
            chipStrip.Children.Add(more);
            FailureMoreChip = more;
        }
        PART_FailureCluster.Children.Add(chipStrip);
    }

    /// <summary>Ribbon chip'i: Ds.Chip stili + ölçü override'ları (height 20, padding '0 6', text-2xs —
    /// BuildApp.jsx:786). ToggleButton momentary davranır: tıklama sonrası IsChecked sıfırlanır (aktif amber
    /// yapışmasın — seçim şeritte DEĞİL kartta gösterilir).</summary>
    private ToggleButton MakeChip(object content, string? brushKey)
    {
        var chip = new ToggleButton
        {
            Content = content,
            Height = RibbonChipHeight,
            Padding = new Thickness(6, 0, 6, 0),
        };
        if (TryFindResource("Ds.Chip") is Style s) chip.Style = s;
        chip.SetResourceReference(FontSizeProperty, "FontSize.2xs");
        if (brushKey is not null) chip.SetResourceReference(ForegroundProperty, brushKey);
        return chip;
    }

    private static void ResetChip(ToggleButton chip) => chip.IsChecked = false;
}
