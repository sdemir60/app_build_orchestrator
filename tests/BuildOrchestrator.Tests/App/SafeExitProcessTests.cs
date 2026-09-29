using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Tests.Git;
using BuildOrchestrator.Tests.MsBuild;
using BuildOrchestrator.Tests.Supervisor;
using Microsoft.Win32.SafeHandles;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [P3 · Task 5] Güvenli tam çıkıştan (<see cref="RunViewModel.RequestExit"/>) sonra HİÇBİR Supervisor ya da
/// <c>MSBuild.exe</c> process'i kalmaz ve koşan derleme öldürülmeden BİTER. <see cref="SafeExitTests"/> sırayı VM
/// düzeyinde motorsuz pinler; bu test aynı sıranın gerçek Supervisor ve gerçek MSBuild ağacında tuttuğunu ölçer.
///
/// <para><b>Sıra üretimdekidir:</b> motor hazır → açılışın Sync'i → Build → <c>MSBuild.exe</c>'ler uçuşta →
/// <see cref="RunViewModel.RequestExit"/> → graceful <c>stopRun</c> → drain → <see cref="RunViewModel.ExitReady"/> →
/// <c>App.OnExit</c>'in yaptığı <see cref="AppShutdown.WaitForAsyncDisposal"/>. Kabuğun payını (MainWindow'un
/// ExitReady → Shutdown → OnExit zinciri) test gövdesi oynar; OnExit'in bekleyişinin kilitlenmediğini
/// <see cref="AppShutdownTests"/> pinler — burada ölçülen Supervisor ve MSBuild tarafıdır.</para>
///
/// <para><b>İddialar, bu sırayla:</b> (1) TEK graceful <c>stopRun</c> gider — drain boyunca da tek kalır — ve ne
/// gittiği anda ne de <see cref="RunViewModel.RequestExit"/> dönerken <c>ExitReady</c> vardır; (2) <c>ExitReady</c>
/// geldiğinde koşuda görülen her <c>MSBuild.exe</c> ÇOKTAN çıkmıştır (kendiliğinden bitti — henüz hiçbir şey
/// öldürülmedi) ve stop anında uçuşta olan her proje <c>projectSucceeded</c> raporlamıştır; (3) disposal kendi
/// tavanı (<see cref="AppShutdown.DisposalTimeout"/>) içinde biter ve döndükten sonra Supervisor ile job'da görülen
/// her üye <see cref="TestPaths.OrphanBudget"/> içinde çıkmıştır (0 orphan — <see cref="ProcessTree"/>). Drain'i
/// beklemeden kapanan ya da uçuştakileri öldüren bir çıkış (1)'i ya da (2)'yi kırar.</para>
///
/// <para><b>Harness:</b> VM thread-safe değildir, üretimde her çağrısı UI dispatcher'ında koşar. Burada da TEK
/// thread'i vardır: <see cref="DispatcherSynchronizationContext"/> kurulu bir STA thread'i (<see cref="StaThread"/>);
/// motor olayları MainWindow'daki gibi dispatcher kuyruğuyla oraya taşınır (<c>BeginInvoke</c> ile: VM'den kaçan bir
/// istisna testi o anda düşürür — gerekçe gövdede), bekleyişler <see cref="DispatcherPump.PumpUntil"/> ile olaya
/// bağlanır (sleep yok — D8). Process ağacı outer Job'un IOCP'si ile izlenir (<see cref="JobMembers"/>). Test
/// <c>[SkippableFact]</c>'tir (<c>[StaFact]</c> Skip'i tanımaz): STA gövdesinde atılan Skip, <see cref="StaThread"/>'in
/// TCS'inden tipi değişmeden geçer.</para>
/// </summary>
[Trait("Category", "MsBuild")]
public sealed class SafeExitProcessTests
{
    [SkippableFact]
    public async Task After_a_safe_exit_no_supervisor_or_msbuild_process_is_left()
    {
        // Bağımsız üç classlib: derleme, ardından ~3 sn'lik Exec ve ortak bin'e post-build copy (LegacyFixture'ın
        // varsayılan gecikmesi). Gecikme, stop anında gerçek MSBuild.exe'leri uçuşta tutar ve drain'i
        // gözlemlenebilir kılar. Sync bir git deposu ister — fixture commit'lenir.
        using var repo = new GitTestRepo();
        string sharedBin = Path.Combine(repo.RootPath, "SharedBin");
        string[] names = ["SE1", "SE2", "SE3"];
        foreach (string name in names)
            LegacyFixture.CreateClassLibWithSharedBinCopy(Path.Combine(repo.RootPath, name), name, sharedBin);
        repo.CommitAll("fixture");

        using var sandbox = new SupervisorSandbox(); // [§5.5] izole önbellek — motordan ÖNCE bildirilir, SONRA silinir
        var engine = sandbox.IsolatedEngineHost(TestPaths.WideStartupTimeout); // [B1/F1] gerekçe TestPaths'te
        using var members = new JobMembers(engine.OuterJob); // port motordan ÖNCE: Supervisor'ın doğumu da görülür
        try
        {
            var exit = await StaThread.RunAsync(() => RunAndExit(engine, members, repo.RootPath), "safe-exit");

            // (3) Uygulama öldü (OnExit'in bekleyişi döndü): disposal kendi tavanı içinde bitti ve Supervisor ile job'da
            // görülen her üye TestPaths.OrphanBudget içinde çıktı. Handle'lar üyeler doğarken açıldı; bekleyiş ve iddia
            // CascadeKillTests/KillMidBuildTests ile ortak (ProcessTree).
            Assert.True(exit.DisposalCompleted,
                $"the engine's disposal did not finish within AppShutdown.DisposalTimeout ({AppShutdown.DisposalTimeout})");
            Assert.Contains(exit.Observed, p => p.Id == exit.SupervisorPid); // vakum karşıtı: port doğumları taşıdı
            await ProcessTree.AssertNoOrphansAsync(exit.Observed, TestPaths.OrphanBudget, exit.SinceExit, "the app exited");
        }
        finally
        {
            await engine.DisposeAsync(); // güvenlik ağı: gövde erken düşse de ağaç süpürülür (idempotent)
        }
    }

