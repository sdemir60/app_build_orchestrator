using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using BuildOrchestrator.Core.ProcessControl;
using Xunit;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.ProcessControl;

/// <summary>
/// [T20-a] K11 motor yarısı: Job Object CPU rate cap (info-class 15, HARD_CAP) + job priority class.
/// Dört şeyi pinler: (1) cap yazılıp geri okunabilir — üstelik HAM alan (<c>CpuRate</c>=percent×100,
/// <c>ControlFlags</c>=ENABLE|HARD_CAP) doğrudan assert edilir, çünkü <c>SetCpuRate</c>/<c>QueryCpuRate</c>
/// birim çarpanına simetriktir ve round-trip tek başına çarpanı ÖLÇEMEZ; (2) priority yazımı
/// <c>ExtendedLimitInformation</c>'ı KILL_ON_JOB_CLOSE ile PAYLAŞTIĞI için LimitFlags Query→OR→Set ile
/// korunmalıdır — üç priority dalının HEPSİNDE yapısal olarak kontrol edilir; (3) cap+priority uygulanmış
/// bir job'da §3 kaskadı hâlâ ≤2sn'de çalışır; (4) cap TAVANI doyurulmuş bir çocuk kümesinde gerçekten
/// tutar (It-5 acceptance'ının "CPU cap tavanı tutar" maddesinin tek otomatik kanıtı).
/// [D8] Hiçbir testte sleep-poll yok: doğum randevusu IOCP, ölüm randevusu <c>WaitForExitAsync</c>,
/// ölçüm penceresi ise iptal token'lı bloklu bekleme + <c>Stopwatch</c> ile kapanır.
/// K11 sabitleri (cap yüzdeleri) literal yazılmaz — tek doğruluk kaynağı <see cref="PerfProfile"/>'dır.
/// </summary>
[Trait("Category", "ProcessControl")]
[Collection("CPU saturating (serial)")] // CPU doyuran perf testi taşır — bkz. CpuSaturatingSerialCollection
public class JobCpuRateTests(ITestOutputHelper output)
{
    private static int BalancedCap => PerfProfile.For(PerfMode.Balanced).CpuCapPercent!.Value; // 70
    private static int LightCap => PerfProfile.For(PerfMode.Light).CpuCapPercent!.Value;       // 40

    /// <summary>Job'un CPU rate control struct'ını HAM olarak okur — <see cref="JobObject.QueryCpuRate"/>'in
    /// yorumundan bağımsız doğrulama (birim çarpanı ve ControlFlags bitleri burada pinlenir).</summary>
    private static NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION RawCpuRate(JobObject job)
    {
        var info = new NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION();
        int size = Marshal.SizeOf<NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>();
        Assert.True(NativeMethods.QueryInformationJobObject(
            job.Handle, NativeMethods.JobObjectCpuRateControlInformation, ref info, size, out _));
        return info;
    }

    private static NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION RawExtendedLimit(JobObject job)
    {
        var info = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        int size = Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        Assert.True(NativeMethods.QueryInformationJobObject(
            job.Handle, NativeMethods.JobObjectExtendedLimitInformation, ref info, size, out _));
        return info;
    }

    // ---------------------------------------------------------------- Step 1: cap yaz / geri oku / temizle

    [Fact]
    public void Fresh_job_reports_no_cpu_cap_before_one_is_applied()
    {
        using var job = JobObject.CreateKillOnClose();
        Assert.Null(job.QueryCpuRate());
        Assert.Equal(0u, RawCpuRate(job).ControlFlags); // rate control hiç kurulmamış
    }

    [Fact]
    public void Cpu_cap_reads_back_at_the_percent_it_was_set_to_and_null_again_once_cleared()
    {
        using var job = JobObject.CreateKillOnClose();

        job.SetCpuRate(BalancedCap);
        Assert.Equal(BalancedCap, job.QueryCpuRate());

        job.SetCpuRate(LightCap);                       // üzerine yazma
        Assert.Equal(LightCap, job.QueryCpuRate());

        job.SetCpuRate(1);                              // alt sınır
        Assert.Equal(1, job.QueryCpuRate());

        job.SetCpuRate(100);                            // üst sınır
        Assert.Equal(100, job.QueryCpuRate());

        job.ClearCpuRate();                             // K11 Full
        Assert.Null(job.QueryCpuRate());
        Assert.Equal(0u, RawCpuRate(job).ControlFlags); // gerçekten kapandı — yalnız "null yorumu" değil
    }

