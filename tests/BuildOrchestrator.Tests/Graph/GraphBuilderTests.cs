using System.IO;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Graph;

namespace BuildOrchestrator.Tests.Graph;

public class GraphBuilderTests
{
    private static EvaluatedProject P(string id, string asm, string[] hints, string[] projRefs) =>
        new(id, asm, [], hints.Select(h => new RawHintPath(h, Path.GetFileName(h).ToLowerInvariant())).ToList(), projRefs, false);

    [Fact]
    public void HintPath_basename_maps_to_producer_edge()
    {
        var a = P("C:\\r\\A.csproj", "OSYS.A", ["..\\B\\OSYS.B.dll"], []);
        var b = P("C:\\r\\B.csproj", "OSYS.B", [], []);
        var producers = ProducerMapBuilder.Build([a, b]);
        var edges = GraphBuilder.BuildEdges([a, b], producers).Single(e => e.ProjectId == a.Path);
        Assert.Equal(["C:\\r\\B.csproj"], edges.Dependencies); // HintPath→producer
    }

    [Fact]
    public void ProjectReference_is_secondary_and_deduped_with_hintpath()
    {
        // A hem HintPath hem ProjectReference ile B'ye bağlı → tek edge
        var a = P("C:\\r\\A.csproj", "OSYS.A", ["..\\B\\OSYS.B.dll"], ["C:\\r\\B.csproj"]);
        var b = P("C:\\r\\B.csproj", "OSYS.B", [], []);
        var producers = ProducerMapBuilder.Build([a, b]);
        var edges = GraphBuilder.BuildEdges([a, b], producers).Single(e => e.ProjectId == a.Path);
        Assert.Single(edges.Dependencies);
    }

    [Fact]
    public void ambiguous_producer_is_excluded_deterministically()
    {
        var a = P("C:\\r\\A.csproj", "DUP", [], []);
        var b = P("C:\\r\\B.csproj", "DUP", [], []); // aynı AssemblyName
        var producers = ProducerMapBuilder.Build([a, b]);
        Assert.Contains("dup.dll", producers.AmbiguousDlls);
        Assert.False(producers.DllToProducer.ContainsKey("dup.dll"));
    }
}
