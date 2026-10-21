using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Diagnostics;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Supervisor;
using BuildOrchestrator.Tests.App;
using BuildOrchestrator.Tests.Git;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.ByteUnits;

namespace BuildOrchestrator.Tests.Diagnostics;

/// <summary>
/// [PERF Faz C/C3] Bellek tanı satırının biçimi ve süreç okuması. Biçim <see cref="MemoryLine"/>'da TEK yerdedir; testler
/// ikinci bir kopya tutmaz, yalnız sözleşmeyi sabitler: alan sırası, birim, yuvarlama, kültürsüzlük, hata yutma.
///
/// <para>[idle-memory · spec §6.6] Okuma (<see cref="MemoryLine.Read"/>) ve MB biçimi (<see cref="MemoryLine.Megabytes"/>) da
/// tektir: satır ve App'in tanı satırları (About'un kopyalanan tanı metni) ikisini de kullanır — iki yüzeyin sayıları aynı kuralla
/// yazılır.</para>
/// </summary>
public class MemoryLineTests
{
    /// <summary>Yuvarlama tablosu — satır ve MB biçimi AYNI tabloyla sınanır (tablo tek yerde).</summary>
    public static TheoryData<long, string> Rounding => new()
    {
        { 0L, "0" },
        { Mb / 2 - 1, "0" },
        { Mb / 2, "1" },
        { 5 * Mb / 2, "3" },
        { 5L * 1024 * Mb, "5120" },
    };

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
    [MemberData(nameof(Rounding))]
    public void Format_rounds_each_value_half_away_from_zero_without_digit_grouping(long bytes, string expected) =>
        Assert.Equal($"memory: private={expected} committed={expected} heap={expected}", MemoryLine.Format(bytes, bytes, bytes));

    /// <summary>[idle-memory · spec §6.6] MB biçimi tek başına da satırınkiyle AYNI kuraldır: App'in tanı satırları (About'un
    /// kopyalanan tanı metni) sayıyı buradan alır, kendi yuvarlamasını yazmaz.</summary>
    [Theory]
    [MemberData(nameof(Rounding))]
    public void Megabytes_rounds_half_away_from_zero_without_digit_grouping_like_the_line(long bytes, string expected) =>
        Assert.Equal(expected, MemoryLine.Megabytes(bytes));

    /// <summary>[kaynak şeridi] Aynı kuralın SAYI hâli: şerit GB'ye geçişi için yazıyı geri okumak (<c>long.Parse</c>) yerine sayıyı alır;
    /// <see cref="MemoryLine.Megabytes"/> bu sayının yazısıdır, yani ikisi tek tablodan okunur.</summary>
    [Theory]
    [MemberData(nameof(Rounding))]
    public void RoundedMegabytes_is_the_number_behind_the_megabytes_text(long bytes, string expected) =>
        Assert.Equal(long.Parse(expected, CultureInfo.InvariantCulture), MemoryLine.RoundedMegabytes(bytes));

    /// <summary>"Yarımlar sıfırdan uzağa" eksi tarafta da geçer (yarım MB'nin eksisi −1'dir, 0 değil): <c>(bayt + yarım) / MB</c> gibi
    /// tam sayı kısayolları bu yönde ayrışırdı. Sayı ve yazı aynı değerdir.</summary>
    [Theory]
    [InlineData(-Mb / 2, -1L)]
    [InlineData(-Mb / 2 + 1, 0L)]
    [InlineData(-5 * Mb / 2, -3L)]
    public void RoundedMegabytes_rounds_negative_halves_away_from_zero_too(long bytes, long expected)
    {
        Assert.Equal(expected, MemoryLine.RoundedMegabytes(bytes));
        Assert.Equal(expected.ToString(CultureInfo.InvariantCulture), MemoryLine.Megabytes(bytes));
    }

    /// <summary>Süreç okuması AYNI biçimi üretir; çalışan bir süreç en az bir MB özel bellek tutar.</summary>
    [Fact]
    public void Current_reads_this_process_into_the_same_line_shape()
    {
        string line = MemoryLine.Current();

        var match = Regex.Match(line, @"^memory: private=(\d+) committed=(\d+) heap=(\d+)$");
        Assert.True(match.Success, line);
        Assert.True(long.Parse(match.Groups[1].Value) >= 1, "a running process holds at least a megabyte of private bytes: " + line);
    }

