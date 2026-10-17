using System.IO;
using System.Windows.Threading;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Tests.ProcessControl;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kaynak şeridi · design v1.25.0 §2.1, §3.6] Title bar'ın kaynak şeridinin örnekleyicisi: <see cref="ResourceSampler"/> (ritim, durdurma,
/// CPU yüzdesinin tabanı), onun gerçek okuyucusu <see cref="JobResourceProbe"/> (outer job + App'in kendisi) ve ortam değişkeniyle açılan
/// dosya sondası <see cref="StripSampleProbe"/> (<c>BO_PROBE_STRIP</c>).
///
/// <para>[D8] Sleep/poll yok: zamanlayıcı elle tıklatılan <see cref="FakePollTimer"/>, saat elle sürülen bir <see cref="TimeSpan"/>'dir; ölçülen
/// şey tikin nominal aralığı değil saatin FARKIDIR (CPU yüzdesi duvar saatiyle bölünür). Tek istisna gerçek <c>DispatcherTimer</c> testidir:
/// aralıklar milisaniyelere kısaltılır ve dispatcher bir KOŞULA kadar pompalanır (üst sınır yalnız güvenlik). Gerçek probe testleri canlı üyeyi
/// stdin'i kapanana dek bekleyen bir çocukla kurar (<see cref="JobTestChildren.GatedBurnChildCmdLine"/>), doğumunu job'un IOCP'si bildirir.</para>
/// </summary>
public class ResourceSamplerTests
{
    private const long Mb = 1024 * 1024;

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);

    /// <summary>Başarılı bir okuma: App ve job işlemci zamanı (sn) ve baytlar. Varsayılan baytlar uyuyan motorun okumasıdır — job boş,
    /// motor ve derleyiciler 0 (ölçüm; okunamadı DEĞİL).</summary>
    private static ResourceReading Reading(double appCpu, double jobCpu, long appBytes = 300 * Mb, long engineBytes = 0, long compilersBytes = 0) =>
        new(Seconds(appCpu), appBytes, Seconds(jobCpu), engineBytes, compilersBytes);

    /// <summary>Job sorgusu düştü (Win32 hatası): probe'un verdiği okuma — yalnız App alanları dolu, job alanları <c>null</c>.</summary>
    private static ResourceReading JobUnreadable(double appCpu, long appBytes = 300 * Mb) =>
        new(Seconds(appCpu), appBytes, null, null, null);

    /// <summary>Sahte probe: <see cref="Next"/> ne ise <see cref="Read"/> onu döner.</summary>
    private sealed class FakeProbe : IResourceProbe
    {
        public ResourceReading Next { get; set; }

        public int Reads { get; private set; }

        public ResourceReading Read()
        {
            Reads++;
            return Next;
        }
    }

    /// <summary>Örnekleyici + sahte probe + elle tıklatılan zamanlayıcı + elle sürülen saat; yayınlanan örnekler <see cref="Published"/>'ta.
    /// Sondayı testler AÇIKÇA verir (varsayılan: yok) — süit ortam değişkenine bağlı değildir.</summary>
    private sealed class Rig
    {
        public FakeProbe Probe { get; } = new();

        public FakePollTimer Timer { get; } = new();

        public List<ResourceSample> Published { get; } = [];

        public TimeSpan Now { get; set; }

        public ResourceSampler Sampler { get; }

        public Rig(int processorCount = 4, StripSampleProbe? trace = null) =>
            Sampler = new ResourceSampler(Probe, Timer, () => Now, processorCount, Published.Add, trace);
    }

    // ---------------------------------------------------------------- ritim: ilk 0,5 sn, sonra 3 sn; gizliyken durur

    [Fact]
    public void The_first_activation_arms_a_half_second_first_sample_then_settles_into_the_three_second_beat()
    {
        var rig = new Rig();
        Assert.Equal(TimeSpan.FromMilliseconds(500), ResourceSampler.FirstDelay);
        Assert.Equal(TimeSpan.FromSeconds(3), ResourceSampler.Period);
        Assert.False(rig.Timer.IsRunning);

        rig.Sampler.SetActive(true);

        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromMilliseconds(500), rig.Timer.Interval);
        Assert.Equal(0, rig.Sampler.SampleCount); // etkinleşme bir örnek DEĞİL, CPU tabanı okumasıdır
        Assert.Empty(rig.Published);

        rig.Now = Seconds(0.5);
        rig.Timer.Tick();

        Assert.Equal(1, rig.Sampler.SampleCount);
        Assert.Single(rig.Published);
        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(3), rig.Timer.Interval);

        rig.Now = Seconds(3.5);
        rig.Timer.Tick();

        Assert.Equal(2, rig.Sampler.SampleCount);
        Assert.Equal(TimeSpan.FromSeconds(3), rig.Timer.Interval);
    }

    [Fact]
    public void Pausing_stops_the_timer_and_no_sample_follows()
    {
        var rig = new Rig();
        rig.Sampler.SetActive(true);
        rig.Now = Seconds(0.5);
        rig.Timer.Tick();
        int before = rig.Sampler.SampleCount;
        Assert.Equal(1, before); // duraklatmadan önce örnek alınıyordu: aşağıdaki "sabit kaldı" iddiası boş değil

        rig.Sampler.SetActive(false);
        rig.Now = Seconds(30);
        rig.Timer.Tick();

        Assert.False(rig.Timer.IsRunning);
        Assert.Equal(before, rig.Sampler.SampleCount);
        Assert.Equal(before, rig.Published.Count);
    }

    /// <summary>Zamanlayıcı durdurulduktan SONRA da elindeki tik'i teslim eder: dispatcher'ın <c>Stop</c>'tan hemen önce kuyruğa aldığı bir
    /// tik'i taklit eder. <see cref="FakePollTimer"/> durdurulunca tik'i atar, yani bu yolu görmez.</summary>
    private sealed class StrayTickTimer : IPollTimer
    {
        private Action? _tick;

        public bool IsRunning { get; private set; }

        public void Start(TimeSpan interval, Action tick)
        {
            if (IsRunning) return;
            IsRunning = true;
            _tick = tick;
        }

        public void Stop() => IsRunning = false; // tik bilerek tutulur

        public void Fire() => _tick?.Invoke();
    }

    /// <summary>Örnekleyicinin kendi sözü: durdurulmuşken hiçbir örnek yok — zamanlayıcı tik'i ne zaman teslim ederse etsin. İlk-tik yolu da
    /// (durdurulmuşken zamanlayıcıyı 3 sn'ye YENİDEN KURMAZ) ve süreğen tik yolu da.</summary>
    [Fact]
    public void A_tick_queued_before_the_pause_is_ignored_on_both_paths()
    {
        var timer = new StrayTickTimer();
        var probe = new FakeProbe { Next = Reading(0, 0) };
        var published = new List<ResourceSample>();
        TimeSpan now = TimeSpan.Zero;
        var sampler = new ResourceSampler(probe, timer, () => now, 4, published.Add, trace: null);

        sampler.SetActive(true);  // ilk tik 0,5 sn sonra gelecekti
        sampler.SetActive(false); // pencere gizlendi; tik çoktan kuyruğa girmişti
        timer.Fire();

        Assert.Empty(published);
        Assert.False(timer.IsRunning); // ilk-tik yolu zamanlayıcıyı yeniden kurmadı

        sampler.SetActive(true); // anında örnek + süreğen tik
        Assert.Single(published);
        sampler.SetActive(false);
        timer.Fire();

        Assert.Single(published); // süreğen yoldan gelen kaçak tik de yutuldu
        Assert.Equal(1, sampler.SampleCount);
    }

    [Fact]
    public void Resuming_takes_one_sample_at_once_and_returns_to_the_three_second_beat()
    {
        var rig = new Rig();
        rig.Sampler.SetActive(true);
        rig.Now = Seconds(0.5);
        rig.Timer.Tick();
        rig.Sampler.SetActive(false);
        rig.Now = Seconds(120);

        rig.Sampler.SetActive(true);

        Assert.Equal(2, rig.Sampler.SampleCount); // ANINDA bir örnek: tikin gelmesi beklenmedi
        Assert.Equal(2, rig.Published.Count);
        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(3), rig.Timer.Interval); // 0,5 sn'lik ilk gecikme yalnız ilk etkinleşmeye aittir

        rig.Now = Seconds(123);
        rig.Timer.Tick();

        Assert.Equal(3, rig.Sampler.SampleCount);
    }

    /// <summary>İlk örnekten ÖNCE durdurulup sürdürülünce de anında örnek alınır (ilk etkinleşme tabanı zaten okundu): pencere açıldıktan
    /// yarım saniye içinde gizlenip yeniden gösterilirse hücre 0,5 sn daha boş beklemez.</summary>
    [Fact]
    public void Pausing_before_the_first_sample_still_resumes_with_an_immediate_one()
    {
        var rig = new Rig();
        rig.Sampler.SetActive(true);
        rig.Sampler.SetActive(false);
        rig.Now = Seconds(10);

        rig.Sampler.SetActive(true);

        Assert.Equal(1, rig.Sampler.SampleCount);
        Assert.Equal(TimeSpan.FromSeconds(3), rig.Timer.Interval);
    }

    /// <summary>Kabuk durumu her değişimde bildirir (<c>Loaded</c>, yüzey gizleme, pencere durumu): aynı değeri yinelemek bir şey yapmaz —
    /// ne ikinci bir taban okuması ne fazladan bir anlık örnek.</summary>
    [Fact]
    public void Repeating_the_same_state_changes_nothing()
    {
        var rig = new Rig();
        rig.Sampler.SetActive(false); // zaten pasif
        Assert.False(rig.Timer.IsRunning);
        Assert.Equal(0, rig.Probe.Reads);

        rig.Sampler.SetActive(true);
        rig.Sampler.SetActive(true);
        Assert.Equal(1, rig.Probe.Reads); // taban TEK kez okundu
        Assert.Equal(TimeSpan.FromMilliseconds(500), rig.Timer.Interval);

        rig.Now = Seconds(0.5);
        rig.Timer.Tick();
        rig.Sampler.SetActive(true); // zaten aktif: anında örnek YOK

        Assert.Equal(1, rig.Sampler.SampleCount);
        Assert.Equal(2, rig.Probe.Reads);
    }

    /// <summary>Üretimin zamanlayıcısını (<see cref="DispatcherPollTimer"/>) tutar ama aralıkları kısaltır: 0,5 sn → 5 ms, 3 sn → 10 ms. Böylece
    /// gerçek bir <c>DispatcherTimer</c>'ın davranışı (aralığı kendi tik'inin İÇİNDEN durdurup yeniden kurmak) gerçek zaman beklenmeden sınanır.</summary>
    private sealed class ShortIntervalTimer(IPollTimer inner) : IPollTimer
    {
        public bool IsRunning => inner.IsRunning;

        public void Start(TimeSpan interval, Action tick) =>
            inner.Start(TimeSpan.FromMilliseconds(interval < TimeSpan.FromSeconds(1) ? 5 : 10), tick);

        public void Stop() => inner.Stop();
    }

    /// <summary>
    /// Gerçek <c>DispatcherTimer</c> üstünde ritim geçişi: ilk tik zamanlayıcıyı kendi tik'inin içinden durdurup süreğen ritimle yeniden kurar ve
    /// örnekler akmaya devam eder (ilk örnek + iki süreğen). Hiçbir sleep yok: dispatcher koşula kadar pompalanır (üst sınır yalnız güvenlik).
    /// </summary>
    [StaFact]
    public void The_first_tick_rearms_a_real_dispatcher_timer_from_inside_its_own_tick_and_the_beat_goes_on()
    {
        var timer = new ShortIntervalTimer(new DispatcherPollTimer(Dispatcher.CurrentDispatcher));
        var probe = new FakeProbe { Next = Reading(0, 0) };
        int published = 0;
        var sampler = new ResourceSampler(probe, timer, ResourceSampler.MonotonicNow, 4, _ => published++, trace: null);

        sampler.SetActive(true);
        DispatcherPump.PumpUntil(() => published >= 3, TimeSpan.FromSeconds(30));
        sampler.SetActive(false);

        Assert.True(published >= 3, $"the first sample and two periodic ones arrived: {published}");
        Assert.False(timer.IsRunning);
    }

    // ---------------------------------------------------------------- CPU yüzdesi: Core formülü, örnekler arası fark

    /// <summary>App + job işlemci zamanı farkı, duvar saati farkı × çekirdek sayısına bölünür (Task 1'in formülü): 0,5 sn + 1 sn / (3 sn × 4)
    /// = %12,5 → yarımlar sıfırdan uzağa → 13. İlk örnek, ETKİNLEŞMEDE okunan tabandan ölçülür.</summary>
    [Theory]
    [InlineData(4, 13)]
    [InlineData(8, 6)]
    [InlineData(1, 50)]
    public void The_cpu_percent_is_the_core_formula_over_the_app_and_the_job(int processorCount, int expected)
    {
        var rig = new Rig(processorCount);
        rig.Probe.Next = Reading(appCpu: 10, jobCpu: 20);
        rig.Sampler.SetActive(true); // taban: App 10 sn, job 20 sn
        rig.Now = Seconds(3);
        rig.Probe.Next = Reading(appCpu: 10.5, jobCpu: 21);

        rig.Timer.Tick();

        Assert.Equal(expected, Assert.Single(rig.Published).CpuPercent);
    }

    /// <summary>R5: pencere geri gelince alınan anlık örneğin CPU'su, SON ÖRNEKTEN beri geçen sürenin (gizli kalınan süre DAHİL) ortalamasıdır —
    /// gizlenirken yeni bir okuma alınıp taban ileri alınmaz, dönüşte kısa bir pencereye bölünmez. 6 sn işlemci / (60,5 sn × 4) = %2,5 → 2;
    /// kısa bir pencereye bölünseydi (6 sn / (3 sn × 4)) %50 çıkardı. Üç saniye sonra güncel pencereye dönülür.</summary>
    [Fact]
    public void The_hidden_stretch_is_averaged_into_the_first_sample_after_coming_back_then_the_beat_returns_to_the_current_window()
    {
        var rig = new Rig();
        rig.Probe.Next = Reading(appCpu: 0, jobCpu: 0);
        rig.Sampler.SetActive(true);
        rig.Now = Seconds(0.5);
        rig.Probe.Next = Reading(appCpu: 0.5, jobCpu: 0);
        rig.Timer.Tick(); // son örnek: t = 0,5 sn, App 0,5 sn
        rig.Now = Seconds(1);
        rig.Sampler.SetActive(false); // tepsiye gizlendi

        rig.Now = Seconds(61);
        rig.Probe.Next = Reading(appCpu: 6.5, jobCpu: 0); // gizliyken App 6 sn işlemci yaktı
        rig.Sampler.SetActive(true);

        Assert.Equal(2, rig.Published[^1].CpuPercent);

        rig.Now = Seconds(64);
        rig.Probe.Next = Reading(appCpu: 7.25, jobCpu: 0); // son 3 sn: 0,75 sn / (3 sn × 4) = %6,25
        rig.Timer.Tick();

        Assert.Equal(6, rig.Published[^1].CpuPercent);
    }

    // ---------------------------------------------------------------- job okunamadı: job'dan türeyen alanlar null; sonraki başarılı örnek düzelir

    /// <summary>Probe job alanlarını <c>null</c> verince (Win32 hatası) yayınlanan örnekte CPU, motor ve derleyici baytları <c>null</c>'dır
    /// (hücreler <c>—</c>); App'in kendi baytları durur. Bir sonraki başarılı örnek DÜZELİR ve CPU'yu son BAŞARILI okumanın tabanından ölçer
    /// (okunamayan örnek tabanı ilerletmez): 3 sn işlemci / (6 sn × 4) = %12,5 → 13.</summary>
    [Fact]
    public void A_job_that_cannot_be_read_blanks_the_job_derived_fields_and_the_next_good_sample_recovers()
    {
        var rig = new Rig();
        rig.Probe.Next = Reading(appCpu: 0, jobCpu: 0);
        rig.Sampler.SetActive(true);

        rig.Now = Seconds(3);
        rig.Probe.Next = JobUnreadable(appCpu: 0.5);
        var blank = rig.Sampler.SampleNow();

        Assert.Null(blank.CpuPercent);
        Assert.Null(blank.EngineBytes);
        Assert.Null(blank.CompilersBytes);
        Assert.Equal(300 * Mb, blank.AppBytes);
        Assert.Null(blank.TotalBytes); // RAM hücresi de okunamadı

        rig.Now = Seconds(6);
        rig.Probe.Next = Reading(appCpu: 1, jobCpu: 2);
        var recovered = rig.Sampler.SampleNow();

        Assert.Equal(13, recovered.CpuPercent);
        Assert.Equal(0L, recovered.EngineBytes);
        Assert.Equal(0L, recovered.CompilersBytes);
        Assert.Equal(300 * Mb, recovered.TotalBytes);
    }

    /// <summary>Uyuyan motor: job boş, motor ve derleyiciler 0 (ölçüm) — örnek bunu <c>null</c>'a ÇEVİRMEZ; RAM yalnız App'tir.</summary>
    [Fact]
    public void A_sleeping_engine_is_a_measured_zero_and_the_ram_is_the_app_alone()
    {
        var rig = new Rig();
        rig.Probe.Next = Reading(appCpu: 0, jobCpu: 0, appBytes: 300 * Mb, engineBytes: 0, compilersBytes: 0);
        rig.Sampler.SetActive(true);
        rig.Now = Seconds(3);

        rig.Timer.Tick();

        var sample = Assert.Single(rig.Published);
        Assert.Equal(new ResourceSample(0, 300 * Mb, 0, 0), sample);
        Assert.Equal(300 * Mb, sample.TotalBytes);
    }

    [Fact]
    public void A_sample_carries_the_three_byte_readings_and_their_sum()
    {
        var rig = new Rig();
        rig.Sampler.SetActive(true);
        rig.Now = Seconds(3);
        rig.Probe.Next = Reading(appCpu: 0, jobCpu: 0, appBytes: 310 * Mb, engineBytes: 85 * Mb, compilersBytes: 4505 * Mb);

        var sample = rig.Sampler.SampleNow();

        Assert.Equal((310 * Mb, 85 * Mb, 4505 * Mb), (sample.AppBytes, sample.EngineBytes, sample.CompilersBytes));
        Assert.Equal(4900 * Mb, sample.TotalBytes);
        Assert.Equal(sample, Assert.Single(rig.Published)); // dönen örnek yayınlananın aynısı
        Assert.Equal(1, rig.Sampler.SampleCount);
    }

    [Theory]
    [InlineData(null, 1L, 1L)]
    [InlineData(1L, null, 1L)]
    [InlineData(1L, 1L, null)]
    public void The_total_is_unreadable_when_any_of_its_three_parts_is(long? app, long? engine, long? compilers) =>
        Assert.Null(new ResourceSample(5, app, engine, compilers).TotalBytes);

    [Fact]
    public void The_monotonic_clock_never_goes_back()
    {
        TimeSpan first = ResourceSampler.MonotonicNow();
        TimeSpan second = ResourceSampler.MonotonicNow();

        Assert.True(first > TimeSpan.Zero, $"the clock reads a positive time: {first}");
        Assert.True(second >= first, $"{first} -> {second}");
    }

    // ---------------------------------------------------------------- gerçek probe: outer job + App'in kendisi

    /// <summary>Boş job (motor uyuyor): motor 0, derleyiciler 0 (ölçüm, <c>null</c> değil), job işlemci zamanı 0; App'in kendi baytı ve işlemci
    /// zamanı sıfırdan büyük.</summary>
    [Fact]
    public void An_empty_job_reads_zero_for_the_engine_and_the_compilers_and_the_app_for_itself()
    {
        using var job = JobObject.CreateKillOnClose();

        var reading = new JobResourceProbe(job, () => null).Read();

        Assert.Equal(0L, reading.EngineBytes);
        Assert.Equal(0L, reading.CompilersBytes);
        Assert.Equal(TimeSpan.Zero, reading.JobCpu);
        Assert.True(reading.AppBytes > 0, $"this process holds private bytes: {reading.AppBytes}");
        Assert.True(reading.AppCpu > TimeSpan.Zero, $"this process has used cpu: {reading.AppCpu}");
    }

    /// <summary>Motor = <c>enginePid</c>'in süreci; derleyiciler = job'daki DİĞER her süreç (job toplamı − motor, hiçbir zaman eksi değil).
    /// Motor PID'i job'da yoksa (uyuyan motor) hepsi derleyicidir.</summary>
    [Fact]
    public void The_engine_process_counts_as_the_engine_and_every_other_member_as_compilers()
    {
        using var job = JobObject.CreateKillOnClose();
        using var port = job.AttachCompletionPort();
        using var child = JobProcessLauncher.Launch(
            job, JobTestChildren.GatedBurnChildCmdLine(), new LaunchOptions(RedirectStdio: true));
        using var more = JobBirths.WaitForProcess(port, TimeSpan.FromSeconds(30), "the waiting process was never born",
            process => process.ProcessName.StartsWith("more", StringComparison.OrdinalIgnoreCase));
        int enginePid = child.Pid;

        var asEngine = new JobResourceProbe(job, () => enginePid).Read();
        var noEngine = new JobResourceProbe(job, () => null).Read();

        Assert.True(asEngine.EngineBytes > 0, $"the engine is a live member: {asEngine.EngineBytes}");
        Assert.True(asEngine.CompilersBytes > 0, $"the other members are compilers: {asEngine.CompilersBytes}");
        Assert.Equal(0L, noEngine.EngineBytes);
        Assert.True(noEngine.CompilersBytes > 0, $"with no engine pid every member is a compiler: {noEngine.CompilersBytes}");
        Assert.NotNull(asEngine.JobCpu);
    }

    /// <summary>
    /// Derleyiciler job'daki DİĞER süreçlerin toplamıdır — motor iki kez sayılmaz, derleyiciler motorun baytından küçük kalabilir ve eksi
    /// olamaz. Gerçek okumalar bunu ayıramaz (cmd'nin baytı, kendisi + <c>more</c> içinde "sıfırdan büyük" kalır); okuyucu dikişiyle sabit
    /// değerler verilir: motor 7 MB, diğer her süreç 1000 bayt. "Derleyiciler = job toplamı" yazılsaydı derleyiciler ≥ 7 MB çıkardı.
    /// </summary>
    [Fact]
    public void The_compilers_are_the_other_members_alone_and_the_engine_is_not_counted_twice()
    {
        using var job = JobObject.CreateKillOnClose();
        using var port = job.AttachCompletionPort();
        using var child = JobProcessLauncher.Launch(
            job, JobTestChildren.GatedBurnChildCmdLine(), new LaunchOptions(RedirectStdio: true));
        using var more = JobBirths.WaitForProcess(port, TimeSpan.FromSeconds(30), "the waiting process was never born",
            process => process.ProcessName.StartsWith("more", StringComparison.OrdinalIgnoreCase));
        int enginePid = child.Pid;
        const long engineBytes = 7 * Mb;
        var probe = new JobResourceProbe(job, () => enginePid, privateBytesOf: pid => pid == enginePid ? engineBytes : 1_000);

        var reading = probe.Read();

        Assert.Equal(engineBytes, reading.EngineBytes);
        Assert.True(reading.CompilersBytes >= 1_000 && reading.CompilersBytes < engineBytes,
            $"the compilers are the other members only: {reading.CompilersBytes}");
        Assert.Equal(0L, reading.CompilersBytes % 1_000);
    }

    /// <summary>PID listelendi ama <c>OpenProcess</c>'e kadar süreç çıktı (bir derleyici bitti): o süreç 0 sayılır, hücre <c>—</c> OLMAZ —
    /// ne motor ne derleyiciler <c>null</c> olur. Çıkış taklidi: okuyucu o PID için <c>null</c> döner (gerçek <c>MemoryLine.PrivateBytesOf</c>
    /// çıkmış süreç için de <c>null</c> verir).</summary>
    [Fact]
    public void A_listed_process_that_is_gone_before_it_can_be_opened_counts_as_zero_and_never_blanks_the_cells()
    {
        using var job = JobObject.CreateKillOnClose();
        using var port = job.AttachCompletionPort();
        using var child = JobProcessLauncher.Launch(
            job, JobTestChildren.GatedBurnChildCmdLine(), new LaunchOptions(RedirectStdio: true));
        using var more = JobBirths.WaitForProcess(port, TimeSpan.FromSeconds(30), "the waiting process was never born",
            process => process.ProcessName.StartsWith("more", StringComparison.OrdinalIgnoreCase));
        int gonePid = child.Pid;
        var probe = new JobResourceProbe(job, () => gonePid, privateBytesOf: pid => pid == gonePid ? null : 1_000);

        var reading = probe.Read();

        Assert.Equal(0L, reading.EngineBytes); // okunamayan motor 0 sayıldı, null DEĞİL
        Assert.True(reading.CompilersBytes >= 1_000 && reading.CompilersBytes % 1_000 == 0,
            $"the readable members are summed: {reading.CompilersBytes}");
        Assert.NotNull(reading.JobCpu);
    }

    /// <summary>Job sorgusu düşerse (geçersiz tutamaç → <see cref="System.ComponentModel.Win32Exception"/>; job kapatılmış →
    /// <see cref="ObjectDisposedException"/>, uygulama kapanırken bir tik) YALNIZ job'dan türeyen alanlar <c>null</c> olur; App'in kendi
    /// okuması durur ve istisna örnekleyiciye ÇIKMAZ.</summary>
    [Fact]
    public void A_job_that_cannot_be_queried_blanks_only_the_job_derived_fields()
    {
        var invalid = JobResourceQueryTests.JobOver(JobResourceQueryTests.ImpossibleHandle);
        var disposed = JobObject.CreateKillOnClose();
        disposed.Dispose();

        foreach (var job in new[] { invalid, disposed })
        {
            var reading = new JobResourceProbe(job, () => null).Read();

            Assert.Null(reading.JobCpu);
            Assert.Null(reading.EngineBytes);
            Assert.Null(reading.CompilersBytes);
            Assert.True(reading.AppBytes > 0);
            Assert.True(reading.AppCpu > TimeSpan.Zero);
        }
    }

    // ---------------------------------------------------------------- ayırma: ısınma sonrası örnek başına SIFIR bayt

    /// <summary>
    /// Örnekleyici ısınmadan sonra örnek başına SIFIR bayt ayırır (plan Global Constraints): gerçek probe + gerçek saat, yayın no-op. CPU
    /// yüzdesi yolu da koşar (ısınma turları tabanı kurar, ölçülen çağrı yüzdeyi hesaplar). <c>Process.GetProcesses()</c>/anlık görüntü yok,
    /// iç tamponlar ve delegeler bir kez kurulur.
    /// </summary>
    [Fact]
    public void Sampler_allocates_nothing_per_sample_after_warm_up()
    {
        using var job = JobObject.CreateKillOnClose();
        var sampler = new ResourceSampler(new JobResourceProbe(job, () => null), new FakePollTimer(),
            ResourceSampler.MonotonicNow, Environment.ProcessorCount, static _ => { }, trace: null);
        Action sample = () => sampler.SampleNow();
        for (int warmUp = 0; warmUp < 5; warmUp++) sample(); // JIT, ilk Process okuması, iç tamponlar, CPU tabanı

        Assert.Equal(0L, PerfMeasure.AllocatedBy(sample));
    }

    /// <summary>Aynı pin, job'da CANLI bir üye varken: PID döngüsü, <c>OpenProcess</c>/<c>GetProcessMemoryInfo</c> ve motor/derleyici ayrımı
    /// ölçülen yolun içindedir.</summary>
    [Fact]
    public void Sampler_allocates_nothing_with_a_live_job_member_either()
    {
        using var job = JobObject.CreateKillOnClose();
        using var child = JobProcessLauncher.Launch(
            job, JobTestChildren.GatedBurnChildCmdLine(), new LaunchOptions(RedirectStdio: true));
        int enginePid = child.Pid;
        var sampler = new ResourceSampler(new JobResourceProbe(job, () => enginePid), new FakePollTimer(),
            ResourceSampler.MonotonicNow, Environment.ProcessorCount, static _ => { }, trace: null);
        Action sample = () => sampler.SampleNow();
        for (int warmUp = 0; warmUp < 5; warmUp++) sample();

        Assert.Equal(0L, PerfMeasure.AllocatedBy(sample));
        Assert.True(sampler.SampleNow().EngineBytes > 0, "the measured path really saw the live member");
    }

    // ---------------------------------------------------------------- R7: BO_PROBE_STRIP dosya sondası

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_path_the_probe_is_off(string? path) => Assert.Null(StripSampleProbe.Open(path));

    [Fact]
    public void The_probe_variable_is_the_documented_one() => Assert.Equal("BO_PROBE_STRIP", StripSampleProbe.Variable);

    [Fact]
    public void A_probe_line_is_the_time_then_the_cpu_percent_then_the_ram_in_megabytes()
    {
        string line = StripSampleProbe.Line(new ResourceSample(13, 300 * Mb, 0, 0));

        Assert.Matches(@"^\d{2}:\d{2}:\d{2}\.\d{3} cpu=13 ram=300$", line);
    }

    [Fact]
    public void An_unreadable_value_is_a_dash_in_the_probe_line()
    {
        string line = StripSampleProbe.Line(new ResourceSample(null, 300 * Mb, null, null));

        Assert.Matches(@"^\d{2}:\d{2}:\d{2}\.\d{3} cpu=- ram=-$", line);
    }

    /// <summary>
    /// "Gizliyken örnekleme durur" kanıtının birim karşılığı: açık sonda her örnekte TEK satır yazar ve örnekleyici durdurulmuşken hiç
    /// yazmaz; anlık örnek de bir örnektir ve bir satır yazar. Etkinleşmenin taban okuması örnek değildir, satır yazmaz (dosya bile açılmaz).
    /// </summary>
    [Fact]
    public void An_armed_probe_writes_one_line_per_sample_and_none_while_the_sampler_is_paused()
    {
        using var dir = new TempDir();
        string file = Path.Combine(dir.Path, "strip.log");
        var rig = new Rig(trace: StripSampleProbe.Open(file));
        rig.Probe.Next = Reading(appCpu: 0, jobCpu: 0);

        rig.Sampler.SetActive(true);
        Assert.False(File.Exists(file));

        rig.Now = Seconds(0.5);
        rig.Probe.Next = Reading(appCpu: 0.25, jobCpu: 0.25);
        rig.Timer.Tick();
        Assert.Single(File.ReadAllLines(file));

        rig.Sampler.SetActive(false);
        rig.Now = Seconds(30);
        rig.Timer.Tick();
        Assert.Single(File.ReadAllLines(file)); // gizliyken satır yok

        rig.Sampler.SetActive(true); // anında örnek
        string[] lines = File.ReadAllLines(file);

        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.Matches(@"^\d{2}:\d{2}:\d{2}\.\d{3} cpu=\d+ ram=300$", line));
    }

    /// <summary>Sonda bir hata ayıklama aracıdır: yazamıyorsa (olmayan klasör, geçersiz yol) örnekleyiciye istisna ÇIKARMAZ, sessizce atlar.</summary>
    [Fact]
    public void A_probe_that_cannot_write_never_throws_into_the_sampler()
    {
        using var dir = new TempDir();
        string missingDirectory = Path.Combine(dir.Path, "missing-directory");
        string[] unwritable =
        [
            Path.Combine(missingDirectory, "strip.log"), // olmayan klasör
            "bad\0name",                                 // yolda boş karakter
        ];

        foreach (string file in unwritable)
        {
            var probe = StripSampleProbe.Open(file);

            Assert.NotNull(probe);
            probe.Record(new ResourceSample(1, Mb, 0, 0)); // fırlatmamalı
        }

        Assert.False(Directory.Exists(missingDirectory)); // sonda klasör de açmaz
    }
}
