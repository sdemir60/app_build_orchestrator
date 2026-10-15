using System.ComponentModel;
using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.App.Services;

/// <summary>[D1 review · A2] Motorun hiç doğamama nedeni — kullanıcıya gösterilen metni bu ayrım belirler.</summary>
public enum EngineUnavailableReason
{
    /// <summary>Supervisor çalıştırılabiliri uygulamanın yanında YOK (eksik/bozuk kurulum; publish çıktısına
    /// <c>supervisor\</c> klasörü girmemiş).</summary>
    NotFound,

    /// <summary>Dosya var ama BAŞLATILAMADI: geçersiz/bozuk exe, erişim reddi, TOCTOU (pre-flight ile launch
    /// arasında silinme) — <c>CreateProcessW</c> senkron başarısız oldu.</summary>
    CannotStart,
}

/// <summary>
/// [D1] Motor hiç doğamadı. Ham <see cref="System.ComponentModel.Win32Exception"/> yerine bu AYRIŞAN tür
/// atılır: çağıran (MainWindow) bunu kullanıcıya görünür, yeniden başlatması ANLAMSIZ bir kurulum hatasına
/// çevirir. Child hiç doğmadığı için <see cref="EngineHost.EngineExited"/> ASLA ateşlenmez → kullanıcıya TEK
/// sinyal gider. Mesaj metni İngilizce'dir (uygulama İngilizce-only).
/// </summary>
public sealed class EngineUnavailableException(string exePath, EngineUnavailableReason reason, Exception? inner = null)
    : InvalidOperationException(
        reason == EngineUnavailableReason.NotFound
            ? $"Supervisor executable was not found: {exePath}"
            : $"Supervisor executable could not be started: {exePath}", inner)
{
    /// <summary>Aranan Supervisor exe yolu — konsol satırında gösterilir.</summary>
    public string ExePath { get; } = exePath;

    /// <summary>Neden (kullanıcıya gösterilecek metni ayırt eder).</summary>
    public EngineUnavailableReason Reason { get; } = reason;
}

