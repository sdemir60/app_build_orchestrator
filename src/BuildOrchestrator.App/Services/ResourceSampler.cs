using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using BuildOrchestrator.Core.Diagnostics;
using BuildOrchestrator.Core.ProcessControl;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [kaynak şeridi · design v1.25.0 §2.1] Bir örnek: title bar'ın CPU ve RAM hücrelerinin ham sayıları. <c>null</c> = OKUNAMADI (hücre ve
/// tooltip parçası <c>—</c>); <c>0</c> bir ölçümdür — uyuyan motorun ve boş job'daki derleyicilerin <c>0 MB</c>'ı gerçek bir okumadır.
/// <see cref="CpuPercent"/> App + job işlemci zamanının iki okuma arasındaki farkından gelir (<see cref="ResourceSampler"/>); baytlar
/// <c>private bytes</c>'tır (commit charge — <see cref="MemoryLine"/> ile aynı ölçü): App'in kendisi, motor süreci ve job'daki diğer her süreç.
/// </summary>
internal readonly record struct ResourceSample(int? CpuPercent, long? AppBytes, long? EngineBytes, long? CompilersBytes)
{
    /// <summary>RAM hücresinin sayısı: App + motor + derleyiciler. Üç parçadan biri okunamadıysa toplam da okunamamıştır
    /// (<c>null</c>) — eksik bir toplam doğru bir sayı gibi görünürdü.</summary>
    public long? TotalBytes =>
        AppBytes is { } app && EngineBytes is { } engine && CompilersBytes is { } compilers ? app + engine + compilers : null;
}

/// <summary>
/// [kaynak şeridi] <see cref="IResourceProbe"/>'un tek okuması. <see cref="AppCpu"/> App'in toplam işlemci zamanı (ömür boyu, artan sayaç);
/// <see cref="JobCpu"/> outer job'un toplam işlemci zamanı (ÇIKMIŞ süreçler dahil, artan sayaç). Job sorgusu düştüyse (Win32 hatası, kapanmış
/// tutamaç) <see cref="JobCpu"/>, <see cref="EngineBytes"/> ve <see cref="CompilersBytes"/> <c>null</c>'dır — App'in kendi alanları bundan
/// etkilenmez. Motor uyurken job boştur: motor ve derleyiciler <c>0</c>'dır, <c>null</c> değil.
/// </summary>
internal readonly record struct ResourceReading(TimeSpan AppCpu, long? AppBytes, TimeSpan? JobCpu, long? EngineBytes, long? CompilersBytes);

/// <summary>[kaynak şeridi] Örnekleyicinin okuma dikişi: üretimde <see cref="JobResourceProbe"/>, testte sahte bir okuyucu.</summary>
internal interface IResourceProbe
{
    /// <summary>Şu anki okuma. Job'dan türeyen alanlar okunamıyorsa <c>null</c> verir, FIRLATMAZ.</summary>
    ResourceReading Read();
}

