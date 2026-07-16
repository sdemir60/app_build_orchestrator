using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace BuildOrchestrator.Core.ProcessControl;

public sealed record LaunchOptions(bool RedirectStdio = false, bool Breakaway = false, string? WorkingDirectory = null);

/// <summary>
/// §3 binding launch sırası: (1) pipes → (2) CreateProcessW SUSPENDED → (3) job.Assign while SUSPENDED
/// (kaçış penceresi yok) → (4) ResumeThread + CloseHandle(hThread) → (5) parent-copy client-handle temizliği.
/// </summary>
public static class JobProcessLauncher
{
    public static JobChildProcess Launch(JobObject job, string commandLine, LaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(options);

        AnonymousPipeServerStream? stdinPipe = null;
        AnonymousPipeServerStream? stdoutPipe = null;
        AnonymousPipeServerStream? stderrPipe = null;

        var si = new NativeMethods.STARTUPINFOW { cb = Marshal.SizeOf<NativeMethods.STARTUPINFOW>() };
        bool inheritHandles = false;

        try
        {
            if (options.RedirectStdio)
            {
                stdinPipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
                stdoutPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
                stderrPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

                si.dwFlags = NativeMethods.STARTF_USESTDHANDLES;
                si.hStdInput = stdinPipe.ClientSafePipeHandle.DangerousGetHandle();
                si.hStdOutput = stdoutPipe.ClientSafePipeHandle.DangerousGetHandle();
                si.hStdError = stderrPipe.ClientSafePipeHandle.DangerousGetHandle();
                inheritHandles = true;
            }

            uint flags = NativeMethods.CREATE_SUSPENDED | NativeMethods.CREATE_NO_WINDOW | NativeMethods.CREATE_UNICODE_ENVIRONMENT;
            if (options.Breakaway) flags |= NativeMethods.CREATE_BREAKAWAY_FROM_JOB;

            // CreateProcessW lpCommandLine değiştirilebilir bir buffer OLMALI — readonly string asla geçilmez.
            var cmdLineBuffer = new StringBuilder(commandLine);
            bool created = NativeMethods.CreateProcessW(
                null, cmdLineBuffer, nint.Zero, nint.Zero, inheritHandles, flags,
                nint.Zero, options.WorkingDirectory, ref si, out var pi);

            if (!created)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                job.Assign(pi.hProcess); // process hâlâ SUSPENDED — kaçış penceresi yok
            }
            catch
            {
                NativeMethods.TerminateProcess(pi.hProcess, 1);
                NativeMethods.CloseHandle(pi.hThread);
                NativeMethods.CloseHandle(pi.hProcess);
                throw;
            }

            NativeMethods.ResumeThread(pi.hThread);
            NativeMethods.CloseHandle(pi.hThread);

            // Parent tarafındaki client-handle kopyalarını kapat (child kendi kopyasını miras aldı).
            stdinPipe?.DisposeLocalCopyOfClientHandle();
            stdoutPipe?.DisposeLocalCopyOfClientHandle();
            stderrPipe?.DisposeLocalCopyOfClientHandle();

            return new JobChildProcess((int)pi.dwProcessId, pi.hProcess, stdinPipe, stdoutPipe, stderrPipe);
        }
        catch
        {
            stdinPipe?.Dispose();
            stdoutPipe?.Dispose();
            stderrPipe?.Dispose();
            throw;
        }
    }
}
