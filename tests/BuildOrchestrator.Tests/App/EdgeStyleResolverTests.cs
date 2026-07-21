using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T63] <see cref="EdgeStyleResolver"/> — design-v1 §2.3 kenar renk/kalınlık/dash kuralının SAF portu
/// (prototype/app/BuildApp.jsx <c>GraphPanel</c> içindeki if-zinciri; WPF'ten tamamen bağımsız test edilir).
/// Dash birimi <b>StrokeThickness çarpanı</b>dır (feasibility §3.4 — WPF'te dash px DEĞİL).
/// </summary>
public class EdgeStyleResolverTests
{
    private static EdgeStyle Resolve(
        GraphStatus source, GraphStatus target,
        bool sourceHasDepIssue = false, bool touchesSelection = false, bool hasSelection = false)
        => EdgeStyleResolver.Resolve(source, sourceHasDepIssue, target, touchesSelection, hasSelection);

    // ---------------------------------------------------------------- varsayılan

    [Fact]
    public void Default_edge_is_border_hairline_solid_and_not_flowing()
    {
        var style = Resolve(GraphStatus.Discovered, GraphStatus.Discovered);

        Assert.Equal("Brush.Border", style.BrushKey);
        Assert.Equal(1.0, style.Thickness);
        Assert.Equal(0.8, style.Opacity);
        Assert.Null(style.Dash);
        Assert.False(style.IsFlowing);
    }

    [Fact]
    public void Default_edge_dims_to_16_percent_while_something_else_is_selected()
    {
        var style = Resolve(GraphStatus.Discovered, GraphStatus.Discovered, hasSelection: true);

        Assert.Equal(0.16, style.Opacity);
    }

    // ---------------------------------------------------------------- hedef durumu

    [Fact]
    public void Target_building_is_amber_and_flows_with_the_4_7_dash()
    {
        var style = Resolve(GraphStatus.Succeeded, GraphStatus.Building);

        Assert.Equal("Brush.Amber", style.BrushKey);
        Assert.True(style.IsFlowing);
        Assert.Equal([4.0, 7.0], style.Dash);
        Assert.Equal(0.85, style.Opacity);
        Assert.Equal(1.0, style.Thickness);
    }

    [Fact]
    public void Target_building_dims_to_20_percent_while_something_else_is_selected()
        => Assert.Equal(0.2, Resolve(GraphStatus.Succeeded, GraphStatus.Building, hasSelection: true).Opacity);

    [Fact]
    public void Target_succeeded_uses_the_green_border_tone_solid()
    {
        var style = Resolve(GraphStatus.Succeeded, GraphStatus.Succeeded);

        Assert.Equal("Brush.StatusSuccessBorder", style.BrushKey);
        Assert.Null(style.Dash);
        Assert.False(style.IsFlowing);
    }

    [Fact]
    public void Target_failed_uses_the_red_border_tone_solid()
    {
        var style = Resolve(GraphStatus.Succeeded, GraphStatus.Failed);

        Assert.Equal("Brush.StatusFailBorder", style.BrushKey);
        Assert.Null(style.Dash);
        Assert.False(style.IsFlowing);
    }

    // ---------------------------------------------------------------- hatanın taşındığı dal

    [Fact]
    public void Error_carrying_branch_from_a_failed_source_is_red_and_statically_dashed_3_4()
    {
        var style = Resolve(GraphStatus.Failed, GraphStatus.Queued);

        Assert.Equal("Brush.StatusFailBorder", style.BrushKey);
        Assert.Equal([3.0, 4.0], style.Dash);
        Assert.False(style.IsFlowing);
        Assert.Equal(0.95, style.Opacity);
    }

    [Fact]
    public void Error_carrying_branch_from_a_dep_issue_source_is_red_and_statically_dashed_3_4()
    {
        var style = Resolve(GraphStatus.Skipped, GraphStatus.Queued, sourceHasDepIssue: true);

        Assert.Equal("Brush.StatusFailBorder", style.BrushKey);
        Assert.Equal([3.0, 4.0], style.Dash);
        Assert.False(style.IsFlowing);
    }

