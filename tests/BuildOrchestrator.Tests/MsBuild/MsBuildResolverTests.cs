using System;
using System.IO;
using Xunit;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Core.MsBuild;

namespace BuildOrchestrator.Tests.MsBuild;

public class MsBuildResolverTests
{
    private sealed class StubRunner(ProcessResult result) : IProcessRunner
    {
        public ProcessSpec? LastSpec;
        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
        { LastSpec = spec; return Task.FromResult(result); }
    }

    [Fact]
    public async Task Parses_vswhere_output_and_passes_canonical_args()
    {
        string self = Environment.ProcessPath!; // File.Exists geçen herhangi bir yol
        var stub = new StubRunner(new ProcessResult(0, self + "\r\n", "", TimeSpan.Zero, false));
        var loc = await new MsBuildResolver(stub).ResolveAsync(vswherePath: self);
        Assert.Equal(self, loc.MsBuildExePath);
        Assert.Equal(["-latest", "-requires", "Microsoft.Component.MSBuild", "-find", @"MSBuild\**\Bin\MSBuild.exe"],
                     stub.LastSpec!.Arguments);
    }

    [Fact]
    public async Task Empty_vswhere_output_throws_with_guidance()
    {
        var stub = new StubRunner(new ProcessResult(0, "", "", TimeSpan.Zero, false));
        var ex = await Assert.ThrowsAsync<MsBuildResolveException>(
            () => new MsBuildResolver(stub).ResolveAsync(vswherePath: Environment.ProcessPath!));
        Assert.Contains("MSBuild.exe bulunamadı", ex.Message);
    }

    [SkippableFact] // veya [Trait("Category","Machine")] — VS kurulu makinede koşar
    public async Task Real_machine_resolves_full_msbuild()
    {
        Skip.IfNot(File.Exists(MsBuildResolver.DefaultVswherePath), "vswhere yok");
        var loc = await new MsBuildResolver(new ProcessRunner()).ResolveAsync();
        Assert.True(File.Exists(loc.MsBuildExePath));
        Assert.True(loc.Version.Major >= 17); // spike: 18.7.8
    }
}
