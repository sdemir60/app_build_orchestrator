using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.App.Graph;

/// <summary>
/// [T63] design-v1 §2.3 dependency graph — <b>Shapes yolu</b>: her düğüm ve kenar birer UIElement
/// (<see cref="Rectangle"/>/<see cref="Path"/>), hit-test ve tooltip native. Tasarımın 36 düğüm / 58 kenarı bu
/// bandın (≲<see cref="ShapesPathMaxNodes"/>) çok içinde (feasibility §3.5).
///
/// <para><b>[T51 / It-5 genişleme kancası — BU TASK'TA UYGULANMAZ]:</b> ~150 düğümün üstünde UIElement-per-node
/// taşımaz; o eşikte render 3 katmana ayrılır: EdgeLayer (tek <c>OnRender</c>, tüm statik kenarlar tek pass),
/// NodeLayer (DrawingVisual koleksiyonu), FlowOverlay. Akan dash kenarları O ZAMAN DA UIElement
/// <see cref="Path"/> kalır (DrawingContext içinde <c>Pen.DashStyle.Offset</c> animasyonu güvenilir çalışmaz —
/// A13.2, doğrulanmış) ve katman opaklığı animasyonu için katmanlar ince UIElement host'lara sarılır
/// (<c>ContainerVisual.Opacity</c> DP değildir, Storyboard hedefleyemez — feasibility §4.5). Bu sınıfın veri
/// girişleri (<see cref="SetGraph"/>/<see cref="UpdateStatuses"/>) ve saf çekirdeği (<see cref="GraphLayout"/>,
/// <see cref="GraphCamera"/>, <see cref="EdgeStyleResolver"/>) o yolda AYNEN yeniden kullanılır; değişen yalnız
/// çizim mekanizmasıdır.</para>
///
/// <para><b>Motion sözleşmesi:</b> her animasyon başlangıcında <see cref="AnimationsEnabledProvider"/> TAZE
/// okunur (varsayılan <c>App.Motion</c>); reduced-motion'da → statik dash + kamerada animasyon yok + stagger yok.
/// Süre/eğri token'ları <c>Duration.*</c>/<c>KeySpline.*</c> anahtarlarından, renkler <c>Brush.*</c>
/// anahtarlarından (<c>SetResourceReference</c>) gelir — hex/ms gömülmez.</para>
/// </summary>
public partial class GraphView : UserControl
{
    /// <summary>Shapes yolunun üst sınırı — üstünde T51'in 3 katmanlı DrawingVisual mimarisi gerekir (It-5).</summary>
    public const int ShapesPathMaxNodes = 150;

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

    // lucide "package" — DS DependencyGraphNode'un çizdiği ikonun BİREBİR geometrisi (24'lük viewBox).
    private const string PackageIconPath =
        "M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z " +
        "M3.3 7 12 12l8.7-5 M12 22V12";
    private const double PackageIconStrokeWidth = 1.6; // viewBox birimi — Viewbox ölçeği ile birlikte küçülür
    // lucide depWarn (DS ProjectRow/graf rozeti) — dolu üçgen, 24'lük viewBox.
    private const string WarningTriangleIconPath = "M12 3 23 21H1Z";

    private readonly Dictionary<string, GraphNodeVisual> _nodes = new(StringComparer.Ordinal);
    private readonly List<GraphEdgeVisual> _edges = [];
    private readonly List<Path> _flowingEdges = [];
    private readonly ScaleTransform _cameraScale = new(1, 1);
    private readonly TranslateTransform _cameraTranslate = new();

    private GraphLayoutResult _layout = GraphLayout.Compute([]);
    private string? _selectedNode;
    private bool _isSettled;
    private Point? _previousFocus;
    private ClockGroup? _dashClockRoot;
    private AnimationClock? _thinDashClock;
    private AnimationClock? _thickDashClock;
    private IMotionSettings? _subscribedMotion;
    private bool _edgesAnimated;
    private bool _hasCamera;