    [Fact]
    public void Error_carrying_branch_dims_to_30_percent_while_something_else_is_selected()
        => Assert.Equal(0.3, Resolve(GraphStatus.Failed, GraphStatus.Queued, hasSelection: true).Opacity);

    [Fact]
    public void An_error_branch_that_also_feeds_a_building_target_keeps_flowing_but_turns_red()
    {
        // JS: `if (bad) { ... if (!flow) { cls = undefined; dash = '3 4'; } }` — flow iken akış SÜRER, yalnız renk kırmızıya döner.
        var style = Resolve(GraphStatus.Failed, GraphStatus.Building);

        Assert.Equal("Brush.StatusFailBorder", style.BrushKey);
        Assert.True(style.IsFlowing);
        Assert.Equal([4.0, 7.0], style.Dash);
    }

    // ---------------------------------------------------------------- seçili düğüme değen kenar

    [Fact]
    public void An_edge_touching_the_selected_node_is_amber_1_6px_fully_opaque_and_solid()
    {
        var style = Resolve(GraphStatus.Succeeded, GraphStatus.Succeeded, touchesSelection: true, hasSelection: true);

        Assert.Equal("Brush.AmberBorder", style.BrushKey);
        Assert.Equal(1.6, style.Thickness);
        Assert.Equal(1.0, style.Opacity);
        Assert.Null(style.Dash);
        Assert.False(style.IsFlowing);
    }

    [Fact]
    public void A_touching_edge_that_would_flow_stops_flowing_when_it_is_not_an_error_branch()
    {
        // JS: `if (hot) { ... if (!bad) cls = undefined; }` — seçime değen sağlıklı kenar DÜZ amber olur.
        var style = Resolve(GraphStatus.Succeeded, GraphStatus.Building, touchesSelection: true, hasSelection: true);

        Assert.Equal("Brush.AmberBorder", style.BrushKey);
        Assert.False(style.IsFlowing);
        Assert.Null(style.Dash);
        Assert.Equal(1.6, style.Thickness);
    }

    [Fact]
    public void A_touching_error_branch_stays_red_at_1_6px_and_keeps_its_static_3_4_dash()
    {
        var style = Resolve(GraphStatus.Failed, GraphStatus.Queued, touchesSelection: true, hasSelection: true);

        Assert.Equal("Brush.StatusFailBorder", style.BrushKey);
        Assert.Equal(1.6, style.Thickness);
        Assert.Equal(1.0, style.Opacity);
        // A13.2: "1.6px seçili kenarda değerler BÖLÜNÜR" — desen çarpan-birimi olduğundan bölünmüş {3/1.6, 4/1.6}
        // MUTLAKTA yine 3px/4px çizer (bölünmeseydi 4.8/6.4px olurdu).
        Assert.Equal([3.0 / 1.6, 4.0 / 1.6], style.Dash);
        Assert.False(style.IsFlowing);
    }

    [Fact]
    public void A_touching_error_branch_into_a_building_target_flows_red_at_1_6px()
    {
        var style = Resolve(GraphStatus.Failed, GraphStatus.Building, touchesSelection: true, hasSelection: true);

        Assert.Equal("Brush.StatusFailBorder", style.BrushKey);
        Assert.Equal(1.6, style.Thickness);
        Assert.True(style.IsFlowing);
        Assert.Equal([4.0 / 1.6, 7.0 / 1.6], style.Dash);
    }

    [Fact]
    public void TouchesSelection_is_ignored_when_nothing_is_selected()
    {
        // JS: `const hot = selected && (...)` — seçim yokken "hot" hiç oluşmaz.
        var style = Resolve(GraphStatus.Succeeded, GraphStatus.Succeeded, touchesSelection: true, hasSelection: false);

        Assert.Equal(1.0, style.Thickness);
        Assert.Equal("Brush.StatusSuccessBorder", style.BrushKey);
    }

    // ------------------------------------------- dash birimi = thickness çarpanı + 1.6px bölmesi (A13.2)

