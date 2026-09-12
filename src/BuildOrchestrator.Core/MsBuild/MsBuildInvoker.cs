using System.Diagnostics;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.MsBuild;

/// <summary>Tek proje invoke isteği. It-2'de <c>BaseIntermediateOutputPath</c> HER ZAMAN null (I2-K2: in-place = default obj; obj-izolasyon It-3/worktree).</summary>
/// <param name="Target">[tek proje · design §3.8] Bu çağrının MSBuild hedefi — varsayılan
/// <see cref="MsBuildTarget.Build"/>; alanı hiç vermeyen her çağrı yeri (tam koşu, SCC turları) birebir aynı
/// komut satırını üretmeye devam eder.</param>
public sealed record MsBuildInvokeRequest(
    string ProjectId, string Configuration, string SolutionDir, bool NeedsRestore,
    string? BaseIntermediateOutputPath = null, MsBuildTarget Target = MsBuildTarget.Build);

/// <summary>
/// [optimize] Tek proje RESTORE isteği — <see cref="MsBuildInvokeRequest"/>'ten ayrı bir tiptir çünkü restore
/// yolunun Configuration'a, obj izolasyonuna ve "önce restore sonra build" sıralamasına İHTİYACI YOKTUR.
/// packages.config restore'u sln bağlamı ister [SPIKE S2-a]: <paramref name="SolutionDir"/> onu taşır.
/// </summary>
public sealed record MsBuildRestoreRequest(string ProjectId, string SolutionDir);

public sealed record MsBuildInvokeResult(int ExitCode, long DurationMs, bool TimedOut, bool Killed);

public interface IMsBuildInvoker
{
    Task<MsBuildInvokeResult> InvokeAsync(MsBuildInvokeRequest req, Action<string> onLine, CancellationToken ct);

    /// <summary>
    /// [optimize] YALNIZ restore koşar (<c>-t:restore</c>, <c>-t:Build</c> YOK) — kullanıcı-tetikli Optimize'ın
    /// "eksik NuGet paketlerini tamamla" adımı. Build yolundan ayrı bir üye olmasının sebebi, restore'un
    /// build'in bir ön adımı DEĞİL kendi başına bir iş olmasıdır: Optimize, build'in hiç dokunmadığı (skip
    /// edilen) projeleri de onarır.
    /// </summary>
    Task<MsBuildInvokeResult> RestoreAsync(MsBuildRestoreRequest req, Action<string> onLine, CancellationToken ct);
}

/// <summary>
/// T22: tek proje MSBuild.exe invoke — inner Job içinde ([§3] her child CREATE_SUSPENDED → Job.Assign → Resume,
/// bkz. JobProcessLauncher), restore-then-build sıralaması, satır-satır callback. [D10] dotnet build DEĞİL.
/// [D7] Tek ProcessRunner job-DIŞI helper'lar için — bu invoker onu KULLANMAZ, JobProcessLauncher'ı kullanır.
/// </summary>
public sealed class MsBuildInvoker(JobObject innerJob, string msbuildExePath) : IMsBuildInvoker
{
    public static readonly TimeSpan PerProjectTimeout = TimeSpan.FromMinutes(10);

    // Fix wave 2 / Finding 3: eskiden TEK "PostKillWait" adı hem başarı-yolu drain'inde (hiçbir şey
    // öldürülmedi) HEM de kill-sonrası beklemede kullanılıyordu — isim, kill OLMAYAN yolda yanıltıcıydı.
    // İkisi anlamca farklı olduğu için (ve gelecekte bağımsız ayarlanabilir olsun diye) İKİ AYRI sabite
    // bölündü; değerleri şimdilik aynı (5s), ProcessRunner ile aynı desen — kill/drain takılsa da hang yok.
    private static readonly TimeSpan DrainWait = TimeSpan.FromSeconds(5); // başarı yolu: MSBuild çıktı, pump'ları BOUNDED bekle
    private static readonly TimeSpan PostKillWait = TimeSpan.FromSeconds(5); // kill yolu: Kill() sonrası çıkış + pump beklemesi