    public GraphView()
    {
        InitializeComponent();

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

    /// <summary>Motion sinyalinin TAZE okunduğu kapı (D8 — sınıf statik <c>App.Motion</c>'a doğrudan bağlanmaz,
    /// testler enjekte eder).</summary>
    public Func<bool> AnimationsEnabledProvider { get; set; } =
        () => BuildOrchestrator.App.App.Motion?.AnimationsEnabled ?? false;

    /// <summary>[M-2] <c>AnimationsEnabledChanged</c>'e abone olunacak kaynak; null ise <c>App.Motion</c>.
    /// Testler kendi sahtesini enjekte eder (abonelik <c>Loaded</c>'da kurulur, <c>Unloaded</c>'da bırakılır).</summary>
    public IMotionSettings? MotionSettings { get; set; }

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

        World.Children.Clear();
        // [M-d] Atılacak eski görsellerin (varsa) sonsuz nabız animasyonunu bırak — aksi halde bunlar artık
        // ağaçta/_nodes'ta olmasa bile timing engine'de 30fps'te uyanık kalırlardı (M-3'ün kapsadığı dash clock
        // sızıntısıyla AYNI sınıf, düğüm nabzı için).
        foreach (var stale in _nodes.Values)
            StopPulse(stale);
        _nodes.Clear();
        _edges.Clear();
        _flowingEdges.Clear();
        _previousFocus = null;
        _hasCamera = false; // yeni topoloji → kamera hedefi baştan hesaplanır

        CountsText.Text = $"{nodes.Count} projects · {edges.Count} dependencies";
        ShowEmptyState(nodes.Count == 0);
        if (nodes.Count == 0) return;

        _layout = GraphLayout.Compute(nodes);
        World.Width = _layout.Width;
        World.Height = _layout.Height;

        // Kenarlar önce eklenir → düğümlerin ALTINDA kalır (prototipte svg, düğüm div'lerinin altında).
        foreach (var edge in edges)
        {
            if (!_layout.Positions.TryGetValue(edge.From, out var from) ||
                !_layout.Positions.TryGetValue(edge.To, out var to))
                continue;

            var path = new Path
            {
                Data = GraphLayout.BuildEdgeGeometry(from, to),
                IsHitTestVisible = false, // kenarlar tıklanmaz; boş alana tıklama seçimi kaldırabilsin
            };
            // NOT: eğri bezier'lerde EdgeMode=Aliased KULLANILMAZ — tırtıklanır (feasibility §3.5).
            World.Children.Add(path);
            _edges.Add(new GraphEdgeVisual { Model = edge, Path = path });
        }

        foreach (var node in nodes)
        {
            if (!_layout.Positions.TryGetValue(node.Name, out var center)) continue;
            var visual = BuildNodeVisual(node, center);
            World.Children.Add(visual.Cell);
            _nodes[node.Name] = visual;
        }

        ApplySelection();
        ApplyEdgeStyles();
        ApplyCamera(animate: false); // ilk yerleşim kamerayı KAYDIRMAZ
        PlayRevealStagger();
    }

    /// <summary>Statüleri (ve dep-hata bayraklarını) yerinde günceller: düğüm renkleri/rozetleri, kenar stilleri
    /// ve kamera hedefi (building frontier). Topoloji ve geometri korunur, stagger TEKRAR OYNAMAZ.</summary>
    public void UpdateStatuses(IReadOnlyList<GraphNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        foreach (var node in nodes)
        {
            if (!_nodes.TryGetValue(node.Name, out var visual)) continue;
            visual.Model = node;
            ApplyNodeStatus(visual);
        }

        ApplyEdgeStyles();
        ApplyCamera(animate: true);
    }

    // ---------------------------------------------------------------- düğüm görselleri

