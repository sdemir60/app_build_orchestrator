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
    public void The_design_sized_36_node_graph_still_lays_out_on_the_original_880px_canvas()
    {
        // [G1] Görsel otorite (design-v1 §2.3 / prototip) 36 düğüm / 6 katmandır. Bu boyutta en kalabalık katman
        // 9 düğümdür → aralık (880-70)/8.5 ≈ 95.3px, tabana (34px) hiç yaklaşmaz. Tuval de 880'de kalır: BUGÜNKÜ
        // graf görünümü ölçek düzeltmesinden ETKİLENMEZ.
        var (nodes, _) = SyntheticGraph.Build(36, layerCount: 6, avgFanIn: 1.6);

        var layout = GraphLayout.Compute(nodes);

        Assert.Equal(880.0, layout.Width);
        var fattest = nodes.GroupBy(n => n.Layer).OrderByDescending(g => g.Count()).First().ToList();
        double spacing = layout.Positions[fattest[1].Name].X - layout.Positions[fattest[0].Name].X;
        Assert.Equal((GraphLayout.CanvasWidth - GraphLayout.SideInset) / (fattest.Count - 0.5), spacing, 10);
        Assert.True(spacing > GraphLayout.MinNodeSpacing);
    }

    // ---------------------------------------------------------------- [G2/It-5] LOD (etiket eşiği)

    [Fact]
    public void The_label_LOD_threshold_is_the_drawn_label_width_not_the_max_width_clamp()
    {
        // [G2 fix round 1 · A1] Eşik, etiketin ÇİZİLEN genişliğidir; hücre kelepçesi (88,4px) DEĞİL. Kelepçe
        // yalnız bir üst sınırdır — onu eşik saymak, kısa adlı graflarda hiç örtüşmeyen etiketleri düşürürdü.
        Assert.True(GraphLayout.LabelsFit(spacing: 40, widestLabelWidth: 40));      // tam sınır: örtüşme YOK
        Assert.False(GraphLayout.LabelsFit(spacing: 39.999, widestLabelWidth: 40)); // altı: örtüşür → kurulmaz
        // Kelepçenin ALTINDA kalan kısa etiketler dar aralıkta da sığar (eski eşik bunları düşürürdü).
        Assert.True(GraphLayout.LabelsFit(spacing: 40, widestLabelWidth: 24));
        Assert.False(GraphLayout.LabelsFit(spacing: 40, widestLabelWidth: GraphLayout.NodeCellWidth));
    }

    [StaFact]
    public void The_measured_label_width_is_the_real_drawn_width_and_never_exceeds_the_cell_clamp()
    {
        // Ölçüm gerçek advance-width matematiğinden gelir (TrackedGlyphs, T57) — uzun ad daha geniştir ve
        // hücre kelepçesini asla aşmaz (etiket orada CharacterEllipsis ile kırpılır).
        var mono = DsResources.MonoFontFamily;
        double shortName = GraphLabelMetrics.WidestLabelWidth(["Api"], mono);
        double longName = GraphLabelMetrics.WidestLabelWidth(["Domain.Vehicle.Registration"], mono);

        Assert.True(shortName > 0, "kısa ad ölçülemedi");
        Assert.True(shortName < longName, $"uzun ad daha dar ölçüldü: {shortName} vs {longName}");
        Assert.True(shortName < GraphLayout.NodeCellWidth, $"3 harflik ad kelepçeye dayandı: {shortName}");
        Assert.Equal(GraphLayout.NodeCellWidth, longName, 10); // kelepçeye oturur
        // Katmanın EN UZUN adı seçilir (katman başına tek ölçüm).
        Assert.Equal(longName, GraphLabelMetrics.WidestLabelWidth(["Api", "Domain.Vehicle.Registration", "Base"], mono), 10);
        // Çözülemeyen bir aile → kelepçeye (muhafazakâr) düşer, çökmez.
        Assert.Null(GraphLabelMetrics.TryMeasure("Api", new FontFamily("#No Such Family")));
    }

    [StaFact] // GlyphTypeface çözümü (bkz. kardeş ölçüm testi)
    public void A_short_named_layer_of_twelve_keeps_its_labels_because_they_do_not_actually_overlap()
    {
        // [A1] 12 düğümlük katmanda aralık (880−70)/11,5 ≈ 70,4px. Kısa adlarda (ör. "Api" ≈ 18px) etiketler
        // HİÇ örtüşmez ⇒ düşmemeli. Eski (kelepçeye dayalı) eşik bunları düşürürdü.
        var mono = DsResources.MonoFontFamily;
        double spacing = GraphLayout.NodeSpacingFor(12);
        Assert.True(GraphLayout.LabelsFit(spacing, GraphLabelMetrics.WidestLabelWidth(["Api"], mono)));
        // Aynı katman UZUN adlarla gerçekten örtüşür ⇒ düşer.
        Assert.False(GraphLayout.LabelsFit(spacing, GraphLabelMetrics.WidestLabelWidth(["Domain.Vehicle.Registration"], mono)));
    }

    [Fact]
    public void The_layout_reports_the_spacing_of_every_layer_so_the_LOD_decision_has_a_single_source()
    {
        var layout = GraphLayout.Compute(SampleNodes());

        Assert.Equal(GraphLayout.NodeSpacingFor(2), layout.LayerSpacing[0], 10);
        Assert.Equal(GraphLayout.NodeSpacingFor(1), layout.LayerSpacing[1], 10);
        // Aralık, konumlardan okunanla AYNI olmalı (iki ayrı hesap değil, tek kaynak).
        double measured = layout.Positions["OSYS.Common.Contracts"].X - layout.Positions["OSYS.Base"].X;
        Assert.Equal(Math.Abs(measured), layout.LayerSpacing[0], 10);
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
