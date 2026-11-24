using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T60] Building spinner: başlangıç modundaki kesikli halkanın (<c>Icon.StatusRing</c>) amber, dönen hâli —
/// 1.4s lineer sonsuz dönüş, 1.5 kontur (ARCHITECTURE §14.4). DS <c>Spinner</c> ve <c>StatusGlyph status='building'</c>
/// aynı çizimdir; <see cref="StatusGlyph"/> ayrı bir çizim tutmaz, BU kontrolü barındırır (kopya YASAK, CLAUDE.md).
///
/// <para>Reduced-motion: dönüş HİÇ kurulmaz (DS'te de kural <c>@media (prefers-reduced-motion: no-preference)</c>
/// içindedir) ve sinyal CANLI izlenir — kapanınca durur, açılınca başlar.</para>
/// </summary>
public class BuildingSpinner : Control
{
    /// <summary>BuildApp.jsx:17 — <c>animation: bo-rot 1.4s linear infinite</c>.</summary>
    internal const double RotationMs = 1400;

    /// <summary>
    /// Kesikli halkanın opaklığı. TEK yer: <see cref="StatusGlyph"/>'in <c>discovered</c> halkası da bunu
    /// okur — aynı halkanın duran ve dönen hâli AYNI sayıyı taşır (BuildApp.jsx:167 <c>opacity="0.9"</c> ·
    /// _ds_bundle.js:1517).
    /// </summary>
    internal const double DashedRingOpacity = 0.9;

    /// <summary>
    /// Halkanın kesik desenini, verilen kontur kalınlığı için WPF'in beklediği birime çevirir.
    ///
    /// <para><b>Neden çevrim gerekiyor:</b> SVG'de <c>stroke-dasharray</c> KULLANICI BİRİMİDİR, WPF'te ise
    /// <see cref="System.Windows.Shapes.Shape.StrokeDashArray"/> <c>StrokeThickness</c>'ın ÇARPANIDIR.
    /// Tasarımda aynı desen iki farklı kalınlıkta çizilir — glyph'in halkası 1 (sayılar birebir geçer),
    /// spinner 1.5 — bu yüzden ikinci bir sayı tablosu YAZILMAZ, desen tek kaynaktan
    /// (<c>Icon.StatusRing.DashArray</c>) türetilir (kopya YASAK, CLAUDE.md).</para>
    ///
    /// <para>Kaynak zinciri yoksa boş döner: çağıran o zaman kesiksiz bir halka çizer — <c>StatusGlyph</c>'in
    /// ve <c>IconPaint</c>'in headless host'ta sessizce boyamayı atlama deseniyle aynı.</para>
    /// </summary>
    internal static DoubleCollection DashesInStrokeUnits(FrameworkElement host, double strokeThickness)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (strokeThickness <= 0 || host.TryFindResource("Icon.StatusRing.DashArray") is not DoubleCollection userUnits)
            return [];
        return [.. userUnits.Select(d => d / strokeThickness)];
    }

    /// <summary>Dekoratif, sonsuz dönen animasyon — GraphView'ün nabzıyla AYNI gerekçe (feasibility §3.4):
    /// tam kare hızında sürmek gereksiz GPU/CPU yükü.</summary>

    static BuildingSpinner()
        => DefaultStyleKeyProperty.OverrideMetadata(
            typeof(BuildingSpinner), new FrameworkPropertyMetadata(typeof(BuildingSpinner)));

    /// <summary>Çizim kutusunun kenarı (DIP). DS varsayılanı 14 (_ds_bundle.js:1367).</summary>
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(BuildingSpinner), new PropertyMetadata(14.0));

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private RotateTransform? _rotation;

    /// <summary>[W2] Provider + <c>MotionSettings</c> seam'i + subscribe-once kablajı TEK yerde
    /// (<see cref="MotionGate"/>) — latch'siz kip. <b>Seam kazanımı:</b> kontrol artık statik <c>App.Motion</c>'a
    /// ÇİVİLENMİŞ değildir; testler kendi sinyalini enjekte edebilir (varsayılan davranış birebir aynıdır).</summary>
    private readonly MotionGate _motion;

    public BuildingSpinner()
    {
        _motion = new MotionGate(this);
        _motion.Changed += (_, _) => Refresh();
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => Refresh();
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
        _rotation = GetTemplateChild("PART_Rotation") as RotateTransform;
        if (GetTemplateChild("PART_Ring") is System.Windows.Shapes.Path ring)
        {
            // Desen ve opaklık şablonda LİTERAL yazılamaz: desen kalınlığa göre türetilir, opaklık ise
            // glyph'in duran halkasıyla PAYLAŞILAN tek sayıdır.
            ring.StrokeDashArray = DashesInStrokeUnits(this, ring.StrokeThickness);
            ring.Opacity = DashedRingOpacity;
        }
        Refresh();
    }

    private bool _isSpinning;

    /// <summary>[Test] Dönüş saati o an CANLI mı — reduced-motion kapsama testi bunun false olduğunu doğrular.</summary>
    internal bool IsRotating => _rotation?.HasAnimatedProperties ?? false;

    /// <summary>[Motion sözleşmesi] Sinyal TAZE okunur — cache'lenmiş bir bayrak DEĞİL.
    /// [GraphView.ApplyBuildingPulse ile AYNI kural] Zaten dönen bir animasyon YENİDEN BAŞLATILMAZ; aksi
    /// halde her tetikleyicide dönüş baştan alır ve "takılı" görünürdü.</summary>
    private void Refresh()
    {
        if (_rotation is null) return;
        bool shouldSpin = IsVisible && _motion.Enabled;
        if (shouldSpin == _isSpinning) return;
        _isSpinning = shouldSpin;
        if (shouldSpin) Start(); else Stop();
    }

    /// <summary>[E3 fold — C-2 pin] Dönüş animasyonunu üreten TEK yer — kontrol ve test AYNI fabrikayı kullanır
    /// (ProjectRow.BuildBreathingAnimation deseni): 270°'lik YAY görselini 0→360° tam tur, <see cref="RotationMs"/>
    /// (900ms, C-2: bundle — README'nin 1.4s'i DEĞİL), lineer, sonsuz, 30fps döndürür. Sayısal değerler burada
    /// pinlenir (inline magic number YOK).</summary>
    internal static DoubleAnimation BuildSpinAnimation()
    {
        var spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromMilliseconds(RotationMs)))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Timeline.SetDesiredFrameRate(spin, MotionTokens.DecorativeFrameRate);
        return spin;
    }

    /// <summary>Dönüş saatinin sahibi — bırakırken ağaçtan da çıkarır (<see cref="DecorativeClock"/>).</summary>
    private readonly DecorativeClock _spin = new();

    private void Start()
    {
        if (_rotation is null) return;
        _spin.Start(BuildSpinAnimation(), _rotation, RotateTransform.AngleProperty);
    }

    private void Stop()
    {
        _isSpinning = false;
        _spin.Stop(); // ağaçtan da çıkar: BeginAnimation(null) saati yalnız söker ve yetim bırakırdı (DecorativeClock)
        if (_rotation is null) return;
        _rotation.Angle = 0;
    }
}
