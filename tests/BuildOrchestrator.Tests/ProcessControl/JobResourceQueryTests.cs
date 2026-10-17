using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using BuildOrchestrator.Core.Diagnostics;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Tests.App;

namespace BuildOrchestrator.Tests.ProcessControl;

/// <summary>
/// [kaynak şeridi · design v1.25.0] Title bar'ın kaynak şeridinin Core tarafındaki Win32 okumaları: App'in outer job'unun muhasebesi
/// (<see cref="JobObject.QueryAccounting"/>), job'daki süreçlerin listesi (<see cref="JobObject.QueryProcessIds"/>) ve bir sürecin
/// özel baytları (<see cref="MemoryLine.PrivateBytesOf"/>, <see cref="MemoryLine.CurrentPrivateBytes"/>). Şerit bunları 3 sn'de bir okur ve
/// ısınmadan sonra örnek başına SIFIR bayt ayırır, makinedeki süreçleri taramaz — sınıf bu sözleri pinler: değerler doğru (motorun iç job'unda
/// yaşayan süreçler için de), hata hâlleri bilinen, ayırma sıfır.
///
/// <para>[D8] Hiçbir testte sleep-poll yok. Canlı üye, stdin'i kapanana dek bekleyen bir çocuktur (<see cref="JobTestChildren.GatedBurnChildCmdLine"/>:
/// boru üzerinde bloklu okuma); çıkış randevusu job'un IOCP'sindeki <c>ACTIVE_PROCESS_ZERO</c> bildirimidir.</para>
/// </summary>
[Trait("Category", "ProcessControl")]
public class JobResourceQueryTests
{
    private const long Mb = 1024 * 1024;

    /// <summary>Eski sayacı BU süreçte okurken kabul edilen fark: 1 MiB olağan kayma + eski sayacın kendi anlık görüntü tamponu için 4 MiB
    /// (görüntünün 1,5 katı; bu makinede 1 MiB + 8 KiB ölçüldü, 4 MiB ≈ 2,7 MiB'lık bir süreç listesi). Gerekçe:
    /// <see cref="The_private_bytes_reader_reads_what_the_old_counter_read"/>.</summary>
    private const long OldCounterAllowance = 5 * Mb;

    /// <summary>Win32 <c>ERROR_INVALID_HANDLE</c>.</summary>
    private const int ErrorInvalidHandle = 6;

    /// <summary>Job'un son üyesi çıkana dek bekler (<c>JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO</c>): çıkışın kernel'deki karşılığı, uyku/poll
    /// değil. Diğer bildirimler (doğumlar, tek tek çıkışlar) atlanır.</summary>
    private static void WaitUntilJobIsEmpty(JobCompletionPort port, TimeSpan timeout)
    {
        while (true)
        {
            var notification = port.WaitNext(timeout) ?? throw new TimeoutException("the job never became empty");
            if (notification.MessageId == NativeMethods.JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO) return;
        }
    }

    // ---------------------------------------------------------------- job muhasebesi ve PID listesi

    [Fact]
    public void An_empty_job_reports_no_processes_and_zero_cpu()
    {
        using var job = JobObject.CreateKillOnClose();
        var destination = new int[8];
        Array.Fill(destination, -1);

        var accounting = job.QueryAccounting();
        int written = job.QueryProcessIds(destination);

        Assert.Equal(new JobAccounting(TimeSpan.Zero, 0), accounting);
        Assert.Equal(0, written);
        Assert.All(destination, pid => Assert.Equal(-1, pid)); // hiçbir yuvaya yazılmadı
    }

