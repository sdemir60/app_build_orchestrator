using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
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
    // [A13/T4 fix-1 · A3] internal (private değil) — motion sabitinin değeri otoriteye karşı SAF assert ile
    // pinlenebilsin diye (ConsoleMotionPathTests). BuildShakeAnimation'ın internal yapıldığı A12-korumalı
    // desenin aynısı; davranış DEĞİŞMEDİ.
    // [A13/B3 · k3] Değer ARTIK burada TANIMLI DEĞİL: tek tanım TypewriterScheduler.CursorHoldMs'tedir
    // (aynı 420 üç sahipte ayrı ayrı yazılıydı, ikisi pinsizdi). Bu, derleme-zamanı alias'tır — RevealStagger.RevealMs
    // deseni; sürüklenmesi OLANAKSIZ.
    internal const double CursorHoldMs = TypewriterScheduler.CursorHoldMs;
    // [3b Minor 1/2] Off-palette hex YOK: base foreground + FontSize XAML token/resource'ından gelir.

    /// <summary>[3b/Ek A #16] Canlı append'te belgede tutulan azami satır (render dilimi). "N lines" sayacı bundan
    /// ETKİLENMEZ — tam mantıksal sayacı VM taşır (render dilimi DEĞİL, Ek A #23).</summary>
    public const int RenderSliceLines = ConsoleRenderSlice.DefaultMaxLines; // 200
    // Kullanıcı tepeye ne kadar yaklaşınca önceki chunk yüklenir (px) — bottom-stick eşiğiyle uyumlu (48px).
    private const double ChunkTopThresholdPx = 48.0;

    private ConsoleColorizer? _colorizer;
    private ConsolePalette? _palette;

    /// <summary>[A13/T1] Motion sinyalinin TAZE okunduğu kapı + CANLI aboneliği — depo <see cref="MotionGate"/>
    /// (kardeş sahiplerin deseni: <see cref="Views.EventStreamView"/>/<see cref="Views.ProjectRow"/>/
    /// <see cref="Graph.GraphView"/>). <b>latch'siz abonelikli kip</b> (<c>new MotionGate(this)</c>) —
    /// <see cref="Controls.StickyLayerList"/>'in aboneliksiz kipi burada YANLIŞ olurdu: o sahip sonsuz saat
    /// TUTMAZ, bu görünüm ise <c>RepeatBehavior.Forever</c> blink saatleri başlatır (<see cref="StartBlink"/>,
    /// <see cref="StartBuildBlink"/>).
    ///
    /// <para><b>Neden seam gerekliydi (1.8/1.9):</b> bu görünüm motion sinyalini statik
    /// <see cref="MotionGate.StaticAnimationsEnabled"/> üzerinden DOĞRUDAN okuyordu; headless'ta <c>App.Motion</c>
    /// null olduğundan üretim append yolunun (<see cref="AppendNarrativeBatch"/>) daktilo kolu HİÇ
    /// koşturulamıyordu. Enjeksiyon yokken varsayılan provider aynı statik ifadedir — okuma davranışı AYNI.</para>
    ///
    /// <para><b>Neden CANLI abonelik gerekliydi (fix-1 · I-D):</b> seam'in ilk hâli aboneliksizdi ve bu gerçek
    /// bir kapsam boşluğu bırakıyordu: OS "animasyon efektleri" ayarı koşu SIRASINDA kapanırsa konsol imleci
    /// SONSUZA DEK dönmeye devam ederdi (kardeşi <see cref="Views.EventStreamView"/> bunu <c>:70-71</c>'de
    /// açıkça kapatmış). Artık <see cref="OnMotionChanged"/> sinyali izler.</para></summary>
    private readonly MotionGate _motion;

    // [T59] Alta-yapışık + `⌄ latest` pill — StickToBottom'ın TEK gerçek kaynağı (bkz. StickToBottom get/set altta).
    private readonly BottomAnchorBehavior _bottomAnchor;
    private double _lastExtentHeight; // AvalonEdit ScrollChanged extent-delta VERMEZ — burada elle izlenir (T59)

    // Aktif-satır daktilosu durumu (yalnız UI thread'inde dokunulur).
    private DispatcherTimer? _typeTimer;
    private Stopwatch? _typeClock;
    private TypewriterScheduler? _scheduler;
    private string _activeText = "";
    private bool _cursorFading; // imleç fade-out animasyonu uçuşta mı (Minor 3)

    // [D4/T34] Anlatı en-yeni-satır daktilo degradation kararı (drop-to-latest / throughput-suspend / hata /
    // ham-MSBuild) — SAF çekirdek, yalnız UI thread'inden çağrılır (AppendNarrativeBatch).
    private readonly ConsoleTypingGate _typingGate = new();
    // [D4/T56-UI] Boşta (idle/boot) "ready" (dim) satırı overlay'de gösteriliyor mu — doküman satırı DEĞİL.
    private bool _idleReady;

    /// <summary>[A13/T3 fix-2 · 5] Idle "ready" satırının duvar-saati kaynağı. Sahibi (<c>MainWindow</c>) bunu
    /// ctor'da <see cref="ViewModels.RunViewModel.WallClock"/>'a bağlar — konsol anlatısı, event stream ve idle
    /// satırı böylece TEK saatten beslenir (kardeş seam deseni: <see cref="AnimationsEnabledProvider"/>).
    /// Bağ <b>indirection</b>'dır (değer kopyası DEĞİL): VM'in saati sonradan değişirse idle satırı da izler.
    /// Enjeksiyon yokken üretimdeki varsayılanla aynı ifadedir — davranış-nötr.</summary>
    internal Func<DateTimeOffset> WallClock { get; set; } = () => DateTimeOffset.Now;

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
        // [A13/T1 fix-1 · I-D] EventStreamView.ctor deseni birebir: gate + Changed aboneliği InitializeComponent'ten ÖNCE.
        _motion = new MotionGate(this);
        _motion.Changed += OnMotionChanged;
        InitializeComponent();
        // Gömülü Geist Mono Console CompositeFont'u (It-0 asset'i) — pack URI burada TEKRARLANMAZ [T64].
        EditorControl.FontFamily = AppFonts.MonoConsole;
        ActiveLineText.FontFamily = AppFonts.MonoConsole;
        BuildProgressText.FontFamily = AppFonts.MonoConsole;
        Loaded += (_, _) => EnsureColorizer();
        EditorControl.TextArea.TextView.ScrollOffsetChanged += (_, _) => OnScrollOffsetChanged();
        // [T59] Kullanıcı tekerleği çevirdiği anda uçuştaki pill-jump animasyonu iptal olur + suppress bayrağı kalkar.
        ScrollAnimator.EnableUserCancellation(EditorControl);
        // Yatay tekerlek/touchpad: WPF WM_MOUSEHWHEEL'i HİÇ dağıtmaz, bu yüzden yatay kaydırma uygulamanın kendi
        // kancasından geçer. Konsol bunu Enable eden TEK panel: yatay taşması olan tek yüzey odur (WordWrap=False).
        HorizontalWheelScroll.Enable(this);
        _bottomAnchor = new BottomAnchorBehavior(
            getOffset: () => EditorControl.VerticalOffset,
            getExtent: () => EditorControl.ExtentHeight,
            getViewport: () => EditorControl.ViewportHeight,
            scrollInstant: v => EditorControl.ScrollToVerticalOffset(v),
            scrollSmooth: AnimateToBottom);
        _bottomAnchor.Changed += OnBottomAnchorChanged;
        // [A13/T5] Pill'in adı host'tan gelir (hangi akışın sonu — bkz. LatestPill.AccessibleName).
        Pill.AccessibleName = AccessibilityNames.LatestConsole;
        // [A13/T1 fix-1 · I-D] EventStreamView.ctor:97 deseni: unload'da SONSUZ blink saatleri bırakılır (aksi
        // halde ağaçtan çıkmış bir görünümün iki clock'u timing engine'de 30fps'te uyanık kalırdı). Uçuştaki
        // daktilo/kaskat BURADA commit EDİLMEZ: commit doküman yazan bir DAVRANIŞTIR ve unload'da yeni bir
        // satır üretmek bugünkü sözleşmeyi değiştirirdi (mod değişimi yollarının kendi commit/iptal kararları var).
        Unloaded += (_, _) => { StopBlink(); StopBuildBlink(); };
    }

    /// <summary>[A13/T1 fix-1 · I-D] Motion sinyali koşu SIRASINDA değişince görünüm uyar
    /// (<see cref="Views.EventStreamView.OnMotionChanged"/> sözleşmesiyle aynı): SONSUZ saatler (aktif/ready
    /// satır imleci + "build in progress" imleci) yalnız GÖRÜNÜR olduklarında yeniden değerlendirilir.
    ///
    /// <para>Bir kereye mahsus efektler (daktilo, kaskat) burada YENİDEN OYNATILMAZ — sinyal sonradan açılınca
    /// geriye dönük animasyon başlatmak sözleşme ihlali olurdu (kardeşindeki <c>TypePlayed</c> guard'ının
    /// tek-yönlülüğüyle aynı gerekçe).</para>
    ///
    /// <para><b>[A13/final · lensA Ö1] İLK İŞ DOKÜMANA YAZMAKTIR:</b> uçuştaki bir imleç fade'i varsa
    /// (<c>_cursorFading</c>) aktif satır önce <c>FinishActiveLine(commit: true)</c> ile kapatılır. Bu, saatleri
    /// yeniden değerlendirmekten farklı bir iştir — gerekçesi gövdedeki yorumdadır.</para></summary>
    private void OnMotionChanged(object? sender, EventArgs e)
    {
        // [A13/final · lensA Ö1] UÇUŞTAKİ İMLEÇ FADE'İ ÖNCE KURALLI YOLDAN KAPATILIR. Aşağıdaki ilk dal
        // ActiveCursor'ın Opacity'sinde BeginAnimation çağırır (StartBlink kurar / StopBlink söker) — bu,
        // BeginCursorRemoval'ın başlattığı fade clock'unu SÖKER ve WPF sökülen bir clock'un Completed'ını
        // ATEŞLEMEZ. Satırın dokümana yazılması YALNIZ o Completed'a bağlı olduğundan (bkz. BeginCursorRemoval),
        // bayrak temizlenmezse _cursorFading true ASILI kalır ve satır dokümana HİÇ girmez; sonraki append'lerin
        // guard'ı (`_typeTimer is not null || _cursorFading`) ancak YENİ bir satır gelirse onarır — run'ın SON
        // satırında kayıp KALICIDIR. FinishActiveLine aynı iptali yaparken bayrağı zaten elle temizler; kurallı
        // kapanış yolu odur, burada da o kullanılır.
        //
        // Erken `return` YOK (lens A'nın önerdiği biçimden bilinçli sapma): BELİRLEYİCİ GEREKÇE, FinishActiveLine'ın
        // overlay'i zaten Collapsed yapması — yani aşağıdaki ilk dal kendiliğinden koşmaz, erken dönüşün kazancı
        // yoktur. İkinci gerekçe (erken dönüş BuildProgressOverlay'in sinyal güncellemesini atlardı) BUGÜN
        // ERİŞİLEMEZ bir duruma dayanır: _cursorFading yalnız ActiveProjectId null iken (Route.Narrative) kurulabilir,
        // BuildProgressOverlay ise yalnız proje seçiliyken görünür ve PlayCascade geçişte bayrağı zaten temizler.
        // Yine de `return` eklenmez — sözleşme değişirse sessizce kırılacak bir kısayol olurdu.
        if (_cursorFading) FinishActiveLine(commit: true);

        if (ActiveLineOverlay.Visibility == Visibility.Visible)
        {
            if (_motion.Enabled) StartBlink(); else StopBlink();
        }
        if (BuildProgressOverlay.Visibility == Visibility.Visible)
        {
            if (_motion.Enabled) StartBuildBlink(); else StopBuildBlink();
        }
    }

    /// <summary>[E4/T48] Konsolun bottom-anchor'ının merkezi arbiter'a bölgesel suppress bildirimi + pill görünürlüğü.
    /// Dibe yapışıksa arbiter'da bu panel yeniden devrede (<see cref="ScrollArbiter.Resume"/>); kullanıcı dipten
    /// uzaklaşınca duraklı (<see cref="ScrollArbiter.NotifyUserScroll"/>) — YALNIZ konsol paneli (stream/frontier
    /// akmaya devam). <see cref="Arbiter"/> null ise (izole test) yalnız pill güncellenir.</summary>
    private void OnBottomAnchorChanged(object? sender, EventArgs e)
    {
        Pill.Visibility = _bottomAnchor.ShowPill ? Visibility.Visible : Visibility.Collapsed;
        if (Arbiter is null) return;
        if (_bottomAnchor.IsStuck) Arbiter.Resume(ScrollPanel.Console);
        else Arbiter.NotifyUserScroll(ScrollPanel.Console);
    }

    /// <summary>[E4/T48] Üç panelin auto-scroll'unu hakem eden merkezi arbiter; null ise izole (bildirimler no-op).
    /// MainWindow enjekte eder.</summary>
    public ScrollArbiter? Arbiter { get; set; }

    /// <summary>[A13/T1 · ProjectRow/GraphView/EventStreamView deseni · D8] Motion sinyalinin TAZE okunduğu kapı —
    /// sınıf statik <c>App.Motion</c>'a doğrudan bağlanmaz; testler gerçek bir daktilo/kaskat/blink saatini
    /// sürebilmek için bunu <c>() =&gt; true</c> ile enjekte eder (headless'ta <c>App.Motion</c> null → reduced).</summary>
    public Func<bool> AnimationsEnabledProvider
    {
        get => _motion.AnimationsEnabledProvider;
        set => _motion.AnimationsEnabledProvider = value;
    }

    /// <summary>[A13/T1 fix-1 · I-D] <c>AnimationsEnabledChanged</c>'e abone olunacak kaynak; null ise
    /// <c>App.Motion</c> (<see cref="Views.EventStreamView.MotionSettings"/> deseni).</summary>
    public IMotionSettings? MotionSettings
    {
        get => _motion.MotionSettings;
        set => _motion.MotionSettings = value;
    }

    /// <summary>Test/host erişimi için altındaki AvalonEdit kontrolü.</summary>
    public TextEditor Editor => EditorControl;

    /// <summary>Task 12'nin run/proje görünümü arasında doküman değiştirebilmesi için dışa açılır.</summary>
    public TextDocument Document
    {
        get => EditorControl.Document;
        set => EditorControl.Document = value;
    }

    /// <summary>
    /// true iken her <see cref="AppendBatch"/> sonrası en alta kaydırılır (varsayılan true).
    ///
    /// <para><b>[T59] Reconciliation:</b> ARTIK <see cref="BottomAnchorBehavior.IsStuck"/>'ın ince bir geçişidir —
    /// TEK bottom-anchor mekanizması <see cref="_bottomAnchor"/>'dır (görev talimatı: "çift iş yok"). Public API
    /// AYNI kaldı (3b testleri elle <c>StickToBottom = false</c> atar, bu hâlâ çalışır — <see cref="BottomAnchorBehavior.ForceStuck"/>
    /// bir doğrudan override'dır). YENİ olan: gerçek uygulamada artık <see cref="OnScrollOffsetChanged"/> her scroll
    /// olayında 48px eşiğine göre bunu OTOMATİK de günceller — önceden (Task 3b'de) hiçbir mekanizma bunu yapmıyordu
    /// (MainWindow hiçbir yerde StickToBottom atamıyordu, konsol pratikte kalıcı yapışıktı). Headless testlerde
    /// gerçek layout/scroll geometrisi oluşmadığından (bkz. ScrollOffsetChanged'in AvalonEdit'te layout gerektirmesi)
    /// bu otomatik yol tetiklenmez — mevcut 3b testleri ETKİLENMEZ.</para>
    /// </summary>
    public bool StickToBottom
    {
        get => _bottomAnchor.IsStuck;
        set => _bottomAnchor.ForceStuck(value);
    }

    /// <summary>
    /// UI thread'inde çağrılır. TEK batch ekler — asla satır satır bölmez, asla <c>Dispatcher.Invoke</c>
    /// çağırmaz (çağıranın/Task 12'nin sorumluluğu). [A13.2 ZORUNLU sıra]. [3b] Run modunda belge daima son
    /// <see cref="RenderSliceLines"/> satırla sınırlanır (baştan kırpma). Proje modunda YALNIZ alta-yapışıkken
    /// (follow) aynı tail-trim uygulanır (chatty build belgeyi sınırsız büyütmesin, §3.6); kırpılan her satır için
    /// <see cref="_loadedFrom"/> ilerletilir ki chunk loader index'i belgenin gerçek ilk satırıyla TUTARLI kalsın
    /// [C-1]. Kırpılan satırlar <see cref="_projectAllLines"/>'ta durur → tepeye kaydırınca prepend onları DELİKSİZ
    /// geri yükler. Kullanıcı yukarı kaydırıp chunk gezerken (follow kapalı) trim YOK — prepend'le çakışmaz.
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
            if (_trimTail || (_projectMode && StickToBottom))
            {
                int trimmed = TrimToRenderSlice(document);
                // [C-1] Tepeden K satır kırpıldıysa belgenin ilk satırı _projectAllLines[_loadedFrom + K] olur.
                // Chunk loader index'ini K kadar ilerlet (aksi halde stale _loadedFrom → sonraki scroll-to-top
                // prepend'i YANLIŞ dilimi yükler, kırpılan satırlar KALICI kaybolur = delik). Yalnız proje modunda:
                // run modunun (_trimTail) chunk bookkeeping'i yoktur (_projectAllLines boş, _loadedFrom=0). Üst sınır
                // _projectAllLines.Count: tüm backlog kırpılınca belgenin ilk satırı live bir satırdır (index'i yok).
                if (_projectMode) _loadedFrom = Math.Min(_loadedFrom + trimmed, _projectAllLines.Count);
            }
        }
        finally
        {
            document.EndUpdate();
        }
        if (StickToBottom)
            EditorControl.ScrollToEnd();
    }

    // Belgeyi son RenderSliceLines satıra kırpar (baştaki fazla satırları TEK Remove ile siler).
    // Tepeden silinen satır sayısını döndürür ([C-1] proje modunda _loadedFrom'u ilerletmek için).
    private static int TrimToRenderSlice(TextDocument document)
    {
        int excess = document.LineCount - RenderSliceLines;
        if (excess <= 0) return 0;
        var lastToRemove = document.GetLineByNumber(excess); // 1..excess satırlarını (ayraçlarıyla) sil
        document.Remove(0, lastToRemove.Offset + lastToRemove.TotalLength);
        return excess;
    }

    // ---------------------------------------------------------------- colorizer

    /// <summary>Loaded'da colorizer'ı bir KEZ kurar. Kaynak yoksa (headless) sessizce atlar.</summary>
    private void EnsureColorizer()
    {
        if (_colorizer is not null) return;
        object? Probe(string key) => TryFindResource(key);
        if (Probe("Brush.TextFaint") is null) return; // token'lar henüz yok — üretimde Loaded'da hazırdır
        // [B1→D4 fold] Konsol puntosu TEK kaynaktır (design-v1 §2.5 "mono 12px" = FontSize.Xs token'ı). XAML'deki
        // DynamicResource FontSize.Xs bir anahtar typo'sunda WPF'in SESSİZ 12px varsayılanına düşerdi (token da 12
        // → hata GÖRÜNMEZ). ConsolePalette.FromLookup ile AYNI fail-fast: token'lar merge edilmişken anahtar YOKSA
        // anlaşılır bir hata fırlatılır (sessiz drift yerine) ve punto koda TEK yerden bağlanır (editör + overlay'ler drift edemez).
        double fontSize = Probe("FontSize.Xs") as double?
            ?? throw new InvalidOperationException("Console: font-size resource 'FontSize.Xs' was not found (Tokens.xaml).");
        EditorControl.FontSize = fontSize;
        ActiveLineText.FontSize = fontSize;
        BuildProgressText.FontSize = fontSize;
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

    // ---------------------------------------------------------------- [D4] anlatı batch'i (en yeni satır daktilo)

    /// <summary>
    /// [D4/T56-UI + T34] Run/anlatı modu batch'i. Batch'in TÜM satırları anında commit edilir; YALNIZ EN YENİ
    /// satır — degradation kuralları izin verirse (<see cref="ConsoleTypingGate"/>) — hibrit daktiloyla yazılır
    /// (<see cref="TypeActiveLine"/>). Aksi halde (ham MSBuild / hata / fırtına / yüksek throughput / reduced-motion)
    /// tüm batch <see cref="AppendBatch"/> ile instant basılır. Proje-log modu BU yoldan GEÇMEZ — MainWindow orada
    /// doğrudan <see cref="AppendBatch"/> çağırır (ham çıktı asla daktilolanmaz — DD2). Batch sözleşmesi: metin
    /// '\n' SONEKLİ tam satırlarla biter (<see cref="ConsoleBatcher"/>).
    /// </summary>
    public void AppendNarrativeBatch(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        HideReadyIfShown();
        EnsureColorizer();

        // Son tam satırı ayır: newest = son satır (soneksiz); prefix = ondan önceki her şey ('\n' sonekli).
        string body = text[^1] == '\n' ? text[..^1] : text;
        int lastNl = body.LastIndexOf('\n');
        string newest = lastNl < 0 ? body : body[(lastNl + 1)..];
        string prefix = lastNl < 0 ? "" : text[..(lastNl + 1)];
        int lineCount = 1;
        for (int i = 0; i < body.Length; i++) if (body[i] == '\n') lineCount++;

        var type = ConsoleLineClassifier.Classify(newest);
        // Ham MSBuild anlatı değildir → zaman damgası (HH:MM:SS öneki) YOKtur. Anlatı satırları AppendRunLine'da
        // damgalanır; damga yoksa satır ham sayılır ve ASLA daktilolanmaz (T34).
        bool isRaw = ConsoleLineParser.Layout(newest).Clock is null;

        if (_typingGate.ShouldType(isRaw, type, lineCount, Environment.TickCount64))
        {
            if (prefix.Length > 0) AppendBatch(prefix); // en yeniden önceki satırlar anında
            TypeActiveLine(newest);                     // en yeni satır hibrit daktiloyla
        }
        else
        {
            if (_typeTimer is not null || _cursorFading) FinishActiveLine(commit: true); // uçuştaki daktilo varsa kapat
            AppendBatch(text); // tüm batch instant
        }
    }

    /// <summary>[D4] Uçuştaki anlatı daktilosunu (varsa) anında commit eder — yoksa no-op. Mod değişiminde
    /// (proje-loguna geçiş) çağrılabilir; <see cref="PlayCascade"/> zaten <c>FinishActiveLine(commit:false)</c> ile
    /// overlay'i temizler, bu daktiloyu KAYBETMEDEN commit etmek isteyen yol için ayrı tutulur.</summary>
    public void CommitActiveLine()
    {
        if (_typeTimer is not null || _cursorFading) FinishActiveLine(commit: true);
    }

    /// <summary>[D4/T56-UI] Boşta (idle/boot) tek satır: design-v1 §2.5 BİREBİR <c>12:04:07 ▮ ready</c> (dim) —
    /// duvar-saati damgası + yanıp sönen blok imleç + metin, BU SIRAYLA. Doküman satırı DEĞİLdir — overlay'de
    /// canlı gösterilir; içerik gelince (AppendNarrativeBatch / PlayCascade) temizlenir. Uçuştaki daktilo varsa
    /// önce commit edilir. Reduced-motion iken imleç statiktir.
    ///
    /// <para>[A13/T3 fix-1 · P3] Damga eskiden HİÇ YOKTU (yalnız <c>"ready"</c> basılıyordu) — otoritede
    /// (<c>BuildApp.jsx:607</c>) satır <c>&lt;NarrLine type="dim" time={eng.wall()} cursor&gt;</c>'dur, yani
    /// damga AÇIKÇA taşınır ve <c>NarrLine</c> (<c>:158-160</c>) onu imleç kolonundan ÖNCE basar.</para>
    ///
    /// <para>[A13/T3 fix-2 · 5] Saat ARTIK parametre DEĞİL, <see cref="WallClock"/> seam'idir: sahibi
    /// (<c>MainWindow</c>) onu ctor'da BİR KEZ VM'in saatine bağlar. Parametreli hâlde kablo iki ayrı çağrı
    /// yerinde tekrarlanıyordu ve <b>hiçbiri headless olarak sürülemiyordu</b> — <c>ShowRunConsole</c>'un idle
    /// dalı yalnız run anlatısı BOŞken koşar, oysa testte motor hiç başlamadığı için proje seçimi
    /// <c>LoadProjectLogAsync</c>'te <c>[error] could not load project log</c> satırını run belgesine düşürüp
    /// o kapıyı kapatıyor. Seam ctor'a taşındığında kablo doğrudan pinlenebilir hâle gelir
    /// (<c>MainWindowRealizeTests</c>).</para></summary>
    public void ShowReady()
    {
        var now = WallClock();
        EnsureColorizer();
        if (_typeTimer is not null || _cursorFading) FinishActiveLine(commit: true);
        _idleReady = true;
        Brush dim = _palette?.Dim ?? EditorControl.Foreground;
        LayoutActiveLine(idleReadyOrder: true);
        ActiveLineTime.Foreground = dim; // README §2.5: idle satır UÇTAN UCA dim
        ActiveLineTime.Text = WallClockFormat.Of(now);
        ActiveLineText.Foreground = dim;
        ActiveLineText.Text = ConsoleEmptyState.Idle; // "ready"
        ActiveCursor.Fill = dim;
        ActiveCursor.Opacity = 1.0;
        ActiveLineOverlay.Visibility = Visibility.Visible;
        if (_motion.Enabled) StartBlink(); else StopBlink(); // [A13/T1] motion sinyalinin TEK kapısı (MotionGate seam'i)
    }

    /// <summary>[A13/T3 fix-1 · P3] Aktif-satır overlay'inin kolon düzeni.
    ///
    /// <para><b>idle "ready"</b>: damga → imleç → metin. Bu OTORİTEDİR: <c>BuildApp.jsx:607</c> satırı
    /// <c>&lt;NarrLine type="dim" time={eng.wall()} cursor&gt;</c> kurar ve <c>NarrLine</c> (<c>:158-160</c>)
    /// <c>{time}</c> → 10px imleç kolonu → <c>{children}</c> sırasıyla basar.</para>
    ///
    /// <para><b>Daktilo</b>: metin → imleç (damga metnin içindedir, colorizer boyar).
    /// <b>UYARI — bu otoriteden KAYITSIZ bir sapmadır:</b> <c>NarrLine</c> imleci daktilo satırında da AYNI
    /// sabit 10px ikon kolonunda, metinden ÖNCE tutar (<c>cursor={l.id === typingLive}</c>, <c>:600</c>).
    /// Sapmanın kaydı YOKTUR: plan v7 <b>A13.2</b> (<c>:315-329</c>) yalnız "AvalonEdit + hibrit aktif satır"
    /// der, imleç KONUMUNDAN hiç söz etmez; <c>design-wpf-feasibility-analysis.md</c> de yalnız imlecin bir
    /// karakter değil <see cref="System.Windows.Shapes.Rectangle"/> olduğunu söyler (<c>:62</c>). Yani bu
    /// düzen <b>açık bir borçtur</b>, kayıtlı bir istisna DEĞİL — "A13.2 kararı" diye etiketlenmesi yanlıştı
    /// (fix-2 · 2). Davranış bilinçli olarak DEĞİŞTİRİLMEDİ (T3 fix-1 kapsamı yalnız idle satırıydı) ve
    /// <c>ConsoleViewTests</c>'te karakterizasyon olarak pinlidir: düzeltilirse o test kırmızıya döner.</para>
    /// </summary>
    private void LayoutActiveLine(bool idleReadyOrder)
    {
        Grid.SetColumn(ActiveCursor, idleReadyOrder ? 1 : 2);
        Grid.SetColumn(ActiveLineText, idleReadyOrder ? 2 : 1);
        ActiveCursor.Margin = idleReadyOrder ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
        ActiveLineTime.Visibility = idleReadyOrder ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideReadyIfShown()
    {
        if (!_idleReady) return;
        _idleReady = false;
        StopBlink();
        ActiveLineOverlay.Visibility = Visibility.Collapsed;
        ActiveLineText.Text = "";
        LayoutActiveLine(idleReadyOrder: false); // damga gizlenir — sonraki daktilo satırı kendi düzeninde açılır
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
        bool animationsEnabled = _motion.Enabled; // [A13/T1] motion sinyalinin TEK kapısı (MotionGate seam'i)

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
        LayoutActiveLine(idleReadyOrder: false); // idle "ready"den geliniyorsa damga kalkar, imleç metnin ARDINA döner
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

        bool animate = _motion.Enabled; // [A13/T1] motion sinyalinin TEK kapısı (MotionGate seam'i)
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

    // [3b M-4 · D3 §3] Aktif-satır imleci ile "build in progress" imlecinin ORTAK blink animasyonu — artık
    // EventStreamView'ın imleci de dahil ÜÇ başlatıcı MotionTokens.CreateBlinkAnimation'ı paylaşır (kopya YASAK).
    private void StartBlink() => ActiveCursor.BeginAnimation(OpacityProperty, MotionTokens.CreateBlinkAnimation());

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
        // [T59] design-v1 §2.5: "konsol↔proje-log geçişinde dibe sabitlenir" — kullanıcı önceki modda serbest
        // kaydırmış olsa bile mod değişimi HER ZAMAN yeniden yapıştırır (aşağıdaki `if (StickToBottom)` artık true okur).
        _bottomAnchor.ForceStuck(true);
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
        // [T59] design-v1 §2.5: "konsol↔proje-log geçişinde dibe sabitlenir" — bkz. ShowRunDocument'taki aynı gerekçe.
        _bottomAnchor.ForceStuck(true);
        HideReadyIfShown();              // [D4] boşta "ready" gösteriliyorsa temizle (proje-loguna geçiş)
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

        bool animationsEnabled = _motion.Enabled; // [A13/T1] motion sinyalinin TEK kapısı (MotionGate seam'i)

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
                ShowBuildInProgress(_motion.Enabled); // [A13/T1] motion sinyalinin TEK kapısı (MotionGate seam'i)
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

    private void StartBuildBlink() => BuildProgressCursor.BeginAnimation(OpacityProperty, MotionTokens.CreateBlinkAnimation());

    private void StopBuildBlink()
    {
        BuildProgressCursor.BeginAnimation(OpacityProperty, null);
        BuildProgressCursor.Opacity = 1.0;
    }

    // ---------------------------------------------------------------- chunk loader (scroll-telafili prepend)

    /// <summary>[I-1 test gözlemi] AvalonEdit'in <c>TextView.ScrollOffsetChanged</c>'ine bağlı GERÇEK handler —
    /// üretimde ctor'da <c>EditorControl.TextArea.TextView.ScrollOffsetChanged += (_, _) => OnScrollOffsetChanged();</c>
    /// ile kablanır. <see cref="EvaluateChunkScroll"/> ile AYNI gerekçeyle internal: testler canlı bir scroll
    /// event'i (AvalonEdit'in layout-bağımlı, headless'ta güvenilmez zamanlamalı) beklemeden ÜRETİMİN ÇAĞIRDIĞI
    /// metodun ta kendisini doğrudan tetikleyebilsin (paralel bir kopya yol DEĞİL).</summary>
    internal void OnScrollOffsetChanged()
    {
        // [I-1 fix] Bottom-anchor'ın IsStuck YENİDEN-HESABI, chunk loader'dan (EvaluateChunkScroll) ÖNCE çalışır.
        // Neden sıra kritik: bir prepend (aşağıda) belgeyi TEPEDE büyütür ve VerticalOffset'i ChunkStitch.
        // CompensatedOffset'e telafi eder — ExtentHeight bu satırdan SONRA okunsaydı, prepend'in kendi
        // büyümesini "dipte içerik büyüdü" sanıp (extentChange>0, stale IsStuck=true) kullanıcıyı dibe
        // YANKLARDI (CompensatedOffset'i ezerdi). Burada ÖNCE çalıştırmak, henüz-prepend-edilmemiş offset/extent
        // ile IsStuck'ı taze tutar (tepeye scroll → IsStuck=false); prepend'in extent artışı SONRAKİ olaya
        // sarkarsa bile o an IsStuck zaten false olduğundan içerik-büyümesi yakalaması tetiklenmez.
        //
        // [T59] AvalonEdit'in ScrollOffsetChanged'i WPF ScrollViewer.ScrollChanged.ExtentHeightChange gibi bir delta
        // VERMEZ — burada elle izlenir (BottomAnchorDecision içerik-büyümesi/kullanıcı-scroll ayrımı için bunu ister).
        double extent = EditorControl.ExtentHeight;
        double extentChange = extent - _lastExtentHeight;
        _lastExtentHeight = extent;
        _bottomAnchor.OnScrollChanged(extentChange);

        EvaluateChunkScroll(EditorControl.VerticalOffset); // [3b, DEĞİŞMEDİ] chunk loader — üstteki eşik ayrı kavram
    }

    // [T59] Pill tıklaması → yumuşak (reduced-motion'da anında) dibe.
    private void OnPillClick(object sender, RoutedEventArgs e) => _bottomAnchor.JumpToBottom();

    // [T59] BottomAnchorBehavior'ın "scrollSmooth" delege'i. [M-1] Ortak desen (taze AnimationsEnabled + Duration.Slow
    // + KeySpline.EaseInOut + ScrollAnimator.AnimateTo) StickyLayerList.AnimateScrollTo ile PAYLAŞILIR —
    // MotionTokens.AnimateSlowEaseInOut'a çıkarıldı (kopya YASAK, CLAUDE.md); ayrı host'lar (TextEditor/ScrollViewer)
    // ScrollAnimator'ın ortak UIElement/ScrollToVerticalOffset çekirdeğinden geçer.
    private bool AnimateToBottom(double target) =>
        MotionTokens.AnimateSlowEaseInOut(this, EditorControl, EditorControl.VerticalOffset, target);

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

    /// <summary>[E3/T36 reduced-motion kapsama] İdle "ready" / aktif-satır imleci — blink'in DURDUĞUNU
    /// (<c>HasAnimatedProperties==false</c>) reduced-motion'da doğrulamak için.</summary>
    internal System.Windows.UIElement ActiveCursorGlyph => ActiveCursor;

    /// <summary>[A13/T1 fix-1 · I-C · <see cref="Views.EventStreamView.ActiveLineInstant"/> ikizi] En yeni satır
    /// için SON kurulan daktilo zamanlayıcısı instant mı — yani üretim append yolu satırı harf harf mi yazıyor,
    /// yoksa tek hamlede mi bastı. Hiç kurulmadıysa (satır instant basıldı / overlay hiç açılmadı) <c>true</c>
    /// varsayılır.
    ///
    /// <para><b>Neden seam:</b> "daktilo GERÇEKTEN koştu" iddiasını ara-kare avlayarak (belirli bir zaman
    /// penceresinde <c>0 &lt; len &lt; full</c> yakalamaya çalışarak) kanıtlamak yük-hassastır: daktilo
    /// <c>DispatcherPriority.Render</c>'da, örnekleyen <see cref="System.Windows.Threading.DispatcherTimer"/> ise
    /// daha DÜŞÜK önceliktedir → yük altında kare kaçabilir ve test teşhissiz kırmızı verir (D8: yeni flake
    /// KABUL EDİLEMEZ). Bu seam aynı iddiayı deterministik kılar.</para></summary>
    internal bool ActiveLineInstant => _scheduler?.Instant ?? true;

    /// <summary>[A13/final · lensA Ö1 + lensB Ö1] Uçuştaki imleç fade-out'u — <see cref="BeginCursorRemoval"/> ile
    /// <c>true</c> olur, <see cref="FinishActiveLine"/> ile <c>false</c>. <see cref="ActiveLineInstant"/> ile AYNI
    /// gerekçeyle salt-okunur seam: bu durum DIŞARIDAN ayırt edilemiyordu (hem hold hem fade sırasında overlay açık,
    /// imleç animasyonlu) — dolayısıyla (a) hold'un GERÇEKTEN tüketildiği ve (b) sinyal fade uçuştayken değişince
    /// satırın commit edildiği iddiaları zamandan bağımsız olarak kurulamıyordu.</summary>
    internal bool CursorFading => _cursorFading;

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
    // [T59] Controls.MotionTokens'a taşındı (ScrollAnimator/BottomAnchor/FollowScroll/LatestPill AYNI ihtiyacı
    // duyar) — kopya YASAK; davranış DEĞİŞMEDİ (aynı TryFindResource + aynı fallback deseni).
    private Duration ResolveDuration(string key, double fallbackMs) => MotionTokens.ResolveDuration(this, key, fallbackMs);
}
