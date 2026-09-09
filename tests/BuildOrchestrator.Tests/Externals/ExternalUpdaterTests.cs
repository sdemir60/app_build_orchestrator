using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Externals;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// [D5] Build'in ilk adımı: harici çalışma kopyalarını kendi sürüm kontrolünden güncelle. <b>Yalnız
/// günceller</b> — "ne derlenecek" kararı taramadan sonra, sıradan incremental yoldan gelir.
///
/// <para>Kir, ayrışma ve bozuk kurulum koşuyu HİÇ BAŞLATMADAN durdurur; ağ hatası yalnız uyarır ve yerel
/// sürümle devam edilir (ana repo degraded fetch ile aynı felsefe).</para>
/// </summary>
public class ExternalUpdaterTests
{
    private readonly List<string> _progress = [];

    private ExternalUpdater Updater(Func<CancellationToken, Task<string>>? tfResolver = null)
        => new(new ProcessRunner(), tfResolver);

    private Task UpdateAsync(ExternalUpdater updater, params ExternalProject[] externals)
        => updater.UpdateAsync(externals, _progress.Add);

    private static ExternalProject GitAt(string path) => new(path, VcsKind.Git);

    // ---------------------------------------------------------------- kapı: koşacak mı

    [Theory]
    [InlineData(RunMode.Build, true, true)]
    [InlineData(RunMode.Rebuild, true, true)]
    [InlineData(RunMode.Build, false, false)]     // [D5] kullanıcı kapatmış
    [InlineData(RunMode.Cycles, true, false)]     // [D12] SCC onarımı çalışma kopyalarına dokunmaz
    public void The_gate_says_whether_this_run_touches_working_copies(RunMode mode, bool flag, bool expected)
        => Assert.Equal(expected, ExternalUpdater.ShouldUpdate(mode, flag, [GitAt(@"D:\ext\mail")]));

    [Fact]
    public void An_empty_or_missing_card_list_never_touches_anything()
    {
        Assert.False(ExternalUpdater.ShouldUpdate(RunMode.Build, true, []));
        Assert.False(ExternalUpdater.ShouldUpdate(RunMode.Build, true, null));
    }

    // ---------------------------------------------------------------- git

    [Fact]
    public async Task A_git_working_copy_that_is_behind_is_fast_forwarded()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.cs", "one");
        upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        upstream.WriteFile("a.cs", "two");
        string expected = upstream.CommitAll("second");

        await UpdateAsync(Updater(), GitAt(clone));