/// <summary>
/// [kaynak şeridi · karar K3] Üretimin okuyucusu: App'in sahibi olduğu outer job'dan (Supervisor ağacının tamamı — motor, git, vswhere, MSBuild
/// ve derleyicileri; iç içe job'a girenler de dahil) ve App'in kendisinden okur. Motora IPC YOKTUR ve makinedeki süreçler TARANMAZ
/// (<c>Process.GetProcesses()</c> yasak — NoProcessScanGuardTests): job'un PID listesi okunur ve her PID tek tek açılır.
///
/// <list type="bullet">
/// <item><b>CPU:</b> App = önbelleğe alınmış TEK <c>Process.GetCurrentProcess()</c> nesnesinin <c>TotalProcessorTime</c>'ı (ölçüldü: çağrı başına
/// 0 bayt); job = <see cref="JobObject.QueryAccounting"/> (kullanıcı + çekirdek, çıkmış süreçler dahil).</item>
/// <item><b>Bellek:</b> App = <see cref="MemoryLine.CurrentPrivateBytes"/>; job'daki her PID için <see cref="MemoryLine.PrivateBytesOf"/>.
/// <b>Motor</b> = <c>enginePid</c>'in süreci; <b>derleyiciler</b> = job'daki DİĞER her süreç (Sync'in git çocukları dahil). Toplam − motor
/// çıkarması yapılmaz, diğerleri doğrudan toplanır; bu yüzden derleyiciler hiçbir zaman eksi olamaz. Motor uyurken <c>enginePid</c> <c>null</c>'dır
/// ve job boştur: ikisi de <c>0</c>.</item>
/// <item><b>Çıkan süreç:</b> PID listelendi ama açılana dek süreç çıktıysa (bir derleyici bitti) <c>PrivateBytesOf</c> <c>null</c> verir ve o
/// süreç <c>0</c> sayılır — bir derleyicinin bitmesi hücreyi <c>—</c> yapmaz. <c>—</c> yalnız JOB sorgusunun kendisi düşünce çıkar.</item>
/// <item><b>Job sorgusu düşerse</b> (<see cref="Win32Exception"/> — ör. tutamaç kapanmış; <see cref="ObjectDisposedException"/> — uygulama
/// kapanırken motor konağı dispose edilmiş, son bir tik) job'dan türeyen üç alan <c>null</c> olur ve örnekleyiciye istisna ÇIKMAZ; sonraki
/// başarılı okuma düzelir.</item>
/// <item><b>Ayırma:</b> ısınmadan sonra okuma başına 0 bayt — PID tamponu ve <c>Process</c> nesnesi bir kez kurulur, okumalar sabitlenmiş
/// P/Invoke'lardır (<c>JobObject.QueryProcessIds</c> tamponu yeniden kullanır; <c>ResourceSamplerTests</c> pinler).</item>
/// </list>
///
/// <para>Yalnız UI thread'inden çağrılır (örnekleyicinin tik'i). Job'un PID listesini okuma iskeleti Exit kartının sayımıyla ortaktır
/// (<see cref="OuterJobMembers"/> — tampon, sınır, düşen sorgunun yutulması).</para>
/// </summary>
internal sealed class JobResourceProbe : IResourceProbe
{
    private readonly JobObject _outerJob;
    private readonly Func<int?> _enginePid;
    private readonly Func<int, long?> _privateBytesOf;
    private readonly Process _self = Process.GetCurrentProcess();
    private readonly OuterJobMembers _members;

    /// <param name="outerJob">App'in outer job'u (<c>EngineHost.OuterJob</c>) — yalnız OKUNUR, şerit job'a dokunmaz.</param>
    /// <param name="enginePid">Motor sürecinin PID'i (<c>EngineHost.EnginePid</c>); motor uyuyorsa/yoksa <c>null</c>. Her okumada sorulur.</param>
    public JobResourceProbe(JobObject outerJob, Func<int?> enginePid)
        : this(outerJob, enginePid, MemoryLine.PrivateBytesOf) { }

    /// <summary>Test dikişi: bir PID'in özel baytlarını okuyan işlev. Üretim <see cref="MemoryLine.PrivateBytesOf"/> kullanır; "listelendi ama
    /// açılana dek çıktı" yarışı (işlev <c>null</c> verir) gerçek bir süreçle kurulamaz, ancak böyle taklit edilir.</summary>
    internal JobResourceProbe(JobObject outerJob, Func<int?> enginePid, Func<int, long?> privateBytesOf)
    {
        ArgumentNullException.ThrowIfNull(outerJob);
        ArgumentNullException.ThrowIfNull(enginePid);
        ArgumentNullException.ThrowIfNull(privateBytesOf);
        _outerJob = outerJob;
        _members = new OuterJobMembers(outerJob);
        _enginePid = enginePid;
        _privateBytesOf = privateBytesOf;
    }

    public ResourceReading Read()
    {
        TimeSpan appCpu = _self.TotalProcessorTime;
        long? appBytes = MemoryLine.CurrentPrivateBytes();
        TimeSpan jobCpu;
        try
        {
            jobCpu = _outerJob.QueryAccounting().TotalCpuTime;
        }
        catch (Exception ex) when (ex is Win32Exception or ObjectDisposedException)
        {
            return WithoutJob();
        }
        if (!_members.TryRead(out var processIds)) return WithoutJob();

        int? enginePid = _enginePid();
        long engineBytes = 0, compilersBytes = 0;
        foreach (int pid in processIds)
        {
            long bytes = _privateBytesOf(pid) ?? 0; // çıkmış/açılamayan süreç 0 sayılır, hata değil
            if (pid == enginePid) engineBytes += bytes;
            else compilersBytes += bytes;
        }
        return new ResourceReading(appCpu, appBytes, jobCpu, engineBytes, compilersBytes);

        // Job okunamadı: job'dan türeyen üç alan okunamamıştır; App'in kendi alanları etkilenmez.
        ResourceReading WithoutJob() => new(appCpu, appBytes, null, null, null);
    }
}

