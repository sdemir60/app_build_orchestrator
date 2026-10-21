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
    /// açılamıyorsa <c>null</c>. Açma ve çıkış sorusu <see cref="LiveProcess.Read"/>'tedir (bellek okumasıyla ortak iskelet).
    /// </summary>
    public static string? NameOf(int processId) =>
        LiveProcess.Read(processId, static handle => ImageNameOf(handle), gone: (string?)null);

    /// <summary>Verilen tutamaçtaki sürecin image dosya adı; okunamazsa <c>null</c>.</summary>
    private static string? ImageNameOf(nint processHandle)
    {
        var buffer = new char[PathCapacity];
        uint size = (uint)buffer.Length;
        return NativeMethods.QueryFullProcessImageName(processHandle, 0, buffer, ref size)
            ? Path.GetFileName(new string(buffer, 0, (int)size))
            : null;
    }
}