/// <param name="supervisorArgs">Supervisor'a geçilecek ek argümanlar. <b>Üretim HİÇ geçmez</b> (Supervisor
/// argümansız başlar, önbellek kullanıcının <c>%LOCALAPPDATA%</c>'sındadır). Yalnız testler kullanır: gerçek bir motor
/// başlatan her test <c>--logs &lt;sandbox&gt;</c> ile izole bir önbellek verir — aksi hâlde motorun açılış
/// kurtarması (spec 2026-09-18 §5.5) kullanıcının gerçek <c>run-inflight.json</c>'ını işlerdi.</param>
/// <param name="killStrategy">Motoru öldüren strateji (bkz. <see cref="KillStrategy"/>). <b>Üretim HİÇ geçmez</b>
/// (varsayılan: Supervisor ve altındaki ağaç). Yalnız testler kullanır: öldürmenin hangi bağlamda koştuğu ancak buradan
/// gözlenir.</param>
public sealed class EngineHost(string supervisorExePath, TimeSpan? startupTimeout = null,
    IReadOnlyList<string>? supervisorArgs = null, Action<System.Diagnostics.Process>? killStrategy = null) : IAsyncDisposable
{
    /// <summary>[B1/F1] <see cref="StartAsync"/>'in <c>engineReady</c>'yi beklerken vazgeçme süresi.
    /// <b>Üretim varsayılanı 5s'de KALIR</b> — donmuş bir supervisor'da uygulama sonsuza dek asılı kalmasın
    /// diye vazgeçmek şart. Enjekte edilebilir olan yalnız BU süre: testler (yük altında supervisor 5s'de
    /// hazır olamayabilir) daha geniş bir değer geçebilir — desen <c>ConsoleView.WallClock</c>/
    /// <c>NeverTickingBatcher</c> ile aynı: üretim varsayılanı sabit, seam test için var.
    /// <para><b>fix-1 · İŞ 1b:</b> <c>private</c> değil <c>internal</c> — varsayılanın 5s'de kaldığını
    /// <c>EngineHostTests.Default_startup_timeout_stays_five_seconds</c> saf literal ile pinler (A13/T4'ün
    /// <c>PopIn.DurationMs</c> deseni). Aksi halde biri flake'i "5 → 60" yaparak susturur ve süit sessiz kalır.</para></summary>
    internal TimeSpan StartupTimeout { get; } = startupTimeout ?? TimeSpan.FromSeconds(5);
    private readonly JobObject _outerJob = JobObject.CreateKillOnClose(); // §3: App = outer Job sahibi
    private JobChildProcess? _child;
    private NdjsonWriter? _writer;
    private TaskCompletionSource<EngineReadyEvent>? _ready;
    private int _generation; // cross-thread okuma → volatile [it0-devir]
    private int _exitReportedGen = -1; // EngineExited hangi generation için fırlatıldı — monotonik tek-atım [it0-devir]

    /// <summary>[idle-memory · dalga 3 · C3] Yaşam döngüsünün TEK kapısı: <see cref="StartAsync"/>, <see cref="RestartAsync"/>,
    /// <see cref="SleepAsync"/>, <see cref="WakeAsync"/> ve <see cref="DisposeAsync"/> bu kapının altında sıraya girer — iki eşzamanlı
    /// uyanma iki süreç doğuramaz, bir uyku yarım kalmış bir başlatmayı öldüremez, dispose uçuştaki bir uyanmanın bitmesini bekler.
    /// <see cref="SendAsync"/> kapıyı ALMAZ (sıcak yol; yazımları <c>NdjsonWriter</c>'ın kendi kilidi sıralar). Kapı hiç dispose
    /// edilmez: bekleme tanıtıcısı istenmedikçe çekirdek nesnesi yoktur ve ikinci <see cref="DisposeAsync"/> da kapıdan geçebilmelidir.
    /// Kapıdaki her bekleyiş <c>ConfigureAwait(false)</c>'tır: çıkıştaki disposal çağıranın (bloklu Dispatcher) bağlamına bağımlı
    /// olamaz [T62 fix].</summary>
    private readonly SemaphoreSlim _lifecycle = new(1, 1);

    /// <summary>[idle-memory · dalga 3] <see cref="IsAsleep"/>'in alanı — yalnız kapının içinde yazılır, her thread'den okunur.</summary>
    private volatile bool _asleep;

    // [D1 review · A3] Bu mesaj KULLANICIYA görünür: TrySendAsync onu konsola "[error] failed to send …"
    // satırıyla yazar. Uygulama İngilizce-only olduğundan metin de İngilizce. [idle-memory · dalga 3] Uyanmanın reddi
    // (uyumayan ve canlı olmayan konak) aynı durumu anlatır — metin tek yerde.
    private const string NotRunningMessage = "Engine is not running.";

    public event Action<IpcEvent>? EventReceived;
    public event Action<int?>? EngineExited;
    public int? EnginePid => _child?.Pid;

    /// <summary>[idle-memory · dalga 3 · spec §6.4] Motor KASITLI olarak durduruldu (<see cref="SleepAsync"/>) ve bir sonraki
    /// <see cref="WakeAsync"/>'i bekliyor: süreç yok, <see cref="EnginePid"/> null, gönderim bugünkü "motor çalışmıyor" hatasıyla
    /// reddedilir. Hiç başlamamış ya da kendiliğinden ölmüş bir konak UYUMAZ — ölüm <see cref="EngineExited"/>'in, toparlanması
    /// <see cref="RestartAsync"/>'in işidir. Ön kontrolü geçen her başlatma (başlat, yeniden başlat, uyandır) uykuyu bitirir; ön
    /// kontrolde düşen uyanma (exe yok) hiçbir şeye dokunmaz, uyku sürer. Her thread'den okunabilir.</summary>
    public bool IsAsleep => _asleep;

    /// <summary>Uçuştaki stderr drain'i (test yüzeyi) — motor yaşadığı sürece tamamlanmaz.</summary>
    internal Task? StderrDrain { get; private set; }

    /// <summary>[idle-memory · dalga 3] Son başlatılan motorun stdout okuma döngüsü (test yüzeyi) — borusu kapanınca biter.
    /// Eski bir neslin geç olayının yeni motorunkiymiş gibi geçmediği ancak o neslin okuyucusu bitince kesinleşir.</summary>
    internal Task? ReadLoop { get; private set; }

    /// <summary>[idle-memory · dalga 3] Son başlatılan motorun çıkış bekçisi (test yüzeyi): süreç çıkıp nesil kararını verince
    /// (<see cref="EngineExited"/> ya da sessizlik) biter. Kasıtlı bir durdurmanın ölüm sinyali ÜRETMEDİĞİ ancak bu bekçi bitince
    /// kesinleşir — sabit bir beklemeyle değil.</summary>
    internal Task? ExitWatch { get; private set; }

    /// <summary>[P3 · Task 5] Outer Job (test yüzeyi): gerçek motorla koşan testler ağacı — Supervisor ve onun
    /// <c>MSBuild.exe</c>'leri — bu job'un IOCP'si üzerinden izler (<c>SafeExitProcessTests</c>). Üretim job'a
    /// buradan dokunmaz; sahibi ve tek kapatanı <see cref="DisposeAsync"/>'tir.</summary>
    internal JobObject OuterJob => _outerJob;

    /// <summary>
    /// Motorun stderr'ini EOF'a kadar okur ve ATAR. Yönlendirilmiş üç pipe'ın (stdin/stdout/stderr) her biri
    /// tüketilmek ZORUNDADIR: okunmayan bir pipe'ın tamponu dolduğunda yazan taraf — yani motor —
    /// <c>Console.Error.WriteLine</c>'ın ortasında SONSUZA DEK bloklanır. Bu, motoru tanı yazdığı HERHANGİ bir
    /// noktada dondurabilir; ölçülen vakada planlamanın ortasıydı: <c>runStarted</c> hiç gelmedi, App kilit
    /// penceresinde asılı kaldı ve ardından gelen <c>stopRun</c>'ın ack'i de yazılamadığı için faz sonsuza dek
    /// <c>Stopping</c>'de kaldı (bkz. <c>EngineStderrDrainTests</c>).
    /// <para><b>İçerik bilerek ATILIR:</b> motorun stderr'i tanı kanalıdır ve kullanıcıya ulaşması gereken her
    /// şey zaten iki yoldan gidiyor — <c>decision.log</c>'a disk üstünde, kullanıcıya görünecekler ise IPC
    /// olayları olarak. Buradaki tek sorumluluk pipe'ı AKIŞTA TUTMAKTIR.</para>
    /// <para>Child öldüğünde/dispose edildiğinde okuma EOF verir ya da fırlatır; her iki durumda da sessizce
    /// biter — drain'in kendisi asla bir hata yüzeyi üretmez.</para>
    /// </summary>
    internal static async Task DrainEngineStderrAsync(Stream stderr)
    {
        ArgumentNullException.ThrowIfNull(stderr);
        var scratch = new byte[4096];
        try
        {
            while (await stderr.ReadAsync(scratch).ConfigureAwait(false) > 0) { }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // child öldü / pipe kapandı — drain'in işi bitti.
        }
    }

    public async Task<EngineReadyEvent> StartAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try { return await StartCoreAsync(ct).ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }

    /// <summary>Başlatmanın kendisi — yaşam döngüsü kapısının İÇİNDEN çağrılır (<see cref="StartAsync"/>, <see cref="RestartAsync"/>,
    /// <see cref="WakeAsync"/>); kapıyı kendisi almaz (kapı yeniden girilemez).</summary>
    private async Task<EngineReadyEvent> StartCoreAsync(CancellationToken ct)
    {
        // [D1] Pre-flight: exe yoksa CreateProcessW'nin ham Win32Exception'ını beklemeyiz — generation'a
        // DOKUNMADAN (hiçbir yan etki bırakmadan) ayrışan tür atılır.
        if (!File.Exists(supervisorExePath))
            throw new EngineUnavailableException(supervisorExePath, EngineUnavailableReason.NotFound);

        int gen = Interlocked.Increment(ref _generation);
        _asleep = false; // [dalga 3] yeni nesil başladı: uyku (varsa) biter — başlatma düşse de konak artık uyuyan değildir
        _ready = new TaskCompletionSource<EngineReadyEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        string cmdLine = WindowsCommandLine.Build(supervisorExePath, [.. supervisorArgs ?? []]);
        // [D1 review · A2] Pre-flight'ı GEÇEN ama başlatılamayan exe (bozuk/geçersiz binary, erişim reddi,
        // TOCTOU) de AYNI görünür yüzeye düşer: ham Win32Exception generic catch'te yutulurdu. Child hiç
        // doğmadığı için burada da EngineExited ateşlenmez → tek sinyal.
        try
        {
            _child = JobProcessLauncher.Launch(_outerJob, cmdLine, new LaunchOptions(RedirectStdio: true));
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            // generation GERİ ALINMAZ: monotonik kalmalı (numara yeniden kullanılırsa eski bir watcher yeni
            // generation'ın rapor hakkını çalabilirdi). Bu gen için hiç watcher/reader kurulmadı — sinyal yok.
            throw new EngineUnavailableException(supervisorExePath, EngineUnavailableReason.CannotStart, ex);
        }
        _writer = new NdjsonWriter(_child.StandardInput!);
        var reader = new NdjsonReader(_child.StandardOutput!);
        ReadLoop = Task.Run(() => ReadLoopAsync(reader, gen), CancellationToken.None);
        // Yönlendirilen ÜÇ pipe'ın da tüketicisi olmak zorunda: stdin'i biz yazıyoruz, stdout'u yukarıdaki
        // okuma döngüsü tüketiyor, stderr ise hiç okunmuyordu — tamponu dolduğunda motor kendi tanı satırını
        // yazarken kilitleniyordu (bkz. DrainEngineStderrAsync).
        StderrDrain = Task.Run(() => DrainEngineStderrAsync(_child.StandardError!), CancellationToken.None);
        var watched = _child;
        ExitWatch = Task.Run(async () =>
        {
            int code = await watched.WaitForExitAsync(CancellationToken.None);
            if (Volatile.Read(ref _generation) == gen)
            {
                // [D1 review · A3] Kullanıcıya ulaşabilen HER metin İngilizce (uygulama İngilizce-only).
                _ready?.TrySetException(new InvalidOperationException($"Engine died during startup (exit {code}).")); // startup-crash tek sinyal
                if (TryClaimExit(gen)) EngineExited?.Invoke(code);
            }
        }, CancellationToken.None);
        try
        {
            // ConfigureAwait(false): catch devamı çağıranın (UI thread'i) bağlamına DÖNMEZ. KillCurrent process'in çıkışını
            // en çok KillExitWait (1 s) bekler; bağlama dönseydi Dispatcher o kadar bloklanırdı (yavaş ilk açılış, antivirüs
            // taraması: 5 s aşılır). Çağıranlar (MainWindow.StartEngineAsync, RunViewModel.RestartEngineAsync) kendi
            // await'leriyle UI'a döner — burada dönmeye gerek yok. [final review #1]
            return await _ready.Task.WaitAsync(StartupTimeout, ct).ConfigureAwait(false);
        }
        catch
        {
            if (Volatile.Read(ref _generation) == gen) KillCurrent(); // timeout/iptal → child sızmasın [it0-devir]
            throw;
        }
    }

    /// <summary>Yazılacak boru yokken — motor hiç başlamadı, uyuyor ya da konak onu kapattı — SENKRON fırlar
    /// (<see cref="NotRunningMessage"/>); kendiliğinden ölmüş motorun kırık borusuna yazım ise dönen görevde düşer.</summary>
    public Task SendAsync(IpcCommand command, CancellationToken ct = default) =>
        _writer?.WriteAsync(command, ct) ?? throw new InvalidOperationException(NotRunningMessage);

    public async Task<EngineReadyEvent> RestartAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ShutdownGracefullyAsync().ConfigureAwait(false);
            return await StartCoreAsync(ct).ConfigureAwait(false);
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>[idle-memory · dalga 3 · spec §6.4] İşsiz motoru KASITLI durdurur: <see cref="ShutdownCommand"/> gider, sürecin kendi
    /// çıkışı en çok <see cref="SleepExitWait"/> beklenir, çıkmazsa öldürülür. Kapanış yolu Restart ve çıkışla AYNIDIR
    /// (<see cref="ShutdownGracefullyAsync"/>; uyku yalnız zarif bekleyiş süresini verir): nesil süreç gitmeden ÖNCE artar, eski bekçi
    /// ve okuyucu susar — kasıtlı çıkış <see cref="EngineExited"/> ÜRETMEZ. Sonunda <see cref="IsAsleep"/> true,
    /// <see cref="EnginePid"/> null.
    /// <para>Yalnız canlı ve hazır bir motorda iş yapar (<see cref="HasLiveReadyEngine"/>); hiç başlamamış, hazır olamamış ya da
    /// ölümü bildirilmiş bir konakta HİÇBİR durumu değiştirmez. Uyuyan konakta tekrar çağrı no-op'tur. "Motor işsiz mi" sorusu
    /// çağıranındır (VM'in boşta kuralı); konak yalnız süreci yönetir.</para>
    /// <para><b>Öldürme çağıranın bağlamında koşmaz</b> (çağıran tik, yani UI thread'i olabilir): öldürme ancak zarif bekleyiş süreç
    /// YAŞARKEN dolunca gelir; o bekleyiş canlı süreçte hep askıya girer ve <c>ConfigureAwait(false)</c> ile havuzda devam eder —
    /// ağaç öldürme + ≤ <see cref="KillExitWait"/> bekleme havuzda koşar. Bekleyiş askıya girmeden dönüyorsa süreç çoktan çıkmıştır;
    /// öldürülecek bir şey kalmamıştır.</para></summary>
    public async Task SleepAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!HasLiveReadyEngine()) return;
            await ShutdownGracefullyAsync(SleepExitWait).ConfigureAwait(false);
            _asleep = true;
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>[idle-memory · dalga 3 · spec §6.4] Uyuyan motoru başlatır — ilk başlatmanın yolu: yeni nesil, süreç outer job'a
    /// girer, <c>engineReady</c> beklenir, vazgeçme süresi aynıdır (<see cref="StartupTimeout"/>). Uyanık ve canlı motorda hiçbir şey
    /// başlatmaz, çalışan motorun hazır olayını döner. Uyumayan ve canlı olmayan konakta (hiç başlamamış, hazır olamamış ya da ölümü
    /// bildirilmiş) <see cref="InvalidOperationException"/> atar: motoru ilk kez başlatmak <see cref="StartAsync"/>'in, ölümü
    /// toparlamak <see cref="RestartAsync"/>'in işidir.
    /// <para>Eşzamanlı çağrılar kapıda sıraya girer: ilki başlatır, sonrakiler uyanık motoru bulup onun hazır olayını alır — tek
    /// süreç.</para>
    /// <para>Başlatma düşerse <see cref="StartAsync"/>'in sözleşmesi geçerlidir: exe yoksa <see cref="EngineUnavailableException"/> ve
    /// hiçbir şey değişmez (uyku sürer); exe başlatılamadıysa yine o tür, ölüm sinyali yok; süreç doğdu ama hazır olamadıysa
    /// (zaman aşımı, iptal, erken ölüm) öldürülür ve <see cref="EngineExited"/> TEK sinyaldir. Son ikisinde konak artık uyumaz.</para></summary>
    public async Task<EngineReadyEvent> WakeAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_asleep) return await StartCoreAsync(ct).ConfigureAwait(false);
            if (HasLiveReadyEngine()) return await _ready!.Task.ConfigureAwait(false);
            throw new InvalidOperationException(NotRunningMessage);
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>Kapının içinde sorulur: bu neslin motoru hazır (<c>engineReady</c> geldi) ve ölümü bildirilmedi. Kendiliğinden ölüm
    /// <see cref="EngineExited"/> ile bildirildiği ANDA cevap "hayır"a döner — konak ölümü VM ile aynı sinyalden öğrenir. Bildirimden
    /// önceki kısa pencere (süreç çıktı, bekçisi henüz uyanmadı) bilinçli sınırdır: o anda uyutulan motorun ölümü uyku sayılır (nesil
    /// artar, bekçi susar) ve sonraki komut taze bir motor uyandırır — uyku yalnız işsiz motora istenir, kaybolan iş yoktur.</summary>
    private bool HasLiveReadyEngine() =>
        _child is not null
        && _ready is { Task.IsCompletedSuccessfully: true }
        && Volatile.Read(ref _exitReportedGen) < Volatile.Read(ref _generation);

    private async Task ReadLoopAsync(NdjsonReader reader, int gen)
    {
        try
        {
            while (true)
            {
                IpcEvent? ev;
                try { ev = await reader.ReadAsync<IpcEvent>(CancellationToken.None); }
                catch (IpcFramingException)
                {
                    // Bozuk frame = kalıcı sağırlık YARATMAZ; Supervisor exit-2 ile simetri: engine'i öldür + TEK sinyal. [it0-devir]
                    if (Volatile.Read(ref _generation) == gen)
                    {
                        _ready?.TrySetException(new InvalidOperationException("Engine died with a framing error.")); // startup'ta anlık sinyal [it0-devir]
                        Interlocked.Increment(ref _generation); // exit watcher'ı sustur → EngineExited tek kez
                        KillCurrent();
                        if (TryClaimExit(gen)) EngineExited?.Invoke(null); // tek raporcu kazanır, stale gen yeni geni bloklayamaz
                    }
                    return;
                }
                if (ev is null) return; // EOF — exit watcher bildirir
                // [idle-memory · dalga 3 · C24] Nesli geride kalmış motorun olayı DÜŞÜRÜLÜR: uykuya, yeniden başlatmaya ya da çıkışa
                // giren motorun okuyucusunda kalmış geç bir olay — engineReady dahil; o, yeni neslin hazır görevini eski PID ile
                // tamamlardı — yeni motorunkiymiş gibi geçemez. Okuma sürer (continue): bayat bir süreç de borusu dolduğu için
                // yazarken asılı kalmasın.
                if (Volatile.Read(ref _generation) != gen) continue;
                if (ev is EngineReadyEvent ready) _ready?.TrySetResult(ready);
                EventReceived?.Invoke(ev);
            }
        }
        catch { /* stream koptu — exit watcher bildirir */ }
    }

    // ConfigureAwait(false): bu yol DisposeAsync'ten (App.OnExit → UI/Dispatcher thread'i) çağrılır. Çağıranın
    // SynchronizationContext'i yakalanırsa continuation, o an bekleyişte bloklu olan Dispatcher'a post edilir →
    // sync-over-async deadlock (process çıkamaz). Disposal yolu ASLA çağıranın context'ine bağımlı olmamalı. [T62 fix]
    // [idle-memory · dalga 3 · C15] Kapanışın TEK yolu — Restart, çıkış ve uyku. Restart ile çıkış zarif çıkışı beklemez
    // (gracefulExitWait varsayılanı sıfır: shutdown yazılır, hemen öldürülür); uyku SleepExitWait verir: süreç shutdown'la kendisi
    // çıkarsa öldürmeye gerek kalmaz. Bekleyiş sürecin tanıtıcısına bağlıdır, sınırı bir zaman aşımı token'ıdır (D8: sleep yok);
    // dolarsa — ya da yazım düşerse — öldürme devralır.
    private async Task ShutdownGracefullyAsync(TimeSpan gracefulExitWait = default)
    {
        Interlocked.Increment(ref _generation); // eski watcher/reader sustur — süreç gitmeden ÖNCE: kasıtlı çıkış EngineExited üretmez
        var child = _child;
        try
        {
            if (_writer is not null)
                await _writer.WriteAsync(new ShutdownCommand()).WaitAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false); // graceful [it0-devir]
            if (child is not null && gracefulExitWait > TimeSpan.Zero)
            {
                using var exitWait = new CancellationTokenSource(gracefulExitWait);
                await child.WaitForExitAsync(exitWait.Token).ConfigureAwait(false);
            }
        }
        catch { /* zaten ölmüş olabilir ya da zarif bekleyiş doldu — öldürme devralır */ }
        KillCurrent();
    }

    // gen için EngineExited'i monotonik olarak sahiplen. Eski (stale) generation, yeni generation'ın
    // rapor hakkını ÇALAMAZ (prev >= gen ise kaybeder); aynı generation'ın iki raporcusundan yalnız biri kazanır. [it0-devir]
    private bool TryClaimExit(int gen)
    {
        while (true)
        {
            int prev = Volatile.Read(ref _exitReportedGen);
            if (prev >= gen) return false;
            if (Interlocked.CompareExchange(ref _exitReportedGen, gen, prev) == prev) return true;
        }
    }

    /// <summary>[motor · Task 11] Öldürülen process'in çıkışına tanınan süre — güncelleyici <c>current\</c> klasörünü
    /// değiştirmeden önce <c>supervisor\*.dll</c> kilitleri bırakılmış olsun (Update.exe App'in çıkışını bekler,
    /// Supervisor'ın değil). <c>TerminateProcess</c> asenkrondur: çağrı döndüğünde process birkaç ms daha yaşar.
    /// Çıkış bütçesi (<see cref="Shell.AppShutdown.DisposalTimeout"/>, 2 s) graceful yazma (500 ms) + bu süreyi
    /// kapsar. Değeri <c>EngineHostTests.Kill_exit_wait_stays_one_second</c> literal ile pinler.</summary>
    internal static readonly TimeSpan KillExitWait = TimeSpan.FromSeconds(1);

    /// <summary>[idle-memory · dalga 3 · spec §6.4] Uykuda motorun <see cref="ShutdownCommand"/>'dan sonra KENDİ çıkmasına tanınan
    /// süre — dolarsa öldürülür. Sağlıklı, işsiz bir Supervisor ms'ler içinde çıkar; süre yalnız kilitli bir motorun üst sınırıdır.
    /// Restart ve çıkış zarif çıkışı beklemez (çıkışın bütçesi <see cref="Shell.AppShutdown.DisposalTimeout"/>). Değeri
    /// <c>EngineHostTests.Sleep_exit_wait_stays_two_seconds</c> literal ile pinler.</summary>
    internal static readonly TimeSpan SleepExitWait = TimeSpan.FromSeconds(2);

    /// <summary>Motorun öldürülmesi: Supervisor ve altındaki ağaç (<c>MSBuild.exe</c>'ler dahil).</summary>
    private static readonly Action<System.Diagnostics.Process> KillTree = p => p.Kill(entireProcessTree: true);

    /// <summary>[final review #1] <see cref="KillCurrent"/>'in öldürme stratejisi — üretimde HEP <see cref="KillTree"/>
    /// (kimse vermez). Dikiş yalnız testler içindir: öldürme, process'in çıkışını en çok <see cref="KillExitWait"/> bekler;
    /// bu beklemenin çağıranın (UI) bağlamında koşmadığı ancak öldürmenin İÇİNDEN gözlenir
    /// (<c>EngineHostTests.A_start_that_gives_up_kills_the_engine_off_the_callers_context</c>). Desen
    /// <see cref="StartupTimeout"/> ile aynı: üretim değeri sabit, seam test için.</summary>
    internal Action<System.Diagnostics.Process> KillStrategy { get; } = killStrategy ?? KillTree;

    /// <summary>[motor · Task 11 · fix-1] Öldür ve process'in GERÇEKTEN sonlanmasını bekle (en çok
    /// <see cref="KillExitWait"/>). Öldürme stratejisi parametredir: üretim ağacı öldürür (<see cref="KillTree"/>);
    /// test etkisi geç inen bir öldürme verir — ağaç öldürmenin process taraması kısa yarışı örttüğü için bekleme
    /// ancak böyle sınanır (<c>EngineHostKillWaitTests.Kill_and_await_exit_returns_only_after_the_process_has_ended</c>).</summary>
    internal static void KillAndAwaitExit(System.Diagnostics.Process process, Action<System.Diagnostics.Process> kill)
    {
        kill(process);
        process.WaitForExit(KillExitWait);
    }

    private void KillCurrent()
    {
        var child = Interlocked.Exchange(ref _child, null); // atomik: yalnız bir thread non-null alır → idempotent [it0-devir]
        if (child is null) return;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(child.Pid);
            KillAndAwaitExit(process, KillStrategy);
        }
        catch (ArgumentException) { /* zaten öldü */ }
        child.Dispose();
        _writer = null;
    }

    public async ValueTask DisposeAsync()
    {
        // [idle-memory · dalga 3] Kapıdan geçer: uçuştaki bir başlatma/uyanma/uyku önce biter. Uyuyan konakta gönderilecek komut ve
        // öldürülecek süreç yoktur — kapanış yalnız outer job'ı kapatır. Uzun bir bekleyiş çıkışı kilitleyemez: App.OnExit kendi
        // tavanında (AppShutdown.DisposalTimeout) vazgeçer, process çıkışı job'ı kapatır ve KILL_ON_JOB_CLOSE ağacı toplar.
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await ShutdownGracefullyAsync().ConfigureAwait(false); // bkz. ShutdownGracefullyAsync notu [T62 fix]
            _outerJob.Dispose(); // KILL_ON_JOB_CLOSE: her koşulda süpürge
        }
        finally { _lifecycle.Release(); }
    }
}
