using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.App.Graph;

/// <summary>
/// [quiet] design v1.3.0 §2.3 dependency graph — "quiet graph".
///
/// <para><b>Panel içinin sözleşmesi:</b> isimsiz mini düğümler katman bantlarında dizilir, yerleşim
/// PANEL ÖLÇÜSÜNÜN fonksiyonudur (<see cref="QuietGraphLayout"/>) ve graf her boyutta tam sığar — scrollbar
/// yoktur. Ad, düğümün üstünde değil hover tooltip'inde ve seçim etiketinde yaşar. Bağımlılık çizgileri
/// yalnız seçim varken çizilir. Kamera kendiliğinden hareket etmez: yalnız seçim onu odaklar.</para>
///
/// <para><b>Yerleşim SetGraph'ta bir kez hesaplanmaz</b> — <c>Ground.SizeChanged</c> her ölçü değişiminde
/// yeniden hesaplar ve görselleri YERİNDE günceller (yeniden kurmaz): splitter sürüklenirken saniyede
/// onlarca ölçü olayı gelir ve her birinde yüzlerce düğümü baştan inşa etmek paneli dondururdu.</para>
///
/// <para><b>Cull YOKTUR ve gerekmez:</b> graf panele tam sığdığı için varsayılan görünümde her düğüm
/// görünür alandadır — eleyecek bir şey kalmaz. Bu yüzden her düğüm <see cref="SetGraph"/>'ta kurulur.</para>
///
/// <para><b>Motion sözleşmesi:</b> her animasyon başlangıcında <see cref="AnimationsEnabledProvider"/> TAZE
/// okunur (varsayılan <c>App.Motion</c>); reduced-motion'da kamera geçişi ve açılış dalgası yoktur.
/// Süre/eğri token'ları <c>Duration.*</c>/<c>KeySpline.*</c> anahtarlarından ya da bu sınıfın adlandırılmış
/// sabitlerinden, renkler <c>Brush.*</c> anahtarlarından (<c>SetResourceReference</c>) gelir — hex/ms
/// gömülmez.</para>
/// </summary>
public partial class GraphView : UserControl
{
    /// <summary>[quiet] Açılış dalgasında DÜĞÜM başına gecikme (§2.3: "gecikme = build-order index × 9ms").
    /// <b>Eski kural KATMAN başınaydı</b> (55ms/katman, tavan 330) — v1.3.0 dalgayı derleme sırasına bağladı,
    /// yani dalga üstten alta VE soldan sağa akar.</summary>
    public const double RevealStepMs = 9.0;
    /// <summary>Dalganın tavanı (§2.3: "max 520ms") — 58. düğümden sonrası aynı anda belirir.</summary>
    public const double RevealDelayCapMs = 520.0;
    /// <summary>Bir düğümün beliriş süresi (prototip <c>bo-reveal .3s</c>). [W2 fix-1] Değer
    /// <see cref="RevealStagger.RevealMs"/>'in derleme-zamanı ALIAS'ıdır — liste satırıyla (ProjectRow) ASLA
    /// sürüklenemez; ikisi de AYNI <c>bo-reveal</c> ailesindendir.</summary>
    public const double RevealMs = RevealStagger.RevealMs;
    /// <summary>Düğüm bu kadar YUKARIDAN gelir (prototip <c>translateY(-5px)</c>) — aynı gerekçe, alias.</summary>
    public const double RevealRisePx = RevealStagger.RevealRisePx;

    /// <summary>[quiet] Seçim varken odak kümesi DIŞINDA kalan her şeyin opaklığı (§2.3: "odak kümesi tam
    /// opak, geri kalan HER ŞEY opacity 0.1"). <b>Eski değer 0.25'ti</b> — v1.3.0 grafı daha sessiz istiyor.</summary>
    public const double UnfocusedNodeOpacity = 0.1;
    /// <summary>Dekoratif sonsuz animasyonlarda kare hızı tavanı (feasibility §3.4).</summary>
    /// <summary>Düğüm karesinin çerçeve kalınlığı (§2.3: "1.5px border").</summary>
    public const double NodeBorderThickness = 1.5;
    /// <summary>Seçili düğüm karesinin çerçeve kalınlığı (DS: 2px).</summary>
    public const double SelectedNodeBorderThickness = 2.0;
    /// <summary>Düğüm karesinin köşe yarıçapı — DS <c>--radius-sm</c> (§2.3: "radius-sm").</summary>
    public const double NodeCornerRadius = 4.0;
    /// <summary>Seçim halkasının kareden dışarı taşma payı: 2px offset + <b>TAM</b> kalem.
    ///
    /// <para>Prototipin halkası CSS <c>outline: 2px solid var(--focus-ring); outline-offset: 2</c>'dir —
    /// iç kenarı kareden 2px, dış kenarı 4px dışarıda. WPF'te kalem Rectangle'ın <b>İÇİNE</b> çizilir
    /// (<c>Rectangle.DefiningGeometry</c> geometriyi yarım kalem kadar içeri alır), dolayısıyla halkanın
    /// dış kenarını 4px dışarı taşıtmak için dikdörtgenin kendisi 4px büyütülür. <b>Eski değer 3'tü</b>
    /// (kalem yola ORTALANIYOR varsayılmıştı) ve halka 2px fazla sıkı duruyordu.</para></summary>
    public const double SelectionRingInset = 4.0;
    /// <summary>[quiet · taşma] Hücrenin düğüm kenarına HER YANDAN eklediği pay.
    ///
    /// <para><b>Neden var (ÖLÇÜLDÜ):</b> WPF bir çocuğu ARRANGE SLOT'una kırpar. Hücre düğüm kadarken
    /// (24px) 30px'lik seçim halkasının layout clip'i <c>(3,3,24,24)</c>, 29.6px'lik beads yörüngesininki
    /// <c>(2.8,2.8,24,24)</c> idi: düz kenarlar tamamen kırpılıyor, geriye yalnız yarıçapı büyük olduğu için
    /// kırpma dikdörtgeninin içine giren KÖŞE YAYLARI kalıyordu — kullanıcının gördüğü "tıklayınca köşelerde
    /// beliren sarı noktalar" halkanın ta kendisiydi, yörünge ise hiç görünmüyordu.</para>
    ///
    /// <para>Prototip bu sorunu hiç yaşamaz: düğüm kabı <c>width:0;height:0</c>'dır ve çocuklar mutlak
    /// konumla dışarı taşar (BuildApp.jsx:437). WPF'te aynı sonuç kabı taşmaya YETECEK kadar büyüterek
    /// alınır — tıklama alanı ise gövdede kalır, dolayısıyla büyümez.</para></summary>
    public static readonly double CellOverhang = Math.Max(
        SelectionRingInset,
        GraphBeads.OrbitGapPx + GraphBeads.StrokeThickness / 2);
    /// <summary>Glyph, düğüm kenarının bu kadarıdır (§2.3: "node'un %52'si").</summary>
    public const double IconFactor = 0.52;
    /// <summary>Glyph kalem kalınlığı (§2.3: "1.8px stroke").</summary>
    public const double IconStroke = 1.8;
    /// <summary>Hover büyütmesi.
    ///
    /// <para><b>§2.3'ün sayısı 1.7'ydi; kullanıcı kararıyla 1.5.</b> Aritmetik neden: düğüm kenarı pitch'in
    /// 0.6'sıdır, dolayısıyla 1.7× büyüyen bir düğüm <c>1.7 × 0.6 = 1.02 pitch</c> yer kaplar — yani hücresini
    /// tam doldurur ve yanındaki düğüme YAPIŞIR. 1.5'te oran 0.9 pitch olur ve komşuyla arada görünür bir
    /// boşluk kalır.</para></summary>
    public const double HoverScale = 1.5;
    /// <summary>Hover'da çerçeve kalınlığı (§2.3: "border 2px").</summary>
    public const double HoverBorderThickness = 2.0;
    /// <summary>Hover büyütmesinin süresi (§2.3: "120ms ease-out").</summary>
    public const double HoverScaleMs = 120.0;
    /// <summary>Boş zeminde bir basış-bırakışın TIKLAMA sayıldığı azami hareket (§2.3: "≤3px hareket
    /// tıklama sayılır, üstü pan"). Platform sürükleme eşiğinin yerine tasarımın kendi sayısı kullanılır.</summary>
    public const double DragThresholdPx = 3.0;

    /// <summary>Icons.xaml geometrilerinin viewBox kenarı (lucide: 24).</summary>
    private const double IconViewBox = 24.0;
    /// <summary>[T64] Düğüm ikonu (lucide "package", 24'lük viewBox). Path data ARTIK BURADA DEĞİL: geometri
    /// uygulamanın TEK ikon sözlüğünden (<c>Resources/Icons.xaml</c>) çözülür. Bu sınıf yalnız ANAHTAR bilir —
    /// aynı path'in ikinci bir kopyası kaldığı sürece iki taraf sessizce ayrışabilirdi.</summary>
    internal const string PackageIconKey = "Icon.Package";

    /// <summary>discovered düğümün kesikli çerçevesi — TEK, DONMUŞ, paylaşımlı örnek (her tick'te yeni bir
    /// koleksiyon allocate etmemek için).</summary>
    private static readonly DoubleCollection DiscoveredDash = FrozenDash([2.0, 2.0]);
    /// <summary>"dash yok" — aynı gerekçe (boş koleksiyon da bir allocation'dır).</summary>
    private static readonly DoubleCollection SolidDash = FrozenDash([]);

    /// <summary>TÜM düğümler — model + yerleşim + görsel. Sıra BESLEME sırasıdır (build-order).</summary>
    private readonly Dictionary<string, GraphNodeSlot> _slots = new(StringComparer.Ordinal);
    private readonly List<GraphNodeSlot> _slotOrder = [];
    /// <summary>Düğüm → DOĞRUDAN bağımlılıkları (yukarıdaki komşular). Seçim kenarları YÖNLÜ çizildiği
    /// için birleşik bir komşuluk kümesi yetmez.</summary>
    private readonly Dictionary<string, List<string>> _deps = new(StringComparer.Ordinal);
    /// <summary>Düğüm → DOĞRUDAN bağımlıları (aşağıdaki komşular).</summary>
    private readonly Dictionary<string, List<string>> _dependents = new(StringComparer.Ordinal);
    /// <summary>Seçimde kurulan kenar görselleri — seçim kalkınca SÖKÜLÜR.</summary>
    private readonly List<Path> _selectionEdges = [];
    /// <summary>Akan kesiklerin PAYLAŞTIĞI tek saat (en fazla komşu sayısı kadar çizgi vardır).</summary>
    private AnimationClock? _edgeFlowClock;

    private readonly ScaleTransform _cameraScale = new(1, 1);
    private readonly TranslateTransform _cameraTranslate = new();
    /// <summary>Kenarlar düğümlerin ALTINDA kalmalı — iki AYRI katman host'u. İkisi de <c>World</c>'ün
    /// çocuğudur, dolayısıyla kamera transform'u TEK ortak parent'ta kalır.</summary>
    private readonly Canvas _edgeLayer = new();
    private readonly Canvas _nodeLayer = new();
    /// <summary>TÜM düğümlerin ikonlarının PAYLAŞTIĞI ölçek. Düğüm boyutu graf genelinde tektir, dolayısıyla
    /// panel yeniden boyutlandığında tek bir nesneyi mutasyona uğratmak yeter (düğüm başına transform yok).
    /// Donmaz — donmuş bir transform güncellenemezdi.</summary>
    private readonly ScaleTransform _iconScale = new(1, 1);
    /// <summary>[quiet] TÜM beads yörüngelerinin PAYLAŞTIĞI tek saat. Düğüm boyutu graf genelinde tek
    /// olduğu için çevre de tektir ⇒ tek saat bütün noktaları faz-kilitli döndürür. N paralel derlemede N
    /// ayrı sonsuz animasyon kurmak timing engine'i gereksiz yere meşgul ederdi.</summary>
    private AnimationClock? _beadsClock;
    private BeadsGeometry _beadsGeometry;
    private DoubleCollection _beadsDash = GraphBeads.DashArrayFor(GraphBeads.For(QuietGraphLayout.MinNodeSize));
    /// <summary>Son building düğüm bittikten sonra saati bırakan TEK ATIMLIK tetik (§2.3: noktalar dönerken
    /// söner). Talep üzerine kurulur; yeni bir building doğarsa iptal edilir.</summary>
    private DispatcherTimer? _beadsSpindown;

