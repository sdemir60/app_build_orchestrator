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
        Assert.Equal([3.0, 4.0], style.Dash);
        Assert.False(style.IsFlowing);
    }

    [Fact]
    public void A_touching_error_branch_into_a_building_target_flows_red_at_1_6px()
    {
        var style = Resolve(GraphStatus.Failed, GraphStatus.Building, touchesSelection: true, hasSelection: true);

        Assert.Equal("Brush.StatusFailBorder", style.BrushKey);
        Assert.Equal(1.6, style.Thickness);
        Assert.True(style.IsFlowing);
        Assert.Equal([4.0, 7.0], style.Dash);
    }

    [Fact]
    public void TouchesSelection_is_ignored_when_nothing_is_selected()
    {
        // JS: `const hot = selected && (...)` — seçim yokken "hot" hiç oluşmaz.
        var style = Resolve(GraphStatus.Succeeded, GraphStatus.Succeeded, touchesSelection: true, hasSelection: false);

        Assert.Equal(1.0, style.Thickness);
        Assert.Equal("Brush.StatusSuccessBorder", style.BrushKey);
    }

    // ---------------------------------------------------------------- dash birimi = thickness çarpanı (A13.2)

    [Fact]
    public void The_flow_offset_is_exactly_two_dash_periods_so_the_loop_is_seamless_at_every_thickness()
    {
        // WPF dash birimi StrokeThickness ÇARPANI'dır: desen {4,7} → periyot 11 çarpan-birimi, HANGİ kalınlıkta olursa
        // olsun. To=-22 tam 2 periyot ⇒ 1px'lik akan kenar da 1.6px'lik (hata dalı + seçime değen) akan kenar da AYNI
        // paylaşımlı clock'a bağlanabilir ve dikiş görünmez.
        double period = EdgeStyleResolver.FlowDash[0] + EdgeStyleResolver.FlowDash[1];

        Assert.Equal(11.0, period);
        Assert.Equal(-2.0, EdgeStyleResolver.FlowDashOffsetTo / period);
        Assert.Equal(0.0, EdgeStyleResolver.FlowDashOffsetTo % period);
    }

    [Fact]
    public void Flowing_edges_at_both_thicknesses_share_one_dash_pattern_instance_so_one_clock_serves_them_all()
    {
        var thin = Resolve(GraphStatus.Succeeded, GraphStatus.Building);
        var thick = Resolve(GraphStatus.Failed, GraphStatus.Building, touchesSelection: true, hasSelection: true);

        Assert.NotEqual(thin.Thickness, thick.Thickness);
        Assert.Same(EdgeStyleResolver.FlowDash, thin.Dash);
        Assert.Same(EdgeStyleResolver.FlowDash, thick.Dash);
    }
}
