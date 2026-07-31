using System.Diagnostics;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.MsBuild;

/// <summary>Tek proje invoke isteği. It-2'de <c>BaseIntermediateOutputPath</c> HER ZAMAN null (I2-K2: in-place = default obj; obj-izolasyon It-3/worktree).</summary>
public sealed record MsBuildInvokeRequest(
    string ProjectId, string Configuration, string SolutionDir, bool NeedsRestore, string? BaseIntermediateOutputPath = null);

public sealed record MsBuildInvokeResult(int ExitCode, long DurationMs, bool TimedOut, bool Killed);

public interface IMsBuildInvoker
{
    Task<MsBuildInvokeResult> InvokeAsync(MsBuildInvokeRequest req, Action<string> onLine, CancellationToken ct);
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

        string workingDirectory = Path.GetDirectoryName(Path.GetFullPath(req.ProjectId))
            ?? throw new ArgumentException("ProjectId is not a valid file path.", nameof(req));
        var sw = Stopwatch.StartNew();

        // Fix wave 1 / Finding 2: PerProjectTimeout invoke BAŞINA bir kez kurulur (restore + build toplamı) —
        // önceden RunChildAsync içinde per-child kuruluyordu (NeedsRestore:true → restore 10dk + build 10dk =
        // 20dk, "PerProjectTimeout" adının vaat ettiğinin iki katı). timeoutOnlyCts SADECE zaman aşımını temsil
        // eder — ct'den ayrı tutulur, Finding 3'ün ct/timeout ayrımı bu ikisinin bağımsızlığına dayanır.
        using var timeoutOnlyCts = new CancellationTokenSource(PerProjectTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutOnlyCts.Token);

        // DurationMs tüm invoke'u (restore + build) kapsar — sw yalnız bir kez başlar, iki child de aynı sw'yi okur.
        if (req.NeedsRestore)
        {
            var restoreArgs = MsBuildArguments.RestorePackagesConfig(req.ProjectId, req.SolutionDir);
            var restoreResult = await RunChildAsync(restoreArgs, workingDirectory, sw, onLine, linkedCts.Token, timeoutOnlyCts);
            if (restoreResult.ExitCode != 0 || restoreResult.TimedOut || restoreResult.Killed)
                return restoreResult; // restore başarısızsa build DENENMEZ
        }

        var buildArgs = MsBuildArguments.Build(req.ProjectId, req.Configuration, req.BaseIntermediateOutputPath);
        return await RunChildAsync(buildArgs, workingDirectory, sw, onLine, linkedCts.Token, timeoutOnlyCts);
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