/// <summary>
/// [kaynak şeridi · design v1.25.0 §3.6 · karar K3] Title bar'ın CPU/RAM örnekleyicisi: bir <see cref="IPollTimer"/>'ın tik'inde
/// (UI thread'i — iş mikrosaniyelerdir, ayrı thread gerekmez) <see cref="IResourceProbe"/>'dan okur, CPU yüzdesini hesaplar ve örneği
/// <c>publish</c>'e verir (üretimde <c>RunViewModel.ApplyResourceSample</c>). Pencereyi, view model'i ve Dispatcher türünü bilmez;
/// kabuk (MainWindow) kurar ve <see cref="SetActive"/> ile sürer.
///
/// <para><b>Ritim.</b> İlk etkinleşmede o anki okuma CPU TABANI olur ve ilk örnek <see cref="FirstDelay"/> (0,5 sn) sonra gelir — hücre ilk
/// 3 sn <c>—</c> beklemez, gerçek sayıyı hemen gösterir; ondan sonra <see cref="Period"/> (3 sn) ritmi. <see cref="SetActive"/>(false)
/// zamanlayıcıyı DURDURUR (pencere gizli/küçültülmüş: tepsideki uygulama kaynak ölçmez) ve araya giren tik yutulur; yeniden
/// <see cref="SetActive"/>(true) HEMEN bir örnek alır, ritim yine 3 sn. Aynı değeri yinelemek bir şey yapmaz (kabuk her durum
/// değişiminde bildirir).</para>
///
/// <para><b>CPU yüzdesi</b> = (ΔApp + ΔJob işlemci zamanı) / (Δduvar × işlemci sayısı) — hesap ve yuvarlama Core'da
/// (<see cref="ResourceStripText.CpuPercent"/>), burada yalnız farklar alınır. Duvar saati enjekte edilir (<c>clock</c>): ölçülen tikin
/// nominal aralığı değil saatin farkıdır. Taban, job'un okunabildiği SON okumadır; job okunamayan bir örnek tabanı ilerletmez ve CPU'su
/// <c>null</c> olur, sonraki başarılı örnek son geçerli tabandan ölçer (hücre bir sonraki örnekte düzelir). R5: pencere geri gelince
/// alınan anlık örnekte CPU, SON ÖRNEKTEN beri geçen sürenin — gizli kalınan süre DAHİL — ortalamasıdır (gizlenirken yeni okuma alınmaz);
/// 3 sn sonra güncel pencereye dönülür.</para>
///
/// <para><b>Ayırma.</b> Tik delegeleri bir kez kurulur; <see cref="SampleNow"/> ısınmadan sonra örnek başına 0 bayt ayırır (örnek bir
/// <c>readonly record struct</c>, okuma bir struct; <c>ResourceSamplerTests</c> pinler). <c>publish</c>'in kendi ayırması burada sayılmaz.</para>
///
/// <para><b>R7 sondası.</b> Üretim kurucusu <c>BO_PROBE_STRIP</c> bir dosya yolu taşıyorsa her örnekte o dosyaya tek satır yazar
/// (<see cref="StripSampleProbe"/>); yoksa hiçbir şey yapmaz. Kabuğun ayrıca bir şey bağlaması gerekmez.</para>
/// </summary>
internal sealed class ResourceSampler
{
    /// <summary>İlk etkinleşmeden ilk örneğe kadar geçen süre.</summary>
    internal static readonly TimeSpan FirstDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>İlk örnekten sonraki (ve her yeniden etkinleşmeden sonraki) örnek aralığı.</summary>
    internal static readonly TimeSpan Period = TimeSpan.FromSeconds(3);

    private readonly IResourceProbe _probe;
    private readonly IPollTimer _timer;
    private readonly Func<TimeSpan> _clock;
    private readonly int _processorCount;
    private readonly Action<ResourceSample> _publish;
    private readonly StripSampleProbe? _trace;
    private readonly Action _firstTick;
    private readonly Action _tick;

    private bool _active;
    private bool _everActivated;

