using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [D3/T?] design-v1 Event stream paneli (BuildApp.jsx:639-737). DataContext bir <see cref="RunViewModel"/>'dir.
/// Panel yalnız GÖRÜNÜM: tampon satırlarını (<see cref="RunViewModel.StreamEvents"/>, render dilimi 150) kod-tarafı
/// <see cref="EventStreamRow"/>'larla yansıtır, "{n} events" sayacını (<see cref="RunViewModel.StreamEventCount"/> —
/// TAM tampon) yazar, canlı aktif satırı (<see cref="RunViewModel.ActiveLineText"/>) daktiloyla sürer ve
/// alta-yapışmayı (<see cref="BottomAnchorBehavior"/> + <see cref="LatestPill"/>, T59) tüketir. Karar mantığı SAF
/// çekirdektedir (<see cref="StreamComposer"/>/<see cref="StreamText"/>) — burada kopyalanmaz.
/// </summary>
public partial class EventStreamView : UserControl
{
    // [A13/B3 · k3] BuildApp.jsx:91 — daktilo bitince imleç ~420ms sonra söner (aktif satırda KALIR).
    // Tek tanım TypewriterScheduler.CursorHoldMs'tedir; bu derleme-zamanı alias'tır (internal: otorite
    // literaline karşı saf assert edilebilsin — ConsoleView.CursorHoldMs ile AYNI desen).
    internal const double CursorHoldMs = TypewriterScheduler.CursorHoldMs;

    private RunViewModel? _vm;
    private readonly BottomAnchorBehavior _bottomAnchor;
    private readonly TextBlock _counter = new();

    // Aktif satır daktilosu
    private DispatcherTimer? _activeTimer;
    private Stopwatch? _activeClock;
    private TypewriterScheduler? _activeScheduler;
    private string _activeFull = "";
    private long _activeGenShown = -1;

    /// <summary>[W2 · fix-1] Provider + <c>MotionSettings</c> seam'i + subscribe-once kablajı TEK yerde
    /// (<see cref="Controls.MotionGate"/>) — latch'siz kip. Fold'dan önce bu görünüm diğer sahiplerden ASİMETRİKTİ:
    /// yalnız provider'ı vardı, canlı aboneliği yoktu; OS ayarı koşu SIRASINDA değişse imleç blink'i eski
    /// durumunda kalırdı. Artık <see cref="OnMotionChanged"/> ile uyar.</summary>
    private readonly Controls.MotionGate _motion;

    /// <summary>[W2 fix-1] <c>AnimationsEnabledChanged</c>'e abone olunacak kaynak; null ise <c>App.Motion</c>.</summary>
    public Services.IMotionSettings? MotionSettings
    {
        get => _motion.MotionSettings;
        set => _motion.MotionSettings = value;
    }

    /// <summary>[ProjectRow deseni · D8] Motion sinyalinin TAZE okunduğu kapı — headless'ta <c>App.Motion</c> null
    /// (AnimationsEnabled=false); testler gerçek bir daktilo/parıltı saatini sürebilmek için bunu <c>() =&gt; true</c>
    /// ile enjekte eder. Oluşturulan her <see cref="EventStreamRow"/>'a da geçirilir.</summary>
    public Func<bool> AnimationsEnabledProvider
    {
        get => _motion.AnimationsEnabledProvider;
        set => _motion.AnimationsEnabledProvider = value;
    }

    /// <summary>[E4/T48] Üç panelin auto-scroll'unu hakem eden merkezi arbiter; null ise izole (bildirimler no-op).
    /// MainWindow enjekte eder.</summary>
    public ScrollArbiter? Arbiter { get; set; }