    /// <summary>
    /// Bir üye canlıyken PID'i listede görünür ve özel baytları (başkasının süreci, <c>OpenProcess(QUERY_LIMITED)</c> ile) okunur; üye
    /// ÇIKINCA listeden düşer ama işlemci zamanı job'da KALIR (<c>TotalUserTime + TotalKernelTime</c> çıkmış süreçleri de içerir) — şeridin
    /// CPU yüzdesi iki örnek arasındaki fark olduğu için bu süre bir derleyici çıkınca GERİ ALINIRSA yüzde negatife sapardı.
    /// </summary>
    [Fact]
    public void A_job_reports_its_member_and_keeps_its_cpu_time_after_it_exits()
    {
        using var job = JobObject.CreateKillOnClose();
        using var port = job.AttachCompletionPort();
        using var child = JobProcessLauncher.Launch(
            job, JobTestChildren.GatedBurnChildCmdLine(), new LaunchOptions(RedirectStdio: true));

        // Çocuk stdin'in kapanmasını bekliyor: kesinlikle canlı ve job'un üyesi (assign, çocuk SUSPENDED iken Launch'ta yapıldı).
        var pids = new int[32];
        int written = job.QueryProcessIds(pids);
        Assert.Contains(child.Pid, pids[..written]);
        var before = job.QueryAccounting();
        Assert.True(before.ActiveProcesses >= 1, $"the waiting child is an active member: {before}");
        long? childBytes = MemoryLine.PrivateBytesOf(child.Pid);
        Assert.True(childBytes > 0, $"a live member of the job reads its private bytes: {childBytes}");
        // Çıkmış bir sürecin Process nesnesi ancak çıkıştan ÖNCE alınmışsa okunur (GetProcessById çıkmış PID'e ArgumentException verir,
        // tutamaç açık olsa bile): tutamaç şimdi alınır, bundan sonra nesne bu süreçtir (JobBirths.TryOpen kalıbı).
        using var member = Process.GetProcessById(child.Pid);
        _ = member.SafeHandle;

        child.StandardInput!.Dispose(); // EOF: bekleyiş biter, çocuk sınırlı yükünü yakar ve çıkar
        WaitUntilJobIsEmpty(port, TimeSpan.FromSeconds(60));

        var after = job.QueryAccounting();
        Assert.Equal(0, after.ActiveProcesses);
        Assert.Equal(0, job.QueryProcessIds(pids)); // çıkan üye listeden düştü
        Assert.True(after.TotalCpuTime > TimeSpan.Zero, $"the job keeps the cpu time of members that exited: {after}");
        Assert.True(after.TotalCpuTime >= before.TotalCpuTime, $"cpu time never goes back: {before} -> {after}");
        // Çıkmış çocuğun kendi işlemci zamanı da job toplamının içindedir (çocuk sınırlı yükünü yaktı: sıfırdan büyüktür).
        TimeSpan own = member.TotalProcessorTime;
        Assert.True(own > TimeSpan.Zero, $"the gated child burns cpu once released: {own}");
        Assert.True(after.TotalCpuTime >= own, $"job total {after.TotalCpuTime} must include the exited member's own {own}");
    }

    /// <summary>
    /// Şeridin dayandığı iç içe job varsayımı: App'in outer job'unun içindeki motor, derleyicileri kendi INNER job'una koyar
    /// (<c>JobProcessLauncher.Launch(innerJob, ...)</c>); bir süreç iç job'a girince dış job'dan ÇIKMAZ — dış job'un PID listesi ve
    /// muhasebesi iç job'un üyesini ve harcadığı işlemci zamanını da görmeye devam eder. Burada motor rolünü dış job'un üyesi olan
    /// çocuk oynar ve çalışırken iç bir job'a atanır (Supervisor'ın MSBuild'e yaptığının aynısı: dış job'daki bir sürecin yeni bir job'a girmesi).
    /// </summary>
    [Fact]
    public void A_member_that_joins_a_nested_job_stays_in_the_outer_jobs_list_and_accounting()
    {
        using var outer = JobObject.CreateKillOnClose();
        using var port = outer.AttachCompletionPort();
        using var child = JobProcessLauncher.Launch(
            outer, JobTestChildren.GatedBurnChildCmdLine(), new LaunchOptions(RedirectStdio: true));
        using var inner = JobObject.CreateKillOnClose();
        using var member = Process.GetProcessById(child.Pid);
        inner.Assign(member.Handle); // dış job'un üyesi, iç içe ikinci bir job'a girer

        var pids = new int[16];
        Assert.Contains(child.Pid, pids[..inner.QueryProcessIds(pids)]);
        Assert.Contains(child.Pid, pids[..outer.QueryProcessIds(pids)]); // iç job'a girmek dış job'dan çıkarmadı
        Assert.True(inner.QueryAccounting().ActiveProcesses >= 1);
        Assert.True(outer.QueryAccounting().ActiveProcesses >= inner.QueryAccounting().ActiveProcesses);

        child.StandardInput!.Dispose();
        WaitUntilJobIsEmpty(port, TimeSpan.FromSeconds(60));

        TimeSpan own = member.TotalProcessorTime;
        Assert.True(own > TimeSpan.Zero, $"the gated child burns cpu once released: {own}");
        Assert.True(outer.QueryAccounting().TotalCpuTime >= own, $"outer {outer.QueryAccounting().TotalCpuTime} must include {own}");
        Assert.True(inner.QueryAccounting().TotalCpuTime > TimeSpan.Zero, "the nested job accounts the member too");
    }

