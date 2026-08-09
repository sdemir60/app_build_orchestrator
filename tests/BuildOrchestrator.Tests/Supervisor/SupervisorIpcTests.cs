using System.Diagnostics;
using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Supervisor;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.Supervisor;

public static class TestPaths
{
    public static string SupervisorExe => Path.Combine(AppContext.BaseDirectory, "BuildOrchestrator.Supervisor.exe");

    /// <summary>[B1/F1 · fix-1] GERÇEK bir Supervisor process'i başlatan her testin <see cref="SupervisorExe"/>
    /// ile birlikte <c>EngineHost</c>'a ENJEKTE ETTİĞİ startup timeout. Üretim varsayılanı (5 sn,
    /// <c>EngineHost.StartupTimeout</c>) yük altında yetmiyor: taze bir process doğup <c>engineReady</c>
    /// yazana kadar 5 sn'yi aşabiliyor ve test SEBEPSİZ kırmızı veriyor (ölçüm: task-B1-report.md İŞ 4,
    /// yük altındaki koşum). <b>Üretim varsayılanı DEĞİŞMEZ</b> — donmuş bir supervisor'da uygulamanın
    /// vazgeçmesi şart; genişleyen yalnız TEST beklemesidir.
    /// <para>Tek yer: aksi halde aynı sabit <c>EngineHostTests</c>/<c>RunViewModelTests</c>/
    /// <c>AppShutdownTests</c>'te üç ayrı kopya olarak yaşardı ve bir sonraki start-eden test yine
    /// yamasız kalırdı (fix-1 öncesi tam olarak bu oldu — 8 start noktasının yalnız 3'ü yamalıydı).</para></summary>
    public static readonly TimeSpan WideStartupTimeout = TimeSpan.FromSeconds(60);

    /// <summary>[cycle rounds] GERÇEK bir run'ın TAMAMLANMASINI bekleyen e2e testlerinin hang-guard'ı — bir
    /// PERF BÜTÇESİ DEĞİL, sonsuz beklemeyi test hatasına çeviren üst sınır (iddiaların hiçbiri süreye
    /// bakmaz). <b>Neden genişledi:</b> tek kullanıcısı <c>RunViewModelTests</c>'in Rebuild e2e testiydi ve
    /// 15 sn bekliyordu; o test X↔Y cycle fixture'ı sayesinde eskiden HİÇ <c>MSBuild.exe</c> child'ı
    /// doğurmuyordu (üyeler pre-skip ediliyordu). Dairesel bağımlılıklar artık turlarla DERLENDİĞİ için aynı
    /// fixture 2 tur × 2 üye = 4 gerçek invoke yapar: tek başına ~2 sn, ama TÜM süit paralel koşarken 15 sn
    /// aşıldı ve test sebepsiz kırmızı verdi (ölçüm: task-6 fix turu, <c>Category!=Acceptance</c> koşumu).
    /// Aynı iş yükünü bekleyen kardeş e2e testi (<c>RunCoordinatorTests</c>) zaten 30 sn'lik bir guard
    /// kullanıyor ve yük altında geçiyor; buradaki pay <see cref="WideStartupTimeout"/> ile aynı tutuldu —
    /// bu test AYRICA gerçek bir EngineHost başlatır.</summary>
    public static readonly TimeSpan WideRunTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Gerçek Supervisor process'ini stdio yönlendirmeli başlatır (RunCoordinatorTests da kullanır).</summary>
    /// <param name="worktreePoolDir">[A5/T69] Worktree havuz kökü — verilmezse üretim varsayılanı
    /// (<c>%LOCALAPPDATA%\BuildOrchestrator\worktrees</c>). Havuza dokunan testler KENDİ temp kökünü verir;
    /// kullanıcının gerçek havuzu ASLA hedef alınmaz (<c>--logs</c>'un cache/state için yaptığının aynısı).</param>
    /// <param name="debugHooks">[A13/B4] <c>debugSpawnChildren</c> kancasını açar. Varsayılan <c>false</c> =
    /// ÜRETİM yolu: kanca kapalıdır ve komut <c>error(debugHooksDisabled)</c> ile reddedilir.</param>
    public static ProcessStartInfo Psi(string? logsDir = null, string? worktreePoolDir = null, bool debugHooks = false)
    {
        var psi = new ProcessStartInfo(SupervisorExe)
        { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (logsDir is not null) { psi.ArgumentList.Add("--logs"); psi.ArgumentList.Add(logsDir); }
        if (worktreePoolDir is not null) { psi.ArgumentList.Add("--worktrees"); psi.ArgumentList.Add(worktreePoolDir); }
        if (debugHooks) psi.ArgumentList.Add(SupervisorHost.DebugHooksArg);
        return psi;
    }

    /// <summary>
    /// [A13/B4] <see cref="Psi"/>'nin İKİNCİ başlatma şekli: <c>JobProcessLauncher</c> yolu
    /// <see cref="ProcessStartInfo"/> değil ham bir komut satırı ister. Kendiliğinden argüman EKLEMEZ —
    /// çağıran ne verdiyse o. Kancasız (üretim yolu) başlatmalar bunu doğrudan kullanır.
    /// </summary>
    public static string SupervisorCommandLine(params string[] args) =>
        WindowsCommandLine.Build(SupervisorExe, args);

    /// <summary>[A13/B4] <see cref="SupervisorCommandLine"/>'ın debug kancaları AÇIK varyantı. Kancayı açan
    /// bayrak burada da AYNI tek sabitten (<see cref="SupervisorHost.DebugHooksArg"/>) gelir — testlere
    /// kopyalanmaz.</summary>
    /// <param name="extraArgs">Bayraktan ÖNCE eklenecek argümanlar (ör. <c>--logs &lt;dir&gt;</c>).</param>
    public static string DebugHooksCommandLine(params string[] extraArgs) =>
        SupervisorCommandLine([.. extraArgs, SupervisorHost.DebugHooksArg]);
}

public class SupervisorIpcTests
{
    private static ProcessStartInfo Psi(string? logsDir = null, string? worktreePoolDir = null, bool debugHooks = false)
        => TestPaths.Psi(logsDir, worktreePoolDir, debugHooks);