    public EventStreamView()
    {
        _motion = new Controls.MotionGate(this);
        _motion.Changed += OnMotionChanged;
        InitializeComponent();

        // "{n} events" sayacı (mono, 2xs, text-faint) — RightContent'e kod-tarafı konur (bkz. XAML notu).
        _counter.VerticalAlignment = VerticalAlignment.Center;
        _counter.FontFamily = AppFonts.Mono;
        _counter.SetResourceReference(TextBlock.FontSizeProperty, "FontSize.2xs");
        _counter.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextFaint");
        PART_Header.RightContent = _counter;

        _bottomAnchor = new BottomAnchorBehavior(
            getOffset: () => PART_Scroll.VerticalOffset,
            getExtent: () => PART_Scroll.ExtentHeight,
            getViewport: () => PART_Scroll.ViewportHeight,
            scrollInstant: v => PART_Scroll.ScrollToVerticalOffset(v),
            scrollSmooth: AnimateToBottom,
            // Kullanıcı kaydırıp elini çekerse akış yeniden izlenmeye başlar (gerekçe: BottomAnchorDecision.IdleResumeMs).
            autoResumeAllowed: () => true);
        _bottomAnchor.Changed += OnBottomAnchorChanged;
        PART_Scroll.ScrollChanged += (_, e) => _bottomAnchor.OnScrollChanged(e.ExtentHeightChange);
        ScrollAnimator.EnableUserCancellation(PART_Scroll);
        PART_Pill.Click += (_, _) => _bottomAnchor.JumpToBottom();
        // [A13/T5] Pill'in adı host'tan gelir (hangi akışın sonu — bkz. LatestPill.AccessibleName).
        PART_Pill.AccessibleName = AccessibilityNames.LatestEvents;

        PART_ActiveLine.MouseLeftButtonUp += OnActiveLineClicked;

        DataContextChanged += OnDataContextChanged;
        // [D3 §5] Unload'da daktilo saatiyle BİRLİKTE imlecin RepeatBehavior.Forever blink clock'unu da durdur
        // (aksi halde sonsuz clock unload'da terk edilirdi — StopActiveTypewriter yalnız type-timer'ı söküyordu).
        Unloaded += (_, _) => { StopActiveTypewriter(); StopCursorBlink(); };
    }

    // ---------------------------------------------------------------- test yüzeyi
    internal TextBlock Counter => _counter;
    internal Panel RowsPanel => PART_Rows;
    internal IReadOnlyList<EventStreamRow> Rows => [.. PART_Rows.Children.OfType<EventStreamRow>()];
    internal FrameworkElement ActiveLine => PART_ActiveLine;
    internal TextBlock ActiveText => PART_ActiveText;
    /// <summary>[Test · D3 §9] Aktif satır için SON kurulan daktilo zamanlayıcısı instant mı — §1 regresyon
    /// guard'ı (eski kod burst yüzünden instant kurardı; fix sonrası gerçek Push→FinishBuilding jump'ında
    /// daktilo koşar). Hiç kurulmadıysa true varsayılır (satır gizli).</summary>
    internal bool ActiveLineInstant => _activeScheduler?.Instant ?? true;
    /// <summary>[E3/T36 reduced-motion kapsama] Aktif satır imleci — blink'in DURDUĞUNU
    /// (<c>HasAnimatedProperties==false</c>) reduced-motion'da doğrulamak için (ConsoleView.ActiveCursorGlyph deseni).
    /// <see cref="ActiveLineInstant"/> yalnız DAKTİLO zamanlayıcısını gözler; imleç blink saatini ayrı bu yüzey kanıtlar.</summary>
    internal UIElement ActiveCursorGlyph => PART_ActiveCursor;
    internal LatestPill Pill => PART_Pill;
    internal ScrollViewer Scroll => PART_Scroll;

    // ---------------------------------------------------------------- lifecycle
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.StreamEvents.CollectionChanged -= OnStreamEventsChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = e.NewValue as RunViewModel;
        if (_vm is not null)
        {
            _vm.StreamEvents.CollectionChanged += OnStreamEventsChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
        RebuildRows();
        RefreshCounter();
        UpdateActiveLine();
        RefreshEmptyState();
    }

