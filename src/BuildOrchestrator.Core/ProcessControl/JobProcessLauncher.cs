using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace BuildOrchestrator.Core.ProcessControl;

public sealed record LaunchOptions(bool RedirectStdio = false, bool Breakaway = false, string? WorkingDirectory = null);

/// <summary>
/// §3 binding launch sırası: (1) pipes → (2) CreateProcessW SUSPENDED → (3) job.Assign while SUSPENDED
/// (kaçış penceresi yok) → (4) ResumeThread + CloseHandle(hThread) → (5) parent-copy client-handle temizliği.
/// [It-2 giriş kriteri] Redirected launch'ta miras YALNIZ PROC_THREAD_ATTRIBUTE_HANDLE_LIST'teki 3 pipe ucuyla
/// sınırlıdır: bInheritHandles=true aksi halde parent'taki TÜM inheritable handle'ları verirdi ve paralel
/// launch'ta kardeş pipe uçları çapraz sızıp EOF'u engellerdi (deadlock).
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
        ProcThreadAttributeList? attributes = null;

        var six = new NativeMethods.STARTUPINFOEXW();
        six.StartupInfo.cb = Marshal.SizeOf<NativeMethods.STARTUPINFOEXW>();
        bool inheritHandles = false;

        try
        {
            uint flags = NativeMethods.CREATE_SUSPENDED | NativeMethods.CREATE_NO_WINDOW | NativeMethods.CREATE_UNICODE_ENVIRONMENT;
            if (options.Breakaway) flags |= NativeMethods.CREATE_BREAKAWAY_FROM_JOB;

            if (options.RedirectStdio)
            {
                stdinPipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
                stdoutPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
                stderrPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

                six.StartupInfo.dwFlags = NativeMethods.STARTF_USESTDHANDLES;
                six.StartupInfo.hStdInput = stdinPipe.ClientSafePipeHandle.DangerousGetHandle();
                six.StartupInfo.hStdOutput = stdoutPipe.ClientSafePipeHandle.DangerousGetHandle();
                six.StartupInfo.hStdError = stderrPipe.ClientSafePipeHandle.DangerousGetHandle();
                inheritHandles = true;

                // 3 uç birbirinden farklı (ayrı pipe'lar) — HANDLE_LIST tekrar kabul etmez.
                attributes = new ProcThreadAttributeList(
                    [six.StartupInfo.hStdInput, six.StartupInfo.hStdOutput, six.StartupInfo.hStdError]);
                six.lpAttributeList = attributes.Handle;
                flags |= NativeMethods.EXTENDED_STARTUPINFO_PRESENT;
            }

            // CreateProcessW lpCommandLine değiştirilebilir bir buffer OLMALI — readonly string asla geçilmez.
            var cmdLineBuffer = new StringBuilder(commandLine);
            bool created = NativeMethods.CreateProcessW(
                null, cmdLineBuffer, nint.Zero, nint.Zero, inheritHandles, flags,
                nint.Zero, options.WorkingDirectory, ref six, out var pi);

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
        finally
        {
            attributes?.Dispose(); // CreateProcess döndü — buffer'lar artık serbest
        }
    }
}
