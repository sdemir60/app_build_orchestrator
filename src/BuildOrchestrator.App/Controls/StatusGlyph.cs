using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T60] DS <c>StatusGlyph</c> (_ds_bundle.js:1446-1531). Statü RENK + GLYPH + METİN üçlüsüyle birlikte
/// taşınır (colorblind-safe, README §1.1) — bu kontrol ilk ikisini çizer, metni (etiket) çağıran verir.
/// Gövde ince bir HALKA + içine 1.5px'lik bir işarettir; bilinmiyor/derlenecek/işaretli aynı halkanın
/// KESİKLİSİ, <c>building</c> ise AYNI halkanın dönen hâlidir (bkz. <see cref="BuildingSpinner"/>).
///
/// <para><b>Statü kümesi:</b> <see cref="VisualStatus"/> YENİDEN KULLANILIR — satır ve node'un tek renk
/// kanalıdır; ikinci bir enum kopya olurdu (CLAUDE.md).</para>
///
/// <para><b>[DEĞİŞEN KURAL — design v1.20.0 §1.4 · §2.4-5]</b> Kontrol eskiden <see cref="GraphStatus"/>
/// (koşu statüsü) alıyordu; satırda atlanan proje <c>—</c> tire gösterirdi ve <c>cycle</c> halkasız bir
/// uyarı üçgeniydi (hiçbir üretici yazmıyordu). Değişme gerekçesi: renk artık çıktının kümülatif durumudur —
/// glyph de onu çizer (güncel ✓, bozuk ✗, derlenecek kesikli daire). <c>—</c> yalnız run-story
/// yüzeylerinde (event stream'in atlama satırı, konsol başlığının koşu sonucu) <see cref="VisualStatus.Skipped"/>
/// ile çizilir; satır/node/sayaç onu hiç almaz.</para>
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
        nameof(Status), typeof(VisualStatus), typeof(StatusGlyph),
        new PropertyMetadata(VisualStatus.Unknown, (d, _) => ((StatusGlyph)d).ApplyStatus()));

    public VisualStatus Status
    {
        get => (VisualStatus)GetValue(StatusProperty);
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
    /// Bilinmiyor/derlenecek/işaretli için <c>text-faint</c>, <c>building</c> için amber ailesidir.</summary>
    internal static string BrushKeyFor(VisualStatus status) => status switch
    {
        VisualStatus.Queued => "Brush.StatusQueuedText",
        VisualStatus.Building => "Brush.AmberText",
        VisualStatus.Current or VisualStatus.Succeeded => "Brush.StatusSuccessText",
        VisualStatus.Failed => "Brush.StatusFailText",
        VisualStatus.Skipped => "Brush.StatusSkippedText", // yalnız run-story yüzeyleri
        _ => "Brush.TextFaint",
    };

    /// <summary>[A13/T5] DS <c>STATUS_META</c>'nın ÜÇÜNCÜ üyesi: statünün İngilizce METNİ. Bu kontrol rengi ve
    /// glyph'i çizer, metni çağıran verir — metin eşlemesi de bu yüzden diğer ikisinin yanında durur. Durum
    /// sözcüklerinin TEK kaynağıdır: filtre chip'lerinin etiketi (<c>ProjectFilter.Label</c>) ve adı
    /// (<see cref="AccessibilityNames"/>) da buradan okur.
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.20.0 §2.7 · §1.4]</b> Eşleme eskiden KOŞU statüsünü
    /// (<see cref="GraphStatus"/>) alıyordu ve satır glyph'i ile graf düğümü de onu duyuruyordu: güncel olduğu
    /// için atlanan satır ✓ gösterirken "Skipped" diye okunuyordu, Sync sonrası yeşil düğüm "Discovered"dı.
    /// Değişme gerekçesi: durum yüzeyleri çıktının durumunu ÇİZER — ekran okuyucu da GÖSTERİLENİ duyar, filtre
    /// chip'iyle AYNI sözcükle. Kova <see cref="VisualStatuses.StateOf"/>'tan okunur (sayaç ve filtreyle tek
    /// kural): yeşil "Up to date" (bu koşuda derlenmiş satır dahil), gri "To build", kırmızı "Failed"; koşu
    /// bindirmesi "Queued"/"Building", işaretleme dalgası "Marked to build", karar yoksa "Not synced". Run-story
    /// yüzeyleri koşunun sonucunu söylemeye devam eder: <see cref="RunLabelFor"/>.</para></summary>
    internal static string LabelFor(VisualStatus status) => VisualStatuses.StateOf(status) switch
    {
        StandingStatus.Current => "Up to date",
        StandingStatus.Stale => "To build",
        StandingStatus.Failed => "Failed",
        _ => status switch
        {
            VisualStatus.Queued => "Queued",
            VisualStatus.Building => "Building",
            VisualStatus.Marked => "Marked to build",
            VisualStatus.Skipped => "Skipped", // yalnız run-story yüzeyleri (durum yüzeyi bunu hiç almaz)
            _ => "Not synced",                 // unknown: karar yok
        },
    };

    /// <summary>[design v1.20.0 §1.4] RUN-STORY yüzeyinin (konsol başlığı) metni: motorun bu proje hakkındaki
    /// son sözü — "Succeeded" ve "Skipped" burada kalır. Ortak sözcükler (Queued · Building · Failed · Skipped)
    /// <see cref="LabelFor(VisualStatus)"/>'dan okunur; yalnız koşu hikâyesine özgü olanlar burada yazılır:
    /// sonucun adı (durum yüzeyinde aynı satır "Up to date"tır) ve motorun hakkında konuşmadığı projenin
    /// adları.</summary>
    internal static string RunLabelFor(GraphStatus status) => status switch
    {
        GraphStatus.Succeeded => "Succeeded",
        GraphStatus.Cycle => "Cycle",
        GraphStatus.Discovered => "Discovered",
        _ => LabelFor(VisualStatuses.OfRun(status)), // Queued · Building · Failed · Skipped
    };

    /// <summary>Halkanın içine düşen işaret (_ds_bundle.js:1459-1478 <c>inner()</c>); <c>null</c> = işaret yok.</summary>
    internal static string? InnerIconKeyFor(VisualStatus status) => status switch
    {
        VisualStatus.Current or VisualStatus.Succeeded => "Icon.StatusCheck",
        VisualStatus.Failed => "Icon.StatusCross",
        VisualStatus.Skipped => "Icon.StatusDash", // yalnız run-story yüzeyleri
        VisualStatus.Queued => "Icon.StatusClock",
        _ => null, // unknown · stale · marked: kesikli halka, işaret yok
    };

    /// <summary>Kesikli halka mı (DS <c>discovered</c> çizimi): bilinmiyor, derlenecek ve işaretli.</summary>
    internal static bool IsDashedRing(VisualStatus status)
        => status is VisualStatus.Unknown or VisualStatus.Stale or VisualStatus.Marked;

    private void ApplyStatus()
    {
        if (_ring is null || _inner is null || _spinner is null) return;

        string brushKey = BrushKeyFor(Status);
        SetResourceReference(ForegroundProperty, brushKey);

        bool building = Status == VisualStatus.Building;
        // building: halka yerine dönen yay.
        bool hasRing = !building;

        _spinner.Visibility = building ? Visibility.Visible : Visibility.Collapsed;
        _ring.Visibility = hasRing ? Visibility.Visible : Visibility.Collapsed;
        if (hasRing)
        {
            IconPaint.Apply(_ring, this, "Icon.StatusRing", brushKey);
            // discovered çizimi = AYNI halkanın kesiklisi (_ds_bundle.js:1515-1518): dash + opaklık .9;
            // diğerlerinde düz halka, opaklık .6 (_ds_bundle.js:1452).
            bool discovered = IsDashedRing(Status);
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
