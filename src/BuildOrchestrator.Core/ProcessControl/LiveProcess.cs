namespace BuildOrchestrator.Core.ProcessControl;

/// <summary>
/// [kaynak şeridi · Exit · inceleme O1] Başka bir sürecin PID'ini açıp, hâlâ çalışıyorsa ondan bir şey okumanın TEK iskeleti:
/// <c>OpenProcess(QUERY_LIMITED)</c> → çıkış sorusu → okuma → <c>CloseHandle</c>. Belleği (<see cref="Diagnostics.MemoryLine.PrivateBytesOf"/>)
/// ve image adını (<see cref="ProcessImage.NameOf"/>) bu okur; makinedeki süreçler taranmaz, yalnız verilen PID açılır.
///
/// <para><b>Çıkış AYRICA sorulur</b> (<c>GetExitCodeProcess</c>): ölçüldü, çıkmış bir sürecin nesnesi bir yerde (çağıranda, bir sistem
/// bileşeninde, bir izleyicide) tutamaçla açık kaldıkça <c>OpenProcess</c> BAŞARILIR — son görünür tutamaç kapandıktan sonra bile — ve okuma
/// ESKİ bir değer verir. Çıkış kodu <c>STILL_ACTIVE</c>'ten (259) farklıysa süreç yoktur; 259 koduyla çıkmış bir süreç çalışıyor görünür —
/// kabul edilen tek sınır. Açılamayan (böyle bir süreç yok, erişim reddedildi) ya da çıkmış süreç fırlatmaz, <paramref name="gone"/> döner:
/// bir PID'i listelemek ile onu açmak arasında sürecin çıkması olağandır (çıkan bir derleyici).</para>
///
/// <para>Isınma sonrası bayt ayırmaz — çağıran statik bir lambda verdiği sürece (derleyici delegeyi önbelleğe alır).</para>
/// </summary>
public static class LiveProcess
{
    /// <summary><paramref name="processId"/> numaralı süreç açılabiliyor ve hâlâ çalışıyorsa <paramref name="read"/>'i onun tutamacıyla
    /// çağırır ve sonucunu döner; aksi hâlde <paramref name="gone"/>. Tutamaç her yolda kapanır.</summary>
    public static TResult Read<TResult>(int processId, Func<nint, TResult> read, TResult gone)
    {
        ArgumentNullException.ThrowIfNull(read);
        // PID bir DWORD'dür: eksi bir numara hiçbir sürece ait olamaz ve OpenProcess onu (ERROR_INVALID_PARAMETER ile) reddeder.
        nint handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, unchecked((uint)processId));
        if (handle == nint.Zero) return gone;
        try
        {
            if (!NativeMethods.GetExitCodeProcess(handle, out uint exitCode) || exitCode != NativeMethods.STILL_ACTIVE) return gone;
            return read(handle);
        }
        finally { NativeMethods.CloseHandle(handle); }
    }
}
