using BuildOrchestrator.Core.MsBuild;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// Bir invoke isteğinin hangi MSBuild çağrılarına dönüştüğü — argüman seçiminin TEK kaynağı. Harici hedefler
/// HER ZAMAN önce restore edilir (paketleri eksik bir harici, ana repo derlenmeden önce sessizce patlardı),
/// ana repo yolu ise bugünküyle bayt-bayt aynıdır.
/// </summary>
public class ExternalInvokePlanTests
{
    private const string Target = @"D:\ext\mail\Mail.sln";

    [Fact]
    public void An_external_target_restores_first_then_builds()
    {
        var plan = MsBuildArguments.PlanFor(new MsBuildInvokeRequest(
            Target, "Debug", SolutionDir: @"D:\ext\mail", NeedsRestore: false, ExternalTarget: true));

        Assert.Equal(MsBuildArguments.RestoreExternal(Target), plan.Restore);
        Assert.Equal(MsBuildArguments.BuildExternal(Target, "Debug"), plan.Build);
    }

    [Fact]
    public void An_external_target_restores_even_when_the_request_does_not_ask_for_it()
    {
        // NeedsRestore ana repo için hesaplanan bir sinyaldir (packages.config var mı); harici projede
        // karar bize ait değildir — kendi paketlerini her koşuda tamamlaması beklenir.
        var plan = MsBuildArguments.PlanFor(new MsBuildInvokeRequest(
            Target, "Debug", @"D:\ext\mail", NeedsRestore: false, ExternalTarget: true));

        Assert.NotNull(plan.Restore);
    }

    [Fact]
    public void A_main_repository_request_without_restore_only_builds()
    {
        var plan = MsBuildArguments.PlanFor(new MsBuildInvokeRequest(
            @"C:\r\A.csproj", "Debug", @"C:\r", NeedsRestore: false));

        Assert.Null(plan.Restore);
        Assert.Equal(MsBuildArguments.Build(@"C:\r\A.csproj", "Debug"), plan.Build);
    }

    [Fact]
    public void A_main_repository_request_with_restore_keeps_the_solution_dir_contract()
    {
        var plan = MsBuildArguments.PlanFor(new MsBuildInvokeRequest(
            @"C:\r\A.csproj", "Debug", @"C:\r", NeedsRestore: true));

        Assert.Equal(MsBuildArguments.RestorePackagesConfig(@"C:\r\A.csproj", @"C:\r"), plan.Restore);
    }

    [Fact]
    public void A_main_repository_request_still_honours_an_isolated_obj_path()
    {
        var plan = MsBuildArguments.PlanFor(new MsBuildInvokeRequest(
            @"C:\r\A.csproj", "Debug", @"C:\r", NeedsRestore: false, BaseIntermediateOutputPath: @"D:\pool\_obj\A"));

        Assert.Contains(@"-p:BaseIntermediateOutputPath=D:\pool\_obj\A\", plan.Build);
    }

    [Fact]
    public void An_external_target_ignores_an_isolated_obj_path()
    {
        // obj izolasyonu worktree kimliğine bağlıdır; harici çalışma kopyası worktree havuzunda yaşamaz.
        var plan = MsBuildArguments.PlanFor(new MsBuildInvokeRequest(
            Target, "Debug", @"D:\ext\mail", NeedsRestore: true, BaseIntermediateOutputPath: @"D:\pool\_obj\X",
            ExternalTarget: true));

        Assert.DoesNotContain(plan.Build, a => a.Contains("BaseIntermediateOutputPath", System.StringComparison.Ordinal));
    }
}
