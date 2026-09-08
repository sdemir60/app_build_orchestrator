using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T60] DS <c>StatusGlyph</c> (_ds_bundle.js:1446-1531). Statü RENK + GLYPH + METİN üçlüsüyle birlikte
/// taşınır (colorblind-safe, README §1.1) — bu kontrol ilk ikisini çizer, metni (etiket) çağıran verir.
/// Gövde ince bir HALKA + içine 1.5px'lik bir işarettir; <c>discovered</c> aynı halkanın KESİKLİSİ,
/// <c>building</c> ise AYNI halkanın dönen hâlidir (bkz. <see cref="BuildingSpinner"/>), <c>cycle</c> halkasız
/// bir uyarı üçgenidir.
///
/// <para><b>Statü kümesi:</b> <see cref="GraphStatus"/> YENİDEN KULLANILIR — DS <c>STATUS_META</c>'nın
/// birebir aynı yedi değeri T63'te zaten tanımlanmıştı; ikinci bir enum kopya olurdu (CLAUDE.md). Tipin adı
/// grafa özgü görünse de içeriği DS'in genel statü kümesidir.</para>
///
/// <para>Geometriler kodda parse EDİLMEZ, <c>Icons.xaml</c>'den çözülür (IconGeometryTests bunu pinler);
/// renkler Tokens.xaml'den.</para>
///
/// <para><b>Kontrolün KENDİ animasyonu yoktur.</b> DS bundle'ının genel <c>StatusGlyph</c>'i
/// <c>building</c>'de 1.6s'lik bir opaklık nabzı (<c>ds-pulse</c>, _ds_bundle.js:1440/:1527) taşır; bu
/// uygulama onu <c>building</c> için HİÇ çizmez — kendi sarmalayıcısı araya girip her seferinde dönen
/// halkayı koyar (BuildApp.jsx:172-175) ve o halkanın tek animasyonu DÖNÜŞtür. Nabız burada da yoktur:
/// tek hareketli yüzey <see cref="BuildingSpinner"/>'dır ve motion kapısı ONUN üzerindedir.</para>
/// </summary>
[TemplatePart(Name = RingPart, Type = typeof(Path))]
[TemplatePart(Name = InnerPart, Type = typeof(Path))]
[TemplatePart(Name = SpinnerPart, Type = typeof(BuildingSpinner))]
public class StatusGlyph : Control
{
    private const string RingPart = "PART_Ring";
    private const string InnerPart = "PART_Inner";
    private const string SpinnerPart = "PART_Spinner";

