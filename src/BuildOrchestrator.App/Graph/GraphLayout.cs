using System.Windows;
using System.Windows.Media;

namespace BuildOrchestrator.App.Graph;

/// <summary>[T63] Yerleşim sonucu: düğüm MERKEZLERİ (ad → nokta) + graf tuvalinin ölçüsü.</summary>
/// <param name="LayerSpacing">[G2] Katman → o katmanda kullanılan düğüm aralığı. LOD kararı (etiket kurulacak mı)
/// bu aralıktan türetilir — bkz. <see cref="GraphLayout.LabelsFit"/>. Aralık katman başına farklı olduğundan
/// (kalabalık katman daralır) karar da katman başınadır.</param>
public sealed record GraphLayoutResult(
    IReadOnlyDictionary<string, Point> Positions,
    double Width,
    double Height,
    IReadOnlyDictionary<int, double> LayerSpacing)
{
    public Size Size => new(Width, Height);
}

/// <summary>[T63] Bir kenarın kübik bezier'i (yukarıdan aşağı) — saf geometri, WPF nesnesi değil.</summary>
public readonly record struct EdgeCurveGeometry(Point Start, Point Control1, Point Control2, Point End);

/// <summary>
/// [T63] design-v1 §2.3 katmanlı DAG yerleşimi — prototype/app/build-data.js <c>GRAPH</c> IIFE'sinin SAF portu.
/// Her katman bir yatay sıra (satır aralığı 96px), düğüm aralığı ≤96px, tuval TABAN 880px geniş; kenarlar
/// yukarıdan aşağı kübik bezier.
///
/// <para><b>[G1/It-5] Tuval düğüm sayısıyla BÜYÜR.</b> Prototipte tuval 880px'e sabitti; 500-1000 düğümlü bir
/// katmanda aralık <c>(880-70)/n</c> ≈ 0.8-1.6px'e iner ve 26px'lik kareler FİZİKSEL OLARAK üst üste binerdi
/// (kamera <see cref="GraphCamera.MinScale"/> tabanına kilitli olduğu için uzaklaşarak da çözülemezdi). Artık
/// aralığın bir TABANI vardır (<see cref="MinNodeSpacing"/>) ve tuval genişliği en kalabalık katmandan TÜRETİLİR.
/// 880'e sığan graflarda (bir katmanda ≤24 düğüm) hem genişlik hem konumlar BİREBİR eskisi gibidir — regresyon
/// yoktur.</para>
///
/// <para><b>Tüm grafın aynı anda sığması HEDEF DEĞİLDİR:</b> 1000 düğümde tuval ~34.000px olur; kamera
/// (<see cref="GraphCamera"/>) zaten seçime/building frontier'ine odaklanıp oraya pan/zoom yapar. Alternatif —
/// sığdırmak için ölçeği düşürmek — düğümleri okunamaz hâle getirirdi; katmanı çok sütuna sarmak ise katman =
/// yatay sıra sözleşmesini (design-v1 §2.3) bozardı.</para>
/// </summary>
public static class GraphLayout
{
    /// <summary>Tuvalin TABAN (asgari) genişliği — prototipin sabit değeri. Bir katman bu genişliğe sığdığı
    /// sürece yerleşim birebir prototiptekidir; sığmadığında tuval <see cref="Compute"/> içinde büyütülür.</summary>
    public const double CanvasWidth = 880.0;
    public const double RowHeight = 96.0;
    public const double TopMargin = 46.0;
    public const double BottomMargin = 58.0;
    public const double MaxNodeSpacing = 96.0;
    /// <summary>Sıranın tuval kenarlarına bırakacağı toplam pay (prototip: <c>(W - 70) / max(1, n - 0.5)</c>).</summary>
    public const double SideInset = 70.0;