    /// <summary>
    /// [P1 review KÖK 2] <c>SetCpuRate</c> percent'i 100 ile ÇARPAR, <c>QueryCpuRate</c> 100'e BÖLER — bu ikisi
    /// simetrik olduğu için round-trip testi birim çarpanını ölçemez (çarpan 1'e düşürülse bile round-trip
    /// yeşil kalır, yani cap 100× fazla kısıtlayıcı olurdu). Bu test HAM <c>CpuRate</c> alanını ve
    /// <c>ControlFlags</c> bitlerini doğrudan pinler.
    /// </summary>
    [Theory]
    [InlineData(1, 100u)]
    [InlineData(40, 4000u)]   // K11 Light
    [InlineData(70, 7000u)]   // K11 Balanced
    [InlineData(100, 10000u)]
    public void Cpu_cap_is_written_as_hundredths_of_a_percent_with_a_hard_cap(int percent, uint expectedRawRate)
    {
        using var job = JobObject.CreateKillOnClose();
        job.SetCpuRate(percent);

        var raw = RawCpuRate(job);
        Assert.Equal(expectedRawRate, raw.CpuRate); // birim: 1/100 yüzde — çarpan burada pinli
        Assert.Equal(
            NativeMethods.JOB_OBJECT_CPU_RATE_CONTROL_ENABLE | NativeMethods.JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP,
            raw.ControlFlags); // HARD_CAP şart: soft cap tavanı garanti etmez
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

    /// <summary>[P1 review KÖK 2] Rate control ENABLE ama HARD_CAP'siz (ya da weight-based) kurulduğunda
    /// <see cref="JobObject.QueryCpuRate"/> bunu "cap" diye raporlamamalı — doğrulama seam'i yalnız
    /// ENABLE|HARD_CAP kombinasyonunu cap sayar.</summary>
    [Fact]
    public void Query_cpu_rate_reports_null_when_the_job_has_rate_control_without_a_hard_cap()
    {
        using var job = JobObject.CreateKillOnClose();
        job.SetCpuRate(LightCap);
        Assert.Equal(LightCap, job.QueryCpuRate());

        // HARD_CAP olmadan, yalnız ENABLE ile yaz (soft/weight yolu) — JobObject bunu cap saymamalı.
        var soft = new NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
        {
            ControlFlags = NativeMethods.JOB_OBJECT_CPU_RATE_CONTROL_ENABLE,
            CpuRate = (uint)(LightCap * 100),
        };
        int size = Marshal.SizeOf<NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>();
        Assert.True(NativeMethods.SetInformationJobObject(
            job.Handle, NativeMethods.JobObjectCpuRateControlInformation, ref soft, size));

        Assert.Null(job.QueryCpuRate());
    }

    // ---------------------------------------------------------------- Step 2: KILL_ON_JOB_CLOSE hayatta kalır

    /// <summary>
    /// [P1 review KÖK 3] Enum→Win32 çevirisi tek yerdedir (<c>JobObject.SetPriorityClass</c>) ve
    /// <see cref="PerfProfileTests"/> yalnız ENUM değerini pinler — çevirinin kendisi yalnız burada test edilir.
    /// Bu yüzden ÜÇ dal da koşar: Normal↔BelowNormal takası gibi bir mutasyon aksi hâlde hiçbir testi kırmazdı
    /// (üretimde en çok kullanılacak Balanced/BelowNormal yolu tamamen testsiz kalırdı).
    /// </summary>
    [Theory]
    [InlineData(ProcessPriorityClassKind.Normal, NativeMethods.NORMAL_PRIORITY_CLASS)]
    [InlineData(ProcessPriorityClassKind.BelowNormal, NativeMethods.BELOW_NORMAL_PRIORITY_CLASS)]
    [InlineData(ProcessPriorityClassKind.Idle, NativeMethods.IDLE_PRIORITY_CLASS)]
    public void Priority_write_maps_to_the_win32_class_and_keeps_the_kill_on_job_close_limit_flag(
        ProcessPriorityClassKind kind, uint expectedWin32Class)
    {
        using var job = JobObject.CreateKillOnClose();

        job.SetCpuRate(LightCap);        // ayrı info-class (15) — ExtendedLimitInformation'ı paylaşmaz
        job.SetPriorityClass(kind);      // AYNI struct'ı paylaşır → Query→OR→Set şart

        var basic = RawExtendedLimit(job).BasicLimitInformation;
        uint flags = basic.LimitFlags;

        Assert.Equal(expectedWin32Class, basic.PriorityClass);
        Assert.True((flags & NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE) != 0,
            $"KILL_ON_JOB_CLOSE priority yazımında silindi (LimitFlags=0x{flags:X}) — §3 kaskat garantisi kaybolur");
        Assert.True((flags & NativeMethods.JOB_OBJECT_LIMIT_PRIORITY_CLASS) != 0,
            $"PRIORITY_CLASS flag'i set edilmemiş (LimitFlags=0x{flags:X})");
        Assert.Equal(LightCap, job.QueryCpuRate()); // priority yazımı cap'i de düşürmemeli
    }

    [Fact]
    public async Task Cascade_kill_still_lands_within_2s_after_a_cap_and_priority_write()
    {
        // [P1 review KÖK 4] pid DEĞİL, canlı iken alınmış Process nesneleri saklanır: açık handle Windows'ta
        // pid geri dönüşümünü engeller. Aksi hâlde aşağıdaki Kill(entireProcessTree) pid'i geri dönüştürülmüş
        // YABANCI bir process ağacını öldürebilirdi.
        var births = new List<Process>();
        try
        {
            using (var job = JobObject.CreateKillOnClose())
            using (var iocp = job.AttachCompletionPort())
            {
                job.SetCpuRate(LightCap);
                job.SetPriorityClass(ProcessPriorityClassKind.Idle);

                using var child = JobProcessLauncher.Launch(job, JobTestChildren.SleepChildCmdLine(), new LaunchOptions());
                // cmd + powershell torunu: en az 2 doğum bildirimi (nested üyelik otomatik miras)
                int notifications = 0;
                while (notifications < 2)
                {
                    var n = iocp.WaitNext(TimeSpan.FromSeconds(15)) ?? throw new TimeoutException("IOCP doğum bildirimi gelmedi");
                    if (n.MessageId != NativeMethods.JOB_OBJECT_MSG_NEW_PROCESS) continue;
                    notifications++;
                    try { births.Add(Process.GetProcessById(n.Pid)); }
                    catch (ArgumentException) { /* çoktan çıkmış — zaten ölü, doğrulanacak bir şey yok */ }
                }
            } // job.Dispose → KILL_ON_JOB_CLOSE kaskadı

            var sw = Stopwatch.StartNew();
            foreach (var p in births)
            {
                try { await p.WaitForExitAsync(new CancellationTokenSource(2000).Token); }
                catch (OperationCanceledException)
                {
                    Assert.Fail($"pid {p.Id} 2sn içinde ölmedi — cap/priority yazımı KILL_ON_JOB_CLOSE'u silmiş olabilir");
                }
            }
            Assert.True(sw.ElapsedMilliseconds <= 2000, $"kaskat {sw.ElapsedMilliseconds}ms");
        }
        finally
        {
            // Test KIRILDIYSA kaskat çalışmamış demektir ve child'lar (powershell: 300sn) hayatta kalır —
            // kırmızı bir koşu makinede artık süreç bırakmasın diye burada zorla temizlenir. Handle hâlâ
            // açık olduğu için hedef kesinlikle BİZİM child'ımızdır (pid geri dönüşümü imkânsız).
            foreach (var p in births)
            {
                try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* yarışta çıkmış — kabul */ }
                finally { p.Dispose(); }
            }
        }
    }