    /// <summary>[idle-memory · spec §6.6] Süreç okumasının kendisi (satırı ve App'in tanı satırlarını besleyen TEK okuma): çalışan
    /// bir süreç en az bir MB özel bellek ve boş olmayan bir yönetilen yığın tutar; GC'nin taahhüdü negatif olamaz (hiç GC
    /// olmadıysa 0).</summary>
    [Fact]
    public void Read_samples_this_process()
    {
        var reading = MemoryLine.Read();

        Assert.True(reading.PrivateBytes >= Mb, $"a running process holds at least a megabyte of private bytes: {reading}");
        Assert.True(reading.HeapBytes > 0, $"a running process has a managed heap: {reading}");
        Assert.True(reading.CommittedBytes >= 0, reading.ToString());
    }

    /// <summary>[idle-memory · final review (Tasks 9-10)] Tanı ekranının (About) okuması: <see cref="MemoryLine.Read"/>'in korumalı
    /// hâli. Çalışan bir süreçte okuma başarılır ve <c>Read</c> ile aynı büyüklükte değerler verir.</summary>
    [Fact]
    public void TryRead_returns_the_reading_of_this_process()
    {
        var reading = MemoryLine.TryRead();

        Assert.NotNull(reading);
        Assert.True(reading.Value.PrivateBytes >= Mb, $"a running process holds at least a megabyte of private bytes: {reading}");
        Assert.True(reading.Value.HeapBytes > 0, reading.ToString());
    }

    /// <summary>
    /// Bir tanı, tanı ekranını bozamaz: süreç sorgusu patlarsa (izin, kapanan süreç) <c>TryRead</c> <c>null</c> döner, fırlatmaz.
    /// Eskiden About'un okuması <see cref="MemoryLine.Read"/>'i doğrudan çağırıyordu — fırlatsa diyalog hiç açılmazdı;
    /// <see cref="MemoryLine.Report"/> motorun satırı için aynı güvenceyi baştan veriyordu. Okuyucu parametreli aşırı yükleme
    /// patlayan bir okumayı taklit etmenin yoludur: <c>internal</c>'dır ve Core derlemesi internal'larını bu test derlemesine açar
    /// (<c>InternalsVisibleTo</c>, Core.csproj).
    /// </summary>
    [Fact]
    public void TryRead_returns_null_instead_of_throwing_when_the_read_fails() =>
        Assert.Null(MemoryLine.TryRead(() => throw new InvalidOperationException("access denied")));

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
/// koleksiyondur, stdout gözlenen bir stream. İki yazım noktası da sınanır: Sync bitişi (host) ve koşu bitişi (koordinatör);
/// ikisi de SONUCU NE OLURSA OLSUN tek satır bırakır — Sync: tamamlanma, beklenmeyen hata, iptal · koşu: tamamlanma, Stop,
/// planFailed, beklenmeyen hata.
/// </summary>
public class MemoryLineWiringTests
{
    internal static bool IsMemoryLine(string line) => line.StartsWith(MemoryLine.Prefix, StringComparison.Ordinal);

    /// <summary>Konsola TEK bellek satırı düşmüştür.</summary>
    private static void AssertSingleMemoryLine(List<string> console)
    {
        lock (console) Assert.Single(console, IsMemoryLine);
    }

    /// <summary>
    /// Host'u tek bir <c>syncWorkspace</c> komutuyla kurup stdin EOF'a kadar koşturur; konsol kanalı ve stdout çağıranın
    /// koleksiyonlarıdır. <paramref name="configure"/> null ise üretim bağlaması (gerçek Sync servisi, izole defter kökü); doluysa servis kümesi onunla uyarlanır (ör.
    /// yalnız Sync fabrikası). Optimize bu testlerde çağrılmaz.
    /// </summary>
    private static async Task<int> RunSyncHostAsync(string sandbox, SyncWorkspaceCommand command, List<string> console,
        SupervisorHostHarness.CollectingStream stdout, Func<WorkspaceServices, WorkspaceServices>? configure = null)
    {
        var writer = new NdjsonWriter(stdout);
        var stdin = await SupervisorHostHarness.StdinWith(command);

        using var job = JobObject.CreateKillOnClose();
        using var coordinator = SupervisorHostHarness.NewIdleCoordinator(job, writer, line => { lock (console) console.Add(line); },
            logsRoot: Path.Combine(sandbox, "logs"));
        var services = WorkspaceServices.Default(sandbox, _ => throw new NotSupportedException("no restore in this test"));
        if (configure is not null) services = configure(services);
        var host = new SupervisorHost(writer, new NdjsonReader(stdin), job, coordinator, services);

        return await Task.Run(() => host.RunAsync()).WaitAsync(SupervisorHostHarness.Limit);
    }

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
            var console = new List<string>();

