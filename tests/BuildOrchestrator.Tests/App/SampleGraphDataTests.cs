using BuildOrchestrator.App.Spikes;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [It-4a Foundation] SampleGraphData: design-v1 build-data.js'ten port edilen 36-proje OSYS grafının veri
/// bütünlüğü — geniş, elle yazılmış bir port (36 düğüm) sessizce bozulmasın diye (yazım hatası, eksik/kopuk
/// bağımlılık referansı) birkaç yapısal doğrulama.
/// </summary>
public class SampleGraphDataTests
{
    [Fact]
    public void Ports_exactly_36_projects_across_6_layers()
    {
        Assert.Equal(36, SampleGraphData.Nodes.Count);
        Assert.Equal(6, SampleGraphData.Layers.Count);
        Assert.Equal([0, 1, 2, 3, 4, 5], SampleGraphData.Layers.Select(l => l.Id));
    }

    [Fact]
    public void All_node_names_are_unique()
    {
        var names = SampleGraphData.Nodes.Select(n => n.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Every_dependency_reference_resolves_to_an_existing_node()
    {
        var names = SampleGraphData.Nodes.Select(n => n.Name).ToHashSet();
        foreach (var node in SampleGraphData.Nodes)
        foreach (var dep in node.Dependencies)
            Assert.True(names.Contains(dep), $"{node.Name} bilinmeyen bağımlılığa işaret ediyor: {dep}");
    }

    [Fact]
    public void Layer_0_root_projects_have_no_dependencies()
    {
        var l0 = SampleGraphData.Nodes.Where(n => n.Layer == 0).ToList();
        Assert.Equal(3, l0.Count);
        Assert.All(l0.Where(n => n.Name != "OSYS.Base"), n => Assert.Contains("OSYS.Base", n.Dependencies));
        Assert.Empty(SampleGraphData.Nodes.Single(n => n.Name == "OSYS.Base").Dependencies);
    }

    [Fact]
    public void Edges_count_matches_the_total_dependency_count_and_points_dependency_to_dependent()
    {
        int expectedEdgeCount = SampleGraphData.Nodes.Sum(n => n.Dependencies.Count);
        Assert.Equal(expectedEdgeCount, SampleGraphData.Edges.Count);
        Assert.Contains(SampleGraphData.Edges, e => e.From == "OSYS.Base" && e.To == "OSYS.Common.Contracts");
    }

    [Fact]
    public void Known_dirty_and_failing_projects_match_build_data_js_source() // Sales.Core + Web.Portal kasıtlı hata (kaynak: build-data.js)
    {
        var salesCore = SampleGraphData.Nodes.Single(n => n.Name == "OSYS.Sales.Core");
        var webPortal = SampleGraphData.Nodes.Single(n => n.Name == "OSYS.Web.Portal");
        Assert.True(salesCore.Dirty);
        Assert.True(salesCore.Fails);
        Assert.True(webPortal.Dirty);
        Assert.True(webPortal.Fails);
        Assert.Equal(7, SampleGraphData.Nodes.Count(n => n.Dirty)); // Domain.Service/Parts, Sales.Core, Reporting.Core, Server.Api, Web.Portal, Client.Sales
        Assert.Equal(2, SampleGraphData.Nodes.Count(n => n.Fails));
    }

    [Theory]
    [InlineData("OSYS.Sales.Core", "Sales.Core")]
    [InlineData("OSYS.Base", "Base")]
    public void ShortName_strips_the_OSYS_prefix(string full, string expected) =>
        Assert.Equal(expected, SampleGraphData.ShortName(full));
}
