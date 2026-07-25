using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;
using Xunit;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.ProcessControl;

/// <summary>
/// [T20-a] K11 motor yarısı: Job Object CPU rate cap (info-class 15, HARD_CAP) + job priority class.
/// Dört şeyi pinler: (1) cap yazılıp geri okunabilir ve 1..100 sınırı doğrulanır — bu, P/Invoke imzasının
/// (özellikle <c>cbJobObjectInfoLength</c>) doğruluğunu yakalayan tek yerdir; (2) priority yazımı
/// <c>ExtendedLimitInformation</c>'ı KILL_ON_JOB_CLOSE ile PAYLAŞTIĞI için LimitFlags Query→OR→Set ile
/// korunmalıdır — flag yapısal olarak kontrol edilir; (3) cap+priority uygulanmış bir job'da §3 kaskadı
/// hâlâ ≤2sn'de çalışır; (4) cap TAVANI doyurulmuş bir çocuk kümesinde gerçekten tutar (It-5
/// acceptance'ının "CPU cap tavanı tutar" maddesinin tek otomatik kanıtı).
/// [D8] Hiçbir testte sleep-poll yok: doğum randevusu IOCP, ölüm randevusu <c>WaitForExitAsync</c>,
/// ölçüm penceresi ise iptal token'lı bloklu bekleme + <c>Stopwatch</c> ile kapanır.
/// </summary>
[Trait("Category", "ProcessControl")]
public class JobCpuRateTests(ITestOutputHelper output)
{
    private static string CmdExe => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static string SleepChildCmdLine() => WindowsCommandLine.Build(
        CmdExe, "/c", "powershell -NoProfile -Command Start-Sleep -Seconds 300");

    /// <summary>Tek process'te tek çekirdeği doyuran cmd döngüsü (step 0 ⇒ sonsuz). Torun süreç doğurmaz,
    /// bu yüzden <see cref="Process.TotalProcessorTime"/> child'ın TÜM tüketimini kapsar.</summary>
    private static string BusyLoopChildCmdLine() => WindowsCommandLine.Build(
        CmdExe, "/c", "for /l %i in (1,0,2) do @rem");

    // ---------------------------------------------------------------- Step 1: cap yaz / geri oku / temizle

    [Fact]
    public void Fresh_job_reports_no_cpu_cap_before_one_is_applied()
    {
        using var job = JobObject.CreateKillOnClose();
        Assert.Null(job.QueryCpuRate());
    }