    static StatusGlyph()
        => DefaultStyleKeyProperty.OverrideMetadata(
            typeof(StatusGlyph), new FrameworkPropertyMetadata(typeof(StatusGlyph)));

    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(GraphStatus), typeof(StatusGlyph),
        new PropertyMetadata(GraphStatus.Discovered, (d, _) => ((StatusGlyph)d).ApplyStatus()));

    public GraphStatus Status
    {
        get => (GraphStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <summary>Çizim kutusunun kenarı (DIP). DS varsayılanı 16 (_ds_bundle.js:1482).</summary>
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(StatusGlyph), new PropertyMetadata(16.0));

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private Path? _ring;
    private Path? _inner;
    private BuildingSpinner? _spinner;

    /// <summary>[test yüzeyi] Kontrolün TEK hareketli yüzeyi — <c>building</c> dışında gizlidir. Motion
    /// kapısı (sinyal, reduced-motion, görünürlük) onun üzerindedir; glyph bir motion sahibi DEĞİLDİR.</summary>
    internal BuildingSpinner? Spinner => _spinner;

    public StatusGlyph() => Loaded += (_, _) => ApplyStatus();

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _ring = GetTemplateChild(RingPart) as Path;
        _inner = GetTemplateChild(InnerPart) as Path;
        _spinner = GetTemplateChild(SpinnerPart) as BuildingSpinner;
        ApplyStatus();
    }

    /// <summary>DS <c>STATUS_META</c> (_ds_bundle.js:1402-1433) — statünün metin/glyph rengi.
    /// <c>discovered</c> için <c>text-faint</c>, <c>building</c> için amber ailesidir.</summary>
    internal static string BrushKeyFor(GraphStatus status) => status switch
    {
        GraphStatus.Queued => "Brush.StatusQueuedText",
        GraphStatus.Building => "Brush.AmberText",
        GraphStatus.Succeeded => "Brush.StatusSuccessText",
        GraphStatus.Failed => "Brush.StatusFailText",
        GraphStatus.Skipped => "Brush.StatusSkippedText",
        GraphStatus.Cycle => "Brush.StatusCycleText",
        _ => "Brush.TextFaint",
    };

    /// <summary>[A13/T5] DS <c>STATUS_META</c>'nın ÜÇÜNCÜ üyesi: statünün İngilizce METNİ (design-v1 EN_STATUS,
    /// BuildApp.jsx:342). Bu kontrol rengi ve glyph'i çizer, metni çağıran verir — metin eşlemesi de bu yüzden
    /// diğer ikisinin yanında durur.
    ///
    /// <para>Eşleme <c>ProjectRow</c>'un private <c>StatusLabel</c>'ıydı; graf düğümünün ekran-okuyucu adı
    /// (<see cref="AccessibilityNames.GraphNode"/>) ikinci tüketici olunca buraya alındı — ikinci bir kopya
    /// YASAK (CLAUDE.md). Davranış değişmedi.</para></summary>
    internal static string LabelFor(GraphStatus status) => status switch
    {
        GraphStatus.Queued => "Queued",
        GraphStatus.Building => "Building",
        GraphStatus.Succeeded => "Succeeded",
        GraphStatus.Failed => "Failed",
        GraphStatus.Skipped => "Skipped",
        GraphStatus.Cycle => "Cycle",
        _ => "Discovered",
    };

    /// <summary>Halkanın içine düşen işaret (_ds_bundle.js:1459-1478 <c>inner()</c>); <c>null</c> = işaret yok.</summary>
    internal static string? InnerIconKeyFor(GraphStatus status) => status switch
    {
        GraphStatus.Succeeded => "Icon.StatusCheck",
        GraphStatus.Failed => "Icon.StatusCross",
        GraphStatus.Skipped => "Icon.StatusDash",
        GraphStatus.Queued => "Icon.StatusClock",
        GraphStatus.Cycle => "Icon.StatusCycle",
        _ => null,
    };

    private void ApplyStatus()
    {
        if (_ring is null || _inner is null || _spinner is null) return;

        string brushKey = BrushKeyFor(Status);
        SetResourceReference(ForegroundProperty, brushKey);

        bool building = Status == GraphStatus.Building;
        // cycle: halkasız (kendi üçgeni gövdedir); building: halka yerine dönen yay.
        bool hasRing = !building && Status != GraphStatus.Cycle;

        _spinner.Visibility = building ? Visibility.Visible : Visibility.Collapsed;
        _ring.Visibility = hasRing ? Visibility.Visible : Visibility.Collapsed;
        if (hasRing)
        {
            IconPaint.Apply(_ring, this, "Icon.StatusRing", brushKey);
            // discovered = AYNI halkanın kesiklisi (_ds_bundle.js:1515-1518): dash + opaklık .9;
            // diğerlerinde düz halka, opaklık .6 (_ds_bundle.js:1452).
            bool discovered = Status == GraphStatus.Discovered;
            // Desen ve opaklık, halkanın DÖNEN hâliyle (BuildingSpinner) PAYLAŞILIR — aynı halkanın iki
            // hâli iki ayrı sayı tablosu taşıyamaz (kopya YASAK, CLAUDE.md).
            _ring.StrokeDashArray = discovered
                ? BuildingSpinner.DashesInStrokeUnits(this, _ring.StrokeThickness)
                : [];
            _ring.Opacity = discovered ? BuildingSpinner.DashedRingOpacity : 0.6;
        }

        string? innerKey = InnerIconKeyFor(Status);
        _inner.Visibility = innerKey is null ? Visibility.Collapsed : Visibility.Visible;
        if (innerKey is not null) IconPaint.Apply(_inner, this, innerKey, brushKey);
    }
}
