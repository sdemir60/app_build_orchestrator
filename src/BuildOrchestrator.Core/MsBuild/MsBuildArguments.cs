namespace BuildOrchestrator.Core.MsBuild;

/// <summary>
/// Bir derleme çağrısının MSBuild HEDEFİ. Varsayılan <see cref="Build"/>'dir ve tam koşuların (Build,
/// Rebuild, Cycles) hepsi onu kullanır — <b>alt bardaki "Rebuild" MSBuild'in Rebuild'i DEĞİLDİR</b>, "cache'i
/// yok say, her projeyi derle" demektir ve proje başına yine <c>-t:Build</c> koşar.
///
/// <para><see cref="Rebuild"/> yalnız satır menüsünün <b>Rebuild</b> maddesinden gelir (design §3.8; prototip
/// <c>msbuild X.csproj /t:Rebuild</c>): tek projelik bir kapsamda "cache'i yok say"ı zaten Build yapar, bu
/// yüzden satırdaki Rebuild'in ayrı bir anlamı olmalıdır — MSBuild'in kendi Clean+Build'i.</para>
///
/// <para><see cref="Clean"/> satır menüsünün <b>Clean</b> maddesidir ve Visual Studio'nun proje Clean'iyle
/// AYNI şeydir: <c>msbuild /t:Clean</c>, yani MSBuild'in o proje için bildiği çıktıların silinmesi. Hiçbir
/// şey derlenmez; cache'lere, NuGet'e ya da başka projelere dokunulmaz.</para>
/// </summary>
public enum MsBuildTarget { Build, Rebuild, Clean }

public static class MsBuildArguments
{
    /// [D9 + SPIKE S2] v1 flag'leri SABİT; BuildProjectReferences=false ZORUNLU (bağımlılıklar ayrı node olarak derlenir).
    public static IReadOnlyList<string> Build(string projectPath, string configuration,
        string? baseIntermediateOutputPath = null, MsBuildTarget target = MsBuildTarget.Build)
    {
        var args = new List<string>
        {
            projectPath, TargetArg(target), $"-p:Configuration={configuration}",
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
                Build(request.ProjectId, request.Configuration, request.BaseIntermediateOutputPath, request.Target));
    }

    /// <summary>Hedefin komut satırı karşılığı — TEK yer; <c>-t:</c> argümanını başka hiçbir yol yazmaz.</summary>
    private static string TargetArg(MsBuildTarget target) => target switch
    {
        MsBuildTarget.Rebuild => "-t:Rebuild",
        MsBuildTarget.Clean => "-t:Clean",
        _ => "-t:Build",
    };

    public static string EnsureTrailingBackslash(string dir) =>
        dir.EndsWith('\\') || dir.EndsWith('/') ? dir : dir + '\\';
}
