using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;

namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/A13.2 + 3a] AvalonEdit tabanlı, salt-okunur, batch-append canlı konsol. Iskelet (A13.2): <see cref="AppendBatch"/>
/// TAM OLARAK <c>BeginUpdate → tek Insert → EndUpdate</c> + ScrollToEnd. 3a eklentileri:
/// <list type="bullet">
/// <item><b>Colorizer</b> (<see cref="ConsoleColorizer"/>): satır-offset bazlı renk; belge DÜZ metin kalır
/// (kopyalanan metin anlamlı). Loaded'da token brush'larından kurulur; testte <see cref="EnableColorizer"/> ile enjekte edilir.</item>
/// <item><b>Hibrit aktif satır</b> (<see cref="TypeActiveLine"/>): en yeni satır dokümana girmeden overlay TextBlock'ta
/// daktilolanır (Stopwatch-bazlı <see cref="TypewriterScheduler"/>), bitince <see cref="AppendBatch"/> ile commit.
/// İmleç 7×13px Rectangle (1.1s blink, DesiredFrameRate=30). Reduced-motion iken INSTANT (overlay/blink YOK).</item>
/// </list>
/// Cascade pop-in, chunk loader, "⌄ latest" pill, copy-log → Task 3b/T59 (burada YOK).
/// </summary>
public partial class ConsoleView : UserControl
{
    // Yazım bitince imlecin sönmeden önce kaldığı süre (design-v1 §2.5 "imleç ~420ms sonra söner").
    private const double CursorHoldMs = 420.0;

    // Gömülü Geist Mono Console composite font (It-0 asset'i) — pack URI (App assembly'sinden gömülü kaynak).
    private static readonly FontFamily ConsoleFontFamily = new(
        new Uri("pack://application:,,,/BuildOrchestrator.App;component/Fonts/"),
        "./#Geist Mono Console");

    private ConsoleColorizer? _colorizer;
    private ConsolePalette? _palette;

    // Aktif-satır daktilosu durumu (yalnız UI thread'inde dokunulur).
    private DispatcherTimer? _typeTimer;
    private Stopwatch? _typeClock;
    private TypewriterScheduler? _scheduler;
    private string _activeText = "";

    public ConsoleView()
    {
        InitializeComponent();
        EditorControl.FontFamily = ConsoleFontFamily;
        ActiveLineText.FontFamily = ConsoleFontFamily;
        Loaded += (_, _) => EnsureColorizer();
    }

    /// <summary>Test/host erişimi için altındaki AvalonEdit kontrolü.</summary>
    public TextEditor Editor => EditorControl;

    /// <summary>Task 12'nin run/proje görünümü arasında doküman değiştirebilmesi için dışa açılır.</summary>
    public TextDocument Document
    {
        get => EditorControl.Document;
        set => EditorControl.Document = value;
    }

    /// <summary>true iken her <see cref="AppendBatch"/> sonrası en alta kaydırılır (varsayılan true).</summary>
    public bool StickToBottom { get; set; } = true;

    /// <summary>
    /// UI thread'inde çağrılır. TEK batch ekler — asla satır satır bölmez, asla <c>Dispatcher.Invoke</c>
    /// çağırmaz (çağıranın/Task 12'nin sorumluluğu). [A13.2 ZORUNLU sıra]
    /// </summary>
    public void AppendBatch(string text)
    {
        var document = EditorControl.Document;
        document.BeginUpdate();
        try
        {
            document.Insert(document.TextLength, text);
        }
        finally
        {
            document.EndUpdate();
        }
        if (StickToBottom)
            EditorControl.ScrollToEnd();
    }

    // ---------------------------------------------------------------- colorizer

    /// <summary>Loaded'da (kontrol logical/visual tree'ye girip token kaynakları çözülebilir olunca) colorizer'ı
    /// bir KEZ kurar. Kaynak yoksa (headless) sessizce atlar — colorizer yalnız görsel katman.</summary>
    private void EnsureColorizer()
    {
        if (_colorizer is not null) return;
        object? Probe(string key) => TryFindResource(key);
        if (Probe("Brush.TextFaint") is null) return; // token'lar henüz yok — üretimde Loaded'da hazırdır
        EnableColorizer(ConsolePalette.FromLookup(Probe));
    }

