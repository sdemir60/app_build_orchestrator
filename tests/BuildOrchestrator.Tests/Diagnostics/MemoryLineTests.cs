using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Diagnostics;
using BuildOrchestrator.Core.Logs;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Supervisor;
using BuildOrchestrator.Tests.Git;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.Diagnostics;

/// <summary>
/// [PERF Faz C/C3] Bellek tanı satırının biçimi ve süreç okuması. Biçim <see cref="MemoryLine"/>'da TEK yerdedir; testler
/// ikinci bir kopya tutmaz, yalnız sözleşmeyi sabitler: alan sırası, birim, yuvarlama, kültürsüzlük, hata yutma.
/// </summary>
public class MemoryLineTests
{
    private const long Mb = 1024 * 1024;

    /// <summary>Üç değer sabit sırayla (private, committed, heap) ve MB olarak yazılır; satır <see cref="MemoryLine.Prefix"/> ile başlar.</summary>
    [Fact]
    public void Format_writes_private_committed_and_heap_in_that_order_in_megabytes()
    {
        string line = MemoryLine.Format(309 * Mb, 250 * Mb, 8 * Mb);

        Assert.Equal("memory: private=309 committed=250 heap=8", line);
        Assert.StartsWith(MemoryLine.Prefix, line, StringComparison.Ordinal);
    }

    /// <summary>
    /// En yakın tam MB'ye, yarımlar sıfırdan UZAĞA yuvarlanır (2,5 MB → 3: bankacı yuvarlaması 2 verirdi); int sınırını
    /// aşan bayt sayısı (5 GB) taşmaz ve binlik ayırıcı almaz.
    /// </summary>
    [Theory]
    [InlineData(0L, "0")]
    [InlineData(Mb / 2 - 1, "0")]
    [InlineData(Mb / 2, "1")]
    [InlineData(5 * Mb / 2, "3")]
    [InlineData(5L * 1024 * Mb, "5120")]
    public void Format_rounds_each_value_half_away_from_zero_without_digit_grouping(long bytes, string expected) =>
        Assert.Equal($"memory: private={expected} committed={expected} heap={expected}", MemoryLine.Format(bytes, bytes, bytes));

    /// <summary>Süreç okuması AYNI biçimi üretir; çalışan bir süreç en az bir MB özel bellek tutar.</summary>
    [Fact]
    public void Current_reads_this_process_into_the_same_line_shape()
    {
        string line = MemoryLine.Current();

        var match = Regex.Match(line, @"^memory: private=(\d+) committed=(\d+) heap=(\d+)$");
        Assert.True(match.Success, line);
        Assert.True(long.Parse(match.Groups[1].Value) >= 1, "a running process holds at least a megabyte of private bytes: " + line);
    }

    [Fact]
    public void Report_hands_the_current_line_to_the_sink()
    {
        var lines = new List<string>();

        MemoryLine.Report(lines.Add);

        Assert.StartsWith(MemoryLine.Prefix, Assert.Single(lines), StringComparison.Ordinal);
    }

    /// <summary>Tanı bir Sync'i ya da koşu kapanışını bozmamalı: kanal yazımı patlarsa (ör. kapanmış stderr pipe'ı) hata yutulur.</summary>
    [Fact]
    public void Report_swallows_a_failing_sink_because_a_diagnostic_must_never_break_the_work() =>
        MemoryLine.Report(_ => throw new IOException("stderr pipe closed"));
}

/// <summary>
/// [PERF Faz C/C3] Satırın NEREYE gittiği: konsol (stderr) kanalına, stdout'a DEĞİL. Üretimde koordinatörün
/// <c>console</c> kanalı <c>Console.Error.WriteLine</c>'dır (<c>Program.Main</c>) ve stdout YALNIZ NDJSON'dır [D4]. Gerçek
/// process açılmaz (<c>SupervisorIsolationGuardTests</c> kuralı): host ve koordinatör bellek içinde kurulur, kanal bir
/// koleksiyondur, stdout gözlenen bir stream. İki yazım noktası da sınanır: Sync bitişi (host) ve koşu bitişi (koordinatör).
/// </summary>
public class MemoryLineWiringTests
{
    /// <summary>Bir Sync bitince konsola TEK satır düşer; telde (stdout) hiçbir iz yoktur.</summary>
    [Fact]
    public async Task MemoryLine_follows_a_finished_sync_on_the_console_and_never_reaches_stdout()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("README.md", "# memory line");
        repo.CommitAll("c1");
        string sandbox = Directory.CreateTempSubdirectory("bo-memline-").FullName;
        try
        {
            var stdout = new SupervisorHostHarness.CollectingStream();
            var writer = new NdjsonWriter(stdout);
            var stdin = await SupervisorHostHarness.StdinWith(new SyncWorkspaceCommand(repo.RootPath, repo.CurrentBranchName()));
            var console = new List<string>();

            using var job = JobObject.CreateKillOnClose();
            using var coordinator = new RunCoordinator(
                planner: (_, _) => throw new InvalidOperationException("no run in this test"),
                msbuildFactory: _ => throw new InvalidOperationException("no run in this test"),
                logFactory: startedAt => new RunLogWriter(Path.Combine(sandbox, "logs"), startedAt),
                writer: writer, innerJob: job, nowMs: () => 0, console: line => { lock (console) console.Add(line); });
            // Üretim kompozisyonu (gerçek Sync servisi, izole defter kökü); Optimize bu testte çağrılmaz.
            var host = new SupervisorHost(writer, new NdjsonReader(stdin), job, coordinator,
                WorkspaceServices.Default(sandbox, _ => throw new NotSupportedException("no restore in this test")));

            Assert.Equal(0, await Task.Run(() => host.RunAsync()).WaitAsync(SupervisorHostHarness.Limit));

            var wire = NdjsonWire.Parse(stdout.Text); // NDJSON olmayan satır [D4] burada patlar
            Assert.Contains(wire, e => e is SyncStartedEvent); // Sync gerçekten koştu
            lock (console)
                Assert.Single(console, l => l.StartsWith(MemoryLine.Prefix, StringComparison.Ordinal));
            Assert.DoesNotContain(MemoryLine.Prefix, stdout.Text);
        }
        finally { SupervisorHostHarness.TryDeleteDirectories(sandbox); }
    }

    /// <summary>Bir koşu bitince konsola TEK satır düşer; telde (stdout) hiçbir iz yoktur.</summary>
    [Fact]
    public async Task MemoryLine_follows_a_finished_run_on_the_console_and_never_reaches_stdout()
    {
        var stdout = new MemoryStream();
        var invoker = new RunCoordinatorTests.FakeInvoker((_, _, _) => Task.FromResult(RunCoordinatorTests.Ok()));
        using var h = new RunCoordinatorTests.Harness(
            RunCoordinatorTests.PlanOf(RunCoordinatorTests.Node("A")), invoker, output: stdout);

        await h.Sut.StartAsync(RunCoordinatorTests.Start(), default);
        await h.Sut.RunCompletion.WaitAsync(RunCoordinatorTests.Limit);

        Assert.IsType<RunCompletedEvent>(h.Events[^1]); // koşu gerçekten sonuna kadar koştu
        lock (h.ConsoleLines)
            Assert.Single(h.ConsoleLines, l => l.StartsWith(MemoryLine.Prefix, StringComparison.Ordinal));
        Assert.DoesNotContain(MemoryLine.Prefix, Encoding.UTF8.GetString(stdout.ToArray()));
    }
}
