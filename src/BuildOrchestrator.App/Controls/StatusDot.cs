using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [design v1.12.0 §2.4-2 · §5] Proje satırındaki <b>8px statü noktası</b>: adın solunda durur ve satırın sol
/// şeridiyle <b>AYNI</b> rengi taşır.
///
/// <para><b>Tek eleman, iki yüz:</b> aynı yerde üst üste duran bir HALKA ve bir DOLU DAİRE vardır; aralarında
/// yalnız opaklık değişir. Başlangıç modunda (<see cref="VisualStatus.Fresh"/>) halka görünür, bir işlem
/// başlayınca <see cref="StartMode.CrossFadeMs"/>'de çapraz-sönümle dolu daireye geçilir. Boyut ve konum
/// sabittir — hiza kaymaz, titreme olmaz.</para>
///
/// <para><b>[DEĞİŞEN KURAL — v1.12.0]</b> Başlangıç modu eskiden dolgusuz bir daire + <b>KESİKLİ</b> 1.5px
/// çemberdi. Ölçülen kusur: 8px'lik çemberde kesikler tırtıklı çiziliyordu. Yerine dört EŞİT yaydan oluşan,
/// daha ince (1.1px) bir halka geldi (<see cref="StartMode"/>) ve geçiş artık eleman değiştirmeden,
/// opaklıkla yapılıyor.</para>
///
/// <para><b>[DEĞİŞEN KURAL — v1.11.0]</b> Kontrol <c>WillBuildDot</c> adıyla doğmuştu ve statüden AYRI,
/// ortogonal bir kanal taşıyordu: "bu proje derlenecek mi?" (dolu amber = dirty · dolu gri = clean · içi boş
/// halka = bilinmiyor), üstüne de döngü üyeliğini turuncuyla EZİYORDU. design-v1.11.0 §9-1 o kanalı KALDIRDI
/// — renk yalnız son işlemin hikâyesini anlatır; plan bilgisi satırın çift SHA metnine
/// (<c>a3f81c2 → b7e91d4</c>), döngü üyeliği tek amber uyarı üçgenine indi.</para>
///
/// <para>Nokta <b>tooltip TAŞIMAZ</b> (§2.4-2): rengi zaten şeritle ve glyph'le aynı şeyi söyler. Ekran
/// okuyucu için satırın kendi adı ve statü glyph'inin UIA adı yeterlidir.</para>
/// </summary>
[TemplatePart(Name = FillPart, Type = typeof(Ellipse))]
[TemplatePart(Name = RingPart, Type = typeof(Ellipse))]
public class StatusDot : Control
{
    private const string FillPart = "PART_Dot";
    private const string RingPart = "PART_Ring";

    static StatusDot()
        => DefaultStyleKeyProperty.OverrideMetadata(
            typeof(StatusDot), new FrameworkPropertyMetadata(typeof(StatusDot)));

    /// <summary>Satırın TEK görsel durumu (<see cref="VisualStatuses"/>) — renk ve başlangıç modu ondan gelir.</summary>
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(VisualStatus), typeof(StatusDot),
        new PropertyMetadata(VisualStatus.Discovered, (d, _) => ((StatusDot)d).ApplyState()));

    public VisualStatus State
    {
        get => (VisualStatus)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    private Ellipse? _fill;
    private Ellipse? _ring;
    private bool _lighting;
    private bool _applied;

    /// <summary>[test yüzeyi] Başlangıç modunun halkası ve statü renkli dolu daire.</summary>
    internal Ellipse Ring => _ring!;
    internal Ellipse Fill => _fill!;

    /// <summary>Motion sinyalinin kapısı — ProjectRow kendi <c>MotionGate</c>'ini buraya bağlar; verilmezse
    /// statik sinyal okunur (MotionGate'in kendi varsayılanıyla aynı desen).</summary>
    public Func<bool> AnimationsEnabledProvider { get; set; } = () => MotionGate.StaticAnimationsEnabled;

    /// <summary>
    /// [design v1.11.0 §2.3] Durumu, rengin AKARAK mı yoksa anında mı oturacağıyla birlikte yazar.
    /// <paramref name="lighting"/> yalnız işaretleme dalgasında true olur — nokta şeritle ve graf düğümüyle
    /// AYNI karede yanmalıdır. Düz <see cref="State"/> yazan (dalga dışı) her yol anında oturur.
    /// </summary>
    public void SetState(VisualStatus state, bool lighting)
    {
        _lighting = lighting && AnimationsEnabledProvider();
        if (State == state) ApplyState(); // aynı değer: DP değişmez, yine de kip değişmiş olabilir
        else State = state;
        _lighting = false;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _fill = GetTemplateChild(FillPart) as Ellipse;
        _ring = GetTemplateChild(RingPart) as Ellipse;
        // Halkanın nötr grisi: şablondan DEĞİL buradan bağlanır (gerekçe Controls.xaml'de — şablon içindeki
        // DynamicResource token fırçasını DONDURUR ve onu paylaşan her yüzeyin geçiş yolunu değiştirir).
        _ring?.SetResourceReference(Shape.StrokeProperty, VisualStatuses.StripeBrushKey(VisualStatus.Fresh));
        _applied = false; // ilk çizim ANINDA oturur: açılışta çapraz-sönüm oynatmak "az önce bir işlem oldu" derdi
        ApplyState();
    }

    private void ApplyState()
    {
        if (_fill is null || _ring is null) return;
        bool start = VisualStatuses.IsStartMode(State);
        string key = VisualStatuses.StripeBrushKey(State); // şeritle AYNI tablo — ikinci bir eşleme YOK

        // Renk: geçişin TEK yolu (kopya YASAK) — dalgada akar, diğer her yolda token referansına oturur.
        MotionTokens.TransitionTokenBrush(this, _fill, Shape.FillProperty, key,
            _lighting, MarkingChoreography.LightMs);

        // Çapraz-sönüm: eleman değişmez, yalnız opaklıklar yer değiştirir.
        bool animate = _applied && AnimationsEnabledProvider();
        SetOpacity(_ring, start ? StartMode.RingOpacity : 0, animate);
        SetOpacity(_fill, start ? 0 : 1, animate);
        _applied = true;
    }

    /// <summary>Opaklığı hedefe yazar. Animasyonlu yolda <see cref="MotionTokens.SplineTo"/> kullanılır —
    /// from'suz olduğu için uçuştaki bir geçiş O ANKİ değerinden devam eder (CSS transition paritesi).</summary>
    private void SetOpacity(UIElement element, double target, bool animate)
    {
        if (!animate)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = target;
            return;
        }
        var spline = MotionTokens.ResolveKeySpline(this, "KeySpline.EaseStandard", new KeySpline(0.4, 0, 0.2, 1));
        element.BeginAnimation(OpacityProperty,
            MotionTokens.SplineTo(target, TimeSpan.FromMilliseconds(StartMode.CrossFadeMs), spline),
            System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
    }
}
