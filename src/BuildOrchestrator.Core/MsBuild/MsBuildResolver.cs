using System.Diagnostics;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.MsBuild;

public sealed class MsBuildResolveException(string message) : Exception(message);

public sealed record MsBuildLocation(string MsBuildExePath, Version Version);

public sealed class MsBuildResolver(IProcessRunner runner)
{
    /// <summary>Kurulumda MSBuild.exe'yi bulan vswhere argümanları.</summary>
    private static readonly string[] FindArguments =
        ["-latest", "-requires", "Microsoft.Component.MSBuild", "-find", @"MSBuild\**\Bin\MSBuild.exe"];

    public static string DefaultVswherePath => VsWhereLocator.DefaultVswherePath;

    public async Task<MsBuildLocation> ResolveAsync(string? vswherePath = null, CancellationToken ct = default)
    {
        // Arama vswhere yüzeyine delege edilir (kopya yasak — tf.exe de aynı yüzeyden çözülür); burada
        // yalnız sonucun bu alanın diline çevrilmesi kalır.
        var found = await new VsWhereLocator(runner).FindAsync(FindArguments, vswherePath, ct);

        string path = found.Outcome switch
        {
            VsWhereOutcome.Found => found.Path!,
            VsWhereOutcome.LocatorMissing => throw new MsBuildResolveException(
                $"vswhere was not found: {found.Detail} (are VS/Build Tools installed?)"),
            VsWhereOutcome.LocatorFailed => throw new MsBuildResolveException($"vswhere error: {found.Detail}"),
            _ => throw new MsBuildResolveException(
                "MSBuild.exe was not found in the vswhere output (is the Microsoft.Component.MSBuild component installed?)"),
        };

        var fvi = FileVersionInfo.GetVersionInfo(path);
        return new MsBuildLocation(path, new Version(fvi.FileMajorPart, fvi.FileMinorPart, fvi.FileBuildPart));
    }
}
