using System.Diagnostics;
using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Supervisor;
using BuildOrchestrator.Tests.Git;
using BuildOrchestrator.Tests.ProcessControl;

namespace BuildOrchestrator.Tests.Supervisor;

public class SupervisorIpcTests
{
    [Fact]
    public async Task Stdout_is_ndjson_only_even_after_garbage_command() // [D4 — It-0 kabul maddesi]
    {
        using var sandbox = new SupervisorSandbox();
        using var p = Process.Start(sandbox.Psi())!;
        await p.StandardInput.WriteLineAsync("""{"type":"ping","seq":1}""");
        await p.StandardInput.WriteLineAsync("bu bir NDJSON degil");
        await p.StandardInput.WriteLineAsync("""{"type":"shutdown"}""");
        // [B1/F2 · fix-1 sweep] Bu bekleme, gerçek Supervisor'ın BOOT'unu + üç komutu + shutdown'ı + EOF'u birlikte
        // kapsıyor; yani içinde F2'nin ölçülen kırılma noktası (boot) VAR ve 10 sn'lik payın büyük kısmını boot
        // yiyebilir. Aynı ilke, aynı tek sabit — bkz. TestPaths.WideStartupTimeout.
        string all = await p.StandardOutput.ReadToEndAsync().WaitAsync(TestPaths.WideStartupTimeout);
        await p.WaitForExitAsync(new CancellationTokenSource(2000).Token);
        var lines = all.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.NotEmpty(lines);
        foreach (var line in lines) // parse edilemeyen tek satır = D4 ihlali = FAIL
            Assert.NotNull(System.Text.Json.JsonSerializer.Deserialize<IpcEvent>(line, IpcJson.Options));
        Assert.Contains(lines, l => l.Contains("\"engineReady\""));
        Assert.Contains(lines, l => l.Contains("\"pong\""));
        Assert.Contains(lines, l => l.Contains("\"badCommand\""));
        Assert.Equal(0, p.ExitCode);
    }

