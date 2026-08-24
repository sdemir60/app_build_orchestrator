using System.IO;
using System.Threading.Tasks;
using BuildOrchestrator.Core.Externals;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// [D7] Harici bir git çalışma kopyasının güncellenmesi — kod tabanındaki TEK mutasyon yapan git yüzeyi.
/// Güncelleme <c>pull</c> DEĞİLDİR: fetch + fast-forward. Fast-forward mümkün değilse çalışma kopyasına
/// DOKUNULMAZ ve durum çağırana veri olarak döner (exception yok).
///
/// <para>Testler gerçek <c>git.exe</c> ve gerçek bir <c>file://</c> remote ile koşar — sahte repo yok.</para>
/// </summary>
public class ExternalGitUpdaterTests
{
    private static ExternalGitUpdater Updater(string root) => new(new ProcessRunner(), root);

    private static string HeadOf(string root) => GitTestRepo.RunGitAt(root, "rev-parse", "HEAD").Trim();

    private static string BranchOf(string root) => GitTestRepo.RunGitAt(root, "symbolic-ref", "--short", "HEAD").Trim();

    private static string TrackingShaOf(string root, string branch)
        => GitTestRepo.RunGitAt(root, "rev-parse", $"refs/remotes/origin/{branch}").Trim();

    [Fact]
    public async Task A_working_copy_behind_its_remote_is_fast_forwarded()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.txt", "one");
        upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        upstream.WriteFile("a.txt", "two");
        string expected = upstream.CommitAll("second");

        var result = await Updater(clone).UpdateAsync();

        Assert.Equal(ExternalUpdateStatus.Updated, result.Status);
        Assert.Equal(expected, result.Revision);
        Assert.Equal(expected, HeadOf(clone));
        Assert.Equal("two", File.ReadAllText(Path.Combine(clone, "a.txt")));
    }

    [Fact]
    public async Task A_working_copy_already_at_its_remote_reports_already_current()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.txt", "one");
        string expected = upstream.CommitAll("first");
        string clone = upstream.CloneFull();

        var result = await Updater(clone).UpdateAsync();

        Assert.Equal(ExternalUpdateStatus.AlreadyCurrent, result.Status);
        Assert.Equal(expected, result.Revision);
        Assert.Equal(expected, HeadOf(clone));
    }

    [Fact]
    public async Task A_dirty_working_copy_is_reported_without_touching_the_remote()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.txt", "one");
        upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        string branch = BranchOf(clone);
        string trackingBefore = TrackingShaOf(clone, branch);
        string headBefore = HeadOf(clone);
        upstream.WriteFile("a.txt", "two");
        upstream.CommitAll("second");                       // remote ilerledi
        File.WriteAllText(Path.Combine(clone, "a.txt"), "local edit");

        var result = await Updater(clone).UpdateAsync();

        Assert.Equal(ExternalUpdateStatus.Dirty, result.Status);
        Assert.Equal(headBefore, HeadOf(clone));
        // Kapı fetch'ten ÖNCE kapanır: remote-tracking ref bile ilerlemez.
        Assert.Equal(trackingBefore, TrackingShaOf(clone, branch));
        Assert.Equal("local edit", File.ReadAllText(Path.Combine(clone, "a.txt")));
    }

    [Fact]
    public async Task An_untracked_file_also_counts_as_dirty()
    {
        // Harici projede HER kir sayılır: commit ya da stash zaten bir kullanıcı eylemidir; araç kullanıcının
        // dosyalarının üstüne çalışmaz.
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.txt", "one");
        upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        File.WriteAllText(Path.Combine(clone, "scratch.cs"), "// notes");

        var result = await Updater(clone).UpdateAsync();

        Assert.Equal(ExternalUpdateStatus.Dirty, result.Status);
    }

    [Fact]
    public async Task A_detached_head_is_reported_and_left_alone()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.txt", "one");
        upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        string headBefore = HeadOf(clone);
        GitTestRepo.RunGitAt(clone, "checkout", "-q", "--detach");

        var result = await Updater(clone).UpdateAsync();

        Assert.Equal(ExternalUpdateStatus.Detached, result.Status);
        Assert.Equal(headBefore, HeadOf(clone));
    }

    [Fact]
    public async Task A_diverged_working_copy_is_reported_and_left_alone()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.txt", "one");
        upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        upstream.WriteFile("a.txt", "upstream change");
        upstream.CommitAll("second");
        // Klonda AYRI bir commit — artık fast-forward mümkün değil.
        File.WriteAllText(Path.Combine(clone, "b.txt"), "local work");
        GitTestRepo.RunGitAt(clone, "add", "-A");
        GitTestRepo.RunGitAt(clone, "-c", "user.email=t@t.local", "-c", "user.name=T", "commit", "-q", "-m", "local");
        string headBefore = HeadOf(clone);

        var result = await Updater(clone).UpdateAsync();

        Assert.Equal(ExternalUpdateStatus.Diverged, result.Status);
        Assert.Equal(headBefore, HeadOf(clone));
        Assert.Equal(headBefore, result.Revision); // yerel sürümle devam edilebilsin diye revizyon yine bildirilir
    }

    [Fact]
    public async Task An_unreachable_remote_degrades_to_the_local_revision()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.txt", "one");
        upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        string headBefore = HeadOf(clone);
        GitTestRepo.RunGitAt(clone, "remote", "set-url", "origin",
            Path.Combine(Path.GetTempPath(), "no-such-remote-3f7c1a"));

        var result = await Updater(clone).UpdateAsync();

        // Ağ/kimlik hatası build'i düşürmez — yerel sürümle devam edilir (ana repo degraded fetch felsefesi).
        Assert.Equal(ExternalUpdateStatus.DegradedOffline, result.Status);
        Assert.Equal(headBefore, result.Revision);
        Assert.Equal(headBefore, HeadOf(clone));
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
    }

    [Fact]
    public async Task A_directory_that_is_not_a_repository_fails_as_data()
    {
        using var temp = new TempDir();

        var result = await Updater(temp.Path).UpdateAsync();

        Assert.Equal(ExternalUpdateStatus.Failed, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
    }
}
