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
/// <item><b>Prompt satırı</b>: overlay'de yanıp sönen blok imleç (+ boşta "ready"); daktilo YOKTUR
/// imleç 7×13px Rectangle (1.1s blink); yazımdan ~420ms sonra <b>fade-out</b> ile söner (3b Minor 3 — hard cut değil).</item>
/// <item><b>Tilt-in</b> (<see cref="PlayCascade"/> / <see cref="ShowRunDocument"/>): panel geçişinde içerik
/// TEK PARÇA olarak aşağı serilir (340ms) — satır sayısından bağımsız, iki yönde de aynı.</item>
/// <item><b>Chunk loader</b>: proje logu render dilimi (son 200) gösterilir; tepeye kaydırınca önceki chunk
/// scroll-telafili prepend edilir (<see cref="ChunkStitch"/>).</item>
/// <item><b>Render dilimi</b>: canlı append'te belge son <see cref="RenderSliceLines"/> satırla sınırlıdır (Ek A #16).</item>
/// </list>
/// </summary>
public partial class ConsoleView : UserControl
{
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

    // [D4/T56-UI] Boşta (idle/boot) "ready" (dim) satırı overlay'de gösteriliyor mu — doküman satırı DEĞİL.
    private bool _idleReady;
    private bool _blinking; // imleç blink saati dönüyor mu (yeniden başlatma guard'ı)