    // [T28] getProjectLog artık AKTİF run'ın dizininden okur (bkz. RunCoordinator.TryGetProjectLogSnapshot) —
    // gerçek run/chunk/dikiş davranışı ProjectLogStreamTests.cs'te (in-process, sahte invoker, gerçek writer)
    // test edilir. Burada yalnız gerçek Supervisor process'i üzerinden "hiç run koşmadıysa bilinmeyen proje
    // 'log yok' cevabı alır + stdout NDJSON kalır" wiring'i doğrulanır (D4).
    // [idle-memory · I1 · DEĞİŞEN KURAL] Eski iddia (..._errors_and_stdout_stays_ndjson): cevap error(logNotFound). "Log yok"
    // artık isteğin kimliğini yankılayan tek, boş, bayraklı bir chunk'tır — gerekçe ProjectLogStreamTests'in
    // Unknown_project_log_is_answered_with_one_empty_not_found_chunk testinde.
    [Fact]
    public async Task GetProjectLog_of_unknown_project_before_any_run_answers_not_found_and_stdout_stays_ndjson()
    {
        using var sandbox = new SupervisorSandbox();
        using var p = Process.Start(sandbox.Psi())!;
        var writer = new NdjsonWriter(p.StandardInput.BaseStream);
        var reader = new NdjsonReader(p.StandardOutput.BaseStream);
        // [B1/F2] Gerçek Supervisor process'i başlatılıyor; 5s yük altında ölçülmüş bir flake'ti (bkz.
        // task-B1-brief.md). Üretimde bu bekleyişin karşılığı yok (App tarafı EngineHost.StartAsync üzerinden
        // KENDİ enjekte edilebilir timeout'unu kullanır) — burada yalnız test beklemesi genişletiliyor.
        Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(30)));

        await writer.WriteAsync(new GetProjectLogCommand(@"d:\yok\yok.csproj", RequestId: 3));
        var reply = Assert.IsType<ProjectLogChunkEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(reply.NotFound);
        Assert.Equal(3, reply.RequestId);

        await writer.WriteAsync(new ShutdownCommand());
        string rest = await p.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(5));
        foreach (var line in rest.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.NotNull(System.Text.Json.JsonSerializer.Deserialize<IpcEvent>(line, IpcJson.Options)); // D4: kalan satırlar da NDJSON
        await p.WaitForExitAsync(new CancellationTokenSource(2000).Token);
    }

    /// <summary>
    /// [idle-memory · dalga 3 · karar 37 · B1 fix 1 · I-2] GERÇEK Supervisor süreci — kendi koşusu yok (uyanmış ya da yeniden başlatılmış motorun
    /// durumu) — önceki bir motorun bıraktığı koşu klasöründeki proje logunu, isteğin adını verdiği klasörden okur. Kabul kuralı motorun kendi log
    /// kökünü ister; kök süreç içinde <c>--logs</c>'tan (ya da varsayılan yoldan) koordinatöre ulaşır — bu test o bağlantının kendisidir: kopsa
    /// in-process testler yeşil kalırdı (onlar kökü kendileri verir) ama her uyanmış motor yine "No log yet" derdi.
    /// <para>KIRMIZI (mutasyon: <c>Program</c> kökü koordinatöre vermiyordu): <c>NotFound</c> true, metin boş.</para>
    /// </summary>
    [Fact]
    public async Task GetProjectLog_of_an_earlier_engines_run_is_read_from_the_folder_the_request_names()
    {
        using var sandbox = new SupervisorSandbox();
        const string projectId = @"d:\repo\A\A.csproj";
        string runDirectory;
        using (var earlier = new BuildOrchestrator.Core.Logs.RunLogWriter(sandbox.LogsDir, DateTimeOffset.Now)) // önceki motorun koşusu
        {
            using (var log = earlier.OpenProjectLog(projectId))
            {
                log.AppendLine("line-1");
                log.AppendLine("line-2");
            }
            runDirectory = earlier.RunDirectory;
        }
        using var p = Process.Start(sandbox.Psi())!;
        var writer = new NdjsonWriter(p.StandardInput.BaseStream);
        var reader = new NdjsonReader(p.StandardOutput.BaseStream);
        Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TestPaths.WideStartupTimeout));

        await writer.WriteAsync(new GetProjectLogCommand(projectId, RequestId: 4, RunDirectory: runDirectory));
        var reply = Assert.IsType<ProjectLogChunkEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.False(reply.NotFound);
        Assert.Equal(4, reply.RequestId);
        Assert.Equal(2, reply.ThroughLineNumber);
        Assert.Equal("line-1" + Environment.NewLine + "line-2" + Environment.NewLine, reply.Text);

        await writer.WriteAsync(new ShutdownCommand());
        await p.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await p.WaitForExitAsync(new CancellationTokenSource(2000).Token);
    }

    [Fact]
    public async Task StopRun_hard_terminates_inner_job_children_and_acks()
    {
        // [A13/B4] Öldürülecek child'lar debugSpawnChildren ile doğuruluyor; o kanca artık VARSAYILAN OLARAK
        // KAPALI, bu yüzden bayrak AÇIKÇA geçilir (test zayıflatılmadı — yalnız kancayı istediği bildiriliyor).
        using var sandbox = new SupervisorSandbox();
        using var p = Process.Start(sandbox.Psi(debugHooks: true))!;
        var writer = new NdjsonWriter(p.StandardInput.BaseStream);
        var reader = new NdjsonReader(p.StandardOutput.BaseStream);
        // [B1/F2] bkz. GetProjectLog_of_unknown_project… testindeki not — aynı kök neden (taze Supervisor
        // process'i, yük altında 5s'de hazır olamayabiliyor), aynı dosyada tekrarlanan desen.
        Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(30)));
        await writer.WriteAsync(new DebugSpawnChildrenCommand(Count: 1, Breakaway: false));
        var spawned = Assert.IsType<DebugChildrenSpawnedEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(10)));
        await writer.WriteAsync(new StopRunCommand("r1", StopKind.Hard)); // T4 base: hard = TerminateJobObject(inner)
        var stopped = Assert.IsType<RunStoppedEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(stopped.WasHard);
        foreach (int pid in spawned.Pids)
        {
            try { await Process.GetProcessById(pid).WaitForExitAsync(new CancellationTokenSource(2000).Token); }
            catch (ArgumentException) { /* zaten öldü */ }
        }
        await writer.WriteAsync(new ShutdownCommand());
        await p.WaitForExitAsync(new CancellationTokenSource(2000).Token);
    }

    // [T20-b/K11] setPerfMode ARTIK unknownCommand DEĞİL: diskriminatör kayıtlı VE dispatch edilmiş olmalı.
    // Geçerli mod, aktif run yokken SESSİZ bir no-op'tur (uygulanacak MSBuild child'ı yoktur) — bu yüzden
    // "tanınıyor mu" sorusu ancak İKİNCİ, çözülemeyen mod için gelen error'ın KODUYLA kanıtlanır:
    // badPerfMode ⇒ komut dispatch edildi; unknownCommand ⇒ hiç bağlanmamış.
    [Fact]
    public async Task SetPerfMode_is_dispatched_and_an_unparseable_mode_answers_with_badPerfMode()
    {
        using var sandbox = new SupervisorSandbox();
        using var p = Process.Start(sandbox.Psi())!;
        var writer = new NdjsonWriter(p.StandardInput.BaseStream);
        var reader = new NdjsonReader(p.StandardOutput.BaseStream);
        // [B1/F2] bkz. GetProjectLog_of_unknown_project… testindeki not — aynı kök neden (taze Supervisor
        // process'i, yük altında 5s'de hazır olamayabiliyor), aynı dosyada tekrarlanan desen.
        Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(30)));

        await writer.WriteAsync(new SetPerfModeCommand("Light"));  // aktif run yok → hiçbir event YOK
        await writer.WriteAsync(new SetPerfModeCommand("Turbo"));  // tanınmayan profil adı
        var err = Assert.IsType<ErrorEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("badPerfMode", err.Code);
        Assert.Equal("Turbo", err.Message);

        await writer.WriteAsync(new ShutdownCommand());
        await p.WaitForExitAsync(new CancellationTokenSource(5000).Token);
    }

    // ---------------------------------------------------------------- [A13/B4] debugSpawnChildren kapısı
    // İKİ TARAFLI ayırt edicilik: aşağıdaki iki test AYNI kurulumdan yalnız BAYRAKLA ayrılır.
    //   negatif → bayrak YOKken komut REDDEDİLİR (üretim ikilisinin varsayılan yüzeyi)
    //   pozitif → bayrak VARken komut HÂLÂ çalışır (kapı, kancayı testler için öldürmedi)
    // Tek başına biri yetmez: yalnız negatif yeşilse kapı her şeyi reddediyor olabilir, yalnız pozitif
    // yeşilse kapı hiç kapanmıyor olabilir.

    [Fact] // negatif — kapı KAPALI
    public async Task DebugSpawnChildren_is_rejected_when_the_supervisor_starts_without_debug_hooks_and_stdout_stays_ndjson()
    {
        using var sandbox = new SupervisorSandbox();
        using var p = Process.Start(sandbox.Psi())!; // --debug-hooks YOK = üretimin başlattığı Supervisor
        var writer = new NdjsonWriter(p.StandardInput.BaseStream);
        var reader = new NdjsonReader(p.StandardOutput.BaseStream);
        // [B1/F2] Gerçek Supervisor process'i başlatılıyor — boot beklemesinin tek sahibi WideStartupTimeout.
        Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TestPaths.WideStartupTimeout));

        await writer.WriteAsync(new DebugSpawnChildrenCommand(Count: 1, Breakaway: false));

        // VAKUM KARŞITI (1): "bir şey döndü" YETMEZ — dönenin KODU tam olarak kapının kodu olmalı.
        // unknownCommand gelseydi komut hiç bağlanmamış olurdu (IPC sözleşmesi KIRILMIŞ olurdu);
        // debugChildrenSpawned gelseydi kapı hiç kapanmamış olurdu.
        var err = Assert.IsType<ErrorEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("debugHooksDisabled", err.Code);
        Assert.Contains(SupervisorHost.DebugHooksArg, err.Message, StringComparison.Ordinal); // hangi bayrakla açıldığı SÖYLENİYOR

        // VAKUM KARŞITI (2): reddetme host'u düşürmedi — sonraki komut hâlâ yanıtlanıyor.
        await writer.WriteAsync(new PingCommand(4));
        Assert.Equal(4, Assert.IsType<PongEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(5))).Seq);

        // VAKUM KARŞITI (3) — [D4] stdout YALNIZ NDJSON: yukarıda TÜKETİLEN satırlar zaten NdjsonReader'dan
        // geçti (parse edilemeyen bir satır orada patlardı), KALAN satırlar da burada tek tek çözülüyor.
        await writer.WriteAsync(new ShutdownCommand());
        string rest = await p.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(5));
        foreach (var line in rest.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.NotNull(System.Text.Json.JsonSerializer.Deserialize<IpcEvent>(line, IpcJson.Options));
        await p.WaitForExitAsync(new CancellationTokenSource(5000).Token);
    }

    /// <summary>
    /// [A13/B4 · fix-1] <b>Kalemin ASIL teslimi burada pinlenir.</b> Yukarıdaki negatif test yalnız
    /// PROTOKOLÜ ölçüyor (dönen <c>Code</c> + NDJSON + host ayakta); Supervisor önce çocukları doğurup
    /// SONRA reddetseydi o test yine yeşil kalırdı — review bunu bir mutasyonla kanıtladı (1646 testin
    /// tamamı yeşilken üretim ikilisi `cmd.exe`+`powershell` doğurabiliyordu). Bu test DAVRANIŞI ölçer:
    /// bayrak yokken komut geldiğinde <b>job'da Supervisor'dan başka HİÇBİR process doğmaz</b>.
    /// <para>Desen icat edilmedi — <see cref="CascadeKillTests"/>'te zaten kanıtlanmış olan outer Job +
    /// <c>AttachCompletionPort</c> + <c>JOB_OBJECT_MSG_NEW_PROCESS</c> okuması yeniden kullanılıyor.</para>
    /// </summary>
    [Fact] // negatif — DAVRANIŞ: reddedilen komut hiçbir çocuk process doğurmaz
    public async Task Rejected_debugSpawnChildren_spawns_no_cmd_or_powershell_child()
    {
        // Sandbox'lar job'dan ÖNCE bildirilir: ters sırada önce job (iki supervisor da ölür), sonra klasörler gider.
        using var sandbox = new SupervisorSandbox();
        using var markerSandbox = new SupervisorSandbox();
        using var outer = JobObject.CreateKillOnClose();
        using var iocp = outer.AttachCompletionPort(); // Launch'tan ÖNCE — kaçırılan doğum bildirimi olmasın

        using var supervisor = LaunchIsolatedSupervisorIn(outer, sandbox); // --debug-hooks YOK = üretimin başlattığı Supervisor
        var writer = new NdjsonWriter(supervisor.StandardInput!);
        var reader = new NdjsonReader(supervisor.StandardOutput!);
        Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TestPaths.WideStartupTimeout));

        // Count: 2 — kapı sızsaydı 2×cmd.exe + 2×powershell doğardı; sinyal geniş olsun.
        await writer.WriteAsync(new DebugSpawnChildrenCommand(Count: 2, Breakaway: false));
        var err = Assert.IsType<ErrorEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("debugHooksDisabled", err.Code);

        // RANDEVU (marker): aynı job'a İKİNCİ bir Supervisor doğuruyoruz. Sızan bir çocuğun doğum bildirimi
        // komut işlenirken kuyruklandığı için marker'ınkinden ÖNCE gelir ⇒ marker'ı gördüğümüz an doğum
        // kuyruğu tükenmiştir. Bekleme gerçek bir olaya bağlı (sleep/poll YOK [D8]).
        // <b>Neden çıkış değil doğum randevusu:</b> hiçbir şey öldürülmediği için doğan process'lerin ADI
        // hâlâ okunabilir; Supervisor'ın çıkışını beklesek sızan çocuklar inner Job kaskadıyla çoktan ölmüş
        // ve isimleri okunamaz olurdu (iddia yanlışlıkla yeşile düşerdi).
        using var marker = LaunchIsolatedSupervisorIn(outer, markerSandbox);

        var births = new List<(int Pid, string Name)>();
        JobBirths.Until(iocp, TestPaths.WideStartupTimeout, "IOCP: marker dogum bildirimi gelmedi", pid =>
        {
            if (pid == marker.Pid) return true;
            births.Add((pid, ProcessTree.NameOfProcess(pid))); // doğum ANINDA — hiçbir şey öldürülmediği için okunabilir
            return false;
        });

        // VAKUM KARŞITI: port GERÇEKTEN doğum taşıyor — Supervisor'ın kendi doğumu listede. Bu kontrol
        // olmasaydı, IOCP hiç bildirim taşımadığında da aşağıdaki iddia yeşil kalırdı.
        Assert.Contains(supervisor.Pid, births.Select(b => b.Pid));

        // ASIL İDDİA — §6'nın ta kendisi: reddedilen komut `cmd.exe`/`powershell` DOĞURMADI.
        // İsim süzgeci (ham sayı değil) bilinçli: `CREATE_NO_WINDOW` ile başlatılan her console process'i
        // yanına bir console-host (conhost) doğurur ve o da job üyesi olur — ham doğum sayısı bu OS
        // artefaktı yüzünden anlamsızdır. Aynı gerekçe `ProcessTree.IsMsBuildProcess`'te de var.
        string[] forbidden = ["cmd", "powershell", "pwsh"];
        var leaked = births.Where(b => forbidden.Contains(b.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        Assert.True(leaked.Count == 0,
            $"reddedilen komut process dogurmus: {string.Join(", ", leaked.Select(l => $"{l.Name}({l.Pid})"))}"
            + $" — job'da gorulen tum dogumlar: {string.Join(", ", births.Select(b => $"{b.Name}({b.Pid})"))}");
    }

    /// <summary>[A13/B4 · fix-1] Verilen job'da, İZOLE logs köküyle ve <b>bayraksız</b> (üretim
    /// yolu) bir Supervisor başlatır. Kullanıcının gerçek dosyalarına dokunulmaz (brief kural 4).</summary>
    private static JobChildProcess LaunchIsolatedSupervisorIn(JobObject job, SupervisorSandbox sandbox) =>
        JobProcessLauncher.Launch(job, sandbox.CommandLine(), new LaunchOptions(RedirectStdio: true));

    [Fact] // pozitif — kapı AÇIK: kanca testler için çalışmaya DEVAM ediyor
    public async Task DebugSpawnChildren_still_spawns_a_real_child_when_the_supervisor_starts_with_debug_hooks()
    {
        using var sandbox = new SupervisorSandbox();
        using var p = Process.Start(sandbox.Psi(debugHooks: true))!;
        var writer = new NdjsonWriter(p.StandardInput.BaseStream);
        var reader = new NdjsonReader(p.StandardOutput.BaseStream);
        Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TestPaths.WideStartupTimeout));

        await writer.WriteAsync(new DebugSpawnChildrenCommand(Count: 1, Breakaway: false));
        var spawned = Assert.IsType<DebugChildrenSpawnedEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(10)));

        // Vakum karşıtı: "event geldi" yetmez — bildirilen pid GERÇEK ve o an CANLI bir process olmalı
        // (yoksa GetProcessById ArgumentException fırlatır). Handle kill'den ÖNCE açılır (CascadeKillTests deseni).
        int pid = Assert.Single(spawned.Pids);
        using var child = Process.GetProcessById(pid);

        // Supervisor'ın inner Job'ı KILL_ON_JOB_CLOSE'dur: düzenli shutdown çocuğu da götürür (sleep/poll YOK).
        await writer.WriteAsync(new ShutdownCommand());
        await p.WaitForExitAsync(new CancellationTokenSource(5000).Token);
        await child.WaitForExitAsync(new CancellationTokenSource(5000).Token);
    }

    // ---------------------------------------------------------------- [spec 2026-09-18 §5.5] çökme kurtarması

    /// <summary>[spec 2026-09-18 §5.5 · karar 12] Önceki motor koşu ortasında ölmüş: <c>run-inflight.json</c> dolu
    /// kalmış. Yeni motor host'u kurmadan ÖNCE kurtarır — listedeki kayıtlı proje kanıtsız hata olur, dosya silinir
    /// ve sayı <c>engineReady.interruptedProjects</c> ile App'e gider (kaydı olmayan proje de sayılır: o da
    /// yeniden derlenecek).</summary>
    [Fact]
    public async Task A_supervisor_started_after_a_crash_recovers_and_reports_the_count()
    {
        using var sandbox = new SupervisorSandbox();
        string recorded = @"C:\r\A\A.csproj";
        var store = new BuildOrchestrator.Core.State.BuildStateStore(sandbox.CacheRoot);
        store.Upsert(new BuildState(recorded, "sigA", LastResult: BuildResult.Succeeded,
            LastRunAt: DateTimeOffset.UtcNow.AddDays(-1)));
        var crashed = new BuildOrchestrator.Core.State.InFlightLedger(sandbox.CacheRoot);
        crashed.Add(recorded);
        crashed.Add(@"C:\r\B\B.csproj");

        using var p = Process.Start(sandbox.Psi())!;
        var writer = new NdjsonWriter(p.StandardInput.BaseStream);
        var reader = new NdjsonReader(p.StandardOutput.BaseStream);
        var ready = Assert.IsType<EngineReadyEvent>(
            await reader.ReadAsync<IpcEvent>().WaitAsync(TestPaths.WideStartupTimeout));

        Assert.Equal(2, ready.InterruptedProjects);
        Assert.False(File.Exists(crashed.FilePath));
        Assert.Equal(BuildResult.Failed, store.Load()[recorded].LastResult);

        await writer.WriteAsync(new ShutdownCommand());
        await p.WaitForExitAsync(new CancellationTokenSource(5000).Token);
    }

    // ---------------------------------------------------------------- [A5/T69] sync / branch

    /// <summary>Tek projelik gerçek bir git repo (bir .csproj + onu içeren bir .sln).</summary>
    private static void SeedWorkspace(GitTestRepo repo)
    {
        repo.WriteFile(Path.Combine("src", "A", "A.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>A</AssemblyName>"
            + "<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repo.WriteFile(Path.Combine("src", "A", "A.cs"), "public class A { }");
        repo.WriteFile("Osys.sln",
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"A\", \"src\\A\\A.csproj\", \"{1}\"\nEndProject\n");
        repo.CommitAll("c1");
    }

    /// <summary><paramref name="stop"/> true dönene kadar event okur; okunan tüm event'leri döner.</summary>
    private static async Task<List<IpcEvent>> ReadUntilAsync(NdjsonReader reader, Func<IpcEvent, bool> stop)
    {
        var events = new List<IpcEvent>();
        while (true)
        {
            var ev = await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(60))
                ?? throw new InvalidOperationException("supervisor stdout kapandı");
            events.Add(ev);
            if (stop(ev)) return events;
        }
    }

    // [A5/T69] syncWorkspace ARTIK unknownCommand DEĞİL: gerçek fetch(ref-only) → tarama → plan → topoloji
    // akışı koşar. Repo'nun remote'u yoktur, bu yüzden bu aynı zamanda gerçek process üzerinden OFFLINE
    // DEGRADE yolunu da kanıtlar (fetch başarısız → warn + yerel HEAD, akış yine de tamamlanır).
    [Fact]
    public async Task SyncWorkspace_streams_topology_and_completion_and_stdout_stays_ndjson()
    {
        using var repo = new GitTestRepo();
        SeedWorkspace(repo);
        string branch = repo.CurrentBranchName();

        using var sandbox = new SupervisorSandbox();
        using var p = Process.Start(sandbox.Psi())!;
        var writer = new NdjsonWriter(p.StandardInput.BaseStream);
        var reader = new NdjsonReader(p.StandardOutput.BaseStream);
        // [B1/F2] bkz. GetProjectLog_of_unknown_project… testindeki not — aynı kök neden (taze Supervisor
        // process'i, yük altında 5s'de hazır olamayabiliyor), aynı dosyada tekrarlanan desen.
        Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(30)));

        await writer.WriteAsync(new SyncWorkspaceCommand(repo.RootPath, branch));
        var events = await ReadUntilAsync(reader, ev => ev is SyncCompletedEvent);

        // Diskriminatör SIRASI: syncStarted … syncDiscovery … workspaceTopology … syncCompleted
        Assert.IsType<SyncStartedEvent>(events[0]);
        int topologyAt = events.FindIndex(e => e is WorkspaceTopologyEvent);
        Assert.True(topologyAt > 0 && topologyAt < events.Count - 1);
        // [design v1.24.0] Keşif sayacı Core'dan emit edildiği anda tele gider — Supervisor'da ayrı bir yol yok.
        int discoveryAt = events.FindIndex(e => e is SyncDiscoveryEvent);
        Assert.True(discoveryAt > 0 && discoveryAt < topologyAt,
            $"syncDiscovery syncStarted ile workspaceTopology arasında bekleniyor; gelen sıra: "
            + string.Join(", ", events.Select(e => e.GetType().Name)));
        Assert.Equal(new SyncDiscoveryEvent(RepositoryProjects: 1, ExternalProjects: 0), events[discoveryAt]);
        Assert.IsType<SyncCompletedEvent>(events[^1]);
        Assert.Contains(events, e => e is BuildPreviewEvent);
        Assert.Contains(events, e => e is SyncProgressEvent sp && sp.Line == $"git fetch origin {branch}");

        var topology = (WorkspaceTopologyEvent)events[topologyAt];
        var node = Assert.Single(topology.Nodes);
        Assert.Equal("A", node.Name);
        Assert.True(node.WillBuild);                                  // will-build pass process ucunda da koştu
        Assert.Equal("Osys", Assert.Single(topology.Solutions).Name); // Open-in-VS (E1) verisi taşınıyor

        var done = (SyncCompletedEvent)events[^1];
        Assert.Equal(1, done.ProjectCount);
        Assert.Equal(1, done.ToBuildCount);
        Assert.True(done.FetchDegraded);                              // remote yok → degrade, ama Sync TAMAMLANDI

        await writer.WriteAsync(new ShutdownCommand());
        string rest = await p.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(5));
        foreach (var line in rest.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.NotNull(System.Text.Json.JsonSerializer.Deserialize<IpcEvent>(line, IpcJson.Options)); // [D4]
        await p.WaitForExitAsync(new CancellationTokenSource(5000).Token);
    }

    [Fact]
    public async Task ListBranches_answers_with_local_and_remote_tracking_refs()
    {
        using var repo = new GitTestRepo();
        SeedWorkspace(repo);
        string active = repo.CurrentBranchName();
        repo.CreateBranch("feature-x");

        using var sandbox = new SupervisorSandbox();
        using var p = Process.Start(sandbox.Psi())!;
        var writer = new NdjsonWriter(p.StandardInput.BaseStream);
        var reader = new NdjsonReader(p.StandardOutput.BaseStream);
        // [B1/F2] bkz. GetProjectLog_of_unknown_project… testindeki not — aynı kök neden (taze Supervisor
        // process'i, yük altında 5s'de hazır olamayabiliyor), aynı dosyada tekrarlanan desen.
        Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(30)));

        await writer.WriteAsync(new ListBranchesCommand(repo.RootPath));
        var list = Assert.IsType<BranchListEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(30)));

        var activeRef = Assert.Single(list.Branches, b => b.Name == active);
        Assert.True(activeRef.IsActive);
        Assert.False(activeRef.IsRemoteTracking);
        Assert.Equal(40, activeRef.Sha.Length);                       // sha GERÇEKTEN çözülmüş
        var feature = Assert.Single(list.Branches, b => b.Name == "feature-x");
        Assert.False(feature.IsActive);

        await writer.WriteAsync(new ShutdownCommand());
        await p.WaitForExitAsync(new CancellationTokenSource(5000).Token);
    }
}
