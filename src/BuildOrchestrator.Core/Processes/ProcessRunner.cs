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

    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = spec.FileName,
            WorkingDirectory = spec.WorkingDirectory ?? string.Empty,
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in spec.Arguments) psi.ArgumentList.Add(a); // elle string birleştirme YASAK (quoting tuzağı)

        var sw = Stopwatch.StartNew();
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Process başlatılamadı: {spec.FileName}");
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
            // post-kill wait BOUNDED — kill takılsa da sonsuz beklemeyiz [it0-devir]
            try { await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token); }
            catch (OperationCanceledException) { /* çıkış onayı gelmedi; yine de sonucu döneriz */ }
            if (ct.IsCancellationRequested) throw; // caller iptali: child öldürüldü, iptal yay
            return new ProcessResult(-1, await stdoutTask, await stderrTask, sw.Elapsed, TimedOut: true);
        }
        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask, sw.Elapsed, TimedOut: false);
    }
}
