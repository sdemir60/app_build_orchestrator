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

    /// <summary>
    /// Bir invoke isteğinin hangi MSBuild çağrılarına dönüştüğü — argüman seçiminin TEK kaynağı (invoker
    /// yalnız çalıştırır, seçmez).
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Bir tur boyunca harici hedefler AYRI bir argüman listesi kullandı
    /// (<c>BuildProjectReferences</c> serbest, koşulsuz restore): o turda harici proje tek bir solution olarak,
    /// bu aracın grafının DIŞINDA derleniyordu. Artık harici projeler taranıyor, grafa giriyor ve
    /// bağımlılıkları bu araç tarafından ayrı düğümler olarak derleniyor — yani ana repo projeleriyle
    /// tamamen aynı sözleşme geçerli. İkinci liste kaldırıldı (kopya YASAK, CLAUDE.md).</para>
    /// </summary>
    /// <returns>Restore çağrısı (gerekmiyorsa null) ve build çağrısı.</returns>
    public static (IReadOnlyList<string>? Restore, IReadOnlyList<string> Build) PlanFor(MsBuildInvokeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (request.NeedsRestore ? RestorePackagesConfig(request.ProjectId, request.SolutionDir) : null,
                Build(request.ProjectId, request.Configuration, request.BaseIntermediateOutputPath));
    }

    public static string EnsureTrailingBackslash(string dir) =>
        dir.EndsWith('\\') || dir.EndsWith('/') ? dir : dir + '\\';
}
