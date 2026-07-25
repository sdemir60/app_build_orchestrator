using System.Globalization;
using System.Windows;
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
/// [T63] design-v1 §2.3 dependency graph — <b>Shapes yolu</b>: her düğüm ve kenar birer UIElement
/// (<see cref="Rectangle"/>/<see cref="Path"/>), hit-test ve tooltip native. Tasarımın 36 düğüm / 58 kenarı bu
/// bandın (≲<see cref="FullDetailMaxNodes"/>) çok içinde (feasibility §3.5).
///
/// <para><b>[G2/It-5] Ölçek, katman göçüyle DEĞİL nesne sayısıyla çözüldü.</b> G1'in ölçümü darboğazın çizim
/// olmadığını gösterdi: maliyetin %64-72'si <see cref="SetGraph"/>'ın görsel-ağaç KURULUMU, %28-36'sı WPF'in aynı
/// ağacı ölçüp yerleştirmesi, saf layout aritmetiği ise %0,03. Ölçekleme lineer — yani "kötü algoritma" yok,
/// düğüm başına sabit maliyet yüksek. Bu yüzden aşağıdaki üçlü uygulandı ve <b>DrawingVisual katman göçü
/// YAPILMADI</b> (o göç ÇİZİM maliyetini hedefler, ölçülen darboğazı değil):
/// <list type="number">
///   <item><b>Viewport cull</b> (<see cref="GraphCulling"/>): görünür dünya dikdörtgenine değmeyen düğüm/kenarın
///     ağacı HİÇ kurulmaz; görünür alana girince kurulur.</item>
///   <item><b>Tembel rozet + LOD etiket</b> (<see cref="GraphNodeVisual"/>): düğüm başına 17 → 9 nesne.</item>
///   <item><b>Statü fast-path'i + paylaşılan frozen dash koleksiyonları</b>: 200ms'lik tick artık değişmemiş
///     düğüme hiç dokunmaz ve her çağrıda yeni <see cref="DoubleCollection"/> üretmez.</item>
/// </list></para>
///
/// <para><b>Motion sözleşmesi:</b> her animasyon başlangıcında <see cref="AnimationsEnabledProvider"/> TAZE
/// okunur (varsayılan <c>App.Motion</c>); reduced-motion'da → statik dash + kamerada animasyon yok + stagger yok.
/// Süre/eğri token'ları <c>Duration.*</c>/<c>KeySpline.*</c> anahtarlarından, renkler <c>Brush.*</c>
/// anahtarlarından (<c>SetResourceReference</c>) gelir — hex/ms gömülmez.</para>
/// </summary>
public partial class GraphView : UserControl
{
    /// <summary>
    /// [G2 · fix round 1 C3] <b>TAM DETAY</b> bandının üst sınırı: bu sayıya kadar (dahil) graf birebir G2
    /// öncesindeki gibi kurulur — <b>cull YOK, LOD YOK</b>, her düğüm/kenar koşulsuz materyalize edilir ve her
    /// düğümde etiket bulunur. Bugünkü graf görünümü ve tüm render testleri bu banttadır; "küçük grafta hiçbir
    /// şey değişmesin" güvencesi bu yüzden bir davranış varsayımı değil YAPISAL bir garantidir.
    ///
    /// <para>Üstünde <see cref="GraphCulling"/> ve etiket LOD'u birlikte devreye girer.</para>
    ///
    /// <para><b>Adın geçmişi:</b> sabit T51'de <c>ShapesPathMaxNodes</c> adıyla "Shapes yolunun üst sınırı"
    /// olarak tanımlanmış ama HİÇ okunmamıştı (ölü sabit). G2 onu DrawingVisual göçü yerine tam-detay eşiği
    /// olarak canlandırdı; ad da yeni anlamını taşısın diye değiştirildi.</para>
    /// </summary>
    public const int FullDetailMaxNodes = 150;

    /// <summary>Katman başına açılış gecikmesi (design-v1 §2.3: "katman başına 55ms").</summary>
    public const double LayerStaggerMs = 55.0;
    /// <summary>Stagger tavanı (Ek A #9: grafta 55ms/katman, tavan 330ms).</summary>
    public const double LayerStaggerCapMs = 330.0;
    /// <summary>Bir düğümün beliriş süresi (prototip <c>bo-reveal .3s</c>).</summary>
    public const double RevealMs = 300.0;
    /// <summary>Düğüm bu kadar YUKARIDAN gelir (prototip <c>translateY(-5px)</c>).</summary>
    public const double RevealRisePx = 5.0;
    /// <summary>Seçim dışı düğümlerin sönme opaklığı (design-v1 §2.3).</summary>
    public const double DimmedNodeOpacity = 0.25;
    /// <summary>Dekoratif sonsuz animasyonlarda kare hızı tavanı (feasibility §3.4).</summary>
    public const int DecorativeFrameRate = 30;
    /// <summary>Düğüm karesinin çerçeve kalınlığı (DS <c>DependencyGraphNode</c>: <c>selected ? 2 : 1.5</c>).</summary>
    public const double NodeBorderThickness = 1.5;
    /// <summary>Seçili düğüm karesinin çerçeve kalınlığı (DS: 2px).</summary>
    public const double SelectedNodeBorderThickness = 2.0;
    /// <summary>Building düğümün nabzı — DS <c>ds-node-pulse 1.6s var(--ease-in-out) infinite</c>.</summary>
    public const double PulseMs = 1600.0;
    /// <summary>Nabzın orta noktadaki opaklığı (DS <c>@keyframes: 50% { opacity: .5 }</c>).</summary>
    public const double PulseMinOpacity = 0.5;

    /// <summary>Icons.xaml geometrilerinin viewBox kenarı (lucide: 24).</summary>
    private const double IconViewBox = 24.0;

    /// <summary>[T64] Düğüm ikonu (lucide "package", 24'lük viewBox). Path data ARTIK BURADA DEĞİL: geometri
    /// uygulamanın TEK ikon sözlüğünden (<c>Resources/Icons.xaml</c>) çözülür. Bu sınıf yalnız ANAHTAR bilir —
    /// aynı path'in ikinci bir kopyası kaldığı sürece iki taraf sessizce ayrışabilirdi (T64 review, fix wave 1).</summary>
    internal const string PackageIconKey = "Icon.Package";
    /// <summary>Dep-hata rozetinin DOLU üçgeni (lucide depWarn) — aynı gerekçe, bkz. <see cref="PackageIconKey"/>.</summary>
    internal const string WarningTriangleIconKey = "Icon.DepWarn";

    /// <summary>[G2] discovered düğümün kesikli çerçevesi — TEK, DONMUŞ, paylaşımlı örnek. Eskiden her
    /// <c>ApplyNodeStatus</c> çağrısı (yani her düğüm × her 200ms tick) yeni bir koleksiyon allocate ediyordu;
    /// desen <c>EdgeStyleResolver</c>'ın statik dash örnekleriyle aynıdır.</summary>
    private static readonly DoubleCollection DiscoveredDash = FrozenDash([2.0, 2.0]);
    /// <summary>[G2] "dash yok" — aynı gerekçe (boş koleksiyon da bir allocation'dır).</summary>
    private static readonly DoubleCollection SolidDash = FrozenDash([]);

    /// <summary>[G2] Kenar dash desenlerinin donmuş karşılıkları. Anahtarlar <see cref="EdgeStyleResolver"/>'ın
    /// STATİK örnekleridir (referans eşitliği) — her stil uygulamasında yeni koleksiyon üretilmez.</summary>
    private static readonly Dictionary<IReadOnlyList<double>, DoubleCollection> EdgeDashes = new()
    {
        [EdgeStyleResolver.FlowDash] = FrozenDash([.. EdgeStyleResolver.FlowDash]),
        [EdgeStyleResolver.FlowDashThick] = FrozenDash([.. EdgeStyleResolver.FlowDashThick]),
        [EdgeStyleResolver.ErrorDash] = FrozenDash([.. EdgeStyleResolver.ErrorDash]),
        [EdgeStyleResolver.ErrorDashThick] = FrozenDash([.. EdgeStyleResolver.ErrorDashThick]),
    };

