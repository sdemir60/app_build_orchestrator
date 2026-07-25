using System.Windows;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T63] <see cref="GraphLayout"/> — design-v1 §2.3 katmanlı DAG yerleşiminin SAF portu (prototype/app/build-data.js
/// <c>GRAPH</c> IIFE'si): her katman bir yatay sıra (satır aralığı 96px), düğüm aralığı ≤96px, kenarlar yukarıdan
/// aşağı kübik bezier.
/// </summary>
public class GraphLayoutTests
{
    private static IReadOnlyList<GraphNode> SampleNodes() =>
    [
        new("OSYS.Base", 0, GraphStatus.Discovered),
        new("OSYS.Common.Contracts", 0, GraphStatus.Discovered),
        new("OSYS.Data.Core", 1, GraphStatus.Discovered),
    ];

    [Fact]
    public void Layers_are_horizontal_rows_96px_apart_starting_at_the_46px_top_margin()
    {
        var layout = GraphLayout.Compute(SampleNodes());

        Assert.Equal(GraphLayout.TopMargin, layout.Positions["OSYS.Base"].Y);
        Assert.Equal(GraphLayout.TopMargin + GraphLayout.RowHeight, layout.Positions["OSYS.Data.Core"].Y);
        Assert.Equal(96.0, GraphLayout.RowHeight);
    }

    [Fact]
    public void Nodes_in_a_layer_are_spread_symmetrically_around_the_canvas_center()
    {
        var layout = GraphLayout.Compute(SampleNodes());

        double a = layout.Positions["OSYS.Base"].X;
        double b = layout.Positions["OSYS.Common.Contracts"].X;
        Assert.Equal(GraphLayout.CanvasWidth / 2, (a + b) / 2, 10);
        // Tek düğümlü katman tam ortada.
        Assert.Equal(GraphLayout.CanvasWidth / 2, layout.Positions["OSYS.Data.Core"].X, 10);
    }

    [Fact]
    public void Node_spacing_never_exceeds_96px()
    {
        var layout = GraphLayout.Compute(SampleNodes());

        double a = layout.Positions["OSYS.Base"].X;
        double b = layout.Positions["OSYS.Common.Contracts"].X;
        Assert.True(Math.Abs(b - a) <= GraphLayout.MaxNodeSpacing);
        Assert.Equal(96.0, GraphLayout.MaxNodeSpacing);
    }

    [Fact]
    public void A_crowded_layer_shrinks_the_spacing_so_the_row_stays_inside_the_canvas()
    {
        // 12 düğümlü tek katman: 96px aralık (880-70)/11.5 = 70.4'e düşer.
        var nodes = Enumerable.Range(0, 12).Select(i => new GraphNode($"P{i}", 0, GraphStatus.Discovered)).ToList();

        var layout = GraphLayout.Compute(nodes);

        double spacing = layout.Positions["P1"].X - layout.Positions["P0"].X;
        Assert.Equal((GraphLayout.CanvasWidth - GraphLayout.SideInset) / (12 - 0.5), spacing, 10);
        Assert.True(spacing < GraphLayout.MaxNodeSpacing);
    }

    [Fact]
    public void Canvas_size_is_880_wide_and_covers_every_layer_plus_the_bottom_margin()
    {
        var layout = GraphLayout.Compute(SampleNodes());

        Assert.Equal(880.0, layout.Width);
        Assert.Equal(GraphLayout.TopMargin + 1 * GraphLayout.RowHeight + GraphLayout.BottomMargin, layout.Height);
    }

    // ---------------------------------------------------------------- [G1/It-5] ölçek

    [Fact]
    public void A_500_node_layer_keeps_at_least_one_node_width_plus_a_gap_between_neighbouring_centres()
    {
        // [G1] It-5 öncesi: tuval 880'e PİNLİYDİ → 500 düğümlük katmanda aralık (880-70)/499.5 ≈ 1.6px olur ve
        // 26px'lik kareler fiziksel olarak üst üste binerdi. Aralığın tabanı artık düğüm genişliği + boşluktur.
        var nodes = Enumerable.Range(0, 500).Select(i => new GraphNode($"P{i}", 0, GraphStatus.Discovered)).ToList();

        var layout = GraphLayout.Compute(nodes);

        double spacing = layout.Positions["P1"].X - layout.Positions["P0"].X;
        Assert.Equal(GraphLayout.NodeSize + GraphLayout.NodeGap, GraphLayout.MinNodeSpacing);
        Assert.True(spacing >= GraphLayout.MinNodeSpacing,
            $"500 düğümlük katmanda aralık {spacing} px — taban {GraphLayout.MinNodeSpacing} px.");
        // Aralık TÜM komşular için aynıdır (katman-içi eşit dağılım) — uçlar da kontrol edilir.
        Assert.Equal(spacing, layout.Positions["P499"].X - layout.Positions["P498"].X, 10);
    }

    [Fact]
    public void The_canvas_grows_with_the_widest_layer_so_a_crowded_row_still_fits_inside_it()
    {
        var nodes = Enumerable.Range(0, 500).Select(i => new GraphNode($"P{i}", 0, GraphStatus.Discovered)).ToList();

        var layout = GraphLayout.Compute(nodes);

        Assert.Equal(GraphLayout.MinNodeSpacing * (500 - 0.5) + GraphLayout.SideInset, layout.Width, 10);
        Assert.True(layout.Width > GraphLayout.CanvasWidth);
        // Sıra tuvalin İÇİNDE: en sol/en sağ karenin kenarı tuvali taşmaz.
        double left = layout.Positions["P0"].X - GraphLayout.NodeSize / 2;
        double right = layout.Positions["P499"].X + GraphLayout.NodeSize / 2;
        Assert.True(left >= 0, $"sıra soldan taşıyor: {left}");
        Assert.True(right <= layout.Width, $"sıra sağdan taşıyor: {right} > {layout.Width}");
    }

    [Fact]
    public void A_layer_that_still_fits_the_880_canvas_is_laid_out_exactly_as_before()
    {
        // [G1] Regresyon kilidi: bugünkü tipik graf boyutlarında (onlarca düğüm) GÖRÜNÜM BİREBİR AYNI kalmalı —
        // 24 düğüm 880'lik tuvalin tam sınırıdır (aralık (880-70)/23.5 ≈ 34.47 ≥ taban).
        var nodes = Enumerable.Range(0, 24).Select(i => new GraphNode($"P{i}", 0, GraphStatus.Discovered)).ToList();

        var layout = GraphLayout.Compute(nodes);

        Assert.Equal(880.0, layout.Width);
        double expectedSpacing = (GraphLayout.CanvasWidth - GraphLayout.SideInset) / (24 - 0.5);
        for (int i = 0; i < 24; i++)
            Assert.Equal(
                GraphLayout.CanvasWidth / 2 + (i - (24 - 1) / 2.0) * expectedSpacing,
                layout.Positions[$"P{i}"].X, 10);
    }

    [Fact]
    public void Compute_on_an_empty_node_set_still_returns_a_usable_canvas()
    {
        var layout = GraphLayout.Compute([]);

        Assert.Empty(layout.Positions);
        Assert.Equal(880.0, layout.Width);
        Assert.True(layout.Height > 0);
    }

    // ---------------------------------------------------------------- kenar geometrisi

    [Fact]
    public void Edge_control_points_form_a_top_down_cubic_bezier_between_the_two_node_stubs()
    {
        var curve = GraphLayout.EdgeCurve(from: new Point(100, 200), to: new Point(400, 400));

        Assert.Equal(new Point(100, 200 + GraphLayout.EdgeStubY), curve.Start);
        Assert.Equal(new Point(100, 200 + GraphLayout.EdgeControlY), curve.Control1);
        Assert.Equal(new Point(400, 400 - GraphLayout.EdgeControlY), curve.Control2);
        Assert.Equal(new Point(400, 400 - GraphLayout.EdgeStubY), curve.End);
    }

    [StaFact]
    public void Edge_geometry_is_a_frozen_stream_geometry_covering_the_curve()
    {
        var geometry = GraphLayout.BuildEdgeGeometry(new Point(100, 200), new Point(400, 400));

        Assert.IsType<StreamGeometry>(geometry);
        Assert.True(geometry.IsFrozen); // her frame'de yeniden inşa YOK (feasibility §3.5)
        var bounds = geometry.Bounds;
        Assert.Equal(100, bounds.Left, 6);
        Assert.Equal(400, bounds.Right, 6);
        Assert.Equal(200 + GraphLayout.EdgeStubY, bounds.Top, 6);
        Assert.Equal(400 - GraphLayout.EdgeStubY, bounds.Bottom, 6);
    }

    [Fact]
    public void The_common_prefix_is_stripped_from_node_labels_and_a_non_matching_name_is_left_intact()
    {
        // [D5] ShortLabel artık 2-arg: önek VERİ-TÜREVLİ (hardcode "OSYS." değil, bkz. GraphBinderTests) — burada
        // yalnız kırpma mekaniği pinlenir: önekle başlayan kırpılır, başlamayan aynen kalır.
        Assert.Equal("Domain.Vehicle", GraphNode.ShortLabel("OSYS.Domain.Vehicle", "OSYS."));
        Assert.Equal("Foo.Bar", GraphNode.ShortLabel("Foo.Bar", "OSYS."));
    }
}