    /// <summary>[D3 §8] "No events yet." boş-durumu — hiç tampon satırı YOK ve canlı aktif satır (building) YOK
    /// iken görünür (prototip BuildApp.jsx:705-707). Satır sayısı veya aktif satır görünürlüğü değişince tazelenir.</summary>
    private void RefreshEmptyState()
    {
        bool empty = (_vm?.StreamEvents.Count ?? 0) == 0 && PART_ActiveLine.Visibility != Visibility.Visible;
        PART_Empty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RunViewModel.StreamEventCount):
                RefreshCounter();
                break;
            case nameof(RunViewModel.ActiveLineGeneration):
                UpdateActiveLine();
                break;
        }
    }

    // ---------------------------------------------------------------- tampon satırları
    private void OnStreamEventsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                for (int i = 0; i < e.NewItems.Count; i++)
                    PART_Rows.Children.Insert(e.NewStartingIndex + i, CreateRow((StreamEventViewModel)e.NewItems[i]!));
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                for (int i = 0; i < e.OldItems.Count; i++)
                    PART_Rows.Children.RemoveAt(e.OldStartingIndex); // front-trim: hep aynı indeksten çekilir
                break;
            default:
                RebuildRows();
                break;
        }
        // Alta yapışıksa yeni satır dibe çeker (BottomAnchorBehavior içerik-büyümesi yakalaması).
        if (_bottomAnchor.IsStuck) _bottomAnchor.OnScrollChanged(1);
        RefreshEmptyState();
    }

    private void RebuildRows()
    {
        PART_Rows.Children.Clear();
        if (_vm is null) return;
        foreach (var item in _vm.StreamEvents) PART_Rows.Children.Add(CreateRow(item));
    }

    private EventStreamRow CreateRow(StreamEventViewModel item) =>
        new()
        {
            DataContext = item,
            AnimationsEnabledProvider = AnimationsEnabledProvider,
            TypewriterStarting = TakeOverTypewriter,
        };

    /// <summary>
    /// [prototip <c>pendingId</c>] Aynı anda YALNIZ BİR satır yazar. Yeni bir satır yazmaya başladığında
    /// önceki ANINDA tamamlanır — "önceki yazı pat diye üstte tamamlanır, imleç yeni yazıya geçer".
    ///
    /// <para>Eskiden her satır kendi zamanlayıcısıyla bağımsız yazıyordu: hızlı akan bir koşuda alt alta iki
    /// üç satır aynı anda soldan sağa açılıyor, ekran huzursuz oluyordu. Prototipte bu tekil bir
    /// <c>pendingId</c>'dir; yenisi geldiğinde eski satır <c>typing</c> olmaktan çıkar ve tam metnine oturur.</para>
    /// </summary>
    private void TakeOverTypewriter(EventStreamRow next)
    {
        if (_typingRow is { } previous && !ReferenceEquals(previous, next)) previous.FinishTypewriterNow();
        _typingRow = next;
    }

    private EventStreamRow? _typingRow;

    private void RefreshCounter()
    {
        int n = _vm?.StreamEventCount ?? 0;
        _counter.Text = string.Format(CultureInfo.InvariantCulture, "{0} events", n);
    }

    // ---------------------------------------------------------------- aktif satır (canlı daktilo)
    private void OnActiveLineClicked(object sender, MouseButtonEventArgs e)
    {
        if (_vm?.ActiveLineProjectId is { } id) _vm.SelectProject(id);
    }

    /// <summary>Aktif proje DEĞİŞTİĞİNDE (<see cref="RunViewModel.ActiveLineGeneration"/>) çağrılır: metin null ise
    /// satır gizlenir; aksi halde saat + imleç yazılır ve metin (fırtına/reduced-motion değilse) daktiloyla açılır.</summary>
    /// <summary>
    /// [prototip BuildApp.jsx:900-909] Yazacak bir şey kalmayınca satır KAYBOLMAZ: geriye saat + yanıp sönen
    /// imleçten oluşan soluk bir bekleme satırı kalır — akışın "burada duruyorum" işareti, konsolun prompt
    /// satırının ikizi.
    ///
    /// <para>Aktif satırdan tek farkı rengidir (amber yerine <c>text-faint</c>) ve metninin boş olmasıdır;
    /// aynı satır yeniden kullanılır — ikinci bir satır kurmak aynı şeyi iki yerde çizmek olurdu. Akışta hiç
    /// olay yokken gösterilmez: orada boş-durum metni ("No events yet.") konuşur.</para>
    /// </summary>
    private void ShowIdlePrompt()
    {
        PART_ActiveText.Text = "";
        if ((_vm?.StreamEvents.Count ?? 0) == 0)
        {
            PART_ActiveLine.Visibility = Visibility.Collapsed;
            StopCursorBlink();
            return;
        }

        PART_ActiveLine.Visibility = Visibility.Visible;
        PART_ActiveTime.Text = Console.WallClockFormat.Of(_vm!.WallClock());
        PART_ActiveText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextFaint"); // imleç bunu izler
        StartCursorBlink();
    }

    private void UpdateActiveLine()
    {
        if (_vm is null) return;
        long gen = _vm.ActiveLineGeneration;
        if (gen == _activeGenShown) return; // aynı aktif proje — yeniden başlatma
        _activeGenShown = gen;

        StopActiveTypewriter();
        string? text = _vm.ActiveLineText;
        if (text is null)
        {
            ShowIdlePrompt();
            RefreshEmptyState(); // [D3 §8] aktif satır gizlendi — tampon da boşsa "No events yet." belirir
            return;
        }

        PART_ActiveLine.Visibility = Visibility.Visible;
        RefreshEmptyState(); // [D3 §8] canlı aktif satır var → boş-durum gizli
        PART_ActiveTime.Text = Console.WallClockFormat.Of(_vm.WallClock());
        PART_ActiveText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AmberText"); // imleç bunu izler
        StartCursorBlink();

        // [D3 §1] Aktif satır KOŞULSUZ daktilo eder (prototip BuildApp.jsx:723 `<TypingLine instant={false} />`).
        // Yalnız reduced-motion instant yapar (TypewriterScheduler animationsEnabled ctor arg'ı üzerinden). Fırtına
        // (burst) YALNIZ tampon satırlarına aittir (Emission.Instant) — aktif satırı ASLA gate etmez.
        bool animate = AnimationsEnabledProvider();
        _activeScheduler = new TypewriterScheduler(text.Length, animationsEnabled: animate);
        if (_activeScheduler.Instant)
        {
            PART_ActiveText.Text = text;
            return;
        }

        _activeFull = text;
        PART_ActiveText.Text = "";
        _activeClock = Stopwatch.StartNew();
        _activeTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        _activeTimer.Tick += OnActiveTypeTick;
        _activeTimer.Start();
        OnActiveTypeTick(this, EventArgs.Empty); // ilk kare
    }

    private void OnActiveTypeTick(object? sender, EventArgs e)
    {
        if (_activeScheduler is null || _activeClock is null) return;
        TimeSpan elapsed = _activeClock.Elapsed;
        int revealed = _activeScheduler.RevealedAt(elapsed);
        PART_ActiveText.Text = _activeFull[..Math.Min(revealed, _activeFull.Length)];
        if (elapsed >= _activeScheduler.Duration + TimeSpan.FromMilliseconds(CursorHoldMs))
        {
            // Daktilo bitti — timer durur ama aktif satır (canlı "building…") KALIR; imleç steady blink'te sürer.
            _activeTimer?.Stop();
            if (_activeTimer is not null) _activeTimer.Tick -= OnActiveTypeTick;
            _activeTimer = null;
            _activeClock?.Stop();
            _activeClock = null;
            PART_ActiveText.Text = _activeFull;
        }
    }

    private void StopActiveTypewriter()
    {
        if (_activeTimer is not null) { _activeTimer.Stop(); _activeTimer.Tick -= OnActiveTypeTick; _activeTimer = null; }
        _activeClock?.Stop();
        _activeClock = null;
        _activeScheduler = null;
    }

    /// <summary>[W2 fix-1] Motion sinyali koşu SIRASINDA değişince görünüm uyar. Tek SONSUZ animasyon aktif
    /// satırın imleç blink'idir: <see cref="StartCursorBlink"/> sinyali TAZE okur ve kapalıysa saati söküp imleci
    /// 1.0'a sabitler, açıksa yeniden kurar. Aktif satır görünmüyorsa yapılacak bir şey yoktur.
    ///
    /// <para>Bir kereye mahsus efektler (aktif satır daktilosu, satır parıltısı) burada YENİDEN OYNATILMAZ —
    /// sinyal sonradan açılınca geriye dönük animasyon başlatmak sözleşme ihlali olurdu (<c>GlowPlayed</c>/
    /// <c>TypePlayed</c> guard'larıyla aynı tek-yönlülük).</para></summary>
    private void OnMotionChanged(object? sender, EventArgs e)
    {
        if (PART_ActiveLine.Visibility == Visibility.Visible) StartCursorBlink();
    }

    // [D3 §3] aktif imleç blink'i — ortak MotionTokens.CreateBlinkAnimation (1.0→0.1, 0.55s, SineEase in/out,
    // 30fps, sonsuz). Reduced-motion'da hiç oynamaz (imleç steady 1.0).
    private void StartCursorBlink()
    {
        if (!AnimationsEnabledProvider()) { PART_ActiveCursor.BeginAnimation(OpacityProperty, null); PART_ActiveCursor.Opacity = 1.0; return; }
        PART_ActiveCursor.BeginAnimation(OpacityProperty, MotionTokens.CreateBlinkAnimation());
    }

    private void StopCursorBlink()
    {
        PART_ActiveCursor.BeginAnimation(OpacityProperty, null);
        PART_ActiveCursor.Opacity = 1.0;
    }

    // ---------------------------------------------------------------- alta-yapışma
    /// <summary>[E4/T48] Stream'in bottom-anchor'ının merkezi arbiter'a bölgesel suppress bildirimi + pill görünürlüğü
    /// (ConsoleView.OnBottomAnchorChanged deseni — YALNIZ stream paneli duraklar/döner). Arbiter null ise yalnız pill.</summary>
    private void OnBottomAnchorChanged(object? sender, EventArgs e)
    {
        PART_Pill.Visibility = _bottomAnchor.ShowPill ? Visibility.Visible : Visibility.Collapsed;
        if (Arbiter is null) return;
        if (_bottomAnchor.IsStuck) Arbiter.Resume(ScrollPanel.Stream);
        else Arbiter.NotifyUserScroll(ScrollPanel.Stream);
    }

    private bool AnimateToBottom(double target) =>
        MotionTokens.AnimateSlowEaseInOut(this, PART_Scroll, PART_Scroll.VerticalOffset, target);
}