    /// <summary>[G2] İkonu 24 birimlik viewBox'tan 13px'e indiren TEK, DONMUŞ ölçek. Eskiden bu işi düğüm başına
    /// bir <see cref="Viewbox"/> (+ onun iç <c>ContainerVisual</c>'ı) yapıyordu — iki nesne. Sonuç geometrik
    /// olarak BİREBİR aynıdır (<c>RenderTransformOrigin</c> merkezde olduğu için 24'lük tuval kendi merkezinde
    /// küçülür) ve <c>GraphRenderTests</c>'te sayısal olarak pinlenmiştir.</summary>
    private static readonly ScaleTransform IconScale = FrozenScale(GraphLayout.NodeSize * 0.5 / IconViewBox);

    /// <summary>TÜM düğümler (materyalize olsun olmasın) — statü/kamera/kenar mantığının kaynağı.</summary>
    private readonly Dictionary<string, GraphNodeSlot> _slots = new(StringComparer.Ordinal);
    private readonly List<GraphNodeSlot> _slotOrder = [];
    /// <summary>TÜM kenarlar (materyalize olsun olmasın).</summary>
    private readonly List<GraphEdgeSlot> _edgeSlots = [];
    /// <summary>Komşuluk (seçim sönmesi + seçimin materyalize edilmesi) — kenarların TAMAMINDAN kurulur.</summary>
    private readonly Dictionary<string, List<string>> _neighbours = new(StringComparer.Ordinal);

    /// <summary>YALNIZ materyalize olmuş düğüm görselleri.</summary>
    private readonly Dictionary<string, GraphNodeVisual> _nodes = new(StringComparer.Ordinal);
    /// <summary>YALNIZ materyalize olmuş kenar görselleri.</summary>
    private readonly List<GraphEdgeVisual> _edges = [];
    private readonly List<Path> _flowingEdges = [];
    private readonly ScaleTransform _cameraScale = new(1, 1);
    private readonly TranslateTransform _cameraTranslate = new();
    /// <summary>Kenarlar düğümlerin ALTINDA kalmalı. Tembel materyalizasyonda ekleme SIRASI z-order'ı garanti
    /// edemez (bir kenar bir düğümden sonra görünür alana girebilir) — bu yüzden iki AYRI katman host'u vardır.
    /// İkisi de <c>World</c>'ün çocuğudur, dolayısıyla kamera transform'u TEK ortak parent'ta kalır.</summary>
    private readonly Canvas _edgeLayer = new();
    private readonly Canvas _nodeLayer = new();

    private GraphLayoutResult _layout = GraphLayout.Compute([]);
    private string? _selectedNode;
    private HashSet<string> _neighbourSet = new(StringComparer.Ordinal);
    private bool _isSettled;
    private Point? _previousFocus;
    private ClockGroup? _dashClockRoot;
    private AnimationClock? _thinDashClock;
    private AnimationClock? _thickDashClock;
    private IMotionSettings? _subscribedMotion;
    private bool _edgesAnimated;
    private bool _hasCamera;
    private bool _cullEnabled;
    /// <summary>[G2 · fix round 1 B1] EN SON taranmış dünya bölgesi — <b>kümülatif DEĞİL</b>, her taramada
    /// DEĞİŞTİRİLİR. Yalnız gereksiz taramayı eler: bu bölgedeki her şey materyalize edilmiş olduğundan, onun
    /// İÇİNDE kalan yeni bir bölge için tekrar gezinmeye gerek yoktur. (İlk turda burada kümülatif bir birleşim
    /// tutuluyordu; o, uzak iki görünüm arasında HİÇ GÖRÜLMEMİŞ düğümleri de materyalize ediyordu.)</summary>
    private Rect _scannedRegion = Rect.Empty;
    private IDisposable? _revealHero;
    /// <summary>[G2 · fix round 1 B2] Açılış stagger'ı ŞU AN oynuyor mu + penceresi. Bu pencere içinde
    /// materyalize olan düğüm de stagger'a katılır (motion sözleşmesi: düğüm animasyonu ATLAYARAK belirmez).</summary>
    private bool _revealPlaying;
    private long _revealStartTicks;
    private long _revealEndTicks;
    private int _revealGen; // [E3/T41] her PlayRevealStagger yeni bir reveal kuşağıdır — stale release'i eleyen damga
    private DispatcherTimer? _revealReleaseTimer;

    public GraphView()
    {
        InitializeComponent();

        World.Children.Add(_edgeLayer);
        World.Children.Add(_nodeLayer);

        // CSS `transform: translate(...) scale(...)` = önce ölçek, sonra öteleme (TransformGroup sırası birebir).
        World.RenderTransform = new TransformGroup { Children = { _cameraScale, _cameraTranslate } };
        World.RenderTransformOrigin = new Point(0, 0);

        // Boş alana tıklama → seçim kalkar (düğüm tıklaması Handled=true yaptığından buraya ulaşmaz).
        Ground.MouseLeftButtonDown += (_, _) => SelectedNode = null;
        Ground.SizeChanged += (_, _) => ApplyCamera(animate: false);

        // [M-2] Canlı reduced-motion: OS ayarı koşu SIRASINDA değişirse akan dash ve nabız ANINDA durur/başlar
        // (aksi halde bir sonraki UpdateStatuses'a kadar dönmeye devam ederdi).
        Loaded += OnLoadedSubscribeMotion;
        Unloaded += OnUnloadedUnsubscribeMotion;

        ShowEmptyState(true);
    }

    private static DoubleCollection FrozenDash(double[] values)
    {
        var collection = new DoubleCollection(values);
        collection.Freeze();
        return collection;
    }

    private static ScaleTransform FrozenScale(double scale)
    {
        var transform = new ScaleTransform(scale, scale);
        transform.Freeze();
        return transform;
    }

    /// <summary>Motion sinyalinin TAZE okunduğu kapı (D8 — sınıf statik <c>App.Motion</c>'a doğrudan bağlanmaz,
    /// testler enjekte eder).</summary>
    public Func<bool> AnimationsEnabledProvider { get; set; } =
        () => BuildOrchestrator.App.App.Motion?.AnimationsEnabled ?? false;

    /// <summary>[M-2] <c>AnimationsEnabledChanged</c>'e abone olunacak kaynak; null ise <c>App.Motion</c>.
    /// Testler kendi sahtesini enjekte eder (abonelik <c>Loaded</c>'da kurulur, <c>Unloaded</c>'da bırakılır).</summary>
    public IMotionSettings? MotionSettings { get; set; }

    /// <summary>[E3/T41/DD9] Reveal stagger'ının içine girdiği hero-mutex; null ise <c>App.HeroMotion</c> (TAZE).
    /// Graf reveal ile liste reveal AYNI hero'dur (<see cref="RevealHeroKey"/>) — başka bir hero sürüyorsa graf
    /// reveal dekoratif yolu atlayıp düğümleri ani yerleştirir. Testler kendi <c>MotionCoordinator</c>'ını enjekte
    /// eder.</summary>
    public MotionCoordinator? HeroCoordinator { get; set; }

    /// <summary>Graf reveal + liste reveal ORTAK hero anahtarı (co-tetiklenir → aynı hero, birlikte oynar).</summary>
    internal const string RevealHeroKey = "sync-reveal";

    private MotionCoordinator? ActiveHeroCoordinator => HeroCoordinator ?? BuildOrchestrator.App.App.HeroMotion;

    private void OnLoadedSubscribeMotion(object? sender, RoutedEventArgs e)
    {
        if (_subscribedMotion is not null) return;
        _subscribedMotion = MotionSettings ?? BuildOrchestrator.App.App.Motion;
        if (_subscribedMotion is not null)
            _subscribedMotion.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
    }

    private void OnUnloadedUnsubscribeMotion(object? sender, RoutedEventArgs e)
    {
        if (_subscribedMotion is not null)
        {
            _subscribedMotion.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
            _subscribedMotion = null;
        }
        // [M-d] View unload olurken paylaşımlı dash clock'u da bırak — aksi halde (M-3'ün kapsamadığı bu yol
        // için) timing engine, view artık ağaçta olmasa bile 30fps'te uyanık kalırdı.
        ReleaseDashClock();
        // [E3/T41] Reveal ortasında unload olursa hero'yu bırak — aksi halde bir sonraki hero sonsuza dek bloke olurdu.
        ReleaseRevealHero();
    }

    private void OnAnimationsEnabledChanged(object? sender, EventArgs e) => ReapplyMotion();

