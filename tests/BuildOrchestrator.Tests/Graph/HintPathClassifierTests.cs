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

    /// <summary>
    /// [optimize] NuGet <c>packages</c> ayrımı TEK kaynaktan gelir: Optimize "bu HintPath restore ile
    /// düzeltilebilir mi" sorusunu bu yardımcıya sorar, literali yeniden yazmaz. "Program Files" yolları
    /// third-party SAYILIR ama NuGet DEĞİLDİR — restore onları asla getiremez, dolayısıyla bir projeyi
    /// restore kuyruğuna sokmazlar.
    /// </summary>
    [Fact]
    public void IsNuGetPackagesPath_matches_the_packages_segment_and_not_program_files()
    {
        Assert.True(HintPathClassifier.IsNuGetPackagesPath("..\\packages\\Newtonsoft.Json.12\\lib\\net45\\Newtonsoft.Json.dll"));
        Assert.True(HintPathClassifier.IsNuGetPackagesPath("../Packages/Foo/lib/Foo.dll")); // ayraç + harf kutusu karışımı
        Assert.False(HintPathClassifier.IsNuGetPackagesPath("C:\\Program Files\\Vendor\\Vendor.dll"));
        Assert.False(HintPathClassifier.IsNuGetPackagesPath("..\\B\\bin\\Debug\\OSYS.B.dll"));
        Assert.False(HintPathClassifier.IsNuGetPackagesPath("..\\packagesfoo\\X.dll")); // segment SINIRI önemli
    }

    /// <summary>[optimize] <c>IsThirdParty</c>'nin packages ayağı artık ortak yardımcıdır; sınıflandırma
    /// davranışı DEĞİŞMEZ — Program Files hâlâ third-party, packages hâlâ third-party.</summary>
    [Fact]
    public void Program_files_and_packages_both_stay_third_party_after_the_helper_is_extracted()
    {
        var a = P("C:\\r\\A.csproj", "OSYS.A",
            "..\\packages\\Foo\\lib\\Foo.dll",
            "C:\\Program Files\\Vendor\\Vendor.dll");
        var r = HintPathClassifier.Classify([a], ProducerMapBuilder.Build([a]));
        Assert.Equal(2, r.ThirdPartyCount);
        Assert.Equal(0, r.UnclassifiedCount);
    }
}