/// <summary>
/// [D3/T?] Event stream tek satırı görünümü (BuildApp.jsx:627-659) — kod-tarafı (parıltı/seçim/daktilo motion
/// sözleşmesince kod-tarafı, MotionTokens.cs). DataContext bir <see cref="StreamEventViewModel"/>'dir; satır onun
/// INotifyPropertyChanged'ini dinler. Şerit 2px amber (yalnız seçili), zemin per-instance brush (seçili →
/// <c>SurfaceRaised</c>, hover → <c>SurfaceHover</c>, parıltı → <c>StatusSuccessSoft</c>→şeffaf 1.1s bir kez).
/// </summary>
public sealed class EventStreamRow : Border
{
    // design-v1 kaynak sabitleri (inline magic number YASAK — StatusGlyph.PulseMs deseni).
    // [A13/T4 fix-1 · A3] internal — SuccessFlourishTests artık değeri saf `Assert.Equal` ile pinliyor.
    internal const double GlowMs = 1100;      // BuildApp.jsx:19 `bo-glow-once 1.1s`
    private const double GlyphColumn = 12;   // BuildApp.jsx:653 glyph 12px kolon
    private const double RowMinHeight = 24;  // BuildApp.jsx:645 minHeight 24
    private const double SlotGap = 8;         // BuildApp.jsx:645 gap 8
    private const double StripeWidth = 2;     // BuildApp.jsx:651 width 2
    // [A13/B3 · k3] Üçüncü kopyaydı — tek tanım TypewriterScheduler.CursorHoldMs (derleme-zamanı alias).
    internal const double CursorHoldMs = TypewriterScheduler.CursorHoldMs;

