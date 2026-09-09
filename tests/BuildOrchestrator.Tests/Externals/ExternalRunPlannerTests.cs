using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Externals;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// [D6] Build anındaki harici fazı: liste sırasıyla kök keşfi → kir kapısı → güncelleme → revizyon → karar.
/// Kir, ayrışma ve bozuk kurulum koşuyu HİÇ BAŞLATMADAN durdurur; ağ hatası ise yalnız uyarır ve yerel
/// sürümle devam eder (ana repo degraded fetch ile aynı felsefe).
/// </summary>
public class ExternalRunPlannerTests
{
    private const string Configuration = "Debug";

    private readonly List<string> _progress = [];

    private ExternalRunPlanner Planner(Func<CancellationToken, Task<string>>? tfResolver = null)
        => new(new ProcessRunner(), tfResolver);

    private static ExternalProject ProjectAt(string directory, string name = "Mail")
        => new(name, directory, Path.Combine(directory, name + ".sln"));

    private Task<IReadOnlyList<ExternalBuildPlan>> PlanAsync(
        ExternalRunPlanner planner, IReadOnlyList<ExternalProject> externals,
        bool rebuild = false, IReadOnlyDictionary<string, BuildState>? state = null)
        => planner.PlanAsync(externals, Configuration, rebuild, state, _progress.Add);

    // ---------------------------------------------------------------- güncelleme

    [Fact]
    public async Task A_git_external_is_fast_forwarded_and_reported_at_its_new_revision()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.cs", "one");
        upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        upstream.WriteFile("a.cs", "two");
        string expected = upstream.CommitAll("second");

        var plans = await PlanAsync(Planner(), [ProjectAt(clone)]);

