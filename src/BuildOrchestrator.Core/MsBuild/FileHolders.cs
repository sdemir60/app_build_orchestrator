using System.Text;
using BuildOrchestrator.Core.ProcessControl;

namespace BuildOrchestrator.Core.MsBuild;

/// <summary>[files-in-use] Bir dosyayı tutan süreç: PID ve image adı (<c>Acme.Host.exe</c>).</summary>
public sealed record FileHolder(int ProcessId, string Name);

/// <summary>
/// [files-in-use] Dosyaları tutan süreçler, Restart Manager ile (<c>RmGetList</c>: açık tutamaçlar ve yüklü modüller). Cevap YALNIZ
/// bilgidir — konsol satırı "held by Acme.Host.exe (PID 42)" diyebilsin diye; taşıma kararını <see cref="FileLock"/> verir. Bu yüzden
/// arama hiçbir koşulda fırlatmaz: Restart Manager'ın bir hatası, kaybolan bir süreç ya da aşılan zaman sınırı boş liste döner.
/// <para><b>Zaman sınırı:</b> tek dosyada sorgu ~100 ms ölçüldü; soğuk bir klasörde bir kez 33 sn sürdü. Sorgu bir arka plan görevinde
/// koşar ve <paramref name="timeout"/> dolunca beklenmez (görev kendi kendine biter) — koşunun yolunda en çok o kadar beklenir.</para>
/// <para>Ad, makinedeki süreçleri taramadan, PID'den okunur (<see cref="ProcessImage.NameOf"/>); okunamazsa Restart Manager'ın uygulama
/// adı kullanılır.</para>
/// </summary>
public static class FileHolders
{
    /// <summary>Koşunun yolunda bir kilit için en çok bu kadar beklenir.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    public static IReadOnlyList<FileHolder> Find(IReadOnlyList<string> paths, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0 || timeout <= TimeSpan.Zero) return [];
        var query = Task.Run(() => Query(paths));
        try
        {
            return query.Wait(timeout) ? query.Result : [];
        }
        catch (AggregateException)
        {
            return [];
        }
    }

    private static IReadOnlyList<FileHolder> Query(IReadOnlyList<string> paths)
    {
        var key = new StringBuilder(NativeMethods.CCH_RM_SESSION_KEY + 1);
        if (NativeMethods.RmStartSession(out uint session, 0, key) != 0) return [];
        try
        {
            if (NativeMethods.RmRegisterResources(session, (uint)paths.Count, [.. paths], 0, null, 0, null) != 0) return [];
            uint count = 0, reasons = 0;
            int rc = NativeMethods.RmGetList(session, out uint needed, ref count, null, ref reasons);
            if (rc == 0 || rc != NativeMethods.ERROR_MORE_DATA || needed == 0) return [];
            var infos = new NativeMethods.RM_PROCESS_INFO[needed];
            count = needed;
            if (NativeMethods.RmGetList(session, out _, ref count, infos, ref reasons) != 0) return [];

            var holders = new List<FileHolder>();
            var seen = new HashSet<int>();
            foreach (var info in infos.Take((int)Math.Min(count, (uint)infos.Length)))
            {
                int pid = info.Process.dwProcessId;
                if (!seen.Add(pid)) continue;
                string name = ProcessImage.NameOf(pid)
                    ?? (string.IsNullOrWhiteSpace(info.strAppName) ? "process " + pid : info.strAppName);
                holders.Add(new FileHolder(pid, name));
            }
            return holders;
        }
        finally
        {
            NativeMethods.RmEndSession(session);
        }
    }
}
