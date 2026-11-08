using System.Diagnostics;
using System.IO;
using System.Text.Json;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Supervisor;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [worktree] Settings Save'in motor karşılığı: <c>createWorktree</c>. Yürütme Core'un <c>WorktreeCreator</c>'ıdır
/// (<see cref="WorktreeCreatorTests"/>); burada pinlenen, Supervisor'ın onu komuttan çağırıp sonucu TEK bir
/// <see cref="WorktreeCreatedEvent"/> olarak bildirmesi, konsol satırı YAZMAMASI (satırları App kurar) ve koşu uçuştayken
/// reddetmesidir.
///
/// <para><see cref="CheckoutBranchTests"/> deseni: ilk test gerçek Supervisor process'i ve gerçek <c>git.exe</c> ile koşar; ret testi
/// koşuyu uçuşta TUTMAK zorunda olduğundan gerçek <see cref="SupervisorHost"/>'u process açmadan sürer.</para>
/// </summary>
public class CreateWorktreeTests
{
    private static GitTestRepo RepoWithCommit(out string head)
    {
        var repo = new GitTestRepo();
        repo.WriteFile("a.cs", "main");
        head = repo.CommitAll("first");
        return repo;
    }

    [Fact]
    public async Task The_engine_creates_a_detached_worktree_and_reports_it()
    {
        using var repo = RepoWithCommit(out string head);
        using var parent = new TempDir();
        string folder = Path.Combine(parent.Path, "acme-worktree");

        using var sandbox = new SupervisorSandbox();
        using var p = Process.Start(sandbox.Psi())!;
        await p.StandardInput.WriteLineAsync(
            JsonSerializer.Serialize<IpcCommand>(new CreateWorktreeCommand(repo.RootPath, folder), IpcJson.Options));
        await p.StandardInput.WriteLineAsync("""{"type":"shutdown"}""");
        string all = await p.StandardOutput.ReadToEndAsync().WaitAsync(TestPaths.WideStartupTimeout);
        await p.WaitForExitAsync(new CancellationTokenSource(2000).Token);
        var events = NdjsonWire.Parse(all);

        var done = Assert.Single(events.OfType<WorktreeCreatedEvent>());
        Assert.True(done.Created, done.Detail);
        Assert.Equal(folder, done.Folder);
        Assert.Equal(head, done.Revision);
        // Supervisor anlatı satırı yazmaz — satırları App olaydan kurar.
        Assert.DoesNotContain(events, e => e is SyncProgressEvent);
        Assert.Equal(WorktreeFolderState.Linked, WorktreeFolder.Inspect(repo.RootPath, folder));
        Assert.NotEqual(0, GitTestRepo.RunGitAllowingFailure(folder, "symbolic-ref", "-q", "HEAD")); // detached: branch yok
    }

    // App kapısı yarışa açıktır (komut yolda iken run başlayabilir); ikinci katman Supervisor'dadır. Ret klasöre dokunmaz.
    [Fact]
    public async Task Creation_is_rejected_while_a_run_is_in_flight()
    {
        using var repo = RepoWithCommit(out _);
        using var parent = new TempDir();
        string folder = Path.Combine(parent.Path, "acme-worktree");
        string sandbox = Directory.CreateTempSubdirectory("bo-worktree-sb-").FullName;
        try
        {
            var stdout = new SupervisorHostHarness.CollectingStream();
            var writer = new NdjsonWriter(stdout);
            using var job = JobObject.CreateKillOnClose();

            // Planlama testin sonuna kadar BLOKLANIR → koşu slotu dolu kalır (sinyal, Thread.Sleep DEĞİL [D8]).
            var releasePlanning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var coordinator = SupervisorHostHarness.NewIdleCoordinator(job, writer, _ => { },
                planner: (_, _) => { releasePlanning.Task.GetAwaiter().GetResult(); throw new InvalidOperationException("plan yok"); },
                logsRoot: Path.Combine(sandbox, "logs"));

            // startRun slotu SENKRON tutar; dispatch loop serildir — createWorktree okunduğunda run KESİNLİKLE aktiftir.
            var stdin = await SupervisorHostHarness.StdinWith(
                new StartRunCommand("r1", RunMode.Build, repo.RootPath, "Debug", 2),
                new CreateWorktreeCommand(repo.RootPath, folder));
            var host = new SupervisorHost(writer, new NdjsonReader(stdin), job, coordinator,
                WorkspaceServices.Default(sandbox, _ => throw new NotSupportedException("bu testte restore yok")));
            Assert.Equal(0, await Task.Run(() => host.RunAsync()).WaitAsync(SupervisorHostHarness.Limit));

            var all = NdjsonWire.Parse(stdout.Text);
            Assert.Contains(all, e => e is ErrorEvent { Code: "worktreeRejected" });
            Assert.DoesNotContain(all, e => e is WorktreeCreatedEvent);
            Assert.False(Directory.Exists(folder));

            releasePlanning.SetResult();
        }
        finally { SupervisorHostHarness.TryDeleteDirectories(sandbox); }
    }
}
