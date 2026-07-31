using System.Diagnostics;
using System.Text;

namespace BuildOrchestrator.Core.Processes;

public sealed record ProcessSpec(string FileName, IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null, TimeSpan? Timeout = null);

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Elapsed, bool TimedOut)
{
    public bool Success => !TimedOut && ExitCode == 0;
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default);
}

public sealed class ProcessRunner : IProcessRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10); // [D7] timeout ZORUNLU — null spec bile tavana çarpar
    public static readonly TimeSpan PostKillWait = TimeSpan.FromSeconds(5); // [it0-devir] kill takılırsa çıktı okuması da bu tavana çarpar

    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = spec.FileName,
            WorkingDirectory = spec.WorkingDirectory ?? string.Empty,
            // [Task 19] stdin de yönlendirilir ve HEMEN kapatılır: aksi halde child, EBEVEYNİN stdin'ini
            // (Supervisor'da bu, App'ten gelen NDJSON PIPE'ı) miras alır — git.exe gibi bir konsol child'ı o
            // pipe'ta EOF beklerken sonsuza dek asılı kalır (conhost tahsis edip bloklar). Kapalı stdin → anında
            // EOF; git/vswhere stdin OKUMAZ, davranış değişmez. Bu runner yalnız git + vswhere spawn eder.
            RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in spec.Arguments) psi.ArgumentList.Add(a); // elle string birleştirme YASAK (quoting tuzağı)

        var sw = Stopwatch.StartNew();
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"The process could not be started: {spec.FileName}");
        process.StandardInput.Close(); // child'a anında EOF ver — asla stdin'de bloklamasın
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(spec.Timeout ?? DefaultTimeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { /* zaten öldü / erişim */ }
            // post-kill exit-wait VE çıktı okuması BOUNDED — kill takılsa da metod asılı kalmaz [it0-devir]
            using var postKillCts = new CancellationTokenSource(PostKillWait);
            try { await process.WaitForExitAsync(postKillCts.Token); } catch (OperationCanceledException) { /* çıkış onayı gelmedi */ }
            if (ct.IsCancellationRequested) throw; // caller iptali: child öldürüldü, iptal yay
            string outText = await ReadBoundedAsync(stdoutTask, postKillCts.Token);
            string errText = await ReadBoundedAsync(stderrTask, postKillCts.Token);
            return new ProcessResult(-1, outText, errText, sw.Elapsed, TimedOut: true);
        }
        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask, sw.Elapsed, TimedOut: false);
    }

    private static async Task<string> ReadBoundedAsync(Task<string> read, CancellationToken ct)
    {
        try { return await read.WaitAsync(ct); }
        catch (OperationCanceledException) { return string.Empty; } // kill takıldı → kısmi/boş çıktı, hang yok
    }
}
