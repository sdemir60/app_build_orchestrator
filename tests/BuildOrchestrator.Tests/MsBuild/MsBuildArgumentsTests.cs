using Xunit;
using BuildOrchestrator.Core.MsBuild;

namespace BuildOrchestrator.Tests.MsBuild;

public class MsBuildArgumentsTests
{
    [Fact]
    public void Build_contains_v1_flags_and_BuildProjectReferences_false() // [SPIKE S2 şart-3 + D9]
    {
        var args = MsBuildArguments.Build(@"c:\r\p.csproj", "Debug");
        Assert.Contains("-p:UseSharedCompilation=false", args);
        Assert.Contains("-nodeReuse:false", args);
        Assert.Contains("-p:BuildProjectReferences=false", args);
        Assert.Contains("-p:Configuration=Debug", args);
        Assert.Equal(@"c:\r\p.csproj", args[0]);
    }

    [Fact]
    public void Build_with_obj_isolation_has_trailing_backslash() // [SPIKE S2 şart-2 — bayat obj zehri]
    {
        var args = MsBuildArguments.Build(@"c:\r\p.csproj", "Debug", @"c:\wt\obj\c__r_p");
        Assert.Contains(@"-p:BaseIntermediateOutputPath=c:\wt\obj\c__r_p\", args);
    }

    [Fact]
    public void Build_with_null_obj_isolation_emits_no_BaseIntermediateOutputPath_arg() // [I2-K2] in-place = VS-parity, projenin kendi obj'i
    {
        var args = MsBuildArguments.Build(@"c:\r\p.csproj", "Debug", baseIntermediateOutputPath: null);
        Assert.DoesNotContain(args, a => a.StartsWith("-p:BaseIntermediateOutputPath=", StringComparison.Ordinal));
    }

    [Fact]
    public void Restore_requires_solutionDir_with_trailing_backslash_and_no_nuget_exe() // [SPIKE S2 şart-1 + S1]
    {
        var args = MsBuildArguments.RestorePackagesConfig(@"c:\r\p.csproj", @"c:\r\slnDir");
        Assert.Contains("-t:restore", args);
        Assert.Contains("-p:RestorePackagesConfig=true", args);
        Assert.Contains(@"-p:SolutionDir=c:\r\slnDir\", args);
        Assert.DoesNotContain(args, a => a.Contains("nuget", StringComparison.OrdinalIgnoreCase)); // nuget.exe bağımlılığı YOK
    }

    [Theory]
    [InlineData(@"c:\x", @"c:\x\")] [InlineData(@"c:\x\", @"c:\x\")] [InlineData("c:/x/", "c:/x/")]
    public void EnsureTrailingBackslash(string input, string expected)
        => Assert.Equal(expected, MsBuildArguments.EnsureTrailingBackslash(input));
}
