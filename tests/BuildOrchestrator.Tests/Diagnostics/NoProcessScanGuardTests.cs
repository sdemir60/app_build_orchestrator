using System.IO;
using System.Text.RegularExpressions;
using BuildOrchestrator.Tests.App;

namespace BuildOrchestrator.Tests.Diagnostics;

/// <summary>
/// [kaynak şeridi · design v1.25.0 · plan Global Constraints] Üretim kodu makinedeki süreçleri TARAMAZ: <c>Process.GetProcesses()</c> ve
/// <c>Process.GetProcessesByName(...)</c> App, Core, Supervisor ve Contracts kaynaklarında yoktur. Kaynak şeridi kendi maliyetini
/// göstermeye çalışırken makineye yük olamaz: bir tarama her çağrıda bütün süreçlerin anlık görüntüsünü alır (ölçüldü:
/// <c>Process.PrivateMemorySize64</c> bile, çünkü o da aynı görüntüyü alır — çağrı başına milisaniyeler ve birkaç KB), oysa şerit yalnız
/// App'in kendi job'unu ve o job'daki süreçleri bilir (<c>JobObject.QueryProcessIds</c>) ve her birini PID'iyle tek tek açar
/// (<c>MemoryLine.PrivateBytesOf</c>).
///
/// <para><b>Taranan:</b> TÜM üretim projeleri (<see cref="SourceGuard.ScanSrc"/>), yorum satırları hariç. Aynı işi Win32 ile yapan
/// numaralandırıcılar da (<c>EnumProcesses</c>, <c>CreateToolhelp32Snapshot</c>, <c>NtQuerySystemInformation</c>) yasaktır; yoksa
/// yasak bir çağrıyı P/Invoke'a taşımak guard'ı sessizce aşardı. Test ağacı taranmaz: bir test kendi yardımcı sürecini bulmak için
/// serbesttir, o üretim yolu değildir.</para>
///
/// <para><b>YAKALAYAMADIĞI (bilinçli sınır):</b> <c>using static System.Diagnostics.Process</c> ile adsız çağrı, tür takma adı
/// (<c>using P = System.Diagnostics.Process</c>), yansıma ve WMI (<c>Win32_Process</c>). Guard çağrının BİÇİMİNE bakar; yeni bir
/// numaralandırma yolunun gerekip gerekmediği review'ın işidir.</para>
/// </summary>
public sealed class NoProcessScanGuardTests
{
    /// <summary>Tarama biçimlerinin her biri: ad, tam adlı ya da metot grubu olarak (<c>Process.GetProcesses</c> parantezsiz de eşleşir).
    /// Nokta çevresindeki boşluk atomiktir; öndeki ön-bakış <c>MyProcess.GetProcesses</c> gibi daha uzun bir sahibi, sondaki
    /// <c>GetProcessById</c> / <c>GetProcessesInfo</c> gibi başka bir adı eşlemez. <c>NtQuerySystemInformation</c> BİLEREK geniş
    /// yasaklanır: çağrının hangi bilgi sınıfını sorduğunu (süreç listesi yalnız biri) kaynak taraması güvenle ayıramaz; süreç DIŞI meşru
    /// bir gelecek kullanım çıkarsa guard'ı bilerek daraltır — o gün bu ifade o çağrıya göre değiştirilir, çağrı sessizce atlanmaz.</summary>
    private static readonly Regex MachineScan = new(
        @"(?<![A-Za-z0-9_])(?:Process\s*\.\s*GetProcesses(?:ByName)?|K32EnumProcesses|EnumProcesses|CreateToolhelp32Snapshot|NtQuerySystemInformation)(?![A-Za-z0-9_])",
        RegexOptions.Compiled);

    [Fact]
    public void No_production_code_enumerates_the_machines_processes()
    {
        var hits = SourceGuard.ScanSrc("*.cs", MachineScan, skipCommentLines: true);

        Assert.True(hits.Count == 0,
            "production code must not enumerate the machine's processes (read the job's members by id instead: "
            + "JobObject.QueryProcessIds + MemoryLine.PrivateBytesOf); found:\n  " + string.Join("\n  ", hits));
    }

    /// <summary>Tarama sessizce boş kalırsa yukarıdaki test yeşil olurdu: dört üretim projesinin de taramaya girdiği pinlenir.</summary>
    [Fact]
    public void The_guard_scans_all_four_production_projects()
    {
        var projects = SourceGuard.ScannedSrcFiles("*.cs")
            .Select(file => file.Split(Path.DirectorySeparatorChar)[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Superset(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "BuildOrchestrator.App", "BuildOrchestrator.Core", "BuildOrchestrator.Supervisor", "BuildOrchestrator.Contracts",
            },
            projects);
    }

    /// <summary>
    /// Guard'ın KENDİ kanıtı: her tarama biçimi (düz, tam adlı, metot grubu, boşluklu, native numaralandırıcılar) satır numarasıyla
    /// raporlanır; bir yasağı ANLATAN yorum satırı, tek bir süreci arayan çağrılar (<c>GetProcessById</c>, <c>GetCurrentProcess</c>)
    /// ve daha uzun adlar sayılmaz.
    /// </summary>
    [Fact]
    public void The_rule_reports_every_scan_form_and_ignores_prose_and_single_process_lookups()
    {
        const string fake = """
            internal static class Fake
            {
                // Process.GetProcesses() burada yalnızca ANLATILIYOR
                private static object A() => Process.GetProcesses();
                private static object B() => System.Diagnostics.Process.GetProcessesByName("msbuild");
                private static readonly Func<Process[]> C = Process.GetProcesses;
                private static object D() => Process . GetProcesses ();
                private static object E() => Process.GetProcessById(42);
                private static object F() => Process.GetCurrentProcess();
                private static object G() => MyProcess.GetProcesses();
                private static object H() => Process.GetProcessesInfo();
                [DllImport("psapi.dll")] private static extern bool EnumProcesses(int[] ids, int cb, out int needed);
                [DllImport("kernel32.dll")] private static extern bool K32EnumProcesses(int[] ids, int cb, out int needed);
                [DllImport("kernel32.dll")] private static extern nint CreateToolhelp32Snapshot(uint flags, uint pid);
                [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int c, nint b, int l, out int r);
            }
            """;

        var hits = SourceGuard.ScanText("Fake.cs", fake, MachineScan, skipCommentLines: true);

        Assert.Equal(8, hits.Count); // A, B, C, D, EnumProcesses, K32EnumProcesses, CreateToolhelp32Snapshot, NtQuerySystemInformation
        Assert.StartsWith("Fake.cs:4: ", hits[0]);
        Assert.StartsWith("Fake.cs:5: ", hits[1]);
        Assert.StartsWith("Fake.cs:6: ", hits[2]);
        Assert.StartsWith("Fake.cs:7: ", hits[3]);
        Assert.StartsWith("Fake.cs:12: ", hits[4]);
        Assert.StartsWith("Fake.cs:13: ", hits[5]);
        Assert.StartsWith("Fake.cs:14: ", hits[6]);
        Assert.StartsWith("Fake.cs:15: ", hits[7]);
    }
}
