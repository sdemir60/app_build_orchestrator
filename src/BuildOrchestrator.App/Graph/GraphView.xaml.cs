using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
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
    /// <summary>Katman başına açılış gecikmesi (design-v1 §2.3: "katman başına 55ms").</summary>
    public const double LayerStaggerMs = 55.0;
    /// <summary>Stagger tavanı (Ek A #9: grafta 55ms/katman, tavan 330ms).</summary>
    public const double LayerStaggerCapMs = 330.0;
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
    public const int DecorativeFrameRate = 30;
    /// <summary>Düğüm karesinin çerçeve kalınlığı (§2.3: "1.5px border").</summary>
    public const double NodeBorderThickness = 1.5;
    /// <summary>Seçili düğüm karesinin çerçeve kalınlığı (DS: 2px).</summary>
    public const double SelectedNodeBorderThickness = 2.0;
    /// <summary>Düğüm karesinin köşe yarıçapı — DS <c>--radius-sm</c> (§2.3: "radius-sm").</summary>
    public const double NodeCornerRadius = 4.0;
    /// <summary>Seçim halkasının kareden dışarı taşma payı: 2px offset + yarım kalem.</summary>
    public const double SelectionRingInset = 3.0;
    /// <summary>Building düğümün nabzı — DS <c>ds-node-pulse 1.6s var(--ease-in-out) infinite</c>.</summary>
    public const double PulseMs = 1600.0;
    /// <summary>Nabzın orta noktadaki opaklığı (DS <c>@keyframes: 50% { opacity: .5 }</c>).</summary>
    public const double PulseMinOpacity = 0.5;
    /// <summary>Glyph, düğüm kenarının bu kadarıdır (§2.3: "node'un %52'si").</summary>
    public const double IconFactor = 0.52;
    /// <summary>Glyph kalem kalınlığı (§2.3: "1.8px stroke").</summary>
    public const double IconStroke = 1.8;
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
    /// <summary>Komşuluk (odak kümesi + Task 8'in seçim kenarları) — kenarların TAMAMINDAN kurulur.</summary>
    private readonly Dictionary<string, List<string>> _neighbours = new(StringComparer.Ordinal);

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

    private QuietLayoutResult _layout = QuietGraphLayout.Compute([], new Size(0, 0));
    private string? _selectedNode;
    private HashSet<string> _focusSet = new(StringComparer.Ordinal);
    private GraphRunPhase _runPhase = GraphRunPhase.Idle;
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
    }

    private void OnAnimationsEnabledChanged(object? sender, EventArgs e) => ReapplyMotion();

    /// <summary>Motion sinyali canlı değiştiğinde sürmekte olan sonsuz animasyonları yeni sinyale göre
    /// yeniden kurar.</summary>
    internal void ReapplyMotion()
    {
        foreach (var slot in _slotOrder)
            ApplyBuildingPulse(slot.Visual);
    }

    /// <summary>Seçili düğüm (null = seçim yok). Değişince: halka + sönme + kamera güncellenir.</summary>
    public string? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (string.Equals(_selectedNode, value, StringComparison.Ordinal)) return;
            _selectedNode = value;
            ApplySelection();
            ApplyCamera(animate: true);
            SelectionChanged?.Invoke(this, value);
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
        ApplyStatuses(nodes);
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
        foreach (var stale in _slotOrder)
            StopPulse(stale.Visual);
        _slots.Clear();
        _slotOrder.Clear();
        _neighbours.Clear();
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
            AddNeighbour(edge.From, edge.To);
            AddNeighbour(edge.To, edge.From);
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
            // §2.3'ün hold-fade'i building'den ÇIKIŞ anına bağlıdır — bu yüzden geçişin KENDİSİ burada,
            // model değişmeden önce okunur.
            bool leftBuilding = slot.Model.Status == GraphStatus.Building && node.Status != GraphStatus.Building;
            slot.Model = node;
            slot.Visual.Model = node;
            ApplyNodeStatus(slot.Visual);
            ApplyNodeOpacity(slot.Visual, leftBuilding);
        }
    }

    private void AddNeighbour(string from, string to)
    {
        if (!_neighbours.TryGetValue(from, out var list))
            _neighbours[from] = list = [];
        list.Add(to);
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
        double size = _layout.NodeSize;
        var world = ToWorld(slot.Center);
        Canvas.SetLeft(slot.Visual.Cell, world.X - size / 2);
        Canvas.SetTop(slot.Visual.Cell, world.Y - size / 2);
    }

    /// <summary>Düğüm ÖLÇÜSÜ graf genelinde tektir (pitch'ten türer) — bu yüzden tek turda hepsine yazılır ve
    /// ikon ölçeği tek paylaşımlı transform üzerinden güncellenir.</summary>
    private void ApplySizes()
    {
        double size = _layout.NodeSize;
        double ring = size + SelectionRingInset * 2;
        _iconScale.ScaleX = _iconScale.ScaleY = size * IconFactor / IconViewBox;

        // Dünya tuvali PANELİN kendisidir: ölçek 1'de graf tam oturur, öteleme 0'dır.
        World.Width = Math.Max(0, ViewportSize.Width);
        World.Height = Math.Max(0, ViewportSize.Height);

        foreach (var slot in _slotOrder)
        {
            var visual = slot.Visual;
            visual.Cell.Width = visual.Cell.Height = size;
            visual.Body.Width = visual.Body.Height = size;
            visual.PulseHost.Width = visual.PulseHost.Height = size;
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

        var pulseHost = new Grid { Children = { selectionRing, square, iconBox } };

        var body = new GraphNodeBody
        {
            Background = Brushes.Transparent, // tıklama alanı
            Cursor = Cursors.Hand,
            Children = { pulseHost },
            // [quiet] §2.3: düğümün üstünde ad yoktur — TAM proje adı tooltip'ten gelir. Düz metin atanır:
            // WPF ToolTip kontrolünü ancak gösterirken kurar, dolayısıyla düğüm başına HİÇBİR ek nesne
            // kurulmaz (WillBuildDot.cs ile aynı desen).
            ToolTip = node.Name,
        };

        var cell = new Grid { Children = { body } };

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

        var visual = new GraphNodeVisual
        {
            Model = node,
            Cell = cell,
            Body = body,
            PulseHost = pulseHost,
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

        // [quiet · ÖLÇÜLMÜŞ SAPMA] §2.3 "Zemin/kenar/glyph renk geçişleri 380ms ease-standard" der; burada
        // renkler ANINDA uygulanır ve bu bilinçlidir.
        //
        // WPF'te bir fırça DP'si interpolate EDİLEMEZ: geçiş, düğüm başına yerel bir SolidColorBrush kurup
        // onun Color'ını animasyonlamayı gerektirir. Uygulandı ve ölçüldü — 177 projenin statüsünün tek
        // tick'te değiştiği durumda (koşu başlangıcı: hepsi Discovered → Queued) üç yüzey × 177 düğüm = 531
        // fırça + 531 ColorAnimation, tick'i 11 ms'den 51 ms'ye çıkarıyor ve UI olay bütçesini (50 ms,
        // UiResponsivenessBudgetTests.EventBudgetMs) AŞIYOR. Ayrıca bir kez yerel fırçaya devreden yüzey
        // token referansını da kaybeder.
        //
        // 8–24px'lik bir karede, opaklığı zaten animasyonlu değişen bir yüzeyin renk geçişi için bütçenin
        // %80'ini harcamak savunulabilir değil. Bütçeyi gevşetmek YASAK (CLAUDE.md), o yüzden geçiş
        // uygulanmadı. Gözle doğrulama listesinde açık madde olarak duruyor.
        visual.Square.SetResourceReference(Shape.StrokeProperty, border);
        visual.Square.SetResourceReference(Shape.FillProperty, background);
        IconPaint.Apply(visual.Icon, this, PackageIconKey, iconColor);

        // WPF Border dashed desteklemez → kesikli çerçeve Rectangle.StrokeDashArray ile. Dash birimi
        // StrokeThickness çarpanıdır: 1.5px'lik çerçevede {2,2} = 3px dolu / 3px boş.
        visual.Square.StrokeDashArray = dashed ? DiscoveredDash : SolidDash;

        ApplyBuildingPulse(visual);
    }

    /// <summary>
    /// [I-3] DS <c>ds-node-pulse</c> paritesi: building düğümün karesi 1.6s'de <c>1 → 0.5 → 1</c> nefes alır
    /// (<c>ease-in-out</c>, sonsuz). Reduced-motion'da HİÇ kurulmaz ve sinyal TAZE okunur.
    ///
    /// <para>Zaten dönen bir nabız YENİDEN BAŞLATILMAZ: <c>UpdateStatuses</c> koşarken saniyede birkaç kez
    /// çağrılır ve her çağrıda animasyonu baştan kurmak nabzı "takılı" gösterirdi.</para>
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
    /// düğümler hem <see cref="ApplyGraph"/>'ta atılan eski görseller için TEK durdurma yolu.</summary>
    private static void StopPulse(GraphNodeVisual visual)
    {
        visual.PulseHost.BeginAnimation(OpacityProperty, null);
        visual.PulseHost.Opacity = 1.0;
    }

    // ---------------------------------------------------------------- seçim (halka + sönme)

    /// <summary>Odak kümesi = seçili düğüm + DOĞRUDAN bağımlılıkları + DOĞRUDAN bağımlıları (§2.3).</summary>
    private void ApplySelection()
    {
        _focusSet = new HashSet<string>(StringComparer.Ordinal);
        if (_selectedNode is { } selected)
        {
            _focusSet.Add(selected);
            if (_neighbours.TryGetValue(selected, out var list))
                foreach (string name in list)
                    _focusSet.Add(name);
        }

        foreach (var slot in _slotOrder)
        {
            string name = slot.Model.Name;
            bool isSelected = string.Equals(name, _selectedNode, StringComparison.Ordinal);
            slot.Visual.SelectionRing.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
            // DS DependencyGraphNode: `border: ${selected ? 2 : 1.5}px …` — seçim kareyi de kalınlaştırır.
            slot.Visual.Square.StrokeThickness = isSelected ? SelectedNodeBorderThickness : NodeBorderThickness;
            ApplyNodeOpacity(slot.Visual, leftBuilding: false);
        }
    }

    // ---------------------------------------------------------------- koşu yaşam döngüsü (opaklık)

    private void ApplyAllOpacities()
    {
        foreach (var slot in _slotOrder)
            ApplyNodeOpacity(slot.Visual, leftBuilding: false);
    }

    /// <summary>
    /// [quiet] §2.3'ün opaklık sistemi. Değer kararı SAF (<see cref="GraphNodeOpacity.Resolve"/>); burada
    /// yalnız ZAMANLAMA yaşar.
    ///
    /// <para><b>Hold-fade YALNIZ building'den çıkış anında doğar</b> (<paramref name="leftBuilding"/>): CSS'in
    /// gecikmeli transition'ı da yalnız değer 1'den 0.2'ye DEĞİŞTİĞİNDE koşar. Sonraki tick'ler değeri zaten
    /// 0.2 bulur ve <see cref="GraphNodeVisual.OpacityTarget"/> kapısı hiçbir animasyon başlatmaz — aksi halde
    /// saniyede birkaç kez yeniden doğan 3.1 saniyelik bir animasyon sönmeyi hiç tamamlatmazdı.</para>
    /// </summary>
    private void ApplyNodeOpacity(GraphNodeVisual visual, bool leftBuilding)
    {
        double target = GraphNodeOpacity.Resolve(
            visual.Model.Status,
            _runPhase,
            _selectedNode is not null,
            _focusSet.Contains(visual.Model.Name),
            string.Equals(_hoveredNode, visual.Model.Name, StringComparison.Ordinal));

        if (target.Equals(visual.OpacityTarget)) return;
        visual.OpacityTarget = target;

        if (!AnimationsEnabledProvider())
        {
            SnapOpacity(visual, target);
            return;
        }

        var spline = MotionTokens.ResolveKeySpline(this, "KeySpline.EaseStandard", new KeySpline(0.4, 0, 0.2, 1));
        // Bekleme bir BeginTime'dır, bir timer DEĞİL: CSS'teki `transition ... 700ms 2400ms` hilesinin
        // birebir karşılığı. Bekleme boyunca değer TAM OPAK tutulur (CSS `both` fill paritesi).
        double durationMs = leftBuilding ? GraphNodeOpacity.FadeMs : GraphNodeOpacity.GlideMs;
        var animation = MotionTokens.SplineTo(target, TimeSpan.FromMilliseconds(durationMs), spline);
        if (leftBuilding)
        {
            animation.BeginTime = TimeSpan.FromMilliseconds(GraphNodeOpacity.HoldMs);
            animation.KeyFrames.Insert(0,
                new DiscreteDoubleKeyFrame(GraphNodeOpacity.Full, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        }

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

    /// <summary>Katman başına gecikme — 55ms, 330ms'de tavanlanır (Ek A #9).</summary>
    internal static double RevealDelayMs(int layer) => Math.Min(layer * LayerStaggerMs, LayerStaggerCapMs);

    private void PlayRevealStagger()
    {
        // [E3/T41/DD9 · W2 fold] Reveal bir HERO'dur. Önceki hero + bekleyen release bırakılır, yeni kuşak
        // damgalanır ve hero alınmaya çalışılır. Başka bir hero sürerken dekoratif dalga ATLANIR.
        var (animate, gen) = _reveal.Begin(AnimationsEnabledProvider(), ActiveHeroCoordinator, RevealHeroKey);

        double maxDelay = -1;
        foreach (var slot in _slotOrder)
        {
            double delay = RevealDelayMs(slot.Model.Layer);
            if (delay > maxDelay) maxDelay = delay;
        }

        foreach (var slot in _slotOrder)
        {
            var visual = slot.Visual;
            visual.Cell.BeginAnimation(OpacityProperty, null);
            if (!animate)
            {
                visual.Cell.Opacity = 1.0;
                visual.Cell.RenderTransform = Transform.Identity;
                continue;
            }

            ApplyRevealTo(visual, RevealDelayMs(slot.Model.Layer));
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
        if (_selectedNode is not { } selected || !_slots.ContainsKey(selected)) return GraphCamera.Default;

        double x0 = double.PositiveInfinity, x1 = double.NegativeInfinity;
        double y0 = double.PositiveInfinity, y1 = double.NegativeInfinity;
        foreach (string name in _focusSet)
        {
            if (!_slots.TryGetValue(name, out var slot)) continue;
            x0 = Math.Min(x0, slot.Center.X); x1 = Math.Max(x1, slot.Center.X);
            y0 = Math.Min(y0, slot.Center.Y); y1 = Math.Max(y1, slot.Center.Y);
        }
        if (double.IsInfinity(x0)) return GraphCamera.Default;

        return GraphCamera.FocusAndFit(
            panel,
            new Rect(x0, y0, x1 - x0, y1 - y0),
            _layout.NodeSize,
            new Vector(QuietGraphLayout.ContentInset, QuietGraphLayout.ContentInset));
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
    /// <summary>Bir düğümün opaklığını süren animasyon (anında uygulandıysa <c>null</c>) — hold-fade'in
    /// zamanlamasını pinleyen testlerin okuduğu yüzey.</summary>
    internal Timeline? OpacityAnimationOf(string nodeName) =>
        _slots.TryGetValue(nodeName, out var slot) ? slot.Visual.OpacityAnimation : null;
}