    private readonly SolidColorBrush _bgBrush = new(Colors.Transparent); // per-instance (A13.2)
    private readonly Rectangle _stripe = new();
    private readonly TextBlock _time = new();
    private readonly Border _glyphHost = new();
    private readonly TextBlock _text = new();
    private StreamEventViewModel? _vm;
    private bool _hover;

    // En yeni satır daktilosu
    private DispatcherTimer? _typeTimer;
    private Stopwatch? _typeClock;
    private TypewriterScheduler? _scheduler;
    private string _typeFull = "";

    /// <summary>[W2 · fix-1] Provider + <c>MotionSettings</c> seam'i + subscribe-once kablajı TEK yerde
    /// (<see cref="Controls.MotionGate"/>) — latch'siz kip. Provider'ı <see cref="EventStreamView"/> her satıra
    /// elle geçirir; canlı abonelik ise satırın KENDİ <c>Loaded</c>/<c>Unloaded</c>'ına bağlıdır.</summary>
    private readonly Controls.MotionGate _motion;

    /// <summary>[W2 fix-1] <c>AnimationsEnabledChanged</c>'e abone olunacak kaynak; null ise <c>App.Motion</c>.</summary>
    public Services.IMotionSettings? MotionSettings
    {
        get => _motion.MotionSettings;
        set => _motion.MotionSettings = value;
    }