        var plan = Assert.Single(plans);
        Assert.Equal(expected, plan.Revision);
        Assert.Equal(VcsKind.Git, plan.Vcs);
        Assert.True(plan.WillBuild);
        Assert.Contains("Updating external 'Mail'", _progress);
    }

    [Fact]
    public async Task An_external_that_was_already_built_at_this_revision_is_skipped()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.cs", "one");
        string head = upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        var project = ProjectAt(clone);
        var state = new Dictionary<string, BuildState>
        {
            [project.TargetPath] = new(project.TargetPath,
                ExternalSignature.Compute(Configuration, VcsKind.Git, head), LastResult: BuildResult.Succeeded),
        };

        var plan = Assert.Single(await PlanAsync(Planner(), [project], state: state));

        Assert.False(plan.WillBuild);
        Assert.Equal(WillBuildReason.UpToDate, plan.Reason);
        Assert.Contains("External 'Mail' is up to date", _progress);
    }

    [Fact]
    public async Task Rebuild_forces_the_external_to_build_even_when_it_is_current()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.cs", "one");
        string head = upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        var project = ProjectAt(clone);
        var state = new Dictionary<string, BuildState>
        {
            [project.TargetPath] = new(project.TargetPath,
                ExternalSignature.Compute(Configuration, VcsKind.Git, head), LastResult: BuildResult.Succeeded),
        };

        var plan = Assert.Single(await PlanAsync(Planner(), [project], rebuild: true, state: state));

        Assert.True(plan.WillBuild);
        Assert.Null(plan.Reason);
    }

    [Fact]
    public async Task The_signature_round_trips_through_the_real_state_store()
    {
        // İkinci koşunun "up to date" demesi, imzanın diske yazılıp geri okunmasına dayanır.
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.cs", "one");
        upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        var project = ProjectAt(clone);
        string cacheRoot = Directory.CreateTempSubdirectory("bo-ext-plan-").FullName;
        var store = new BuildStateStore(cacheRoot);

        var first = Assert.Single(await PlanAsync(Planner(), [project]));
        Assert.True(first.WillBuild);
        store.Upsert(new BuildState(project.TargetPath, first.Signature, LastResult: BuildResult.Succeeded));

        var second = Assert.Single(await PlanAsync(Planner(), [project], state: store.Load()));
        Assert.False(second.WillBuild);
    }

    // ---------------------------------------------------------------- kapılar

    [Fact]
    public async Task Local_changes_stop_the_run_before_it_starts()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.cs", "one");
        upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        File.WriteAllText(Path.Combine(clone, "a.cs"), "local edit");

        var ex = await Assert.ThrowsAsync<ExternalPreparationException>(
            () => PlanAsync(Planner(), [ProjectAt(clone)]));

        Assert.Contains("'Mail'", ex.Message);
        Assert.Contains(clone, ex.Message);
        Assert.Contains("uncommitted changes", ex.Message);
    }

    [Fact]
    public async Task A_diverged_external_stops_the_run()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.cs", "one");
        upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        upstream.WriteFile("a.cs", "upstream");
        upstream.CommitAll("second");
        File.WriteAllText(Path.Combine(clone, "b.cs"), "local");
        GitTestRepo.RunGitAt(clone, "add", "-A");
        GitTestRepo.RunGitAt(clone, "-c", "user.email=t@t.local", "-c", "user.name=T", "commit", "-q", "-m", "local");

        var ex = await Assert.ThrowsAsync<ExternalPreparationException>(
            () => PlanAsync(Planner(), [ProjectAt(clone)]));

        Assert.Contains("diverged", ex.Message);
    }

    [Fact]
    public async Task A_missing_folder_stops_the_run()
    {
        var missing = ProjectAt(Path.Combine(Path.GetTempPath(), "no-such-external-2ad9"), "Ocr");

        var ex = await Assert.ThrowsAsync<ExternalPreparationException>(() => PlanAsync(Planner(), [missing]));

        Assert.Contains("'Ocr'", ex.Message);
    }

    [Fact]
    public async Task An_unreachable_remote_only_warns_and_the_local_revision_is_used()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.cs", "one");
        upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        string local = GitTestRepo.RunGitAt(clone, "rev-parse", "HEAD").Trim();
        GitTestRepo.RunGitAt(clone, "remote", "set-url", "origin", Path.Combine(Path.GetTempPath(), "no-remote-6b2f"));

        var plan = Assert.Single(await PlanAsync(Planner(), [ProjectAt(clone)]));

        Assert.Equal(local, plan.Revision);
        Assert.Contains(_progress, l => l.StartsWith("warning: external 'Mail' could not be updated", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_folder_without_version_control_is_built_as_is()
    {
        using var temp = new TempDir();

        var plan = Assert.Single(await PlanAsync(Planner(), [ProjectAt(temp.Path)]));

        Assert.Equal(VcsKind.Unknown, plan.Vcs);
        Assert.Null(plan.Revision);
        Assert.True(plan.WillBuild); // bilinmeyen revizyon hiçbir zaman "up to date" olamaz
        Assert.Contains(_progress, l => l.Contains("no version control detected", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- TFVC

    [Fact]
    public async Task A_tfvc_external_without_team_explorer_stops_the_run_with_guidance()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "$tf"));

        var ex = await Assert.ThrowsAsync<ExternalPreparationException>(
            () => PlanAsync(Planner(_ => throw new TfResolveException("TF.exe was not found — install Team Explorer.")),
                [ProjectAt(temp.Path)]));

        Assert.Contains("Team Explorer", ex.Message);
    }

    [Fact]
    public async Task The_tf_executable_is_resolved_only_when_a_tfvc_external_is_present()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.cs", "one");
        upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        bool resolved = false;

        await PlanAsync(Planner(_ => { resolved = true; return Task.FromResult(@"C:\TF.exe"); }), [ProjectAt(clone)]);

        Assert.False(resolved);
    }

    // ---------------------------------------------------------------- sıra

    [Fact]
    public async Task Externals_are_prepared_in_the_order_the_user_listed_them()
    {
        using var first = new GitTestRepo();
        first.WriteFile("a.cs", "one");
        first.CommitAll("first");
        using var second = new GitTestRepo();
        second.WriteFile("b.cs", "one");
        second.CommitAll("second");

        var plans = await PlanAsync(Planner(), [ProjectAt(second.RootPath, "Ocr"), ProjectAt(first.RootPath, "Mail")]);

        Assert.Equal(["Ocr", "Mail"], plans.Select(p => p.Project.Name));
        Assert.Equal(
            [.. _progress.Where(l => l.StartsWith("Updating external", StringComparison.Ordinal))],
            new[] { "Updating external 'Ocr'", "Updating external 'Mail'" });
    }
}