    /// <summary>Colorizer'ı verilen palet ile kurar (test enjeksiyonu; üretimde <see cref="EnsureColorizer"/> çağırır).
    /// Idempotent değildir — bir kez çağrılmalı; ikinci çağrı öncekini değiştirir.</summary>
    public void EnableColorizer(ConsolePalette palette)
    {
        _palette = palette;
        if (_colorizer is not null)
            EditorControl.TextArea.TextView.LineTransformers.Remove(_colorizer);
        _colorizer = new ConsoleColorizer(palette);
        EditorControl.TextArea.TextView.LineTransformers.Add(_colorizer);
    }

    // ---------------------------------------------------------------- hibrit aktif satır (typewriter)

    /// <summary>
    /// En yeni satırı hibrit daktiloyla yazar: overlay'de karakter karakter (Stopwatch-bazlı, ≤~250ms), bitince
    /// dokümana commit (düz metin). İmleç ~420ms daha kalıp söner. Reduced-motion iken INSTANT — doğrudan
    /// <see cref="AppendBatch"/> (overlay/blink YOK). Motion sinyali BAŞLATMA anında TAZE okunur (motion sözleşmesi).
    /// </summary>
    public void TypeActiveLine(string text)
    {
        EnsureColorizer();
        text ??= "";
        bool animationsEnabled = BuildOrchestrator.App.App.Motion?.AnimationsEnabled ?? false;

        var type = ConsoleLineClassifier.Classify(text);
        Brush color = _palette?.ForType(type) ?? EditorControl.Foreground;

        // Önceki uçuştaki daktilo bitmemişse kaybetme: hemen commit et.
        if (_typeTimer is not null) FinishActiveLine(commit: true);

        if (!animationsEnabled)
        {
            AppendBatch(text + "\n"); // instant — satır tam görünür, blink yok
            return;
        }

        _scheduler = new TypewriterScheduler(text.Length, animationsEnabled: true);
        _activeText = text;
        ActiveLineText.Foreground = color;
        ActiveLineText.Text = "";
        ActiveCursor.Fill = color;
        ActiveLineOverlay.Visibility = Visibility.Visible;
        StartBlink();

        _typeClock = Stopwatch.StartNew();
        _typeTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        _typeTimer.Tick += OnTypeTick;
        _typeTimer.Start();
        OnTypeTick(this, EventArgs.Empty); // ilk kareyi hemen çiz
    }

    private void OnTypeTick(object? sender, EventArgs e)
    {
        if (_scheduler is null || _typeClock is null) return;
        TimeSpan elapsed = _typeClock.Elapsed;
        int revealed = _scheduler.RevealedAt(elapsed);
        ActiveLineText.Text = _activeText[..Math.Min(revealed, _activeText.Length)];

        // Yazım tamamlandıktan CursorHoldMs sonra: satır dokümana yerleşir, imleç söner.
        if (elapsed >= _scheduler.Duration + TimeSpan.FromMilliseconds(CursorHoldMs))
            FinishActiveLine(commit: true);
    }

    private void FinishActiveLine(bool commit)
    {
        if (_typeTimer is not null)
        {
            _typeTimer.Stop();
            _typeTimer.Tick -= OnTypeTick;
            _typeTimer = null;
        }
        _typeClock?.Stop();
        _typeClock = null;
        StopBlink();
        ActiveLineOverlay.Visibility = Visibility.Collapsed;
        ActiveLineText.Text = "";
        string pending = _activeText;
        _activeText = "";
        _scheduler = null;
        if (commit && pending.Length > 0) AppendBatch(pending + "\n");
    }

    private void StartBlink()
    {
        // design-v1: 1.1s, opacity 1→0.1→1 (ease-in-out). Dekoratif sonsuz animasyon → DesiredFrameRate=30.
        var blink = new DoubleAnimation(1.0, 0.1, new Duration(TimeSpan.FromSeconds(0.55)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(blink, 30);
        ActiveCursor.BeginAnimation(OpacityProperty, blink);
    }

    private void StopBlink()
    {
        ActiveCursor.BeginAnimation(OpacityProperty, null);
        ActiveCursor.Opacity = 1.0;
    }
}
