using System.Diagnostics;
using System.IO;
using System.Text.Json;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [v1.16.0] Alt bardaki <c>N behind</c> chip'inin motor karşılığı: <c>pullRepository</c>.
///
/// <para><b>Kuralın bilinçli güncellemesi burada pinlenir:</b> araç ana repoda kendiliğinden ASLA pull
/// yapmaz — bu komut yalnız kullanıcının tıklamasıyla gelir — ve geldiğinde de yalnız ff-only ilerletir.
/// Kirli ya da ayrışmış bir ağaç REDDEDİLİR ve çalışma kopyasına DOKUNULMAZ.</para>
///
/// <para>Testler gerçek Supervisor process'i, gerçek <c>git.exe</c> ve gerçek bir <c>file://</c> remote ile
/// koşar — sahte repo yok, sahte git yok.</para>
/// </summary>
public class PullRepositoryTests
{
    private static async Task<IReadOnlyList<IpcEvent>> PullAsync(string root, string branch)
    {
        using var p = Process.Start(TestPaths.Psi())!;
        await p.StandardInput.WriteLineAsync(
            JsonSerializer.Serialize<IpcCommand>(new PullRepositoryCommand(root, branch), IpcJson.Options));
        await p.StandardInput.WriteLineAsync("""{"type":"shutdown"}""");

        string all = await p.StandardOutput.ReadToEndAsync().WaitAsync(TestPaths.WideStartupTimeout);
        await p.WaitForExitAsync(new CancellationTokenSource(2000).Token);

        return [.. all
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonSerializer.Deserialize<IpcEvent>(line, IpcJson.Options))
            .OfType<IpcEvent>()];
    }

    private static IReadOnlyList<string> Lines(IReadOnlyList<IpcEvent> events) =>
        [.. events.OfType<SyncProgressEvent>().Select(e => e.Line)];

    [Fact]
    public async Task A_branch_that_is_behind_is_fast_forwarded_and_the_console_says_from_where_to_where()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.cs", "one");
        string first = upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        upstream.WriteFile("a.cs", "two");
        string second = upstream.CommitAll("second");
        string branch = upstream.CurrentBranchName();

        var events = await PullAsync(clone, branch);

        Assert.Contains($"git merge --ff-only origin/{branch}", Lines(events));
        Assert.Contains($"Pulled origin/{branch} — fast-forward {first[..7]}..{second[..7]}", Lines(events));
        Assert.True(Assert.Single(events.OfType<PullCompletedEvent>()).Succeeded);
        Assert.Equal(second, GitTestRepo.RunGitAt(clone, "rev-parse", "HEAD").Trim());
    }

    [Fact]
    public async Task An_up_to_date_branch_is_left_alone_and_says_so()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.cs", "one");
        string head = upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        string branch = upstream.CurrentBranchName();

        var events = await PullAsync(clone, branch);

        Assert.Contains($"Already up to date with origin/{branch}", Lines(events));
        // Succeeded=false: ilerleyen bir şey YOK. App bunu "chip'i düşür ama Sync koşturma" diye okur —
        // zaten geride olmayan bir ağaçta yeni bir plan hesaplamak boşuna iş olurdu.
        Assert.False(Assert.Single(events.OfType<PullCompletedEvent>()).Succeeded);
        Assert.Equal(head, GitTestRepo.RunGitAt(clone, "rev-parse", "HEAD").Trim());
    }

    [Fact]
    public async Task Uncommitted_work_refuses_the_pull_and_never_touches_the_working_tree()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.cs", "one");
        string first = upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        upstream.WriteFile("a.cs", "two");
        upstream.CommitAll("second");
        string branch = upstream.CurrentBranchName();

        // Kullanıcının commit'lenmemiş çalışması: araç onun üstüne ASLA çalışmaz.
        File.WriteAllText(Path.Combine(clone, "a.cs"), "local edit");

        var events = await PullAsync(clone, branch);

        Assert.Contains(
            "Pull refused — uncommitted changes in the working tree; commit or stash them first", Lines(events));
        Assert.False(Assert.Single(events.OfType<PullCompletedEvent>()).Succeeded);
        Assert.Equal(first, GitTestRepo.RunGitAt(clone, "rev-parse", "HEAD").Trim());
        Assert.Equal("local edit", File.ReadAllText(Path.Combine(clone, "a.cs")));
    }

    [Fact]
    public async Task A_diverged_branch_is_refused_with_the_reason_instead_of_a_merge()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.cs", "one");
        upstream.CommitAll("first");
        string clone = upstream.CloneFull();
        string branch = upstream.CurrentBranchName();

        // İki taraf da kendi commit'ini attı → fast-forward imkânsız. Araç birleştirme kararı VERMEZ.
        upstream.WriteFile("a.cs", "upstream change");
        upstream.CommitAll("upstream");
        File.WriteAllText(Path.Combine(clone, "b.cs"), "local work");
        GitTestRepo.RunGitAt(clone, "add", ".");
        GitTestRepo.RunGitAt(clone, "commit", "-m", "local");
        string localHead = GitTestRepo.RunGitAt(clone, "rev-parse", "HEAD").Trim();

        var events = await PullAsync(clone, branch);

        Assert.Contains(
            $"Pull refused — local branch has diverged from origin/{branch}; reconcile it manually", Lines(events));
        Assert.False(Assert.Single(events.OfType<PullCompletedEvent>()).Succeeded);
        Assert.Equal(localHead, GitTestRepo.RunGitAt(clone, "rev-parse", "HEAD").Trim());
    }
}