    private GraphNodeVisual BuildNodeVisual(GraphNode node, Point center)
    {
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

        var icon = new Path
        {
            Data = Geometry.Parse(PackageIconPath),
            StrokeThickness = PackageIconStrokeWidth,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        var iconBox = new Viewbox
        {
            Width = GraphLayout.NodeSize * 0.5, // DS: size * 0.5 → 26px düğümde 13px ikon
            Height = GraphLayout.NodeSize * 0.5,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Child = new Canvas { Width = 24, Height = 24, Children = { icon } },
        };

        var badgeCircle = new Ellipse { StrokeThickness = 1 };
        badgeCircle.SetResourceReference(Shape.FillProperty, "Brush.SurfaceBase");
        badgeCircle.SetResourceReference(Shape.StrokeProperty, "Brush.StatusFailBorder");
        var badgeTriangle = new Path { Data = Geometry.Parse(WarningTriangleIconPath) };
        badgeTriangle.SetResourceReference(Shape.FillProperty, "Brush.StatusFailText");
        var badge = new Grid
        {
            Width = 13,
            Height = 13,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            // Prototip: top -6, left calc(50% + 7px) → 26'lık karede sol 13+7=20.
            Margin = new Thickness(20, -6, 0, 0),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Children =
            {
                badgeCircle,
                new Viewbox
                {
                    Width = 8, Height = 8, Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new Canvas { Width = 24, Height = 24, Children = { badgeTriangle } },
                },
            },
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
            Children = { pulseHost, badge },
        };

        var label = new TextBlock
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

        var body = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Brushes.Transparent, // etiketi de kapsayan tıklama alanı (prototipteki div gibi)
            Cursor = Cursors.Hand,
            Children = { squareHost, label },
        };

        var cell = new Grid { Width = GraphLayout.NodeCellWidth, Children = { body } };
        Canvas.SetLeft(cell, center.X - GraphLayout.NodeCellWidth / 2);
        Canvas.SetTop(cell, center.Y - GraphLayout.NodeSize / 2);

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
            PulseHost = pulseHost,
            Square = square,
            SelectionRing = ring,
            Icon = icon,
            Label = label,
            Badge = badge,
            BadgeCircle = badgeCircle,
            BadgeTriangle = badgeTriangle,
            Center = center,
        };
        ApplyNodeStatus(visual);
        return visual;
    }

    /// <summary>DS <c>DependencyGraphNode</c> statü tablosunun birebir karşılığı: çerçeve + zemin + ikon rengi
    /// (+ discovered'ın kesikli çerçevesi), dep-hata rozetinin görünürlüğü ve building nabzı.</summary>
    private void ApplyNodeStatus(GraphNodeVisual visual)
    {
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
        visual.Icon.SetResourceReference(Shape.StrokeProperty, iconColor);
        // WPF Border dashed desteklemez → kesikli çerçeve Rectangle.StrokeDashArray ile (feasibility §3.5).
        // Dash birimi StrokeThickness çarpanı: 1.5px'lik çerçevede {2,2} = 3px dolu / 3px boş — CSS'in
        // `1.5px dashed` varsayılanının karşılığı (tasarımda ayrı bir sayısal değer verilmemiştir).
        visual.Square.StrokeDashArray = dashed ? [2.0, 2.0] : [];
        visual.Badge.Visibility = visual.Model.HasDepIssue ? Visibility.Visible : Visibility.Collapsed;
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
        var neighbours = new HashSet<string>(StringComparer.Ordinal);
        if (_selectedNode is { } selected)
        {
            neighbours.Add(selected);
            foreach (var edge in _edges)
            {
                if (string.Equals(edge.Model.From, selected, StringComparison.Ordinal)) neighbours.Add(edge.Model.To);
                if (string.Equals(edge.Model.To, selected, StringComparison.Ordinal)) neighbours.Add(edge.Model.From);
            }
        }

        bool animate = AnimationsEnabledProvider();
        foreach (var (name, visual) in _nodes)
        {
            bool isSelected = string.Equals(name, _selectedNode, StringComparison.Ordinal);
            visual.SelectionRing.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
            // [M-1] DS DependencyGraphNode: `border: ${selected ? 2 : 1.5}px …` — seçim kareyi de kalınlaştırır.
            visual.Square.StrokeThickness = isSelected ? SelectedNodeBorderThickness : NodeBorderThickness;
            // DS DependencyGraphNode: etiket seçiliyken text-primary, aksi halde text-dim.
            visual.Label.SetResourceReference(TextBlock.ForegroundProperty,
                isSelected ? "Brush.TextPrimary" : "Brush.TextDim");
            double target = _selectedNode is null || neighbours.Contains(name) ? 1.0 : DimmedNodeOpacity;
            SetBodyOpacity(visual.Body, target, animate);
        }
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
        var statuses = _nodes;
        _flowingEdges.Clear();

        foreach (var edge in _edges)
        {
            statuses.TryGetValue(edge.Model.From, out var source);
            statuses.TryGetValue(edge.Model.To, out var target);

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
            if (motionUnchanged && edge.Style == style)
            {
                if (style.IsFlowing) _flowingEdges.Add(edge.Path);
                continue;
            }

            edge.Style = style;
            edge.Path.SetResourceReference(Shape.StrokeProperty, style.BrushKey);
            edge.Path.StrokeThickness = style.Thickness;
            edge.Path.Opacity = style.Opacity;
            edge.Path.StrokeDashArray = style.Dash is null ? [] : new DoubleCollection(style.Dash);

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

        // [M-3] Akan kenar kalmadıysa (veya motion kapandıysa) clock BIRAKILIR — aksi halde timing engine boşta
        // da 30fps uyanık kalırdı. Bir sonraki akan kenarda yeniden kurulur (aşağıdaki hızlı yol notuna bak).
        if (_flowingEdges.Count == 0 || !animationsEnabled)
            ReleaseDashClock();
    }

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
        bool animate = AnimationsEnabledProvider();
        foreach (var visual in _nodes.Values)
        {
            visual.Cell.BeginAnimation(OpacityProperty, null);
            if (!animate)
            {
                visual.Cell.Opacity = 1.0;
                visual.Cell.RenderTransform = Transform.Identity;
                continue;
            }

            // CSS `both` fill paritesi: gecikme boyunca opaklık 0 TUTULUR — flash yok (feasibility §3.4).
            visual.Cell.Opacity = 0.0;
            var rise = new TranslateTransform(0, -RevealRisePx);
            visual.Cell.RenderTransform = rise;

            var begin = TimeSpan.FromMilliseconds(RevealDelayMs(visual.Model.Layer));
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
    }

    // ---------------------------------------------------------------- kamera

    private void ApplyCamera(bool animate)
    {
        if (_nodes.Count == 0) return;

        var viewport = ViewportSize;
        if (viewport.Width <= 0 || viewport.Height <= 0) return;

        Point? selected = _selectedNode is { } name && _nodes.TryGetValue(name, out var sel) ? sel.Center : null;
        var building = _nodes.Values
            .Where(v => v.Model.Status == GraphStatus.Building)
            .Select(v => v.Center)
            .ToList();

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
        if (_hasCamera && camera == CurrentCamera) return;
        CurrentCamera = camera;
        _hasCamera = true;

        bool animationsEnabled = animate && AnimationsEnabledProvider();
        LastCameraAnimated = animationsEnabled;

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

    internal IReadOnlyDictionary<string, GraphNodeVisual> NodeVisuals => _nodes;
    internal IReadOnlyList<GraphEdgeVisual> EdgeVisuals => _edges;
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
    internal Point NodeCenter(string name) => _nodes[name].Center;
}
