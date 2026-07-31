using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace BuildOrchestrator.App.Graph;

/// <summary>
/// [A13/T5] Bir graf düğümünün TIKLANABİLİR gövdesi (kare + etiket) — davranışça <see cref="StackPanel"/>'in
/// TA KENDİSİ, tek fark bir automation peer'ı olmasıdır.
///
/// <para><b>Neden ayrı bir tip (ölçüldü):</b> düz bir <see cref="StackPanel"/>'in peer'ı YOKTUR
/// (<c>UIElementAutomationPeer.CreatePeerForElement(new StackPanel())</c> → <c>null</c>), yani UIA ağacında
/// kendi öğesi olarak HİÇ GÖRÜNMEZ ve ona verilen bir <c>AutomationProperties.Name</c> ekran okuyucuya ASLA
/// ulaşmaz. Grafın "ekran okuyucuya görünmez" olması bu yüzden tek başına ad eksikliği değildi; düğümün UIA'da
/// bir öğe HÂLİNE gelmesi gerekiyordu.</para>
///
/// <para>Görünüm/ölçü/motion tarafında hiçbir üye override EDİLMEZ — düğümün yerleşimi, opaklık hedefleri ve
/// hit-test'i birebir eskisi gibidir.</para>
/// </summary>
internal sealed class GraphNodeBody : StackPanel
{
    protected override AutomationPeer OnCreateAutomationPeer() => new GraphNodeBodyPeer(this);

    /// <summary>Gövdeye tıklamak düğümü seçer → UIA rolü <see cref="AutomationControlType.Button"/>'dır.
    /// Adı (proje adı + statü) düğüm başına <see cref="GraphView"/> verir.</summary>
    private sealed class GraphNodeBodyPeer(GraphNodeBody owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        protected override string GetClassNameCore() => nameof(GraphNodeBody);
    }
}

/// <summary>
/// [G2] Bir düğümün MODELİ + yerleşimi + (varsa) görseli. Cull tembel materyalizasyondur: görünür alana
/// değmeyen düğümün <see cref="Visual"/>'i <c>null</c>'dır ve hiçbir UIElement kurulmamıştır. Statü/kamera/kenar
/// mantığı MODEL üzerinden çalışır, dolayısıyla cull edilmiş bir düğüm de kamerada frontier'e katılır, kenar
/// stilini besler ve seçildiğinde doğru görünür (materyalizasyon o anda yapılır).
/// </summary>
internal sealed class GraphNodeSlot
{
    public required GraphNode Model { get; set; }
    /// <summary>Düğüm karesinin graf koordinatlarındaki merkezi (kamera hedefi + cull sınırı).</summary>
    public required Point Center { get; init; }
    /// <summary>Cull testinde kullanılan dünya sınırları (<see cref="GraphCulling.NodeBounds"/>).</summary>
    public required Rect Bounds { get; init; }
    /// <summary>[G2/LOD] Bu düğümün katmanında etiket kurulur mu (<see cref="GraphLayout.LabelsFit"/>).</summary>
    public required bool ShowsLabel { get; init; }
    public GraphNodeVisual? Visual { get; set; }
}

/// <summary>[G2] Bir kenarın modeli + uç noktaları + (varsa) görseli — düğüm tarafıyla aynı tembel şema.</summary>
internal sealed class GraphEdgeSlot
{
    public required GraphEdge Model { get; init; }
    public required Point From { get; init; }
    public required Point To { get; init; }
    public required Rect Bounds { get; init; }
    public GraphEdgeVisual? Visual { get; set; }
}

