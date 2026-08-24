using System;
using System.IO;
using System.Threading.Tasks;
using BuildOrchestrator.Core.Externals;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// [D8] tf.exe, MSBuild.exe ile AYNI mekanizmadan — vswhere — çözülür; ikinci bir arama mantığı yazılmaz.
/// </summary>
public class TfResolverTests
{
    [Fact]
    public async Task Resolves_the_first_path_vswhere_reports()
    {
        string self = Environment.ProcessPath!; // File.Exists geçen herhangi bir yol
        var runner = new FakeProcessRunner(FakeProcessRunner.Output(self + "\r\n"));

        string resolved = await new TfResolver(runner).ResolveAsync(vswherePath: self);

        Assert.Equal(self, resolved);
        Assert.Equal(
            ["-latest", "-products", "*", "-find", @"Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\TF.exe"],
            runner.LastSpec.Arguments);
    }

    [Fact]
    public async Task An_empty_vswhere_result_points_at_team_explorer()
    {
        var runner = new FakeProcessRunner(FakeProcessRunner.Output(""));

        var ex = await Assert.ThrowsAsync<TfResolveException>(
            () => new TfResolver(runner).ResolveAsync(vswherePath: Environment.ProcessPath!));

        Assert.Contains("Team Explorer", ex.Message);
    }

    [Fact]
    public async Task A_missing_vswhere_is_reported_with_its_path()
    {
        string missing = Path.Combine(Path.GetTempPath(), "no-such-vswhere-8c21.exe");
        var runner = new FakeProcessRunner(FakeProcessRunner.Output(""));

        var ex = await Assert.ThrowsAsync<TfResolveException>(
            () => new TfResolver(runner).ResolveAsync(vswherePath: missing));

        Assert.Contains(missing, ex.Message);
    }
}