    public async Task<MsBuildInvokeResult> InvokeAsync(MsBuildInvokeRequest req, Action<string> onLine, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        ArgumentNullException.ThrowIfNull(onLine);

        var scope = OpenScope(req.ProjectId, nameof(req), ct);
        try
        {
            // DurationMs tüm invoke'u (restore + build) kapsar — sw yalnız bir kez başlar, iki child de aynı sw'yi okur.
            // Argüman seçimi TEK kaynaktan: PlanFor hem restore ihtiyacını hem hedefi (Build/Rebuild/Clean) bilir;
            // burada elle MsBuildArguments.Build çağırmak satır menüsünün hedefini sessizce düşürürdü.
            var (restoreArgs, buildArgs) = MsBuildArguments.PlanFor(req);
            if (restoreArgs is not null)
            {
                var restoreResult = await RunChildAsync(restoreArgs, scope.WorkingDirectory, scope.Stopwatch, onLine,
                    scope.Linked.Token, scope.TimeoutOnly);
                if (restoreResult.ExitCode != 0 || restoreResult.TimedOut || restoreResult.Killed)
                    return restoreResult; // restore başarısızsa build DENENMEZ
            }

            return await RunChildAsync(buildArgs, scope.WorkingDirectory, scope.Stopwatch, onLine,
                scope.Linked.Token, scope.TimeoutOnly);
        }
        finally { scope.Dispose(); }
    }

    /// <summary>
    /// [optimize] TEK bir restore child'ı koşar — build yolunun AYNI çekirdeğiyle (inner job'a assign, satır
    /// pump'ı, bounded drain, timeout/iptalde kill). Argümanlar <see cref="MsBuildArguments.RestorePackagesConfig"/>
    /// tek kaynağından gelir; <c>-t:Build</c> HİÇ eklenmez, dolayısıyla bu yol asla derleme yapmaz.
    /// </summary>
    public async Task<MsBuildInvokeResult> RestoreAsync(MsBuildRestoreRequest req, Action<string> onLine, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        ArgumentNullException.ThrowIfNull(onLine);

        var scope = OpenScope(req.ProjectId, nameof(req), ct);
        try
        {
            var restoreArgs = MsBuildArguments.RestorePackagesConfig(req.ProjectId, req.SolutionDir);
            return await RunChildAsync(restoreArgs, scope.WorkingDirectory, scope.Stopwatch, onLine,
                scope.Linked.Token, scope.TimeoutOnly);
        }
        finally { scope.Dispose(); }
    }

