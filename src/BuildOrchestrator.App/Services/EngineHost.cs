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
    /// uyanma iki süreç doğuramaz, bir uyku yarım kalmış bir başlatmayı öldüremez. Dispose sıraya girmez, ÖNE GEÇER (bkz.
    /// <see cref="_lifetime"/>). <see cref="SendAsync"/> kapıyı ALMAZ (sıcak yol; yazımları <c>NdjsonWriter</c>'ın kendi kilidi
    /// sıralar). Kapı hiç dispose edilmez: bekleme tanıtıcısı istenmedikçe çekirdek nesnesi yoktur ve ikinci
    /// <see cref="DisposeAsync"/> da kapıdan geçebilmelidir. Kapıdaki her bekleyiş <c>ConfigureAwait(false)</c>'tır: çıkıştaki
    /// disposal çağıranın (bloklu Dispatcher) bağlamına bağımlı olamaz [T62 fix].</summary>
    private readonly SemaphoreSlim _lifecycle = new(1, 1);

    /// <summary>[idle-memory · dalga 3 · fix 1 · I1] Konağın ömrü: <see cref="DisposeAsync"/> kapıyı beklemeden ÖNCE iptal eder.
    /// Başlatmanın hazır bekleyişi ve kasıtlı durdurmanın (uyku, Restart) bekleyişleri buna bağlıdır — uçuştaki işlem milisaniyeler
    /// içinde vazgeçer (<see cref="OperationCanceledException"/>), öldürmez, kapıyı bırakır; öldürmek dispose'un kendi kapanışının
    /// işidir. Tek istisna öldürmenin İÇİNDE olan işlemdir (zarif bekleyişi dolmuş bir uyku, zaman aşımına düşmüş bir başlatma): o öldürme
    /// biter — en çok <see cref="KillExitWait"/> + ağaç taraması, yine <see cref="Shell.AppShutdown.DisposalTimeout"/> içinde. Çıkış
    /// böylece bir uyanmanın (≤ <see cref="StartupTimeout"/>) ya da uykunun (≤ <see cref="SleepExitWait"/>) arkasında
    /// beklemez: <c>App.OnExit</c> disposal'ı <see cref="Shell.AppShutdown.DisposalTimeout"/> kadar bekler, sonra güncelleyiciyi
    /// başlatır — motor öldürülüp çıkışı beklenmeden değil. Hiç dispose edilmez (ikinci dispose da iptal edebilmeli).</summary>
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>[idle-memory · dalga 3 · fix 1 · M3] Konak dispose edildi: Start/Restart/Wake hiçbir yan etkiden önce
    /// <see cref="ObjectDisposedException"/> atar, uyku no-op'tur. Dispose kapıyı beklemeden önce yazar, okuyanlar kapının içinde
    /// sorar.</summary>
    private volatile bool _disposed;

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
    /// reddedilir. Hiç başlamamış ya da ölmüş bir konak UYUMAZ — ölüm <see cref="EngineExited"/>'in, toparlanması
    /// <see cref="RestartAsync"/>'in işidir; ölümle uykunun yarışı tek kuralla çözülür: <see cref="HasLiveReadyEngine"/>. Ön kontrolü
    /// geçen her başlatma (başlat, yeniden başlat, uyandır) uykuyu bitirir; ön kontrolde düşen uyanma (exe yok) hiçbir şeye dokunmaz,
    /// uyku sürer. Dispose edilmiş konak uyumaz. Her thread'den okunabilir.</summary>
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

    /// <summary>[test dikişi — idle-memory · dalga 3 · fix 1] YALNIZ testler ayarlar: çıkış bekçisi kendi neslini güncel bulduktan
    /// SONRA, o neslin hazır görevine ve ölüm hakkına dokunmadan ÖNCE çağrılır. "Bekçi kontrolü geçti, ardından nesil değişti"
    /// penceresi — kendiliğinden ölümle kasıtlı durdurmanın yarışı — ancak böyle tutulur (<c>EngineHostTests</c>). Üretimde hep
    /// null.</summary>
    internal Action? DebugExitWatchPassedCheck;

    /// <summary>[test dikişi — idle-memory · dalga 3 · fix 2] YALNIZ testler ayarlar: uyku motoru canlı bulup havuza sıçradıktan SONRA,
    /// nesli sahiplenip durdurmaya başlamadan ÖNCE (havuz thread'inde) çağrılır. "Uyku kontrolü geçti, ardından ölüm bildirildi"
    /// penceresi ancak böyle tutulur (<c>EngineHostTests</c>). Üretimde hep null.</summary>
    internal Action? DebugSleepPassedCheck;

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
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await StartCoreAsync(ct).ConfigureAwait(false);
        }
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
        // [fix 1 · I1] Dispose öne geçtiyse yeni bir süreç doğurulmaz (kapı alındıktan sonra gelen iptal; yan etki yok).
        _lifetime.Token.ThrowIfCancellationRequested();

        int gen = Interlocked.Increment(ref _generation);
        _asleep = false; // [dalga 3] yeni nesil başladı: uyku (varsa) biter — başlatma düşse de konak artık uyuyan değildir
        // [fix 1 · M1] Neslin KENDİ hazır görevi: bekçisi ve okuyucusu yalnız bunu tamamlar ya da bozar — alandaki (_ready) daha
        // yeni bir neslin görevine asla dokunmazlar.
        var ready = new TaskCompletionSource<EngineReadyEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ready = ready;
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
        ReadLoop = Task.Run(() => ReadLoopAsync(reader, gen, ready), CancellationToken.None);
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
                DebugExitWatchPassedCheck?.Invoke();
                // [D1 review · A3] Kullanıcıya ulaşabilen HER metin İngilizce (uygulama İngilizce-only).
                ready.TrySetException(new InvalidOperationException($"Engine died during startup (exit {code}).")); // startup-crash tek sinyal
                if (TryClaimExit(gen)) EngineExited?.Invoke(code);
            }
        }, CancellationToken.None);
        // [fix 1 · I1] Bekleyiş çağıranın iptaline VE konağın ömrüne bağlı: dispose öne geçerse milisaniyeler içinde vazgeçilir.
        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        try
        {
            // ConfigureAwait(false): catch devamı çağıranın (UI thread'i) bağlamına DÖNMEZ. KillCurrent process'in çıkışını
            // en çok KillExitWait (1 s) bekler; bağlama dönseydi Dispatcher o kadar bloklanırdı (yavaş ilk açılış, antivirüs
            // taraması: 5 s aşılır). Çağıranlar (MainWindow.StartEngineAsync, RunViewModel.RestartEngineAsync) kendi
            // await'leriyle UI'a döner — burada dönmeye gerek yok. [final review #1]
            return await ready.Task.WaitAsync(StartupTimeout, giveUp.Token).ConfigureAwait(false);
        }
        catch
        {
            // timeout/iptal → child sızmasın [it0-devir]. Dispose öne geçtiyse öldürmek ONUN işidir: nesil zaten ilerledi, bu
            // dal yalnız vazgeçer (öldürmez, ölüm sinyali yok).
            if (Volatile.Read(ref _generation) == gen && !_lifetime.IsCancellationRequested) KillCurrent();
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
            ObjectDisposedException.ThrowIf(_disposed, this);
            await ShutdownGracefullyAsync(giveUp: _lifetime.Token).ConfigureAwait(false);
            return await StartCoreAsync(ct).ConfigureAwait(false);
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>[idle-memory · dalga 3 · spec §6.4] İşsiz motoru KASITLI durdurur: <see cref="ShutdownCommand"/> gider, sürecin kendi
    /// çıkışı en çok <see cref="SleepExitWait"/> beklenir, çıkmazsa öldürülür. Kapanış yolu Restart ve çıkışla AYNIDIR
    /// (<see cref="ShutdownGracefullyAsync"/>; uyku yalnız zarif bekleyiş süresini verir). Sonunda <see cref="IsAsleep"/> true,
    /// <see cref="EnginePid"/> null; kasıtlı çıkış <see cref="EngineExited"/> ÜRETMEZ. Kontrolden sonra motorun kendiliğinden ölümü
    /// bildirildiyse uyku süreci yine toparlar ama UYUMAZ — ikisi de <see cref="HasLiveReadyEngine"/>'in kuralı.
    /// <para>Yalnız canlı ve hazır bir motorda iş yapar; hiç başlamamış, hazır olamamış, ölümü bildirilmiş ya da dispose edilmiş
    /// bir konakta HİÇBİR durumu değiştirmez. Uyuyan konakta tekrar çağrı no-op'tur. "Motor işsiz mi" sorusu çağıranındır (VM'in
    /// boşta kuralı); konak yalnız süreci yönetir.</para>
    /// <para><b>Çağıranın bağlamında iş yapmaz</b> (çağıran tik, yani UI thread'i olabilir): kapı serbestse çağıranın thread'inde
    /// alınır ve iki alan okunur, sonra açık bir sıçrayışla havuza geçilir — shutdown yazımı ve devamları, zarif bekleyiş, gerekirse
    /// ağaç öldürme + ≤ <see cref="KillExitWait"/> bekleme havuzda koşar.</para>
    /// <para>Dispose öne geçerse uyku vazgeçer: <see cref="OperationCanceledException"/>, öldürmeden ve uyumadan — öldürmek
    /// dispose'un kendi kapanışının işidir; uyku öldürmenin İÇİNDEYSE o öldürme biter (<see cref="_lifetime"/>'ın istisnası) ve
    /// dispose ardından konağı uyutmaz.</para></summary>
    public async Task SleepAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            int gen = Volatile.Read(ref _generation); // kontrolün canlı bulduğu nesil — uyku tam olarak bunu sahiplenir
            if (_disposed || !HasLiveReadyEngine(gen)) return;
            // [fix 1] Çağıranın bağlamından havuza sıçra: ForceYielding tamamlanmış görevi de askıya aldırır, bağlam yakalanmadığı
            // için devam havuzda koşar. Kapanışın hiçbir parçası (NdjsonWriter'ın devamları dahil) çağıranın thread'ine dönmez.
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            DebugSleepPassedCheck?.Invoke();
            // [fix 2 · N1] Sahiplenmeyi kaybettiyse ölüm kontrolden sonra bildirildi: süreç toparlandı ama konak uyumaz.
            if (await ShutdownGracefullyAsync(SleepExitWait, _lifetime.Token, claim: gen).ConfigureAwait(false))
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
    /// (zaman aşımı, iptal, erken ölüm) öldürülür ve <see cref="EngineExited"/> TEK sinyaldir. Son ikisinde konak artık uyumaz.
    /// Dispose öne geçerse uyanma vazgeçer (<see cref="OperationCanceledException"/>; öldürmek dispose'un işidir); dispose edilmiş
    /// konakta <see cref="ObjectDisposedException"/>.</para></summary>
    public async Task<EngineReadyEvent> WakeAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_asleep) return await StartCoreAsync(ct).ConfigureAwait(false);
            if (HasLiveReadyEngine(Volatile.Read(ref _generation))) return await _ready!.Task.ConfigureAwait(false);
            throw new InvalidOperationException(NotRunningMessage);
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>Kapının içinde sorulur: <paramref name="gen"/> neslinin motoru hazır (<c>engineReady</c> geldi) ve ölümü sahiplenilmedi.
    /// <para><b>Ölüm–durdurma kuralı (tek ifadesi burası):</b> bir neslin ölüm hakkını (<see cref="TryClaimExit"/>) önce alan kazanır.
    /// Kasıtlı bir durdurma nesli ilerletirken bir nesli sahiplenir (<see cref="RetireGeneration"/>): uyku tam olarak kontrolünün canlı
    /// bulduğu nesli, Restart ve çıkış giden nesli.</para>
    /// <list type="bullet">
    /// <item><b>Durdurma kazanırsa</b> kontrolünü çoktan geçmiş bir bekçi — ya da nesli kendisi ilerleten çerçeve hatası okuyucusu —
    /// hakkı kaybeder: ölüm durdurmanın parçası sayılır, ölüm sinyali yoktur (uykuda: uyku sayılır, sonraki komut taze bir motor
    /// uyandırır; uyku yalnız işsiz motora istenir, kaybolan iş yok).</item>
    /// <item><b>Bekçi (ya da okuyucu) kazanırsa</b> ölüm bildirilir (<see cref="EngineExited"/>) ve konak UYUMAZ: uyku ya kontrolde durur
    /// (bu soru "hayır" der) ya da kontrolden sonra sahiplenmeyi kaybeder, süreci toparlar ve uyumadan döner; uyanma
    /// <see cref="InvalidOperationException"/> atar — ölümü <see cref="RestartAsync"/> toparlar.</item>
    /// </list>
    /// <para>Bekçi ve okuyucu yalnız KENDİ neslinin hazır görevine dokunur — daha yeni bir başlatmayı düşüremez. Konak ölümü VM ile aynı
    /// sinyalden öğrenir; iki görüş ayrışmaz.</para></summary>
    private bool HasLiveReadyEngine(int gen) =>
        _child is not null
        && _ready is { Task.IsCompletedSuccessfully: true }
        && Volatile.Read(ref _exitReportedGen) < gen;

    /// <summary>Nesli ilerletir ve bir nesli sahiplenir — <paramref name="claim"/> verilmezse GİDEN nesli (Restart, çıkış), verilirse
    /// çağıranın kontrolünün canlı bulduğu nesli (uyku: çerçeve hatası okuyucusu nesli kendisi ilerletmiş olsa da doğru nesil).
    /// Sahiplenilen neslin bekçisi — kontrolünü çoktan geçmiş olsa bile — ölüm sinyalini artık veremez; okuyucusu olaylarını düşürür.
    /// Dönen değer sahiplenmenin kazanılıp kazanılmadığıdır (<see cref="HasLiveReadyEngine"/>'in kuralı). Kasıtlı her durdurmanın ilk
    /// adımı.</summary>
    private bool RetireGeneration(int? claim = null)
    {
        int next = Interlocked.Increment(ref _generation);
        return TryClaimExit(claim ?? next - 1);
    }

    private async Task ReadLoopAsync(NdjsonReader reader, int gen, TaskCompletionSource<EngineReadyEvent> ready)
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
                        ready.TrySetException(new InvalidOperationException("Engine died with a framing error.")); // startup'ta anlık sinyal [it0-devir]
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
                if (ev is EngineReadyEvent readyEvent) ready.TrySetResult(readyEvent);
                EventReceived?.Invoke(ev);
            }
        }
        catch { /* stream koptu — exit watcher bildirir */ }
    }

    /// <summary>Kasıtlı durdurmanın TEK yolu — Restart, çıkış ve uyku [idle-memory · dalga 3 · C15]:
    /// <list type="number">
    /// <item>Nesil süreç gitmeden ÖNCE ilerler ve bir nesil sahiplenilir — <paramref name="claim"/> (uyku: kontrolünün canlı bulduğu
    /// nesil) ya da giden nesil (Restart, çıkış) — (<see cref="RetireGeneration"/>): eski bekçi ve okuyucu susar, kasıtlı çıkış
    /// <see cref="EngineExited"/> ÜRETMEZ. Dönen değer bu sahiplenmenin kazanılıp kazanılmadığıdır: kaybedildiyse ölüm daha önce
    /// bildirildi (<see cref="HasLiveReadyEngine"/>'in kuralı); süreç yine toparlanır.</item>
    /// <item>Yazıcı İLK iş devralınır [fix 1 · M5]: durdurma sürerken gelen bir <see cref="SendAsync"/> ölmekte olan sürecin borusuna
    /// yazılıp kaybolmaz, bugünkü "motor çalışmıyor" hatasıyla hemen düşer; <see cref="ShutdownCommand"/> devralınan yazıcıdan gider
    /// (≤ 500 ms).</item>
    /// <item>Zarif çıkış yalnız <paramref name="gracefulExitWait"/> verilince beklenir (uyku: <see cref="SleepExitWait"/>; Restart ve
    /// çıkış beklemez, varsayılan sıfır). Bekleyiş sürecin tanıtıcısına bağlıdır, sınırı bir zaman aşımı token'ıdır (D8: sleep
    /// yok); dolarsa — ya da yazım düşerse — öldürme devralır.</item>
    /// <item><paramref name="giveUp"/> (konağın ömrü) iptal edilirse — dispose öne geçti — yazım ve bekleyiş hemen kesilir ve yol
    /// ÖLDÜRMEDEN <see cref="OperationCanceledException"/> ile çıkar: öldürmek dispose'un kendi kapanışının işidir.</item>
    /// </list>
    /// Her bekleyiş <c>ConfigureAwait(false)</c>'tır: bu yol <see cref="DisposeAsync"/>'ten de (App.OnExit → bloklu Dispatcher)
    /// çağrılır; çağıranın bağlamına post edilen bir devam orada hiç koşmaz → sync-over-async deadlock [T62 fix].</summary>
    private async Task<bool> ShutdownGracefullyAsync(TimeSpan gracefulExitWait = default, CancellationToken giveUp = default,
        int? claim = null)
    {
        bool claimed = RetireGeneration(claim);
        var writer = Interlocked.Exchange(ref _writer, null);
        var child = _child;
        try
        {
            if (writer is not null)
                await writer.WriteAsync(new ShutdownCommand()).WaitAsync(TimeSpan.FromMilliseconds(500), giveUp).ConfigureAwait(false); // graceful [it0-devir]
            if (child is not null && gracefulExitWait > TimeSpan.Zero)
            {
                using var exitWait = CancellationTokenSource.CreateLinkedTokenSource(giveUp);
                exitWait.CancelAfter(gracefulExitWait);
                await child.WaitForExitAsync(exitWait.Token).ConfigureAwait(false);
            }
        }
        catch { /* zaten ölmüş olabilir, zarif bekleyiş doldu ya da dispose öne geçti — aşağıda ayrılır */ }
        giveUp.ThrowIfCancellationRequested();
        KillCurrent();
        return claimed;
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
        finally
        {
            // [fix 1 · M4] Öldürme fırlatsa bile: borular ve süreç tanıtıcısı bırakılır, yazıcı nesli geride kalmış (sinyali
            // susturulmuş) motoru göstermez. Boruları kapanan canlı bir motor stdin'inde EOF görür ve kendisi çıkar.
            child.Dispose();
            _writer = null;
        }
    }

    /// <summary>Konağı kapatır: motoru durdurur (shutdown ≤ 500 ms, öldürme + çıkış bekleyişi ≤ <see cref="KillExitWait"/>) ve outer
    /// job'ı kapatır (<c>KILL_ON_JOB_CLOSE</c>: her koşulda süpürge).
    /// <para><b>Sıraya girmez, ÖNE GEÇER [idle-memory · dalga 3 · fix 1 · I1]:</b> kapıyı beklemeden önce konağı dispose edilmiş
    /// işaretler, nesli ilerletir ve konağın ömrünü (<see cref="_lifetime"/>) iptal eder — uçuştaki başlatma/uyanma/uyku/Restart
    /// milisaniyeler içinde vazgeçer (öldürmez; nesil ilerlediği için ölüm sinyali de yok) ve kapıyı bırakır; işlem öldürmenin İÇİNDEYSE
    /// o öldürme biter (en çok <see cref="KillExitWait"/> + ağaç taraması — yine tavanın içinde). Böylece
    /// <c>App.OnExit</c>'in bekleyişi (<see cref="Shell.AppShutdown.DisposalTimeout"/>) motor öldürülüp çıkışı beklenmeden dolmaz ve
    /// güncelleyici Supervisor'ın DLL kilitleri bırakılmadan çalışmaz. Uyuyan konakta gönderilecek komut ve öldürülecek süreç
    /// yoktur; dispose sonrasında konak uyumaz, başlatmalar <see cref="ObjectDisposedException"/> atar. İkinci çağrı zararsızdır.</para></summary>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        RetireGeneration();
        _lifetime.Cancel();
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            _asleep = false;
            await ShutdownGracefullyAsync().ConfigureAwait(false); // bkz. ShutdownGracefullyAsync notu [T62 fix]
            _outerJob.Dispose(); // KILL_ON_JOB_CLOSE: her koşulda süpürge
        }
        finally { _lifecycle.Release(); }
    }
}