    public Func<bool> AnimationsEnabledProvider
    {
        get => _motion.AnimationsEnabledProvider;
        set => _motion.AnimationsEnabledProvider = value;
    }

    /// <summary>[Test] Parıltının kaç kez BAŞLATILDIĞI — container recycle sonrası TEKRAR oynanmadığını
    /// (GlowPlayed guard'ı) kanıtlar.</summary>
    internal int GlowPlayCount { get; private set; }
    internal Rectangle SelectionStripe => _stripe;
    internal StreamEventViewModel? ViewModel => _vm;
    /// <summary>[A13/T4 fix-1 · B3] Satırın kendi (akan) metin yüzeyi — <c>Typography.NumeralAlignment</c>'ın
    /// altıncı üretim yeri (aşağıda, <see cref="_text"/>'in kurulumunda) bu alandadır; tüketicisi
    /// <c>EventStreamTests.The_active_line_and_row_text_are_tabular</c>'dır.
    /// <para>[A13/final · lensA Ö3] Atıf DÜZELTİLDİ: doc, bu branch'in KENDİ fix-1 · C3 turunda dağıttığı
    /// <c>TabularFiguresTests</c> sınıfına işaret ediyordu (o sınıf artık YOK) ve satır referansı da bayattı.
    /// Satır numarası yerine üye adı yazılır — sürüklenmesi olanaksız.</para></summary>
    internal TextBlock Text => _text;
    /// <summary>[A13/T3b · b8] Glyph kolonunun host'u (12px genişlik, BuildApp.jsx:653) — dış testlerin
    /// ölçüm iddiasını gerçek bir realize üzerinde doğrulayabilmesi için (kural 5) SelectionStripe deseniyle
    /// AYNI gerekçeyle dışa açılır.</summary>
    internal Border GlyphHost => _glyphHost;