        Assert.Equal(expected, GitTestRepo.RunGitAt(clone, "rev-parse", "HEAD").Trim());
        Assert.Contains("Updating external '" + Path.GetFileName(clone) + "'", _progress);
    }

    [Fact]
    public async Task Local_changes_stop_the_run_before_it_starts()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.cs", "one");
        upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        File.WriteAllText(Path.Combine(clone, "a.cs"), "local edit");

        var ex = await Assert.ThrowsAsync<ExternalPreparationException>(() => UpdateAsync(Updater(), GitAt(clone)));

        Assert.Contains(Path.GetFileName(clone), ex.Message);
        Assert.Contains(clone, ex.Message);
        Assert.Contains("uncommitted changes", ex.Message);
    }

    [Fact]
    public async Task A_diverged_working_copy_stops_the_run()
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

        var ex = await Assert.ThrowsAsync<ExternalPreparationException>(() => UpdateAsync(Updater(), GitAt(clone)));

        Assert.Contains("diverged", ex.Message);
    }

    [Fact]
    public async Task An_unreachable_remote_only_warns_and_leaves_the_local_version_in_place()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.cs", "one");
        upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        string local = GitTestRepo.RunGitAt(clone, "rev-parse", "HEAD").Trim();
        GitTestRepo.RunGitAt(clone, "remote", "set-url", "origin", Path.Combine(Path.GetTempPath(), "no-remote-6b2f"));

        await UpdateAsync(Updater(), GitAt(clone));

        Assert.Equal(local, GitTestRepo.RunGitAt(clone, "rev-parse", "HEAD").Trim());
        Assert.Contains(_progress, l => l.StartsWith("warning: external", StringComparison.Ordinal)
            && l.Contains("could not be updated", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- çalışma kopyası yok

    [Fact]
    public async Task A_path_with_no_working_copy_of_the_selected_kind_is_left_alone_with_a_warning()
    {
        // Kullanıcı "Git" dedi ama yolun üstünde .git yok: güncelleme de kir kapısı da yok — projeler
        // olduğu gibi derlenir ve kullanıcı neden aranıldığını görür.
        using var temp = new TempDir();

        await UpdateAsync(Updater(), GitAt(temp.Path));

        Assert.Contains(_progress, l => l.Contains("no git working copy found", StringComparison.Ordinal));
        Assert.DoesNotContain(_progress, l => l.StartsWith("Updating external", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_path_that_does_not_exist_is_left_to_the_scan_to_report()
    {
        // Yolun kendisi bozuksa güncelleyici sessizce geçer; taramanın kapısı (NotScanned) koşuyu durdurur.
        var missing = Path.Combine(Path.GetTempPath(), "gone-3f7a");

        await UpdateAsync(Updater(), GitAt(missing));

        Assert.Contains(_progress, l => l.Contains("no git working copy found", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- TFVC

    [Fact]
    public async Task A_tfvc_card_without_team_explorer_stops_the_run_with_guidance()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "$tf"));

        var ex = await Assert.ThrowsAsync<ExternalPreparationException>(() => UpdateAsync(
            Updater(_ => throw new TfResolveException("TF.exe was not found — install Team Explorer.")),
            new ExternalProject(temp.Path, VcsKind.Tfvc)));

        Assert.Contains("Team Explorer", ex.Message);
    }

    [Fact]
    public async Task The_tf_executable_is_resolved_only_when_a_tfvc_card_is_present()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.cs", "one");
        upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        bool resolved = false;

        await UpdateAsync(Updater(_ => { resolved = true; return Task.FromResult(@"C:\TF.exe"); }), GitAt(clone));

        Assert.False(resolved);
    }

    // ---------------------------------------------------------------- sıra

    [Fact]
    public async Task Cards_are_updated_in_the_order_the_user_listed_them()
    {
        using var first = new GitTestRepo();
        first.WriteFile("a.cs", "one");
        first.CommitAll("first");
        using var second = new GitTestRepo();
        second.WriteFile("b.cs", "one");
        second.CommitAll("second");

        await UpdateAsync(Updater(), GitAt(second.RootPath), GitAt(first.RootPath));

        Assert.Equal(
            [$"Updating external '{Post(second.RootPath)}'",
             $"Updating external '{Post(first.RootPath)}'"],
            _progress.Where(l => l.StartsWith("Updating external", StringComparison.Ordinal)));
    }

    private static string Post(string path) => Path.GetFileName(path);

    // ---------------------------------------------------------------- [tek proje] kapsam: yalnız hedefin çalışma kopyası

    /// <summary>[design v1.15.0 §9] Tek proje koşusunda YALNIZ hedefi içeren çalışma kopyası güncellenir —
    /// kapsam dışına dokunulmaz: başka bir kartın kopyası ne fast-forward edilir ne de onun için satır yazılır.</summary>
    [Fact]
    public async Task A_scoped_run_updates_only_the_working_copy_that_holds_the_target()
    {
        using var upstreamA = new GitTestRepo();
        upstreamA.WriteFile("a.cs", "one");
        upstreamA.CommitAll("first");
        string cloneA = upstreamA.CloneFull();
        upstreamA.WriteFile("a.cs", "two");
        string expectedA = upstreamA.CommitAll("second");

        using var upstreamB = new GitTestRepo();
        upstreamB.WriteFile("b.cs", "one");
        upstreamB.CommitAll("first");
        string cloneB = upstreamB.CloneFull();
        string behindB = GitTestRepo.RunGitAt(cloneB, "rev-parse", "HEAD").Trim();
        upstreamB.WriteFile("b.cs", "two");
        upstreamB.CommitAll("second");

        await Updater().UpdateAsync([GitAt(cloneB), GitAt(cloneA)], _progress.Add,
            scopeProjectPath: Path.Combine(cloneA, "src", "A", "A.csproj"));

        Assert.Equal(expectedA, GitTestRepo.RunGitAt(cloneA, "rev-parse", "HEAD").Trim()); // hedefin kopyası ilerledi
        Assert.Equal(behindB, GitTestRepo.RunGitAt(cloneB, "rev-parse", "HEAD").Trim());   // diğeri OLDUĞU GİBİ
        Assert.Equal([$"Updating external '{Post(cloneA)}'"],
            _progress.Where(l => l.StartsWith("Updating external", StringComparison.Ordinal)));
    }

    /// <summary>Hedef ana repodaysa (hiçbir kartın altında değil) tek bir kopyaya bile dokunulmaz ve tek satır
    /// bile yazılmaz — kartın çalışma kopyası olmasa da ("no working copy" uyarısı dahil).</summary>
    [Fact]
    public async Task A_scoped_run_for_a_repository_project_touches_no_external_card_at_all()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.cs", "one");
        upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        string behind = GitTestRepo.RunGitAt(clone, "rev-parse", "HEAD").Trim();
        upstream.WriteFile("a.cs", "two");
        upstream.CommitAll("second");
        using var noVcs = new TempDir();

        await Updater().UpdateAsync([GitAt(clone), GitAt(noVcs.Path)], _progress.Add,
            scopeProjectPath: @"D:\repo\src\Main\Main.csproj");

        Assert.Equal(behind, GitTestRepo.RunGitAt(clone, "rev-parse", "HEAD").Trim());
        Assert.Empty(_progress);
    }

    /// <summary>Kapsam kararı saf bir yol sorusudur: hedef kartın arama kökünün ya da çalışma kopyasının
    /// ALTINDA mı. Harf-duyarsız ve ayraç-farkındadır — <c>D:\ext\mail2</c>, <c>D:\ext\mail</c>'in altı değildir.</summary>
    [Theory]
    [InlineData(@"D:\ext\mail", @"d:\EXT\mail\src\Mail.csproj", true)]
    [InlineData(@"D:\ext\mail\", @"D:\ext\mail\Mail.csproj", true)]
    [InlineData(@"D:\ext\mail", @"D:\ext\mail2\Mail.csproj", false)]
    [InlineData(@"D:\ext\mail", @"D:\other\Mail.csproj", false)]
    public void The_scope_test_is_a_path_prefix_that_respects_separators(string root, string project, bool expected)
        => Assert.Equal(expected, ExternalUpdater.Contains(root, project));
}
