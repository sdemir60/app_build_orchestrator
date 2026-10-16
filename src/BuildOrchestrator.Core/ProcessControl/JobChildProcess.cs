using System.Diagnostics;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.ProcessControl;

/// <summary>Job'a assign edilip resume edilmiş child process'in handle'ı — Launch'ın döndürdüğü nesne.</summary>
public sealed class JobChildProcess : IDisposable
{
    private nint _processHandle;
    private bool _disposed;

    internal JobChildProcess(int pid, nint processHandle, Stream? standardInput, Stream? standardOutput, Stream? standardError)
    {
        Pid = pid;
        _processHandle = processHandle;
        StandardInput = standardInput;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    public int Pid { get; }
    public Stream? StandardInput { get; }
    public Stream? StandardOutput { get; }
    public Stream? StandardError { get; }

    public async Task<int> WaitForExitAsync(CancellationToken ct = default)
    {
        try
        {
            // Her çağrı kendi Process nesnesini kurar; bekleyiş için açtığı tanıtıcı bekleyiş biter bitmez (iptal dahil) bırakılır,
            // sonlandırıcıya kalmaz — aynı çocuk için tekrarlanan bekleyişler tanıtıcı biriktirmez.
            using var process = Process.GetProcessById(Pid);
            return await ProcessExit.WaitForExitCodeAsync(process, ct);
        }
        catch (ArgumentException)
        {
            // pid artık sistemde yok — zaten çıkmış say (spec §3).
            return NativeMethods.GetExitCodeProcess(_processHandle, out uint code) ? (int)code : 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StandardInput?.Dispose();
        StandardOutput?.Dispose();
        StandardError?.Dispose();
        if (_processHandle != nint.Zero) NativeMethods.CloseHandle(_processHandle);
        _processHandle = nint.Zero;
    }
}