    /// <summary>
    /// <c>ERROR_MORE_DATA</c> bir hata DEĞİLDİR: hedef üye sayısından küçükse sığan kadar yazılır ve yazılan sayı döner; boş hedef de
    /// hata vermez. İç bayt tamponu hedefe göre kurulur ve YENİDEN KULLANILIR: önce geniş sonra dar bir hedef (ve tersi) aynı nesnede
    /// doğru okur — tampon kendi boyuna değil, o çağrının hedefine göre okunur.
    /// </summary>
    [Fact]
    public void A_destination_smaller_than_the_member_count_gets_what_fits()
    {
        using var job = JobObject.CreateKillOnClose();
        using var port = job.AttachCompletionPort();
        using var child = JobProcessLauncher.Launch(job, JobTestChildren.SleepChildCmdLine(), new LaunchOptions());
        int born = 0;
        // cmd + (powershell ya da conhost): 300 sn uyuyan ağaçta en az iki KALICI üye (hiçbiri bu test bitmeden çıkmaz).
        JobBirths.Until(port, TimeSpan.FromSeconds(20), "the second job member was never born", _ => ++born >= 2);

        var narrow = new int[1];
        Assert.Equal(1, job.QueryProcessIds(narrow)); // ilk çağrı: tampon dar hedefe göre kurulur
        Assert.True(narrow[0] > 0, $"a listed process id is positive: {narrow[0]}");

        var roomy = new int[64];
        Array.Fill(roomy, -1);
        int all = job.QueryProcessIds(roomy); // tampon büyür
        Assert.InRange(all, 2, roomy.Length - 1);
        Assert.Contains(child.Pid, roomy[..all]);
        Assert.Contains(narrow[0], roomy[..all]);
        Assert.Equal(all, roomy[..all].Distinct().Count());
        Assert.Equal(-1, roomy[all]); // yazılan yuvaların ötesine dokunulmaz

        Array.Clear(narrow);
        Assert.Equal(1, job.QueryProcessIds(narrow)); // büyümüş tampon dar hedefle de doğru okunur
        Assert.Contains(narrow[0], roomy[..all]);
        Assert.Equal(0, job.QueryProcessIds([])); // boş hedef: hata değil, hiçbir şey yazılmaz
    }