    /// <summary>
    /// Uygulamanın payı, VM'in TEK thread'inde: açılış, Sync, Build, güvenli çıkış ve OnExit'in disposal bekleyişi.
    /// (1) ve (2) burada, oldukları ANDA ölçülür; dönen değer (3)'ün girdisidir.
    /// </summary>
    private static ExitOutcome RunAndExit(EngineHost engine, JobMembers members, string root)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        var vm = new RunViewModel(engine, MainWindowHost.NeverTickingBatcher(), () => "r1")
        {
            RootPath = root,
            LegacyWorktreePoolRoot = TestPaths.MissingLegacyPoolRoot, // [final review M8]
        };

        // Motorun olayları MainWindow'daki gibi VM'in thread'ine, dispatcher kuyruğuyla taşınır; sıra korunur. Yol
        // InvokeAsync DEĞİL BeginInvoke'tur [Task 5 fix round 1 · M3]: InvokeAsync, VM'den (ya da ExitReady
        // handler'ından) kaçan bir istisnayı kendi görevinde sessizce tutar ve test bir zaman aşımına düşerdi;
        // BeginInvoke onu pompadan kaçırır — PumpUntil gerçek sebeple, o anda kırılır. Proje sonuçları VM'e verilmeden
        // ÖNCE kaydedilir: ExitReady bir olayın ORTASINDA atılır ve o ana kadarki her sonucu görmelidir.
        var succeeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool synced = false;
        ErrorEvent? error = null;
        engine.EventReceived += ev => dispatcher.BeginInvoke(() =>
        {
            switch (ev)
            {
                case ProjectSucceededEvent e: succeeded.Add(e.ProjectId); break;
                case ProjectFailedEvent e: failed.Add(e.ProjectId); break;
                case SyncCompletedEvent: synced = true; break;
                case ErrorEvent e: error ??= e; break;
            }
            vm.OnEvent(ev);
        });
        engine.EngineExited += code => dispatcher.BeginInvoke(() => vm.OnEngineExited(code));

