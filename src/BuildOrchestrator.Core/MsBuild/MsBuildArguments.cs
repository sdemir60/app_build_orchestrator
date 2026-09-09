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
    /// [D10] HARİCİ hedefin build argümanları. Ana repo listesinden üç farkı vardır ve üçü de kasıtlıdır:
    /// <c>BuildProjectReferences=false</c> YOKTUR (harici solution kendi referanslarını ve post-build copy
    /// event'lerini kendisi derlemek zorundadır — OSYS'in HintPath ile okuduğu DLL'ler oraya böyle düşer),
    /// obj yeniden yönlendirilmez (izolasyon worktree havuzuna aittir, harici çalışma kopyası orada yaşamaz)
    /// ve <c>OutDir</c>'e hiçbir koşulda dokunulmaz.
    /// </summary>
    public static IReadOnlyList<string> BuildExternal(string projectPath, string configuration) =>
    [
        projectPath, "-t:Build", $"-p:Configuration={configuration}",
        "-p:UseSharedCompilation=false", "-nodeReuse:false", "-clp:Summary", "-nologo",
    ];

    /// <summary>[D10] HARİCİ hedefin restore argümanları. <c>SolutionDir</c> DIŞARIDAN DAYATILMAZ: hedefin
    /// kendisi bir solution'dır (ya da kendi bağlamını taşır) ve harici projenin paket yolunu bilen odur.</summary>
    public static IReadOnlyList<string> RestoreExternal(string projectPath) =>
    [
        projectPath, "-t:restore", "-p:RestorePackagesConfig=true", "-nologo",
    ];

    /// <summary>
    /// Bir invoke isteğinin hangi MSBuild çağrılarına dönüştüğü — argüman seçiminin TEK kaynağı (invoker
    /// yalnız çalıştırır, seçmez).
    ///
    /// <para>Harici hedefler HER ZAMAN restore edilir: <c>NeedsRestore</c> ana repo için hesaplanan bir
    /// sinyaldir (projenin yanında <c>packages.config</c> var mı), harici solution'ın paket durumunu ise bu
    /// araç bilmez — eksik paket, ana repo hiç derlenmeden gelen kriptik bir hataya dönerdi.</para>
    /// </summary>
    /// <returns>Restore çağrısı (gerekmiyorsa null) ve build çağrısı.</returns>
    public static (IReadOnlyList<string>? Restore, IReadOnlyList<string> Build) PlanFor(MsBuildInvokeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.ExternalTarget
            ? (RestoreExternal(request.ProjectId), BuildExternal(request.ProjectId, request.Configuration))
            : (request.NeedsRestore ? RestorePackagesConfig(request.ProjectId, request.SolutionDir) : null,
               Build(request.ProjectId, request.Configuration, request.BaseIntermediateOutputPath));
    }

    public static string EnsureTrailingBackslash(string dir) =>
        dir.EndsWith('\\') || dir.EndsWith('/') ? dir : dir + '\\';
}
