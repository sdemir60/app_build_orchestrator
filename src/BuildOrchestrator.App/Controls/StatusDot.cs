using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [design v1.11.0 §2.4-2 · §5] Proje satırındaki <b>8px statü noktası</b>: adın solunda durur ve satırın sol
/// şeridiyle <b>AYNI</b> rengi taşır. Başlangıç modunda (<see cref="VisualStatus.Fresh"/>) dolgusu yoktur —
/// 1.5px <c>--status-skipped-border</c> KESİKLİ halka çizilir.
///
/// <para><b>[DEĞİŞEN KURAL]</b> Kontrol <c>WillBuildDot</c> adıyla doğmuştu ve statüden AYRI, ortogonal bir
/// kanal taşıyordu: "bu proje derlenecek mi?" (dolu amber = dirty · dolu gri = clean · içi boş halka =
/// bilinmiyor), üstüne de döngü üyeliğini turuncuyla EZİYORDU. design-v1.11.0 §9-1 o kanalı KALDIRDI —
/// renk yalnız son işlemin hikâyesini anlatır; plan bilgisi satırın çift SHA metnine
/// (<c>a3f81c2 → b7e91d4</c>), döngü üyeliği tek amber uyarı üçgenine indi. Kontrol bu yüzden yeniden
/// adlandırıldı: adı artık taşıdığı bilgiyi söylüyor.</para>
///
/// <para>Nokta <b>tooltip TAŞIMAZ</b> (§2.4-2): rengi zaten şeritle ve glyph'le aynı şeyi söyler. Ekran
/// okuyucu için satırın kendi adı ve statü glyph'inin UIA adı yeterlidir.</para>
/// </summary>
[TemplatePart(Name = DotPart, Type = typeof(Ellipse))]
public class StatusDot : Control
{
    private const string DotPart = "PART_Dot";

    /// <summary>[§2.4-2] Başlangıç modundaki kesikli halkanın kalınlığı ve deseni. Dash birimi
    /// <see cref="Shape.StrokeThickness"/> çarpanıdır: 1.5px'lik halkada {1.6, 1.6} ≈ 2.4px dolu / 2.4px boş —
    /// 8px'lik bir dairenin çevresinde (≈25px) yaklaşık beş çizgi, prototipteki <c>1.5px dashed</c>'in
    /// tarayıcıdaki görünümüyle aynı sıklık.</summary>
    /// <para><b>DONDURULMUŞ</b> olmalı: paylaşılan bir <see cref="System.Windows.Freezable"/> dondurulmazsa
    /// onu ilk kuran thread'e bağlanır ve başka bir STA thread'inden okunduğunda
    /// <see cref="InvalidOperationException"/> fırlatır (GraphView.FrozenDash ile AYNI gerekçe).</para>
    internal const double FreshRingThickness = 1.5;
    internal static readonly System.Windows.Media.DoubleCollection FreshRingDash = FrozenDash([1.6, 1.6]);

    private static System.Windows.Media.DoubleCollection FrozenDash(double[] values)
    {
        var collection = new System.Windows.Media.DoubleCollection(values);
        collection.Freeze();
        return collection;
    }

    static StatusDot()
        => DefaultStyleKeyProperty.OverrideMetadata(
            typeof(StatusDot), new FrameworkPropertyMetadata(typeof(StatusDot)));

    /// <summary>Satırın TEK görsel durumu (<see cref="VisualStatuses"/>) — renk ve kesiklilik ondan gelir.</summary>
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(VisualStatus), typeof(StatusDot),
        new PropertyMetadata(VisualStatus.Discovered, (d, _) => ((StatusDot)d).ApplyState()));

    public VisualStatus State
    {
        get => (VisualStatus)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    private Ellipse? _dot;
    private bool _lighting;

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
        _dot = GetTemplateChild(DotPart) as Ellipse;
        ApplyState();
    }

    private void ApplyState()
    {
        if (_dot is null) return;
        bool fresh = VisualStatuses.IsDashed(State);
        string key = VisualStatuses.StripeBrushKey(State); // şeritle AYNI tablo — ikinci bir eşleme YOK

        if (fresh)
        {
            // Başlangıç modu: dolgu YOK, kesikli halka VAR. Dalganın hedefi değildir (dalga düz amber'a yakar).
            _dot.Fill = null;
            _dot.SetResourceReference(Shape.StrokeProperty, key);
            _dot.StrokeThickness = FreshRingThickness;
            _dot.StrokeDashArray = FreshRingDash;
            return;
        }

        // Geçişin TEK yolu (kopya YASAK): dalgada akar, diğer her yolda token referansına oturur.
        MotionTokens.TransitionTokenBrush(this, _dot, Shape.FillProperty, key,
            _lighting, MarkingChoreography.LightMs);
        _dot.Stroke = null;
        _dot.StrokeDashArray = null;
    }
}
