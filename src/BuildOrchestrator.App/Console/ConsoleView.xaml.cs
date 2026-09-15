using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
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
    /// TUTMAZ, bu görünüm ise <c>RepeatBehavior.Forever</c> bir blink saati başlatır (<see cref="StartBlink"/>).
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

    // [Task 6] Satır hover bandının YEREL (donmamış) fırçası — MotionTokens.TransitionColor bunu animate eder.
    private readonly SolidColorBrush _hoverBandBrush;

    // Kaskat durumu (yalnız UI thread'inde).
    // [design v1.7.0 §2.5] Panel geçişinin tek parça "tilt in" ölçüleri (prototip: 14px + 340ms + rotateX 7°).
    private const double TiltInMs = 340.0;
    private const double TiltInOffsetPx = 14.0;
    // Prototip birebir (animasyon spec §2.2): perspective(900px) + rotateX(7deg). Değerler spec'tendir,
    // "iyileştirilmez".
    private const double TiltInAngleDeg = 7.0;
    private const double TiltInPerspectivePx = 900.0;
    private int _tiltGeneration; // uçuştaki bir geçişi yeni bir geçiş geçersiz kılar

    // Render dilimi / chunk loader durumu.
    // [tail-trim tek kural] Ayrı bir "kırp/kırpma" bayrağı YOKTUR: yetki BottomAnchorBehavior.ShouldFollow'dadır
    // (takip açıkken kırpılır). Eski _trimTail alanının taşıdığı ayrım o predicate'in içinde zaten yaşıyordu —
    // proje modu takip KAPALI açıldığı için orada kırpma olmaz.
    private bool _projectMode;                            // proje-log modu (kaynak: disk logu; anlatıda kaynak VM tamponudur)
    private bool _prepending;                             // re-entrancy guard (prepend VerticalOffset'i değiştirir)
    private bool _armedForChunk;                          // kullanıcı tepeden UZAKLAŞTI mı — ilk layout spurious prepend'ini önler

    /// <summary>Belgenin ARKASINDAKİ satırlar — render diliminin (200) dışında kalan geçmiş. Chunk loader
    /// tepeye kaydırıldığında buradan geri yükler.
    ///
    /// <para><b>Moddan bağımsızdır.</b> Eskiden yalnız proje logunun disk satırlarıydı ve anlatı modunda BOŞ
    /// kalıyordu; sonucu şuydu: run konsolunda 200 satırdan öncesine hiçbir jestle erişilemiyordu. Paralel bir
    /// build saniyede yüzlerce satır aktığı için o pencere anında dolar ve ekran "bir sürü şey aktı, azıcık
    /// kaldı" hâline gelirdi. Veri hiç kaybolmuyordu (anlatının tamamı VM'in tamponunda, her projenin logu
    /// kendi tamponunda) — kaybolan yalnız görünürlüktü.</para>
    ///
    /// <para><b>Canlı büyür.</b> Kırpma belgenin başından satır siler; bu satırlar backlog'un ucunu aşıyorsa
    /// (yani daha önce hiç kaydedilmemiş CANLI satırlarsa) buraya EKLENİR. Eski kod bunu yapmadığı için
    /// proje modunda da bir delik vardı: canlı gelen satırlar kırpılınca <see cref="_loadedFrom"/> bir
    /// clamp'e takılıyor ("index'i yok") ve o satırlara bir daha ulaşılamıyordu.</para></summary>
    private List<string> _backlogLines = [];

    /// <summary>Belgenin ilk satırının <see cref="_backlogLines"/>'daki index'i — yani "backlog'un kaçıncı
    /// satırına kadarı belgeye yüklendi". <c>_backlogLines[0.._loadedFrom)</c> belgede DEĞİLDİR (geçmiş);
    /// bu index backlog'un sonuna eşitse belgedeki her satır backlog'un ötesindeki canlı satırlardır.</summary>
    private int _loadedFrom;

    public ConsoleView()
    {
        // [A13/T1 fix-1 · I-D] EventStreamView.ctor deseni birebir: gate + Changed aboneliği InitializeComponent'ten ÖNCE.
        _motion = new MotionGate(this);
        _motion.Changed += OnMotionChanged;
        InitializeComponent();
        // Gömülü Geist Mono Console CompositeFont'u (It-0 asset'i) — pack URI burada TEKRARLANMAZ [T64].
        EditorControl.FontFamily = AppFonts.MonoConsole;
        ActiveLineText.FontFamily = AppFonts.MonoConsole;
        // [design v1.7.0 §1.2] Konsol gövdesi 300 (Light) — editör ve prompt satırı AYNI token'ı okur
        // (drift edemez).
        EditorControl.SetResourceReference(FontWeightProperty, "FontWeight.Console");
        ActiveLineText.SetResourceReference(FontWeightProperty, "FontWeight.Console");
        Loaded += (_, _) => { EnsureColorizer(); PositionPrompt(); };
        EditorControl.TextArea.TextView.ScrollOffsetChanged += (_, _) => OnScrollOffsetChanged();
        // Belgenin son satırının yeri ancak görsel satırlar kurulduktan sonra bilinir; her değişimde
        // (yeni satır, punto, yeniden boyutlanma) prompt yeniden konumlanır.
        // [M-2 review round 1] AYNI olay hover bandını da tazeler — imleç kımıldamadan içerik kayarsa (scroll,
        // ekleme, chunk-dikiş, mod değişimi) bant ESKİ satırda asılı kalmasın diye (bkz. RefreshHoverBand doc'u).
        EditorControl.TextArea.TextView.VisualLinesChanged += (_, _) => { RefreshPrompt(); RefreshHoverBand(); };
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
        // [Task 6/design v1.17.0 §9] Konsol gövdesi standart OK imleci ister — AvalonEdit'in TextArea'sı kendi
        // IBeam'ini yönlendirilmiş QueryCursor olayı ÜZERİNDEN dayatır (statik Cursor özelliği değil, bkz.
        // ForceArrowCursor doc'u). Aynı olay burada (üst ata EditorControl) handledEventsToo:true ile YENİDEN
        // yakalanır: kabarcıklanma AvalonEdit'in kararından SONRA buraya ulaşır, SON SÖZÜ biz söyleriz.
        EditorControl.AddHandler(Mouse.QueryCursorEvent, new QueryCursorEventHandler(ForceArrowCursor), handledEventsToo: true);
        // Belt-and-suspenders: AvalonEdit'in hiç ele almadığı konumlarda (QueryCursor bubbling'i hiç
        // tetiklenmeyen köşe durumlar) statik değer de Arrow'dur — üç seviye de gerçekten Arrow görünsün diye.
        EditorControl.Cursor = Cursors.Arrow;
        EditorControl.TextArea.Cursor = Cursors.Arrow;
        EditorControl.TextArea.TextView.Cursor = Cursors.Arrow;
        _hoverBandBrush = (SolidColorBrush)HoverBand.Fill;
        // [M-1 review round 1] TextView DEĞİL, EditorControl dinlenir: TextView editörün 12px iç dolgusunun
        // (Padding) İÇİNDE durur, yalnız onu dinlemek panelin sol/sağ 12px + üst 8px + alt 14px kenarlarında
        // bandın kaybolmasına yol açardı — tasarım tam tersini istiyor (kenardan kenara). EditorControl
        // Background="Transparent" olduğu için tüm dolgu dahil her yerde hit-test edilebilir; konum yine de
        // TextView'e GÖRE alınır (UpdateHoverBand dolgunun İÇİNDEKİ/DIŞINDAKİ Y'yi aynı şekilde işler).
        EditorControl.MouseMove += (_, e) => UpdateHoverBand(e.GetPosition(EditorControl.TextArea.TextView).Y);
        EditorControl.MouseLeave += (_, _) => HideHoverBand();
        // [A13/T1 fix-1 · I-D] EventStreamView.ctor:97 deseni: unload'da SONSUZ blink saatleri bırakılır (aksi
        // halde ağaçtan çıkmış bir görünümün iki clock'u timing engine'de 30fps'te uyanık kalırdı). Uçuştaki
        // daktilo/kaskat BURADA commit EDİLMEZ: commit doküman yazan bir DAVRANIŞTIR ve unload'da yeni bir
        // satır üretmek bugünkü sözleşmeyi değiştirirdi (mod değişimi yollarının kendi commit/iptal kararları var).
        Unloaded += (_, _) => StopBlink();
        // Pencere tepsiye inince görünüm BOŞALTILMAZ (Unloaded ateşlenmez) — yalnız görünmez olur. Sonsuz saatler
        // görünürlüğe bağlıdır (ARCHITECTURE §14.5); geri gelişte prompt yeniden kurulur (bkz. StartBlink kapısı).
        IsVisibleChanged += (_, _) => RefreshPrompt();
    }

    /// <summary>[A13/T1 fix-1 · I-D] Motion sinyali koşu SIRASINDA değişince görünüm uyar
    /// (<see cref="Views.EventStreamView.OnMotionChanged"/> sözleşmesiyle aynı): SONSUZ saatler (aktif/ready
    /// satır imleci) yalnız GÖRÜNÜR olduğunda yeniden değerlendirilir.
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
    /// çağırmaz (çağıranın/Task 12'nin sorumluluğu). [A13.2 ZORUNLU sıra]. [3b] Belge son
    /// <see cref="RenderSliceLines"/> satırla sınırlanır (baştan kırpma) — ama YALNIZ takip açıkken; gerekçe
    /// gövdededir. Kırpılan her satır için proje modunda <see cref="_loadedFrom"/> ilerletilir ki chunk loader
    /// index'i belgenin gerçek ilk satırıyla TUTARLI kalsın [C-1]; kırpılan satırlar
    /// <see cref="_projectAllLines"/>'ta durur → tepeye kaydırınca prepend onları DELİKSİZ geri yükler.
    /// </summary>
    public void AppendBatch(string text)
    {
        var document = EditorControl.Document;
        document.BeginUpdate();
        try
        {
            document.Insert(document.TextLength, text);
            // Tail-trim: chatty bir build (MSBuild hacmi) belgeyi sınırsız büyütmesin — render dilimi kadar
            // tutulur (§3.6). [3b M-2]
            //
            // AMA YALNIZ TAKİP AÇIKKEN. Kırpma belgenin BAŞINDAN satır siler; kullanıcı yukarıda okurken bu,
            // kaydırma konumu sabit kalsa bile metni onun altından yukarı kaydırır. Sahada görülen "scroll
            // duruyor ama yazılar akmaya devam ediyor" tam olarak buydu: panel kullanıcıya bırakılmıştı ama
            // okuduğu satırlar ayağının altından siliniyordu. Kullanıcı elini çekince (bekleme dolar, takip
            // geri gelir) kırpma tek hamlede yetişir ve o an panel zaten dipte olduğu için görünmez.
            if (_bottomAnchor.ShouldFollow)
            {
                var (trimmed, removed) = TrimToRenderSlice(document);
                // [C-1] Tepeden K satır kırpıldıysa belgenin ilk satırı bir K kadar ileri kayar; chunk loader
                // index'i onunla birlikte ilerlemelidir (aksi halde stale _loadedFrom → sonraki scroll-to-top
                // prepend'i YANLIŞ dilimi yükler = tekrar/kayıp).
                //
                // Kırpılan satırların bir kısmı backlog'da ZATEN vardır (belgeye oradan yüklenmişlerdi);
                // geri kalanı hiç kaydedilmemiş CANLI satırlardır ve backlog'un SONUNA eklenir. Eski kod bu
                // ikinciyi yapmıyor, index'i backlog'un uzunluğuna clamp'liyordu — o clamp tam da deliğin
                // kendisiydi: canlı kırpılan satır ne belgede ne backlog'da kalıyordu.
                if (trimmed > 0)
                {
                    int alreadyInBacklog = _backlogLines.Count - _loadedFrom;
                    if (trimmed > alreadyInBacklog)
                        _backlogLines.AddRange(LastLines(removed, trimmed - alreadyInBacklog));
                    _loadedFrom += trimmed;
                }
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
    /// [design v1.7.0 §2.5] Mod geçişinin ikinci adımı: <b>(1) içeriği değiştir → (2) PİNLE → (3) animasyonu
    /// başlat</b>. Hangi uca pinleneceği moda göredir: anlatı SONDAN, proje logu BAŞTAN okunur (gerekçeler
    /// <see cref="ShowRunDocument"/> ve <see cref="PlayCascade"/> doc'larındadır).
    ///
    /// <para><b>Neden önce <c>UpdateLayout</c>:</b> belge az önce değiştirildi ve editörün kaydırma
    /// geometrisi (extent/viewport) henüz yeniden ölçülmemiştir. O anda kaydırmak hesabı ESKİ geometriye
    /// yaptırır ve panel istenen uçta durmaz (ölçüldü: 3739px'lik bir belgede dip 3161 yerine 19'da
    /// kalıyordu). Ölçüm zorlandığında pin deterministik olur — geçişte bir kez çalışır ve belge zaten
    /// render dilimiyle (200 satır) sınırlıdır.</para>
    ///
    /// <para><b>Neden bir de SONRA:</b> <c>ScrollToEnd</c> bir İSTEKTİR, bir hareket değil —
    /// <c>ScrollViewer</c> onu ancak bir sonraki ölçüm turunda <c>TextView</c>'e iletir. İkinci tur olmadan
    /// metot döndüğünde <c>VerticalOffset</c> hâlâ ESKİ değerdedir (ölçüldü: dip 3573 iken 0) ve istek,
    /// geçişin ORTASINDA oturur. Bedeli görünürdü: geçişin dokusu (<see cref="BuildTiltScene"/>) o kareden
    /// alındığı için 340ms boyunca metnin BAŞI gösteriliyor, sonra panel tek karede dibe atlıyordu — sahada
    /// "animasyonla birlikte pat diye alta gitme" diye görülen buydu. İmleç de aynı kareye bağlıdır:
    /// <see cref="PositionPrompt"/> belgenin son satırı görünür pencerede değilken erken döner ve imleci ESKİ
    /// yerinde bırakır, yani metnin üstünde "sanki sondaymış gibi" durur.</para>
    /// </summary>
    private void PinAfterModeSwitch(bool toBottom)
    {
        EditorControl.UpdateLayout();
        if (!toBottom)
        {
            EditorControl.ScrollToVerticalOffset(0);
            EditorControl.UpdateLayout();
            return;
        }
        // [dip TAM olmalı] Ölçüm turu extent'i BÜYÜTEBİLİR: AvalonEdit görsel satırları ancak kaydırılan
        // konumda kurar ve o satırların gerçek yükseklikleri (sarma yok ama dolgu/son satır var) ilk tahminden
        // farklı çıkabilir. Tek bir ScrollToEnd + tek tur bu yüzden panelin bir SATIR kadar tepede kalmasına
        // yol açıyordu: altta küçük bir boşluk, `⌄ latest` pill'i (dipten >48px) ve dönüşten hemen sonra
        // görünen minik bir kayma. Ölçüm SABİTLENENE kadar (extent değişmeyene kadar) yeniden pinlenir.
        // Döngü sınırlıdır: birkaç turda oturmuyorsa geometri zaten oynaktır ve daha fazla tur bir şey katmaz.
        for (int pass = 0; pass < MaxPinPasses; pass++)
        {
            double before = EditorControl.ExtentHeight;
            EditorControl.ScrollToEnd();
            EditorControl.UpdateLayout();
            if (Math.Abs(EditorControl.ExtentHeight - before) < 0.5) break;
        }
    }

    /// <summary>
    /// Geçiş oturduktan SONRA dibi tazeler — yalnız anlatı modunda ve panel akışı izliyorken.
    ///
    /// <para><b>Neden pin tek başına yetmiyor:</b> <see cref="PinAfterModeSwitch"/> belge takasının hemen
    /// ardından ölçümü sabitler, ama geçişin 340 ms'i boyunca daha geç yerleşim turları gelir — prompt satırı
    /// konumlanır (<c>VisualLinesChanged</c>), görsel satırlar kurulur, dolgu netleşir. Bunlar extent'i bir
    /// satır kadar büyütebilir ve panel dibin biraz üstünde kalır: altta küçük bir boşluk ve dipten &gt;48 px
    /// olduğu için ara sıra <c>⌄ latest</c> pill'i. Sahada "her zaman değil, nadir de olsa" görülmesinin sebebi
    /// budur — kayma yalnız o geç turların extent'i gerçekten değiştirdiği durumlarda oluşur.</para>
    ///
    /// <para>Kullanıcı geçiş sırasında tekerleğe dokunduysa <see cref="BottomAnchorBehavior.ShouldFollow"/>
    /// kapanır ve buraya girilmez: bir geçiş, okuyucunun kendi kaydırmasını ezmez.</para></summary>
    private void SettleAtBottomIfFollowing()
    {
        if (_projectMode || !_bottomAnchor.ShouldFollow) return;
        PinAfterModeSwitch(toBottom: true);
    }

    /// <summary>Dip pininin ölçüm turu sayısı — ilk tur belgeyi kurar, ikincisi büyümüş extent'i yakalar,
    /// üçüncüsü emniyettir. Sayı çağrı yerinde literal YAZILMAZ (StatusGlyph.PulseMs deseni).</summary>
    private const int MaxPinPasses = 3;

    // Belgeyi son RenderSliceLines satıra kırpar (baştaki fazla satırları TEK Remove ile siler).
    // Silinen satır SAYISINI ve METNİNİ döndürür: sayı [C-1] _loadedFrom'u ilerletmek, metin ise hiç
    // kaydedilmemiş canlı satırları backlog'a taşımak içindir (bkz. AppendBatch).
    private static (int Lines, string Removed) TrimToRenderSlice(TextDocument document)
    {
        int excess = document.LineCount - RenderSliceLines;
        if (excess <= 0) return (0, "");
        var lastToRemove = document.GetLineByNumber(excess); // 1..excess satırlarını (ayraçlarıyla) sil
        int length = lastToRemove.Offset + lastToRemove.TotalLength;
        string removed = document.GetText(0, length);
        document.Remove(0, length);
        return (excess, removed);
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
    /// kendi sonu vardır) ve <b>dipte</b> olmak.
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

    // [3b M-4 · D3 §3] Aktif-satır imlecinin blink animasyonu — artık
    // EventStreamView'ın imleci de dahil ÜÇ başlatıcı MotionTokens.CreateBlinkAnimation'ı paylaşır (kopya YASAK).
    /// <summary>[StatusGlyph/BuildingSpinner deseni] Zaten dönen saat YENİDEN BAŞLATILMAZ: RefreshPrompt her
    /// görsel-satır değişiminde koşar ve her seferinde yeni bir blink kurmak imleci "takılı" gösterirdi.</summary>
    private void StartBlink()
    {
        // Görünmezken saat KURULMAZ. Kapı burada, çağıranlarda değil: RefreshPrompt her görsel-satır değişiminde
        // koşar ve tepsideyken de koşar — yalnız IsVisibleChanged'de durdurmak saati bir sonraki olayda geri
        // kurardı (ölçüldü, bkz. HiddenCursorClockTests).
        if (!IsVisible) { StopBlink(); return; }
        if (_blinking) return;
        _blinking = true;
        ActiveCursor.BeginAnimation(OpacityProperty, MotionTokens.CreateBlinkAnimation());
        // [design v1.12.1 §2.5] Kırpmanın üstüne renk turu biner — ikisi ayrı saatlerdir ve yalnız FAZLARI
        // ortaktır (renk kırpmanın dibinde atlar, bkz. CursorHop).
        CursorHop.Start(this, ActiveCursor);
    }

    private void StopBlink()
    {
        _blinking = false;
        ActiveCursor.BeginAnimation(OpacityProperty, null);
        ActiveCursor.Opacity = 1.0;
        // Prompt'un dinlenme rengi amberdir — turdan çıkınca imleç oraya döner (stream'in ton kanalının eşi).
        CursorHop.Stop(ActiveCursor, ConsolePalette.Keys.Icon);
    }

    // ---------------------------------------------------------------- narrative (run) modu

    /// <summary>[3b] Run/anlatı dokümanını kurar (<c>← Back</c> akışı): render dilimi (son
    /// <see cref="RenderSliceLines"/>) belgeye yüklenir, ÖNCESİ backlog'a bırakılır ve canlı append'te
    /// tail-trim açıktır.
    ///
    /// <para><b>Anlatıya dönüş SONDAN okunur</b> (kullanıcı kararı): koşu anlatısında ilgi çeken şey en son
    /// olandır ve panel canlı akışı izlemeye devam eder. Proje logu tersidir — bkz.
    /// <see cref="PlayCascade"/>.</para>
    ///
    /// <para><b>Geçmiş erişilebilirdir</b> (bkz. <see cref="_backlogLines"/>): render dilimi bir SINIR değil
    /// bir PENCEREdir — tepeye kaydırınca önceki dilim geri gelir, tıpkı proje logunda olduğu gibi. Kaynak
    /// farkı önemsizdir: proje logu diskten sayfalanır, anlatı ise VM'in tam tamponundan gelir ve bu metot
    /// onu zaten tam metin olarak alır.</para>
    /// <para>[design v1.7.0 §2.5] Geçiş animasyonu İKİ YÖNDE de aynıdır (<see cref="PlayTiltIn"/>).</para></summary>
    public void ShowRunDocument(string fullRunText)
    {
        ResetRunDocument(fullRunText);
        PlayTiltIn(fromAbove: true); // dönüş açılışın TAM AYNASI
    }

    /// <summary>
    /// [design v1.13.2 §2.5 · §9] <b>Yeni işlem başladı: anlatı belgesi ANINDA boşalır.</b> Konsol ve event
    /// stream her işlemde (Build · Rebuild · Resolve · Sync) temizlenir, ardından yalnız o işlemin satırları
    /// yazılır. VM kendi tamponunu <c>RunViewModel.ClearConsoleForNewOperation</c>'da siler; ekrandaki belge
    /// onu BU çağrıyla izler (kablo <c>MainWindow</c>'da, <c>RunViewModel.ConsoleCleared</c>).
    ///
    /// <para><b>Tilt YOK:</b> <see cref="PlayTiltIn"/> yalnız panel GEÇİŞİNDE (proje logu ↔ anlatı) oynar; bu
    /// ise aynı panelin sıfırlanmasıdır — <see cref="ShowRunDocument"/>'ın tilt'siz çekirdeği. "ready" satırına
    /// dokunulmaz: ilk anlatı satırı gelince metni zaten boşalır (<see cref="ClearReadyText"/>).</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL — ölçüldü]</b> Eskiden işlem başlangıcında ekrana hiç dokunulmuyordu: VM
    /// tamponu silinse de AvalonEdit belgesi yalnız mod geçişinde yeniden kuruluyordu, yeni işlemin satırları
    /// bir öncekinin ALTINA ekleniyordu — Build ve Sync'te konsol "hiç temizlenmiyor" diye görülen buydu.</para>
    /// </summary>
    public void ClearRunDocument() => ResetRunDocument("");

    /// <summary>Anlatı belgesini verilen metinle yeniden kurar (render dilimi + chunk loader + dip pini +
    /// takip): <see cref="ShowRunDocument"/> ile <see cref="ClearRunDocument"/>'ın ORTAK gövdesi (kopya YASAK).</summary>
    private void ResetRunDocument(string fullRunText)
    {
        _projectMode = false;
        _armedForChunk = false; // ilk layout'ta spurious prepend olmasın (kullanıcı henüz kaydırmadı)
        _backlogLines = SplitLines(fullRunText ?? "");
        // Render dilimi: son RenderSliceLines satır belgeye; öncesi chunk loader'a bırakılır (PlayCascade ile
        // AYNI hesap — iki mod tek kuralı paylaşır).
        _loadedFrom = Math.Max(0, _backlogLines.Count - RenderSliceLines);
        EditorControl.Document = new TextDocument(Join(_backlogLines, _loadedFrom, _backlogLines.Count));
        PinAfterModeSwitch(toBottom: true);
        // [SIRA ÖNEMLİ] Takibi devralmak pin'den SONRA gelir. Kullanıcı önceki modda serbest kaydırmış olsa
        // bile mod değişimi takibi yeniden alır — ama ForceStuck bir bildirim yayınlar ve pill'in görünürlüğü
        // yalnız DİPTEN UZAKLIĞA bakar (BottomAnchorDecision.ShouldShowPill). Pin'den ÖNCE çağrıldığında editör
        // hâlâ ÖNCEKİ belgeyi (proje logu, tepede) tutuyor, uzaklık kocaman çıkıyor ve `⌄ latest` tam o karede
        // beliriyor, pin bitince kayboluyordu — sahada "geri diyorsun latest çıkıyor kayboluyor" diye görülen
        // buydu. Pin'den SONRA geometri doğrudur: uzaklık sıfır, pill hiç çıkmaz.
        _bottomAnchor.ForceStuck(true);
        RefreshPrompt(); // anlatıya dönüldü → prompt satırı geri gelir
    }

    // ---------------------------------------------------------------- proje-log kaskatı

    /// <summary>
    /// [design v1.7.0 §2.5] Proje-log moduna geçiş. İçerik <see cref="PlayTiltIn"/> ile TEK PARÇA serilir ve
    /// chunk loader kurulur. Motion TAZE okunur.
    ///
    /// <para><b>Proje logu BAŞTAN okunur</b> (kullanıcı kararı, §5.1'den bilinçli sapma): bir derleme logunda
    /// aranan şey ilk hatadır, son satır değil. Takip de bu yüzden KAPALI açılır — açık olsaydı gelen ilk
    /// canlı satır okuyucuyu hemen dibe fırlatırdı. Kullanıcı kendi eliyle dibe inerse takip geri gelir
    /// (bu, herhangi bir kullanıcı kaydırmasıyla aynı kuraldır).</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL] Sayfanın sonunda "build in progress ▮" işareti YOKTUR.</b> Eskiden derlenen bir
    /// projenin logu açıldığında sona amber, yanıp sönen bir işaret konurdu. Ölçülen sorun: işaret açılış ANINDA
    /// kurulup bir daha güncellenmiyordu — proje kullanıcı loguna bakarken bitince "build in progress" ekranda
    /// KALIYORDU. İşaretin söylediği şeyi zaten iki yüzey doğru söylüyor: başlıktaki statü glyph'i/adı ve
    /// listedeki satırın kendisi. Üçüncü ve senkronu olmayan bir kanal, doğru olmadığı anlarda ekranı yalancı
    /// yapıyordu; senkronlamak yerine kaldırıldı.</para>
    /// </summary>
    public void PlayCascade(IReadOnlyList<string> allLines)
    {
        EnsureColorizer();
        _bottomAnchor.ForceStuck(false); // proje logu baştan okunur — dibe çekilmez
        allLines ??= [];
        _projectMode = true;
        RefreshPrompt();                 // proje-log modunda prompt imleci yoktur
        _idleReady = false;
        ActiveLineText.Text = "";
        _armedForChunk = false;            // ilk layout'ta spurious prepend olmasın (kullanıcı henüz kaydırmadı)
        _backlogLines = [.. allLines];     // kopya: backlog canlı satırlarla BÜYÜR, çağıranın listesi değişmez

        // Render dilimi: son RenderSliceLines satır belgeye; öncesi chunk loader'a bırakılır.
        _loadedFrom = Math.Max(0, _backlogLines.Count - RenderSliceLines);
        EditorControl.Document = new TextDocument(Join(_backlogLines, _loadedFrom, _backlogLines.Count));
        PinAfterModeSwitch(toBottom: false);

        PlayTiltIn(fromAbove: false);
    }

    /// <summary>
    /// [design v1.7.0 §2.5 · animasyon spec §2] Panel geçişinin TEK hareketi. Log bloğu, <b>alt kenarı sabit
    /// kalacak şekilde</b> izleyiciye doğru düzleşir: 14px aşağıdan, hafif kısaltılmış ve saydam başlar,
    /// 340ms ease-out ile tam düz ve tam opak olur. Kâğıdın masaya oturması gibi — alt kenar hiç oynamaz.
    ///
    /// <para><b>Gerçek perspektif</b> (animasyon spec §2.4'ün "birebir gerekiyorsa" yolu): blok bir 3B
    /// düzlemdir, <c>PerspectiveCamera</c> 900px uzaklıktadır ve X ekseni etrafında 7° → 0 döner. Böylece üst
    /// kenar geriye giderken DARALIR, alt kenar öne gelirken GENİŞLER — trapez. Bir ara sürüm bunu 2B
    /// ölçek+kaydırma ile yaklaşıklıyordu; WPF'in 2D dönüşümleri afin olduğundan trapez üretilemiyor ve jest
    /// "kâğıdın masaya oturması" değil salt "aşağıdan kayma" gibi okunuyordu.</para>
    ///
    /// <para>Düzlemin dokusu, geçişin başında log bloğundan alınan bir görüntüdür. Canlı bir görsel fırçası
    /// (VisualBrush) kullanılamaz: fırça kaynağını olduğu gibi çizer, yani gerçek bloğu gizlemek onu fırçada
    /// da gizlerdi. Doku 340ms sürer; sonrasında gerçek editör geri gelir ve metin yeniden keskindir.</para>
    ///
    /// <para>Hareket TEK PARÇADIR: satır sayısından bağımsız olarak her zaman aynı sürede biter. Panelin
    /// yüksekliği/yerleşimi geçiş boyunca DEĞİŞMEZ (spec §2.4) — 3B katman yerleşimden bağımsız çizilir.</para>
    ///
    /// <para>Yalnız log bloğu REMOUNT edildiğinde oynar (proje logu açma / <c>← Back</c> / proje değişimi /
    /// boş-durum) — canlı satır eklenirken, kaydırırken ya da yeniden boyutlanırken ASLA (spec §2.1/§3).
    /// Reduced-motion iken hiç oynatılmaz (motion sözleşmesi).</para>
    /// </summary>
    private void PlayTiltIn(bool fromAbove)
    {
        int generation = ++_tiltGeneration;
        void Land()
        {
            if (generation != _tiltGeneration) return; // yeni bir geçiş devraldı
            PART_Tilt3D.Visibility = Visibility.Collapsed;
            PART_Tilt3D.Children.Clear(); // dokuyu ve sahneyi bırak (geçiş başına bir görüntü tutulmaz)
            PART_TiltHost.Opacity = 1.0;
            SettleAtBottomIfFollowing();
        }

        // [A13/T1] motion sinyalinin TEK kapısı (MotionGate seam'i). Doku alınamıyorsa (panel henüz
        // ölçülmemiş) animasyon da yoktur — içerik doğrudan son hâlinde görünür, asla boş kalmaz.
        if (!_motion.Enabled || BuildTiltScene(fromAbove) is not { } scene) { Land(); return; }

        var duration = TimeSpan.FromMilliseconds(TiltInMs);
        var ease = MotionTokens.ResolveKeySpline(this, "KeySpline.EaseOut", new KeySpline(0.22, 1, 0.36, 1));

        PART_TiltHost.Opacity = 0.0;              // gerçek blok geçiş boyunca 3B katmanın yerini bırakır
        PART_Tilt3D.Opacity = 0.0;
        PART_Tilt3D.Visibility = Visibility.Visible;

        // Üst kenar GERİYE gider: WPF'te +X ekseni etrafında pozitif dönüş üstü izleyiciye DOĞRU getirir,
        // bu yüzden işaret negatiftir (prototip: rotateX(7deg) üstü geriye yatırır).
        scene.Rotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, MotionTokens.SplineTo(0.0, duration, ease));
        // CSS'te +Y aşağıdır, WPF'te yukarı: translateY(14px) = -14 birim.
        scene.Translate.BeginAnimation(TranslateTransform3D.OffsetYProperty, MotionTokens.SplineTo(0.0, duration, ease));

        var fade = MotionTokens.SplineTo(1.0, duration, ease);
        fade.Completed += (_, _) => Land();
        PART_Tilt3D.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// Log bloğunun görüntüsünü 900px perspektifli bir kameranın önündeki düzleme yerleştirir ve sahneyi
    /// <see cref="PART_Tilt3D"/>'ye kurar; menteşe düzlemin ALT kenarındadır.
    ///
    /// <para>Kamera düzlemden <see cref="TiltInPerspectivePx"/> uzaktadır ve görüş açısı, dönüş sıfırken
    /// düzlemin viewport'u TAM olarak doldurmasına göre seçilir — CSS <c>perspective</c>'in tanımı budur ve
    /// geçişin sonunda 2B içerikle birebir örtüşmeyi (pop olmamasını) o sağlar.</para>
    ///
    /// <para>Panel ölçülmemişse (genişlik/yükseklik 0 — headless ya da ilk kare) <c>null</c> döner.</para>
    /// </summary>
    private TiltScene? BuildTiltScene(bool fromAbove)
    {
        double w = PART_TiltHost.ActualWidth, h = PART_TiltHost.ActualHeight;
        if (w <= 0 || h <= 0) return null;

        var dpi = VisualTreeHelper.GetDpi(this);
        var texture = new RenderTargetBitmap(
            (int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY),
            96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        texture.Render(PART_TiltHost);
        texture.Freeze();

        double halfW = w / 2, halfH = h / 2;
        var mesh = new MeshGeometry3D
        {
            Positions = [new(-halfW, halfH, 0), new(halfW, halfH, 0), new(halfW, -halfH, 0), new(-halfW, -halfH, 0)],
            TextureCoordinates = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)],
            TriangleIndices = [0, 3, 2, 0, 2, 1],
        };

        // [tam ayna] Açılış: 14px AŞAĞIDAN, menteşe ALT kenarda, ÜST kenar geride (açı negatif).
        // Dönüş:  14px YUKARIDAN, menteşe ÜST kenarda, ALT kenar geride (açı pozitif). Süre/eğri/açı AYNI.
        double sign = fromAbove ? 1 : -1;
        var rotation = new AxisAngleRotation3D(new Vector3D(1, 0, 0), sign * TiltInAngleDeg);
        var translate = new TranslateTransform3D(0, sign * TiltInOffsetPx, 0);
        var model = new GeometryModel3D(mesh, new DiffuseMaterial(new ImageBrush(texture)))
        {
            // Sıra CSS'in okunuşuyla aynı: önce translateY, sonra rotateX (menteşe alt kenarda).
            Transform = new Transform3DGroup { Children = { translate, new RotateTransform3D(rotation, 0, sign * halfH, 0) } },
        };

        var scene = new Model3DGroup();
        // Varsayılan ambient ışık beyazdır: doku tam parlaklıkta ve gölgelenmesiz çizilir. Emissive bir
        // materyal ışık gerektirmezdi ama toplamalı çizerdi — log bloğunun zemini saydam olduğu için
        // (zemin dış Grid'e aittir, geçişe katılmaz) renkler panelin üstünde yanlış çıkardı.
        scene.Children.Add(new AmbientLight());
        scene.Children.Add(model);

        PART_Tilt3D.Children.Clear();
        PART_Tilt3D.Children.Add(new ModelVisual3D { Content = scene });
        PART_Tilt3D.Camera = new PerspectiveCamera(
            new Point3D(0, 0, TiltInPerspectivePx), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0),
            fieldOfView: 2 * Math.Atan(halfW / TiltInPerspectivePx) * 180 / Math.PI);

        return new TiltScene(rotation, translate);
    }

    /// <summary>Geçişin sürülen iki 3B parçası — açı (menteşe) ve dikey kayma.</summary>
    private readonly record struct TiltScene(AxisAngleRotation3D Rotation, TranslateTransform3D Translate);

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
        RefreshHoverBand(); // [M-2 review round 1] imleç kımıldamadan scroll olursa bant ESKİ satırda asılı kalmasın
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
    /// yalnız gerçek bir tepeye-scroll önceki chunk'ı yükler.
    /// <para><b>Mod ayrımı YOKTUR:</b> jest anlatıda da proje logunda da aynıdır ve karar tek bir soruya
    /// bakar — backlog'da daha eski satır kaldı mı (<see cref="_loadedFrom"/> &gt; 0). Kapı eskiden proje
    /// moduna bağlıydı ve run anlatısında geçmişe dönmenin hiçbir yolu yoktu.</para></summary>
    internal void EvaluateChunkScroll(double verticalOffset)
    {
        if (_prepending) return;
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

    /// <summary>[Test] Geçiş boyunca log bloğunun yerini alan 3B katman.</summary>
    internal Viewport3D Tilt3D => PART_Tilt3D;

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
        string chunk = Join(_backlogLines, from, _loadedFrom);
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

    // Metni satırlara böler (ayraçlar ATILIR — Join onları geri koyar). Sondaki '\n'in doğurduğu boş kuyruk
    // parçası satır SAYILMAZ: append sözleşmesi gereği canlı metin '\n' ile biter, yani o parça bir satır
    // değil bir sonektir. Boş metin → boş liste.
    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        if (text.Length == 0) return lines;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') { lines.Add(text[start..i]); start = i + 1; }
        if (start < text.Length) lines.Add(text[start..]); // '\n' ile bitmeyen son parça (savunmacı)
        return lines;
    }

    // Metnin SON `count` satırı — kırpılan bloğun yalnız backlog'a girmemiş kuyruğunu almak için.
    private static List<string> LastLines(string text, int count)
    {
        var lines = SplitLines(text);
        return count >= lines.Count ? lines : lines.GetRange(lines.Count - count, count);
    }

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

    // ---------------------------------------------------------------- [Task 6] ok imleç + satır hover bandı

    /// <summary>
    /// [Task 6/design v1.17.0 §9 "3 — Konsol ve event stream"] Konsol gövdesinin imleci standart OK'tur — el
    /// işareti yalnız tıklanabilir öğelere aittir, bu panelde metin I-beam'i istenmedi (spec'in kendi "Denenen ve
    /// bırakılan" notu).
    ///
    /// <para><b>Neden yönlendirilmiş bir olay, statik <c>Cursor</c> özelliği değil:</b> AvalonEdit'in
    /// <c>SelectionMouseHandler</c>'ı (decompile ile doğrulandı) IBeam'i — ve sürükle-bırak sırasında Arrow'u —
    /// <c>TextArea.QueryCursor</c> yönlendirilmiş olayını ELE ALARAK dayatır; <c>TextArea</c>/<c>TextView</c>
    /// hiçbir yerde kendi <c>Cursor</c> özelliğini ATAMAZ. Statik <c>Cursor</c> bu yüzden yalnız AvalonEdit'in
    /// hiç ele almadığı konumlarda (editör sınırlarının dışı) işe yarar — asıl metin gövdesinde etkisizdir.</para>
    ///
    /// <para>Aynı olay burada üst atada (<c>EditorControl</c>) <c>handledEventsToo:true</c> ile YENİDEN
    /// yakalanır: kabarcıklanma <c>TextView</c>'den başlar, AvalonEdit'in kararından (ele alınmış ya da değil)
    /// SONRA buraya ulaşır. <b>[I-1 review round 1 — DEĞİŞEN KURAL]</b> Eski hâli SON SÖZÜ HER ZAMAN biz
    /// söylüyorduk (ne olursa olsun Arrow) — ama AvalonEdit'in <c>EnableHyperlinks</c>'i varsayılan AÇIKTIR
    /// (`src/` içinde hiç kapatılmadı) ve Ctrl basılıyken bir bağlantının üstünde <c>Cursors.Hand</c> döner; eski
    /// davranış "el işareti yalnız tıklanabilir öğelere aittir" kuralını bağlantılarda BOZUYORDU. Doğrusu:
    /// yalnız IBeam'i (ya da hiç ele alınmamış/boş kararı) Arrow'a çevir, Hand (ve AvalonEdit'in kararı verdiği
    /// başka her şeyi) OLDUĞU GİBİ bırak. <c>internal</c>: testler üretimin ÇAĞIRDIĞI metodun ta kendisini
    /// gerçek bir <c>RaiseEvent</c> ile tetikleyebilsin.</para>
    /// </summary>
    internal void ForceArrowCursor(object sender, QueryCursorEventArgs e)
    {
        if (e.Cursor is not null && e.Cursor != Cursors.IBeam) return; // Hand (bağlantı) vb. KORUNUR
        e.Cursor = Cursors.Arrow;
        e.Handled = true;
    }

    // [M-2 review round 1] En son GERÇEK MouseMove'un TextView-yerel Y'si — imleç kımıldamadan içerik kayarsa
    // (scroll/ekleme/chunk-dikiş/mod değişimi) bunu yeniden besleyerek bandı tazeleriz (bkz. RefreshHoverBand).
    // null = imleç editörün üzerinde değil (MouseLeave'den beri hiç MouseMove gelmedi).
    private double? _lastMouseYInTextView;
    // [I-2 review round 1] Son bantlanan satırın (Top,Height) çifti — fare AYNI satır aralığında kaldığı sürece
    // (en sık durum: sürekli MouseMove akışı) VisualLines taranmaz, dönüşüm alınmaz, renk geçişi YENİDEN
    // KURULMAZ. null = şu an bant GÖRSEL OLARAK GİZLİ (HideHoverBandVisual'ın idempotency guard'ı bunu okur —
    // review round 2: bu alanı "gizli mi" DIŞINDA bir anlamda KULLANMA, aşağıdaki _forceHoverRefresh'in
    // varlığı tam olarak bu yüzden — bkz. doc'u).
    private (double Top, double Height)? _hoveredLine;
    // [M-2 review round 2] RefreshHoverBand'ın "aynı satır" kısayolunu (I-2 perf) BİLEREK atlatması için AYRI bir
    // bayrak — _hoveredLine'ı bu amaçla temizlemek YANLIŞTI: HideHoverBandVisual'ın idempotency guard'ı da AYNI
    // alana bakıyor ("null = zaten gizli") ve önbelleği force-temizleyip sonra hiçbir satır bulunamayan bir
    // yapısal tazelemede (ör. çok daha kısa bir belgeye geçiş) guard bunu "zaten gizliymiş" sanıp GERÇEK gizleme
    // çağrısını SESSİZCE YUTUYORDU — bant eski (artık geçersiz) konumunda GÖRSEL OLARAK asılı kalıyordu. Bu
    // bayrak yalnız "önbelleği bu bir seferliğine atla" der, görünürlük durumuna hiç dokunmaz.
    private bool _forceHoverRefresh;

    /// <summary>[Test/I-2] Bandın renk hedefini GERÇEKTEN kaç kez değiştirdiğimiz — komşu satır içi
    /// <c>MouseMove</c>'ların animasyonu YENİDEN KURMADIĞINI kanıtlamak için.</summary>
    internal int HoverColorTransitionCount { get; private set; }

    /// <summary>
    /// [Task 6/design v1.17.0 §9] İmlecin altındaki satırı tam genişlik, doğrudan <c>Brush.Surface</c> zeminli
    /// bir bantla işaretler — event stream'in satır hover'ıyla AYNI algısal adım ("iki panelde hover adımı
    /// eşittir"). Hedef satırın hesabı saf <see cref="ConsoleHoverBand.LineAt"/>'a çıkarılmıştır (renderer'sız
    /// test edilebilir); burası yalnız GERÇEK <c>TextView.VisualLines</c> listesini ona besler ve sonucu
    /// <see cref="HoverBand"/>'ın Margin/Height'ına uygular.
    ///
    /// <para><b>Neden bir Y PARAMETRESİ, gerçek <c>MouseMove</c>'dan okuma değil:</b>
    /// <see cref="EvaluateChunkScroll"/> ile AYNI desen — gerçek <c>MouseDevice</c> konumu (OS imlecinin gerçek
    /// ekran konumu) headless'ta simüle edilemez; üretim kablosu (ctor) konumu ÇIKARIP buraya geçer, testler
    /// üretimin çağırdığı metodu doğrudan sürer. <paramref name="mouseYInTextView"/> <c>TextView</c>-yerel bir
    /// koordinattır ve dolgu (Padding) içindeyken NEGATİF ya da <c>ActualHeight</c>'ı AŞAN bir değer olabilir
    /// (M-1: kaynak artık <c>EditorControl</c>, dolgu dahil her yer) — belge-uzayına çevrilirken bu sorun
    /// çıkarmaz, yalnız EKRANA çizilen bant <c>TextView</c>'in kendi dikey sınırlarına KIRPILIR (aşağıda).</para>
    ///
    /// <para><b>[I-2 review round 1] Aynı satır içindeki tekrar çağrılar ucuzdur:</b> <see cref="_hoveredLine"/>
    /// önbelleği belge-Y hâlâ son bantlanan satırın aralığındaysa <c>VisualLines</c> taranmadan, dönüşüm
    /// alınmadan, kaynak sözlüğü sorgulanmadan hemen döner — sürekli gelen <c>MouseMove</c> akışının satır İÇİNDE
    /// hiçbir iş YAPMAMASını sağlar. Farklı bir satıra geçildiğinde <see cref="MotionTokens.TransitionColor"/>
    /// çağrılır; O metot da ARTIK (aynı review) zaten hedef renkteyse yeniden animasyon KURMAZ — guard TEK yerde,
    /// kopya YASAK. <b>[M-2 review round 2]</b> Bu kısayol yalnız gerçek <c>MouseMove</c>'dan (aynı çağrı içinde
    /// hem Y HEM scroll offset sabit) gelen tekrar çağrılar için güvenlidir — <see cref="RefreshHoverBand"/>
    /// SCROLL sonrası çağırdığında önbelleği ÖNCE temizler (aşağıda), aksi halde satır KİMLİĞİ değişmese bile
    /// (belge-Y hâlâ aynı aralıkta — ör. bir satırdan küçük bir scroll) EKRANDAKİ konumu güncellenmez ve bant
    /// içerikten kopup asılı kalırdı.</para>
    ///
    /// <para><b>[M-1 review round 1] Bant <c>TextView</c>'in kendi dikey sınırlarına KIRPILIR:</b> panel kenardan
    /// kenara tam genişlik olsa da (<c>HorizontalAlignment="Stretch"</c>), üstte/altta KISMEN görünen bir satırın
    /// gerçek yüksekliği <c>TextView.ActualHeight</c>'ı aşabilir (viewport'un tam ortasında değilse) — kırpma
    /// olmadan bant üst 8px dolguya ya da alt kaydırma çubuğu track'ine TAŞARDI. Sağlık kontrolü olarak, kırpılan
    /// aralık boşsa (panel tamamen dışına düşen bir konum) bant gizlenir.</para>
    ///
    /// <para><b>[M-2 review round 2]</b> Bir eşleşme bulunamayan içsel yollar (<c>VisualLinesValid==false</c>,
    /// <see cref="ConsoleHoverBand.LineAt"/> null, kırpılan aralık boş) yalnız GÖRSEL olarak gizler
    /// (<see cref="HideHoverBandVisual"/>) — <see cref="_lastMouseYInTextView"/>'i SİLMEZ. Gerekçe: AvalonEdit
    /// <c>ScrollOffsetChanged</c>'i YENİDEN ÖLÇÜMDEN ÖNCE yayınlar (decompile ile doğrulandı —
    /// <c>IScrollInfo.SetVerticalOffset</c> önce olayı ateşler, <c>InvalidateMeasure</c> SONRA gelir); eski
    /// viewport'un çok ötesine tek seferde atlayan bir scroll'da (büyük bir takip batch'i, dibe anlık zıplama) bu
    /// yol o anda GERÇEKTEN "eşleşme yok" der — ama imleç hâlâ editörün üzerindedir. Konumu silmek, biraz sonra
    /// gelen GERÇEK <c>VisualLinesChanged</c>'in (yeniden ölçüm bitince) bandı doğru satırda GERİ GETİRMESİNİ
    /// engellerdi. Yalnız gerçek <c>MouseLeave</c> (<see cref="HideHoverBand"/>) konumu siler.</para>
    ///
    /// <para>Ek saat AÇMAZ (ARCHITECTURE §14.5, boşta-saat kuralı): yalnız çağrıldığında çalışır, boşta hiçbir
    /// şey koşmaz.</para>
    /// </summary>
    internal void UpdateHoverBand(double mouseYInTextView)
    {
        _lastMouseYInTextView = mouseYInTextView;
        var view = EditorControl.TextArea.TextView;
        if (!view.VisualLinesValid || view.VisualLines.Count == 0) { HideHoverBandVisual(); return; }

        double documentY = mouseYInTextView + view.ScrollOffset.Y;

        // [I-2] Aynı satır aralığında kalınıyorsa (satır içi piksel hareketleri) hiçbir şey yeniden hesaplanmaz.
        // [M-2 review round 2] RefreshHoverBand bu kısayolu _forceHoverRefresh ile BİLEREK atlatır (yapısal bir
        // tazelemede satır KİMLİĞİ aynı kalsa bile EKRAN geometrisi yeniden hesaplanmalıdır) — bayrak burada
        // TÜKETİLİR, önbelleğin KENDİSİNE (_hoveredLine) hiç dokunulmaz: o alan yalnız "bant şu an görünür mü"
        // sorusunun tek doğruluk kaynağıdır (HideHoverBandVisual'ın idempotency guard'ı da ona bakar).
        bool skipSameLineShortcut = _forceHoverRefresh;
        _forceHoverRefresh = false;
        if (!skipSameLineShortcut && _hoveredLine is { } cached &&
            documentY >= cached.Top && documentY < cached.Top + cached.Height)
            return;

        var lines = new List<(double Top, double Height)>(view.VisualLines.Count);
        foreach (var visual in view.VisualLines) lines.Add((visual.VisualTop, visual.Height));

        if (ConsoleHoverBand.LineAt(lines, documentY) is not { } line) { HideHoverBandVisual(); return; }
        _hoveredLine = line;

        // [PositionPrompt deseni] Referans TİLT KABIDIR (PART_TiltHost), ConsoleView değil — editör ve bant
        // aynı kabın içindedir, aralarındaki mesafe geçiş animasyonundan ETKİLENMEZ.
        var toHost = view.TransformToAncestor(PART_TiltHost);
        double top = toHost.Transform(new Point(0, line.Top - view.ScrollOffset.Y)).Y;
        // [M-1] TextView'in kendi dikey sınırları — bant bunun DIŞINA taşamaz.
        double viewTop = toHost.Transform(new Point(0, 0)).Y;
        double viewBottom = toHost.Transform(new Point(0, view.ActualHeight)).Y;
        if (!double.IsFinite(top) || !double.IsFinite(viewTop) || !double.IsFinite(viewBottom)) { HideHoverBandVisual(); return; }

        double clippedTop = Math.Max(top, viewTop);
        double clippedBottom = Math.Min(top + line.Height, viewBottom);
        if (clippedBottom <= clippedTop) { HideHoverBandVisual(); return; } // panelin tamamen dışında

        HoverBand.Height = clippedBottom - clippedTop;
        HoverBand.Margin = new Thickness(0, clippedTop, 0, 0);
        HoverColorTransitionCount++;
        MotionTokens.TransitionColor(this, _hoverBandBrush, ResolveHoverBandColor());
    }

    /// <summary>[M-2 review round 1 · round 2] İmleç kımıldamadan bandı etkileyen bir şey olursa (scroll,
    /// ekleme, chunk-dikiş, mod değişimi) en son bilinen GERÇEK <c>MouseMove</c> konumuyla
    /// <see cref="UpdateHoverBand"/> yeniden çağrılır — imleç <c>TextView</c>-yerel EKRAN konumu scroll'dan
    /// ETKİLENMEZ (yalnız hangi satırın o pikselde durduğu değişir), bu yüzden canlı <c>Mouse.GetPosition</c>
    /// sorgusuna gerek YOKTUR ve bu yol headless'ta da test edilebilir kalır. İmleç editörün üzerinde değilse
    /// (<see cref="_lastMouseYInTextView"/> null — yalnız GERÇEK <see cref="HideHoverBand"/> onu temizler)
    /// hiçbir şey yapmaz: uzaktaki bir scroll bandı GERİ GETİRMEMELİDİR.
    ///
    /// <para><b>[review round 2 — DEĞİŞEN KURAL]</b> Önbellek (<see cref="_hoveredLine"/>) burada ÖNCE
    /// temizlenir. Eski hâli <see cref="UpdateHoverBand"/>'a doğrudan devrediyordu ve belge-Y hâlâ eski satırın
    /// aralığındaysa (bir satırdan KÜÇÜK bir scroll — animasyonlu kaydırmanın ara kareleri gibi) o metodun kendi
    /// "aynı satır" kısayoluna TAKILIYORDU: satır KİMLİĞİ değişmemiş sayılıyor, ekran konumu YENİDEN
    /// HESAPLANMIYORDU — bant içerikle birlikte kaymak yerine eski pikselde asılı kalıyordu. Aynı sorun bir
    /// belge değişiminde de (mod değişimi, aynı offsette çok daha kısa bir belge) oluşurdu: eski (Top,Height)
    /// artık YANLIŞ bir satırı (ya da hiç var olmayan bir satırı) tarif ederken kısayol onu sorgusuzca
    /// KORUYORDU. Yapısal bir tazeleme YAPISAL OLARAK farklı bir andır — satır kimliği aynı kalsa bile ekran
    /// geometrisi (dolayısıyla Margin/Height) YENİDEN hesaplanmalıdır; bu yüzden önbellek her seferinde
    /// atlanır, MouseMove'un satır-içi kısayolu ise (I-2 perf) dokunulmadan kalır.</para></summary>
    private void RefreshHoverBand()
    {
        if (_lastMouseYInTextView is not { } y) return;
        // [review round 2 düzeltmesi] Önbelleği (_hoveredLine) DEĞİL, ayrı bir bayrağı işaretler — o alan aynı
        // zamanda "bant görünür mü" sorusunun tek doğruluk kaynağıdır; onu burada temizlemek
        // HideHoverBandVisual'ın idempotency guard'ını "zaten gizliymiş" sanıp GERÇEK bir gizleme çağrısını
        // sessizce yutmasına yol açıyordu (ölçüldü: belge kısalınca bant eski konumunda görsel olarak asılı
        // kalıyordu, sayaç hiç artmıyordu).
        _forceHoverRefresh = true;
        UpdateHoverBand(y);
    }

    /// <summary>Fare panelden (dolgu dahil, M-1) çıkınca bant kalkar (design v1.17.0 §9) —
    /// <c>EditorControl.MouseLeave</c>'e kablanır. <see cref="_lastMouseYInTextView"/>'i SİLEN TEK yer burasıdır
    /// (review round 2 M-2) — <see cref="UpdateHoverBand"/>'ın içsel "eşleşme yok" yolları yalnız
    /// <see cref="HideHoverBandVisual"/> çağırır, konumu SAKLAR.</summary>
    private void HideHoverBand()
    {
        _lastMouseYInTextView = null;
        HideHoverBandVisual();
    }

    /// <summary>Bandın GÖRSEL gizlenmesi — hafızadaki son imleç konumuna DOKUNMAZ (review round 2 M-2:
    /// AvalonEdit'in <c>ScrollOffsetChanged</c>'i yeniden ölçümden ÖNCE yayınladığı köşe durumda, o anki
    /// eşleşme-yok yalnız GEÇİCİDİR — biraz sonra gelecek gerçek <c>VisualLinesChanged</c> bandı doğru satırda
    /// geri getirebilsin diye konum saklı kalır).</summary>
    private void HideHoverBandVisual()
    {
        if (_hoveredLine is null) return; // zaten gizli — art arda gelen çağrılarda tekrar tetiklenmez
        _hoveredLine = null;
        HoverColorTransitionCount++;
        MotionTokens.TransitionColor(this, _hoverBandBrush, Colors.Transparent);
    }

    private Color ResolveHoverBandColor() =>
        TryFindResource("Brush.Surface") is SolidColorBrush brush ? brush.Color : Colors.Transparent;
}
