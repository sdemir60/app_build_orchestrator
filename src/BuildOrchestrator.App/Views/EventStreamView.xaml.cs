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
        // "Kullanıcı kaydırdı" HAM GİRDİDEN bildirilir — tekerlek, kaydırma çubuğu ve gezinme tuşları
        // (gerekçe: UserScrollSignal / BottomAnchorBehavior.NotifyUserScroll).
        UserScrollSignal.Wire(this, _bottomAnchor.NotifyUserScroll);
        ScrollAnimator.EnableUserCancellation(PART_Scroll);
        PART_Pill.Click += (_, _) => _bottomAnchor.JumpToBottom();
        // [A13/T5] Pill'in adı host'tan gelir (hangi akışın sonu — bkz. LatestPill.AccessibleName).
        PART_Pill.AccessibleName = AccessibilityNames.LatestEvents;

        PART_ActiveLine.MouseLeftButtonUp += OnActiveLineClicked;

        DataContextChanged += OnDataContextChanged;
        // [D3 §5] Unload'da daktilo saatiyle BİRLİKTE imlecin RepeatBehavior.Forever blink clock'unu da durdur
        // (aksi halde sonsuz clock unload'da terk edilirdi — StopActiveTypewriter yalnız type-timer'ı söküyordu).
        Unloaded += (_, _) => StopCursorBlink();
    }

    // ---------------------------------------------------------------- test yüzeyi
    internal TextBlock Counter => _counter;
    internal Panel RowsPanel => PART_Rows;
    internal IReadOnlyList<EventStreamRow> Rows => [.. PART_Rows.Children.OfType<EventStreamRow>()];
    internal FrameworkElement ActiveLine => PART_ActiveLine;
    internal TextBlock ActiveText => PART_ActiveText;
    /// <summary>[Test] Şu an daktilo eden satır (varsa) — "aynı anda tek satır yazar" kuralının yüzeyi.</summary>
    internal EventStreamRow? TypingRow => _typingRow;
    /// <summary>[E3/T36 reduced-motion kapsama] Aktif satır imleci — blink'in DURDUĞUNU
    /// (<c>HasAnimatedProperties==false</c>) reduced-motion'da doğrulamak için (ConsoleView.ActiveCursorGlyph deseni).
    /// <see cref="ActiveLineInstant"/> yalnız DAKTİLO zamanlayıcısını gözler; imleç blink saatini ayrı bu yüzey kanıtlar.</summary>
    internal UIElement ActiveCursorGlyph => PART_ActiveCursor;
    internal LatestPill Pill => PART_Pill;
    internal ScrollViewer Scroll => PART_Scroll;
    /// <summary>[Test · ConsoleView.FollowsBottom ikizi] Dibe çekme yetkisi
    /// (<see cref="BottomAnchorBehavior.ShouldFollow"/>).</summary>
    internal bool FollowsBottom => _bottomAnchor.ShouldFollow;

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
                {
                    var item = (StreamEventViewModel)e.NewItems[i]!;
                    // AYNI ANDA YALNIZ EN YENİ SATIR YAZAR (prototip §6): yeni bir satır gelince öncekinin
                    // yazımı ANINDA tamamlanır. Kural buradadır çünkü "en yeni" bilgisi yalnız burada var;
                    // satırın kendisi kardeşlerini bilmez. Bu kural olmadan hızlı bir koşuda alt alta birkaç
                    // satır aynı anda soldan açılıyordu.
                    _typingRow?.FinishTyping();
                    var row = CreateRow(item);
                    PART_Rows.Children.Insert(e.NewStartingIndex + i, row);
                    // Yazım BURADA başlar, satırın Loaded'ında değil: WPF Loaded'ı ertelenmiş olarak yayar ve
                    // satır bir kare boyunca tam metniyle görünüp SONRA boşalırdı (görünür bir kırpışma).
                    row.StartTypingIfPending();
                    _typingRow = row;
                }
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                double removedHeight = 0;
                for (int i = 0; i < e.OldItems.Count; i++)
                {
                    if (PART_Rows.Children[e.OldStartingIndex] is FrameworkElement row) removedHeight += row.ActualHeight;
                    PART_Rows.Children.RemoveAt(e.OldStartingIndex); // front-trim: hep aynı indeksten çekilir
                }
                CompensateForTrim(removedHeight);
                break;
            default:
                RebuildRows();
                break;
        }
        // Takip açıksa yeni satır dibe çeker (BottomAnchorBehavior içerik-büyümesi yakalaması). Yetki TEK
        // yerdedir: kullanıcı kaydırdıysa ShouldFollow kapalıdır ve satır onu yerinden oynatmaz.
        if (_bottomAnchor.ShouldFollow) _bottomAnchor.OnScrollChanged(1);
        UpdateActiveLine();
    }

    /// <summary>
    /// Tampon TEPEDEN kırpıldığında (render dilimi 150) okuyucunun konumunu korur.
    ///
    /// <para>Kaydırma konumu mutlak bir pikseldir: tepeden satır silmek, offset sabit kalsa bile okunan metni
    /// yukarı kaydırır. Sahada "scroll duruyor ama yazılar akmaya devam ediyor" diye görülen buydu — panel
    /// kullanıcıya bırakılmıştı ama içerik ayağının altından çekiliyordu. Silinen yükseklik kadar geri
    /// alınır (chunk prepend'in telafisinin aynadaki hâli). Takip açıkken gerek yoktur: orada zaten dibe
    /// yapışıyoruz.</para>
    /// </summary>
    private void CompensateForTrim(double removedHeight)
    {
        if (removedHeight <= 0 || _bottomAnchor.ShouldFollow) return;
        PART_Scroll.ScrollToVerticalOffset(Math.Max(0, PART_Scroll.VerticalOffset - removedHeight));
    }

    private void RebuildRows()
    {
        PART_Rows.Children.Clear();
        if (_vm is null) return;
        foreach (var item in _vm.StreamEvents) PART_Rows.Children.Add(CreateRow(item));
    }

    private EventStreamRow CreateRow(StreamEventViewModel item) =>
        new() { DataContext = item, AnimationsEnabledProvider = AnimationsEnabledProvider };

    /// <summary>Şu an daktilo eden (en yeni) satır — yeni bir satır gelince yazımı anında tamamlanır.</summary>
    private EventStreamRow? _typingRow;

    /// <summary>Prompt satırının tonu — hem derlenen projeyi anlatırken hem bomboşken. İmleç de bu rengi
    /// taşır (kendi rengi yoktur, satırınkini izler).</summary>
    private const string WaitingToneKey = "Brush.AmberText";

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

    /// <summary>
    /// [DEĞİŞEN KURAL · prototip §6] Prompt satırı bir GÖSTERGEDİR, yazı yüzeyi DEĞİL. İki hâli vardır ve
    /// ikisi de amberdir: derlenen proje (<c>X building…</c>) ya da bomboş (yalnız saat + imleç). Hiç daktilo
    /// etmez ve hiçbir olayın rengini almaz.
    ///
    /// <para>Eski iddia: her olay burada, kendi renginde yazılır ve sonra tampona bırakılır. Değişme gerekçesi
    /// (sahada görüldü): bırakılma anında satırın 12px'lik imleç sütunu statü glyph'ine dönüşüyordu — metin
    /// hiç değişmese de göz bunu "renk değişti" diye okuyor ve akış kararsız görünüyordu. Prototipin kendi
    /// modelinde bu kopukluk YOKTUR: satır kendi yerinde, kendi glyph'i ve kendi rengiyle yazılır; prompt
    /// satırı hiç karışmaz. Ekranda aynı anda tek bir hareket kalır ve o da en yeni satırın daktilosudur.</para>
    ///
    /// <para>Satır KOŞULSUZ durur — akış boşken de (kullanıcı kararı: imleç ilk kareden itibaren oradadır).</para>
    /// </summary>
    private void UpdateActiveLine()
    {
        if (_vm is null) return;
        PART_ActiveLine.Visibility = Visibility.Visible;
        PART_ActiveTime.Text = Console.WallClockFormat.Of(_vm.WallClock());
        PART_ActiveText.SetResourceReference(TextBlock.ForegroundProperty, WaitingToneKey); // imleç bunu izler
        PART_ActiveText.Text = _vm.ActiveLineText ?? "";
        StartCursorBlink();
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
        // Ağaçtan çıkan bir satır daktilosunu terk etmez (parıltı tek atımlıktır, sökülecek sonsuz saati yok).
        Unloaded += (_, _) => FinishTyping();
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

    // ---------------------------------------------------------------- daktilo (yalnız EN YENİ satır)

    private DispatcherTimer? _typeTimer;
    private Stopwatch? _typeClock;
    private TypewriterScheduler? _typeScheduler;

    /// <summary>[Test] Daktilo şu an koşuyor mu.</summary>
    internal bool IsTyping => _typeTimer is not null;

    /// <summary>Yazımı BAŞLATIR (henüz oynamadıysa). Sahibi <see cref="EventStreamView"/> satırı ağaca
    /// eklerken çağırır — Loaded'ı beklemek bir karelik kırpışma bırakırdı.</summary>
    internal void StartTypingIfPending() => ApplyTypewriter();

    /// <summary>
    /// [prototip §6] Satır KENDİ YERİNDE, kendi rengi ve kendi glyph'iyle harf harf yazılır. Fırtına/hata
    /// satırları (<see cref="StreamEventViewModel.ShouldType"/>) ve reduced-motion anında basılır; her satır
    /// yalnız BİR KEZ yazar (<c>TypePlayed</c> — container recycle tekrar oynatmaz).
    ///
    /// <para>"Aynı anda yalnız en yeni satır yazar" kuralı BURADA DEĞİL <see cref="EventStreamView"/>'dadır:
    /// satır kardeşlerini bilmez, yeni bir satır gelince önceki <see cref="FinishTyping"/> ile kapatılır.</para>
    /// </summary>
    private void ApplyTypewriter()
    {
        if (_vm is null) return;
        if (!_vm.ShouldType || _vm.TypePlayed || !AnimationsEnabledProvider()) { _text.Text = _vm.Text; return; }

        _vm.TypePlayed = true;
        var scheduler = new TypewriterScheduler(_vm.Text.Length, animationsEnabled: true);
        if (scheduler.Instant) { _text.Text = _vm.Text; return; }

        _typeScheduler = scheduler;
        _text.Text = "";
        _typeClock = Stopwatch.StartNew();
        _typeTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        _typeTimer.Tick += OnTypeTick;
        _typeTimer.Start();
        OnTypeTick(this, EventArgs.Empty); // ilk kare
    }

    private void OnTypeTick(object? sender, EventArgs e)
    {
        if (_vm is null || _typeScheduler is null || _typeClock is null) return;
        TimeSpan elapsed = _typeClock.Elapsed;
        int revealed = _typeScheduler.RevealedAt(elapsed);
        _text.Text = _vm.Text[..Math.Min(revealed, _vm.Text.Length)];
        // [spec §6] Metin dolduktan SONRA satır ~420ms daha "yazıyor" sayılır (prototip: tamamlanmadan 420ms
        // sonra bitti kabul edilir) — tek-yazar kuralı bu pencere boyunca da bu satırdadır.
        if (elapsed >= _typeScheduler.Duration + TimeSpan.FromMilliseconds(CursorHoldMs)) FinishTyping();
    }

    /// <summary>Yazımı ANINDA tamamlar — yeni bir satır geldiğinde ya da satır ağaçtan çıkarken.</summary>
    internal void FinishTyping()
    {
        if (_typeTimer is not null) { _typeTimer.Stop(); _typeTimer.Tick -= OnTypeTick; _typeTimer = null; }
        _typeClock?.Stop();
        _typeClock = null;
        _typeScheduler = null;
        if (_vm is not null) _text.Text = _vm.Text;
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