    /// <summary>[quiet] Eğri token'ları ÖNBELLEKLENİR. Süreler bilerek her başlangıçta taze okunur
    /// (reduced-motion onları CANLI 0'a çeker) ama <c>KeySpline</c>'lar sabittir — <c>Motion.xaml</c>'in
    /// kendi başlığı bunu yazar: "0 süreli bir animasyonda eğri şekli zaten etkisizdir". Ölçüldü: 177
    /// düğümlük bir statü tick'inde düğüm başına iki kaynak yürüyüşü (354 <c>TryFindResource</c>) tick'in
    /// gözlenebilir bir bölümünü yiyordu.</summary>
    private KeySpline? _easeOut;
    private KeySpline? _easeStandard;

    private QuietLayoutResult _layout = QuietGraphLayout.Compute([], new Size(0, 0));
    private string? _selectedNode;
    /// <summary>[design v1.13.2 §2.5] Bitiş koreografisi doğarken BIRAKILAN seçim (prototip <c>focusOff</c>,
    /// BuildApp.jsx:460-468). <c>null</c> = ya hiç finale olmadı ya da seçim o zamandan beri değişti (odak
    /// normal çalışıyor). <see cref="IsFinale"/>'nin TEK okuyucusu budur — kopya YASAK.</summary>
    private string? _focusOff;
    private HashSet<string> _focusSet = new(StringComparer.Ordinal);
    private GraphRunPhase _runPhase = GraphRunPhase.Idle;
    /// <summary>[design v1.7.0 — Filtreleme] Listenin görünür kümesinin proje ADLARI; null = filtre yok.</summary>
    private IReadOnlySet<string>? _filterMatches;

