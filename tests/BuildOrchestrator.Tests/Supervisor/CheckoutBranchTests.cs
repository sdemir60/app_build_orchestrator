using System.Diagnostics;
using System.IO;
using System.Text.Json;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Logs;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Supervisor;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [spec 2026-09-18 §6.3] Branch chip'inin motor karşılığı: <c>checkoutBranch</c>. Yürütme Core'un
/// <c>BranchSwitcher</c>'ıdır; burada pinlenen, Supervisor'ın onu komuttan çağırıp sonucu TEK bir
/// <see cref="CheckoutCompletedEvent"/> olarak bildirmesi ve konsol satırı YAZMAMASIdır (satırları App kurar:
/// temizlik önce, not sonra).
///
/// <para>İlk iki test gerçek Supervisor process'i ve gerçek <c>git.exe</c> ile koşar
/// (<see cref="PullRepositoryTests"/> deseni). Reddetme testi koşuyu uçuşta TUTMAK zorunda olduğundan gerçek
/// <see cref="SupervisorHost"/>'u process açmadan sürer (<see cref="CleanDispatchTests"/> deseni).</para>
/// </summary>
public class CheckoutBranchTests
{
    private static async Task<IReadOnlyList<IpcEvent>> CheckoutAsync(CheckoutBranchCommand cmd)
    {
        using var sandbox = new SupervisorSandbox();
        using var p = Process.Start(sandbox.Psi())!;
        await p.StandardInput.WriteLineAsync(JsonSerializer.Serialize<IpcCommand>(cmd, IpcJson.Options));
        await p.StandardInput.WriteLineAsync("""{"type":"shutdown"}""");

        string all = await p.StandardOutput.ReadToEndAsync().WaitAsync(TestPaths.WideStartupTimeout);
        await p.WaitForExitAsync(new CancellationTokenSource(2000).Token);
        return NdjsonWire.Parse(all);
    }

    /// <summary>İki branch'li bir repo: <c>feature-x</c>'te <c>a.cs</c> farklıdır; ağaç ana branch'te bırakılır.</summary>
    private static (string main, string featureSha) SeedTwoBranches(GitTestRepo repo)
    {
        repo.WriteFile("a.cs", "main");
        repo.CommitAll("first");
        string main = repo.CurrentBranchName();
        repo.CreateBranch("feature-x");
        repo.Checkout("feature-x");
        repo.WriteFile("a.cs", "feature");
        string featureSha = repo.CommitAll("feature");
        repo.Checkout(main);
        return (main, featureSha);
    }

    [Fact]
    public async Task Checkout_switches_the_working_tree_and_reports_it()
    {
        using var repo = new GitTestRepo();
        var (main, featureSha) = SeedTwoBranches(repo);

        var events = await CheckoutAsync(new CheckoutBranchCommand(repo.RootPath, "feature-x", IsRemote: false, StashIfDirty: false));

        var done = Assert.Single(events.OfType<CheckoutCompletedEvent>());
        Assert.Equal(CheckoutStatus.Switched, done.Status);
        Assert.Equal(main, done.FromBranch);
        Assert.Equal("feature-x", done.Branch);
        Assert.Equal(featureSha, done.Revision);
        Assert.Null(done.StashMessage);
        // Supervisor anlatı satırı yazmaz — satırları App olaydan kurar.
        Assert.DoesNotContain(events, e => e is SyncProgressEvent);

        Assert.Equal("feature-x", repo.CurrentBranchName());
        Assert.Equal("feature", File.ReadAllText(Path.Combine(repo.RootPath, "a.cs")));
    }

    [Fact]
    public async Task Checkout_on_a_dirty_tree_is_refused_by_default()
    {
        using var repo = new GitTestRepo();
        var (main, _) = SeedTwoBranches(repo);
        repo.WriteFile("a.cs", "local edit");

        var events = await CheckoutAsync(new CheckoutBranchCommand(repo.RootPath, "feature-x", IsRemote: false, StashIfDirty: false));

        var done = Assert.Single(events.OfType<CheckoutCompletedEvent>());
        Assert.Equal(CheckoutStatus.Dirty, done.Status);
        Assert.Equal(1, done.DirtyCount);
        Assert.Null(done.Revision);
        Assert.Equal(main, repo.CurrentBranchName());
        Assert.Equal("local edit", File.ReadAllText(Path.Combine(repo.RootPath, "a.cs")));
        Assert.Empty(GitTestRepo.RunGitAt(repo.RootPath, "stash", "list").Trim());
    }

