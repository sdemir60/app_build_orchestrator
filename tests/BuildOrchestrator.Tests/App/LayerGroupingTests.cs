using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T54] Katman gruplaması — <b>mimari kural: App'te katman regex'i YOKTUR</b>. Gruplama YALNIZ topolojinin
/// <see cref="ProjectNode.LayerName"/>/<see cref="ProjectNode.LayerIndex"/>'inden gelir (regex yalnız Core'da).
/// Saf/WPF'siz test.
/// </summary>
public class LayerGroupingTests
{
    private static ProjectNode Node(string id, string name, string? layerName, int? layerIndex) =>
        new(id, name, id, SolutionNames: [], Dependencies: [], BuildOrder: 0,
            LayerIndex: layerIndex, LayerName: layerName, InCycle: false, WillBuild: null);

    private static ProjectRowViewModel Row(string id, string name) =>
        new(id, name, ProjectRowState.Pending);

    [Fact]
    public void Rows_are_grouped_by_layer_name_from_the_topology_not_by_a_regex_in_the_app()
    {
        // Adları BENZEMEYEN iki proje (Api, Core) AYNI LayerName="Domain" ile aynı gruba; ad-tabanlı bir regex
        // bunları ASLA birleştiremezdi — gruplamanın kaynağının topoloji olduğunu pinler.
        var rows = new[] { Row(@"C:\p\Api.csproj", "Api"), Row(@"C:\p\Core.csproj", "Core"), Row(@"C:\p\Web.csproj", "Web") };
        var topology = new[]
        {
            Node(@"C:\p\Api.csproj", "Api", "Domain", 0),
            Node(@"C:\p\Core.csproj", "Core", "Domain", 0),
            Node(@"C:\p\Web.csproj", "Web", "App", 1),
        };

        var groups = LayerGrouping.Build(rows, topology);

        Assert.Equal(2, groups.Count);
        Assert.Equal("Domain", groups[0].Name);
        Assert.Equal(new[] { "Api", "Core" }, groups[0].Rows.Select(r => r.Name).ToArray());
        Assert.Equal("App", groups[1].Name);
        Assert.Equal(new[] { "Web" }, groups[1].Rows.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void No_layer_names_yields_a_single_unnamed_group_flat_build_order()
    {
        var rows = new[] { Row(@"C:\p\Api.csproj", "Api"), Row(@"C:\p\Web.csproj", "Web") };
        var topology = new[]
        {
            Node(@"C:\p\Api.csproj", "Api", layerName: null, layerIndex: null),
            Node(@"C:\p\Web.csproj", "Web", layerName: null, layerIndex: null),
        };

        var groups = LayerGrouping.Build(rows, topology);

        Assert.Single(groups);
        Assert.Null(groups[0].Name); // isimsiz → StickyLayerList'te başlıksız = düz build-order
        Assert.Equal(2, groups[0].Rows.Count);
    }

    [Fact]
    public void Empty_topology_falls_back_to_a_single_flat_group_in_build_order()
    {
        var rows = new[] { Row(@"C:\p\a.csproj", "a"), Row(@"C:\p\b.csproj", "b") };

        var groups = LayerGrouping.Build(rows, topology: []);

        Assert.Single(groups);
        Assert.Null(groups[0].Name);
        Assert.Equal(new[] { "a", "b" }, groups[0].Rows.Select(r => r.Name).ToArray());
    }
}
