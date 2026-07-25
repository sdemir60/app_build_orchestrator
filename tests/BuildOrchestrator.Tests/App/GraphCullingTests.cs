using System.Windows;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T51/G2] <see cref="GraphCulling"/> — viewport cull'un SAF aritmetiği (WPF'siz). Kameranın gösterdiği dünya
/// dikdörtgeni, düğüm/kenar sınırları ve pop-in'i önleyen pay burada pinlenir; WPF kablajı
/// <see cref="GraphCullTests"/>'te.
/// </summary>
public class GraphCullingTests
{
    [Fact]
    public void The_visible_world_rect_is_the_inverse_of_the_camera_transform_plus_a_row_of_margin()
    {
        // Kamera bir RenderTransform'dur: dünya noktası p ekranda p·scale + t'ye düşer.
        var camera = new CameraTransform(Scale: 0.5, Tx: -100, Ty: -40);

        var visible = GraphCulling.VisibleWorldRect(new Size(600, 400), camera);

        double margin = GraphCulling.MarginPx;
        Assert.Equal(200 - margin, visible.Left, 10);   // (0 − (−100)) / 0.5
        Assert.Equal(80 - margin, visible.Top, 10);     // (0 − (−40)) / 0.5
        Assert.Equal(1200 + 2 * margin, visible.Width, 10);  // 600 / 0.5
        Assert.Equal(800 + 2 * margin, visible.Height, 10);  // 400 / 0.5
        Assert.Equal(GraphLayout.RowHeight, GraphCulling.MarginPx); // pay = bir satır aralığı
    }

    [Fact]
    public void An_unmeasured_panel_or_a_zero_scale_camera_yields_an_empty_rect_instead_of_a_bogus_one()
    {
        // Panel henüz ölçülmemişken (veya kamera hiç hesaplanmamışken) "her şey görünür" DEMEK yanlış olurdu;
        // çağıran o turda hiçbir şey materyalize etmez ve ilk gerçek SizeChanged'de yeniden sorar.
        Assert.True(GraphCulling.VisibleWorldRect(new Size(0, 0), new CameraTransform(1, 0, 0)).IsEmpty);
        Assert.True(GraphCulling.VisibleWorldRect(new Size(600, 400), default).IsEmpty);
    }

    [Fact]
    public void Node_bounds_cover_the_label_cell_not_just_the_26px_square()
    {
        // Cull kararı ETİKET HÜCRESİNE göre verilir: kare ekrandan çıkmış ama etiketi hâlâ görünür olabilir.
        var bounds = GraphCulling.NodeBounds(new Point(500, 300));

        Assert.Equal(500 - GraphLayout.NodeCellWidth / 2, bounds.Left, 10);
        Assert.Equal(GraphLayout.NodeCellWidth, bounds.Width, 10);
        Assert.Equal(300 - GraphLayout.NodeSize / 2, bounds.Top, 10);
        Assert.Equal(
            GraphLayout.NodeSize + GraphLayout.LabelGap + GraphLayout.LabelHeight, bounds.Height, 10);
    }

    [StaFact] // donmuş StreamGeometry üretir (kardeş GraphLayoutTests ile aynı gerekçe)
    public void Edge_bounds_contain_the_whole_curve_because_a_bezier_never_leaves_its_control_hull()
    {
        var from = new Point(100, 200);
        var to = new Point(400, 400);

        var bounds = GraphCulling.EdgeBounds(from, to);
        var curve = GraphLayout.EdgeCurve(from, to);

        foreach (var p in new[] { curve.Start, curve.Control1, curve.Control2, curve.End })
            Assert.True(bounds.Contains(p), $"kontrol noktası {p} sınırların dışında");
        // Eğrinin GERÇEK sınırları (donmuş geometriden) da içeride olmalı — dışbükey zarf garantisi.
        var geometryBounds = GraphLayout.BuildEdgeGeometry(from, to).Bounds;
        Assert.True(bounds.Contains(geometryBounds), $"eğri sınırları {geometryBounds} taşıyor: {bounds}");
    }
}