    public EventStreamRow()
    {
        _motion = new Controls.MotionGate(this);
        _motion.Changed += OnMotionChanged;
        MinHeight = RowMinHeight;
        Background = _bgBrush;
        SnapsToDevicePixels = true;

        _stripe.Width = StripeWidth;
        _stripe.HorizontalAlignment = HorizontalAlignment.Left;
        _stripe.VerticalAlignment = VerticalAlignment.Stretch;
        _stripe.Visibility = Visibility.Collapsed;
        _stripe.SetResourceReference(Shape.FillProperty, "Brush.Amber");

        _time.VerticalAlignment = VerticalAlignment.Center;
        _time.Margin = new Thickness(0, 0, SlotGap, 0);
        _time.FontFamily = AppFonts.Mono;
        _time.SetResourceReference(TextBlock.FontSizeProperty, "FontSize.Xs");
        _time.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextFaint");

        _glyphHost.Width = GlyphColumn;
        _glyphHost.Margin = new Thickness(0, 0, SlotGap, 0);
        _glyphHost.VerticalAlignment = VerticalAlignment.Center;

        _text.VerticalAlignment = VerticalAlignment.Center;
        _text.FontFamily = AppFonts.Mono;
        _text.TextTrimming = TextTrimming.CharacterEllipsis;
        _text.TextWrapping = TextWrapping.NoWrap;
        _text.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        _text.SetResourceReference(TextBlock.FontSizeProperty, "FontSize.Xs");
        _text.SetResourceReference(TextBlock.LineHeightProperty, "LineHeight.Mono12"); // [D3 §7] prototip lineHeight var(--leading-mono), BuildApp.jsx:646

        // [D3 §6] içerik iki-yan 10px padding (prototip padding '0 10px', BuildApp.jsx:645) — sağ nefes (NoWrap+
        // ellipsis metin sağ kenara dayanmasın); aktif satır zaten Margin='10,0' ile doğru.
        var content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(_time);
        content.Children.Add(_glyphHost);
        content.Children.Add(_text);

        var grid = new Grid();
        grid.Children.Add(_stripe);
        grid.Children.Add(content);
        Child = grid;

        DataContextChanged += OnDataContextChanged;
        MouseEnter += (_, _) => SetHover(true);
        MouseLeave += (_, _) => SetHover(false);
        MouseLeftButtonUp += OnClicked;
        Loaded += OnLoaded;
        Unloaded += (_, _) => StopTypewriter();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as StreamEventViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;
        ApplyAll();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StreamEventViewModel.IsSelected)) ApplySelection();
    }

    private void ApplyAll()
    {
        if (_vm is null) return;
        _time.Text = _vm.Time;
        _text.Text = _vm.Text; // daktilo edilecekse Loaded'da sıfırlanır
        _text.SetResourceReference(TextBlock.ForegroundProperty, _vm.TextBrushKey);
        Cursor = _vm.IsClickable ? Cursors.Hand : Cursors.Arrow;
        BuildGlyph();
        ApplySelection();
    }

    /// <summary>BuildApp.jsx:653 — statü glyph'i 12px; sync/info için amber <c>▸</c> (12px kolonda ortalı).</summary>
    private void BuildGlyph()
    {
        if (_vm?.GlyphStatus is { } status)
        {
            _glyphHost.Child = new StatusGlyph { Status = status, Size = GlyphColumn, VerticalAlignment = VerticalAlignment.Center };
        }
        else
        {
            var marker = new TextBlock
            {
                Text = "▸", // ▸
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = AppFonts.Mono,
            };
            marker.SetResourceReference(TextBlock.FontSizeProperty, "FontSize.Xs");
            marker.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AmberText");
            _glyphHost.Child = marker;
        }
    }

    private void ApplySelection()
    {
        bool selected = _vm?.IsSelected ?? false;
        _stripe.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        ApplyBackground();
    }

    private void SetHover(bool hover)
    {
        if (_vm is null || !_vm.IsClickable) return; // done/sync/info: hover zemini yok (parıltıyı ezmesin)
        if (_hover == hover) return;
        _hover = hover;
        ApplyBackground();
    }

    private void ApplyBackground()
    {
        bool selected = _vm?.IsSelected ?? false;
        Color target = selected ? ResolveColor("Brush.SurfaceRaised")
            : _hover && (_vm?.IsClickable ?? false) ? ResolveColor("Brush.SurfaceHover")
            : Colors.Transparent;
        MotionTokens.TransitionColor(this, _bgBrush, target);
    }

    private void OnClicked(object sender, MouseButtonEventArgs e)
    {
        if (_vm?.ProjectId is { } id) FindRunViewModel()?.SelectProject(id);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyGlow();
        ApplyTypewriter();
    }

    // ---------------------------------------------------------------- parıltı (bir kez)
    /// <summary>[A13.2] Yalnız <c>done</c>+hatasız satır: per-instance zemin <c>StatusSuccessSoft</c>→şeffaf, 1.1s
    /// EaseOut, BİR KEZ. <see cref="StreamEventViewModel.GlowPlayed"/> guard'ı container recycle'da tekrar oynatmaz;
    /// reduced-motion'da hiç oynamaz (yalnız oynandı işaretlenir).</summary>
    private void ApplyGlow()
    {
        if (_vm is null || !_vm.GlowEligible || _vm.GlowPlayed) return;
        if (!AnimationsEnabledProvider()) { _vm.GlowPlayed = true; return; }

        Color from = ResolveColor("Brush.StatusSuccessSoft");
        var spline = MotionTokens.ResolveKeySpline(this, "KeySpline.EaseOut", new KeySpline(0.22, 1, 0.36, 1));
        var anim = new ColorAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        anim.KeyFrames.Add(new DiscreteColorKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new SplineColorKeyFrame(Colors.Transparent, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(GlowMs)), spline));
        _bgBrush.Color = Colors.Transparent; // taban: Stop sonrası buraya döner (yeşile geri sıçrama YOK)
        _bgBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);

        _vm.GlowPlayed = true;
        GlowPlayCount++;
    }

    /// <summary>[Test] Container recycle'ı taklit eder — DataContext'i (aynı VM) yeniden bağlar ve Loaded yolunu
    /// yeniden koşar. GlowPlayed guard'ı sayesinde parıltı TEKRAR oynamaz (<see cref="GlowPlayCount"/> sabit kalır).</summary>
    internal void SimulateContainerRecycle()
    {
        ApplyAll();
        ApplyGlow();
        ApplyTypewriter();
    }

    /// <summary>
    /// [prototip <c>pendingId</c>] Daktilo BAŞLARKEN host'a haber verilir; host aynı anda YALNIZ BİR satırın
    /// yazmasına izin verir ve öncekini anında tamamlar. Tekil <c>pendingId</c>'nin karşılığıdır.
    /// </summary>
    internal Action<EventStreamRow>? TypewriterStarting { get; set; }

    /// <summary>Uçuştaki daktiloyu ANINDA bitirir: metin tam hâline oturur. Yeni bir satır yazmaya
    /// başladığında host bunu çağırır — "önceki yazı pat diye üstte tamamlanır".</summary>
    internal void FinishTypewriterNow()
    {
        if (_typeTimer is null) return;
        StopTypewriter();
        _text.Text = _typeFull;
    }

    // ---------------------------------------------------------------- en yeni satır daktilosu (bir kez)
    private void ApplyTypewriter()
    {
        if (_vm is null) return;
        if (!_vm.ShouldType || _vm.TypePlayed || _vm.Instant) { _text.Text = _vm.Text; return; }
        if (!AnimationsEnabledProvider()) { _text.Text = _vm.Text; _vm.TypePlayed = true; return; }

        TypewriterStarting?.Invoke(this);
        _vm.TypePlayed = true;
        _scheduler = new TypewriterScheduler(_vm.Text.Length, animationsEnabled: true);
        _typeFull = _vm.Text;
        _text.Text = "";
        _typeClock = Stopwatch.StartNew();
        _typeTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        _typeTimer.Tick += OnTypeTick;
        _typeTimer.Start();
        OnTypeTick(this, EventArgs.Empty);
    }

    private void OnTypeTick(object? sender, EventArgs e)
    {
        if (_scheduler is null || _typeClock is null) return;
        TimeSpan elapsed = _typeClock.Elapsed;
        int revealed = _scheduler.RevealedAt(elapsed);
        _text.Text = _typeFull[..Math.Min(revealed, _typeFull.Length)];
        if (elapsed >= _scheduler.Duration + TimeSpan.FromMilliseconds(CursorHoldMs))
        {
            StopTypewriter();
            _text.Text = _typeFull;
        }
    }

    /// <summary>[W2 fix-1] Motion sinyali koşu SIRASINDA KAPANIRSA uçuştaki daktilo ANINDA tamamlanır — satır
    /// harf harf yazmaya devam edemez (motion sözleşmesi: sinyal taze okunur). Sinyalin sonradan AÇILMASI ise
    /// hiçbir şeyi yeniden oynatmaz (<c>TypePlayed</c>/<c>GlowPlayed</c> guard'larıyla aynı tek-yönlülük);
    /// bu yüzden burada yalnız "kapandı" yönü ele alınır.</summary>
    private void OnMotionChanged(object? sender, EventArgs e)
    {
        if (AnimationsEnabledProvider() || _scheduler is null) return;
        StopTypewriter();
        _text.Text = _typeFull;
    }

    private void StopTypewriter()
    {
        if (_typeTimer is not null) { _typeTimer.Stop(); _typeTimer.Tick -= OnTypeTick; _typeTimer = null; }
        _typeClock?.Stop();
        _typeClock = null;
        _scheduler = null;
    }

    // ---------------------------------------------------------------- yardımcılar
    private Color ResolveColor(string key) => TryFindResource(key) is SolidColorBrush b ? b.Color : Colors.Transparent;

    private RunViewModel? FindRunViewModel()
    {
        DependencyObject? d = this;
        while (d is not null)
        {
            if (d is FrameworkElement fe && fe.DataContext is RunViewModel run) return run;
            d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
        }
        return null;
    }
}
