using System.Runtime.InteropServices;

namespace BuildOrchestrator.Core.ProcessControl;

/// <summary>
/// [PERF Faz D / karar 10] Makinenin, işçi bütçesini (<see cref="WorkerBudget"/>) belirleyen İKİ ölçüsü. Kural
/// <see cref="WorkerBudget"/>'tadır; burası yalnız OKUR — böylece kural donanımdan bağımsız test edilir ve Supervisor
/// koşu başında makineyi tek çağrıyla (<see cref="Snapshot"/>) görür. Boş belleğin Win32 okuması
/// (<c>GlobalMemoryStatusEx</c>) Core'daki TEK P/Invoke yerindedir: <c>NativeMethods</c>.
/// </summary>
public static class MachineResources
{
    /// <summary><see cref="FreePhysicalBytes"/> okunamadığında dönen "bilinmiyor" işareti: bellek kuralı hiçbir işçiyi
    /// kırpmasın (bilinmeyen bir şeye dayanarak kullanıcının profilini kısmak yanlış olurdu).</summary>
    public const long FreeBytesUnknown = long.MaxValue;

    /// <summary>Mantıksal işlemci sayısı (en az 1). <see cref="Environment.ProcessorCount"/> sürecin yakınlık maskesine
    /// uyar — kısıtlı bir makine (ya da yakınlık maskesiyle sınırlanmış bir koşu) kısıtlı sayıyı görür.</summary>
    public static int LogicalCores => Math.Max(1, Environment.ProcessorCount);

    /// <summary>Boş fiziksel bellek (bayt): yeni bir sürecin disk yerine RAM'den başlayabileceği, hemen kullanılabilir
    /// kısım (<c>ullAvailPhys</c> — beklemedeki önbellek sayfaları dahil). Okuma başarısızsa
    /// <see cref="FreeBytesUnknown"/>.</summary>
    public static long FreePhysicalBytes()
    {
        var status = new NativeMethods.MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>() };
        return NativeMethods.GlobalMemoryStatusEx(ref status)
            ? (long)Math.Min(status.ullAvailPhys, (ulong)long.MaxValue)
            : FreeBytesUnknown;
    }

    /// <summary>Koşu başında bir kez okunan görüntü — <see cref="WorkerBudget.Clamp"/>'in girdisi.</summary>
    public static (int Cores, long FreeBytes) Snapshot() => (LogicalCores, FreePhysicalBytes());
}
