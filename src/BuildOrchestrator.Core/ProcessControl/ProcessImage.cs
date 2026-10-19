using System.IO;

namespace BuildOrchestrator.Core.ProcessControl;

/// <summary>
/// [Exit] Bir PID'in image dosya adı (<c>MSBuild.exe</c>) — onay kartının "ENDS ON EXIT" listesi outer job'un üyelerini bununla
/// adlandırır. Makine çapında süreç taraması YOKTUR (<c>Process.GetProcesses()</c> yasak — <c>NoProcessScanGuardTests</c>): PID tek tek
/// <c>OpenProcess(QUERY_LIMITED)</c> ile açılır, <c>QueryFullProcessImageNameW</c> ile okunur — <see cref="Diagnostics.MemoryLine.PrivateBytesOf"/>'un
/// deseni. <c>System.Diagnostics.Process.ProcessName</c> kullanılmaz: o makinedeki tüm süreçlerin anlık görüntüsünü alır ve uzantıyı atar.
/// </summary>
public static class ProcessImage
{
    /// <summary>Win32 yollarının olağan üst sınırı (uzun yol desteğinde 32K'ya çıkabilir; image yolları bunun içinde kalır).</summary>
    private const int PathCapacity = 1024;

    /// <summary>
    /// <paramref name="processId"/> numaralı sürecin image dosya adı (yolsuz, uzantılı: <c>MSBuild.exe</c>); süreç çıktıysa ya da
    /// açılamıyorsa <c>null</c>. Tutamacı başka biri (başlatıcı, bir izleyici) açık tuttukça çıkmış bir sürecin <c>OpenProcess</c>'i de
    /// başarır — bu yüzden çıkış kodu ayrıca sorulur, <c>MemoryLine</c>'ın kuralı.
    /// </summary>
    public static string? NameOf(int processId)
    {
        // PID bir DWORD'dür: eksi bir numara hiçbir sürece ait olamaz ve OpenProcess onu (ERROR_INVALID_PARAMETER ile) reddeder.
        nint handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, unchecked((uint)processId));
        if (handle == nint.Zero) return null;
        try
        {
            if (!NativeMethods.GetExitCodeProcess(handle, out uint exitCode) || exitCode != NativeMethods.STILL_ACTIVE) return null;
            var buffer = new char[PathCapacity];
            uint size = (uint)buffer.Length;
            if (!NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size)) return null;
            return Path.GetFileName(new string(buffer, 0, (int)size));
        }
        finally { NativeMethods.CloseHandle(handle); }
    }
}
