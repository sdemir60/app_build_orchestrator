using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3 "Koşu yaşam döngüsü — soluk/parlak sistemi" — prototype/app/BuildApp.jsx satır
/// 421-429'un SAF portu. Sıra bağlayıcıdır: seçim &gt; koşu &gt; hover.
/// </summary>
public class GraphNodeOpacityTests
{
    private static double Op(
        GraphStatus status, GraphRunPhase phase,
        bool selection = false, bool focus = false, bool hover = false)
        => GraphNodeOpacity.Resolve(status, phase, selection, focus, hover);

    /// <summary>idle/boot/sync ve koşu bittikten sonra: TÜMÜ tam opak (§2.3).</summary>
    [Theory]
    [InlineData(GraphStatus.Discovered)]
    [InlineData(GraphStatus.Queued)]
    [InlineData(GraphStatus.Building)]
    [InlineData(GraphStatus.Succeeded)]
    [InlineData(GraphStatus.Failed)]
    [InlineData(GraphStatus.Skipped)]
    [InlineData(GraphStatus.Cycle)]
    public void Everything_is_fully_opaque_while_the_graph_is_idle(GraphStatus status)
        => Assert.Equal(1.0, Op(status, GraphRunPhase.Idle), 6);

    /// <summary>Koşu başlayınca graf soluklaşır: queued/discovered 0.13, yalnız derlenenler tam opak (§2.3).</summary>
    [Fact]
    public void A_running_graph_fades_the_untouched_nodes_to_thirteen_percent_and_keeps_the_building_ones_bright()
    {
        Assert.Equal(0.13, Op(GraphStatus.Queued, GraphRunPhase.Running), 6);
        Assert.Equal(0.13, Op(GraphStatus.Discovered, GraphRunPhase.Running), 6);
        Assert.Equal(1.0, Op(GraphStatus.Building, GraphRunPhase.Running), 6);
    }

    /// <summary>Biten proje sonuç rengine döner ve (bekleme + sönmeden sonra) 0.2'de kalır (§2.3).</summary>
    [Theory]
    [InlineData(GraphStatus.Succeeded)]
    [InlineData(GraphStatus.Failed)]
    [InlineData(GraphStatus.Skipped)]
    [InlineData(GraphStatus.Cycle)]
    public void A_finished_node_settles_at_twenty_percent_while_the_run_continues(GraphStatus status)
        => Assert.Equal(0.2, Op(status, GraphRunPhase.Running), 6);

    /// <summary>AYIRT EDİCİ: seçim koşu kararını EZER — odak kümesi tam opak, geri kalan HER ŞEY 0.1 (§2.3).
    /// Sıra ters olsaydı koşarken seçilen bir queued düğüm 0.13'te kalırdı.</summary>
    [Fact]
    public void A_selection_overrides_the_run_system_entirely()
    {
        Assert.Equal(1.0, Op(GraphStatus.Queued, GraphRunPhase.Running, selection: true, focus: true), 6);
        Assert.Equal(0.1, Op(GraphStatus.Building, GraphRunPhase.Running, selection: true, focus: false), 6);
        Assert.Equal(0.1, Op(GraphStatus.Succeeded, GraphRunPhase.Idle, selection: true, focus: false), 6);
    }

    /// <summary>Hover her şeyi ezer — soluk moddayken bile opaklık 1 (§2.3 "Hover").</summary>
    [Fact]
    public void Hover_wins_over_everything_including_the_selection_dim()
    {
        Assert.Equal(1.0, Op(GraphStatus.Queued, GraphRunPhase.Running, hover: true), 6);
        Assert.Equal(1.0, Op(GraphStatus.Queued, GraphRunPhase.Running, selection: true, focus: false, hover: true), 6);
        Assert.Equal(1.0, Op(GraphStatus.Succeeded, GraphRunPhase.Running, hover: true), 6);
    }

    /// <summary>§2.3'ün sayıları — birinin sessizce kayması bu testi düşürür.</summary>
    [Fact]
    public void The_opacity_and_timing_numbers_are_pinned_to_their_spec_values()
    {
        Assert.Equal(0.13, GraphNodeOpacity.RunDim, 6);
        Assert.Equal(0.2, GraphNodeOpacity.Finished, 6);
        Assert.Equal(0.1, GraphNodeOpacity.Unfocused, 6);
        Assert.Equal(2400.0, GraphNodeOpacity.HoldMs, 6);
        Assert.Equal(700.0, GraphNodeOpacity.FadeMs, 6);
        Assert.Equal(280.0, GraphNodeOpacity.GlideMs, 6);
    }
}