    // ---- [design v1.11.0 §9-4/§9-5] koreografiler: açılış (marking) ve bitiş (neon) ----
    private MarkStep _markStep = MarkStep.None;
    private IReadOnlySet<string> _markedNodes = new HashSet<string>(StringComparer.Ordinal);
    private EndStep _endStep = EndStep.None;
    private IReadOnlySet<string> _builtNodes = new HashSet<string>(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, int> _endOrder = new Dictionary<string, int>(StringComparer.Ordinal);
    private double _endStaggerMs;
    private readonly StepPlayer _endPlayer = new();
    /// <summary>İmlecin altındaki düğüm — opaklık kararının son (ve her şeyi ezen) girdisi.</summary>
    private string? _hoveredNode;

    /// <summary>[W2] Provider + <c>MotionSettings</c> seam'i + subscribe-once kablajı TEK yerde
    /// (<see cref="MotionGate"/>). <b>latch-first</b> kipi: ilk abonelikten sonra <see cref="MotionSettings"/>
    /// ataması YOK SAYILIR — <c>MainWindow</c> bu sözleşmeye dayanır.</summary>
    private readonly MotionGate _motion;

    /// <summary>[W2] Hero + kuşak + generation-guarded release muhasebesi TEK yerde
    /// (<see cref="RevealStagger"/>) — <see cref="Controls.StickyLayerList"/> ile ORTAK.</summary>
    private readonly RevealStagger _reveal = new();

    // ---------------------------------------------------------------- jestler (§2.3 "Serbest gezinme")
    /// <summary>Zeminde sol tuş basılı; henüz sürükleme olmayabilir (eşik aşılmadıysa bu bir TIKLAMADIR).</summary>
    private bool _panPressed;
    /// <summary>Sürükleme eşiği aşıldı — pan sürüyor.</summary>
    private bool _dragging;
    /// <summary>Basışın başladığı nokta — eşik BURADAN ölçülür (her karede sıfırlanan deltadan değil).</summary>
    private Point _panOrigin;
    /// <summary>Son jest noktası; pan deltası ekran uzayındadır.</summary>
    private Point _panLast;

    public GraphView()
    {
        _motion = new MotionGate(this, latchFirst: true);
        InitializeComponent();

        World.Children.Add(_edgeLayer);
        World.Children.Add(_nodeLayer);

        // CSS `transform: translate(...) scale(...)` = önce ölçek, sonra öteleme (TransformGroup sırası birebir).
        World.RenderTransform = new TransformGroup { Children = { _cameraScale, _cameraTranslate } };
        World.RenderTransformOrigin = new Point(0, 0);
        CurrentCamera = GraphCamera.Default;
        // [quiet · görsel geçiş] Overlay (tooltip + ad etiketi) EKRAN koordinatındadır, yani konumu kameranın
        // CANLI hâlinden türer. Yalnız hedef değiştiğinde tazelemek yetmez: kamera 460ms (seçim) / 160ms
        // (wheel) boyunca ANİMASYONLA kayar ve o ara karelerde etiket hedefte, graf ise yolda olurdu. Freezable
        // Changed her ara karede ateşlenir — tek kanal, iki öğe.
        _cameraScale.Changed += OnCameraFrame;
        _cameraTranslate.Changed += OnCameraFrame;

        // Kenarlar düğümlerin ALTINDA kalmalı. Sıra AÇIKÇA ilan edilir: ekleme sırasına güvenmek, katmanlardan
        // biri sonradan yeniden eklendiğinde sessizce bozulabilirdi.
        Panel.SetZIndex(_edgeLayer, 0);
        Panel.SetZIndex(_nodeLayer, 1);

        // Jest kablosu. Basış bir sürüklemenin başı OLABİLECEĞİ için seçim kararı release'e taşınır
        // (click-vs-drag ayrımı; düğüm tıklaması Handled=true yaptığından buraya ulaşmaz). Jest mantığının
        // tamamı internal seam'lerdedir (HandlePan*/HandleWheel) ve testler onları doğrudan sürer —
        // headless'ta gerçek mouse capture ALINAMAZ (PresentationSource yok).
        Ground.MouseLeftButtonDown += (_, e) =>
        {
            // Kapı TEK yerde (HandlePanStart): jest başlayabildiyse capture alınır. Kapının kopyası buraya
            // YAZILMAZ — aksi halde "jest başlamadı ama capture alındı ve tıklama da yutuldu" deliği açılırdı.
            if (HandlePanStart(e.GetPosition(Ground))) Ground.CaptureMouse();
        };
        Ground.MouseMove += (_, e) => { if (_panPressed) HandlePanMove(e.GetPosition(Ground)); };
        // Bırakma ÖNCE işlenir, capture SONRA bırakılır: ReleaseMouseCapture senkron olarak LostMouseCapture'ı
        // yükseltir ve o yol İPTAL semantiğindedir. Ters sırada üretimde jesti iptal yolu bitirir, headless'ta
        // (capture hiç alınamaz) bırakma yolu — iki ortam ayrışırdı.
        Ground.MouseLeftButtonUp += (_, _) => { HandlePanEnd(); Ground.ReleaseMouseCapture(); };
        // Capture BAŞKA bir sebeple düşerse (Alt+Tab, popup) bu bir BIRAKMA DEĞİL İPTALDİR: jest durumu ve el
        // imleci temizlenir, seçime DOKUNULMAZ.
        Ground.LostMouseCapture += (_, _) => ResetPanGesture();
        Ground.MouseWheel += (_, e) =>
        {
            e.Handled = true;
            HandleWheel(e.GetPosition(Ground), e.Delta);
        };
        Ground.SizeChanged += (_, _) => { Relayout(); ApplyCamera(animate: false); };

        // [M-2] Canlı reduced-motion: OS ayarı koşu SIRASINDA değişirse sürmekte olan sonsuz animasyonlar
        // ANINDA durur/başlar. Abonelik kablajı MotionGate'te.
        _motion.Changed += OnAnimationsEnabledChanged;
        Unloaded += OnUnloadedReleaseClocks;

        ShowEmptyState(true);
    }

    /// <summary>Giriş eğrisi (ease-out) — ilk kullanımda çözülür, sonra önbellekten.</summary>
    private KeySpline EaseOut =>
        _easeOut ??= MotionTokens.ResolveKeySpline(this, "KeySpline.EaseOut", new KeySpline(0.22, 1, 0.36, 1));

    /// <summary>Durum değişimi eğrisi (ease-standard) — aynı gerekçe.</summary>
    private KeySpline EaseStandard =>
        _easeStandard ??= MotionTokens.ResolveKeySpline(this, "KeySpline.EaseStandard", new KeySpline(0.4, 0, 0.2, 1));

    /// <summary>[design v1.11.0 §9-4/§9-5] Yer değiştirme/örtüşen veda eğrisi (ease-in-out) — koreografilerin
    /// opaklık geçişleri bunu kullanır (§1.3: <c>cubic-bezier(.65,0,.35,1)</c>).</summary>
    private KeySpline EaseInOut =>
        _easeInOut ??= MotionTokens.ResolveKeySpline(this, "KeySpline.EaseInOut", new KeySpline(0.65, 0, 0.35, 1));

    private KeySpline? _easeInOut;

    /// <summary>Kameranın o an EKRANA uygulanmış hâli (animasyon sürüyorsa ara kare) — hedefi değil.</summary>
    private CameraTransform LiveCamera => new(_cameraScale.ScaleX, _cameraTranslate.X, _cameraTranslate.Y);

    private void OnCameraFrame(object? sender, EventArgs e)
    {
        UpdateTooltip();
        UpdateSelectionLabel();
    }

    private static DoubleCollection FrozenDash(double[] values)
    {
        var collection = new DoubleCollection(values);
        collection.Freeze();
        return collection;
    }

    /// <summary>Motion sinyalinin TAZE okunduğu kapı (D8 — sınıf statik <c>App.Motion</c>'a doğrudan bağlanmaz,
    /// testler enjekte eder). [W2] Depo <see cref="MotionGate"/>.</summary>
    public Func<bool> AnimationsEnabledProvider
    {
        get => _motion.AnimationsEnabledProvider;
        set => _motion.AnimationsEnabledProvider = value;
    }

    /// <summary>[M-2] <c>AnimationsEnabledChanged</c>'e abone olunacak kaynak; null ise <c>App.Motion</c>.
    /// <b>latch-first</b>: ilk abonelikten SONRA yapılan atama yok sayılır (bkz. <see cref="MotionGate"/>).</summary>
    public IMotionSettings? MotionSettings
    {
        get => _motion.MotionSettings;
        set => _motion.MotionSettings = value;
    }

    /// <summary>[E3/T41/DD9] Reveal stagger'ının içine girdiği hero-mutex; null ise <c>App.HeroMotion</c> (TAZE).
    /// Graf reveal ile liste reveal AYNI hero'dur (<see cref="RevealHeroKey"/>).</summary>
    public MotionCoordinator? HeroCoordinator { get; set; }

    /// <summary>Graf reveal + liste reveal ORTAK hero anahtarı (co-tetiklenir → aynı hero, birlikte oynar).</summary>
    internal const string RevealHeroKey = "sync-reveal";

    private MotionCoordinator? ActiveHeroCoordinator => HeroCoordinator ?? BuildOrchestrator.App.App.HeroMotion;

    private void OnUnloadedReleaseClocks(object? sender, RoutedEventArgs e)
    {
        // [E3/T41] Reveal ortasında unload olursa hero'yu bırak — aksi halde bir sonraki hero sonsuza dek bloke olurdu.
        _reveal.Release();
        // [M-d] Paylaşımlı beads saati ve onun spin-down tetiği: view ağaçtan düşse bile timing engine 30fps'te
        // uyanık kalır, DispatcherTimer ise view'ı (ve tüm graf ağacını) kökler.
        ReleaseBeadsClock();
        ReleaseEdgeFlowClock();
    }

    private void OnAnimationsEnabledChanged(object? sender, EventArgs e) => ReapplyMotion();

    /// <summary>Motion sinyali canlı değiştiğinde sürmekte olan sonsuz animasyonları yeni sinyale göre
    /// yeniden kurar.</summary>
    internal void ReapplyMotion()
    {
        foreach (var slot in _slotOrder)
            ApplyBeads(slot.Visual);
        if (!AnimationsEnabledProvider()) ReleaseBeadsClock();
    }

    /// <summary>Seçili düğüm (null = seçim yok). Değişince: halka + sönme + kamera güncellenir.</summary>
    public string? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (string.Equals(_selectedNode, value, StringComparison.Ordinal)) return;
            _selectedNode = value;
            // [design v1.13.2 §2.5] Bırakılan odak SEÇİM DEĞİŞİNCE düşer: yeni değer hatırlanandan
            // (_focusOff) farklıysa "final hâl" biter, odak normal çalışmasına döner (aynı projeyi yeniden
            // seçmek de bir DEĞİŞİKLİKTİR — Toggle önce null'a döner, buradan iki kez geçer).
            if (_focusOff is not null && !string.Equals(_focusOff, value, StringComparison.Ordinal))
                _focusOff = null;
            // §2.3: "Seçim değişince hover temizlenir (odak kayması sonrası imleç altında bayat hover
            // kalmaz)." Kamera 460ms'de başka bir yere gider; imleç artık o düğümün üstünde değildir.
            SetHover(null);
            ApplySelection();
            ApplyCamera(animate: true);
            SelectionChanged?.Invoke(this, value);
        }
    }

    /// <summary>
    /// [design v1.7.0 — Filtreleme] Listede etkin filtrenin (ve aramanın) eşleştirdiği proje ADLARI;
    /// <c>null</c> = filtre yok. Eşleşmeyen düğümler <see cref="GraphNodeOpacity.Unfocused"/>'a söner.
    /// Küme DIŞARIDAN verilir: eşleşme kuralı <c>ProjectFilter.Matches</c>'tir ve listeyle graf onu
    /// PAYLAŞIR — graf ikinci bir eşleme yazmaz (kopya YASAK).
    /// </summary>
    public IReadOnlySet<string>? FilterMatches
    {
        get => _filterMatches;
        set
        {
            if (ReferenceEquals(_filterMatches, value)) return;
            _filterMatches = value;
            ApplyAllOpacities(GraphNodeOpacity.FilterFadeMs); // kullanıcı hareketi — koşu tikinden uzun
        }
    }

    /// <summary>
    /// [quiet] Koşu fazı (§2.3 "Koşu yaşam döngüsü"). Değişince TÜM düğümlerin opaklığı yeniden uygulanır:
    /// koşu başlayınca graf soluklaşır, bitince (done/stopped) tümü sonuç renginde tam opak canlanır.
    /// </summary>
    public GraphRunPhase RunPhase
    {
        get => _runPhase;
        set
        {
            if (_runPhase == value) return;
            _runPhase = value;
            ApplyAllOpacities();
            // Koşuya GİRİŞ: önce sönme oynasın, görünüm değişimi arkasına alınsın (HoldStatusesUntilDimmed).
            if (value == GraphRunPhase.Running) HoldStatusesUntilDimmed();
        }
    }

    // ---------------------------------------------------------------- [design v1.11.0 §9-4] açılış koreografisi

    /// <summary>[test yüzeyi] Grafın o anki açılış-koreografisi adımı.</summary>
    internal MarkStep MarkStep => _markStep;

    /// <summary>
    /// [design v1.11.0 §9-4 <c>_beginOp</c>] Yeni bir işlem başladı — bir önceki koşunun bitiş koreografisi
    /// ANINDA kesilir. Bu, işaretleme dalgasından AYRI bir kapıdır: kapsamı boş bir işlem (ör. Sync'in hemen
    /// ardından "Everything up to date" ile biten Build) hiç dalga oynatmaz ama yine de bir işlemdir.
    /// </summary>
    public void BeginOperation() => StopEndFinale();

    /// <summary>
    /// [design v1.11.0 §9-4 · §2.3] Açılış koreografisinin adımını ve kapsamını grafa iter: node opaklıkları
    /// "örtüşen veda"yı oynar (kapsam 0.45'e 440ms'de, geri kalan 0.18'e 1120ms'de — ikisi aynı anda biter).
    ///
    /// <para>Kapsamın AMBER'a yanması ayrı bir kanal DEĞİLDİR: dalga sırasında sürücü satırların
    /// <c>Marked</c>'ını tek tek açar ve renk normal statü itişiyle (<see cref="UpdateStatuses"/>) gelir —
    /// prototipteki per-node <c>transition-delay</c>'in WPF karşılığı budur ve düğüm başına fırça animasyonu
    /// gerektirmez (bkz. ApplyNodeStatus'taki ölçülmüş sapma).</para>
    /// </summary>

    public void SetMarking(MarkStep step, IReadOnlySet<string> markedNodeNames)
    {
        ArgumentNullException.ThrowIfNull(markedNodeNames);
        _markedNodes = markedNodeNames;
        if (_markStep == step) { ApplyAllOpacities(); return; }
        _markStep = step;
        ApplyAllOpacities();
    }

    // ---------------------------------------------------------------- [design v1.11.0 §9-5] bitiş koreografisi

    /// <summary>[test yüzeyi] Grafın o anki bitiş-koreografisi adımı.</summary>
    internal EndStep EndStep => _endStep;

    /// <summary>
    /// [design v1.13.2 §2.5] <b>Bitiş koreografisi tam görünümde oynar.</b> Koreografi doğarken (Hold'dan
    /// itibaren) VE bittikten SONRA — o sıradaki seçim hâlâ <see cref="_focusOff"/>'a eşit olduğu sürece —
    /// graf seçim odağını BIRAKMIŞ sayılır: kamera, odak kümesi, seçim kenarları, node halkası ve ad
    /// etiketi hepsi "seçim yokmuş gibi" davranır (bkz. <see cref="EffectiveSelection"/>, bu beş yüzeyin
    /// TEK okuduğu yer — kopya YASAK). <b>Seçimin kendisi SİLİNMEZ</b> — yalnız ona odaklanma bırakılır ve
    /// BİR DAHA geri gelmez, ta ki kullanıcı seçimi DEĞİŞTİRENE dek (<see cref="SelectedNode"/> setter'ı
    /// <see cref="_focusOff"/>'u o an düşürür).
    ///
    /// <para>Prototip otoritesi (BuildApp.jsx:460-468): <c>const finale = !!endStep || (!!selected &amp;&amp;
    /// selected === focusOff);</c> — WPF karşılığı birebir aynı formüldür.</para>
    /// </summary>
    private bool IsFinale => _endStep != EndStep.None
        || (_selectedNode is { } s && string.Equals(s, _focusOff, StringComparison.Ordinal));

    /// <summary>Odak/kamera/kenar/halka/etiket sistemlerinin gördüğü seçim — <see cref="IsFinale"/> iken
    /// HER ZAMAN <c>null</c>. "Seçim yokmuş gibi davran" kuralının TEK kaynağı budur: beş yüzeyin hepsi
    /// <see cref="_selectedNode"/> yerine BUNU okur, kendi <c>IsFinale ? null : _selectedNode</c> kopyasını
    /// yazmaz.</summary>
    private string? EffectiveSelection => IsFinale ? null : _selectedNode;

    /// <summary>
    /// [design v1.11.0 §9-5 · §2.3] <b>"Neon tutuşma".</b> Koşu bitince YALNIZ grafta oynar: hepsi soluk
    /// bekler → bu koşuda derlenenler (succeeded ∪ failed) RANDOM sırayla düzensiz titreyerek tutuşur →
    /// nefes → kalan tüm griler birlikte belirginleşir.
    ///
    /// <para>Derlenen yoksa (ya da reduced-motion) koreografi HİÇ oynamaz — final görünüme doğrudan gidilir.</para>
    /// </summary>
    /// <param name="builtNodeNames">Bu koşuda derlenen düğümlerin adları.</param>
    /// <param name="runCount">Random sırayı tohumlayan koşu numarası.</param>
    public void PlayEndFinale(IReadOnlyList<string> builtNodeNames, int runCount)
    {
        ArgumentNullException.ThrowIfNull(builtNodeNames);
        StopEndFinale();
        if (builtNodeNames.Count == 0 || !AnimationsEnabledProvider()) return;

        // [design v1.13.2 §2.5] Koreografi doğarken o anki seçim "bırakılmış odak" olarak hatırlanır — bkz.
        // IsFinale. Hemen aşağıdaki _endPlayer.Play ilk adımı (Hold) SENKRON tetikler (StepPlayer.Play);
        // SetEndStep bu değeri okuyup kamerayı ve odağa bağlı görselleri ANINDA tazeler.
        _focusOff = _selectedNode;

        _builtNodes = new HashSet<string>(builtNodeNames, StringComparer.Ordinal);
        var order = EndFinale.Order(builtNodeNames.Count, runCount);
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < builtNodeNames.Count; i++) map[builtNodeNames[i]] = order[i];
        _endOrder = map;
        _endStaggerMs = EndFinale.StaggerMs(builtNodeNames.Count);

        var steps = EndFinale.Steps
            .Select(s => (EndFinale.StepAtMs(s, builtNodeNames.Count), (Action)(() => SetEndStep(s))))
            .ToList();
        steps.Add((EndFinale.TotalMs(builtNodeNames.Count), () => SetEndStep(EndStep.None)));
        _endPlayer.Play(steps);
    }

    /// <summary>Bekleyen bitiş koreografisini iptal eder ve final görünüme döner.</summary>
    public void StopEndFinale()
    {
        _endPlayer.Stop();
        if (_endStep == EndStep.None) return;
        SetEndStep(EndStep.None);
    }

    private void SetEndStep(EndStep step)
    {
        _endStep = step;
        if (step == EndStep.None)
        {
            _builtNodes = new HashSet<string>(StringComparer.Ordinal);
            _endOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        }
        ApplyAllOpacities();
        // [design v1.13.2 §2.5] IsFinale burada değişmiş olabilir (finale doğarken ya da None'a dönerken) —
        // odağa bağlı BEŞ yüzeyin hepsi (ApplySelection'ın kurduğu odak kümesi/kenar/halka/etiket + kamera)
        // TEK yerden tazelenir; hedef/değer değişmediyse ilgili çağrılar zaten no-op'tur.
        ApplySelection();
        ApplyCamera(animate: true);
        if (step == EndStep.Neon) PlayNeon();
    }

    /// <summary>[§9-5] Derlenen her düğüm, kendi zincir gecikmesiyle <c>bo-neon</c>'u oynar: floresan lambanın
    /// düzensiz tutuşması. Keyframe'ler <see cref="EndFinale.NeonKeyframes"/>'tedir (kopya YASAK).</summary>
    private void PlayNeon()
    {
        foreach (var slot in _slotOrder)
        {
            if (!_builtNodes.Contains(slot.Model.Name)) continue;
            int index = _endOrder.TryGetValue(slot.Model.Name, out int i) ? i : 0;

            var flicker = new DoubleAnimationUsingKeyFrames
            {
                BeginTime = TimeSpan.FromMilliseconds(index * _endStaggerMs),
                FillBehavior = FillBehavior.HoldEnd,
            };
            foreach (var (percent, opacity) in EndFinale.NeonKeyframes)
                flicker.KeyFrames.Add(new DiscreteDoubleKeyFrame(
                    opacity, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(EndFinale.NeonMs * percent))));
            Timeline.SetDesiredFrameRate(flicker, DecorativeFrameRate);

            slot.Visual.OpacityAnimation = flicker;
            slot.Visual.OpacityTarget = double.NaN; // sıradaki ApplyNodeOpacity kapıyı GEÇSİN (değer titreşti)
            slot.Visual.Body.BeginAnimation(OpacityProperty, flicker, HandoffBehavior.SnapshotAndReplace);
        }
    }

    public event EventHandler<string?>? SelectionChanged;

    // ---------------------------------------------------------------- veri girişi

    /// <summary>
    /// [quiet] Panel GİZLİYKEN gelen besleme SAKLANIR, görsele çevrilmez.
    ///
    /// <para><b>Neden:</b> <c>list</c>/<c>focus</c> yerleşim modunda graf paneli <c>Collapsed</c>'dır ama
    /// besleme yolu (<c>MainWindow.PushGraphStatuses</c>) buna bakmıyordu — panel ekranda YOKKEN de her
    /// 200ms'lik tick'te her düğümün stili yeniden hesaplanıyordu. Kapı ÇAĞIRANDA değil BURADA: aksi halde
    /// her çağıran aynı kontrolü ve "panel geri geldiğinde kaçırılanı yakalama" mantığını kopyalardı.</para>
    ///
    /// <para>Kapı bir SUSTURUCU değil ERTELEYİCİDİR: yalnız EN SON besleme tutulur (ara durumlar zaten hiç
    /// görülmedi) ve panel görünür olduğunda TOPOLOJİ ÖNCE, statüler SONRA uygulanır.</para>
    /// </summary>
    private (IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges)? _pendingTopology;
    private IReadOnlyList<GraphNode>? _pendingStatuses;

    private bool IsPanelVisible => Visibility == Visibility.Visible;

    /// <summary>Topolojiyi (düğüm + kenar) kurar: yerleşim, görseller ve ilk açılış dalgası. Yalnız topoloji
    /// DEĞİŞTİĞİNDE çağrılır — statü güncellemeleri için <see cref="UpdateStatuses"/> kullanılır.</summary>
    public void SetGraph(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        if (!IsPanelVisible)
        {
            // Yeni topoloji bekleyen statüleri GEÇERSİZ kılar: o statüler ESKİ grafın düğümlerine aitti.
            _pendingTopology = (nodes, edges);
            _pendingStatuses = null;
            return;
        }

        ApplyGraph(nodes, edges);
    }

    /// <summary>Statüleri yerinde günceller: düğüm renkleri ve building animasyonu. Topoloji ve geometri
    /// korunur, açılış dalgası TEKRAR OYNAMAZ.</summary>
    public void UpdateStatuses(IReadOnlyList<GraphNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        if (!IsPanelVisible) { _pendingStatuses = nodes; return; }
        // Koşuya girerken graf ÖNCE söner, görünüm SONRA değişir (aşağıdaki alanın doc'u).
        if (_dimFirst is not null) { _pendingStatuses = nodes; return; }
        ApplyStatuses(nodes);
    }

    /// <summary>
    /// Koşu başlarken görünüm değişimini SÖNMENİN ARKASINA alan tek atımlık zamanlayıcı; boştayken
    /// <c>null</c>.
    ///
    /// <para><b>Neden:</b> Build'e basıldığı an iki şey birden oluyordu — graf soluklaşmaya başlıyor
    /// (280 ms) ve aynı karede derlenecek düğümlerin kesikli çerçevesi düz çerçeveye dönüyordu (renk/çerçeve
    /// değişimleri ANINDA uygulanır, ölçülmüş sapma). Değişim tam parlaklıkta görüldüğü, sönme ise sonra
    /// geldiği için ekran "önce derlenecekler belirdi, sonra hepsi söndü" diyordu. Kullanıcının istediği
    /// sıra: önce topluca sön, sonra görünüm değişsin, sonra derleme başlasın.</para>
    ///
    /// <para>Bekleme boyunca gelen statüler <see cref="_pendingStatuses"/>'a düşer (panel gizliyken kullanılan
    /// AYNI kanal) ve yalnız SONUNCUSU uygulanır — ara durumlar zaten görülmezdi. Planlama saniyeler sürdüğü
    /// için pratikte bekleme bittiğinde henüz hiçbir proje derlenmeye başlamamış olur.</para>
    /// </summary>
    private DispatcherTimer? _dimFirst;

    /// <summary>Sönme süresi kadar bekleyip biriken statüyü uygular. Koşudan çıkışta beklenmez: bitişte
    /// zaten her şey tam opağa döner ve saklanacak bir şey yoktur.</summary>
    private void HoldStatusesUntilDimmed()
    {
        _dimFirst?.Stop();
        if (!AnimationsEnabledProvider()) { _dimFirst = null; return; } // reduced-motion: sönme anlık, bekleme anlamsız

        var timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(GraphNodeOpacity.GlideMs),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();                       // tek atımlık — dispatcher onu köklüyor
            if (!ReferenceEquals(_dimFirst, timer)) return; // eskimiş kuşak
            _dimFirst = null;
            if (_pendingStatuses is { } queued && IsPanelVisible)
            {
                _pendingStatuses = null;
                ApplyStatuses(queued);
            }
        };
        _dimFirst = timer;
        timer.Start();
    }

    /// <summary>Görünürlük DEĞİŞİMİNİ yakalamanın headless'ta da çalışan TEK yolu. <c>IsVisible</c> ve
    /// <c>IsVisibleChanged</c> KULLANILAMAZ: bağlı olmayan bir görsel ağaçta <c>IsVisible</c> her zaman
    /// false'tur ve olay hiç ateşlenmez — süit ile üretim ayrışırdı. <c>Visibility</c> öğenin KENDİ
    /// özelliğidir (<c>ShellRoot.ApplyLayout</c>'un sürdüğü sinyalin ta kendisi) ve iki ortamda da aynı
    /// davranır.</summary>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property != VisibilityProperty || (Visibility)e.NewValue != Visibility.Visible) return;

        // SIRA bağlayıcıdır: statüler kurulmuş bir grafın üstüne yazılır.
        if (_pendingTopology is { } topology)
        {
            _pendingTopology = null;
            ApplyGraph(topology.Nodes, topology.Edges);
        }
        if (_pendingStatuses is { } statuses)
        {
            _pendingStatuses = null;
            ApplyStatuses(statuses);
        }
    }

    private void ApplyGraph(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges)
    {
        _edgeLayer.Children.Clear();
        _nodeLayer.Children.Clear();
        ReleaseBeadsClock();     // eski görsellerin yörüngeleri atılıyor — saat onlarla birlikte bırakılır
        ReleaseEdgeFlowClock();  // aynı gerekçe: eski seçimin akan kesikleri
        _slots.Clear();
        _slotOrder.Clear();
        _deps.Clear();
        _dependents.Clear();
        ResetPanGesture();
        CurrentCamera = GraphCamera.Default;

        // [M-4] Global Constraint: sayı biçimlemesi InvariantCulture.
        CountsText.Text = string.Format(
            CultureInfo.InvariantCulture, "{0} projects · {1} dependencies", nodes.Count, edges.Count);
        ShowEmptyState(nodes.Count == 0);
        if (nodes.Count == 0)
        {
            _layout = QuietGraphLayout.Compute([], ViewportSize);
            return;
        }

        _layout = QuietGraphLayout.Compute(nodes, ViewportSize);

        foreach (var node in nodes)
        {
            if (!_layout.Positions.TryGetValue(node.Name, out var center)) continue;
            var visual = BuildNodeVisual(node);
            var slot = new GraphNodeSlot { Model = node, Center = center, Visual = visual };
            _slots[node.Name] = slot;
            _slotOrder.Add(slot);
            _nodeLayer.Children.Add(visual.Cell);
            PlaceNode(slot);
        }

        foreach (var edge in edges)
        {
            if (!_slots.ContainsKey(edge.From) || !_slots.ContainsKey(edge.To)) continue;
            // GraphEdge yönü: From = bağımlılık, To = bağımlı proje.
            Link(_deps, edge.To, edge.From);
            Link(_dependents, edge.From, edge.To);
        }

        ApplySizes();
        ApplySelection();
        ApplyCamera(animate: false); // ilk yerleşim kamerayı KAYDIRMAZ
        PlayRevealStagger();
    }

    private void ApplyStatuses(IReadOnlyList<GraphNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (!_slots.TryGetValue(node.Name, out var slot)) continue;
            // "Değişmediyse dokunma": GraphNode bir record'dur, değer eşitliği burada güvenlidir ve statü
            // görselinin TAMAMI yalnız bu modelden türetilir. Eskiden her tick her düğümde iki
            // SetResourceReference + IconPaint.Apply (ağaç yukarı TryFindResource yürüyüşü) yapılıyordu.
            if (slot.Model == node) continue;
            // §2.3'ün hold-fade'i BUILDING'den çıkışa bağlıydı. <b>Kural artık SONUÇ statüsüne GİRİŞ.</b>
            // Atlanan proje hiç building olmaz; eski kuralla tek "parlak an"ı hiç almıyor ve koşu sonunda
            // "bu hiç işlem görmedi" hissi veriyordu. Geçişin KENDİSİ burada, model değişmeden önce okunur.
            bool settled = !GraphNodeOpacity.IsSettled(slot.Model.Status)
                && GraphNodeOpacity.IsSettled(node.Status);
            slot.Model = node;
            slot.Visual.Model = node;
            ApplyNodeStatus(slot.Visual);
            ApplyNodeOpacity(slot.Visual, settled ? GraphNodeOpacity.HoldMs : 0);
        }
    }

    private static void Link(Dictionary<string, List<string>> map, string key, string value)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(value);
    }

    // ---------------------------------------------------------------- yerleşim

    /// <summary>İçerik koordinatı → dünya (çizim) koordinatı: yerleşim kenar payının İÇİNDE hesaplanır.</summary>
    private static Point ToWorld(Point content) =>
        new(content.X + QuietGraphLayout.ContentInset, content.Y + QuietGraphLayout.ContentInset);

    /// <summary>
    /// [quiet] Yerleşimi panel ölçüsünden YENİDEN hesaplar ve görselleri YERİNDE günceller (yeniden KURMAZ).
    /// §2.3: "graf HER panel boyutunda tam sığar" — yerleşim artık <see cref="SetGraph"/>'ın değil PANEL
    /// ÖLÇÜSÜNÜN fonksiyonudur.
    /// </summary>
    private void Relayout()
    {
        if (_slotOrder.Count == 0) return;
        LayoutComputeCount++;

        _layout = QuietGraphLayout.Compute([.. _slotOrder.Select(slot => slot.Model)], ViewportSize);
        foreach (var slot in _slotOrder)
        {
            if (!_layout.Positions.TryGetValue(slot.Model.Name, out var center)) continue;
            slot.Center = center;
            PlaceNode(slot);
        }
        ApplySizes();
    }

    /// <summary>Tek düğümün KONUMUNU canlı yerleşimden uygular. Kurulum ile yeniden boyutlanma AYNI yolu
    /// kullanır (kopya YASAK).</summary>
    private void PlaceNode(GraphNodeSlot slot)
    {
        // Hücre düğümden CellOverhang kadar büyüktür ama düğümün MERKEZİNE oturur — taşan görseller
        // (halka, yörünge) hücrenin içinde eş-merkezli kalır.
        double cell = _layout.NodeSize + CellOverhang * 2;
        var world = ToWorld(slot.Center);
        Canvas.SetLeft(slot.Visual.Cell, world.X - cell / 2);
        Canvas.SetTop(slot.Visual.Cell, world.Y - cell / 2);
    }

    /// <summary>Düğüm ÖLÇÜSÜ graf genelinde tektir (pitch'ten türer) — bu yüzden tek turda hepsine yazılır ve
    /// ikon ölçeği tek paylaşımlı transform üzerinden güncellenir.</summary>
    private void ApplySizes()
    {
        double size = _layout.NodeSize;
        double ring = size + SelectionRingInset * 2;
        double cell = size + CellOverhang * 2;
        _iconScale.ScaleX = _iconScale.ScaleY = size * IconFactor / IconViewBox;

        // Düğüm boyutu değiştiyse yörüngenin ÇEVRESİ de değişir ⇒ desen ve paylaşımlı saat yeniden kurulur;
        // aksi halde noktalar yeni çevreye tam bölünmez ve ek yerinde bindirirdi.
        var beads = GraphBeads.For(size);
        if (beads != _beadsGeometry)
        {
            _beadsGeometry = beads;
            _beadsDash = GraphBeads.DashArrayFor(beads);
            bool wasSpinning = _beadsClock is not null;
            ReleaseBeadsClock();
            foreach (var slot in _slotOrder)
                if (slot.Visual.Beads is { } orbit) ApplyBeadsGeometry(orbit);
            if (wasSpinning) EnsureBeadsClock();
        }

        // Dünya tuvali PANELİN kendisidir: ölçek 1'de graf tam oturur, öteleme 0'dır.
        World.Width = Math.Max(0, ViewportSize.Width);
        World.Height = Math.Max(0, ViewportSize.Height);

        foreach (var slot in _slotOrder)
        {
            var visual = slot.Visual;
            visual.Cell.Width = visual.Cell.Height = cell;
            visual.Body.Width = visual.Body.Height = size;
            visual.Base.Width = visual.Base.Height = size;
            visual.Square.Width = visual.Square.Height = size;
            visual.SelectionRing.Width = visual.SelectionRing.Height = ring;
        }
    }

    // ---------------------------------------------------------------- düğüm görselleri

    private GraphNodeVisual BuildNodeVisual(GraphNode node)
    {
        var selectionRing = new Rectangle
        {
            RadiusX = NodeCornerRadius + SelectionRingInset,
            RadiusY = NodeCornerRadius + SelectionRingInset,
            StrokeThickness = SelectedNodeBorderThickness,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        selectionRing.SetResourceReference(Shape.StrokeProperty, "Brush.FocusRing");

        var opaqueBase = new Rectangle
        {
            RadiusX = NodeCornerRadius,
            RadiusY = NodeCornerRadius,
            IsHitTestVisible = false,
        };
        // Panel zemini rengi: düğüm neyin üstünde duruyorsa onun rengindedir ⇒ görünüm DEĞİŞMEZ, yalnız
        // altından geçen seçim çizgisi %12 alfalı statü zemininin içinden görünmez olur.
        opaqueBase.SetResourceReference(Shape.FillProperty, "Brush.SurfaceBase");

        var square = new Rectangle
        {
            RadiusX = NodeCornerRadius,
            RadiusY = NodeCornerRadius,
            StrokeThickness = NodeBorderThickness,
        };

        // [T60] Geometri + boya TEK yerden: Icons.xaml'in kardeş Icon.X.StrokeThickness anahtarı.
        // 24 → node×0.52 indirgemesi PAYLAŞILAN bir ScaleTransform'ladır (Viewbox + iç ContainerVisual yerine):
        // merkezden ölçeklendiği için sonuç birebir aynıdır ve düğüm başına iki nesne kazandırır.
        var icon = new Path();
        var iconBox = new Canvas
        {
            Width = IconViewBox,
            Height = IconViewBox,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _iconScale,
            Children = { icon },
        };

        var body = new GraphNodeBody
        {
            Background = Brushes.Transparent, // tıklama alanı
            Cursor = Cursors.Hand,
            Children = { opaqueBase, square, iconBox },
            // §2.3 "Hover": scale(1.7) — merkezden büyür, komşularını itmez (RenderTransform layout'a girmez).
            // Aynı transform halka ve beads yörüngesiyle PAYLAŞILIR: prototipte ölçek kareye uygulanır ve
            // halka (CSS outline) ile yörünge onunla birlikte büyür (BuildApp.jsx:442, :457); ayrı üç
            // transform zamanla ayrışabilirdi.
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
        };

        // Halka gövdenin İÇİNDE değil YANINDA yaşar: gövde düğüm kadardır ve WPF taşan bir çocuğu arrange
        // slot'una KIRPAR (bkz. CellOverhang). Gövdeyi halka kadar büyütmek ise tıklama alanını büyütür ve
        // dar pitch'te komşunun üstüne bindirirdi.
        selectionRing.RenderTransformOrigin = new Point(0.5, 0.5);
        selectionRing.RenderTransform = body.RenderTransform;

        var cell = new Grid { Children = { selectionRing, body } };

        string name = node.Name;
        // [A13/T5 fix-1] Düğümün etkinleştirilmesi TEK yerde: fare tıklaması da UIA Invoke'u da (ekran okuyucu)
        // AYNI yerel fonksiyonu çağırır — ikinci bir seçim mantığı YOK (kopya YASAK).
        void Toggle() => SelectedNode = string.Equals(SelectedNode, name, StringComparison.Ordinal) ? null : name;
        body.Activate = Toggle;
        body.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true; // zemine ulaşmasın (aksi halde bırakışta seçim kalkardı)
            Toggle();
        };
        // §2.3: tooltip GECİKMESİZ — native ToolTipService değil, ekran koordinatlı overlay kullanılır.
        body.MouseEnter += (_, _) => SetHover(name);
        body.MouseLeave += (_, _) => { if (string.Equals(_hoveredNode, name, StringComparison.Ordinal)) SetHover(null); };

        var visual = new GraphNodeVisual
        {
            Model = node,
            Cell = cell,
            Body = body,
            Base = opaqueBase,
            Square = square,
            SelectionRing = selectionRing,
            Icon = icon,
        };

        ApplyNodeStatus(visual);
        return visual;
    }

    /// <summary>DS <c>DependencyGraphNode</c> statü tablosunun birebir karşılığı: çerçeve + zemin + ikon rengi
    /// (+ discovered'ın kesikli çerçevesi) ve building animasyonu.</summary>
    private void ApplyNodeStatus(GraphNodeVisual visual)
    {
        NodeStatusApplyCount++;

        // [A13/T5] Ekran-okuyucu adı: kare/ikon görselleri ekran okuyucuya HİÇBİR ŞEY söylemez. Ad düğüm
        // BAŞINA anlamlıdır (tam proje adı + statü) ve statü görselleriyle AYNI yerde sürülür — statü
        // değişince ad da tazelenir, bayat kalmaz.
        AutomationProperties.SetName(
            visual.Body, AccessibilityNames.GraphNode(visual.Model.Name, StatusGlyph.LabelFor(visual.Model.Status)));

        // [design v1.11.0 §2.3 "Renk kuralı"] TEK statü kanalı: node border'ı, zemini ve içindeki küp AYNI
        // görsel durumdan boyanır. Eşleme tablosu VisualStatuses'tedir — liste satırı da AYNI tablodan okur;
        // graf ikinci bir eşleme YAZMAZ (kopya YASAK).
        //
        // [DEĞİŞEN KURAL] Burada eskiden İKİ eşleme vardı: kenar/zemin statüden, ÇEKİRDEK ise ayrı bir
        // plan/cycle kanalından (döngü üyesi → turuncu; aksi halde amber "derlenecek" / gri "güncel").
        // v1.11.0 o kanalları kaldırdı — renk yalnız son işlemin hikâyesini anlatır. Kesikli çerçeve de artık
        // YALNIZ başlangıç modundadır (fresh); `discovered` DÜZ gridir ve "bir işlem başladı ama bu proje
        // kapsamda değil" der.
        var state = visual.Model.Visual;
        string border = VisualStatuses.NodeBorderBrushKey(state);
        string background = VisualStatuses.NodeBackgroundBrushKey(state);
        string iconColor = VisualStatuses.NodeCoreBrushKey(state);
        bool dashed = VisualStatuses.IsStartMode(state);

        // [§2.3 · §1.3] Renk geçişi YALNIZ işaretleme dalgası oynarken açıktır — kapsam amber'a 200ms'de
        // AKAR, ÇAKMAZ (BuildApp.jsx:529-533).
        //
        // [ÖLÇÜLMÜŞ SAPMA — dalga DIŞINDA geçerlidir] Renkler dalga dışında ANINDA uygulanır ve bu bilinçlidir.
        // WPF'te bir fırça DP'si interpolate EDİLEMEZ: geçiş, düğüm başına yerel bir SolidColorBrush kurup
        // onun Color'ını animasyonlamayı gerektirir. Uygulandı ve ölçüldü — 177 projenin statüsünün tek
        // tick'te değiştiği durumda (koşu başlangıcı: hepsi Discovered → Queued) üç yüzey × 177 düğüm = 531
        // fırça + 531 ColorAnimation, tick'i 11 ms'den 51 ms'ye çıkarıyor ve UI olay bütçesini (50 ms,
        // UiResponsivenessBudgetTests.EventBudgetMs) AŞIYOR.
        //
        // Dalga tam TERSİ durumdur ve ölçüm onu kapsamaz: tempo 110ms/node (36 projede ~31ms), yani tick
        // başına bir-iki düğüm değişir. Yüzey dalga sırasında yerel fırçaya devreder ve dalga bitince token
        // referansına GERİ döner (TransitionTokenBrush) — referans kalıcı kaybedilmez.
        bool lighting = _markStep != MarkStep.None && AnimationsEnabledProvider();
        MotionTokens.TransitionTokenBrush(this, visual.Square, Shape.StrokeProperty, border,
            lighting, MarkingChoreography.LightMs);
        MotionTokens.TransitionTokenBrush(this, visual.Square, Shape.FillProperty, background,
            lighting, MarkingChoreography.LightMs);
        IconPaint.Apply(visual.Icon, this, PackageIconKey, iconColor, lighting, MarkingChoreography.LightMs);

        // WPF Border dashed desteklemez → kesikli çerçeve Rectangle.StrokeDashArray ile. Dash birimi
        // StrokeThickness çarpanıdır: 1.5px'lik çerçevede {2,2} = 3px dolu / 3px boş.
        visual.Square.StrokeDashArray = dashed ? DiscoveredDash : SolidDash;

        ApplyBeads(visual);
    }

    // ---------------------------------------------------------------- beads (§2.3 building animasyonu)

    /// <summary>
    /// [quiet] §2.3 "Building animasyonu — beads": derlenen düğümün 2.8px dışında dolanan sık amber
    /// noktalar. Yörünge DOM'da sürekli durur, yalnız OPAKLIĞI değişir — girişte 420ms, çıkışta 640ms
    /// ease-out; noktalar DÖNERKEN söner, donup kaybolmaz.
    ///
    /// <para>Zaten doğru durumdaki bir yörünge YENİDEN kurulmaz (<see cref="GraphNodeVisual.BeadsVisible"/>):
    /// koşarken statü itişi saniyede birkaç kez gelir ve her çağrıda animasyonu baştan başlatmak yörüngeyi
    /// "takılı" gösterirdi.</para>
    /// </summary>
    private void ApplyBeads(GraphNodeVisual visual)
    {
        bool live = visual.Model.Status == GraphStatus.Building && AnimationsEnabledProvider();
        if (live == visual.BeadsVisible) return;
        visual.BeadsVisible = live;

        if (live)
        {
            _beadsSpindown?.Stop(); // yeni bir cephe doğdu — saat bırakılmayacak
            EnsureBeads(visual);
            EnsureBeadsClock();
            FadeBeads(visual, 1.0, GraphBeads.FadeInMs);
            return;
        }

        if (visual.Beads is null) return;
        FadeBeads(visual, 0.0, GraphBeads.FadeOutMs);
        ArmBeadsSpindown();
    }

    private void FadeBeads(GraphNodeVisual visual, double target, double durationMs)
    {
        visual.Beads!.BeginAnimation(OpacityProperty,
            MotionTokens.SplineTo(target, TimeSpan.FromMilliseconds(durationMs), EaseOut),
            HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Yörüngeyi TALEP ÜZERİNE kurar (bir kez) — düğümlerin çoğu bir koşuda hiç derlenmez.</summary>
    private void EnsureBeads(GraphNodeVisual visual)
    {
        if (visual.Beads is not null) return;

        var orbit = new Rectangle
        {
            Fill = null,
            StrokeThickness = GraphBeads.StrokeThickness,
            StrokeDashCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Opacity = 0.0, // giriş animasyonu 0'dan başlar
            // Hover/seçim büyütmesini GÖVDEYLE PAYLAŞIR — aksi halde kare büyürken yörünge yerinde kalır ve
            // kare onun içinden taşar (gözle bulunan kusur).
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = visual.Body.RenderTransform,
        };
        orbit.SetResourceReference(Shape.StrokeProperty, "Brush.AmberText");
        ApplyBeadsGeometry(orbit);
        // Kareyi ÖRTMEZ (2.8px dışında) ama gövdenin DIŞINDA durur: gövde tıklama alanıdır ve yörünge
        // taşmasının hit-test'e karışmaması gerekir.
        visual.Cell.Children.Add(orbit);
        visual.Beads = orbit;
        if (_beadsClock is { } clock) orbit.ApplyAnimationClock(Shape.StrokeDashOffsetProperty, clock);
    }

    private void ApplyBeadsGeometry(Rectangle orbit)
    {
        orbit.Width = orbit.Height = _beadsGeometry.Side;
        orbit.RadiusX = orbit.RadiusY = _beadsGeometry.CornerRadius;
        orbit.StrokeDashArray = _beadsDash;
    }

    /// <summary>Paylaşımlı saati kurar (yoksa) ve mevcut TÜM yörüngelere bağlar.</summary>
    private void EnsureBeadsClock()
    {
        if (_beadsClock is not null) return;

        var spin = new DoubleAnimation
        {
            From = 0,
            To = -_beadsGeometry.Perimeter,
            Duration = TimeSpan.FromMilliseconds(GraphBeads.CycleMs),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Timeline.SetDesiredFrameRate(spin, MotionTokens.DecorativeFrameRate); // dekoratif sonsuz animasyon (feasibility §3.4)
        _beadsClock = spin.CreateClock();

        foreach (var slot in _slotOrder)
            slot.Visual.Beads?.ApplyAnimationClock(Shape.StrokeDashOffsetProperty, _beadsClock);
    }

    /// <summary>§2.3: saat bitişten <see cref="GraphBeads.SpinAfterStopMs"/> sonra bırakılır — çıkış
    /// animasyonu (640ms) o pencerenin içinde biter, yani noktalar DÖNERKEN söner, donup kaybolmaz.</summary>
    private void ArmBeadsSpindown()
    {
        _beadsSpindown ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(GraphBeads.SpinAfterStopMs),
        };
        if (_beadsSpindown.Tag is null)
        {
            _beadsSpindown.Tag = this; // abonelik BİR kez kurulur
            _beadsSpindown.Tick += (_, _) => HandleBeadsSpindownTick();
        }
        _beadsSpindown.Stop();
        _beadsSpindown.Start();
    }

    /// <summary>Spin-down penceresi doldu. Bu arada yeni bir cephe doğduysa saat KORUNUR — tetik yalnız
    /// gerçekten boşalmış bir grafta saati bırakır.</summary>
    internal void HandleBeadsSpindownTick()
    {
        _beadsSpindown?.Stop();
        foreach (var slot in _slotOrder)
            if (slot.Visual.BeadsVisible) return;
        ReleaseBeadsClock();
    }

    private void ReleaseBeadsClock()
    {
        _beadsSpindown?.Stop();
        if (_beadsClock is null) return;
        foreach (var slot in _slotOrder)
            slot.Visual.Beads?.ApplyAnimationClock(Shape.StrokeDashOffsetProperty, null);
        _beadsClock = null;
    }

    // ---------------------------------------------------------------- hover + ekran koordinatlı tooltip

    /// <summary>
    /// [quiet] §2.3 "Hover": node scale(1.7) (120ms ease-out), border 2px, opacity 1 (soluk moddayken bile),
    /// z-index öne; tooltip GECİKMESİZ ve TAM proje adıyla.
    /// </summary>
    private void SetHover(string? nodeName)
    {
        if (string.Equals(_hoveredNode, nodeName, StringComparison.Ordinal)) return;

        string? previous = _hoveredNode;
        _hoveredNode = nodeName;
        if (previous is not null) ApplyHover(previous);
        if (nodeName is not null) ApplyHover(nodeName);
        UpdateTooltip();
    }

    /// <summary>
    /// Bir düğümün VURGU durumunu uygular: ölçek, çerçeve kalınlığı, z-order ve opaklık.
    ///
    /// <para>[quiet · görsel geçiş] Vurgu iki kaynaktan gelir ve ikisi de AYNI görünümü verir: hover VE
    /// seçim. Seçili düğümün hover ölçeğinde KALMASI ana prototipte yok (orada <c>scale</c> yalnız hover'a
    /// bağlı, BuildApp.jsx:442) — Graph Lab denemesinde vardı ve ana prototipe taşınmamış; kullanıcı istenen
    /// davranışın o olduğunu doğruladı. Öne alma ise bir düzeltmedir: seçim halkası düğümden
    /// <see cref="SelectionRingInset"/> kadar taşar ve dar pitch'te komşular onun üstünü örtüyordu.</para>
    ///
    /// <para>[design v1.13.2 §2.5 — review fix] İkinci disjunct <see cref="EffectiveSelection"/> okur,
    /// <see cref="_selectedNode"/> DEĞİL: "node halkası/outline" prototipte İKİ satırdır (BuildApp.jsx:589
    /// kalın çerçeve, :592 CSS outline) ve ikisi AYNI <c>!finale</c> kapısını paylaşır. Bu üç etki (kalın
    /// çerçeve, z-order öne alma, WPF'e özgü 1.5× büyütme) TEK <c>hovered</c> bayrağından geldiği için ham
    /// <c>_selectedNode</c> okumak önceden seçili düğümü finale boyunca VE final hâlde "spotlight"ta
    /// bırakırdı — kamerası, kenarları, halkası ve etiketi bırakılmışken.</para>
    /// </summary>
    private void ApplyHover(string nodeName)
    {
        if (!_slots.TryGetValue(nodeName, out var slot)) return;
        var visual = slot.Visual;
        bool hovered = string.Equals(_hoveredNode, nodeName, StringComparison.Ordinal)
            || string.Equals(nodeName, EffectiveSelection, StringComparison.Ordinal);

        double target = hovered ? HoverScale : 1.0;
        var scale = (ScaleTransform)visual.Body.RenderTransform;
        if (AnimationsEnabledProvider())
        {
            var glide = MotionTokens.SplineTo(target, TimeSpan.FromMilliseconds(HoverScaleMs), EaseOut);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, glide, HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, glide, HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = scale.ScaleY = target;
        }

        // Büyüyen düğüm — ve halkası — komşuların ÜSTÜNDE kalmalı.
        Panel.SetZIndex(visual.Cell, hovered ? 1 : 0);
        visual.Square.StrokeThickness = hovered ? HoverBorderThickness : NodeBorderThickness;
        ApplyNodeOpacity(visual, holdMs: 0);
    }

    /// <summary>Tooltip'i (tek, yeniden kullanılan öğe) günceller ve EKRAN koordinatına yerleştirir.</summary>
    private void UpdateTooltip()
    {
        if (_hoveredNode is not { } name || !_slots.TryGetValue(name, out var slot))
        {
            TooltipBox.Visibility = Visibility.Collapsed;
            return;
        }

        TooltipText.Text = name; // §2.3: TAM proje adı, kısaltmasız
        TooltipBox.Visibility = Visibility.Visible;
        // Hover edilen düğüm vurgu ölçeğindedir; halkası ancak AYNI ZAMANDA seçiliyse vardır (finale
        // sırasında/sonrasında hiç yoktur — EffectiveSelection null döner).
        bool ringed = string.Equals(name, EffectiveSelection, StringComparison.Ordinal);
        PlaceOverlayBox(TooltipBox, box => GraphOverlay.TooltipTopLeft(
            slot.Center, LiveCamera, PaintedHalfExtent(ringed, LiveCamera.Scale), ViewportSize, box));
    }

    /// <summary>
    /// Overlay kutusunu ÖLÇÜP yerleştirir. Ölçüm şart: kutu ankraja ORTALANIR ve genişliği ancak ölçtükten
    /// sonra bilinir (prototipin karakter-genişliği tahmini yerine).
    ///
    /// <para><b><see cref="UIElement.InvalidateMeasure"/> çağrısı zorunludur</b> ve gözle bulunan bir kusurun
    /// düzeltmesidir: metin değiştiğinde WPF yalnız <c>TextBlock</c>'u kirli işaretler, ata zinciri ise
    /// LAYOUT TURUNDA yürütülür. Tur dışından çağrılan <c>Measure</c> Border'ı temiz görüp ERKEN ÇIKIYOR ve
    /// <c>DesiredSize</c> BİR ÖNCEKİ adın genişliğinde kalıyordu — ölçüldü: hangi proje seçilirse seçilsin
    /// kutu genişliği ilk ölçümdeki 24.6px'te takılı kalıyor, dolayısıyla uzun adlar düğümün epey soluna
    /// kayıyordu ("proje adına göre her zaman ortalı değil").</para>
    /// </summary>
    private static void PlaceOverlayBox(FrameworkElement box, Func<Size, Point> place)
    {
        box.InvalidateMeasure();
        box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var topLeft = place(box.DesiredSize);
        Canvas.SetLeft(box, topLeft.X);
        Canvas.SetTop(box, topLeft.Y);
    }

    /// <summary>
    /// Düğümün EKRANDA kapladığı YARIM yükseklik: kare yarısı + dışarı taşan halka/yörünge, vurgu ölçeği ve
    /// kamera dahil. Overlay kutuları bunun DIŞINA konumlanır.
    ///
    /// <para>Prototipin 0.9/0.95 katsayıları (JSX:471, :481) kendi düğümü için kalibreliydi: orada seçili
    /// düğüm BÜYÜMEZ ve halkası CSS outline'dır. Bizim seçili düğümümüz hover ölçeğinde DURUR ve halkası
    /// var — o katsayılarla ad etiketi halkanın içine düşüyordu ("borderla neredeyse bitişik").</para>
    ///
    /// <para><paramref name="withRing"/> düğümün HALKASI olup olmadığıdır: seçili düğümün etiketi halkanın,
    /// yalnız hover edilen bir düğümün tooltip'i ise KARENİN dışında durur. İkisi arasındaki mesafe aynı
    /// (<see cref="GraphOverlay.OverlayGapPx"/>) ama ölçtükleri kenar farklıdır — kullanıcı kararı budur.</para>
    /// </summary>
    private double PaintedHalfExtent(bool withRing, double scale) =>
        (_layout.NodeSize / 2 + (withRing ? SelectionRingInset : 0)) * HoverScale * scale;

    // ---------------------------------------------------------------- seçim (halka + sönme)

    /// <summary>Odak kümesi = seçili düğüm + DOĞRUDAN bağımlılıkları + DOĞRUDAN bağımlıları (§2.3).
    /// [design v1.13.2 §2.5] Finale sırasında/sonrasında (<see cref="IsFinale"/>) <see cref="EffectiveSelection"/>
    /// null döner — odak kümesi BOŞ kurulur, yani "seçim yokmuş gibi" davranır.</summary>
    private void ApplySelection()
    {
        _focusSet = new HashSet<string>(StringComparer.Ordinal);
        if (EffectiveSelection is { } selected)
        {
            _focusSet.Add(selected);
            foreach (string name in DirectNeighboursOf(selected))
                _focusSet.Add(name);
        }
        RebuildSelectionEdges();
        UpdateSelectionLabel();

        foreach (var slot in _slotOrder)
        {
            string name = slot.Model.Name;
            bool isSelected = string.Equals(name, EffectiveSelection, StringComparison.Ordinal);
            slot.Visual.SelectionRing.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
            // DS DependencyGraphNode: `border: ${selected ? 2 : 1.5}px …` — seçim kareyi de kalınlaştırır.
            ApplyHover(name); // ölçek + çerçeve + z-order + opaklık TEK yerden (kopya YASAK)
        }
    }

    // ---------------------------------------------------------------- seçim kenarları (§2.3)

    /// <summary>Seçili düğümün DOĞRUDAN komşuları (bağımlılıklar + bağımlılar) — odak kümesinin kaynağı.</summary>
    private IEnumerable<string> DirectNeighboursOf(string node)
    {
        if (_deps.TryGetValue(node, out var deps))
            foreach (string name in deps) yield return name;
        if (_dependents.TryGetValue(node, out var dependents))
            foreach (string name in dependents) yield return name;
    }

    /// <summary>
    /// [quiet] §2.3: "Bağımlılık çizgileri YALNIZ seçimde: deps→node ve node→dependents." Seçim değişince
    /// eski çizgiler SÖKÜLÜR — kalıcı bir ağ yoktur, dolayısıyla koşarken stillenecek kenar da yoktur.
    /// [design v1.13.2 §2.5] Finale sırasında/sonrasında da (<see cref="EffectiveSelection"/> null) hiç
    /// kurulmaz.
    /// </summary>
    private void RebuildSelectionEdges()
    {
        _edgeLayer.Children.Clear();
        _selectionEdges.Clear();
        ReleaseEdgeFlowClock();

        if (EffectiveSelection is not { } selected || !_slots.TryGetValue(selected, out var target)) return;

        var centre = ToWorld(target.Center);
        if (_deps.TryGetValue(selected, out var deps))
            foreach (string name in deps) AddSelectionEdge(name, centre, dependencyAbove: true);
        if (_dependents.TryGetValue(selected, out var dependents))
            foreach (string name in dependents) AddSelectionEdge(name, centre, dependencyAbove: false);

        if (_selectionEdges.Count > 0 && AnimationsEnabledProvider()) EnsureEdgeFlowClock();
    }

    private void AddSelectionEdge(string otherName, Point selectedCentre, bool dependencyAbove)
    {
        if (!_slots.TryGetValue(otherName, out var other)) return;
        var otherCentre = ToWorld(other.Center);

        var path = new Path
        {
            // Yön ÖNEMLİ: eğri yukarıdan aşağı akar (bağımlılık → bağımlı), böylece kontrol noktaları
            // iki ucun orta yüksekliğinde kalır ve çizgi grafın okuma yönünü izler.
            Data = dependencyAbove
                ? SelectionEdgeStyle.Curve(otherCentre, selectedCentre)
                : SelectionEdgeStyle.Curve(selectedCentre, otherCentre),
            StrokeThickness = SelectionEdgeStyle.Thickness,
            Opacity = SelectionEdgeStyle.Opacity,
            StrokeDashArray = SelectionEdgeStyle.DashArray,
            IsHitTestVisible = false,
        };
        path.SetResourceReference(Shape.StrokeProperty, SelectionEdgeStyle.BrushKey);
        _edgeLayer.Children.Add(path);
        _selectionEdges.Add(path);
    }

    /// <summary>Akan kesiklerin PAYLAŞILAN saati — beads ile aynı gerekçe (çizgi başına ayrı sonsuz
    /// animasyon kurulmaz). Desen tek olduğu için tek saat hepsini faz-kilitli sürer.</summary>
    private void EnsureEdgeFlowClock()
    {
        var flow = new DoubleAnimation
        {
            From = 0,
            To = SelectionEdgeStyle.DashOffsetTarget,
            Duration = TimeSpan.FromMilliseconds(SelectionEdgeStyle.FlowDurationMs),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Timeline.SetDesiredFrameRate(flow, MotionTokens.DecorativeFrameRate);
        _edgeFlowClock = flow.CreateClock();
        foreach (var path in _selectionEdges)
            path.ApplyAnimationClock(Shape.StrokeDashOffsetProperty, _edgeFlowClock);
    }

    private void ReleaseEdgeFlowClock()
    {
        if (_edgeFlowClock is null) return;
        foreach (var path in _selectionEdges)
            path.ApplyAnimationClock(Shape.StrokeDashOffsetProperty, null);
        _edgeFlowClock = null;
    }

    /// <summary>§2.3: "Seçili node'un altında 6px boşlukla ad etiketi … ekran koordinatında." Tooltip'le
    /// AYNI overlay katmanında, TEK bir öğe olarak yaşar.</summary>
    private void UpdateSelectionLabel()
    {
        if (EffectiveSelection is not { } selected || !_slots.TryGetValue(selected, out var slot))
        {
            SelectionLabelBox.Visibility = Visibility.Collapsed;
            return;
        }

        SelectionLabelText.Text = selected;
        SelectionLabelBox.Visibility = Visibility.Visible;
        // Seçili düğüm vurgu ölçeğinde durur VE halkası kareden taşar.
        PlaceOverlayBox(SelectionLabelBox, box => GraphOverlay.NameLabelTopLeft(
            slot.Center, LiveCamera, PaintedHalfExtent(withRing: true, LiveCamera.Scale), ViewportSize, box));
    }

    // ---------------------------------------------------------------- koşu yaşam döngüsü (opaklık)

    private void ApplyAllOpacities(double? glideMs = null)
    {
        foreach (var slot in _slotOrder)
            ApplyNodeOpacity(slot.Visual, holdMs: 0, glideMs);
    }

    /// <summary>
    /// [quiet] §2.3'ün opaklık sistemi. Değer kararı SAF (<see cref="GraphNodeOpacity.Resolve"/>); burada
    /// yalnız ZAMANLAMA yaşar.
    ///
    /// <para><b>Hold-fade YALNIZ sonuç statüsüne giriş anında doğar</b> (<paramref name="holdMs"/> &gt; 0):
    /// CSS'in gecikmeli transition'ı da yalnız değer 1'den 0.2'ye DEĞİŞTİĞİNDE koşar. Sonraki tick'ler değeri
    /// zaten 0.2 bulur ve <see cref="GraphNodeVisual.OpacityTarget"/> kapısı hiçbir animasyon başlatmaz —
    /// aksi halde saniyede birkaç kez yeniden doğan bir animasyon sönmeyi hiç tamamlatmazdı.</para>
    ///
    /// <para><b>Bekleme artık AÇIKÇA yazılır</b> (üç keyframe: parlak → parlak → sonuç). Eski kodlama
    /// beklemeyi bir <c>BeginTime</c>'a yıkıyor ve düğümün o sırada ZATEN parlak olduğuna güveniyordu — bu
    /// yalnız building'den çıkan düğüm için doğruydu. Atlanan düğüm 0.13'ten gelir: parlaklığa onu bir şeyin
    /// ÇIKARMASI gerekir, yoksa hiç parlamadan söner.</para>
    /// </summary>
    /// <param name="holdMs">Sonuç renginde PARLAK bekleme; 0 = bekleme yok (düz 280ms geçiş).</param>
    /// <param name="glideMs">Beklemesiz geçişin süresi; <c>null</c> = <see cref="GraphNodeOpacity.GlideMs"/>.
    /// Yalnız filtre bunu ezer (<see cref="GraphNodeOpacity.FilterFadeMs"/>) — gerekçe orada.</param>
    private void ApplyNodeOpacity(GraphNodeVisual visual, double holdMs, double? glideMs = null)
    {
        // [design v1.11.0 §9-4/§9-5] Koreografiler opaklık kararını EZER ve kendi süreleriyle koşar. Sıra:
        // açılış (marking) > bitiş (neon) > normal koşu/seçim/filtre sistemi. Neonun KENDİ animasyonu
        // (PlayNeon) bu yoldan geçmez — o adım burada yalnız hedefi 1'de tutar.
        if (_markStep != MarkStep.None)
        {
            bool marked = _markedNodes.Contains(visual.Model.Name);
            ApplyOpacityTarget(visual,
                MarkingChoreography.Opacity(_markStep, marked, MarkingChoreography.NodeEnvOpacity),
                MarkingChoreography.GlideMs(_markStep, marked), EaseInOut);
            return;
        }
        if (_endStep != EndStep.None)
        {
            bool built = _builtNodes.Contains(visual.Model.Name);
            // Neon adımında düğümün opaklığını PlayNeon sürüyor — burada ikinci bir animasyon kurma.
            if (built && _endStep == EndStep.Neon) return;
            ApplyOpacityTarget(visual, EndFinale.Opacity(_endStep, built), EndFinale.GlideMs(built), EaseInOut);
            return;
        }

        double target = GraphNodeOpacity.Resolve(
            visual.Model.Status,
            _runPhase,
            EffectiveSelection is not null,
            _focusSet.Contains(visual.Model.Name),
            string.Equals(_hoveredNode, visual.Model.Name, StringComparison.Ordinal),
            _filterMatches is not null,
            _filterMatches?.Contains(visual.Model.Name) ?? true);

        if (target.Equals(visual.OpacityTarget)) return;
        visual.OpacityTarget = target;

        if (!AnimationsEnabledProvider())
        {
            SnapOpacity(visual, target);
            return;
        }

        // Bekleme keyframe'lerle taşınır, bir timer DEĞİL: CSS'teki gecikmeli transition'ın karşılığı.
        DoubleAnimationUsingKeyFrames animation;
        if (holdMs > 0)
        {
            animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(
                GraphNodeOpacity.Full, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(
                GraphNodeOpacity.Full, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(holdMs))));
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(
                target,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(holdMs + GraphNodeOpacity.FadeMs)),
                EaseStandard));
        }
        else
        {
            animation = MotionTokens.SplineTo(
                target, TimeSpan.FromMilliseconds(glideMs ?? GraphNodeOpacity.GlideMs), EaseStandard);
        }

        visual.OpacityAnimation = animation;
        visual.Body.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>[design v1.11.0 §9-4/§9-5] Koreografilerin ortak opaklık uygulayıcısı: hedef + süre + eğri
    /// dışarıdan gelir (koreografi kendi zamanlamasını taşır), "değişmediyse dokunma" kapısı ve
    /// reduced-motion snap'i normal yolla AYNI kalır.</summary>
    private void ApplyOpacityTarget(GraphNodeVisual visual, double target, double glideMs, KeySpline ease)
    {
        if (target.Equals(visual.OpacityTarget)) return;
        visual.OpacityTarget = target;

        if (!AnimationsEnabledProvider()) { SnapOpacity(visual, target); return; }

        var animation = MotionTokens.SplineTo(target, TimeSpan.FromMilliseconds(glideMs), ease);
        visual.OpacityAnimation = animation;
        visual.Body.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void SnapOpacity(GraphNodeVisual visual, double target)
    {
        visual.OpacityAnimation = null;
        visual.Body.BeginAnimation(OpacityProperty, null);
        visual.Body.Opacity = target;
    }

    // ---------------------------------------------------------------- ilk açılış dalgası

    /// <summary>Bir düğümün beliriş gecikmesi: BUILD-ORDER indeksinden (besleme sırası), katmandan DEĞİL.
    /// Dalga bu yüzden grafın okuma yönünü (üstten alta, soldan sağa) izler — bantlar da build-order'a göre
    /// dizildiği için ikisi aynı sırayı verir.</summary>
    internal static double RevealDelayMs(int buildOrderIndex) =>
        Math.Min(buildOrderIndex * RevealStepMs, RevealDelayCapMs);

    private void PlayRevealStagger()
    {
        // [E3/T41/DD9 · W2 fold] Reveal bir HERO'dur. Önceki hero + bekleyen release bırakılır, yeni kuşak
        // damgalanır ve hero alınmaya çalışılır. Başka bir hero sürerken dekoratif dalga ATLANIR.
        var (animate, gen) = _reveal.Begin(AnimationsEnabledProvider(), ActiveHeroCoordinator, RevealHeroKey);

        double maxDelay = RevealDelayMs(Math.Max(0, _slotOrder.Count - 1));

        for (int index = 0; index < _slotOrder.Count; index++)
        {
            var visual = _slotOrder[index].Visual;
            visual.Cell.BeginAnimation(OpacityProperty, null);
            if (!animate)
            {
                visual.RevealDelayMs = null;
                visual.Cell.Opacity = 1.0;
                visual.Cell.RenderTransform = Transform.Identity;
                continue;
            }

            double delay = RevealDelayMs(index);
            visual.RevealDelayMs = delay;
            ApplyRevealTo(visual, delay);
        }

        // [E3/T41 — release fix] Hero, reveal PENCERESİ boyunca tutulur ve en geç biten düğümün reveal'i
        // tamamlanınca generation-guarded bir DispatcherTimer'la bırakılır.
        _reveal.ScheduleRelease(maxDelay, RevealMs, gen);
    }

    /// <summary>Tek bir düğümün beliriş animasyonu (opaklık 0→1 + 5px yukarıdan).</summary>
    private void ApplyRevealTo(GraphNodeVisual visual, double delayMs)
    {
        visual.Cell.BeginAnimation(OpacityProperty, null);
        // CSS `both` fill paritesi: gecikme boyunca opaklık 0 TUTULUR — flash yok (feasibility §3.4).
        visual.Cell.Opacity = 0.0;
        var rise = new TranslateTransform(0, -RevealRisePx);
        visual.Cell.RenderTransform = rise;

        var begin = TimeSpan.FromMilliseconds(delayMs);
        var duration = TimeSpan.FromMilliseconds(RevealMs);
        var fade = MotionTokens.SplineTo(1.0, duration, EaseOut);
        fade.BeginTime = begin;
        fade.KeyFrames.Insert(0, new DiscreteDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        visual.Cell.BeginAnimation(OpacityProperty, fade);

        var slide = MotionTokens.SplineTo(0.0, duration, EaseOut);
        slide.BeginTime = begin;
        slide.KeyFrames.Insert(0, new DiscreteDoubleKeyFrame(-RevealRisePx, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        rise.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    /// <summary>[E3/T41] Reveal tamamlandığında hero'yu bırakan generation-guarded karar. Test bunu doğrudan
    /// çağırır (gerçek timer tick'i beklemeden).</summary>
    internal void ReleaseRevealHeroIfCurrent(int gen) => _reveal.ReleaseIfCurrent(gen);

    // ---------------------------------------------------------------- jestler (§2.3 "Serbest gezinme")

    /// <summary>
    /// Zeminde sol tuş basıldı. Henüz hiçbir şey OLMAZ: bu basış bir tıklama da olabilir, bir sürüklemenin
    /// başı da — ayrımı <see cref="DragThresholdPx"/> yapar (<see cref="HandlePanMove"/>).
    /// </summary>
    /// <returns><c>true</c> = jest başladı (çağıran capture alır).</returns>
    internal bool HandlePanStart(Point position)
    {
        if (_slotOrder.Count == 0) return false;
        _panPressed = true;
        _dragging = false;
        _panOrigin = position;
        _panLast = position;
        return true;
    }

    /// <summary>Sürükleme: eşik aşıldığı KAREDE el imleci takılır; sonrasında her hareket kamerayı ekran
    /// deltası kadar öteler. Eşik BASIŞ NOKTASINDAN ölçülür — her karede sıfırlanan deltadan değil, aksi
    /// halde yavaş bir sürükleme hiç eşiği aşamazdı.</summary>
    internal void HandlePanMove(Point position)
    {
        if (!_panPressed) return;

        if (!_dragging)
        {
            var fromOrigin = position - _panOrigin;
            // Prototip: `Math.abs(dx) + Math.abs(dy) > 3` (BuildApp.jsx:314).
            if (Math.Abs(fromOrigin.X) + Math.Abs(fromOrigin.Y) <= DragThresholdPx) return;
            _dragging = true;
            Ground.Cursor = Cursors.Hand; // el imleci YALNIZ sürüklerken
        }

        // Delta HER hareketten sonra sıfırlanır: birikirse kamera imleç hızının katlarıyla kayar ve elin
        // altındaki nokta grafı takip etmez.
        var delta = position - _panLast;
        _panLast = position;
        SnapCameraTo(GraphCamera.Pan(CurrentCamera, delta));
    }

    /// <summary>
    /// Bırakma. Eşik hiç aşılmadıysa bu bir TIKLAMADIR ve §2.3'ün iki kollu kuralı işler: seçim VARSA
    /// bırakılır, YOKSA görünüm varsayılana döner. Sürükleme olduysa hiçbir şey yapılmaz — "drag sonrası
    /// bırakma boş-alan tıklaması TETİKLEMEZ".
    /// </summary>
    internal void HandlePanEnd()
    {
        if (!_panPressed) return;
        bool wasDragging = _dragging;
        ResetPanGesture();
        if (wasDragging) return;

        if (_selectedNode is not null) SelectedNode = null;
        else AnimateCameraTo(GraphCamera.Default, animate: AnimationsEnabledProvider());
    }

    /// <summary>Wheel: imleç merkezli zoom (§2.3). Yön yalnız <paramref name="delta"/>'nın işaretinden
    /// okunur — kademe çarpansaldır, dolayısıyla ileri/geri simetriktir.</summary>
    internal void HandleWheel(Point cursor, int delta)
    {
        if (_slotOrder.Count == 0) return;
        double factor = delta > 0 ? GraphCamera.WheelZoomStep : 1 / GraphCamera.WheelZoomStep;
        AnimateCameraTo(
            GraphCamera.ZoomAt(CurrentCamera, cursor, factor),
            animate: AnimationsEnabledProvider(),
            durationMs: GraphCamera.WheelTransitionMs,
            splineKey: "KeySpline.EaseOut");
    }

    /// <summary>Jest durumunu sıfırlar ve el imlecini bırakır; seçime DOKUNMAZ — bu yüzden nötrdür ve üç ayrı
    /// anlamda çağrılabilir: capture kaybı (İPTAL), yeni topoloji ve <see cref="HandlePanEnd"/>'in temizlik
    /// adımı (seçim kararını çağıranın kendisi verir).</summary>
    private void ResetPanGesture()
    {
        _panPressed = false;
        _dragging = false;
        Ground.ClearValue(CursorProperty);
    }

    // ---------------------------------------------------------------- kamera

    /// <summary>
    /// Kameranın hedefi: seçim varsa odak kümesinin sığdırması, yoksa varsayılan görünüm. Kamera başka
    /// hiçbir sebeple hareket etmez (§2.3: koşu sırasında kamera durur).
    /// </summary>
    private void ApplyCamera(bool animate)
    {
        if (_slotOrder.Count == 0) return;

        var panel = ViewportSize;
        if (panel.Width <= 0 || panel.Height <= 0) return;

        AnimateCameraTo(ResolveCameraTarget(panel), animate && AnimationsEnabledProvider());
    }

    private CameraTransform ResolveCameraTarget(Size panel)
    {
        // [design v1.13.2 §2.5] Finale sırasında/sonrasında EffectiveSelection null döner ⇒ kamera
        // sığdırma yapmaz, doğrudan fit-all'a (Default) döner.
        if (EffectiveSelection is not { } selected || !_slots.TryGetValue(selected, out var target))
            return GraphCamera.Default;

        double x0 = double.PositiveInfinity, x1 = double.NegativeInfinity;
        double y0 = double.PositiveInfinity, y1 = double.NegativeInfinity;
        foreach (string name in _focusSet)
        {
            if (!_slots.TryGetValue(name, out var slot)) continue;
            x0 = Math.Min(x0, slot.Center.X); x1 = Math.Max(x1, slot.Center.X);
            y0 = Math.Min(y0, slot.Center.Y); y1 = Math.Max(y1, slot.Center.Y);
        }
        if (double.IsInfinity(x0)) return GraphCamera.Default;

        var camera = GraphCamera.FocusAndFit(
            panel,
            new Rect(x0, y0, x1 - x0, y1 - y0),
            _layout.NodeSize,
            new Vector(QuietGraphLayout.ContentInset, QuietGraphLayout.ContentInset));
        return ReserveRoomForSelectionLabel(camera, target.Center, panel);
    }

    /// <summary>
    /// [quiet] Kamerayı, seçili düğümün AD ETİKETİ panelin iç payının içinde kalacak kadar öteler.
    ///
    /// <para>Etiket her zaman düğümün ALTINDA durur (kullanıcı kararı) — dolayısıyla yer açmak etiketin
    /// değil kameranın işidir: en alttaki bantta ya da kenardaki bir sütunda seçilen düğümün etiketi
    /// paneli taşıyordu. Seçim zaten kamerayı hareket ettirdiği için doğru yer burasıdır.</para>
    ///
    /// <para>Düzeltme yalnız ÖTELEMEDİR; ölçek <see cref="GraphCamera.FocusAndFit"/>'in verdiği gibi kalır,
    /// böylece odak kümesinin sığdırması bozulmaz. Öteleme düğümü de etiketi de birlikte taşıdığı için tek
    /// adım yeter.</para>
    /// </summary>
    private CameraTransform ReserveRoomForSelectionLabel(CameraTransform camera, Point contentCentre, Size panel)
    {
        if (SelectionLabelBox.Visibility != Visibility.Visible) return camera;

        var box = SelectionLabelBox.DesiredSize;
        var topLeft = GraphOverlay.NameLabelTopLeft(
            contentCentre, camera, PaintedHalfExtent(withRing: true, camera.Scale), panel, box);

        return camera with
        {
            Tx = GraphCamera.RoundPixels(camera.Tx + Nudge(topLeft.X, box.Width, panel.Width)),
            Ty = GraphCamera.RoundPixels(camera.Ty + Nudge(topLeft.Y, box.Height, panel.Height)),
        };
    }

    /// <summary>Bir kutuyu panelin iç payına sokmak için gereken EN KÜÇÜK kayma. Kutu paya hiç sığmıyorsa
    /// ortalanır — iki yandan eşit taşmak, bir yana yaslanmaktan okunaklıdır.</summary>
    private static double Nudge(double start, double length, double panelLength)
    {
        double low = QuietGraphLayout.ContentInset;
        double high = panelLength - QuietGraphLayout.ContentInset - length;
        if (high < low) return (panelLength - length) / 2 - start;
        if (start < low) return low - start;
        if (start > high) return high - start;
        return 0;
    }

    private void AnimateCameraTo(
        CameraTransform camera,
        bool animate,
        double durationMs = GraphCamera.TransitionMs,
        string splineKey = "KeySpline.EaseInOut")
    {
        // Hedef DEĞİŞMEDİYSE hiçbir animasyon yeniden başlatılmaz: aynı hedefe her seferinde yeni bir geçiş
        // başlatmak uçuştaki geçişi sürekli "yeniden doğurur" (Zeno etkisi — kamera hedefe hiç oturmaz).
        if (camera == CurrentCamera) return;
        CurrentCamera = camera;
        LastCameraAnimated = animate;

        if (!animate)
        {
            SnapCameraTo(camera);
            return;
        }

        var duration = TimeSpan.FromMilliseconds(durationMs);
        var spline = MotionTokens.ResolveKeySpline(this, splineKey, new KeySpline(0.65, 0, 0.35, 1));
        // From'SUZ To-animasyonu + SnapshotAndReplace = CSS transition retarget paritesi (feasibility §3.4).
        _cameraScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            MotionTokens.SplineTo(camera.Scale, duration, spline), HandoffBehavior.SnapshotAndReplace);
        _cameraScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            MotionTokens.SplineTo(camera.Scale, duration, spline), HandoffBehavior.SnapshotAndReplace);
        _cameraTranslate.BeginAnimation(TranslateTransform.XProperty,
            MotionTokens.SplineTo(camera.Tx, duration, spline), HandoffBehavior.SnapshotAndReplace);
        _cameraTranslate.BeginAnimation(TranslateTransform.YProperty,
            MotionTokens.SplineTo(camera.Ty, duration, spline), HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Kamerayı ANINDA uygular (uçuştaki animasyonu keserek) — sürükleme kareleri ve
    /// reduced-motion bu yolu paylaşır (kopya YASAK).</summary>
    private void SnapCameraTo(CameraTransform camera)
    {
        CurrentCamera = camera;
        // Overlay'i BURADA tazelemeye gerek yok: aşağıdaki transform yazımları Changed'i ateşler ve tek
        // kanaldan (OnCameraFrame) güncellenir — ikinci bir çağrı kopya olurdu.
        _cameraScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _cameraScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _cameraTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        _cameraTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        _cameraScale.ScaleX = camera.Scale;
        _cameraScale.ScaleY = camera.Scale;
        _cameraTranslate.X = camera.Tx;
        _cameraTranslate.Y = camera.Ty;
    }

    private void ShowEmptyState(bool visible)
    {
        EmptyState.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        Viewport.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------------------------------------------------------------- test/görünürlük yüzeyi

    internal IReadOnlyDictionary<string, GraphNodeVisual> NodeVisuals =>
        _slots.ToDictionary(pair => pair.Key, pair => pair.Value.Visual, StringComparer.Ordinal);

    internal int NodeCount => _slotOrder.Count;
    /// <summary>Statü görselinin kaç kez uygulandığı — "değişmediyse dokunma" hızlı yolunun ve gizli-panel
    /// kapısının kanıtı.</summary>
    internal int NodeStatusApplyCount { get; private set; }
    /// <summary>Yerleşimin kaç kez YENİDEN hesaplandığı (panel ölçüsü değişimi).</summary>
    internal int LayoutComputeCount { get; private set; }
    internal int RevealGeneration => _reveal.Generation;
    internal bool HasPendingRevealRelease => _reveal.HasPendingRelease;
    internal CameraTransform CurrentCamera { get; private set; }
    internal bool LastCameraAnimated { get; private set; }
    internal string HeaderCountsText => CountsText.Text;
    internal FontFamily HeaderCountsFontFamily => CountsText.FontFamily;
    internal bool IsEmptyStateVisible => EmptyState.Visibility == Visibility.Visible;
    internal string EmptyStateText => EmptyStateLabel.Text;
    internal Size ViewportSize => new(Ground.ActualWidth, Ground.ActualHeight);
    /// <summary>Düğümün İÇERİK koordinatlarındaki merkezi.</summary>
    internal Point NodeCenter(string name) => _slots[name].Center;
    /// <summary>Canlı düğüm kenarı (pitch × 0.6, kelepçeli).</summary>
    internal double NodeSize => _layout.NodeSize;
    /// <summary>Canlı düğüm adımı.</summary>
    internal double Pitch => _layout.Pitch;
    /// <summary>Kameranın CANLI (uygulanmış) hâlini testten oynatır — bir animasyon ara karesinin yaptığı
    /// şeyin ta kendisi. Headless'ta compositor saati ilerlemez, bu yüzden ara kare elle sürülür.</summary>
    internal void MoveLiveCameraForTest(CameraTransform camera)
    {
        _cameraScale.ScaleX = _cameraScale.ScaleY = camera.Scale;
        _cameraTranslate.X = camera.Tx;
        _cameraTranslate.Y = camera.Ty;
    }
    /// <summary>Açılış dalgasında bir düğüme uygulanan gecikme (dalga oynamadıysa <c>null</c>).</summary>
    internal double? RevealDelayOf(string nodeName) =>
        _slots.TryGetValue(nodeName, out var slot) ? slot.Visual.RevealDelayMs : null;
    /// <summary>TÜM beads yörüngelerinin paylaştığı saat (hiç dönmüyorsa <c>null</c>).</summary>
    internal AnimationClock? BeadsClock => _beadsClock;
    /// <summary>Canlı yörünge geometrisi (düğüm boyutundan türer).</summary>
    internal BeadsGeometry BeadsGeometry => _beadsGeometry;

    /// <summary>Hover'ı testten sürer — headless'ta gerçek <c>MouseEnter</c> yükseltilemez
    /// (<c>PresentationSource</c> yok). Seam'in ÜSTÜNDEKİ kablo (Body.MouseEnter/MouseLeave) gerçek routed
    /// event'le ayrıca pinlenir.</summary>
    internal void SetHoverForTest(string? nodeName) => SetHover(nodeName);
    internal string? HoveredNode => _hoveredNode;
    internal Visibility TooltipVisibility => TooltipBox.Visibility;
    internal string TooltipContent => TooltipText.Text;
    internal Point TooltipTopLeft => new(Canvas.GetLeft(TooltipBox), Canvas.GetTop(TooltipBox));
    internal Size TooltipBoxSize => TooltipBox.DesiredSize;
    internal Border TooltipElement => TooltipBox;
    /// <summary>Seçimde kurulan bağımlılık çizgileri — seçim yokken BOŞ.</summary>
    internal IReadOnlyList<Path> SelectionEdgePaths => _selectionEdges;
    /// <summary>Akan kesiklerin paylaşımlı saati (akmıyorsa <c>null</c>).</summary>
    internal AnimationClock? EdgeFlowClock => _edgeFlowClock;
    internal Visibility SelectionLabelVisibility => SelectionLabelBox.Visibility;
    internal string SelectionLabelContent => SelectionLabelText.Text;
    internal Point SelectionLabelTopLeft => new(Canvas.GetLeft(SelectionLabelBox), Canvas.GetTop(SelectionLabelBox));
    internal Size SelectionLabelBoxSize => SelectionLabelBox.DesiredSize;
    /// <summary>HER ZAMAN <c>null</c> olmalı — overlay kamera transform'unun DIŞINDA yaşar (§2.3).</summary>
    internal Transform? OverlayLayerTransform => OverlayLayer.RenderTransform as MatrixTransform is { } m
        && m.Matrix.IsIdentity ? null : OverlayLayer.RenderTransform;
    /// <summary>Bir düğümün opaklığını süren animasyon (anında uygulandıysa <c>null</c>) — hold-fade'in
    /// zamanlamasını pinleyen testlerin okuduğu yüzey.</summary>
    internal Timeline? OpacityAnimationOf(string nodeName) =>
        _slots.TryGetValue(nodeName, out var slot) ? slot.Visual.OpacityAnimation : null;
}