    /// <summary>Motion sinyali canlı değiştiğinde sürmekte olan sonsuz animasyonları (akan dash + building nabzı)
    /// yeni sinyale göre yeniden kurar. <see cref="ApplyEdgeStyles"/> zaten sinyal değişimini tespit edip TÜM
    /// kenar kablajını yeniler; nabız düğüm başına ayrıca güncellenir.</summary>
    internal void ReapplyMotion()
    {
        ApplyEdgeStyles();
        foreach (var visual in _nodes.Values)
            ApplyBuildingPulse(visual);
    }

    /// <summary>Koşu bitti/durduruldu mu — kamera bu durumda grafın tam merkezine oturur (design-v1 §2.3).</summary>
    public bool IsSettled
    {
        get => _isSettled;
        set
        {
            if (_isSettled == value) return;
            _isSettled = value;
            ApplyCamera(animate: true);
        }
    }

    /// <summary>Seçili düğüm (null = seçim yok). Değişince: halka + sönme + kenar stilleri + kamera güncellenir.</summary>
    public string? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (string.Equals(_selectedNode, value, StringComparison.Ordinal)) return;
            _selectedNode = value;
            // [G2] Seçili düğüm, komşuları ve onlara değen kenarlar ASLA cull edilmez — seçim ekran dışından da
            // (liste tıklamasıyla) gelebilir ve kamera oraya ancak 460ms'de varır.
            MaterializeSelection();
            ApplySelection();
            ApplyEdgeStyles();
            ApplyCamera(animate: true);
            SelectionChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<string?>? SelectionChanged;

    // ---------------------------------------------------------------- veri girişi

    /// <summary>Topolojiyi (düğüm + kenar) kurar: yerleşim, görseller, kenar geometrileri ve ilk açılış
    /// stagger'ı. Yalnız topoloji DEĞİŞTİĞİNDE çağrılır — statü güncellemeleri için
    /// <see cref="UpdateStatuses"/> kullanılır (yeniden inşa YOK).</summary>
    public void SetGraph(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        _edgeLayer.Children.Clear();
        _nodeLayer.Children.Clear();
        // [M-d] Atılacak eski görsellerin (varsa) sonsuz nabız animasyonunu bırak — aksi halde bunlar artık
        // ağaçta/_nodes'ta olmasa bile timing engine'de 30fps'te uyanık kalırlardı (M-3'ün kapsadığı dash clock
        // sızıntısıyla AYNI sınıf, düğüm nabzı için).
        foreach (var stale in _nodes.Values)
            StopPulse(stale);
        _nodes.Clear();
        _edges.Clear();
        _slots.Clear();
        _slotOrder.Clear();
        _edgeSlots.Clear();
        _neighbours.Clear();
        _flowingEdges.Clear();
        _previousFocus = null;
        _hasCamera = false; // yeni topoloji → kamera hedefi baştan hesaplanır
        CurrentCamera = default;
        _scannedRegion = Rect.Empty;
        _revealPlaying = false; // eski grafın reveal penceresi yeni grafın materyalizasyonuna sızmasın

        // [M-4] Global Constraint: sayı biçimlemesi InvariantCulture.
        CountsText.Text = string.Format(
            CultureInfo.InvariantCulture, "{0} projects · {1} dependencies", nodes.Count, edges.Count);
        ShowEmptyState(nodes.Count == 0);
        if (nodes.Count == 0) return;

        _layout = GraphLayout.Compute(nodes);
        World.Width = _layout.Width;
        World.Height = _layout.Height;
        // [G2 · fix round 1 A2] TAM DETAY bandı: bugünkü graf boyutlarında (onlarca düğüm) NE cull NE LOD
        // devreye girer ⇒ görünüm ve nesne kurulumu birebir eskisi gibidir. İki mekanizma da AYNI kapıya
        // bağlıdır — LOD'un ayrı bir eşikten kaçıp küçük grafta etiket düşürmesi mümkün değildir.
        bool fullDetail = nodes.Count <= FullDetailMaxNodes;
        _cullEnabled = !fullDetail;
        // [fix round 1 A1] LOD eşiği katmanın EN GENİŞ etiketinin ÇİZİLEN genişliğinden türetilir (kelepçeden
        // değil). Ölçüm katman başına TEK kez yapılır ve yalnız tam-detay bandının DIŞINDA gerekir.
        var labelWidths = fullDetail ? null : MeasureLayerLabelWidths(nodes);

        foreach (var node in nodes)
        {
            if (!_layout.Positions.TryGetValue(node.Name, out var center)) continue;
            var slot = new GraphNodeSlot
            {
                Model = node,
                Center = center,
                Bounds = GraphCulling.NodeBounds(center),
                ShowsLabel = labelWidths is null || GraphLayout.LabelsFit(
                    _layout.LayerSpacing.TryGetValue(node.Layer, out double s) ? s : GraphLayout.MaxNodeSpacing,
                    labelWidths.GetValueOrDefault(node.Layer, GraphLayout.NodeCellWidth)),
            };
            _slots[node.Name] = slot;
            _slotOrder.Add(slot);
        }

        foreach (var edge in edges)
        {
            if (!_layout.Positions.TryGetValue(edge.From, out var from) ||
                !_layout.Positions.TryGetValue(edge.To, out var to))
                continue;

            _edgeSlots.Add(new GraphEdgeSlot
            {
                Model = edge,
                From = from,
                To = to,
                Bounds = GraphCulling.EdgeBounds(from, to),
            });
            AddNeighbour(edge.From, edge.To);
            AddNeighbour(edge.To, edge.From);
        }

        UpdateMaterialization();     // cull kapalıysa TAMAMI burada kurulur (kamera beklenmez)
        MaterializeSelection();
        ApplyCamera(animate: false); // ilk yerleşim kamerayı KAYDIRMAZ; cull açıkken görünür kümeyi de kurar
        ApplySelection();
        ApplyEdgeStyles();
        PlayRevealStagger();
    }

    /// <summary>[G2 · fix round 1 A1] Katman → o katmandaki EN GENİŞ etiketin çizilen genişliği. Katman başına
    /// tek ölçüm (<see cref="GraphLabelMetrics"/>); LOD kararı bunu aralıkla karşılaştırır.</summary>
    private Dictionary<int, double> MeasureLayerLabelWidths(IReadOnlyList<GraphNode> nodes)
    {
        var byLayer = new Dictionary<int, string>();
        foreach (var node in nodes)
            if (!byLayer.TryGetValue(node.Layer, out string? longest) || node.ShortName.Length > longest.Length)
                byLayer[node.Layer] = node.ShortName;

        var widths = new Dictionary<int, double>(byLayer.Count);
        foreach (var (layer, longest) in byLayer)
            widths[layer] = GraphLabelMetrics.WidestLabelWidth([longest], LabelFontFamily);
        return widths;
    }

    /// <summary>[G2 fix round 1 · A1] Etiket ölçümünde kullanılan aile; null ise <see cref="AppFonts.Mono"/>.
    /// TEST SEAM'i: <c>pack://</c> aileler gerçek bir <c>Application</c> olmadan çözülmez, testler
    /// <c>TestAssets/Fonts</c>'a <c>file://</c> tabanlı bir aile enjekte eder (<c>TrackedTextBlockTests</c>
    /// deseni). Üretimde ASLA set edilmez — etiketin kendisi her zaman <see cref="AppFonts.Mono"/> çizer.</summary>
    internal FontFamily? LabelFontFamily { get; set; }

    private void AddNeighbour(string from, string to)
    {
        if (!_neighbours.TryGetValue(from, out var list))
            _neighbours[from] = list = [];
        list.Add(to);
    }

    /// <summary>Statüleri (ve dep-hata bayraklarını) yerinde günceller: düğüm renkleri/rozetleri, kenar stilleri
    /// ve kamera hedefi (building frontier). Topoloji ve geometri korunur, stagger TEKRAR OYNAMAZ.</summary>
    public void UpdateStatuses(IReadOnlyList<GraphNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        foreach (var node in nodes)
        {
            if (!_slots.TryGetValue(node.Name, out var slot)) continue;
            // [G2] "Değişmediyse dokunma" — kenar tarafındaki (ApplyEdgeStyle) desenin düğüm SİMETRİĞİ.
            // GraphNode bir record'dur: değer eşitliği burada güvenlidir ve statü görselinin TAMAMI yalnız bu
            // modelden türetilir. Eskiden her tick her düğümde 2× SetResourceReference + IconPaint.Apply
            // (ağaç yukarı TryFindResource yürüyüşü) + yeni DoubleCollection allocation yapılıyordu.
            if (slot.Model == node) continue;
            slot.Model = node;
            if (slot.Visual is not { } visual) continue;
            visual.Model = node;
            ApplyNodeStatus(visual);
        }

        ApplyEdgeStyles();
        ApplyCamera(animate: true);
    }

