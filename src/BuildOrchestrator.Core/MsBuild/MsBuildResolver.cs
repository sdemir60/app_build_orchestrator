using System.Diagnostics;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.MsBuild;

public sealed class MsBuildResolveException(string message) : Exception(message);

public sealed record MsBuildLocation(string MsBuildExePath, Version Version);

public sealed class MsBuildResolver(IProcessRunner runner)
{
    public static string DefaultVswherePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "Microsoft Visual Studio", "Installer", "vswhere.exe");

    public async Task<MsBuildLocation> ResolveAsync(string? vswherePath = null, CancellationToken ct = default)
    {
        string vswhere = vswherePath ?? DefaultVswherePath;
        if (!File.Exists(vswhere))
            throw new MsBuildResolveException($"vswhere was not found: {vswhere} (are VS/Build Tools installed?)");
        var result = await runner.RunAsync(new ProcessSpec(vswhere,
            ["-latest", "-requires", "Microsoft.Component.MSBuild", "-find", @"MSBuild\**\Bin\MSBuild.exe"],
            Timeout: TimeSpan.FromSeconds(30)), ct);
        if (!result.Success)
            throw new MsBuildResolveException($"vswhere error: exit={result.ExitCode} stderr={result.StandardError}");
        string? path = result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (path is null || !File.Exists(path))
            throw new MsBuildResolveException("MSBuild.exe was not found in the vswhere output (is the Microsoft.Component.MSBuild component installed?)");
        var fvi = FileVersionInfo.GetVersionInfo(path);
        return new MsBuildLocation(path, new Version(fvi.FileMajorPart, fvi.FileMinorPart, fvi.FileBuildPart));
    }
}