/// <summary>
/// [T63] Tek bir graf düğümünün WPF görselleri. Statü değiştiğinde bu parçalar YERİNDE güncellenir — şablon
/// yeniden kurulmaz (kenar geometrileri gibi düğüm ağacı da koşu boyunca sabittir).
///
/// <para><b>ÜÇ ayrı opaklık taşıyıcısı:</b> <see cref="Cell"/> ilk açılıştaki katman stagger'ının hedefi,
/// <see cref="Body"/> seçim sönmesinin hedefi, <see cref="PulseHost"/> ise building nabzının hedefidir. Aynı
/// elemanda olsalardı biri diğerinin değerini ezerdi (stagger HoldEnd sönmeyi kilitler; sonsuz nabız da hem
/// stagger'ı hem sönmeyi ezerdi).</para>
///
/// <para><b>[G2] İki alt-ağaç TEMBELDİR:</b> dep-hata rozeti (<see cref="Badge"/>) yalnız <c>HasDepIssue</c>
/// olduğunda, etiket (<see cref="Label"/>) yalnız katmanın aralığı etiket hücresini taşıdığında kurulur. Rozet
/// eskiden HER düğümde kurulup <c>Collapsed</c> ediliyordu — düğüm başına 17 nesnenin 6'sı, gerçek profilde
/// düğümlerin ~%97'sinde ÖLÜ. İkisi de <c>null</c> olabilir; okuyan taraf null kontrolü yapar.</para>
/// </summary>
internal sealed class GraphNodeVisual
{
    public required GraphNode Model { get; set; }
    /// <summary>Canvas'a yerleştirilen dış hücre — katman reveal (opacity + 5px yukarıdan) animasyonunun hedefi.</summary>
    public required Grid Cell { get; init; }
    /// <summary>Tıklanabilir gövde (kare + etiket) — seçim sönmesinin (%25) hedefi. [A13/T5] Ekran-okuyucu adını
    /// TAŞIYAN öğe de budur (bkz. <see cref="GraphNodeBody"/>: tıklanan öğe = UIA'da adlanan öğe).</summary>
    public required GraphNodeBody Body { get; init; }
    /// <summary>Kare + rozet kabı — rozet TEMBEL kurulduğunda buraya (nabız kabının KARDEŞİ olarak) eklenir.</summary>
    public required Grid SquareHost { get; init; }
    /// <summary>Halka + kare + ikon kabı — <c>building</c> nabzının (1↔0.5, 1.6s) hedefi. DS'te <c>ds-node-pulse</c>
    /// sınıfı YALNIZ kare span'ındadır: dep-hata rozeti (kardeş eleman) ve etiket nabızla sönmez, bu yüzden rozet
    /// bu kabın DIŞINDA, <c>squareHost</c> düzeyinde durur.</summary>
    public required Grid PulseHost { get; init; }
    /// <summary>Nabız şu an dönüyor mu — her <c>UpdateStatuses</c> tick'inde animasyonun baştan başlatılmasını
    /// (nabzın "takılmasını") önler.</summary>
    public bool IsPulsing { get; set; }
    /// <summary>26px, 4px köşe yarıçaplı KARE (K4/DS; README'deki "daire" ifadesi DS koduyla çelişir).
    /// discovered'ta kesikli çerçeve — WPF Border dashed desteklemediği için Rectangle.</summary>
    public required Rectangle Square { get; init; }
    /// <summary>Seçiliyken görünen amber halka (DS: 2px outline, 2px offset).</summary>
    public required Rectangle SelectionRing { get; init; }
    /// <summary>13px paket ikonu (lucide "package" geometrisi; tam ikon seti T64).</summary>
    public required Path Icon { get; init; }
    /// <summary>[G2/LOD] Kare altındaki mono 10px kısa ad — <c>TextFormattingMode=Ideal</c> LOKAL override taşır.
    ///
    /// <para>Katmanın düğüm aralığı, o katmanın <b>en geniş etiketinin ÇİZİLEN genişliğinin</b> altına
    /// düştüğünde etiketler gerçekten üst üste biner ve ikisi de okunmaz olur; bu durumda etiket HİÇ KURULMAZ
    /// ve burası <c>null</c> kalır. Eşik ölçülür (<see cref="GraphLabelMetrics"/>), sabit bir sayı ya da
    /// <see cref="GraphLayout.NodeCellWidth"/> kelepçesi DEĞİLDİR — kelepçe yalnız kırpma sınırıdır, dolayısıyla
    /// kısa adlı bir katman dar aralıkta da etiketlerini korur. LOD, cull ile aynı kapıya bağlıdır
    /// (<see cref="GraphView.FullDetailMaxNodes"/>): o bandın altında etiket ASLA düşmez.</para>
    ///
    /// <para>Etiketi düşen düğüm anonim kalmaz — tam proje adını veren bir tooltip taşır.</para></summary>
    public TextBlock? Label { get; init; }
    /// <summary>[G2] Dep-hata rozeti kabı (13px, sağ üst köşe) — yalnız <c>HasDepIssue</c> olan düğümde KURULUR
    /// (eskiden her düğümde kurulup gizleniyordu). Yoksa <c>null</c>.</summary>
    public Grid? Badge { get; set; }
    public Ellipse? BadgeCircle { get; set; }
    public Path? BadgeTriangle { get; set; }
    /// <summary>Düğümün graf koordinatlarındaki merkezi (kamera hedefi).</summary>
    public required Point Center { get; init; }
}

/// <summary>[T63] Tek bir kenarın görseli + en son uygulanan stil (akan kenarlar paylaşılan dash clock'una bağlanır).</summary>
internal sealed class GraphEdgeVisual
{
    public required GraphEdge Model { get; init; }
    public required Path Path { get; init; }
    public EdgeStyle? Style { get; set; }
}
