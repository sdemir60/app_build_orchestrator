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
/// [T56/A13.2 + 3a + 3b] AvalonEdit tabanlı, salt-okunur, batch-append canlı konsol. Iskelet (A13.2):
/// <see cref="AppendBatch"/> TAM OLARAK <c>BeginUpdate → tek Insert → EndUpdate</c> + ScrollToEnd.
/// <list type="bullet">
/// <item><b>Colorizer</b> (<see cref="ConsoleColorizer"/>): satır-offset bazlı renk; belge DÜZ metin kalır.</item>
/// <item><b>Hibrit aktif satır</b> (<see cref="TypeActiveLine"/>): overlay TextBlock'ta daktilo, bitince commit;
/// imleç 7×13px Rectangle (1.1s blink); yazımdan ~420ms sonra <b>fade-out</b> ile söner (3b Minor 3 — hard cut değil).</item>
/// <item><b>Kaskat</b> (<see cref="PlayCascade"/>): proje-log moduna geçişte satırlar 26ms'de 3, satır başına 140ms
/// opacity-fade ile belirir (<see cref="CascadeScheduler"/> + <see cref="CascadeFadeTransformer"/>).</item>
/// <item><b>Chunk loader</b>: proje logu render dilimi (son 200) gösterilir; tepeye kaydırınca önceki chunk
/// scroll-telafili prepend edilir (<see cref="ChunkStitch"/>).</item>
/// <item><b>Render dilimi</b>: canlı append'te belge son <see cref="RenderSliceLines"/> satırla sınırlıdır (Ek A #16).</item>
/// </list>
/// </summary>
public partial class ConsoleView : UserControl
{
    // Yazım bitince imlecin sönmeden önce kaldığı süre (design-v1 §2.5 "imleç ~420ms sonra söner").
    private const double CursorHoldMs = 420.0;
    // [3b Minor 1/2] Off-palette hex YOK: base foreground + FontSize XAML token/resource'ından gelir.

    /// <summary>[3b/Ek A #16] Canlı append'te belgede tutulan azami satır (render dilimi). "N lines" sayacı bundan
    /// ETKİLENMEZ — tam mantıksal sayacı VM taşır (render dilimi DEĞİL, Ek A #23).</summary>
    public const int RenderSliceLines = ConsoleRenderSlice.DefaultMaxLines; // 200
    // Kullanıcı tepeye ne kadar yaklaşınca önceki chunk yüklenir (px) — bottom-stick eşiğiyle uyumlu (48px).
    private const double ChunkTopThresholdPx = 48.0;

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
    private bool _cursorFading; // imleç fade-out animasyonu uçuşta mı (Minor 3)

    // Kaskat durumu (yalnız UI thread'inde).
    private DispatcherTimer? _cascadeTimer;
    private Stopwatch? _cascadeClock;
    private CascadeFadeTransformer? _cascadeFade;
    private bool _buildInProgressPending; // kaskat bitince amber "build in progress ▮" gösterilecek mi

    // Render dilimi / chunk loader durumu.
    private bool _trimTail = true;                        // canlı append son RenderSliceLines'e kırpılır (run modu)
    private bool _projectMode;                            // proje-log modu (chunk prepend etkin)
    private bool _prepending;                             // re-entrancy guard (prepend VerticalOffset'i değiştirir)
    private bool _armedForChunk;                          // kullanıcı tepeden UZAKLAŞTI mı — ilk layout spurious prepend'ini önler
    private IReadOnlyList<string> _projectAllLines = [];  // proje logunun TAM satırları (chunk kaynağı)
    private int _loadedFrom;                              // belgede yüklü ilk satırın _projectAllLines'taki index'i