    [Fact]
    public void Cpu_cap_reads_back_at_the_percent_it_was_set_to_and_null_again_once_cleared()
    {
        using var job = JobObject.CreateKillOnClose();

        job.SetCpuRate(70);                     // K11 Balanced
        Assert.Equal(70, job.QueryCpuRate());

        job.SetCpuRate(40);                     // K11 Light — üzerine yazma
        Assert.Equal(40, job.QueryCpuRate());

        job.SetCpuRate(1);                      // alt sınır
        Assert.Equal(1, job.QueryCpuRate());

        job.SetCpuRate(100);                    // üst sınır
        Assert.Equal(100, job.QueryCpuRate());

        job.ClearCpuRate();                     // K11 Full
        Assert.Null(job.QueryCpuRate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Cpu_cap_outside_one_to_hundred_percent_is_rejected(int percent)
    {
        using var job = JobObject.CreateKillOnClose();
        Assert.Throws<ArgumentOutOfRangeException>(() => job.SetCpuRate(percent));
    }

    // ---------------------------------------------------------------- Step 2: KILL_ON_JOB_CLOSE hayatta kalır

    [Fact]
    public void Priority_write_keeps_the_kill_on_job_close_limit_flag()
    {
        using var job = JobObject.CreateKillOnClose();

        job.SetCpuRate(40);                                       // ayrı info-class (15) — struct'ı paylaşmaz
        job.SetPriorityClass(ProcessPriorityClassKind.Idle);      // AYNI struct'ı paylaşır → Query→OR→Set şart

        var info = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        int size = Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        Assert.True(NativeMethods.QueryInformationJobObject(
            job.Handle, NativeMethods.JobObjectExtendedLimitInformation, ref info, size, out _));

        uint flags = info.BasicLimitInformation.LimitFlags;
        Assert.True((flags & NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE) != 0,
            $"KILL_ON_JOB_CLOSE priority yazımında silindi (LimitFlags=0x{flags:X}) — §3 kaskat garantisi kaybolur");
        Assert.True((flags & NativeMethods.JOB_OBJECT_LIMIT_PRIORITY_CLASS) != 0,
            $"PRIORITY_CLASS flag'i set edilmemiş (LimitFlags=0x{flags:X})");
        Assert.Equal(NativeMethods.IDLE_PRIORITY_CLASS, info.BasicLimitInformation.PriorityClass);
        Assert.Equal(40, job.QueryCpuRate()); // priority yazımı cap'i de düşürmemeli
    }

    [Fact]
    public async Task Cascade_kill_still_lands_within_2s_after_a_cap_and_priority_write()
    {
        var births = new List<int>();
        using (var job = JobObject.CreateKillOnClose())
        using (var iocp = job.AttachCompletionPort())
        {
            job.SetCpuRate(40);
            job.SetPriorityClass(ProcessPriorityClassKind.Idle);

            using var child = JobProcessLauncher.Launch(job, SleepChildCmdLine(), new LaunchOptions());
            // cmd + powershell torunu: en az 2 doğum bildirimi (nested üyelik otomatik miras)
            while (births.Count < 2)
            {
                var n = iocp.WaitNext(TimeSpan.FromSeconds(15)) ?? throw new TimeoutException("IOCP doğum bildirimi gelmedi");
                if (n.MessageId == NativeMethods.JOB_OBJECT_MSG_NEW_PROCESS) births.Add(n.Pid);
            }
        } // job.Dispose → KILL_ON_JOB_CLOSE kaskadı

        var sw = Stopwatch.StartNew();
        try
        {
            foreach (var pid in births)
            {
                try { await Process.GetProcessById(pid).WaitForExitAsync(new CancellationTokenSource(2000).Token); }
                catch (ArgumentException) { /* zaten öldü — kabul */ }
                catch (OperationCanceledException)
                {
                    Assert.Fail($"pid {pid} 2sn içinde ölmedi — cap/priority yazımı KILL_ON_JOB_CLOSE'u silmiş olabilir");
                }
            }
            Assert.True(sw.ElapsedMilliseconds <= 2000, $"kaskat {sw.ElapsedMilliseconds}ms");
        }
        finally
        {
            // Test KIRILDIYSA kaskat çalışmamış demektir ve child'lar (powershell: 300sn) hayatta kalır —
            // kırmızı bir koşu makinede artık süreç bırakmasın diye burada zorla temizlenir.
            foreach (var pid in births)
            {
                try { Process.GetProcessById(pid).Kill(entireProcessTree: true); }
                catch (ArgumentException) { /* zaten yok — beklenen mutlu yol */ }
                catch (InvalidOperationException) { /* yarışta çıkmış — kabul */ }
            }
        }
    }

    // ---------------------------------------------------------------- Step 3: cap tavanı gerçekten tutar

    [Fact]
    [Trait("Category", "Perf")] // flake riski etiketlenir ama test normal suite'te KOŞAR (Category!=Acceptance)
    public async Task Cpu_hard_cap_holds_under_a_saturating_child()
    {
        const int capPercent = 40;                                   // K11 Light
        int childCount = Environment.ProcessorCount + 2;             // oversubscribe → makineyi gerçekten doyur
        var window = TimeSpan.FromSeconds(2);                        // ≥1.5sn

        using var job = JobObject.CreateKillOnClose();
        using var iocp = job.AttachCompletionPort();

        var children = new List<JobChildProcess>();
        var procs = new List<Process>();
        try
        {
            for (int i = 0; i < childCount; i++)
                children.Add(JobProcessLauncher.Launch(job, BusyLoopChildCmdLine(), new LaunchOptions()));

            // Ölçümden önce hepsinin gerçekten doğduğunu IOCP randevusuyla bekle (D8: uyku/poll yok).
            int born = 0;
            while (born < childCount)
            {
                var n = iocp.WaitNext(TimeSpan.FromSeconds(20)) ?? throw new TimeoutException("IOCP doğum bildirimi gelmedi");
                if (n.MessageId == NativeMethods.JOB_OBJECT_MSG_NEW_PROCESS) born++;
            }
            procs.AddRange(children.Select(c => Process.GetProcessById(c.Pid)));

            double uncapped = await MeasureCpuShareAsync(procs, window);

            job.SetCpuRate(capPercent);
            // Cap yazımından hemen sonraki pencere throttle'ın RAMP-IN'ini içerir (ölçülen: ~0.49 — kararlı
            // hâlin belirgin üzerinde). Bu pencere bilerek harcanır ve assert edilmez; yerine bir sonraki
            // KARARLI pencere ölçülür. (Uyku değil — o da tam bir ölçüm penceresi, yalnız raporlanır.)
            double rampIn = await MeasureCpuShareAsync(procs, window);
            double capped = await MeasureCpuShareAsync(procs, window);

            // Makineye duyarlı bir ölçüm — kırıldığında sayılar elde olsun diye HER ZAMAN raporlanır.
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"cpu share: capsiz={uncapped:F3} ramp={rampIn:F3} capli={capped:F3} " +
                $"(cap=%{capPercent}, child={childCount}, core={Environment.ProcessorCount})"));

            // (a) Ölçüm ayırt edici mi? Capsiz koşu doyuma yaklaşmadıysa cap'in etkisi de ölçülemez.
            Assert.True(uncapped > 0.60,
                $"capsiz oran {uncapped:F3} — makine zaten dolu, ölçüm ayırt edici değil");
            // (b) ASIL assert: cap belirgin bir oransal düşüş yaratır (makineye görece, mutlak değil).
            Assert.True(capped < uncapped * 0.80,
                $"cap oransal düşüş yaratmadı: capsiz {uncapped:F3} → capli {capped:F3}");
            // (c) İkincil assert: mutlak tavan, cömert toleransla (cap + 15 puan; ölçülen kararlı dağılım 0.38-0.46).
            Assert.True(capped <= (capPercent + 15) / 100.0,
                $"capli oran {capped:F3} mutlak tavanı (%{capPercent} + 15 puan) aştı");
        }
        finally
        {
            foreach (var p in procs) p.Dispose();
            foreach (var c in children) c.Dispose();
        }
        // job.Dispose (using) → KILL_ON_JOB_CLOSE tüm busy-loop'ları deterministik olarak sonlandırır.
    }

