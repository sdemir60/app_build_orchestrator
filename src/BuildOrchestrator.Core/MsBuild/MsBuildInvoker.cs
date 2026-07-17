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
    private static readonly TimeSpan PostKillWait = TimeSpan.FromSeconds(5); // ProcessRunner ile aynı desen — kill takılsa da hang yok

    public async Task<MsBuildInvokeResult> InvokeAsync(MsBuildInvokeRequest req, Action<string> onLine, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        ArgumentNullException.ThrowIfNull(onLine);

        string workingDirectory = Path.GetDirectoryName(Path.GetFullPath(req.ProjectId))
            ?? throw new ArgumentException("ProjectId geçerli bir dosya yolu değil.", nameof(req));
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
        var onLineLock = new object();
        void SafeOnLine(string line) { lock (onLineLock) onLine(line); }

        var stdoutTask = PumpLinesAsync(child.StandardOutput!, SafeOnLine);
        var stderrTask = PumpLinesAsync(child.StandardError!, SafeOnLine);

        try
        {
            int exitCode = await child.WaitForExitAsync(timeoutToken);
            // Fix wave 1 / Finding 1: başarı yolu da BOUNDED beklemeli — MSBuild.exe çıksa bile, post-build
            // <Exec>'in başlattığı bir grandchild (örn. copy-event) inherited stdout/stderr pipe uçlarının bir
            // kopyasını tutuyor olabilir (JobProcessLauncher'ın HANDLE_LIST'i yalnız BİZİM doğrudan miras
            // verdiğimiz uçları sınırlar — torunun mirasını sınırlamaz), bu durumda pipe hiç EOF vermez ve eski
            // unbounded Task.WhenAll sonsuza dek asılı kalırdı. WaitPumpsBoundedAsync zaten kill-yolunda var
            // olan yardımcı; burada da PostKillWait (5s) içinde döner. `using var child` (yukarıda) metot
            // dönünce Dispose olur, akışları kapatır — bloklu ReadLineAsync bu yüzden ObjectDisposedException/
            // IOException fırlatır ve pump görevi PumpLinesAsync'in kendi filtresiyle sessizce/hatasız biter.
            await WaitPumpsBoundedAsync(stdoutTask, stderrTask);
            return new MsBuildInvokeResult(exitCode, sw.ElapsedMilliseconds, TimedOut: false, Killed: false);
        }
        catch (OperationCanceledException)
        {
            // Fix wave 1 / Finding 3: timedOut artık ct'nin durumundan DOLAYLI çıkarılmıyor (eski:
            // !ct.IsCancellationRequested) — timeoutOnlyCts'in KENDİSİ ateşlendi mi diye DOĞRUDAN bakılır. ct ve
            // PerProjectTimeout neredeyse eşzamanlı ateşlenirse eski kod gerçek bir timeout'u TimedOut:false
            // raporlardı; bu iki kaynak birbirinden bağımsız tutulduğu için artık öyle bir yanlış-atıf yok.
            bool timedOut = timeoutOnlyCts.IsCancellationRequested;
            KillChild(child.Pid);
            using var postKillCts = new CancellationTokenSource(PostKillWait);
            try { await child.WaitForExitAsync(postKillCts.Token); }
            catch (OperationCanceledException) { /* çıkış onayı gelmedi — devam, metot asılı kalmaz */ }
            await WaitPumpsBoundedAsync(stdoutTask, stderrTask);
            return new MsBuildInvokeResult(-1, sw.ElapsedMilliseconds, TimedOut: timedOut, Killed: true);
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
        using var reader = new StreamReader(stream, MsBuildOutputEncoding.Value,
            detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true); // stream sahipliği child'ta (Dispose orada)
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

    private static async Task WaitPumpsBoundedAsync(Task stdoutTask, Task stderrTask)
    {
        using var cts = new CancellationTokenSource(PostKillWait);
        try { await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(cts.Token); }
        catch (OperationCanceledException) { /* pump'lar takılsa da metot asılı kalmaz */ }
    }
}
