using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T60] DS <c>StatusGlyph</c> (_ds_bundle.js:1446-1531). Statü RENK + GLYPH + METİN üçlüsüyle birlikte
/// taşınır (colorblind-safe, README §1.1) — bu kontrol ilk ikisini çizer, metni (etiket) çağıran verir.
/// Gövde ince bir HALKA + içine 1.5px'lik bir işarettir; <c>discovered</c> aynı halkanın KESİKLİSİ,
/// <c>building</c> ise dönen 270°'lik yaydır (bkz. <see cref="BuildingSpinner"/>), <c>cycle</c> halkasız
/// bir uyarı üçgenidir.
///
/// <para><b>Statü kümesi:</b> <see cref="GraphStatus"/> YENİDEN KULLANILIR — DS <c>STATUS_META</c>'nın
/// birebir aynı yedi değeri T63'te zaten tanımlanmıştı; ikinci bir enum kopya olurdu (CLAUDE.md). Tipin adı
/// grafa özgü görünse de içeriği DS'in genel statü kümesidir.</para>
///
/// <para>Geometriler kodda parse EDİLMEZ, <c>Icons.xaml</c>'den çözülür (IconGeometryTests bunu pinler);
/// renkler Tokens.xaml'den. Building'in 1.6s'lik "nefes"i (_ds_bundle.js:1440 <c>ds-pulse</c>) reduced-motion'da
/// hiç kurulmaz ve sinyal canlı izlenir — GraphView'ün düğüm nabzıyla AYNI kural.</para>
/// </summary>
[TemplatePart(Name = RingPart, Type = typeof(Path))]
[TemplatePart(Name = InnerPart, Type = typeof(Path))]
[TemplatePart(Name = SpinnerPart, Type = typeof(BuildingSpinner))]
public class StatusGlyph : Control
{
    private const string RingPart = "PART_Ring";
    private const string InnerPart = "PART_Inner";
    private const string SpinnerPart = "PART_Spinner";

    /// <summary>_ds_bundle.js:1440 — <c>ds-pulse 1.6s var(--ease-in-out) infinite</c>, opaklık 1 → .45 → 1.</summary>
    private const double PulseMs = 1600;
    private const double PulseMinOpacity = 0.45;
    private const int DecorativeFrameRate = 30;

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

    /// <summary>[W2] Provider + <c>MotionSettings</c> seam'i + subscribe-once kablajı TEK yerde
    /// (<see cref="MotionGate"/>) — latch'siz kip. <b>Seam kazanımı:</b> BuildingSpinner ile aynı gerekçe.</summary>
    private readonly MotionGate _motion;

    public StatusGlyph()
    {
        _motion = new MotionGate(this);
        _motion.Changed += (_, _) => ApplyPulse();
        Loaded += (_, _) => ApplyStatus();
        Unloaded += (_, _) => StopPulse();
        // [BuildingSpinner.Refresh deseni] Görünürlük DEĞİŞİMİ nabzı yeniden değerlendirir. Gizlenmek
        // Unloaded DEĞİLDİR: gizlenen kontrol ağaçta kalır, Status'u değişmez ve ApplyPulse bir daha hiç
        // çağrılmazdı — sonsuz nabız görünmeyen bir kontrolün üzerinde dönmeye devam ederdi. WPF'in
        // zamanlayıcısı etkin tek bir saat kaldığı sürece boş kareye inmediği için bunun bedeli yalnız o
        // kontrol değil, TÜM render döngüsüdür.
        IsVisibleChanged += (_, _) => ApplyPulse();
    }

    /// <summary>[W2] Motion sinyalinin TAZE okunduğu kapı (D8) — testler enjekte eder; varsayılan <c>App.Motion</c>.</summary>
    public Func<bool> AnimationsEnabledProvider
    {
        get => _motion.AnimationsEnabledProvider;
        set => _motion.AnimationsEnabledProvider = value;
    }

    /// <summary>[W2] <c>AnimationsEnabledChanged</c>'e abone olunacak kaynak; null ise <c>App.Motion</c>.</summary>
    public Services.IMotionSettings? MotionSettings
    {
        get => _motion.MotionSettings;
        set => _motion.MotionSettings = value;
    }

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
            _ring.StrokeDashArray = discovered
                ? TryFindResource("Icon.StatusRing.DashArray") as DoubleCollection ?? []
                : [];
            _ring.Opacity = discovered ? 0.9 : 0.6;
        }

        string? innerKey = InnerIconKeyFor(Status);
        _inner.Visibility = innerKey is null ? Visibility.Collapsed : Visibility.Visible;
        if (innerKey is not null) IconPaint.Apply(_inner, this, innerKey, brushKey);

        ApplyPulse();
    }

    private bool _isPulsing;

    /// <summary>[Test] Nabız o an dönüyor mu — reduced-motion kapsama testi bunun false olduğunu doğrular.</summary>
    internal bool IsPulsing => _isPulsing;

    /// <summary>[GraphView.ApplyBuildingPulse ile AYNI kural] Zaten dönen bir nabız YENİDEN BAŞLATILMAZ —
    /// aksi halde her statü güncellemesinde nabız baştan alır ve "takılı" görünür.</summary>
    private void ApplyPulse()
    {
        // Görünürlük terimi kardeş kontrolle (BuildingSpinner.Refresh) AYNI: görünmeyen bir yüzey animasyon
        // saati tutmaz.
        bool shouldPulse = Status == GraphStatus.Building && IsVisible && _motion.Enabled;
        if (shouldPulse == _isPulsing) return;
        _isPulsing = shouldPulse;

        if (!shouldPulse) { StopPulse(); return; }

        var spline = MotionTokens.ResolveKeySpline(this, "KeySpline.EaseInOut", new KeySpline(0.65, 0, 0.35, 1));
        var pulse = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        pulse.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        pulse.KeyFrames.Add(new SplineDoubleKeyFrame(PulseMinOpacity, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(PulseMs / 2)), spline));
        pulse.KeyFrames.Add(new SplineDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(PulseMs)), spline));
        Timeline.SetDesiredFrameRate(pulse, DecorativeFrameRate);
        BeginAnimation(OpacityProperty, pulse);
    }

    private void StopPulse()
    {
        _isPulsing = false;
        BeginAnimation(OpacityProperty, null);
        Opacity = 1.0;
    }
}