    // ---------------------------------------------------------------- Step 3: cap tavanı gerçekten tutar

    /// <summary>
    /// It-5 acceptance'ının "CPU cap tavanı tutar" maddesinin tek otomatik kanıtı. Sınıf serileştirilmiş bir
    /// collection'dadır (suite içi çekişme yok), ama makinedeki HARİCİ yüke karşı ayrıca bounded-retry taşır:
    /// capsiz ölçüm doyuma ulaşamazsa test kırmızı düşmez, <b>açıkça SKIP</b> edilir — sessizce yeşil veren
    /// bir yapı kurulmaz (bkz. sınıf doc'u ve rapor).
    /// </summary>
    [SkippableFact]
    [Trait("Category", "Perf")] // flake riski etiketlenir ama test normal suite'te KOŞAR (Category!=Acceptance)
    public async Task Cpu_hard_cap_holds_under_a_saturating_child()
    {
        int capPercent = LightCap;                                   // K11 Light — literal değil, tek kaynak
        int childCount = Environment.ProcessorCount + 2;             // oversubscribe → makineyi gerçekten doyur
        var window = TimeSpan.FromSeconds(2);                        // ≥1.5sn
        const double SaturationFloor = 0.60;                         // altında ölçüm ayırt edici değil
        const int MaxAttempts = 3;

        using var job = JobObject.CreateKillOnClose();
        using var iocp = job.AttachCompletionPort();

        var children = new List<JobChildProcess>();
        var procs = new List<Process>();
        try
        {
            for (int i = 0; i < childCount; i++)
                children.Add(JobProcessLauncher.Launch(job, JobTestChildren.BusyLoopChildCmdLine(), new LaunchOptions()));

            // Ölçümden önce hepsinin gerçekten doğduğunu IOCP randevusuyla bekle (D8: uyku/poll yok).
            int born = 0;
            while (born < childCount)
            {
                var n = iocp.WaitNext(TimeSpan.FromSeconds(20)) ?? throw new TimeoutException("IOCP doğum bildirimi gelmedi");
                if (n.MessageId == NativeMethods.JOB_OBJECT_MSG_NEW_PROCESS) born++;
            }
            procs.AddRange(children.Select(c => Process.GetProcessById(c.Pid)));

            // Bounded retry: harici bir yük anlık ise sonraki pencere doyuma ulaşır (normal makinede TEK
            // ölçüm yapılır — iyi durumda ek maliyet yok). Kalıcı harici yükte döngü tükenir ve SKIP edilir.
            double uncapped = 0;
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                uncapped = await MeasureCpuShareAsync(procs, window);
                if (uncapped > SaturationFloor) break;
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"capsiz ölçüm {attempt}/{MaxAttempts} doyuma ulaşmadı: {uncapped:F3}"));
            }
            Skip.If(uncapped <= SaturationFloor, string.Create(CultureInfo.InvariantCulture,
                $"makinede harici yük var (capsiz oran {uncapped:F3} ≤ {SaturationFloor:F2}) — " +
                $"cap ölçümü ayırt edici olamaz, atlandı"));

            job.SetCpuRate(capPercent);
            // Cap yazımından hemen sonraki pencere throttle'ın RAMP-IN'ini içerir (ölçülen: ~0.47 — kararlı
            // hâlin belirgin üzerinde). Bu pencere bilerek harcanır ve assert edilmez; yerine bir sonraki
            // KARARLI pencere ölçülür. (Uyku değil — o da tam bir ölçüm penceresi, yalnız raporlanır.)
            double rampIn = await MeasureCpuShareAsync(procs, window);
            double capped = await MeasureCpuShareAsync(procs, window);

            // Makineye duyarlı bir ölçüm — kırıldığında sayılar elde olsun diye HER ZAMAN raporlanır.
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"cpu share: capsiz={uncapped:F3} ramp={rampIn:F3} capli={capped:F3} " +
                $"(cap=%{capPercent}, child={childCount}, core={Environment.ProcessorCount})"));

            // (a) ASIL assert: cap belirgin bir oransal düşüş yaratır (makineye görece, mutlak değil).
            Assert.True(capped < uncapped * 0.80,
                $"cap oransal düşüş yaratmadı: capsiz {uncapped:F3} → capli {capped:F3}");
            // (b) İkincil assert: mutlak tavan, cömert toleransla (cap + 15 puan; ölçülen kararlı dağılım 0.38-0.46).
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