    // CPU tabanı: job'un okunabildiği SON okuma (duvar saati + App ve job işlemci zamanı).
    private bool _hasBase;
    private TimeSpan _baseAt;
    private TimeSpan _baseAppCpu;
    private TimeSpan _baseJobCpu;

    /// <param name="probe">Okuma dikişi (<see cref="JobResourceProbe"/>).</param>
    /// <param name="timer">Tik kaynağı; <see cref="IPollTimer.Start"/> aynı anda yalnız bir kez geçerlidir, bu sınıf araya <c>Stop</c> koyar.</param>
    /// <param name="clock">Duvar saati (tek yönlü artan); CPU yüzdesinin paydasıdır. Üretimde <see cref="MonotonicNow"/>.</param>
    /// <param name="processorCount">Mantıksal işlemci sayısı (<c>Environment.ProcessorCount</c>).</param>
    /// <param name="publish">Her örneğin gittiği yer.</param>
    public ResourceSampler(IResourceProbe probe, IPollTimer timer, Func<TimeSpan> clock, int processorCount, Action<ResourceSample> publish)
        : this(probe, timer, clock, processorCount, publish, StripSampleProbe.StartIfRequested()) { }

    /// <summary>Sondayı açıkça veren kurucu (testler <c>null</c> ya da bir geçici dosya verir; süit ortam değişkenine bağlı olmaz).</summary>
    internal ResourceSampler(IResourceProbe probe, IPollTimer timer, Func<TimeSpan> clock, int processorCount,
        Action<ResourceSample> publish, StripSampleProbe? trace)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(timer);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(publish);
        _probe = probe;
        _timer = timer;
        _clock = clock;
        _processorCount = processorCount;
        _publish = publish;
        _trace = trace;
        _firstTick = FirstTick;
        _tick = Tick;
    }

    /// <summary>Şimdiye dek alınan örnek sayısı (testler ve "gizliyken örnek yok" kanıtı). Etkinleşmenin taban okuması örnek DEĞİLDİR.</summary>
    internal int SampleCount { get; private set; }

    /// <summary>Örnekleme açık mı (<see cref="SetActive"/>'in son değeri). Kabuğun etkinlik kuralının (pencere yüklü, görünür, küçültülmemiş)
    /// sonucunu gözleyen testler içindir; üretim kodu okumaz.</summary>
    internal bool IsActive => _active;

    /// <summary>Üretimin saati: <see cref="Stopwatch"/> tabanlı, tek yönlü artan, ayırmaz.</summary>
    internal static TimeSpan MonotonicNow() => Stopwatch.GetElapsedTime(0);

    /// <summary>
    /// Örneklemeyi açar/kapatır. Kabuk bunu pencere görünürlüğü her değiştiğinde çağırır (<c>Loaded</c>, yüzeyin gizlenmesi/geri gelmesi,
    /// pencere durumu): <c>true</c> = pencere görünür ve küçültülmemiş. Aynı değeri yinelemek bir şey yapmaz.
    /// </summary>
    public void SetActive(bool active)
    {
        if (active == _active) return;
        _active = active;
        if (!active)
        {
            _timer.Stop();
            return;
        }

        if (!_everActivated)
        {
            _everActivated = true;
            Rebase(_probe.Read(), _clock()); // ilk etkinleşme: bu okuma CPU tabanıdır, ilk örnek FirstDelay sonra
            _timer.Start(FirstDelay, _firstTick);
            return;
        }

        _timer.Start(Period, _tick);
        SampleNow(); // yeniden görünür: HEMEN bir örnek (R5: CPU son örnekten beri geçen sürenin ortalaması)
    }

    /// <summary>İlk tik: ritmi 3 sn'ye alır ve ilk örneği verir. Durdurulmuşken gelen (kuyruğa girmiş) bir tik hiçbir şey yapmaz.</summary>
    private void FirstTick()
    {
        if (!_active) return;
        _timer.Stop();
        _timer.Start(Period, _tick);
        SampleNow();
    }

    private void Tick()
    {
        if (_active) SampleNow();
    }

    /// <summary>Şimdi bir örnek alır: okur, CPU yüzdesini son tabana göre hesaplar, <c>publish</c>'e verir ve (sonda açıksa) bir satır yazar.
    /// Etkin olup olmadığına bakmaz — çağıran (tik, yeniden etkinleşme, test) karar verir.</summary>
    internal ResourceSample SampleNow()
    {
        ResourceReading reading = _probe.Read();
        TimeSpan now = _clock();

        int? cpu = null;
        if (reading.JobCpu is { } jobCpu && _hasBase)
            cpu = ResourceStripText.CpuPercent((reading.AppCpu - _baseAppCpu) + (jobCpu - _baseJobCpu), now - _baseAt, _processorCount);
        Rebase(reading, now);

        var sample = new ResourceSample(cpu, reading.AppBytes, reading.EngineBytes, reading.CompilersBytes);
        SampleCount++;
        _publish(sample);
        _trace?.Record(sample);
        return sample;
    }

    /// <summary>CPU tabanını bu okumaya taşır — yalnız job okunabildiyse (okunamayan okuma tabanı bozmaz, bir sonraki başarılı örnek eski
    /// tabandan ölçer).</summary>
    private void Rebase(in ResourceReading reading, TimeSpan now)
    {
        if (reading.JobCpu is not { } jobCpu) return;
        _baseAt = now;
        _baseAppCpu = reading.AppCpu;
        _baseJobCpu = jobCpu;
        _hasBase = true;
    }
}

