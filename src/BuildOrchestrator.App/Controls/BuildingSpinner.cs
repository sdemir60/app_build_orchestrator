using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T60] DS <c>Spinner</c> (_ds_bundle.js:1358-1396): 270°'lik yay, <c>currentColor</c>, 900ms lineer sonsuz
/// dönüş — "sakin, sabit hız". Aynı yay <c>StatusGlyph status='building'</c>'in de gövdesidir
/// (_ds_bundle.js:1505 ile :1392 BİREBİR aynı path), bu yüzden <see cref="StatusGlyph"/> ayrı bir çizim
/// tutmaz, BU kontrolü barındırır (kopya YASAK, CLAUDE.md).
///
/// <para><b>SAPMA — hakemlik bekliyor:</b> design-v1 README:66 building spinner'ını "discovered'ın KESİKLİ
/// halkasının dönen hali, <c>stroke-dasharray 2.3 2.5</c>, 1.4s" diye tarif eder; bundle'ın kodu ise 270°'lik
/// bir YAY'ı 900ms'de döndürür. Bunlar farklı çizimler ve farklı hızlardır. T60'ın brief'inde kayıtlı tek
/// çelişki çözümü ("kod kazanır", Dialog yüzeyi örneği) uygulandı: bundle esas alındı. Karar gözden
/// geçirilmelidir.</para>
///
/// <para>Reduced-motion: dönüş HİÇ kurulmaz (DS'te de kural <c>@media (prefers-reduced-motion: no-preference)</c>
/// içindedir) ve sinyal CANLI izlenir — kapanınca durur, açılınca başlar.</para>
/// </summary>
public class BuildingSpinner : Control
{
    /// <summary>_ds_bundle.js:1359 — <c>animation: ds-spinner-rot 900ms linear infinite</c>.</summary>
    internal const double RotationMs = 900;

    /// <summary>Dekoratif, sonsuz dönen animasyon — GraphView'ün nabzıyla AYNI gerekçe (feasibility §3.4):
    /// tam kare hızında sürmek gereksiz GPU/CPU yükü.</summary>
    private const int DecorativeFrameRate = 30;

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
        Timeline.SetDesiredFrameRate(spin, DecorativeFrameRate);
        return spin;
    }

    private void Start()
    {
        if (_rotation is null) return;
        _rotation.BeginAnimation(RotateTransform.AngleProperty, BuildSpinAnimation());
    }

    private void Stop()
    {
        _isSpinning = false;
        if (_rotation is null) return;
        _rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        _rotation.Angle = 0;
    }
}
