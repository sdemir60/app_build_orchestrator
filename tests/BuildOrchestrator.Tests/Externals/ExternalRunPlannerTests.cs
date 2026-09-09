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
/// [D6] Build anındaki harici fazı: liste sırasıyla hedef çözümü → kök keşfi → kir kapısı → güncelleme →
/// revizyon → karar. Çözülemeyen yol, kir, ayrışma ve bozuk kurulum koşuyu HİÇ BAŞLATMADAN durdurur; ağ
/// hatası ise yalnız uyarır ve yerel sürümle devam eder (ana repo degraded fetch ile aynı felsefe).
/// </summary>
public class ExternalRunPlannerTests
{
    private const string Configuration = "Debug";

    private readonly List<string> _progress = [];

    private ExternalRunPlanner Planner(Func<CancellationToken, Task<string>>? tfResolver = null)
        => new(new ProcessRunner(), tfResolver);

    /// <summary>Upstream'e tek bir <c>&lt;name&gt;.sln</c> koyar ve ilk commit'i atar — klon hedefi bulur.</summary>
    private static void SeedUpstream(GitTestRepo upstream, string name = "Mail")
    {
        upstream.WriteFile("a.cs", "one");
        upstream.WriteFile(name + ".sln", "");
        upstream.CommitAll("first");
    }

    private static ExternalProject GitAt(string directory) => new(directory, VcsKind.Git);

    private static string TargetOf(string directory, string name = "Mail") => Path.Combine(directory, name + ".sln");

    private Task<IReadOnlyList<ExternalBuildPlan>> PlanAsync(
        ExternalRunPlanner planner, IReadOnlyList<ExternalProject> externals,
        bool rebuild = false, IReadOnlyDictionary<string, BuildState>? state = null)
        => planner.PlanAsync(externals, Configuration, rebuild, state, _progress.Add);

    // ---------------------------------------------------------------- güncelleme

    [Fact]
    public async Task A_git_external_is_fast_forwarded_and_reported_at_its_new_revision()
    {
        using var upstream = new GitTestRepo();
        SeedUpstream(upstream);
        string clone = upstream.CloneFull();
        upstream.WriteFile("a.cs", "two");
        string expected = upstream.CommitAll("second");

        var plans = await PlanAsync(Planner(), [GitAt(clone)]);

        var plan = Assert.Single(plans);
        Assert.Equal(expected, plan.Revision);
        Assert.Equal(VcsKind.Git, plan.Vcs);
        Assert.True(plan.WillBuild);
        Assert.Equal(TargetOf(clone), plan.Target.TargetPath);
        Assert.Contains("Updating external 'Mail'", _progress);
    }

    [Fact]
    public async Task An_external_that_was_already_built_at_this_revision_is_skipped()
    {
        using var upstream = new GitTestRepo();
        SeedUpstream(upstream);
        string head = GitTestRepo.RunGitAt(upstream.RootPath, "rev-parse", "HEAD").Trim();
        string clone = upstream.CloneFull();
        var state = new Dictionary<string, BuildState>
        {
            [TargetOf(clone)] = new(TargetOf(clone),
                ExternalSignature.Compute(Configuration, VcsKind.Git, head), LastResult: BuildResult.Succeeded),
        };

        var plan = Assert.Single(await PlanAsync(Planner(), [GitAt(clone)], state: state));

        Assert.False(plan.WillBuild);
        Assert.Equal(WillBuildReason.UpToDate, plan.Reason);
        Assert.Contains("External 'Mail' is up to date", _progress);
    }

    [Fact]
    public async Task Rebuild_forces_the_external_to_build_even_when_it_is_current()
    {
        using var upstream = new GitTestRepo();
        SeedUpstream(upstream);
        string head = GitTestRepo.RunGitAt(upstream.RootPath, "rev-parse", "HEAD").Trim();
        string clone = upstream.CloneFull();
        var state = new Dictionary<string, BuildState>
        {
            [TargetOf(clone)] = new(TargetOf(clone),
                ExternalSignature.Compute(Configuration, VcsKind.Git, head), LastResult: BuildResult.Succeeded),
        };

        var plan = Assert.Single(await PlanAsync(Planner(), [GitAt(clone)], rebuild: true, state: state));

        Assert.True(plan.WillBuild);
        Assert.Null(plan.Reason);
    }

    [Fact]
    public async Task The_signature_round_trips_through_the_real_state_store()
    {
        // İkinci koşunun "up to date" demesi, imzanın diske yazılıp geri okunmasına dayanır.
        using var upstream = new GitTestRepo();
        SeedUpstream(upstream);
        string clone = upstream.CloneFull();
        string cacheRoot = Directory.CreateTempSubdirectory("bo-ext-plan-").FullName;
        var store = new BuildStateStore(cacheRoot);

        var first = Assert.Single(await PlanAsync(Planner(), [GitAt(clone)]));
        Assert.True(first.WillBuild);
        store.Upsert(new BuildState(TargetOf(clone), first.Signature, LastResult: BuildResult.Succeeded));

        var second = Assert.Single(await PlanAsync(Planner(), [GitAt(clone)], state: store.Load()));
        Assert.False(second.WillBuild);
    }

    // ---------------------------------------------------------------- kapılar