    /// <summary>Pencere boyunca child kümesinin toplam CPU zamanının, makinenin sunabildiği toplam CPU
    /// zamanına oranı: <c>Δcpu / (Δwall × logicalCores)</c>. Pencere sabit uykuyla değil, asla çıkmayacak
    /// bir child'ın <see cref="Process.WaitForExitAsync"/> beklemesinin iptaliyle kapanır (kernel objesi
    /// üzerinde bloklu bekleme — poll yok); gerçek süre <see cref="Stopwatch"/>'tan okunur.</summary>
    private static async Task<double> MeasureCpuShareAsync(IReadOnlyList<Process> procs, TimeSpan window)
    {
        TimeSpan before = TotalCpu(procs);
        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(window);

        bool windowClosedByChildExit = true;
        try { await procs[0].WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { windowClosedByChildExit = false; } // beklenen yol
        sw.Stop();

        // Busy-loop child'ın erken çıkması (ör. cmd döngü sözdiziminin bir Windows sürümünde sonsuz olmaması)
        // pencereyi kısaltır ve oranı ANLAMSIZ kılardı — sessiz yeşil/kırmızı yerine açık bir hata verilir.
        Assert.False(windowClosedByChildExit,
            $"busy-loop child ölçüm penceresi ({window.TotalMilliseconds}ms) dolmadan çıktı — ölçüm geçersiz");

        TimeSpan after = TotalCpu(procs);

        return (after - before).TotalMilliseconds / (sw.Elapsed.TotalMilliseconds * Environment.ProcessorCount);
    }

    private static TimeSpan TotalCpu(IReadOnlyList<Process> procs)
    {
        var total = TimeSpan.Zero;
        foreach (var p in procs)
        {
            p.Refresh();
            total += p.TotalProcessorTime;
        }
        return total;
    }
}