    // ---------------------------------------------------------------- [G2] cull / materyalizasyon

    /// <summary>
    /// [fix round 1 · B1] Materyalizasyon kararı <b>ŞU ANKİ görünür alana</b> göre verilir; görülmüş tüm
    /// alanların kümülatif birleşimine göre DEĞİL. Uzak iki görünüm arasında gezinmek, aralarında kalan ve hiç
    /// görülmemiş düğümleri artık materyalize etmez.
    ///
    /// <para><paramref name="traversing"/>, kameranın hedefe <b>animasyonla</b> gideceğini söyler: 460ms'lik
    /// geçişin ara karelerinde görünen dikdörtgen, mevcut görünüm ile hedefin ARASINDADIR, dolayısıyla o iki
    /// dikdörtgenin sınır kutusu taranır (yalnız BU İKİSİ — birikim yok). Kamera anlık oturuyorsa (reduced
    /// motion, ilk yerleşim, panel yeniden boyutlanması) ara kare yoktur ve yalnız hedef taranır.</para>
    ///
    /// <para>Cull tek yönlüdür: bir kez kurulan görsel sökülmez, yalnız yenisi eklenir.</para>
    /// </summary>
    private void UpdateMaterialization(bool traversing = false)
    {
        if (_slotOrder.Count == 0) return;

        if (!_cullEnabled)
        {
            foreach (var slot in _slotOrder)
                if (slot.Visual is null) MaterializeNode(slot);
            foreach (var edge in _edgeSlots)
                if (edge.Visual is null) MaterializeEdge(edge);
            return;
        }

        var viewport = ViewportSize;
        if (viewport.Width <= 0 || viewport.Height <= 0) return; // henüz ölçülmedi — SizeChanged yeniden sorar

        var region = GraphCulling.VisibleWorldRect(viewport, CurrentCamera);
        if (region.IsEmpty) return;

        if (traversing)
        {
            var live = GraphCulling.VisibleWorldRect(viewport, LiveCamera);
            if (!live.IsEmpty) region = Rect.Union(live, region);
        }

        // Bu bölge en son taranan bölgenin İÇİNDEYSE her şeyi zaten kurmuşuz — O(N) gezinmeye gerek yok.
        if (!_scannedRegion.IsEmpty && _scannedRegion.Contains(region)) return;
        _scannedRegion = region; // DEĞİŞTİRİLİR, birleştirilmez

        foreach (var slot in _slotOrder)
            if (slot.Visual is null && region.IntersectsWith(slot.Bounds)) MaterializeNode(slot);
        foreach (var edge in _edgeSlots)
            if (edge.Visual is null && region.IntersectsWith(edge.Bounds)) MaterializeEdge(edge);
    }

    /// <summary>Kameranın O ANDA ekrana uygulanmış (animasyon sürüyorsa ara karedeki) hâli — hedefi değil.</summary>
    private CameraTransform LiveCamera => new(_cameraScale.ScaleX, _cameraTranslate.X, _cameraTranslate.Y);

    /// <summary>Seçili düğümü, DOĞRUDAN komşularını ve seçime değen kenarları — nerede olurlarsa olsunlar —
    /// materyalize eder. Seçim ekranın tamamen dışından gelebilir (liste tıklaması) ve kamera oraya ancak
    /// animasyon sonunda varır; halka/sönme/kalın kenar o ana kadar da doğru olmalıdır.</summary>
    private void MaterializeSelection()
    {
        if (_selectedNode is not { } selected || !_slots.ContainsKey(selected)) return;

        MaterializeByName(selected);
        if (_neighbours.TryGetValue(selected, out var neighbours))
            foreach (string name in neighbours)
                MaterializeByName(name);

        foreach (var edge in _edgeSlots)
        {
            if (edge.Visual is not null) continue;
            if (string.Equals(edge.Model.From, selected, StringComparison.Ordinal) ||
                string.Equals(edge.Model.To, selected, StringComparison.Ordinal))
                MaterializeEdge(edge);
        }
    }

    private void MaterializeByName(string name)
    {
        if (_slots.TryGetValue(name, out var slot) && slot.Visual is null) MaterializeNode(slot);
    }

    private void MaterializeNode(GraphNodeSlot slot)
    {
        var visual = BuildNodeVisual(slot);
        slot.Visual = visual;
        _nodes[slot.Model.Name] = visual;
        _nodeLayer.Children.Add(visual.Cell);
        ApplyNodeSelection(visual, animate: false);
        JoinRevealIfPlaying(visual);
    }

    /// <summary>
    /// [G2 · fix round 1 B2] Açılış stagger'ı SÜRERKEN materyalize olan düğüm de stagger'a KATILIR.
    ///
    /// <para>MOTION SÖZLEŞMESİ bağlayıcıdır: düğüm animasyonu atlayarak, tam opaklıkta belirmez. Bu yol gerçek
    /// bir senaryodur — <c>MainWindow</c> her büyük graf rebuild'inde <c>SetGraph</c>'ın hemen ardından
    /// <c>IsSettled</c>'ı iter ve kamera yeniden hedeflenir, yani reveal penceresinin İÇİNDE yeni düğümler
    /// materyalize olur.</para>
    ///
    /// <para>Gecikme, kuşağın başlangıcından bu yana GEÇEN süre düşülerek verilir ⇒ geç gelen düğüm kendi
    /// katmanının zamanlamasına oturur, sıfırdan yeni bir gecikme başlatmaz. Pencere kapandıysa (veya reduced
    /// motion) düğüm zaten ani yerleşir.</para>
    /// </summary>
    private void JoinRevealIfPlaying(GraphNodeVisual visual)
    {
        if (!_revealPlaying) return;

        long now = Environment.TickCount64;
        if (now >= _revealEndTicks || !AnimationsEnabledProvider())
        {
            _revealPlaying = false;
            return;
        }

        double remaining = Math.Max(0, RevealDelayMs(visual.Model.Layer) - (now - _revealStartTicks));
        ApplyRevealTo(visual, remaining);
    }

    private void MaterializeEdge(GraphEdgeSlot slot)
    {
        var path = new Path
        {
            Data = GraphLayout.BuildEdgeGeometry(slot.From, slot.To),
            IsHitTestVisible = false, // kenarlar tıklanmaz; boş alana tıklama seçimi kaldırabilsin
        };
        // NOT: eğri bezier'lerde EdgeMode=Aliased KULLANILMAZ — tırtıklanır (feasibility §3.5).
        var visual = new GraphEdgeVisual { Model = slot.Model, Path = path };
        slot.Visual = visual;
        _edges.Add(visual);
        _edgeLayer.Children.Add(path);
        // Stil HEMEN uygulanır: kenar bir sonraki ApplyEdgeStyles'a kadar boyasız (görünmez) kalmamalı.
        ApplyEdgeStyle(visual, AnimationsEnabledProvider(), force: false);
    }

    // ---------------------------------------------------------------- düğüm görselleri