    // App kapısı yarışa açıktır (komut yolda iken run başlayabilir); ikinci katman Supervisor'dadır.
    [Fact]
    public async Task Checkout_is_rejected_while_a_run_is_in_flight()
    {
        using var repo = new GitTestRepo();
        var (main, _) = SeedTwoBranches(repo);
        string sandbox = Directory.CreateTempSubdirectory("bo-checkout-sb-").FullName;
        try
        {
            var stdout = new SupervisorHostHarness.CollectingStream();
            var writer = new NdjsonWriter(stdout);
            using var job = JobObject.CreateKillOnClose();

            // Planlama testin sonuna kadar BLOKLANIR → koşu slotu dolu kalır (sinyal, Thread.Sleep DEĞİL [D8]).
            var releasePlanning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var coordinator = new RunCoordinator(
                planner: (_, _) => { releasePlanning.Task.GetAwaiter().GetResult(); throw new InvalidOperationException("plan yok"); },
                msbuildFactory: _ => throw new InvalidOperationException("bu testte msbuild yok"),
                logFactory: startedAt => new RunLogWriter(Path.Combine(sandbox, "logs"), startedAt),
                writer: writer, innerJob: job, nowMs: () => 0, console: _ => { });

            // startRun slotu SENKRON tutar; dispatch loop serildir — checkoutBranch okunduğunda run KESİNLİKLE aktiftir.
            var stdin = await SupervisorHostHarness.StdinWith(
                new StartRunCommand("r1", RunMode.Build, repo.RootPath, "Debug", 2),
                new CheckoutBranchCommand(repo.RootPath, "feature-x", IsRemote: false, StashIfDirty: false));
            var host = new SupervisorHost(writer, new NdjsonReader(stdin), job, coordinator,
                WorkspaceServices.Default(sandbox, _ => throw new NotSupportedException("bu testte restore yok")));
            Assert.Equal(0, await Task.Run(() => host.RunAsync()).WaitAsync(SupervisorHostHarness.Limit));

            var all = NdjsonWire.Parse(stdout.Text);
            Assert.Contains(all, e => e is ErrorEvent { Code: "checkoutRejected" });
            Assert.DoesNotContain(all, e => e is CheckoutCompletedEvent);
            Assert.Equal(main, repo.CurrentBranchName());

            releasePlanning.SetResult();
        }
        finally { SupervisorHostHarness.TryDeleteDirectories(sandbox); }
    }

    // Exception IPC sınırını ASLA geçmez (Clean/Optimize'ın aynı kuralı): tanımlı bir hata event'ine dönüşür ve
    // host yaşamaya devam eder — App o kodla chip kilidini açar.
    [Fact]
    public async Task An_unexpected_checkout_exception_becomes_error_checkoutFailed_not_a_crash()
    {
        string sandbox = Directory.CreateTempSubdirectory("bo-checkout-sb-").FullName;
        try
        {
            var stdout = new SupervisorHostHarness.CollectingStream();
            var writer = new NdjsonWriter(stdout);
            using var job = JobObject.CreateKillOnClose();
            using var coordinator = new RunCoordinator(
                planner: (_, _) => throw new InvalidOperationException("bu testte run yok"),
                msbuildFactory: _ => throw new InvalidOperationException("bu testte run yok"),
                logFactory: startedAt => new RunLogWriter(Path.Combine(sandbox, "logs"), startedAt),
                writer: writer, innerJob: job, nowMs: () => 0, console: _ => { });
            var services = WorkspaceServices.Default(sandbox, _ => throw new NotSupportedException("bu testte restore yok"))
                with { Git = _ => throw new InvalidOperationException("beklenmeyen hata") };

            var stdin = await SupervisorHostHarness.StdinWith(
                new CheckoutBranchCommand(sandbox, "feature-x", IsRemote: false, StashIfDirty: false), new PingCommand(7));
            var host = new SupervisorHost(writer, new NdjsonReader(stdin), job, coordinator, services);
            var crash = await Record.ExceptionAsync(() => Task.Run(() => host.RunAsync()).WaitAsync(SupervisorHostHarness.Limit));

            Assert.Null(crash);
            var all = NdjsonWire.Parse(stdout.Text);
            Assert.Contains(all, e => e is ErrorEvent { Code: "checkoutFailed" });
            Assert.Contains(all, e => e is PongEvent { Seq: 7 }); // host ayakta: sonraki komut hâlâ işleniyor
        }
        finally { SupervisorHostHarness.TryDeleteDirectories(sandbox); }
    }
}
