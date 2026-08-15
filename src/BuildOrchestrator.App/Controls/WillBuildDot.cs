using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T60] DS <c>WillBuildDot</c> (_ds_bundle.js:1842-1874): statü accent'inden AYRI, ORTOGONAL kanal —
/// "bu proje derlenecek mi?". 8px daire (<c>Size.DotSize</c>): dolu amber = dirty (derlenecek), dolu gri =
/// clean (atlanacak), içi boş + 1px halka = unknown (Sync öncesi).
///
/// <para><see cref="State"/> bilerek <c>bool?</c>'tır: üç durum "evet / hayır / henüz bilinmiyor"dur ve
/// üçüncüsü bir enum değeri değil, BİLGİNİN YOKLUĞUDUR (README:224 "Sync yapılmadı → tüm dot'lar unknown").</para>
///
/// <para><b>ÇELİŞKİ — hakemlik bekliyor (unknown halkasının rengi):</b> tokens/colors.css:41
/// (<c>--dot-outline-color: var(--border-subtle)</c>) ve README:42 ("transparent + 1px <c>#1c1c20</c> halka")
/// AYNI şeyi söyler ve Tokens.xaml'deki <c>Brush.DotOutline</c> tam da bunun içindir; bundle'ın bileşen kodu
/// (_ds_bundle.js:1864) ise halkayı <c>var(--text-faint)</c> ile çizer. Burada iki tasarım kaynağının
/// (token dosyası + README) hemfikir olduğu değer alındı — <c>Brush.DotOutline</c>. Bu, T60 brief'indeki
/// "kod kazanır" örneğinin (Dialog yüzeyi) TERSİ yönde bir seçimdir ve bilinçlidir: orada çelişki 1-1'di,
/// burada 2-1'dir. Karar gözden geçirilmelidir; tek anahtar değişimiyle geri alınabilir.</para>
/// </summary>
[TemplatePart(Name = DotPart, Type = typeof(Ellipse))]
public class WillBuildDot : Control
{
    private const string DotPart = "PART_Dot";

    static WillBuildDot()
        => DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WillBuildDot), new FrameworkPropertyMetadata(typeof(WillBuildDot)));

    /// <summary><c>true</c> = dirty (derlenecek) · <c>false</c> = clean (atlanacak) · <c>null</c> = unknown.</summary>
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(bool?), typeof(WillBuildDot),
        new PropertyMetadata(null, (d, _) => ((WillBuildDot)d).ApplyState()));

    public bool? State
    {
        get => (bool?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>
    /// [design v1.7.0 §5 — C kanalı] Proje bir dairesel bağımlılıkta mı. <b>Plan kanalını EZER:</b> üyenin
    /// noktası, derlenip yeşil bitse bile turuncu kalır — kod hâlâ döngülüdür ve bu, koşunun sonucundan
    /// bağımsız KALICI bir yapısal olgudur. Turuncu ancak kaynak düzelip Sync bunu görmediğinde kalkar.
    /// </summary>
    public static readonly DependencyProperty InCycleProperty = DependencyProperty.Register(
        nameof(InCycle), typeof(bool), typeof(WillBuildDot),
        new PropertyMetadata(false, (d, _) => ((WillBuildDot)d).ApplyState()));

    public bool InCycle
    {
        get => (bool)GetValue(InCycleProperty);
        set => SetValue(InCycleProperty, value);
    }

    private Ellipse? _dot;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _dot = GetTemplateChild(DotPart) as Ellipse;
        ApplyState();
    }

    /// <summary>_ds_bundle.js:1848-1856 — ekran okuyucu adı ve tooltip. Metin İNGİLİZCE'dir (uygulamanın
    /// tüm kullanıcı-görünür metni İngilizcedir; kaynaktaki Türkçe etiketler çevrilir).</summary>
    internal static string DescriptionFor(bool? state) => state switch
    {
        true => "Changed — will build",
        false => "Up to date — will skip",
        _ => "Unknown — waiting for Sync",
    };

    /// <summary>[design v1.7.0 §2.4-2] Bu düğümün döngü YOLU (<c>A → B → C → A</c>) — tooltip'in ikinci
    /// satırı. Boşsa tek satır kalır. Metin <see cref="ViewModels.CycleText.Path"/>'ten gelir; bu kontrol onu
    /// KURMAZ, yalnız gösterir.</summary>
    public static readonly DependencyProperty CyclePathProperty = DependencyProperty.Register(
        nameof(CyclePath), typeof(string), typeof(WillBuildDot),
        new PropertyMetadata("", (d, _) => ((WillBuildDot)d).ApplyState()));

    public string CyclePath
    {
        get => (string)GetValue(CyclePathProperty);
        set => SetValue(CyclePathProperty, value);
    }

    private void ApplyState()
    {
        string description = InCycle
            ? ViewModels.CycleText.Lines(ViewModels.CycleText.Membership, CyclePath)
            : DescriptionFor(State);
        SetValue(AutomationProperties.NameProperty, description);
        ToolTip = description;

        if (_dot is null) return;
        // C kanalı B'yi EZER: döngü üyesi her zaman turuncudur (design v1.7.0 §5).
        _dot.SetResourceReference(Shape.FillProperty, InCycle ? "Brush.StatusCycle" : State switch
        {
            true => "Brush.DotDirty",
            false => "Brush.DotClean",
            _ => "Brush.DotUnknown",
        });
        // Halka YALNIZ unknown'da çizilir (dolu iki durumda kontur yoktur, _ds_bundle.js:1859-1868).
        if (State is null && !InCycle) _dot.SetResourceReference(Shape.StrokeProperty, "Brush.DotOutline");
        else _dot.Stroke = null;
    }
}