    [Theory]
    [InlineData(EdgeStyleResolver.DefaultThickness)]
    [InlineData(EdgeStyleResolver.SelectedThickness)]
    public void The_flowing_dash_draws_the_same_absolute_4px_7px_pattern_at_both_thicknesses(double thickness)
    {
        // WPF'te dash birimi StrokeThickness ÇARPANI'dır → MUTLAK px = birim × kalınlık. A13.2: 1px'te birebir,
        // 1.6px seçili kenarda değerler BÖLÜNÜR ⇒ iki kalınlıkta da tasarımın `stroke-dasharray: 4 7`'si.
        var dash = thickness == EdgeStyleResolver.SelectedThickness
            ? EdgeStyleResolver.FlowDashThick
            : EdgeStyleResolver.FlowDash;

        Assert.Equal(4.0, dash[0] * thickness, 9);
        Assert.Equal(7.0, dash[1] * thickness, 9);
    }

    [Theory]
    [InlineData(EdgeStyleResolver.DefaultThickness)]
    [InlineData(EdgeStyleResolver.SelectedThickness)]
    public void The_static_error_dash_draws_the_same_absolute_3px_4px_pattern_at_both_thicknesses(double thickness)
    {
        // Statik desen HİÇ clock'a bağlanmaz — "tek clock" gerekçesi buraya uygulanmaz, bölme kuralı çıplaktır.
        var dash = thickness == EdgeStyleResolver.SelectedThickness
            ? EdgeStyleResolver.ErrorDashThick
            : EdgeStyleResolver.ErrorDash;

        Assert.Equal(3.0, dash[0] * thickness, 9);
        Assert.Equal(4.0, dash[1] * thickness, 9);
    }

    [Theory]
    [InlineData(EdgeStyleResolver.DefaultThickness, -22.0)]
    [InlineData(EdgeStyleResolver.SelectedThickness, -13.75)]
    public void The_flow_offset_is_two_periods_of_its_OWN_divided_pattern_so_one_clock_stays_phase_locked(
        double thickness, double expectedOffset)
    {
        var dash = thickness == EdgeStyleResolver.SelectedThickness
            ? EdgeStyleResolver.FlowDashThick
            : EdgeStyleResolver.FlowDash;
        double offset = EdgeStyleResolver.FlowDashOffsetFor(thickness);
        double period = dash[0] + dash[1];

        Assert.Equal(expectedOffset, offset, 9);
        // Her dal KENDİ (bölünmüş) deseninin tam 2 periyodunu kat eder ⇒ dikiş görünmez.
        Assert.Equal(-2.0, offset / period, 9);
        // ... ve ikisi de 0.9s'de AYNI 22px MUTLAK yolu alır ⇒ tek paylaşılan clock'un iki dalı faz-kilitli kalır.
        Assert.Equal(-EdgeStyleResolver.FlowTravelPx, offset * thickness, 9);
    }

    [Fact]
    public void Every_dash_pattern_is_a_stable_static_instance_so_the_style_fast_path_can_compare_by_reference()
    {
        // GraphView "stil değişmediyse fırça/dash/clock kablajına dokunma" hızlı yolu EdgeStyle record eşitliğine
        // dayanır; IReadOnlyList<double> için bu REFERANS eşitliğidir — her çağrıda yeni dizi üretilirse hızlı yol
        // sessizce ölür (her tick'te full binding refresh).
        Assert.Same(EdgeStyleResolver.FlowDash,
            Resolve(GraphStatus.Succeeded, GraphStatus.Building).Dash);
        Assert.Same(EdgeStyleResolver.FlowDashThick,
            Resolve(GraphStatus.Failed, GraphStatus.Building, touchesSelection: true, hasSelection: true).Dash);
        Assert.Same(EdgeStyleResolver.ErrorDash,
            Resolve(GraphStatus.Failed, GraphStatus.Queued).Dash);
        Assert.Same(EdgeStyleResolver.ErrorDashThick,
            Resolve(GraphStatus.Failed, GraphStatus.Queued, touchesSelection: true, hasSelection: true).Dash);

        // Aynı girdi iki kez çağrıldığında da AYNI örnek döner (record eşitliği bozulmaz).
        Assert.Equal(Resolve(GraphStatus.Succeeded, GraphStatus.Building),
                     Resolve(GraphStatus.Succeeded, GraphStatus.Building));
    }
}