            Assert.Equal(0, await RunSyncHostAsync(
                sandbox, new SyncWorkspaceCommand(repo.RootPath, repo.CurrentBranchName()), console, stdout));

            var wire = NdjsonWire.Parse(stdout.Text); // NDJSON olmayan satır [D4] burada patlar
            Assert.Contains(wire, e => e is SyncStartedEvent); // Sync gerçekten koştu
            AssertSingleMemoryLine(console);
            Assert.DoesNotContain(MemoryLine.Prefix, stdout.Text);
        }
        finally { SupervisorHostHarness.TryDeleteDirectories(sandbox); }
    }

    /// <summary>
    /// [son toparlama B2 · M3] Beklenmeyen bir hata da Sync'in bir SONUCUDUR: <c>planFailed</c> yolunda da konsola TEK satır
    /// düşer (satır <c>finally</c>'dedir, koşu yoluyla simetrik). Eskiden satır yalnız <c>RunAsync</c> normal dönünce
    /// yazılıyordu; ARCHITECTURE §11.4 ise "sonucu ne olursa olsun" diyordu.
    /// </summary>
    [Fact]
    public async Task MemoryLine_follows_a_sync_that_fails_unexpectedly_too()
    {
        string sandbox = Directory.CreateTempSubdirectory("bo-memline-").FullName;
        try
        {
            var stdout = new SupervisorHostHarness.CollectingStream();
            var console = new List<string>();
            var command = new SyncWorkspaceCommand(Path.Combine(sandbox, "workspace"), "main");

            Assert.Equal(0, await RunSyncHostAsync(sandbox, command, console, stdout,
                configure: s => s with { Sync = _ => throw new InvalidOperationException("sync service exploded") }));

            Assert.Contains(NdjsonWire.Parse(stdout.Text), e => e is ErrorEvent { Code: "planFailed" }); // yol gerçekten koştu
            AssertSingleMemoryLine(console);   // KIRMIZI: planFailed yolunda satır yazılmıyordu
            Assert.DoesNotContain(MemoryLine.Prefix, stdout.Text);
        }
        finally { SupervisorHostHarness.TryDeleteDirectories(sandbox); }
    }

    /// <summary>[son toparlama B2 · M3] İptal de bir sonuçtur: iptal yolunda da (istisna yayılırken) TEK satır düşer.</summary>
    [Fact]
    public async Task MemoryLine_follows_a_cancelled_sync_too()
    {
        string sandbox = Directory.CreateTempSubdirectory("bo-memline-").FullName;
        try
        {
            var stdout = new SupervisorHostHarness.CollectingStream();
            var console = new List<string>();
            var command = new SyncWorkspaceCommand(Path.Combine(sandbox, "workspace"), "main");

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunSyncHostAsync(sandbox, command, console, stdout,
                configure: s => s with { Sync = _ => throw new OperationCanceledException() }));

            AssertSingleMemoryLine(console);   // KIRMIZI: iptal yolunda satır yazılmıyordu
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
        AssertSingleMemoryLine(h.ConsoleLines);
        Assert.DoesNotContain(MemoryLine.Prefix, Encoding.UTF8.GetString(stdout.ToArray()));
    }

    /// <summary>
    /// [son toparlama B2 · M4] Koşunun diğer normal çıkışları da tek satır bırakır: satır <c>ExecuteRunAsync</c>'in
    /// <c>finally</c>'sindedir (yuva bırakıldıktan sonra). Planlama düşer (<c>planFailed</c>) ve koşu hiç başlamaz.
    /// </summary>
    [Fact]
    public async Task MemoryLine_follows_a_run_whose_planning_fails()
    {
        var invoker = new RunCoordinatorTests.FakeInvoker((_, _, _) => Task.FromResult(RunCoordinatorTests.Ok()));
        using var h = new RunCoordinatorTests.Harness(RunCoordinatorTests.PlanOf(RunCoordinatorTests.Node("A")), invoker,
            planner: (_, _) => throw new IOException("disk unreadable"));

        await h.Sut.StartAsync(RunCoordinatorTests.Start(), default);
        await h.Sut.RunCompletion.WaitAsync(RunCoordinatorTests.Limit);

        Assert.Contains(h.Events, e => e is ErrorEvent { Code: "planFailed" }); // çıkışın türü
        AssertSingleMemoryLine(h.ConsoleLines);
    }

    /// <summary>[son toparlama B2 · M4] Koşu Stop ile biter: runStopped yazılır ve satır yine tek.</summary>
    [Fact]
    public async Task MemoryLine_follows_a_run_that_is_stopped()
    {
        var inFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invoker = new RunCoordinatorTests.FakeInvoker(async (_, _, _) =>
        {
            inFlight.TrySetResult();
            await release.Task;
            return RunCoordinatorTests.Ok();
        });
        using var h = new RunCoordinatorTests.Harness(
            RunCoordinatorTests.PlanOf(RunCoordinatorTests.Node("A"), RunCoordinatorTests.Node("B")), invoker);

        await h.Sut.StartAsync(RunCoordinatorTests.Start(parallelism: 1), default);
        await inFlight.Task.WaitAsync(RunCoordinatorTests.Limit);
        Assert.True(h.Sut.TryRequestStop(StopKind.Graceful));
        release.SetResult();
        await h.Sut.RunCompletion.WaitAsync(RunCoordinatorTests.Limit);

        Assert.Contains(h.Events, e => e is RunStoppedEvent); // çıkışın türü
        AssertSingleMemoryLine(h.ConsoleLines);
    }

    /// <summary>
    /// [son toparlama B2 · M4] Beklenmeyen hata (<c>runFailed</c>): planlayıcının yakalanmayan bir istisnası koşuyu düşürür,
    /// satır yine tek. Yakalanan türler (IO, izin, argüman, dış hazırlık) <c>planFailed</c>'a gider; <see cref="InvalidOperationException"/> gitmez.
    /// </summary>
    [Fact]
    public async Task MemoryLine_follows_a_run_that_fails_unexpectedly()
    {
        var invoker = new RunCoordinatorTests.FakeInvoker((_, _, _) => Task.FromResult(RunCoordinatorTests.Ok()));
        using var h = new RunCoordinatorTests.Harness(RunCoordinatorTests.PlanOf(RunCoordinatorTests.Node("A")), invoker,
            planner: (_, _) => throw new InvalidOperationException("planner exploded"));

        await h.Sut.StartAsync(RunCoordinatorTests.Start(), default);
        await h.Sut.RunCompletion.WaitAsync(RunCoordinatorTests.Limit);

        Assert.Contains(h.Events, e => e is ErrorEvent { Code: "runFailed" }); // çıkışın türü
        AssertSingleMemoryLine(h.ConsoleLines);
    }

    /// <summary>
    /// [son toparlama B2 · M6] Üretim bağı: koordinatörün konsol kanalı <c>Console.Error.WriteLine</c>'dır ve kaçak
    /// <c>Console.WriteLine</c> stderr'e gider (<c>Console.SetOut(Console.Error)</c>) — stdout YALNIZ NDJSON [D4]. Testler kanalı
    /// bir koleksiyonla değiştirdiği için bağın kendisini yalnız kaynak sabitler. Yalnız KOD taranır (yorum ve literaller
    /// ayıklanır): yorum olarak kopyalanmış bir bağ, gerçeği silinse de guard'ı tatmin ederdi.
    /// </summary>
    [Fact]
    public void The_engine_wires_its_console_channel_to_stderr_in_production()
    {
        string source = SourceLiterals.CodeOnly(
            File.ReadAllText(Path.Combine(RepoPaths.SrcRoot, "BuildOrchestrator.Supervisor", "Program.cs")));

        Assert.Matches(new Regex(@"\bconsole:\s*Console\.Error\.WriteLine\b"), source);
        Assert.Matches(new Regex(@"\bConsole\.SetOut\(\s*Console\.Error\s*\)"), source);
    }
}