    /// <summary>
    /// İç tampon kilit altında yeniden kullanılır: farklı boyda hedeflerle EŞZAMANLI okuyan iş parçacıkları birbirinin tamponunu
    /// bozmaz (kilitsiz bir tampon büyürken başka bir okuyucunun altından çekilir ya da iki çağrının başlığı birbirine karışır; dar
    /// hedef sıfır, geniş hedef bir üye görürdü). Sürekli uyuyan ağaçta üye sayısı hiç azalmaz, yani her okuma ≥ 2 üye görmelidir.
    /// </summary>
    [Fact]
    public async Task Concurrent_pid_queries_with_different_destination_sizes_do_not_disturb_each_other()
    {
        using var job = JobObject.CreateKillOnClose();
        using var port = job.AttachCompletionPort();
        using var child = JobProcessLauncher.Launch(job, JobTestChildren.SleepChildCmdLine(), new LaunchOptions());
        int born = 0;
        JobBirths.Until(port, TimeSpan.FromSeconds(20), "the second job member was never born", _ => ++born >= 2);

        const int iterations = 4000;
        var failures = new ConcurrentQueue<string>();
        var workers = Enumerable.Range(0, 4).Select(worker => Task.Factory.StartNew(() =>
        {
            bool narrow = worker % 2 == 0;
            var destination = new int[narrow ? 1 : 16];
            for (int i = 0; i < iterations; i++)
            {
                int count = job.QueryProcessIds(destination);
                if (narrow ? count != 1 : count < 2)
                    failures.Enqueue($"worker {worker} ({destination.Length} slots) iteration {i}: {count} process ids");
                else if (destination[0] <= 0)
                    failures.Enqueue($"worker {worker} iteration {i}: process id {destination[0]}");
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        await Task.WhenAll(workers);

        Assert.True(failures.IsEmpty, $"{failures.Count} bad reads, first: {string.Join(" | ", failures.Take(3))}");
    }

    /// <summary>Tutamacı job'un altından kapanmış bir sorgu Win32 hatasıyla düşer (<c>QueryCpuRate</c> kalıbı): sessiz bir sıfır
    /// değil <see cref="Win32Exception"/>. Şeridin örnekleyicisi bu hatayı yakalar ve job alanlarını okunamadı sayar. <c>Dispose</c>
    /// BİLEREK çağrılmaz — kapanmış tutamacı bir daha kapatmak, bu arada başka bir nesneye yeniden verilmiş bir değeri kapatabilir
    /// (<c>JobObject</c>'in sonlandırıcısı yok, sızıntı zararsızdır).</summary>
    [Fact]
    public void Job_queries_throw_the_win32_error_when_the_job_handle_is_invalid()
    {
        var job = JobObject.CreateKillOnClose();
        Assert.True(NativeMethods.CloseHandle(job.Handle));

        Assert.Equal(ErrorInvalidHandle, Assert.Throws<Win32Exception>(() => job.QueryAccounting()).NativeErrorCode);
        Assert.Equal(ErrorInvalidHandle, Assert.Throws<Win32Exception>(() => job.QueryProcessIds(new int[4])).NativeErrorCode);
    }

    [Fact]
    public void Job_queries_refuse_a_disposed_job()
    {
        var job = JobObject.CreateKillOnClose();
        job.Dispose();

        Assert.Throws<ObjectDisposedException>(() => job.QueryAccounting());
        Assert.Throws<ObjectDisposedException>(() => job.QueryProcessIds(new int[4]));
    }

    // ---------------------------------------------------------------- süreç özel baytları

    /// <summary>
    /// Yeni okuma (<c>GetProcessMemoryInfo.PrivateUsage</c>) eski sayaçla (<c>Process.PrivateMemorySize64</c>) AYNI ölçüdür: art arda üç
    /// okumada (yeni, eski, yeni) eski değer iki yeninin arasındadır ya da <see cref="OldCounterAllowance"/> kadar yakındadır. Eski sayaç
    /// test kodunda serbesttir (<see cref="MemoryReadGuardTests"/> yalnız üretim ağacını tarar); burada yalnız karşılaştırma için
    /// okunur — her çağrıda makinedeki tüm süreçlerin anlık görüntüsünü aldığı için ÜRETİMDE kullanılmaz.
    ///
    /// <para><b>ÖLÇÜLDÜ — eski sayaç KENDİ anlık görüntü tamponunu da sayar:</b> görüntü bu sürecin içinde, sayaçtan hemen önce ayrılan
    /// (yerel) bir tampona yazılır ve çekirdek sayıyı tampon canlıyken verir. Tampon makinedeki süreç/iş parçacığı sayısıyla büyür: bu
    /// makinede 704 512 baytlık görüntü için 1 056 768 bayt (görüntünün 1,5 katı), yani eski sayaç iki yeni okumadan TAM 1 MiB + 8 KiB
    /// büyük okudu; yeni okumalar birbirine eşitti. Brief'in 1 MiB toleransı bu yüzden bu makinede 8 KiB farkla düşüyordu. Tolerans bu
    /// artefaktı kapsayacak kadar geniştir; GERÇEK ölçü eşitliği (tolerans 256 KiB) aşağıdaki testtedir, orada tampon bizim sürecimizde
    /// değil başkasında olduğu için fazlalık yoktur. Yeni okuma bu fazlalığı taşımaz: tamponu o ayırmaz.</para>
    /// </summary>
    [Fact]
    public void The_private_bytes_reader_reads_what_the_old_counter_read() =>
        AssertSameMeasure("the new reader and the old counter", OldCounterAllowance, () =>
        {
            long? first = MemoryLine.CurrentPrivateBytes();
            long old;
            using (var process = Process.GetCurrentProcess()) old = process.PrivateMemorySize64;
            return (first, old, MemoryLine.CurrentPrivateBytes());
        });

    /// <summary>
    /// Aynı eşitlik BAŞKASININ süreci için de geçer — şeridin gerçek yolu budur (motor ve derleyiciler PID'leriyle açılır). Eski sayacın
    /// anlık görüntü tamponu bu süreçte değil BİZİM süreçte durur, yani fazlalık yoktur ve tolerans dardır (256 KiB). Hedef,
    /// <c>more</c>'dur: stdin'de bloklu, hiçbir şey ayırmaz; yeni okuma eski sayacı (üç okuma, art arda) sarar. Doğumunu IOCP
    /// bildirir — bekleme çekirdek nesnesi üzerindedir (sleep/poll yok).
    /// </summary>
    [Fact]
    public void A_foreign_process_reads_what_the_old_counter_read_too()
    {
        using var job = JobObject.CreateKillOnClose();
        using var port = job.AttachCompletionPort();
        using var child = JobProcessLauncher.Launch(
            job, JobTestChildren.GatedBurnChildCmdLine(), new LaunchOptions(RedirectStdio: true));
        // .NET yalnız ".exe" uzantısını atar: more.com'un ProcessName'i "more.com"dur.
        using var more = JobBirths.WaitForProcess(port, TimeSpan.FromSeconds(30), "the waiting process was never born",
            process => process.ProcessName.StartsWith("more", StringComparison.OrdinalIgnoreCase));

        AssertSameMeasure("a foreign process", 256 * 1024, () =>
        {
            long? first = MemoryLine.PrivateBytesOf(more.Id);
            more.Refresh(); // ProcessName anlık görüntüyü zaten yüklemişti: eski sayaç taze bir görüntü okusun
            long old = more.PrivateMemorySize64;
            return (first, old, MemoryLine.PrivateBytesOf(more.Id));
        });
    }

    /// <summary>Başkasının süreci gibi okunan bu süreç (<c>OpenProcess</c> + <c>CloseHandle</c>) ile sözde tutamaçla okunan AYNI sayıdır
    /// (1 MiB'a dek tolerans: iki okuma arasında başka testler bellek ayırır).</summary>
    [Fact]
    public void A_process_read_through_its_id_equals_the_same_process_read_through_the_pseudo_handle() =>
        AssertSameMeasure("this process by id and by pseudo handle", Mb, () =>
            (MemoryLine.CurrentPrivateBytes(), MemoryLine.PrivateBytesOf(Environment.ProcessId), MemoryLine.CurrentPrivateBytes()));

    /// <summary>
    /// İki okumanın AYNI ölçü olduğunu üçlüyle (önce yeni, ölçülen, sonra yeni) sınar: ölçülen değer iki yeni okumanın arasındadır ya da
    /// ilkine <paramref name="tolerance"/> kadar yakındadır. Süreç belleği başka iş parçacıklarının ayırmasıyla kayar; üçlü bu kaymayı
    /// kapsar (kayma tek yönlüyse ölçülen hep aralıktadır). Yine de bir okuma iki yeni okuma arasında yükselip düşen bir sıçramaya
    /// denk gelebilir: üçlü en çok üç kez denenir — sistematik bir ölçü farkı ÜÇÜNDE de görünür.
    /// </summary>
    private static void AssertSameMeasure(string what, long tolerance, Func<(long? Before, long? Measured, long? After)> readTriple)
    {
        var failures = new List<string>();
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var (before, measured, after) = readTriple();
            Assert.NotNull(before);
            Assert.NotNull(measured);
            Assert.NotNull(after);
            Assert.True(measured.Value > 0, $"{what}: a live process holds private bytes: {measured}");

            bool between = measured.Value >= Math.Min(before.Value, after.Value) && measured.Value <= Math.Max(before.Value, after.Value);
            if (between || Math.Abs(measured.Value - before.Value) <= tolerance) return;
            failures.Add($"new {before} / measured {measured} / new {after}");
        }

        Assert.Fail($"{what} must read the same measure (tolerance {tolerance}); every attempt differed: {string.Join(" | ", failures)}");
    }

    /// <summary>
    /// Çıkmış bir süreç <c>null</c> okunur. ÖLÇÜLDÜ: çıkmış bir sürecin tutamacı başka birinde (çağıran, <c>csrss</c>, bir izleyici) açık
    /// kaldıkça <c>OpenProcess</c> BAŞARILIR ve <c>GetProcessMemoryInfo</c> başarıyla ESKİ bir sayı verir (~76 KB) — okuma "açılamadı"
    /// diye düşmez, bu yüzden çıkış ayrıca sorulur. Tutamaç burada bilerek AÇIK tutulur: PID bir başkasına verilemez, test kararlıdır.
    /// </summary>
    [Fact]
    public void A_process_that_has_exited_reads_as_null()
    {
        using var process = Process.Start(new ProcessStartInfo(JobTestChildren.CmdExe, "/c exit 0")
            { CreateNoWindow = true, UseShellExecute = false })!;
        process.WaitForExit();
        Assert.True(process.HasExited);

        Assert.Null(MemoryLine.PrivateBytesOf(process.Id));
    }

    /// <summary>Hiçbir sürece ait olamayacak numaralar (Windows PID'leri 4'ün katıdır; sıfır boşta sürecin sahte numarasıdır) açılamaz:
    /// <c>null</c>, istisna değil.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void A_process_id_that_names_no_process_reads_as_null(int processId) =>
        Assert.Null(MemoryLine.PrivateBytesOf(processId));

    // ---------------------------------------------------------------- ayırma

    /// <summary>
    /// Şerit 3 sn'de bir örnek alır ve ısınmadan sonra örnek başına SIFIR bayt ayırır: iki job sorgusu ve iki özel bayt okuması ısınma
    /// sonrası hiçbir şey ayırmaz (iç tampon ilk çağrıda bir kez kurulur; P/Invoke kalıpları kopyasız).
    /// <c>Process.PrivateMemorySize64</c> aynı iş için çağrı başına birkaç KB yönetilen bellek (ve yerel bir görüntü tamponu) ayırırdı.
    /// </summary>
    [Fact]
    public void Job_queries_and_private_bytes_allocate_nothing_after_warm_up()
    {
        using var job = JobObject.CreateKillOnClose();
        using var child = JobProcessLauncher.Launch(
            job, JobTestChildren.GatedBurnChildCmdLine(), new LaunchOptions(RedirectStdio: true));
        var buffer = new int[16];
        int pid = child.Pid;
        Action sample = () =>
        {
            job.QueryAccounting();
            job.QueryProcessIds(buffer);
            MemoryLine.PrivateBytesOf(pid);
            MemoryLine.CurrentPrivateBytes();
        };

        for (int warmUp = 0; warmUp < 3; warmUp++) sample(); // IL stub'ları, iç tampon, JIT

        Assert.Equal(0L, PerfMeasure.AllocatedBy(sample));
    }
}