    private GraphNodeVisual BuildNodeVisual(GraphNodeSlot slot)
    {
        var node = slot.Model;
        var ring = new Rectangle
        {
            // DS: 2px outline + 2px offset ⇒ 26 + 2×(offset 2 + yarım kalem 1) = 32; yarıçap da payla büyür.
            Width = GraphLayout.NodeSize + 6,
            Height = GraphLayout.NodeSize + 6,
            RadiusX = GraphLayout.NodeCornerRadius + 3,
            RadiusY = GraphLayout.NodeCornerRadius + 3,
            StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        ring.SetResourceReference(Shape.StrokeProperty, "Brush.FocusRing");

        var square = new Rectangle
        {
            Width = GraphLayout.NodeSize,
            Height = GraphLayout.NodeSize,
            RadiusX = GraphLayout.NodeCornerRadius,
            RadiusY = GraphLayout.NodeCornerRadius,
            StrokeThickness = NodeBorderThickness, // seçiliyken 2px — bkz. ApplySelection (DS: selected ? 2 : 1.5)
        };

        // [T60] Geometri + boya (kontur mu dolgu mu, hangi kalınlıkta) TEK yerden: Icons.xaml'in kardeş
        // Icon.X.StrokeThickness anahtarı. Kalınlık burada ARTIK YAZILI DEĞİL — ApplyNodeStatus statüye göre
        // fırçayı da verdiği için ikonun boyası oradan sürülür (bkz. IconPaint.Apply).
        // [G2] 24 → 13px indirgemesi PAYLAŞILAN donmuş bir ScaleTransform'ladır (Viewbox + iç ContainerVisual
        // yerine): merkezden ölçeklendiği için sonuç birebir aynıdır ve düğüm başına iki nesne kazandırır.
        var icon = new Path();
        var iconBox = new Canvas
        {
            Width = IconViewBox,
            Height = IconViewBox,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = IconScale,
            Children = { icon },
        };

        // Nabız kabı: DS'te `ds-node-pulse` YALNIZ kare span'ındadır — halka (outline) ve ikon onunla birlikte
        // solar, dep-hata rozeti (kardeş eleman) ise solmaz. Bu yüzden rozet bu kabın DIŞINDA kalır.
        var pulseHost = new Grid
        {
            Width = GraphLayout.NodeSize,
            Height = GraphLayout.NodeSize,
            Children = { ring, square, iconBox },
        };

        var squareHost = new Grid
        {
            Width = GraphLayout.NodeSize,
            Height = GraphLayout.NodeSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { pulseHost },
        };

        var body = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Brushes.Transparent, // etiketi de kapsayan tıklama alanı (prototipteki div gibi)
            Cursor = Cursors.Hand,
            Children = { squareHost },
        };

        TextBlock? label = null;
        if (slot.ShowsLabel)
        {
            label = new TextBlock
            {
                FontFamily = AppFonts.Mono,
                FontSize = 10,
                MaxWidth = GraphLayout.NodeCellWidth,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, GraphLayout.LabelGap, 0, 0),
                Text = node.ShortName,
            };
            // feasibility §3.4/§4.4: kök TextOptions.TextFormattingMode="Display" scale transform ALTINDA bozulur —
            // graf etiketlerinde LOKAL Ideal override (kök MainWindow ayarına DOKUNULMAZ, T65).
            TextOptions.SetTextFormattingMode(label, TextFormattingMode.Ideal);
            label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextDim"); // seçiliyken text-primary (DS)
            body.Children.Add(label);
        }
        else
        {
            // [G2 · fix round 1 A3] Etiketi düşen düğüm ANONİM KALMAZ: tam proje adını veren bir tooltip
            // taşır ve hedefi düğüm karesinin TAMAMIDIR (body = tıklama alanının kendisi). Tooltip DÜZ METİN
            // atanır — WPF, ToolTip kontrolünü ancak gösterilirken kurar, dolayısıyla düğüm başına HİÇBİR ek
            // nesne kurulmaz (WillBuildDot.cs:66 ile aynı desen; Controls.xaml'deki implicit ToolTip stili —
            // InitialShowDelay=0 + CustomPopupPlacementCallback, A13.2 — otomatik sarılan tooltip'e de uygulanır).
            body.ToolTip = node.Name;
        }

        var cell = new Grid { Width = GraphLayout.NodeCellWidth, Children = { body } };
        Canvas.SetLeft(cell, slot.Center.X - GraphLayout.NodeCellWidth / 2);
        Canvas.SetTop(cell, slot.Center.Y - GraphLayout.NodeSize / 2);