/// <summary>
/// [R7 · perf sondası] Kaynak şeridinin örneklerini bir dosyaya satır satır yazan kaydedici — yalnız <see cref="Variable"/>
/// (<c>BO_PROBE_STRIP</c>) bir dosya yolu taşıyorsa vardır; üretimde yoktur (<see cref="FrameGapProbe"/> kalıbı). Amaç: gerçek uygulamada
/// "pencere gizliyken örnekleme durur" iddiasını gözle doğrulamak — her ÖRNEK bir satır yazar, gizliyken satır eklenmez; ilk etkinleşmenin taban
/// okuması örnek sayılmaz, dosya o zamana dek açılmaz bile.
///
/// <para>Satır: <c>HH:mm:ss.fff cpu=&lt;yüzde|-&gt; ram=&lt;MB|-&gt;</c> — CPU tam sayı yüzde, RAM App + motor + derleyiciler toplamı
/// (tam MB, <see cref="MemoryLine.Megabytes"/>); okunamayan değer <c>-</c>. Dosya her satırda açılıp kapanır (sondayı tutan bir tutamaç
/// yoktur, başka bir araç dosyayı okuyabilir/silebilir); yazım hatası (olmayan klasör, kilitli dosya, geçersiz yol) SESSİZCE yutulur — bu
/// bir tanıdır, örnekleyiciye ve uygulamaya hata çıkarmaz.</para>
/// </summary>
internal sealed class StripSampleProbe
{
    internal const string Variable = "BO_PROBE_STRIP";

    private readonly string _path;

    private StripSampleProbe(string path) => _path = path;

    /// <summary>Ortam değişkeni bir yol taşıyorsa sondayı kurar; yoksa <c>null</c> — çağıran hiçbir şey yapmaz.</summary>
    internal static StripSampleProbe? StartIfRequested() => Open(Environment.GetEnvironmentVariable(Variable));

    /// <summary>Verilen yola yazan sonda; yol boş/boşluksa <c>null</c> (sonda kapalı). Yolun yazılabilirliği burada denenmez, yazımda yutulur.</summary>
    internal static StripSampleProbe? Open(string? path) => string.IsNullOrWhiteSpace(path) ? null : new StripSampleProbe(path);

    /// <summary>Örneği dosyaya bir satır olarak ekler; hiçbir yazım hatası dışarı çıkmaz.</summary>
    internal void Record(ResourceSample sample)
    {
        try
        {
            File.AppendAllText(_path, Line(sample) + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // tanı: yazılamadı — örnekleyici ve uygulama etkilenmez
        }
    }

    /// <summary>Bir örneğin satırı (satır sonu YOK): zaman, CPU yüzdesi, toplam RAM (MB).</summary>
    internal static string Line(ResourceSample sample)
    {
        string cpu = sample.CpuPercent is { } percent ? percent.ToString(CultureInfo.InvariantCulture) : "-";
        string ram = sample.TotalBytes is { } total ? MemoryLine.Megabytes(total) : "-";
        return string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:HH:mm:ss.fff} cpu={cpu} ram={ram}");
    }
}