    [Fact]
    public async Task Stdout_is_ndjson_only_even_after_garbage_command() // [D4 — It-0 kabul maddesi]
    {
        using var p = Process.Start(Psi())!;
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
    // logNotFound döner + stdout NDJSON kalır" wiring'i doğrulanır (D4).
    [Fact]
    public async Task GetProjectLog_of_unknown_project_before_any_run_errors_and_stdout_stays_ndjson()
    {
        using var p = Process.Start(Psi())!;
        var writer = new NdjsonWriter(p.StandardInput.BaseStream);
        var reader = new NdjsonReader(p.StandardOutput.BaseStream);
        // [B1/F2] Gerçek Supervisor process'i başlatılıyor; 5s yük altında ölçülmüş bir flake'ti (bkz.
        // task-B1-brief.md). Üretimde bu bekleyişin karşılığı yok (App tarafı EngineHost.StartAsync üzerinden
        // KENDİ enjekte edilebilir timeout'unu kullanır) — burada yalnız test beklemesi genişletiliyor.
        Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(30)));

        await writer.WriteAsync(new GetProjectLogCommand(@"d:\yok\yok.csproj"));
        var err = Assert.IsType<ErrorEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("logNotFound", err.Code);

        await writer.WriteAsync(new ShutdownCommand());
        string rest = await p.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(5));
        foreach (var line in rest.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.NotNull(System.Text.Json.JsonSerializer.Deserialize<IpcEvent>(line, IpcJson.Options)); // D4: kalan satırlar da NDJSON
        await p.WaitForExitAsync(new CancellationTokenSource(2000).Token);
    }

