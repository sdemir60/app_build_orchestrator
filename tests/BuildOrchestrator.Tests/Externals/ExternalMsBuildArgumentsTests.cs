using System.Linq;
using BuildOrchestrator.Core.MsBuild;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// [D10] Harici projeler YERİNDE derlenir: kendi solution'larını referanslarıyla ve post-build copy
/// event'leriyle derlerler — çıktılar OSYS'in HintPath ile okuduğu yerlere böyle düşer. Bu yüzden ana repo
/// argümanlarının üç ayırt edici parçası burada YOKTUR.
/// </summary>
public class ExternalMsBuildArgumentsTests
{
    private const string Target = @"D:\ext\mail\Mail.sln";

    [Fact]
    public void The_external_build_argument_list_is_pinned()
        => Assert.Equal(
            [Target, "-t:Build", "-p:Configuration=Release", "-p:UseSharedCompilation=false",
             "-nodeReuse:false", "-clp:Summary", "-nologo"],
            MsBuildArguments.BuildExternal(Target, "Release"));

    [Fact]
    public void The_external_build_never_disables_project_references()
    {
        // Ana repoda her proje ayrı bir node'dur ve bağımlılıkları ayrıca derlenir; harici solution ise
        // kendi içindeki referansları KENDİ derlemek zorundadır.
        Assert.DoesNotContain("-p:BuildProjectReferences=false", MsBuildArguments.BuildExternal(Target, "Debug"));
    }

    [Fact]
    public void The_external_build_never_redirects_obj_or_out()
    {
        // obj izolasyonu worktree'lere aittir; OutDir'e ise hiçbir koşulda dokunulmaz.
        var args = MsBuildArguments.BuildExternal(Target, "Debug");

        Assert.DoesNotContain(args, a => a.Contains("BaseIntermediateOutputPath", System.StringComparison.Ordinal));
        Assert.DoesNotContain(args, a => a.Contains("OutDir", System.StringComparison.Ordinal));
    }

    [Fact]
    public void The_external_restore_argument_list_is_pinned()
        => Assert.Equal(
            [Target, "-t:restore", "-p:RestorePackagesConfig=true", "-nologo"],
            MsBuildArguments.RestoreExternal(Target));

    [Fact]
    public void The_external_restore_does_not_pass_a_solution_dir()
    {
        // Hedefin kendisi zaten bir solution (ya da kendi bağlamını taşıyan bir proje) — SolutionDir'i
        // dışarıdan dayatmak harici projenin kendi paket yolunu bozardı.
        Assert.DoesNotContain(MsBuildArguments.RestoreExternal(Target),
            a => a.StartsWith("-p:SolutionDir=", System.StringComparison.Ordinal));
    }

    [Fact]
    public void The_main_repository_argument_lists_are_unchanged()
    {
        Assert.Equal(
            [@"C:\r\A.csproj", "-t:Build", "-p:Configuration=Debug", "-p:UseSharedCompilation=false",
             "-nodeReuse:false", "-p:BuildProjectReferences=false", "-clp:Summary", "-nologo"],
            MsBuildArguments.Build(@"C:\r\A.csproj", "Debug"));
        Assert.Equal(
            [@"C:\r\A.csproj", "-t:restore", "-p:RestorePackagesConfig=true", @"-p:SolutionDir=C:\r\", "-nologo"],
            MsBuildArguments.RestorePackagesConfig(@"C:\r\A.csproj", @"C:\r"));
    }
}
