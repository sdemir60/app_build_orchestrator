using System.Windows;
using System.Windows.Media;

namespace BuildOrchestrator.App.Graph;

/// <summary>[T63] Yerleşim sonucu: düğüm MERKEZLERİ (ad → nokta) + graf tuvalinin ölçüsü.</summary>
public sealed record GraphLayoutResult(IReadOnlyDictionary<string, Point> Positions, double Width, double Height)
{
    public Size Size => new(Width, Height);
}

/// <summary>[T63] Bir kenarın kübik bezier'i (yukarıdan aşağı) — saf geometri, WPF nesnesi değil.</summary>
public readonly record struct EdgeCurveGeometry(Point Start, Point Control1, Point Control2, Point End);

/// <summary>
/// [T63] design-v1 §2.3 katmanlı DAG yerleşimi — prototype/app/build-data.js <c>GRAPH</c> IIFE'sinin SAF portu.
/// Her katman bir yatay sıra (satır aralığı 96px), düğüm aralığı ≤96px, tuval 880px geniş; kenarlar yukarıdan
/// aşağı kübik bezier.
/// </summary>
public static class GraphLayout
{
    public const double CanvasWidth = 880.0;
    public const double RowHeight = 96.0;
    public const double TopMargin = 46.0;
    public const double BottomMargin = 58.0;
    public const double MaxNodeSpacing = 96.0;
    /// <summary>Sıranın tuval kenarlarına bırakacağı toplam pay (prototip: <c>(W - 70) / max(1, n - 0.5)</c>).</summary>
    public const double SideInset = 70.0;

    /// <summary>Düğüm karesinin kenarı (design-v1 §2.3 / DS <c>DependencyGraphNode</c> size=26).</summary>
    public const double NodeSize = 26.0;
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

    public static GraphLayoutResult Compute(IReadOnlyList<GraphNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var counts = new Dictionary<int, int>();
        foreach (var n in nodes)
            counts[n.Layer] = counts.GetValueOrDefault(n.Layer) + 1;

        var indexInLayer = new Dictionary<int, int>();
        var positions = new Dictionary<string, Point>(nodes.Count, StringComparer.Ordinal);
        int maxLayer = 0;

        foreach (var node in nodes)
        {
            int i = indexInLayer.GetValueOrDefault(node.Layer);
            indexInLayer[node.Layer] = i + 1;
            int n = counts[node.Layer];
            double spacing = Math.Min(MaxNodeSpacing, (CanvasWidth - SideInset) / Math.Max(1, n - 0.5));
            positions[node.Name] = new Point(
                CanvasWidth / 2 + (i - (n - 1) / 2.0) * spacing,
                TopMargin + node.Layer * RowHeight);
            maxLayer = Math.Max(maxLayer, node.Layer);
        }

        return new GraphLayoutResult(positions, CanvasWidth, TopMargin + maxLayer * RowHeight + BottomMargin);
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
