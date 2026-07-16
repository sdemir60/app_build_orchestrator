namespace BuildOrchestrator.Core.MsBuild;

public static class MsBuildArguments
{
    /// [D9 + SPIKE S2] v1 flag'leri SABİT; BuildProjectReferences=false ZORUNLU (bağımlılıklar ayrı node olarak derlenir).
    public static IReadOnlyList<string> Build(string projectPath, string configuration, string? baseIntermediateOutputPath = null)
    {
        var args = new List<string>
        {
            projectPath, "-t:Build", $"-p:Configuration={configuration}",
            "-p:UseSharedCompilation=false", "-nodeReuse:false", "-p:BuildProjectReferences=false",
            "-clp:Summary", "-nologo",
        };
        if (baseIntermediateOutputPath is not null)
            args.Add($"-p:BaseIntermediateOutputPath={EnsureTrailingBackslash(baseIntermediateOutputPath)}");
        return args;
    }

    /// [SPIKE S2 şart-1] packages.config restore sln bağlamı İSTER; [S1] nuget.exe YOK.
    public static IReadOnlyList<string> RestorePackagesConfig(string projectPath, string solutionDir) =>
    [
        projectPath, "-t:restore", "-p:RestorePackagesConfig=true",
        $"-p:SolutionDir={EnsureTrailingBackslash(solutionDir)}", "-nologo",
    ];

    public static string EnsureTrailingBackslash(string dir) =>
        dir.EndsWith('\\') || dir.EndsWith('/') ? dir : dir + '\\';
}