        string name = node.Name;
        body.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true; // zemine ulaşmasın (aksi halde hemen ardından seçim kalkardı)
            SelectedNode = string.Equals(SelectedNode, name, StringComparison.Ordinal) ? null : name;
        };

        var visual = new GraphNodeVisual
        {
            Model = node,
            Cell = cell,
            Body = body,
            SquareHost = squareHost,
            PulseHost = pulseHost,
            Square = square,
            SelectionRing = ring,
            Icon = icon,
            Label = label,
            Center = slot.Center,
        };
        ApplyNodeStatus(visual);
        return visual;
    }

    /// <summary>[G2] Dep-hata rozetini TALEP ÜZERİNE kurar (bir kez). Rozet nabız kabının KARDEŞİDİR ve ondan
    /// SONRA eklenir (üstte kalır) — DS'te de <c>ds-node-pulse</c> yalnız kare span'ındadır.</summary>
    private void EnsureBadge(GraphNodeVisual visual)
    {
        if (visual.Badge is not null) return;

        var badgeCircle = new Ellipse { StrokeThickness = 1 };
        badgeCircle.SetResourceReference(Shape.FillProperty, "Brush.SurfaceBase");
        badgeCircle.SetResourceReference(Shape.StrokeProperty, "Brush.StatusFailBorder");
        var badgeTriangle = new Path();
        IconPaint.Apply(badgeTriangle, this, WarningTriangleIconKey, "Brush.StatusFailText"); // DOLU üçgen — kip sözlükten
        var badge = new Grid
        {
            Width = 13,
            Height = 13,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            // Prototip: top -6, left calc(50% + 7px) → 26'lık karede sol 13+7=20.
            Margin = new Thickness(20, -6, 0, 0),
            IsHitTestVisible = false,
            Children =
            {
                badgeCircle,
                new Viewbox
                {
                    Width = 8, Height = 8, Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new Canvas { Width = IconViewBox, Height = IconViewBox, Children = { badgeTriangle } },
                },
            },
        };

        visual.SquareHost.Children.Add(badge);
        visual.Badge = badge;
        visual.BadgeCircle = badgeCircle;
        visual.BadgeTriangle = badgeTriangle;
    }

    /// <summary>DS <c>DependencyGraphNode</c> statü tablosunun birebir karşılığı: çerçeve + zemin + ikon rengi
    /// (+ discovered'ın kesikli çerçevesi), dep-hata rozetinin görünürlüğü ve building nabzı.</summary>
    private void ApplyNodeStatus(GraphNodeVisual visual)
    {
        NodeStatusApplyCount++;

        var (border, background, iconColor, dashed) = visual.Model.Status switch
        {
            GraphStatus.Queued => ("Brush.StatusQueued", "Brush.SurfaceRaised", "Brush.StatusQueuedText", false),
            GraphStatus.Building => ("Brush.Amber", "Brush.AmberSoft", "Brush.AmberText", false),
            GraphStatus.Succeeded => ("Brush.StatusSuccess", "Brush.StatusSuccessSoft", "Brush.StatusSuccessText", false),
            GraphStatus.Failed => ("Brush.StatusFail", "Brush.StatusFailSoft", "Brush.StatusFailText", false),
            GraphStatus.Skipped => ("Brush.StatusSkippedBorder", "Brush.StatusSkippedSoft", "Brush.StatusSkippedText", false),
            GraphStatus.Cycle => ("Brush.StatusCycle", "Brush.StatusCycleSoft", "Brush.StatusCycleText", false),
            _ => ("Brush.BorderStrong", "Brush.SurfaceRaised", "Brush.TextFaint", true),
        };

        visual.Square.SetResourceReference(Shape.StrokeProperty, border);
        visual.Square.SetResourceReference(Shape.FillProperty, background);
        IconPaint.Apply(visual.Icon, this, PackageIconKey, iconColor);
        // WPF Border dashed desteklemez → kesikli çerçeve Rectangle.StrokeDashArray ile (feasibility §3.5).
        // Dash birimi StrokeThickness çarpanı: 1.5px'lik çerçevede {2,2} = 3px dolu / 3px boş — CSS'in
        // `1.5px dashed` varsayılanının karşılığı (tasarımda ayrı bir sayısal değer verilmemiştir).
        visual.Square.StrokeDashArray = dashed ? DiscoveredDash : SolidDash;

        if (visual.Model.HasDepIssue)
        {
            EnsureBadge(visual);
            visual.Badge!.Visibility = Visibility.Visible;
        }
        else if (visual.Badge is { } badge)
        {
            badge.Visibility = Visibility.Collapsed;
        }

        ApplyBuildingPulse(visual);
    }

    /// <summary>
    /// [I-3] DS <c>ds-node-pulse</c> paritesi: building düğümün karesi 1.6s'de <c>1 → 0.5 → 1</c> nefes alır
    /// (<c>ease-in-out</c>, sonsuz). Reduced-motion'da HİÇ kurulmaz (DS'te de kural
    /// <c>@media (prefers-reduced-motion: no-preference)</c> içindedir) ve sinyal TAZE okunur.
    ///
    /// <para>Zaten dönen bir nabız YENİDEN BAŞLATILMAZ: <c>UpdateStatuses</c> koşarken saniyede birkaç kez çağrılır
    /// ve her çağrıda animasyonu baştan kurmak nabzı "takılı" gösterirdi (kameradaki Zeno korumasının eşi).</para>
    /// </summary>
    private void ApplyBuildingPulse(GraphNodeVisual visual)
    {
        bool shouldPulse = visual.Model.Status == GraphStatus.Building && AnimationsEnabledProvider();
        if (shouldPulse == visual.IsPulsing) return;
        visual.IsPulsing = shouldPulse;

        if (!shouldPulse)
        {
            StopPulse(visual);
            return;
        }

        var spline = MotionTokens.ResolveKeySpline(this, "KeySpline.EaseInOut", new KeySpline(0.65, 0, 0.35, 1));
        var half = TimeSpan.FromMilliseconds(PulseMs / 2);
        var full = TimeSpan.FromMilliseconds(PulseMs);
        var pulse = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        pulse.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        pulse.KeyFrames.Add(new SplineDoubleKeyFrame(PulseMinOpacity, KeyTime.FromTimeSpan(half), spline));
        pulse.KeyFrames.Add(new SplineDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(full), spline));
        Timeline.SetDesiredFrameRate(pulse, DecorativeFrameRate); // dekoratif sonsuz animasyon (feasibility §3.4)
        visual.PulseHost.BeginAnimation(OpacityProperty, pulse);
    }

    /// <summary>[M-d] Nabız animasyonunu (varsa) bırakıp opaklığı 1.0'a sabitler — hem building'den çıkan
    /// düğümler (<see cref="ApplyBuildingPulse"/>) hem <see cref="SetGraph"/>'ta atılan eski görseller için TEK
    /// durdurma yolu (kopya YASAK, CLAUDE.md).</summary>
    private static void StopPulse(GraphNodeVisual visual)
    {
        visual.PulseHost.BeginAnimation(OpacityProperty, null);
        visual.PulseHost.Opacity = 1.0;
    }

    // ---------------------------------------------------------------- seçim (halka + sönme)

    private void ApplySelection()
    {
        // [G2] Komşuluk kümesi TÜM kenarlardan kurulur (materyalize olanlardan DEĞİL) — aksi halde cull edilmiş
        // bir kenarın ucundaki komşu yanlışlıkla sönerdi.
        _neighbourSet = new HashSet<string>(StringComparer.Ordinal);
        if (_selectedNode is { } selected)
        {
            _neighbourSet.Add(selected);
            if (_neighbours.TryGetValue(selected, out var list))
                foreach (string name in list)
                    _neighbourSet.Add(name);
        }

        bool animate = AnimationsEnabledProvider();
        foreach (var visual in _nodes.Values)
            ApplyNodeSelection(visual, animate);
    }

    private void ApplyNodeSelection(GraphNodeVisual visual, bool animate)
    {
        string name = visual.Model.Name;
        bool isSelected = string.Equals(name, _selectedNode, StringComparison.Ordinal);
        visual.SelectionRing.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        // [M-1] DS DependencyGraphNode: `border: ${selected ? 2 : 1.5}px …` — seçim kareyi de kalınlaştırır.
        visual.Square.StrokeThickness = isSelected ? SelectedNodeBorderThickness : NodeBorderThickness;
        // DS DependencyGraphNode: etiket seçiliyken text-primary, aksi halde text-dim.
        visual.Label?.SetResourceReference(TextBlock.ForegroundProperty,
            isSelected ? "Brush.TextPrimary" : "Brush.TextDim");
        double target = _selectedNode is null || _neighbourSet.Contains(name) ? 1.0 : DimmedNodeOpacity;
        SetBodyOpacity(visual.Body, target, animate);
    }

    private void SetBodyOpacity(UIElement body, double target, bool animate)
    {
        if (!animate)
        {
            body.BeginAnimation(OpacityProperty, null);
            body.Opacity = target;
            return;
        }

        var duration = MotionTokens.ResolveDuration(this, "Duration.Base", 180.0);
        if (duration.TimeSpan <= TimeSpan.Zero)
        {
            body.BeginAnimation(OpacityProperty, null);
            body.Opacity = target;
            return;
        }

        var spline = MotionTokens.ResolveKeySpline(this, "KeySpline.EaseStandard", new KeySpline(0.4, 0, 0.2, 1));
        body.BeginAnimation(OpacityProperty,
            MotionTokens.SplineTo(target, duration.TimeSpan, spline), HandoffBehavior.SnapshotAndReplace);
    }

    // ---------------------------------------------------------------- kenar stilleri + akan dash

    private void ApplyEdgeStyles()
    {
        bool animationsEnabled = AnimationsEnabledProvider();
        // Motion sinyali değiştiyse (reduced-motion açıldı/kapandı) clock kablajı MUTLAKA yenilenir.
        bool motionUnchanged = _edgesAnimated == animationsEnabled;
        _edgesAnimated = animationsEnabled;
        _flowingEdges.Clear();

        foreach (var edge in _edges)
            ApplyEdgeStyle(edge, animationsEnabled, force: !motionUnchanged);

        // [M-3] Akan kenar kalmadıysa (veya motion kapandıysa) clock BIRAKILIR — aksi halde timing engine boşta
        // da 30fps uyanık kalırdı. Bir sonraki akan kenarda yeniden kurulur (aşağıdaki hızlı yol notuna bak).
        if (_flowingEdges.Count == 0 || !animationsEnabled)
            ReleaseDashClock();
    }

    /// <summary>Tek bir kenarın stilini uygular. <see cref="ApplyEdgeStyles"/> (tam geçiş) ve
    /// <see cref="MaterializeEdge"/> (yeni görünür olan kenar) AYNI yolu kullanır — kopya YASAK.</summary>
    private void ApplyEdgeStyle(GraphEdgeVisual edge, bool animationsEnabled, bool force)
    {
        _slots.TryGetValue(edge.Model.From, out var source);
        _slots.TryGetValue(edge.Model.To, out var target);

        bool touchesSelection = _selectedNode is { } sel &&
            (string.Equals(edge.Model.From, sel, StringComparison.Ordinal) ||
             string.Equals(edge.Model.To, sel, StringComparison.Ordinal));

        var style = EdgeStyleResolver.Resolve(
            source?.Model.Status ?? GraphStatus.Discovered,
            source?.Model.HasDepIssue ?? false,
            target?.Model.Status ?? GraphStatus.Discovered,
            touchesSelection,
            _selectedNode is not null);

        // "Her tick'te full binding refresh yapma" (feasibility §3.4): stil DEĞİŞMEDİYSE fırça/dash/clock
        // kablajına hiç dokunulmaz. EdgeStyle bir record'dur ve Dash alanı daima aynı statik örnektir
        // (FlowDash/ErrorDash/null) — değer eşitliği burada güvenlidir.
        if (!force && edge.Style == style)
        {
            if (style.IsFlowing) _flowingEdges.Add(edge.Path);
            return;
        }

        edge.Style = style;
        edge.Path.SetResourceReference(Shape.StrokeProperty, style.BrushKey);
        edge.Path.StrokeThickness = style.Thickness;
        edge.Path.Opacity = style.Opacity;
        edge.Path.StrokeDashArray = DashCollectionFor(style.Dash);

        if (style.IsFlowing)
            _flowingEdges.Add(edge.Path);

        if (style.IsFlowing && animationsEnabled)
        {
            // TEK paylaşımlı clock: bütün akan kenarlar (1px ve 1.6px olanlar dahil) aynı fazda kayar.
            edge.Path.ApplyAnimationClock(Shape.StrokeDashOffsetProperty, DashClockFor(style.Thickness));
        }
        else
        {
            edge.Path.ApplyAnimationClock(Shape.StrokeDashOffsetProperty, null);
            edge.Path.StrokeDashOffset = 0; // reduced-motion: kesikli AMA statik
        }
    }

    /// <summary>[G2] Stil deseninin DONMUŞ, paylaşılan koleksiyonu (bilinmeyen bir desen gelirse — bugün
    /// gelmiyor — güvenli tarafta yeni bir donmuş kopya üretilir).</summary>
    private static DoubleCollection DashCollectionFor(IReadOnlyList<double>? dash) =>
        dash is null ? SolidDash
        : EdgeDashes.TryGetValue(dash, out var collection) ? collection
        : FrozenDash([.. dash]);

    /// <summary>
    /// Akan dash'in TEK paylaşımlı clock'u (A13.2). Kök <see cref="ClockGroup"/> timing engine'de TEK bir clock'tur;
    /// iki çocuğu (1px ve 1.6px dalı) aynı kökten türediği için faz farkı MATEMATİKSEL OLARAK imkânsızdır.
    ///
    /// <para><b>Neden iki çocuk:</b> A13.2 deseni 1.6px'te BÖLMEYİ de şart koşar; bölünmüş desenin periyodu da
    /// bölündüğünden (11 → 6.875 çarpan-birimi) "tam 2 periyot" offset'i kalınlığa göre farklıdır
    /// (−22 / −13.75). İki farklı hedef değeri tek bir <see cref="AnimationClock"/> üretemez — ama tek bir KÖK
    /// clock'un iki çocuğu üretir. İki dal da 0.9s'de 22px MUTLAK yol alır ⇒ dikişsiz ve faz-kilitli.</para>
    /// </summary>
    private AnimationClock DashClockFor(double thickness)
    {
        if (_dashClockRoot is null)
        {
            var root = new ParallelTimeline();
            root.Children.Add(BuildDashAnimation(EdgeStyleResolver.DefaultThickness));
            root.Children.Add(BuildDashAnimation(EdgeStyleResolver.SelectedThickness));
            // DesiredFrameRate yalnız KÖK timeline'da dikkate alınır (WPF) — dolayısıyla köke konur.
            Timeline.SetDesiredFrameRate(root, DecorativeFrameRate);
            root.Freeze();

            _dashClockRoot = (ClockGroup)root.CreateClock();
            _thinDashClock = (AnimationClock)_dashClockRoot.Children[0];
            _thickDashClock = (AnimationClock)_dashClockRoot.Children[1];
        }

        return thickness == EdgeStyleResolver.SelectedThickness ? _thickDashClock! : _thinDashClock!;
    }

    private static DoubleAnimation BuildDashAnimation(double thickness) => new()
    {
        From = 0.0, // paylaşılan clock birden çok Path'e uygulanır → başlangıç Path'in taban değerine BIRAKILMAZ
        To = EdgeStyleResolver.FlowDashOffsetFor(thickness),
        Duration = new Duration(TimeSpan.FromMilliseconds(EdgeStyleResolver.FlowDurationMs)),
        RepeatBehavior = RepeatBehavior.Forever,
    };

    /// <summary>[M-3] Kök clock'u durdurur ve bırakır. Yeniden kurulum güvenlidir: clock ancak akan kenar
    /// KALMADIĞINDA bırakılır; yeni bir kenar akmaya başladığında stili değişmiş olacağından hızlı yola
    /// (<c>edge.Style == style</c>) girmez ve kablaj yeniden kurulur.</summary>
    private void ReleaseDashClock()
    {
        if (_dashClockRoot is null) return;
        _dashClockRoot.Controller?.Stop();
        _dashClockRoot = null;
        _thinDashClock = null;
        _thickDashClock = null;
    }

    // ---------------------------------------------------------------- ilk açılış stagger'ı

    /// <summary>Katman başına gecikme — 55ms, 330ms'de tavanlanır (Ek A #9).</summary>
    internal static double RevealDelayMs(int layer) => Math.Min(layer * LayerStaggerMs, LayerStaggerCapMs);

    private void PlayRevealStagger()
    {
        // Önceki reveal hero'su + varsa bekleyen release timer'ı bırak — yeni bir SetGraph yeni bir reveal başlatır.
        ReleaseRevealHero();
        int gen = ++_revealGen; // bu reveal kuşağı — release yalnız bu kuşak hâlâ geçerliyken uygulanır

        bool animate = AnimationsEnabledProvider();
        // [E3/T41/DD9] Reveal bir HERO'dur. Başka bir hero sürerken (Hero null döner) dekoratif stagger ATLANIR —
        // düğümler ani yerleştirilir (reduced-motion yolu). Aksi halde hero TUTULUR ve reveal tamamlanınca (aşağıda
        // generation-guarded timer) bırakılır. Coordinator yoksa (headless, enjekte edilmemiş) davranış eskisi gibi.
        if (animate)
        {
            _revealHero = ActiveHeroCoordinator?.Hero(RevealHeroKey);
            if (ActiveHeroCoordinator is not null && _revealHero is null)
                animate = false; // başka bir hero aktif → ani sonuç
        }

        // [G2] Cull edilmiş düğümün reveal'i ATLANIR; gecikme KATMAN indeksinden türetildiği (koşan bir sayaçtan
        // değil) için kalan düğümlerin zamanlaması KAYMAZ. Pencerenin uzunluğu ise TÜM katmanlardan hesaplanır
        // (yalnız materyalize olanlardan değil) — sonradan görünür olan bir düğüm de pencereye girebilmeli.
        double maxDelay = -1;
        foreach (var slot in _slotOrder)
        {
            double d = RevealDelayMs(slot.Model.Layer);
            if (d > maxDelay) maxDelay = d;
        }

        _revealPlaying = animate && maxDelay >= 0;
        _revealStartTicks = Environment.TickCount64;
        _revealEndTicks = _revealStartTicks + (long)(maxDelay + RevealMs);

        foreach (var visual in _nodes.Values)
        {
            visual.Cell.BeginAnimation(OpacityProperty, null);
            if (!animate)
            {
                visual.Cell.Opacity = 1.0;
                visual.Cell.RenderTransform = Transform.Identity;
                continue;
            }

            ApplyRevealTo(visual, RevealDelayMs(visual.Model.Layer));
        }

        // [E3/T41 — release fix] Hero, reveal PENCERESİ boyunca tutulur ve en geç biten düğümün reveal'i
        // (maxDelay + RevealMs) tamamlanınca bırakılır. Tetik bir DispatcherTimer'dır: bir fade'in clock'u
        // BeginAnimation ile üretildikten SONRA eklenen Completed handler'ı HİÇ ateşlenmez (gerçek-HWND WPF
        // harness ile doğrulandı) — dolayısıyla eski Completed yolu ÖLÜ koddu. Timer, generation-guarded bir
        // release'e (ReleaseRevealHeroIfCurrent) bağlanır: reveal #1 sürerken hızlı bir ikinci SetGraph gelirse
        // #1'in timer'ı ateşlense bile #2'nin taze hero'suna DOKUNMAZ (gen != _revealGen). Headless testte timer
        // tick etmez; test release'i doğrudan bu kuşak damgasıyla çağırır (bkz. ReleaseRevealHeroIfCurrent).
        if (_revealHero is not null)
        {
            if (maxDelay < 0)
            {
                ReleaseRevealHero(); // animate ama düğüm yok (savunmacı) — hero'yu bekletme
            }
            else
            {
                var releaseAfter = TimeSpan.FromMilliseconds(maxDelay + RevealMs);
                _revealReleaseTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = releaseAfter };
                _revealReleaseTimer.Tick += (_, _) => ReleaseRevealHeroIfCurrent(gen);
                _revealReleaseTimer.Start();
            }
        }
    }

    /// <summary>[G2 · fix round 1 B2] Tek bir düğümün beliriş animasyonu (opaklık 0→1 + 5px yukarıdan).
    /// <see cref="PlayRevealStagger"/> (açılış) ve <see cref="JoinRevealIfPlaying"/> (pencere içinde sonradan
    /// materyalize olan düğüm) AYNI yolu kullanır — kopya YASAK.</summary>
    private void ApplyRevealTo(GraphNodeVisual visual, double delayMs)
    {
        visual.Cell.BeginAnimation(OpacityProperty, null);
        // CSS `both` fill paritesi: gecikme boyunca opaklık 0 TUTULUR — flash yok (feasibility §3.4).
        visual.Cell.Opacity = 0.0;
        var rise = new TranslateTransform(0, -RevealRisePx);
        visual.Cell.RenderTransform = rise;

        var begin = TimeSpan.FromMilliseconds(delayMs);
        var duration = TimeSpan.FromMilliseconds(RevealMs);
        var spline = MotionTokens.ResolveKeySpline(this, "KeySpline.EaseOut", new KeySpline(0.22, 1, 0.36, 1));

        var fade = MotionTokens.SplineTo(1.0, duration, spline);
        fade.BeginTime = begin;
        fade.KeyFrames.Insert(0, new DiscreteDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        visual.Cell.BeginAnimation(OpacityProperty, fade);

        var slide = MotionTokens.SplineTo(0.0, duration, spline);
        slide.BeginTime = begin;
        slide.KeyFrames.Insert(0, new DiscreteDoubleKeyFrame(-RevealRisePx, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        rise.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    /// <summary>[E3/T41] Reveal tamamlandığında hero'yu bırakan generation-guarded karar: YALNIZ tetikleyen reveal
    /// hâlâ geçerliyse (<paramref name="gen"/> == <see cref="RevealGeneration"/>) bırakır. Superseded bir reveal'in
    /// gecikmiş release'i (rapid ikinci SetGraph) mevcut reveal'in taze hero'sunu düşürmez. Test bunu doğrudan
    /// çağırır (gerçek timer tick'i beklemeden).</summary>
    internal void ReleaseRevealHeroIfCurrent(int gen)
    {
        if (gen != _revealGen) return; // stale kuşak — mevcut hero'ya dokunma
        ReleaseRevealHero();
    }

    /// <summary>[E3/T41] Reveal hero'sunu (varsa) ve bekleyen release timer'ını bırakır — yeni bir hero girebilir.
    /// Çift-bırakma güvenli (HeroScope.Dispose idempotent).</summary>
    private void ReleaseRevealHero()
    {
        if (_revealReleaseTimer is not null)
        {
            _revealReleaseTimer.Stop();
            _revealReleaseTimer = null;
        }
        _revealHero?.Dispose();
        _revealHero = null;
    }

    // ---------------------------------------------------------------- kamera

    private void ApplyCamera(bool animate)
    {
        if (_slotOrder.Count == 0) return;

        var viewport = ViewportSize;
        if (viewport.Width <= 0 || viewport.Height <= 0) return;

        // [G2] Odak TÜM modellerden hesaplanır — cull edilmiş bir building düğümü de frontier'e katılır, aksi
        // halde kamera görünmeyen bir cepheye hiç yönelmezdi (kendi kendini kilitleyen bir cull).
        Point? selected = _selectedNode is { } name && _slots.TryGetValue(name, out var sel) ? sel.Center : null;
        var building = new List<Point>();
        foreach (var slot in _slotOrder)
            if (slot.Model.Status == GraphStatus.Building) building.Add(slot.Center);

        var focus = GraphCamera.ResolveFocus(selected, building, _isSettled, GraphSize, _previousFocus);
        // [M-5] <8px eşiği YALNIZ frontier dalında geçerlidir (GraphCamera.ResolveFocus) — bu yüzden odak yalnız
        // O DALDAN geldiyse hatırlanır. Aksi halde seçimden yeni çıkılmış bir odak (ya da settled merkezi) ilk
        // frontier hedefini eşiğin altında kalarak BASTIRABİLİRDİ.
        bool focusCameFromFrontier = selected is null && building.Count > 0;
        _previousFocus = focusCameFromFrontier ? focus : null;

        var camera = GraphCamera.Compute(viewport, GraphSize, focus);
        // Hedef DEĞİŞMEDİYSE hiçbir animasyon yeniden başlatılmaz: koşarken UpdateStatuses saniyede birkaç kez
        // çağrılır ve aynı hedefe her seferinde yeni bir 460ms geçişi başlatmak uçuştaki geçişi sürekli
        // "yeniden doğurur" (Zeno etkisi — kamera hedefe hiç oturmaz).
        if (_hasCamera && camera == CurrentCamera)
        {
            // Hedef aynı ⇒ kamera hareket etmiyor (ara kare yok); yalnız viewport büyümüş olabilir (SizeChanged).
            UpdateMaterialization();
            return;
        }
        CurrentCamera = camera;
        _hasCamera = true;

        bool animationsEnabled = animate && AnimationsEnabledProvider();
        LastCameraAnimated = animationsEnabled;
        // Kamera animasyonla gidecekse ARA KARELER de görünür ⇒ mevcut görünüm + hedef taranır (bkz. B1).
        UpdateMaterialization(traversing: animationsEnabled);

        if (!animationsEnabled)
        {
            _cameraScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _cameraScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _cameraTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            _cameraTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            _cameraScale.ScaleX = camera.Scale;
            _cameraScale.ScaleY = camera.Scale;
            _cameraTranslate.X = camera.Tx;
            _cameraTranslate.Y = camera.Ty;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(GraphCamera.TransitionMs);
        var spline = MotionTokens.ResolveKeySpline(this, "KeySpline.EaseInOut", new KeySpline(0.65, 0, 0.35, 1));
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

    private void ShowEmptyState(bool visible)
    {
        EmptyState.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        Viewport.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------------------------------------------------------------- test/görünürlük yüzeyi

    /// <summary>MATERYALİZE olmuş düğüm görselleri (cull kapalıyken = tüm düğümler).</summary>
    internal IReadOnlyDictionary<string, GraphNodeVisual> NodeVisuals => _nodes;
    /// <summary>MATERYALİZE olmuş kenar görselleri (cull kapalıyken = tüm kenarlar).</summary>
    internal IReadOnlyList<GraphEdgeVisual> EdgeVisuals => _edges;
    /// <summary>[G2] Grafın TOPLAM düğüm sayısı (cull'dan bağımsız).</summary>
    internal int NodeCount => _slotOrder.Count;
    /// <summary>[G2] Grafın TOPLAM kenar sayısı (cull'dan bağımsız).</summary>
    internal int EdgeCount => _edgeSlots.Count;
    /// <summary>[G2] Cull bu graf için etkin mi (düğüm sayısı <see cref="ShapesPathMaxNodes"/>'u aştı mı).</summary>
    internal bool IsCullEnabled => _cullEnabled;
    /// <summary>[G2] <c>ApplyNodeStatus</c> kaç kez koştu — "değişmediyse dokunma" fast-path'inin DETERMİNİSTİK
    /// kanıtı (duvar saati değil, sayaç).</summary>
    internal int NodeStatusApplyCount { get; private set; }
    /// <summary>[E3/T41] Aktif reveal kuşağının damgası — test, doğru kuşakla release'i tetiklemek için okur.</summary>
    internal int RevealGeneration => _revealGen;
    /// <summary>[E3/T41] Reveal tamamlandığında hero'yu bırakacak CANLI bir release zamanlandı mı — ölü Completed
    /// yolunun aksine gerçek bir tetik kuruldu mu (test ayırt edici olarak okur).</summary>
    internal bool HasPendingRevealRelease => _revealReleaseTimer is { IsEnabled: true };
    /// <summary>O an akan (hedefi building) kenarların Path'leri — paylaşılan clock TAM BU kümeye uygulanır.</summary>
    internal IReadOnlyList<Path> FlowingEdgePaths => _flowingEdges;
    /// <summary>Akan dash'in TEK kök clock'u (null = hiç akan kenar yok / motion kapalı).</summary>
    internal ClockGroup? SharedDashClock => _dashClockRoot;
    /// <summary>Kökün 1px / 1.6px dalları — ikisi de AYNI köke bağlıdır (faz kilidi).</summary>
    internal AnimationClock? ThinDashClock => _thinDashClock;
    internal AnimationClock? ThickDashClock => _thickDashClock;
    internal CameraTransform CurrentCamera { get; private set; }
    internal bool LastCameraAnimated { get; private set; }
    /// <summary>[M-5] Yalnız FRONTIER dalından gelen odak hatırlanır (8px eşiği yalnız orada geçerli).</summary>
    internal Point? PreviousFocus => _previousFocus;
    internal string HeaderCountsText => CountsText.Text;
    internal FontFamily HeaderCountsFontFamily => CountsText.FontFamily;
    internal bool IsEmptyStateVisible => EmptyState.Visibility == Visibility.Visible;
    internal string EmptyStateText => EmptyStateLabel.Text;
    internal Size ViewportSize => new(Ground.ActualWidth, Ground.ActualHeight);
    internal Size GraphSize => _layout.Size;
    internal Point NodeCenter(string name) => _slots[name].Center;
}