    /// <summary>
    /// İki giriş noktasının ORTAK prologu: çalışma dizini, süre ölçümü ve <see cref="PerProjectTimeout"/>
    /// kurulumu. Timeout invoke BAŞINA bir kez kurulur (build yolunda restore + build toplamı) — child başına
    /// kurulsaydı "per project" adı iki katını vaat ederdi [Fix wave 1 / Finding 2]. <c>TimeoutOnly</c> SADECE
    /// zaman aşımını temsil eder; ct'den ayrı tutulur, <c>TimedOut</c>/<c>Killed</c> ayrımı buna dayanır.
    /// </summary>
    private static InvokeScope OpenScope(string projectId, string paramName, CancellationToken ct)
    {
        string workingDirectory = Path.GetDirectoryName(Path.GetFullPath(projectId))
            ?? throw new ArgumentException("ProjectId is not a valid file path.", paramName);
        var timeoutOnly = new CancellationTokenSource(PerProjectTimeout);
        return new InvokeScope(workingDirectory, Stopwatch.StartNew(), timeoutOnly,
            CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutOnly.Token));
    }

    /// <summary>Prologun ürettiği tek kullanımlık kapsam. <see cref="IDisposable"/> DEĞİLDİR ve bilinçli
    /// olarak <c>try/finally</c> ile kapatılır: <c>using</c> bir kopya üzerinde Dispose çağırırdı.</summary>
    private readonly record struct InvokeScope(
        string WorkingDirectory, Stopwatch Stopwatch, CancellationTokenSource TimeoutOnly, CancellationTokenSource Linked)
    {
        public void Dispose() { Linked.Dispose(); TimeoutOnly.Dispose(); }
    }

    private async Task<MsBuildInvokeResult> RunChildAsync(
        IReadOnlyList<string> msbuildArgs, string workingDirectory, Stopwatch sw, Action<string> onLine,
        CancellationToken timeoutToken, CancellationTokenSource timeoutOnlyCts)
    {
        string commandLine = WindowsCommandLine.Build(msbuildExePath, [.. msbuildArgs]);
        using var child = JobProcessLauncher.Launch(innerJob, commandLine,
            new LaunchOptions(RedirectStdio: true, WorkingDirectory: workingDirectory));

        // invoke-lokal kilit: stdout/stderr iki ayrı reader task'ından geliyor, onLine çağrıları serileşir.
        // Fix wave 2 / Finding 1: `detached`, aynı kilit altında bir LATCH görevi de görür — RunChildAsync
        // dönmeden HEMEN ÖNCE (aşağıdaki finally) true'ya çekilir, SafeOnLine bundan sonra onLine'ı hiç
        // ÇAĞIRMAZ. Gerekçe: WaitPumpsBoundedAsync (aşağıda) pes ettiğinde pump TASK'ları iptal EDİLMEZ —
        // yalnız beklemekten vazgeçilir (bkz. Finding 2 açıklaması) — bir sonraki satır geldiğinde abandoned
        // pump'ın ReadLineAsync'i tamamlanır ve InvokeAsync DÖNDÜKTEN SONRA onLine'ı çağırabilirdi. Task 9'da
        // onLine çağrıya karşılık gelen ProjectLogFile invoke bitince dispose edilir (Task 4 fix'i: dispose
        // sonrası AppendLine artık ObjectDisposedException fırlatır) — bu latch olmadan, thread-pool
        // thread'inde yakalayan olmayan bir exception Supervisor'ı düşürebilir.
        var onLineLock = new object();
        bool detached = false;
        void SafeOnLine(string line) { lock (onLineLock) { if (!detached) onLine(line); } }

        var stdoutTask = PumpLinesAsync(child.StandardOutput!, SafeOnLine);
        var stderrTask = PumpLinesAsync(child.StandardError!, SafeOnLine);

        try
        {
            try
            {
                int exitCode = await child.WaitForExitAsync(timeoutToken);
                // Fix wave 1 / Finding 1: başarı yolu da BOUNDED beklemeli — MSBuild.exe çıksa bile, post-build
                // <Exec>'in başlattığı bir grandchild (örn. copy-event) inherited stdout/stderr pipe uçlarının
                // bir kopyasını tutuyor olabilir (JobProcessLauncher'ın HANDLE_LIST'i yalnız BİZİM doğrudan
                // miras verdiğimiz uçları sınırlar — torunun mirasını sınırlamaz), bu durumda pipe hiç EOF
                // vermez ve eski unbounded Task.WhenAll sonsuza dek asılı kalırdı. WaitPumpsBoundedAsync zaten
                // kill-yolunda var olan yardımcı; burada da DrainWait (5s) içinde döner.
                // Fix wave 2 / Finding 2 DÜZELTMESİ: aşağıdaki `using var child`'ın Dispose'u, pump'ı
                // İPTAL/ABORT ETMEZ — yalnız TERK EDER. `child.StandardOutput`/`StandardError`
                // AnonymousPipeServerStream'dir; anonim pipe'lar overlapped OLUŞTURULAMAZ, bu yüzden
                // ReadLineAsync fiilen thread-pool thread'inde BLOKLU bir ReadFile'a düşer. SafePipeHandle
                // referans-sayımlıdır ve devam eden ReadFile bir referans TUTAR — Dispose bu yüzden
                // CloseHandle'ı ERTELER, bloklu okumayı KESMEZ. Pump ancak grandchild bir daha yazınca ya da
                // çıkınca (Finding 1) uyanır. Task 9 ölçeğinde (~178 kopya olayı/177 proje) bu, grandchild'ların
                // TÜM ömrü boyunca yüzlerce bloklu thread-pool thread'i tutabilir; pool ise yeni thread'i
                // yalnız ~1-2/sn enjekte eder — `InvokeAsync` yine de HİÇ asılmaz (kontrat korunur), ama bu
                // yorum, Task 9/13'ü ayarlayacak kişiyi yanlış yönlendirmesin diye düzeltildi.
                await WaitPumpsBoundedAsync(stdoutTask, stderrTask, DrainWait);
                return new MsBuildInvokeResult(exitCode, sw.ElapsedMilliseconds, TimedOut: false, Killed: false);
            }
            catch (OperationCanceledException)
            {
                // Fix wave 1 / Finding 3: timedOut artık ct'nin durumundan DOLAYLI çıkarılmıyor (eski:
                // !ct.IsCancellationRequested) — timeoutOnlyCts'in KENDİSİ ateşlendi mi diye DOĞRUDAN bakılır.
                // ct ve PerProjectTimeout neredeyse eşzamanlı ateşlenirse eski kod gerçek bir timeout'u
                // TimedOut:false raporlardı; bu iki kaynak birbirinden bağımsız tutulduğu için artık öyle bir
                // yanlış-atıf yok.
                bool timedOut = timeoutOnlyCts.IsCancellationRequested;
                KillChild(child.Pid);
                using var postKillCts = new CancellationTokenSource(PostKillWait);
                try { await child.WaitForExitAsync(postKillCts.Token); }
                catch (OperationCanceledException) { /* çıkış onayı gelmedi — devam, metot asılı kalmaz */ }
                await WaitPumpsBoundedAsync(stdoutTask, stderrTask, PostKillWait);
                return new MsBuildInvokeResult(-1, sw.ElapsedMilliseconds, TimedOut: timedOut, Killed: true);
            }
        }
        finally
        {
            // Fix wave 2 / Finding 1: hangi yoldan dönülürse dönülsün (başarı, kill, ya da beklenmeyen bir
            // exception) RunChildAsync'in kendisi dönmeden ÖNCE latch kapanır — abandoned pump'lar bundan
            // sonra hiçbir onLine çağrısı BAŞLATAMAZ.
            lock (onLineLock) detached = true;
        }
    }

    private static void KillChild(int pid)
    {
        try { Process.GetProcessById(pid).Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // pid zaten çıkmış olabilir (ArgumentException) ya da kill ile eşzamanlı çıkış yarışı (InvalidOperationException/Win32Exception) —
            // ProcessRunner.RunAsync ile aynı desen.
        }
    }

    private static async Task PumpLinesAsync(Stream stream, Action<string> onLine)
    {
        // Task 15: detectEncodingFromByteOrderMarks:true — MsBuildOutputEncoding.Value artık UTF-8; olası bir
        // UTF-8 BOM (EF BB BF) StreamReader tarafından yutulur, satıra sızmaz. BOM yoksa (normal durum) Value
        // olduğu gibi kullanılır — davranış değişmez.
        using var reader = new StreamReader(stream, MsBuildOutputEncoding.Value,
            detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true); // stream sahipliği child'ta (Dispose orada)
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
                onLine(line);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // kill sırasında pipe aniden kapanabilir — kısmi çıktı kabul edilir, pump sessizce biter.
        }
    }

    private static async Task WaitPumpsBoundedAsync(Task stdoutTask, Task stderrTask, TimeSpan wait)
    {
        using var cts = new CancellationTokenSource(wait);
        try { await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(cts.Token); }
        catch (OperationCanceledException) { /* pump'lar takılsa da metot asılı kalmaz */ }
    }
}