    public ConsoleView()
    {
        InitializeComponent();
        EditorControl.FontFamily = ConsoleFontFamily;
        ActiveLineText.FontFamily = ConsoleFontFamily;
        BuildProgressText.FontFamily = ConsoleFontFamily;
        Loaded += (_, _) => EnsureColorizer();
        EditorControl.TextArea.TextView.ScrollOffsetChanged += (_, _) => OnScrollOffsetChanged();
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
    /// çağırmaz (çağıranın/Task 12'nin sorumluluğu). [A13.2 ZORUNLU sıra]. [3b] Run modunda belge, son
    /// <see cref="RenderSliceLines"/> satırla sınırlanır (baştan kırpma); proje modunda kırpma YOK (chunk loader
    /// eski satırları yönetir).
    /// </summary>
    public void AppendBatch(string text)
    {
        var document = EditorControl.Document;
        document.BeginUpdate();
        try
        {
            document.Insert(document.TextLength, text);
            // Run modunda daima; proje modunda YALNIZ alta-yapışıkken (follow) tail-trim: chatty bir build
            // (MSBuild hacmi) belgeyi sınırsız büyütmesin — render dilimi kadar tutulur (§3.6). [3b M-2]
            // Kullanıcı yukarı kaydırıp chunk gezerken (StickToBottom=false) trim YOK — prepend'le çakışmaz.
            if (_trimTail || (_projectMode && StickToBottom)) TrimToRenderSlice(document);
        }
        finally
        {
            document.EndUpdate();
        }
        if (StickToBottom)
            EditorControl.ScrollToEnd();
    }

    // Belgeyi son RenderSliceLines satıra kırpar (baştaki fazla satırları TEK Remove ile siler).
    private static void TrimToRenderSlice(TextDocument document)
    {
        int excess = document.LineCount - RenderSliceLines;
        if (excess <= 0) return;
        var lastToRemove = document.GetLineByNumber(excess); // 1..excess satırlarını (ayraçlarıyla) sil
        document.Remove(0, lastToRemove.Offset + lastToRemove.TotalLength);
    }

    // ---------------------------------------------------------------- colorizer

    /// <summary>Loaded'da colorizer'ı bir KEZ kurar. Kaynak yoksa (headless) sessizce atlar.</summary>
    private void EnsureColorizer()
    {
        if (_colorizer is not null) return;
        object? Probe(string key) => TryFindResource(key);
        if (Probe("Brush.TextFaint") is null) return; // token'lar henüz yok — üretimde Loaded'da hazırdır
        EnableColorizer(ConsolePalette.FromLookup(Probe));
    }

    /// <summary>Colorizer'ı verilen palet ile kurar (test enjeksiyonu; üretimde <see cref="EnsureColorizer"/> çağırır).</summary>
    public void EnableColorizer(ConsolePalette palette)
    {
        _palette = palette;
        if (_colorizer is not null)
            EditorControl.TextArea.TextView.LineTransformers.Remove(_colorizer);
        _colorizer = new ConsoleColorizer(palette);
        // Colorizer'ı kaskat fade'inden ÖNCE tut (fade rengi okur, alpha'sını modüle eder).
        int cascadeIndex = _cascadeFade is null ? -1 : EditorControl.TextArea.TextView.LineTransformers.IndexOf(_cascadeFade);
        if (cascadeIndex >= 0)
            EditorControl.TextArea.TextView.LineTransformers.Insert(cascadeIndex, _colorizer);
        else
            EditorControl.TextArea.TextView.LineTransformers.Add(_colorizer);
    }

    // ---------------------------------------------------------------- hibrit aktif satır (typewriter)

    /// <summary>
    /// En yeni satırı hibrit daktiloyla yazar: overlay'de karakter karakter (Stopwatch-bazlı, ≤~250ms), bitince
    /// dokümana commit. İmleç ~420ms daha kalıp <b>fade-out</b> ile söner. Reduced-motion iken INSTANT — doğrudan
    /// <see cref="AppendBatch"/>. Motion sinyali BAŞLATMA anında TAZE okunur (motion sözleşmesi).
    /// </summary>
    public void TypeActiveLine(string text)
    {
        EnsureColorizer();
        text ??= "";
        bool animationsEnabled = BuildOrchestrator.App.App.Motion?.AnimationsEnabled ?? false;

        var type = ConsoleLineClassifier.Classify(text);
        Brush color = _palette?.ForType(type) ?? EditorControl.Foreground;

        // Önceki uçuştaki daktilo/fade bitmemişse kaybetme: hemen (instant) commit et.
        if (_typeTimer is not null || _cursorFading) FinishActiveLine(commit: true);

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
        ActiveCursor.Opacity = 1.0;
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

        // Yazım tamamlandıktan CursorHoldMs sonra: imleç fade-out'a girer (bitince commit + hide).
        if (elapsed >= _scheduler.Duration + TimeSpan.FromMilliseconds(CursorHoldMs))
            BeginCursorRemoval();
    }

    /// <summary>[3b Minor 3] İmleci HARD collapse yerine kısa bir opacity fade-out ile kaldırır ("imleç ~420ms
    /// sonra söner" bir kesme değil, bir fade olarak okunur). Reduced-motion → instant. Fade bitince satır commit
    /// edilir + overlay gizlenir.</summary>
    private void BeginCursorRemoval()
    {
        // Daktilo bitti — timer'ı durdur, blink'i durdur.
        if (_typeTimer is not null) { _typeTimer.Stop(); _typeTimer.Tick -= OnTypeTick; _typeTimer = null; }
        _typeClock?.Stop();
        _typeClock = null;
        StopBlink();

        bool animate = BuildOrchestrator.App.App.Motion?.AnimationsEnabled ?? false;
        if (!animate) { FinishActiveLine(commit: true); return; }

        _cursorFading = true;
        var fade = new DoubleAnimation(1.0, 0.0, ResolveDuration("Duration.Base", 180.0));
        fade.Completed += (_, _) => { if (_cursorFading) FinishActiveLine(commit: true); };
        ActiveCursor.BeginAnimation(OpacityProperty, fade);
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
        _cursorFading = false;
        ActiveCursor.BeginAnimation(OpacityProperty, null); // varsa uçuştaki fade'i iptal et
        ActiveCursor.Opacity = 1.0;
        ActiveLineOverlay.Visibility = Visibility.Collapsed;
        ActiveLineText.Text = "";
        string pending = _activeText;
        _activeText = "";
        _scheduler = null;
        if (commit && pending.Length > 0) AppendBatch(pending + "\n");
    }

    // [3b M-4] Aktif-satır imleci ile "build in progress" imlecinin ORTAK blink animasyonu (design-v1 §2.5:
    // 1.0→0.1, 0.55s, SineEase in/out, 30fps). Tek kaynak — iki başlatıcı bunu paylaşır (kopya yok).
    private static DoubleAnimation CreateBlinkAnimation()
    {
        var blink = new DoubleAnimation(1.0, 0.1, new Duration(TimeSpan.FromSeconds(0.55)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(blink, 30);
        return blink;
    }

    private void StartBlink() => ActiveCursor.BeginAnimation(OpacityProperty, CreateBlinkAnimation());

    private void StopBlink()
    {
        ActiveCursor.BeginAnimation(OpacityProperty, null);
        ActiveCursor.Opacity = 1.0;
    }

    // ---------------------------------------------------------------- narrative (run) modu

    /// <summary>[3b] Run/anlatı dokümanını kurar (Back akışı): kaskat/build-in-progress iptal, render dilimi
    /// (son <see cref="RenderSliceLines"/>) uygulanır, canlı append'te tail-trim AÇIK. Chunk loader kapalı.</summary>
    public void ShowRunDocument(string fullRunText)
    {
        CancelCascade();
        HideBuildInProgress();
        _projectMode = false;
        _trimTail = true;
        _projectAllLines = [];
        _loadedFrom = 0;
        EditorControl.Document = new TextDocument(ConsoleRenderSlice.LastLines(fullRunText ?? "", RenderSliceLines));
        if (StickToBottom) EditorControl.ScrollToEnd();
    }

    // ---------------------------------------------------------------- proje-log kaskatı

    /// <summary>
    /// [3b] Proje-log moduna geçişte log satırlarını kaskatla açar (26ms'de 3 satır, satır başına 140ms
    /// opacity-fade; flash yok) ve chunk loader'ı kurar. Reduced-motion iken INSTANT (tüm satırlar, fade yok).
    /// <paramref name="buildInProgress"/> iken sonda amber "build in progress ▮" belirir. Motion TAZE okunur.
    /// </summary>
    public void PlayCascade(IReadOnlyList<string> allLines, bool buildInProgress)
    {
        EnsureColorizer();
        CancelCascade();
        HideBuildInProgress();
        FinishActiveLine(commit: false); // narrative typewriter varsa temizle (mod değişimi)

        allLines ??= [];
        _projectMode = true;
        _armedForChunk = false;            // ilk layout'ta spurious prepend olmasın (kullanıcı henüz kaydırmadı)
        _trimTail = false;                 // proje modunda tail-trim yok — chunk loader eski satırları yönetir
        _projectAllLines = allLines;
        _buildInProgressPending = buildInProgress;

        // Render dilimi: son RenderSliceLines satır belgeye; öncesi chunk loader'a bırakılır.
        _loadedFrom = Math.Max(0, allLines.Count - RenderSliceLines);
        string sliceText = Join(allLines, _loadedFrom, allLines.Count);
        int sliceCount = allLines.Count - _loadedFrom;

        bool animationsEnabled = BuildOrchestrator.App.App.Motion?.AnimationsEnabled ?? false;

        var scheduler = new CascadeScheduler(sliceCount, animationsEnabled);
        if (scheduler.Instant)
        {
            EditorControl.Document = new TextDocument(sliceText);
            if (StickToBottom) EditorControl.ScrollToEnd();
            if (buildInProgress) ShowBuildInProgress(animationsEnabled: false);
            return;
        }

        _cascadeFade = new CascadeFadeTransformer(scheduler) { Elapsed = TimeSpan.Zero };
        EditorControl.TextArea.TextView.LineTransformers.Add(_cascadeFade); // colorizer'dan SONRA (rengi okur)
        EditorControl.Document = new TextDocument(sliceText);
        EditorControl.TextArea.TextView.Redraw();
        if (StickToBottom) EditorControl.ScrollToEnd();

        _cascadeClock = Stopwatch.StartNew();
        _cascadeTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        _cascadeTimer.Tick += OnCascadeTick;
        _cascadeTimer.Start();
        OnCascadeTick(this, EventArgs.Empty); // ilk kareyi hemen çiz (t=0'da tüm satırlar opacity 0 — flash yok)
    }

    private void OnCascadeTick(object? sender, EventArgs e)
    {
        if (_cascadeFade is null || _cascadeClock is null) return;
        TimeSpan elapsed = _cascadeClock.Elapsed;
        _cascadeFade.Elapsed = elapsed;
        EditorControl.TextArea.TextView.Redraw(DispatcherPriority.Render);
        if (_cascadeFade.IsComplete(elapsed))
        {
            CancelCascade(); // transformer'ı kaldırır + tam opak redraw
            // Blink dekoratif sonsuz animasyon — motion sinyalini BAŞLATMA anında TAZE oku (motion sözleşmesi).
            if (_buildInProgressPending)
                ShowBuildInProgress(BuildOrchestrator.App.App.Motion?.AnimationsEnabled ?? false);
        }
    }

    private void CancelCascade()
    {
        if (_cascadeTimer is not null) { _cascadeTimer.Stop(); _cascadeTimer.Tick -= OnCascadeTick; _cascadeTimer = null; }
        _cascadeClock?.Stop();
        _cascadeClock = null;
        if (_cascadeFade is not null)
        {
            EditorControl.TextArea.TextView.LineTransformers.Remove(_cascadeFade);
            _cascadeFade = null;
            EditorControl.TextArea.TextView.Redraw(); // kalan tam-opak render
        }
    }

    // ---------------------------------------------------------------- build in progress (amber ▮)

    private void ShowBuildInProgress(bool animationsEnabled)
    {
        BuildProgressCursor.Opacity = 1.0;
        BuildProgressOverlay.Visibility = Visibility.Visible;
        if (animationsEnabled) StartBuildBlink(); else StopBuildBlink();
    }

    private void HideBuildInProgress()
    {
        StopBuildBlink();
        BuildProgressOverlay.Visibility = Visibility.Collapsed;
    }

    private void StartBuildBlink() => BuildProgressCursor.BeginAnimation(OpacityProperty, CreateBlinkAnimation());

    private void StopBuildBlink()
    {
        BuildProgressCursor.BeginAnimation(OpacityProperty, null);
        BuildProgressCursor.Opacity = 1.0;
    }

    // ---------------------------------------------------------------- chunk loader (scroll-telafili prepend)

    private void OnScrollOffsetChanged() => EvaluateChunkScroll(EditorControl.VerticalOffset);

    /// <summary>[3b I-2] Chunk-scroll kararı. Offset dışarıdan verilir — üretimde <see cref="OnScrollOffsetChanged"/>
    /// <c>EditorControl.VerticalOffset</c> ile çağırır; böylece GERÇEK yol (arm → tepeye-scroll → prepend → re-arm)
    /// canlı bir scroll event'i olmadan test edilebilir (paralel bir kopya yol DEĞİL — üretimin çağırdığı metodun
    /// ta kendisi). Kullanıcı tepeden uzaklaşınca "arm" (ilk layout'ta offset=0 iken spurious prepend olmaz);
    /// yalnız gerçek bir tepeye-scroll önceki chunk'ı yükler.</summary>
    internal void EvaluateChunkScroll(double verticalOffset)
    {
        if (!_projectMode || _prepending) return;
        if (verticalOffset > ChunkTopThresholdPx) { _armedForChunk = true; return; }
        if (_armedForChunk && _loadedFrom > 0)
        {
            _armedForChunk = false; // prepend sonrası offset telafi edilir → tepeden uzaklaşır → yeniden arm olur
            PrependPreviousChunk();
        }
    }

    /// <summary>[Test gözlemi] Son <see cref="PrependPreviousChunk"/>'ın uyguladığı scroll-telafisi: prepend ÖNCESİ
    /// offset, eklenen dilimin piksel yüksekliği (delta) ve uygulanan yeni offset. Yalnız test okur.</summary>
    internal (double Before, double Delta, double Applied)? LastPrepend { get; private set; }

    /// <summary>Belgede yüklü ilk satırdan ÖNCEKİ ~<see cref="RenderSliceLines"/> satırı (contiguous, sequence-id
    /// bitişik → tekrar/kayıp yok) tepeye prepend eder ve <c>VerticalOffset</c>'i prepend edilen içeriğin piksel
    /// yüksekliği kadar artırır (<see cref="ChunkStitch.CompensatedOffset"/>) → viewport zıplamaz.</summary>
    internal void PrependPreviousChunk()
    {
        int from = Math.Max(0, _loadedFrom - RenderSliceLines);
        string chunk = Join(_projectAllLines, from, _loadedFrom);
        if (chunk.Length == 0) { _loadedFrom = from; return; }

        _prepending = true;
        try
        {
            var tv = EditorControl.TextArea.TextView;
            double before = EditorControl.VerticalOffset;
            int prependedLines = _loadedFrom - from;
            double delta = prependedLines * tv.DefaultLineHeight;

            var document = EditorControl.Document;
            document.BeginUpdate();
            try { document.Insert(0, chunk); }
            finally { document.EndUpdate(); }

            _loadedFrom = from;
            double applied = ChunkStitch.CompensatedOffset(before, delta);
            LastPrepend = (before, delta, applied);
            EditorControl.ScrollToVerticalOffset(applied);
        }
        finally { _prepending = false; }
    }

    // ---------------------------------------------------------------- yardımcılar

    // allLines[from..to) satırlarını '\n' SONEKLİ birleştirir (append/dikiş sözleşmesiyle uyumlu — tam satır biter).
    private static string Join(IReadOnlyList<string> lines, int from, int to)
    {
        if (from >= to) return "";
        var sb = new System.Text.StringBuilder();
        for (int i = from; i < to; i++) sb.Append(lines[i]).Append('\n');
        return sb.ToString();
    }

    // Duration.* kaynağını çözer (motion sözleşmesi: süreler token'dan); yoksa fallback ms.
    private Duration ResolveDuration(string key, double fallbackMs)
        => TryFindResource(key) is Duration d ? d : new Duration(TimeSpan.FromMilliseconds(fallbackMs));
}