        // (1)'in ölçüsü: stopRun giderken ExitReady kaç kez atılmıştı. (2)'ninki: ExitReady ANINDAKİ durum.
        var sent = new List<IpcCommand>();
        int readyCount = 0, readyAtStop = -1;
        vm.DebugOnCommandSent = cmd =>
        {
            sent.Add(cmd);
            if (cmd is StopRunCommand) readyAtStop = readyCount;
        };
        ExitMoment? atReady = null;
        vm.ExitReady += (_, _) =>
        {
            readyCount++;
            atReady ??= new ExitMoment([.. members.MsBuilds.Where(p => !p.HasExited).Select(p => p.Id)],
                new HashSet<string>(succeeded, succeeded.Comparer), new HashSet<string>(failed, failed.Comparer));
        };

        // Açılış — MainWindow.StartEngineAsync'in yolu: motor hazır olunca açılışın Sync'i gider.
        var start = engine.StartAsync();
        DispatcherPump.PumpUntil(() => start.IsCompleted, TestPaths.WideStartupTimeout);
        Assert.True(start.IsCompletedSuccessfully,
            $"the engine did not become ready: {start.Exception?.GetBaseException().Message}");
        var ready = start.Result;
        vm.OnEngineReady(ready.EngineVersion, ready.Pid);
        DispatcherPump.PumpUntil(() => synced || error is not null, TestPaths.WideRunTimeout);
        Assert.True(synced && error is null, $"the startup sync did not complete: {error?.Code} {error?.Message}");

        // Build — kullanıcının yolu: düğme gerçekten açık (topoloji geldi, uçuşta iş yok). Bekleyiş, bir projenin
        // derlendiğini VM'in de gördüğü ve gerçek bir MSBuild.exe'nin canlı olduğu ana kadar.
        Assert.True(vm.BuildCommand.CanExecute(null), "Build is not enabled after the startup sync");
        vm.BuildCommand.Execute(null);
        DispatcherPump.PumpUntil(() => error is not null
            || (members.LiveMsBuildCount > 0 && vm.Projects.Any(r => r.State == ProjectRowState.Started)),
            TestPaths.WideRunTimeout);
        if (error is { Code: "msbuildNotFound" } missing) Skip.If(true, missing.Message); // KillMidBuildTests deseni
        Assert.True(error is null, $"the build failed: {error?.Code} {error?.Message}");
        var inFlight = vm.Projects.Where(r => r.State == ProjectRowState.Started).Select(r => r.Id).ToList();
        Assert.True(inFlight.Count > 0 && members.LiveMsBuildCount > 0, "no real MSBuild.exe was seen building");

        // Güvenli çıkış — Close to tray kapalıyken × ya da tepsi → Exit.
        vm.RequestExit();

        // (1) TEK graceful stopRun gitti; ExitReady ne o gittiği anda ne de RequestExit dönerken vardı — çıkış drain'i
        // bekliyor. Drain bitince yeniden sayılır: bekleyiş ikinci bir stopRun da üretmedi.
        var stop = Assert.Single(sent.OfType<StopRunCommand>());
        Assert.Equal(StopKind.Graceful, stop.Kind);
        Assert.True(readyAtStop == 0, "ExitReady fired before the stop was sent");
        Assert.True(readyCount == 0, "ExitReady fired before the drain — RequestExit did not wait for the work in flight");
        DispatcherPump.PumpUntil(() => atReady is not null, TestPaths.WideRunTimeout);
        Assert.True(atReady is not null, "ExitReady never fired — the drain did not end");
        Assert.True(sent.OfType<StopRunCommand>().Count() == 1, "a second stopRun was sent while the exit waited");

