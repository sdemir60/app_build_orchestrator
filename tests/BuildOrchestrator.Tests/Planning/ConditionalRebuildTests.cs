using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.Planning;

/// <summary>
/// Koşullu yeniden derlemenin saf kararları (<see cref="ConditionalRebuild"/>): dep-issue notlu, imzası
/// değişmemiş bir proje sırası geldiğinde ANCAK kayıtlı köklerinden biri artık başarılıysa derlenir.
/// Belirsizlikte yön derlemedir.
/// </summary>
public class ConditionalRebuildTests
{
    private static Dictionary<string, BuildResult> Completed(params (string Id, BuildResult Result)[] entries) =>
        new(entries.Select(e => new KeyValuePair<string, BuildResult>(e.Id, e.Result)), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, BuildState> Ledger(params (string Id, BuildResult Result)[] entries) =>
        new(entries.Select(e => new KeyValuePair<string, BuildState>(e.Id,
            new BuildState(e.Id, "sig", LastResult: e.Result))), StringComparer.OrdinalIgnoreCase);

    private static readonly Func<string, bool> Everywhere = _ => true;

    // ---------------------------------------------------------------- Decide

    /// <summary>Senaryo 1 (saf): kök bu koşuda yine patladı ⇒ derlemek aynı bayat çıktıya link'lemek olurdu.</summary>
    [Fact]
    public void a_root_that_failed_again_in_this_run_keeps_the_project_waiting()
        => Assert.Equal(ConditionalRebuildVerdict.DependencyStillFailing,
            ConditionalRebuild.Decide(["A"], Completed(("A", BuildResult.Failed)), Everywhere,
                Ledger(("A", BuildResult.Failed))));

    /// <summary>Senaryo 2 (saf): kök bu koşuda başarıyla derlendi ⇒ proje derlenir. Defter koşu BAŞINDAKİ
    /// hâlidir (kök orada hâlâ hatalı) — bu koşunun sonucu önce gelir.</summary>
    [Fact]
    public void a_root_that_succeeded_in_this_run_releases_the_project()
        => Assert.Equal(ConditionalRebuildVerdict.Build,
            ConditionalRebuild.Decide(["A"], Completed(("A", BuildResult.Succeeded)), Everywhere,
                Ledger(("A", BuildResult.Failed))));

    /// <summary>Senaryo 3 (saf, §8.3 güvenlik): kök kaynak değişmeden düzeldi — bu koşuda "up to date" atlandı,
    /// defterdeki son sonucu başarı ⇒ proje derlenir.</summary>
    [Fact]
    public void a_root_skipped_in_this_run_whose_last_recorded_result_is_success_releases_the_project()
        => Assert.Equal(ConditionalRebuildVerdict.Build,
            ConditionalRebuild.Decide(["A"], Completed(("A", BuildResult.Skipped)), Everywhere,
                Ledger(("A", BuildResult.Succeeded))));

    [Fact]
    public void a_root_skipped_in_this_run_whose_last_recorded_result_is_failure_keeps_the_project_waiting()
        => Assert.Equal(ConditionalRebuildVerdict.DependencyStillFailing,
            ConditionalRebuild.Decide(["A"], Completed(("A", BuildResult.Skipped)), Everywhere,
                Ledger(("A", BuildResult.Failed))));

    [Fact]
    public void one_successful_root_among_failing_ones_is_enough()
        => Assert.Equal(ConditionalRebuildVerdict.Build,
            ConditionalRebuild.Decide(["A", "B"],
                Completed(("A", BuildResult.Failed), ("B", BuildResult.Succeeded)), Everywhere,
                Ledger(("A", BuildResult.Failed), ("B", BuildResult.Failed))));

    [Fact]
    public void all_roots_failing_across_run_and_ledger_keeps_the_project_waiting()
        => Assert.Equal(ConditionalRebuildVerdict.DependencyStillFailing,
            ConditionalRebuild.Decide(["A", "B"],
                Completed(("A", BuildResult.Failed)), Everywhere,          // B bu koşuda hiç görünmedi
                Ledger(("A", BuildResult.Succeeded), ("B", BuildResult.Failed))));

    /// <summary>Kök projeden kaldırıldı ⇒ bekleyecek bir şey yok, güvenli yön derlemek.</summary>
    [Fact]
    public void a_root_missing_from_the_workspace_releases_the_project()
        => Assert.Equal(ConditionalRebuildVerdict.Build,
            ConditionalRebuild.Decide(["Gone", "A"], Completed(("A", BuildResult.Failed)),
                id => id != "Gone", Ledger(("A", BuildResult.Failed))));

    /// <summary>Kökün defter kaydı yoksa sonucu bilinmiyor ⇒ derlenir.</summary>
    [Fact]
    public void a_root_without_a_ledger_record_that_did_not_run_releases_the_project()
        => Assert.Equal(ConditionalRebuildVerdict.Build,
            ConditionalRebuild.Decide(["A"], Completed(), Everywhere, Ledger()));

    /// <summary>Senaryo 4 (saf): kök listesi olmayan (eski) kayıt ⇒ bugünkü davranış, derlenir.</summary>
    [Fact]
    public void unknown_roots_release_the_project()
    {
        Assert.Equal(ConditionalRebuildVerdict.Build,
            ConditionalRebuild.Decide(null, Completed(("A", BuildResult.Failed)), Everywhere, Ledger()));
        Assert.Equal(ConditionalRebuildVerdict.Build,
            ConditionalRebuild.Decide([], Completed(("A", BuildResult.Failed)), Everywhere, Ledger()));
    }

    // ---------------------------------------------------------------- AppliesTo

    private static ProjectNode Waiting(WillBuildReason reason = WillBuildReason.WaitingForDependency, bool? willBuild = true) =>
        new("P", "P", "P", [], [], 0, null, null, InCycle: false, WillBuild: willBuild, WillBuildReason: reason);

    [Fact]
    public void build_and_cycles_runs_evaluate_a_waiting_project_conditionally()
    {
        Assert.True(ConditionalRebuild.AppliesTo(Waiting(), RunMode.Build, scopedRun: false, cycleGroupMember: false));
        Assert.True(ConditionalRebuild.AppliesTo(Waiting(), RunMode.Cycles, scopedRun: false, cycleGroupMember: false));
    }

    /// <summary>Senaryo 7 (saf): Rebuild her şeyi derler; satırdan tetiklenen hedef koşulsuz derlenir.</summary>
    [Fact]
    public void rebuild_and_single_project_runs_are_unconditional()
    {
        Assert.False(ConditionalRebuild.AppliesTo(Waiting(), RunMode.Rebuild, scopedRun: false, cycleGroupMember: false));
        Assert.False(ConditionalRebuild.AppliesTo(Waiting(), RunMode.Build, scopedRun: true, cycleGroupMember: false));
    }

    [Fact]
    public void a_cycle_group_member_is_unconditional()
        => Assert.False(ConditionalRebuild.AppliesTo(Waiting(), RunMode.Cycles, scopedRun: false, cycleGroupMember: true));

    [Fact]
    public void only_the_waiting_reason_makes_a_project_conditional()
    {
        Assert.False(ConditionalRebuild.AppliesTo(Waiting(WillBuildReason.DepIssue), RunMode.Build, false, false));
        Assert.False(ConditionalRebuild.AppliesTo(Waiting(WillBuildReason.SignatureChanged), RunMode.Build, false, false));
        // Pre-skip edilen (WillBuild=false) proje koşuda zaten derlenmez — koşullu da sayılmaz.
        Assert.False(ConditionalRebuild.AppliesTo(Waiting(willBuild: false), RunMode.Cycles, false, false));
    }

    // ---------------------------------------------------------------- RootNames

    [Fact]
    public void root_names_are_display_names_sorted_with_a_file_name_fallback()
    {
        var state = new BuildState("P", "sig", LastResult: BuildResult.Succeeded, DepIssue: true,
            DepIssueRoots: [@"C:\r\Zeta\Zeta.csproj", @"C:\r\gone\Gone.csproj", @"C:\r\A\A.csproj"]);

        var names = ConditionalRebuild.RootNames(state,
            id => id.EndsWith("Zeta.csproj", StringComparison.Ordinal) ? "OSYS.Zeta"
                : id.EndsWith("A.csproj", StringComparison.Ordinal) ? "OSYS.A" : null);

        Assert.Equal(["Gone", "OSYS.A", "OSYS.Zeta"], names);
    }

    [Fact]
    public void there_are_no_root_names_without_recorded_roots()
    {
        Assert.Null(ConditionalRebuild.RootNames(null, _ => null));
        Assert.Null(ConditionalRebuild.RootNames(
            new BuildState("P", "sig", LastResult: BuildResult.Succeeded, DepIssue: true), _ => null));
    }
}