    /// <summary>Düğüm karesinin kenarı (design-v1 §2.3 / DS <c>DependencyGraphNode</c> size=26).</summary>
    public const double NodeSize = 26.0;
    /// <summary>[G1] Kalabalık bir katmanda iki kare arasında bırakılan asgari boşluk.</summary>
    public const double NodeGap = 8.0;
    /// <summary>[G1] Düğüm aralığının TABANI: kare + boşluk. Bunun altına inilseydi kareler üst üste binerdi —
    /// tuval bu yüzden daralmak yerine genişler (bkz. <see cref="Compute"/>).</summary>
    public const double MinNodeSpacing = NodeSize + NodeGap;
    /// <summary>Düğüm karesinin köşe yarıçapı (DS <c>--radius-sm</c> = 4px).</summary>
    public const double NodeCornerRadius = 4.0;
    /// <summary>Kenarın düğüm merkezinden başlayacağı dikey pay (prototip: <c>A.y + 15</c> / <c>B.y - 15</c>).</summary>
    public const double EdgeStubY = 15.0;
    /// <summary>Bezier kontrol noktalarının dikey uzaklığı (prototip: <c>±54</c>).</summary>
    public const double EdgeControlY = 54.0;

    /// <summary>Düğüm hücresinin genişliği = etiketin azami genişliği (DS: <c>maxWidth: size * 3.4</c>).</summary>
    public const double NodeCellWidth = NodeSize * 3.4;
    /// <summary>Kare ile etiket arasındaki boşluk (DS <c>gap: 5</c>).</summary>
    public const double LabelGap = 5.0;
    /// <summary>[G2] 10px mono etiket satırının yükseklik ÜST SINIRI — yalnız cull sınırlarını (GraphCulling)
    /// hesaplamak için kullanılır, yerleşimi etkilemez. Gerçek satır kutusundan büyük tutulur ki cull bir düğümü
    /// erken atmasın.</summary>
    public const double LabelHeight = 14.0;

    /// <summary>
    /// [G2/LOD · fix round 1] Bir katmanın ETİKETLERİ kurulur mu: komşu iki etiket <b>gerçekten</b> örtüşüyor
    /// mu.
    ///
    /// <para>Etiketler düğüm merkezinde ortalanır, dolayısıyla iki komşu etiket ancak
    /// <c>aralık &lt; çizilen genişlik</c> olduğunda üst üste biner. <paramref name="widestLabelWidth"/> o
    /// katmanın EN GENİŞ etiketinin <b>çizilen</b> (ölçülen) genişliğidir — <see cref="NodeCellWidth"/>
    /// kelepçesi DEĞİL. Kelepçe yalnız bir üst sınırdır (etiket orada <c>CharacterEllipsis</c> ile kırpılır);
    /// onu eşik olarak kullanmak, kısa adlı graflarda hiç örtüşmeyen etiketleri de düşürürdü
    /// (fix round 1 · A1). Ölçüm <see cref="GraphLabelMetrics"/>'tedir; burası saf karşılaştırmadır.</para>
    ///
    /// <para>Etiket düşen düğüm anonim kalmaz: o düğüme proje adını veren bir tooltip kurulur
    /// (<c>GraphView.BuildNodeVisual</c>).</para>
    ///
    /// <para><b>[sinema]</b> Bu, aynı kararın <b>ölçek 1</b> hâlidir: kamera yakınlaştıkça aralık EKRANDA büyür
    /// ve etiket sığmaya başlar — genel kural <see cref="LabelVisibleAtScale"/>'dedir, burası ona delege eder
    /// (kopya YASAK).</para>
    /// </summary>
    public static bool LabelsFit(double spacing, double widestLabelWidth) =>
        LabelVisibleAtScale(spacing, widestLabelWidth, 1.0, currentlyVisible: false);

    /// <summary>[sinema] Gizli bir etiketin belirme eşiği (oran = aralık×ölçek / en geniş etiket).</summary>
    public const double LabelShowRatio = 1.0;
    /// <summary>[sinema] Görünür bir etiketin tutunma eşiği — histerezis bandı titremeyi önler (spec §3.3).</summary>
    public const double LabelHideRatio = 0.85;

