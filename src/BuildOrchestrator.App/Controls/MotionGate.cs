using System.Windows;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [W2/It-5] Motion sinyalinin sahip-tarafı kablajı — TEK yer. Öncesinde AYNI üçlü altı tipte elle kopyalanmıştı:
/// (a) <c>AnimationsEnabledProvider</c> ve onun birebir aynı varsayılan lambda'sı, (b) <c>MotionSettings</c> test
/// seam'i, (c) <c>Loaded</c>/<c>Unloaded</c> üstünde subscribe-once abonelik + <c>_subscribedMotion</c> alanı.
///
/// <para><b>İki abonelik kipi vardır ve ikisi de KASITLIDIR</b> (<see cref="MotionGate(FrameworkElement, bool)"/>
/// <c>latchFirst</c>):
/// <list type="bullet">
///   <item><b>latch-first</b> (<see cref="Graph.GraphView"/>): İLK abonelikten sonra <see cref="MotionSettings"/>
///     ataması YOK SAYILIR. <c>MainWindow</c> bu sözleşmeye açıkça dayanır ("GraphView'ın MotionSettings'i
///     Loaded'dan ÖNCE atanmalı"). Latch yalnız <c>Unloaded</c>'da açılır.</item>
///   <item><b>latch'siz</b> (ProjectRow/StickyRibbon/BuildingSpinner/StatusGlyph): her <c>Loaded</c>'da kaynak
///     YENİDEN okunur; abonelik <c>-=</c> sonra <c>+=</c> ile idempotent tutulur (Loaded iki kez ateşlense de
///     TEK abonelik kalır → çift Refresh birikmez).</item>
/// </list>
/// İki kip de <c>MotionOwnerHygieneTests</c>'te AYRI AYRI pinlenmiştir.</para>
///
/// <para>Sahiplerden bir kısmı yalnız TAZE OKUMA ister (StickyLayerList, EventStreamView/Row) — onlar
/// parametresiz ctor'u kullanır: canlı abonelik KURULMAZ (bugünkü davranış birebir korunur).</para>
/// </summary>
internal sealed class MotionGate
{
    /// <summary>Statik motion sinyalinin TEK okuma ifadesi — <c>App.Motion?.AnimationsEnabled ?? false</c>.
    /// Bu satır önce altı provider varsayılanında + <see cref="MotionTokens"/>'ın üç geçiş kapısında ayrı ayrı
    /// yazılıydı. <c>App.Motion</c> headless'ta null'dur (= reduced-motion) — testler bunu açıkça assert eder.</summary>
    public static bool StaticAnimationsEnabled => App.Motion?.AnimationsEnabled ?? false;

    private readonly bool _latchFirst;
    private IMotionSettings? _subscribed;

    /// <summary>Yalnız TAZE OKUMA kapısı — canlı <c>AnimationsEnabledChanged</c> aboneliği KURULMAZ.</summary>
    public MotionGate()
    {
    }

    /// <summary>Okuma kapısı + <paramref name="owner"/>'ın <c>Loaded</c>/<c>Unloaded</c>'ına bağlı canlı abonelik.
    /// Kablaj ctor'da kurulduğundan, sahibinin KENDİ <c>Loaded</c> handler'ından ÖNCE koşar (eski kodda abonelik
    /// de handler'ın ilk satırıydı — sıra birebir korunur).</summary>
    public MotionGate(FrameworkElement owner, bool latchFirst = false)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _latchFirst = latchFirst;
        owner.Loaded += (_, _) => Subscribe();
        owner.Unloaded += (_, _) => Unsubscribe();
    }

    /// <summary>Motion sinyalinin TAZE okunduğu kapı (D8 — sahip statik <c>App.Motion</c>'a doğrudan bağlanmaz,
    /// testler enjekte eder).</summary>
    public Func<bool> AnimationsEnabledProvider { get; set; } = () => StaticAnimationsEnabled;

    /// <summary><c>AnimationsEnabledChanged</c>'e abone olunacak kaynak; null ise <c>App.Motion</c>.</summary>
    public IMotionSettings? MotionSettings { get; set; }

    /// <summary>Animasyonlar ŞU AN açık mı — her çağrıda provider'dan TAZE okunur (cache YOK).</summary>
    public bool Enabled => AnimationsEnabledProvider();

    /// <summary>Abone olunan kaynağın sinyali değiştiğinde yeniden yayınlanır (sahip kendi motion'ını tazeler).</summary>
    public event EventHandler? Changed;

    private void Subscribe()
    {
        if (_latchFirst && _subscribed is not null) return; // latch kapalı — ilk kaynak sabitlendi
        _subscribed = MotionSettings ?? App.Motion;
        if (_subscribed is not { } motion) return;
        motion.AnimationsEnabledChanged -= OnSignalChanged; // subscribe-once: çift abonelik birikmesin
        motion.AnimationsEnabledChanged += OnSignalChanged;
    }

    private void Unsubscribe()
    {
        if (_subscribed is { } motion) motion.AnimationsEnabledChanged -= OnSignalChanged;
        _subscribed = null;
    }

    private void OnSignalChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