    [Fact]
    public async Task Local_changes_stop_the_run_before_it_starts()
    {
        using var upstream = new GitTestRepo();
        SeedUpstream(upstream);
        string clone = upstream.CloneFull();
        File.WriteAllText(Path.Combine(clone, "a.cs"), "local edit");

        var ex = await Assert.ThrowsAsync<ExternalPreparationException>(
            () => PlanAsync(Planner(), [GitAt(clone)]));

        Assert.Contains("'Mail'", ex.Message);
        Assert.Contains(clone, ex.Message);
        Assert.Contains("uncommitted changes", ex.Message);
    }

    [Fact]
    public async Task A_diverged_external_stops_the_run()
    {
        using var upstream = new GitTestRepo();
        SeedUpstream(upstream);
        string clone = upstream.CloneFull();
        upstream.WriteFile("a.cs", "upstream");
        upstream.CommitAll("second");
        File.WriteAllText(Path.Combine(clone, "b.cs"), "local");
        GitTestRepo.RunGitAt(clone, "add", "-A");
        GitTestRepo.RunGitAt(clone, "-c", "user.email=t@t.local", "-c", "user.name=T", "commit", "-q", "-m", "local");

        var ex = await Assert.ThrowsAsync<ExternalPreparationException>(
            () => PlanAsync(Planner(), [GitAt(clone)]));

        Assert.Contains("diverged", ex.Message);
    }

    [Fact]
    public async Task A_path_that_cannot_be_resolved_stops_the_run_and_names_the_fix()
    {
        var missing = new ExternalProject(Path.Combine(Path.GetTempPath(), "Ocr-2ad9"), VcsKind.Git);

        var ex = await Assert.ThrowsAsync<ExternalPreparationException>(() => PlanAsync(Planner(), [missing]));

        Assert.Contains("'Ocr-2ad9'", ex.Message);
        Assert.Contains("was not found", ex.Message);
        Assert.Contains("fix the path in Settings", ex.Message);
    }

    [Fact]
    public async Task A_folder_with_two_solutions_stops_the_run_instead_of_guessing()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "Mail.sln"), "");
        File.WriteAllText(Path.Combine(temp.Path, "Mail.Tools.sln"), "");

        var ex = await Assert.ThrowsAsync<ExternalPreparationException>(
            () => PlanAsync(Planner(), [GitAt(temp.Path)]));

        Assert.Contains("more than one solution", ex.Message);
    }

    [Fact]
    public async Task An_unreachable_remote_only_warns_and_the_local_revision_is_used()
    {
        using var upstream = new GitTestRepo();
        SeedUpstream(upstream);
        string clone = upstream.CloneFull();
        string local = GitTestRepo.RunGitAt(clone, "rev-parse", "HEAD").Trim();
        GitTestRepo.RunGitAt(clone, "remote", "set-url", "origin", Path.Combine(Path.GetTempPath(), "no-remote-6b2f"));

        var plan = Assert.Single(await PlanAsync(Planner(), [GitAt(clone)]));

        Assert.Equal(local, plan.Revision);
        Assert.Contains(_progress, l => l.StartsWith("warning: external 'Mail' could not be updated", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_path_with_no_working_copy_of_the_selected_kind_is_built_as_is()
    {
        // Kullanıcı "Git" dedi ama yolun üstünde .git yok: güncelleme yok, kir kapısı yok, revizyon bilinmez.
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "Mail.sln"), "");

        var plan = Assert.Single(await PlanAsync(Planner(), [GitAt(temp.Path)]));

        Assert.Equal(VcsKind.Git, plan.Vcs);
        Assert.Null(plan.Revision);
        Assert.True(plan.WillBuild); // bilinmeyen revizyon hiçbir zaman "up to date" olamaz
        Assert.Contains(_progress, l => l.Contains("no git working copy found", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- TFVC

    [Fact]
    public async Task A_tfvc_external_without_team_explorer_stops_the_run_with_guidance()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "$tf"));
        File.WriteAllText(Path.Combine(temp.Path, "Mail.sln"), "");

        var ex = await Assert.ThrowsAsync<ExternalPreparationException>(
            () => PlanAsync(Planner(_ => throw new TfResolveException("TF.exe was not found — install Team Explorer.")),
                [new ExternalProject(temp.Path, VcsKind.Tfvc)]));

        Assert.Contains("Team Explorer", ex.Message);
    }

    [Fact]
    public async Task The_tf_executable_is_resolved_only_when_a_tfvc_external_is_present()
    {
        using var upstream = new GitTestRepo();
        SeedUpstream(upstream);
        string clone = upstream.CloneFull();
        bool resolved = false;

        await PlanAsync(Planner(_ => { resolved = true; return Task.FromResult(@"C:\TF.exe"); }), [GitAt(clone)]);

        Assert.False(resolved);
    }

    // ---------------------------------------------------------------- sıra

    [Fact]
    public async Task Externals_are_prepared_in_the_order_the_user_listed_them()
    {
        using var first = new GitTestRepo();
        SeedUpstream(first, "Mail");
        using var second = new GitTestRepo();
        SeedUpstream(second, "Ocr");

        var plans = await PlanAsync(Planner(), [GitAt(second.RootPath), GitAt(first.RootPath)]);

        Assert.Equal(["Ocr", "Mail"], plans.Select(p => p.Target.Name));
        Assert.Equal(
            [.. _progress.Where(l => l.StartsWith("Updating external", StringComparison.Ordinal))],
            new[] { "Updating external 'Ocr'", "Updating external 'Mail'" });
    }
}