        // (2) ExitReady anında koşuda görülen her MSBuild.exe kendiliğinden çıkmıştı — henüz hiçbir şey öldürülmedi —
        // ve stop anında uçuştaki her proje başarıyla bitti: drain kesilmedi.
        Assert.True(atReady.LiveMsBuilds.Count == 0,
            $"MSBuild.exe still running when ExitReady fired (pid {string.Join(", ", atReady.LiveMsBuilds)}) "
            + "— the exit did not wait for the drain");
        var notSucceeded = inFlight.Where(id => !atReady.Succeeded.Contains(id)).ToList();
        Assert.True(notSucceeded.Count == 0 && atReady.Failed.Count == 0,
            $"in flight at the stop but not succeeded: [{string.Join(", ", notSucceeded.Select(Path.GetFileName))}], "
            + $"failed: [{string.Join(", ", atReady.Failed.Select(Path.GetFileName))}] — the drain was cut short");

        // (3)'ün girdisi — OnExit'in yaptığı disposal bekleyişi. Üye kümesi ondan ÖNCE kesinleşir: kapanışta doğan
        // bir üye beklenmez, olsa da KILL_ON_JOB_CLOSE onu da toplar.
        var observed = members.Freeze();
        bool disposed = AppShutdown.WaitForAsyncDisposal(engine, AppShutdown.DisposalTimeout);
        return new ExitOutcome(observed, ready.Pid, Stopwatch.StartNew(), disposed);
    }

    /// <summary>(2)'nin ölçüsü — <see cref="RunViewModel.ExitReady"/> atıldığı ANDA hâlâ canlı <c>MSBuild.exe</c>'ler
    /// ve o ana kadar raporlanan proje sonuçları.</summary>
    private sealed record ExitMoment(IReadOnlyList<int> LiveMsBuilds, IReadOnlySet<string> Succeeded,
        IReadOnlySet<string> Failed);

    /// <summary>(3)'ün girdisi: job'da görülen üyeler, Supervisor'ın PID'i, uygulamanın öldüğü andan (OnExit'in
    /// disposal bekleyişi döndü) beri geçen süre ve bekleyişin süresi içinde bitip bitmediği.</summary>
    private sealed record ExitOutcome(IReadOnlyList<Process> Observed, int SupervisorPid, Stopwatch SinceExit,
        bool DisposalCompleted);

    /// <summary>
    /// Outer Job'un üyelerini DOĞDUKLARI ANDA sabitleyen izleyici. IOCP'nin <c>NEW_PROCESS</c> bildirimi ayrı bir
    /// thread'de bloklu beklenir (sleep/poll yok — D8) ve her doğumda process'e bir handle açılır: handle açıkken PID
    /// başkasına geçemez, yani (2) ve (3)'ün sorduğu process hep o üyedir.
    ///
    /// <para><b>Üyelik doğrulanır:</b> handle açılana kadar çoktan çıkmış kısa ömürlü bir üyenin (git, vswhere) PID'i
    /// o arada başka bir process'e — ör. paralel koşan bir testin motoruna — geçmiş olabilir. O yabancı
    /// sabitlenseydi (3) onu orphan sayardı; <c>IsProcessInJob</c> yalnız bu job'un (iç içe job'ları dahil)
    /// üyelerini geçirir. <c>MSBuild.exe</c>'ler ayrıca işaretlenir
    /// (<see cref="KillMidBuildTests.IsMsBuildProcess"/> — isim süzgecinin tek yeri).</para>
    ///
    /// <para><b>[Task 5 fix round 1 · M3] İzleme sessizce ölemez:</b> döngü yalnız <see cref="Dispose"/> portu
    /// kapattığında kendiliğinden biter; başka her istisna (ör. <see cref="Pin"/>'den kaçan beklenmedik bir hata)
    /// task'ı düşürür ve <see cref="MsBuilds"/>/<see cref="Freeze"/> onu orijinal tipiyle yeniden fırlatır. Aksi hâlde
    /// (2) ve (3) eksik kalmış bir üye kümesini — bir alt kümeyi — sessizce denetlerdi.</para>
    /// </summary>
    private sealed class JobMembers : IDisposable
    {
        private readonly JobCompletionPort _port;
        private readonly nint _job;
        private readonly Task _watch;
        private readonly Lock _gate = new();
        private readonly List<Process> _members = [];
        private readonly List<Process> _msBuilds = [];
        private bool _frozen;
        private volatile bool _closing;

        /// <summary>Port <paramref name="job"/>'a HEMEN bağlanır: motor başlamadan kurulmalıdır ki doğum kaçmasın.</summary>
        public JobMembers(JobObject job)
        {
            _job = job.Handle;
            _port = job.AttachCompletionPort();
            _watch = Task.Factory.StartNew(Watch, CancellationToken.None, TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        /// <summary>Koşuda görülen her <c>MSBuild.exe</c>.</summary>
        public IReadOnlyList<Process> MsBuilds
        {
            get
            {
                ThrowIfWatchFailed();
                lock (_gate) return [.. _msBuilds];
            }
        }

        /// <summary>Şu an canlı <c>MSBuild.exe</c> sayısı.</summary>
        public int LiveMsBuildCount => MsBuilds.Count(p => !p.HasExited);

        /// <summary>İzlemeyi bitirir: bundan sonra doğan sabitlenmez. Dönen küme o ana kadar görülen her üyedir.</summary>
        public IReadOnlyList<Process> Freeze()
        {
            ThrowIfWatchFailed();
            return TakeFrozen();
        }

        private IReadOnlyList<Process> TakeFrozen()
        {
            lock (_gate)
            {
                _frozen = true;
                return [.. _members];
            }
        }

        /// <summary>İzleme düştüyse üye kümesi eksiktir: sebep burada, orijinal tipiyle fırlatılır.</summary>
        private void ThrowIfWatchFailed()
        {
            if (_watch.IsFaulted) _watch.GetAwaiter().GetResult();
        }

        private void Watch()
        {
            while (true)
            {
                JobNotification? n;
                try { n = _port.WaitNext(Timeout.InfiniteTimeSpan); }
                catch (Exception ex) when (_closing && ex is (Win32Exception or ObjectDisposedException))
                {
                    return; // port kapandı (Dispose): bloklu bekleyiş ERROR_ABANDONED_WAIT_0 ile döner — izleme biter
                }
                if (n is { MessageId: NativeMethods.JOB_OBJECT_MSG_NEW_PROCESS } born) Pin(born.Pid);
            }
        }

        private void Pin(int pid)
        {
            Process process;
            try
            {
                process = Process.GetProcessById(pid);
                _ = process.SafeHandle; // handle ŞİMDİ açılır ve önbelleklenir — PID artık başkasına geçemez
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
            {
                return; // çoktan çıkmış kısa ömürlü üye — geride kalamaz
            }
            bool member = IsProcessInJob(process.SafeHandle, _job, out bool inJob) && inJob;
            bool msBuild = member && KillMidBuildTests.IsMsBuildProcess(pid);
            lock (_gate)
            {
                if (member && !_frozen)
                {
                    _members.Add(process);
                    if (msBuild) _msBuilds.Add(process);
                    return;
                }
            }
            process.Dispose();
        }

        public void Dispose()
        {
            _closing = true;
            _port.Dispose(); // bloklu WaitNext döner, izleme thread'i biter
            foreach (var p in TakeFrozen()) p.Dispose(); // Dispose fırlatmaz: asıl hatayı (varsa) örtmesin
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsProcessInJob(SafeProcessHandle process, nint job, out bool result);
    }
}