    // Kaskat durumu (yalnız UI thread'inde).
    // [design v1.7.0 §2.5] Panel geçişinin tek parça "tilt in" ölçüleri (prototip: 14px + 340ms + rotateX 7°).
    private const double TiltInMs = 340.0;
    private const double TiltInOffsetPx = 14.0;
    // Prototipin WPF eşlemesi (animasyon spec §2.4): perspective(900px) rotateX(7°) → alt kenara sabitlenmiş
    // ScaleY 0.965. Değer spec'ten alınmıştır, "iyileştirilmez".
    private const double TiltInScaleFrom = 0.965;
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
        // [design v1.7.0 §1.2] Konsol gövdesi 300 (Light) — editör, prompt satırı ve "build in progress"
        // işaretçisi AYNI token'ı okur (drift edemez).
        EditorControl.SetResourceReference(FontWeightProperty, "FontWeight.Console");
        ActiveLineText.SetResourceReference(FontWeightProperty, "FontWeight.Console");
        BuildProgressText.SetResourceReference(FontWeightProperty, "FontWeight.Console");
        Loaded += (_, _) => { EnsureColorizer(); PositionPrompt(); };
        EditorControl.TextArea.TextView.ScrollOffsetChanged += (_, _) => OnScrollOffsetChanged();
        // Belgenin son satırının yeri ancak görsel satırlar kurulduktan sonra bilinir; her değişimde
        // (yeni satır, punto, yeniden boyutlanma) prompt yeniden konumlanır.
        EditorControl.TextArea.TextView.VisualLinesChanged += (_, _) => RefreshPrompt();
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
            scrollSmooth: AnimateToBottom,
            // Anlatı modunda kullanıcı elini çekince akış yeniden izlenir. PROJE-LOG modunda dönülmez:
            // orada izlenecek canlı bir akış (ve prompt imleci) yoktur, kullanıcı bir logu okuyordur.
            autoResumeAllowed: () => !_projectMode);
        _bottomAnchor.Changed += OnBottomAnchorChanged;
        // "Kullanıcı kaydırdı" HAM GİRDİDEN bildirilir — tekerlek, kaydırma çubuğu ve gezinme tuşları
        // (gerekçe: UserScrollSignal / BottomAnchorBehavior.NotifyUserScroll).
        UserScrollSignal.Wire(this, _bottomAnchor.NotifyUserScroll);
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
        RefreshPrompt(); // dipten uzaklaşınca prompt da gider (gerekçe: RefreshPrompt doc'u)
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
        // Dibe çekme yetkisi TEK yerdedir (BottomAnchorBehavior.ShouldFollow): takip açık, uçuşta atlama yok
        // ve direksiyon kullanıcıda değil. Burası eskiden salt StickToBottom'a bakıyordu — kullanıcının
        // kaydırmasını görmeyen ikinci bir auto-scroll yoluydu ve derleme sürerken konsolu kaydırılamaz
        // hâle getiriyordu.
        if (_bottomAnchor.ShouldFollow)
            EditorControl.ScrollToEnd();
    }

    /// <summary>
    /// [design v1.7.0 §2.5] Mod geçişinin ikinci adımı: <b>(1) içeriği değiştir → (2) DİBE PİNLE → (3)
    /// animasyonu başlat</b>. Yeni içerik her zaman dipten okunur.
    ///
    /// <para><b>Neden önce <c>UpdateLayout</c>:</b> belge az önce değiştirildi ve editörün kaydırma
    /// geometrisi (extent/viewport) henüz yeniden ölçülmemiştir. O anda <c>ScrollToEnd</c> çağırmak,
    /// hesabı ESKİ geometriye yaptırır ve panel dipte değil TEPEDE kalır (ölçüldü: 3739px'lik bir belgede
    /// offset 3161 yerine 19'da kalıyordu). Ölçüm zorlandığında pin deterministik olur — geçişte bir kez
    /// çalışır ve belge zaten render dilimiyle (200 satır) sınırlıdır.</para>
    /// </summary>
    private void PinToBottomAfterModeSwitch()
    {
        if (!_bottomAnchor.ShouldFollow) return;
        EditorControl.UpdateLayout();
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
        ClearReadyText();
        EnsureColorizer();

        // Son tam satırı ayır: newest = son satır (soneksiz); prefix = ondan önceki her şey ('\n' sonekli).
        string body = text[^1] == '\n' ? text[..^1] : text;
        int lastNl = body.LastIndexOf('\n');
        string newest = lastNl < 0 ? body : body[(lastNl + 1)..];
        string prefix = lastNl < 0 ? "" : text[..(lastNl + 1)];
        int lineCount = 1;
        for (int i = 0; i < body.Length; i++) if (body[i] == '\n') lineCount++;

        AppendBatch(text); // [design v1.7.0 §2.5] anlatı satırları ANINDA basılır — daktilo yok
    }

    /// <summary>[design v1.7.0 §2.5] Boşta (idle/boot) tek prompt satırı: <b>yanıp sönen blok imleç + "ready"
    /// (dim)</b>. Duvar-saati damgası kaldırıldı (§2.5: konsolda saat sütunu yok) — prompt satırı imleçle
    /// başlar ve konsolun geri kalanıyla aynı sol hizadadır. Doküman satırı DEĞİLdir: overlay'de canlı
    /// gösterilir, içerik gelince (<see cref="AppendNarrativeBatch"/> / <see cref="PlayCascade"/>) temizlenir.
    /// Reduced-motion iken imleç statiktir.</summary>
    public void ShowReady()
    {
        EnsureColorizer();
        _idleReady = true;
        ActiveLineText.Foreground = _palette?.Dim ?? EditorControl.Foreground;
        ActiveLineText.Text = ConsoleEmptyState.Idle; // "ready"
        RefreshPrompt();
    }

    /// <summary>İçerik geldi: prompt satırının yalnız METNİ boşalır — imleç durur (§2.5, prototip
    /// <c>BuildApp.jsx:766-771</c>: satır koşulsuz render edilir, idle/boot değilken içi boşalır).</summary>
    private void ClearReadyText()
    {
        if (!_idleReady) return;
        _idleReady = false;
        ActiveLineText.Text = "";
        RefreshPrompt();
    }

    /// <summary>
    /// Prompt satırının TEK görünürlük yazıcısı. İki koşul: <b>anlatı modunda</b> olmak (proje-log modunun
    /// kendi sonu vardır — amber "build in progress ▮") ve <b>dipte</b> olmak.
    ///
    /// <para>Dip koşulu şundandır: prompt panelin altına yaslıdır, belgeyle birlikte kaymaz. Kullanıcı yukarı
    /// kaydırıp geçmişe baktığında dipte asılı kalan bir imleç oradaki metnin üstüne binerdi. Dipten
    /// uzaklaşınca zaten <c>⌄ latest</c> pill'i çıkar; prompt onunla birlikte gider ve dibe dönünce geri gelir.</para>
    /// </summary>
    private void RefreshPrompt()
    {
        bool show = !_projectMode && _bottomAnchor.IsStuck;
        ActiveLineOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) { StopBlink(); return; }
        ActiveCursor.Opacity = 1.0;
        PositionPrompt();
        if (_motion.Enabled) StartBlink(); else StopBlink(); // [A13/T1] motion sinyalinin TEK kapısı (MotionGate seam'i)
    }

    /// <summary>
    /// Prompt satırını BELGENİN SONUNA yerleştirir: imleç her zaman son metin satırının hemen ALTINDAKİ
    /// satırdadır ve yeni satırlar onun üstüne birikir.
    ///
    /// <para><b>Neden panelin dibine yaslanmıyor:</b> AvalonEdit içeriği yukarıdan aşağı dizer. Konsolda üç
    /// satır varken metin tepede kalır; panele yaslı bir imleç o metinden kopup dipte tek başına yanardı.
    /// Doğru yer belgenin kendi son satırıdır.</para>
    ///
    /// <para>Satır sözleşmesi gereği canlı metin '\n' ile biter (<c>ConsoleBatcher</c>), yani belgenin SON
    /// satırı zaten boş prompt satırıdır — imleç oraya oturur. Sonda yeni satır yoksa (savunmacı) imleç bir
    /// satır aşağı iner. Konum, görsel satırın kendi koordinatından alınıp bu kontrole taşınır; böylece
    /// editörün dolgusu, kaydırma ve satır yüksekliği ayrı ayrı hesaplanmaz.</para>
    /// </summary>
    private void PositionPrompt()
    {
        if (ActiveLineOverlay.Visibility != Visibility.Visible) return;

        var view = EditorControl.TextArea.TextView;
        var document = EditorControl.Document;
        if (document is null || !view.VisualLinesValid || view.VisualLines.Count == 0) return;

        var lastLine = document.GetLineByNumber(document.LineCount);
        var visual = view.GetVisualLine(lastLine.LineNumber);
        if (visual is null) return; // son satır görünür pencerede değil — konum bir sonraki kaydırmada tazelenir

        // Düzen daha oturmadıysa satırın yeri HENÜZ YOKTUR (VisualTop NaN, ScrollOffset sonsuz gelir) —
        // o an konumlandırmak Margin'e NaN yazmak olurdu. Atlanır; düzen oturunca VisualLinesChanged bu
        // metodu yeniden çağırır.
        if (!double.IsFinite(visual.VisualTop) || !double.IsFinite(view.ScrollOffset.Y)) return;

        // Boş son satır zaten prompt satırıdır; dolu ise imleç onun ALTINA geçer.
        double top = visual.VisualTop + (lastLine.Length == 0 ? 0 : visual.Height);
        // Referans TİLT KABIDIR, ConsoleView değil: geçiş animasyonu kabı ölçekleyip kaydırır ve kök baz
        // alınsaydı imlecin konumu animasyonun ortasındaki ara değerlerle hesaplanırdı. Editör ile prompt
        // aynı kabın içinde olduğundan aralarındaki mesafe dönüşümden ETKİLENMEZ.
        var point = view.TransformToAncestor(PART_TiltHost)
                        .Transform(new Point(0, top - view.ScrollOffset.Y));
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;

        // İmleç metin satırıyla aynı taban çizgisinde dursun: satır yüksekliği içinde dikey ortalanır.
        double centred = point.Y + Math.Max(0, (visual.Height - ActiveCursor.Height) / 2);
        var margin = new Thickness(point.X, centred, 0, 0);
        if (ActiveLineOverlay.Margin != margin) ActiveLineOverlay.Margin = margin;
    }

    // [3b M-4 · D3 §3] Aktif-satır imleci ile "build in progress" imlecinin ORTAK blink animasyonu — artık
    // EventStreamView'ın imleci de dahil ÜÇ başlatıcı MotionTokens.CreateBlinkAnimation'ı paylaşır (kopya YASAK).
    /// <summary>[StatusGlyph/BuildingSpinner deseni] Zaten dönen saat YENİDEN BAŞLATILMAZ: RefreshPrompt her
    /// görsel-satır değişiminde koşar ve her seferinde yeni bir blink kurmak imleci "takılı" gösterirdi.</summary>
    private void StartBlink()
    {
        if (_blinking) return;
        _blinking = true;
        ActiveCursor.BeginAnimation(OpacityProperty, MotionTokens.CreateBlinkAnimation());
    }

    private void StopBlink()
    {
        _blinking = false;
        ActiveCursor.BeginAnimation(OpacityProperty, null);
        ActiveCursor.Opacity = 1.0;
    }

    // ---------------------------------------------------------------- narrative (run) modu

    /// <summary>[3b] Run/anlatı dokümanını kurar (Back akışı): build-in-progress iptal, render dilimi
    /// (son <see cref="RenderSliceLines"/>) uygulanır, canlı append'te tail-trim AÇIK. Chunk loader kapalı.
    /// <para>[design v1.7.0 §2.5] Geçiş İKİ YÖNDE de aynıdır: içerik tek parça <see cref="PlayTiltIn"/> ile
    /// serilir.</para></summary>
    public void ShowRunDocument(string fullRunText)
    {
        HideBuildInProgress();
        // [T59] design-v1 §2.5: "konsol↔proje-log geçişinde dibe sabitlenir" — kullanıcı önceki modda serbest
        // kaydırmış olsa bile mod değişimi HER ZAMAN yeniden yapıştırır (aşağıdaki `if (StickToBottom)` artık true okur).
        _bottomAnchor.ForceStuck(true);
        _projectMode = false;
        _trimTail = true;
        _projectAllLines = [];
        _loadedFrom = 0;
        EditorControl.Document = new TextDocument(ConsoleRenderSlice.LastLines(fullRunText ?? "", RenderSliceLines));
        PinToBottomAfterModeSwitch();
        RefreshPrompt(); // anlatıya dönüldü → prompt satırı geri gelir
        PlayTiltIn();
    }

    // ---------------------------------------------------------------- proje-log kaskatı

    /// <summary>
    /// [design v1.7.0 §2.5] Proje-log moduna geçiş. İçerik <see cref="PlayTiltIn"/> ile TEK PARÇA serilir ve
    /// chunk loader kurulur. <paramref name="buildInProgress"/> iken sonda amber "build in progress ▮" belirir.
    /// Motion TAZE okunur.
    /// </summary>
    public void PlayCascade(IReadOnlyList<string> allLines, bool buildInProgress)
    {
        EnsureColorizer();
        HideBuildInProgress();
        // [T59] design §2.5: "konsol↔proje-log geçişinde dibe sabitlenir" — bkz. ShowRunDocument'taki aynı gerekçe.
        _bottomAnchor.ForceStuck(true);
        allLines ??= [];
        _projectMode = true;
        RefreshPrompt();                 // proje-log modunun kendi sonu var (amber "build in progress ▮")
        _idleReady = false;
        ActiveLineText.Text = "";
        _armedForChunk = false;            // ilk layout'ta spurious prepend olmasın (kullanıcı henüz kaydırmadı)
        _trimTail = false;                 // proje modunda tail-trim yok — chunk loader eski satırları yönetir
        _projectAllLines = allLines;
        _buildInProgressPending = buildInProgress;

        // Render dilimi: son RenderSliceLines satır belgeye; öncesi chunk loader'a bırakılır.
        _loadedFrom = Math.Max(0, allLines.Count - RenderSliceLines);
        EditorControl.Document = new TextDocument(Join(allLines, _loadedFrom, allLines.Count));
        PinToBottomAfterModeSwitch();

        PlayTiltIn();
        if (buildInProgress) ShowBuildInProgress(_motion.Enabled); // [A13/T1] motion sinyalinin TEK kapısı
    }

    /// <summary>
    /// [design v1.7.0 §2.5 · animasyon spec §2] Panel geçişinin TEK hareketi. Log bloğu, <b>alt kenarı sabit
    /// kalacak şekilde</b> izleyiciye doğru düzleşir: 14px aşağıdan, hafif kısaltılmış ve saydam başlar,
    /// 340ms ease-out ile tam düz ve tam opak olur. Kâğıdın masaya oturması gibi — alt kenar hiç oynamaz.
    ///
    /// <para>Prototipteki <c>perspective(900px) rotateX(7deg)</c> WPF'te doğrudan yoktur (PlaneProjection
    /// WPF'e ait değildir); spec §2.4'ün önerdiği native eşleme uygulanır: <c>RenderTransformOrigin 0.5,1</c>
    /// + <c>ScaleY 0.965 → 1</c> + <c>TranslateY 14 → 0</c> + <c>Opacity 0 → 1</c>, 340ms,
    /// <c>KeySpline 0.22,1 0.36,1</c>. Trapez kaybolur ama alt-menteşe + düzleşme okuması korunur.</para>
    ///
    /// <para>Hareket TEK PARÇADIR: satır sayısından bağımsız olarak her zaman aynı sürede biter — 3 satırlık
    /// bir log ile 200 satırlık anlatı aynı ritimde açılır. Yalnız transform + opacity animasyonu vardır;
    /// layout/yükseklik animasyonu YASAKTIR (spec §2.4) ve panelin yüksekliği geçiş boyunca değişmez.</para>
    ///
    /// <para>Yalnız log bloğu REMOUNT edildiğinde oynar (proje logu açma / <c>← Back</c> / proje değişimi /
    /// boş-durum) — canlı satır eklenirken, kaydırırken ya da yeniden boyutlanırken ASLA (spec §2.1/§3).
    /// Reduced-motion iken hiç oynatılmaz (motion sözleşmesi).</para>
    /// </summary>
    private void PlayTiltIn()
    {
        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform(0, 0);
        PART_TiltHost.RenderTransformOrigin = new Point(0.5, 1.0); // menteşe ALT kenardadır
        PART_TiltHost.RenderTransform = new TransformGroup { Children = { scale, translate } };

        if (!_motion.Enabled) // [A13/T1] motion sinyalinin TEK kapısı (MotionGate seam'i)
        {
            PART_TiltHost.Opacity = 1.0;
            return;
        }

        // Süre bir tasarım token'ı DEĞİL, bu geçişin kendi ölçüsüdür (prototip 340ms; motion skalası
        // 80/120/180/280'de durur) — bileşenin kendi sabiti olarak yukarıda durur.
        var duration = TimeSpan.FromMilliseconds(TiltInMs);
        var ease = MotionTokens.ResolveKeySpline(this, "KeySpline.EaseOut", new KeySpline(0.22, 1, 0.36, 1));

        // YÖN: içerik AŞAĞIDAN yukarı oturur. Prototip `from { translateY(14px) } to { translateY(0) }` der
        // (BuildApp.jsx:37) — yani başlangıç son yerinin 14px ALTINDADIR. Bir ara sürümde işaret ters yazılmıştı
        // ve içerik yukarıdan aşağı iniyordu; menteşe alt kenarda olduğu için bu, oturmak yerine "düşmek" gibi
        // okunuyordu.
        scale.ScaleY = TiltInScaleFrom;
        translate.Y = TiltInOffsetPx;
        PART_TiltHost.Opacity = 0.0;
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, MotionTokens.SplineTo(1.0, duration, ease));
        translate.BeginAnimation(TranslateTransform.YProperty, MotionTokens.SplineTo(0.0, duration, ease));
        PART_TiltHost.BeginAnimation(OpacityProperty, MotionTokens.SplineTo(1.0, duration, ease));
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
        // [I-1 fix] Bottom-anchor'ın yeniden-hesabı, chunk loader'dan (EvaluateChunkScroll) ÖNCE çalışır: bir
        // prepend (aşağıda) belgeyi TEPEDE büyütür ve VerticalOffset'i ChunkStitch.CompensatedOffset'e telafi
        // eder; önce çalışmak, henüz-prepend-edilmemiş geometriyle takip kararını taze tutar.
        //
        // Bu bir OFFSET olayıdır, extent olayı değil — AvalonEdit'in ScrollOffsetChanged'i yalnız kaydırma
        // konumu değişince ateşlenir. Bir ara sürüm burada extent farkını elle izliyordu ve gerçek kusur oydu:
        // kullanıcı yukarıdayken eklenen içerik offset'i oynatmadığından hiç olay doğmuyor, izlenen extent
        // bayatlıyordu; kullanıcı nihayet tekerleği çevirdiğinde olay "içerik büyüdü" (extentChange>0) gibi
        // görünüp takip kararını ATLATIYORDU. Panel dibe yapışık kalıyor ve her batch kullanıcıyı geri
        // fırlatıyordu. İçerik büyümesini artık büyümeyi YAPAN yer bildirir (AppendBatch → ShouldFollow).
        _bottomAnchor.OnScrollChanged(extentHeightChange: 0);

        EvaluateChunkScroll(EditorControl.VerticalOffset); // [3b, DEĞİŞMEDİ] chunk loader — üstteki eşik ayrı kavram

        // Prompt AYNI olayda taşınır. Konumu belgenin son satırından gelir; kaydırma o satırı yukarı iterken
        // imleç eski yerinde kalsaydı — bir tık tekerlek çevirmek yetiyordu — bir kare boyunca metnin ÜSTÜNE
        // biner, ancak bir sonraki görsel-satır olayında düzelirdi. Sahada görülen anlık bindirme buydu.
        RefreshPrompt();
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

    /// <summary>[Test] Panel geçişinin (tilt in) uygulandığı log bloğu — prompt satırı bunun DIŞINDADIR.</summary>
    internal FrameworkElement TiltHost => PART_TiltHost;

    /// <summary>[Test] Dibe çekme yetkisi (<see cref="BottomAnchorBehavior.ShouldFollow"/>) — kullanıcı
    /// kaydırdığında kapanır, bekleme dolunca geri açılır.</summary>
    internal bool FollowsBottom => _bottomAnchor.ShouldFollow;

    /// <summary>[A13/T1 fix-1 · I-C · <see cref="Views.EventStreamView.ActiveLineInstant"/> ikizi] En yeni satır
    /// için SON kurulan daktilo zamanlayıcısı instant mı — yani üretim append yolu satırı harf harf mi yazıyor,
    /// yoksa tek hamlede mi bastı. Hiç kurulmadıysa (satır instant basıldı / overlay hiç açılmadı) <c>true</c>
    /// varsayılır.
    ///
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