    /// <summary>[sinema] Etiket verilen kamera hedef ölçeğinde görünür mü. Histerezisli: gizliyken
    /// <see cref="LabelShowRatio"/>, görünürken <see cref="LabelHideRatio"/> eşiği geçerlidir — aksi halde eşiğin
    /// tam üzerindeki bir katman, kameranın her küçük yeniden hedeflemesinde etiketlerini yakıp söndürürdü.</summary>
    public static bool LabelVisibleAtScale(
        double spacing, double widestLabelWidth, double scale, bool currentlyVisible) =>
        spacing * scale >= widestLabelWidth * (currentlyVisible ? LabelHideRatio : LabelShowRatio);

    /// <summary>[G1] <paramref name="count"/> düğümlü bir katmanın düğüm aralığı. Prototipin formülü TABAN
    /// tuvalden hesaplanır (<c>(880-70)/(n-0.5)</c>) ve <see cref="MinNodeSpacing"/>–<see cref="MaxNodeSpacing"/>
    /// bandına kıstırılır. Taban tuvalden hesaplanması ŞART: aksi halde genişleyen tuval formülü besleyip
    /// aralığı geri şişirirdi (özyineleme) ve 880'e sığan graflar da eskisinden farklı çıkardı.</summary>
    public static double NodeSpacingFor(int count) =>
        Math.Clamp((CanvasWidth - SideInset) / Math.Max(1, count - 0.5), MinNodeSpacing, MaxNodeSpacing);

    /// <summary>[G1] <paramref name="count"/> düğümlük bir sıranın gerektirdiği tuval genişliği — aralık
    /// formülünün TERSİ, dolayısıyla sıra tuvalin içinde kalır ve 880'e sığan katmanlarda sonuç tam 880'dir.</summary>
    private static double CanvasWidthFor(int count) =>
        NodeSpacingFor(count) * Math.Max(1, count - 0.5) + SideInset;

    public static GraphLayoutResult Compute(IReadOnlyList<GraphNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var counts = new Dictionary<int, int>();
        foreach (var n in nodes)
            counts[n.Layer] = counts.GetValueOrDefault(n.Layer) + 1;

        // [G1] Tuval en kalabalık katmandan TÜRETİLİR — taban 880. Sözlük gezinme sırası sonucu etkilemez (max).
        double width = CanvasWidth;
        var spacings = new Dictionary<int, double>(counts.Count);
        foreach (var (layer, count) in counts)
        {
            spacings[layer] = NodeSpacingFor(count);
            width = Math.Max(width, CanvasWidthFor(count));
        }

        var indexInLayer = new Dictionary<int, int>();
        var positions = new Dictionary<string, Point>(nodes.Count, StringComparer.Ordinal);
        int maxLayer = 0;

        foreach (var node in nodes)
        {
            int i = indexInLayer.GetValueOrDefault(node.Layer);
            indexInLayer[node.Layer] = i + 1;
            int n = counts[node.Layer];
            positions[node.Name] = new Point(
                width / 2 + (i - (n - 1) / 2.0) * spacings[node.Layer],
                TopMargin + node.Layer * RowHeight);
            maxLayer = Math.Max(maxLayer, node.Layer);
        }

        return new GraphLayoutResult(positions, width, TopMargin + maxLayer * RowHeight + BottomMargin, spacings);
    }

    /// <summary>Kenarın kübik bezier kontrol noktaları (saf) — prototip:
    /// <c>M A.x,A.y+15 C A.x,A.y+54  B.x,B.y-54  B.x,B.y-15</c>.</summary>
    public static EdgeCurveGeometry EdgeCurve(Point from, Point to) => new(
        new Point(from.X, from.Y + EdgeStubY),
        new Point(from.X, from.Y + EdgeControlY),
        new Point(to.X, to.Y - EdgeControlY),
        new Point(to.X, to.Y - EdgeStubY));

    /// <summary>Kenarın DONMUŞ <see cref="StreamGeometry"/>'si — statü değişiminde yalnız kalem/dash güncellenir,
    /// geometri yeniden inşa EDİLMEZ (feasibility §3.5).</summary>
    public static Geometry BuildEdgeGeometry(Point from, Point to)
    {
        var curve = EdgeCurve(from, to);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(curve.Start, isFilled: false, isClosed: false);
            ctx.BezierTo(curve.Control1, curve.Control2, curve.End, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }
}