    [Fact]
    public async Task StopRun_hard_terminates_inner_job_children_and_acks()
    {
        // [A13/B4] Öldürülecek child'lar debugSpawnChildren ile doğuruluyor; o kanca artık VARSAYILAN OLARAK
        // KAPALI, bu yüzden bayrak AÇIKÇA geçilir (test zayıflatılmadı — yalnız kancayı istediği bildiriliyor).
        using var p = Process.Start(Psi(debugHooks: true))!;
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
        using var p = Process.Start(IsolatedPsi())!;
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
        using var p = Process.Start(IsolatedPsi())!; // --debug-hooks YOK = üretimin başlattığı Supervisor
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
        using var outer = JobObject.CreateKillOnClose();
        using var iocp = outer.AttachCompletionPort(); // Launch'tan ÖNCE — kaçırılan doğum bildirimi olmasın

        using var supervisor = LaunchIsolatedSupervisorIn(outer); // --debug-hooks YOK = üretimin başlattığı Supervisor
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
        using var marker = LaunchIsolatedSupervisorIn(outer);

        var births = new List<(int Pid, string Name)>();
        while (true)
        {
            var n = iocp.WaitNext(TestPaths.WideStartupTimeout)
                ?? throw new TimeoutException("IOCP: marker dogum bildirimi gelmedi");
            if (n.MessageId != NativeMethods.JOB_OBJECT_MSG_NEW_PROCESS) continue;
            if (n.Pid == marker.Pid) break;
            births.Add((n.Pid, NameOfProcess(n.Pid)));
        }

        // VAKUM KARŞITI: port GERÇEKTEN doğum taşıyor — Supervisor'ın kendi doğumu listede. Bu kontrol
        // olmasaydı, IOCP hiç bildirim taşımadığında da aşağıdaki iddia yeşil kalırdı.
        Assert.Contains(supervisor.Pid, births.Select(b => b.Pid));

        // ASIL İDDİA — §6'nın ta kendisi: reddedilen komut `cmd.exe`/`powershell` DOĞURMADI.
        // İsim süzgeci (ham sayı değil) bilinçli: `CREATE_NO_WINDOW` ile başlatılan her console process'i
        // yanına bir console-host (conhost) doğurur ve o da job üyesi olur — ham doğum sayısı bu OS
        // artefaktı yüzünden anlamsızdır. Aynı gerekçe `KillMidBuildTests.IsMsBuildProcess`'te de var.
        string[] forbidden = ["cmd", "powershell", "pwsh"];
        var leaked = births.Where(b => forbidden.Contains(b.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        Assert.True(leaked.Count == 0,
            $"reddedilen komut process dogurmus: {string.Join(", ", leaked.Select(l => $"{l.Name}({l.Pid})"))}"
            + $" — job'da gorulen tum dogumlar: {string.Join(", ", births.Select(b => $"{b.Name}({b.Pid})"))}");
    }

    /// <summary>[A13/B4 · fix-1] Verilen job'da, İZOLE logs/worktree kökleriyle ve <b>bayraksız</b> (üretim
    /// yolu) bir Supervisor başlatır. Kullanıcının gerçek dosyalarına dokunulmaz (brief kural 4).</summary>
    private static JobChildProcess LaunchIsolatedSupervisorIn(JobObject job)
    {
        string sandbox = Directory.CreateTempSubdirectory("bo-ipc-").FullName;
        return JobProcessLauncher.Launch(job,
            TestPaths.SupervisorCommandLine("--logs", Path.Combine(sandbox, "logs"),
                                            "--worktrees", Path.Combine(sandbox, "worktrees")),
            new LaunchOptions(RedirectStdio: true));
    }

    /// <summary>Doğum ANINDA okunan process adı (hiçbir şey öldürülmediği için okunabilir); pid çoktan
    /// gitmişse ayırt edilebilir bir yer tutucu. <c>KillMidBuildTests.IsMsBuildProcess</c> ile aynı desen.</summary>
    private static string NameOfProcess(int pid)
    {
        try { return Process.GetProcessById(pid).ProcessName; }
        catch (ArgumentException) { return "(exited)"; }
    }

    [Fact] // pozitif — kapı AÇIK: kanca testler için çalışmaya DEVAM ediyor
    public async Task DebugSpawnChildren_still_spawns_a_real_child_when_the_supervisor_starts_with_debug_hooks()
    {
        using var p = Process.Start(IsolatedPsi(debugHooks: true))!;
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

    // ---------------------------------------------------------------- [A5/T69] sync / branch / worktree

    /// <summary>İzole bir Supervisor: kendi logs/cache kökü + kendi worktree havuzu (kullanıcının gerçek dosyaları korunur).</summary>
    /// <param name="debugHooks">[A13/B4] <c>debugSpawnChildren</c> kancasını açar; varsayılan KAPALI = üretim yolu.</param>
    private static ProcessStartInfo IsolatedPsi(bool debugHooks = false)
    {
        string sandbox = Directory.CreateTempSubdirectory("bo-ipc-").FullName;
        return Psi(Path.Combine(sandbox, "logs"), Path.Combine(sandbox, "worktrees"), debugHooks);
    }

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

        using var p = Process.Start(IsolatedPsi())!;
        var writer = new NdjsonWriter(p.StandardInput.BaseStream);
        var reader = new NdjsonReader(p.StandardOutput.BaseStream);
        // [B1/F2] bkz. GetProjectLog_of_unknown_project… testindeki not — aynı kök neden (taze Supervisor
        // process'i, yük altında 5s'de hazır olamayabiliyor), aynı dosyada tekrarlanan desen.
        Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(30)));

        await writer.WriteAsync(new SyncWorkspaceCommand(repo.RootPath, branch));
        var events = await ReadUntilAsync(reader, ev => ev is SyncCompletedEvent);

        // Diskriminatör SIRASI: syncStarted … workspaceTopology … syncCompleted
        Assert.IsType<SyncStartedEvent>(events[0]);
        int topologyAt = events.FindIndex(e => e is WorkspaceTopologyEvent);
        Assert.True(topologyAt > 0 && topologyAt < events.Count - 1);
        Assert.IsType<SyncCompletedEvent>(events[^1]);
        Assert.Contains(events, e => e is BuildPreviewEvent);
        Assert.Contains(events, e => e is SyncProgressEvent sp && sp.Line == $"▸ git fetch origin {branch}");

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

        using var p = Process.Start(IsolatedPsi())!;
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

    // Havuz izole ve BOŞ: listWorktrees boş envanter döner; deleteWorktree bilinmeyen bir ad için error döner
    // ama Supervisor AYAKTA kalır ve sonraki komutlara yanıt vermeye devam eder (per-command hata).
    [Fact]
    public async Task ListWorktrees_answers_with_an_inventory_and_delete_of_an_unknown_worktree_errors_without_killing_the_host()
    {
        using var repo = new GitTestRepo();
        SeedWorkspace(repo);

        using var p = Process.Start(IsolatedPsi())!;
        var writer = new NdjsonWriter(p.StandardInput.BaseStream);
        var reader = new NdjsonReader(p.StandardOutput.BaseStream);
        // [B1/F2] bkz. GetProjectLog_of_unknown_project… testindeki not — aynı kök neden (taze Supervisor
        // process'i, yük altında 5s'de hazır olamayabiliyor), aynı dosyada tekrarlanan desen.
        Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(30)));

        await writer.WriteAsync(new ListWorktreesCommand(repo.RootPath));
        var list = Assert.IsType<WorktreeListEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Empty(list.Worktrees); // havuz izole ve boş — ANA çalışma ağacı envantere GİRMEZ

        await writer.WriteAsync(new DeleteWorktreeCommand(repo.RootPath, "no-such-worktree"));
        var err = Assert.IsType<ErrorEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal("worktreeDeleteFailed", err.Code);

        await writer.WriteAsync(new PingCommand(9)); // host hâlâ canlı
        Assert.Equal(9, Assert.IsType<PongEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(5))).Seq);

        await writer.WriteAsync(new ShutdownCommand());
        await p.WaitForExitAsync(new CancellationTokenSource(5000).Token);
    }
}
