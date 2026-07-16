using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BuildOrchestrator.Core.ProcessControl;

/// <summary>Job Object'e bağlı IOCP bildirimi. dwNumberOfBytes = mesaj kodu, lpOverlapped = pid (spike ile birebir eşleme).</summary>
public readonly record struct JobNotification(uint MessageId, int Pid);

public sealed class JobCompletionPort : IDisposable
{
    private nint _handle;
    private bool _disposed;

    internal JobCompletionPort(nint handle) => _handle = handle;

    /// <summary>Sonraki job bildirimini bekler. null = timeout (sleep-poll yok — tamamen IOCP bloklu bekleme).</summary>
    public JobNotification? WaitNext(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint ms = timeout == Timeout.InfiniteTimeSpan ? NativeMethods.INFINITE : checked((uint)timeout.TotalMilliseconds);

        bool ok = NativeMethods.GetQueuedCompletionStatus(_handle, out uint messageId, out _, out nint overlapped, ms);
        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            if (err == NativeMethods.WAIT_TIMEOUT) return null;
            throw new Win32Exception(err);
        }

        // lpOverlapped alanı, job completion bildirimlerinde pointer değil pid taşır (spike'taki eşleme).
        int pid = unchecked((int)(uint)overlapped.ToInt64());
        return new JobNotification(messageId, pid);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NativeMethods.CloseHandle(_handle);
        _handle = nint.Zero;
    }
}
