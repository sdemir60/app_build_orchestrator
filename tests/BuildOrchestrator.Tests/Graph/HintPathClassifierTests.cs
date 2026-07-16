using System.IO;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Graph;

namespace BuildOrchestrator.Tests.Graph;

public class HintPathClassifierTests
{
    private static EvaluatedProject P(string id, string asm, params string[] hints) =>
        new(id, asm, [], hints.Select(h => new RawHintPath(h, Path.GetFileName(h).ToLowerInvariant())).ToList(), [], false);

    [Fact]
    public void classifies_edge_thirdparty_osysplatform_and_unclassified()
    {
        var a = P("C:\\r\\A.csproj", "OSYS.A",
            "..\\B\\OSYS.B.dll",                                  // Edge (producer var)
            "..\\packages\\Newtonsoft.Json.12\\lib\\Newtonsoft.Json.dll", // ExternalThirdParty
            "C:\\OSYS\\Server\\Bin\\OSYS.Kernel.dll",            // ExternalOsysPlatform (\Bin\, producer yok)
            "..\\weird\\Mystery.dll");                            // Unclassified
        var b = P("C:\\r\\B.csproj", "OSYS.B");
        var producers = ProducerMapBuilder.Build([a, b]);
        var r = HintPathClassifier.Classify([a, b], producers);
        Assert.Equal(1, r.EdgeCount);
        Assert.Equal(1, r.ThirdPartyCount);
        Assert.Equal(1, r.OsysPlatformCount);
        Assert.Equal(1, r.UnclassifiedCount);
        Assert.Equal(0.5, r.RepoResolveRatio, 3); // 1/(1+1)
        Assert.Single(r.Warnings);
    }
}
