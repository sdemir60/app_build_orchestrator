using System.IO;
using System.Text.RegularExpressions;
using BuildOrchestrator.Tests.App;

namespace BuildOrchestrator.Tests.Diagnostics;

/// <summary>
/// [idle-memory · final review (Tasks 9-10) · kaynak şeridi R1] Süreç belleğinin TEK okuma yerinin kaynak-tarayan guard'ı:
/// <c>GetProcessMemoryInfo</c> (özel baytlar), <c>GC.GetGCMemoryInfo</c> ve <c>GC.GetTotalMemory</c> çağrıları yalnız
/// <c>Core/Diagnostics/MemoryLine.cs</c>'te yazılır (<c>MemoryLine.Read</c>, <c>PrivateBytesOf</c>, <c>CurrentPrivateBytes</c>). Motorun
/// bellek satırı, App'in tanı satırları (About'un kopyalanan tanı metni) ve kaynak şeridi o okumayı kullanır; ikinci bir okuma yüzeylerin sayılarını
/// sessizce ayrıştırır (hangi sayaç, toplamadan önce mi sonra mı, hangi birim).
///
/// <para><b>Değişen kural (ölçüm, 2026-10-17):</b> eski iddia "özel baytlar <c>Process.PrivateMemorySize64</c> ile okunur, tek yeri
/// <c>MemoryLine.Read</c>'dir" idi ve guard o adı tarıyordu. Kaynak şeridi App + motor + derleyicilerin özel baytlarını 3 sn'de bir okur ve
/// makine çapında süreç taraması yasaktır (<see cref="NoProcessScanGuardTests"/>). Ölçüldü: <c>PrivateMemorySize64</c> HER çağrıda
/// makinedeki tüm süreçlerin anlık görüntüsünü alır (çağrı başına milisaniyeler ve birkaç KB); <c>GetProcessMemoryInfo(...).PrivateUsage</c>
/// mikrosaniyeler sürer, bayt ayırmaz ve AYNI ölçüdür (başkasının sürecinde art arda okumada eşit; kendi sürecimizde eski sayaç kendi
/// anlık görüntü tamponunu da sayar — bu makinede 1 MiB + 8 KiB fazla, bkz. <c>JobResourceQueryTests</c>). Bu yüzden özel baytların TEK
/// yolu <c>GetProcessMemoryInfo</c>'dur ve <c>PrivateMemorySize64</c> üretim ağacında HİÇBİR yerde yoktur — <c>MemoryLine</c> içinde bile.
/// Kural değişince bu guard yeni kuralı pinler (eski adı yasaklar), eski testi gevşetmez.</para>
///
/// <para><b>Bildirim dosyası muaftır:</b> <c>GetProcessMemoryInfo</c>'un P/Invoke bildirimi Core'un TEK P/Invoke dosyasında
/// (<c>ProcessControl/NativeMethods.cs</c>) durur ve bildirim bir okuma değildir; ÇAĞRI yalnız <c>MemoryLine.cs</c>'tedir. Muafiyet yalnız bu
/// kurala uygulanır ve BOŞA DÜŞMEZ: dosya bildirimi hâlâ taşıyor mu ayrıca pinlenir.</para>
///
/// <para><b>GC.GetTotalMemory'nin bir yüzü daha var:</b> <c>GC.GetTotalMemory(true)</c> bir OKUMA gibi görünür ama GİZLİ bir tam
/// toplamadır ve <c>GC.Collect</c> yazmaz — <see cref="MemoryTrimGuardTests"/> onu göremez. Bu yüzden çağrı yalnız <c>MemoryLine.cs</c>'te
/// yazılabilir VE yalnız <c>false</c> argümanıyla: başka her argüman (<c>true</c>, bir değişken) toplama istemek olabilir.</para>
///
/// <para><b>Taranan:</b> TÜM üretim projeleri (<see cref="SourceGuard.ScanSrc"/>), yorum satırları hariç. Test ağacı taranmaz: ölçüm
/// yardımcıları ve karşılaştırma testleri (ör. eski sayaçla aynı ölçü olduğunu gösteren) kendi okumalarını yazabilir, o üretim yolu değildir.</para>
///
/// <para><b>YAKALAYAMADIĞI (bilinçli sınır):</b> belleğin başka bir sayaçla okunması (ör. <c>WorkingSet64</c>, <c>PerformanceCounter</c>)
/// ya da çağrının yansımayla yapılması. Guard çağrının BİÇİMİNE bakar; yeni bir ölçünün gerekip gerekmediği review'ın işidir.</para>
/// </summary>
public sealed class MemoryReadGuardTests
{
    /// <summary>Yönetilen bellek okumasının her biçimi: ad, tam adlı ya da metot grubu olarak. <c>\b</c> ve ön-bakış, daha uzun bir adın
    /// parçasını (<c>GetGCMemoryInfoExtra</c> gibi) eşlemez.</summary>
    private static readonly Regex MemoryRead = new(
        @"(?<![A-Za-z0-9_])(?:GetGCMemoryInfo|GC\.GetTotalMemory)(?![A-Za-z0-9_])", RegexOptions.Compiled);

    /// <summary>Özel baytların TEK okuma yolu: <c>GetProcessMemoryInfo</c>, Win32'nin <c>K32</c> adıyla da (kernel32'nin dışa aktardığı ad).
    /// Ad, tam adlı ya da metot grubu olarak eşlenir; <c>GetProcessMemoryInfoEx</c> gibi daha uzun bir ad eşlenmez.</summary>
    private static readonly Regex ProcessMemoryInfo = new(
        @"(?<![A-Za-z0-9_])(?:K32)?GetProcessMemoryInfo(?![A-Za-z0-9_])", RegexOptions.Compiled);

    /// <summary>ESKİ okuma yolu: <c>Process.PrivateMemorySize64</c> makinedeki tüm süreçlerin anlık görüntüsünü alır — üretimde yoktur.</summary>
    private static readonly Regex SnapshotCounter = new(
        @"(?<![A-Za-z0-9_])PrivateMemorySize64(?![A-Za-z0-9_])", RegexOptions.Compiled);

    /// <summary><c>GC.GetTotalMemory(</c> çağrısı, <c>false</c> DIŞINDA bir argümanla ya da argümansız yazılmışsa: gizli bir tam
    /// toplama olabilir. Boşluk grubu ATOMİKTİR: geri izleme boşluğu kısaltıp <c>(  false  )</c>'u yanlışlıkla eşlemesin.</summary>
    private static readonly Regex ForcingTotalMemory = new(
        @"(?<![A-Za-z0-9_])GC\.GetTotalMemory\s*\((?>\s*)(?!false\s*\))", RegexOptions.Compiled);

    /// <summary>Okumanın MEŞRU tek yeri (src köküne göre).</summary>
    private static readonly string MemoryLineFile = Path.Combine("BuildOrchestrator.Core", "Diagnostics", "MemoryLine.cs");

    /// <summary><c>GetProcessMemoryInfo</c>'un P/Invoke BİLDİRİM dosyası (src köküne göre) — çağrı değil bildirim taşır, muaftır.</summary>
    private static readonly string NativeMethodsFile = Path.Combine("BuildOrchestrator.Core", "ProcessControl", "NativeMethods.cs");

    [Fact]
    public void The_process_memory_is_read_in_exactly_one_file()
    {
        var hits = SourceGuard.ScanSrc("*.cs", MemoryRead, skipCommentLines: true)
            .Concat(SourceGuard.ScanSrc("*.cs", ProcessMemoryInfo, [NativeMethodsFile], skipCommentLines: true))
            .ToList();
        var files = hits.Select(hit => hit[..hit.IndexOf(':')]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Hem "başka dosyada yok" hem "okuma gerçekten orada": boş bir tarama (ya da silinen/taşınan MemoryLine.cs) guard'ı
        // sessizce yeşil bırakamaz.
        Assert.True(files.Count == 1 && string.Equals(files[0], MemoryLineFile, StringComparison.OrdinalIgnoreCase),
            $"the process memory must be read only in {MemoryLineFile}; found:\n  " + string.Join("\n  ", hits));
    }

    /// <summary>Eski sayaç hiçbir yerde yok: <c>MemoryLine.cs</c> dahil. Yeni okuma aynı sayıyı verir ve makineyi taramaz (sınıf özeti).</summary>
    [Fact]
    public void The_machine_wide_private_bytes_counter_is_read_nowhere()
    {
        var hits = SourceGuard.ScanSrc("*.cs", SnapshotCounter, skipCommentLines: true);

        Assert.True(hits.Count == 0,
            "Process.PrivateMemorySize64 snapshots every process on the machine; read private bytes with MemoryLine.PrivateBytesOf / "
            + "CurrentPrivateBytes (GetProcessMemoryInfo) instead; found:\n  " + string.Join("\n  ", hits));
    }

    /// <summary>Muafiyet BOŞA DÜŞMESİN: bildirim dosyası hâlâ <c>GetProcessMemoryInfo</c>'u bildiriyor. Bildirim taşınır ya da silinirse
    /// yukarıdaki muafiyet sessizce ölü bir satıra dönerdi.</summary>
    [Fact]
    public void The_exempt_declaration_file_really_declares_the_call()
    {
        string path = Path.Combine(RepoPaths.SrcRoot, NativeMethodsFile);
        Assert.True(File.Exists(path), "the P/Invoke declaration file moved — the guard's exemption is stale");

        var declarations = SourceGuard.ScanText(NativeMethodsFile, File.ReadAllText(path), ProcessMemoryInfo, skipCommentLines: true);

        Assert.NotEmpty(declarations);
    }

    /// <summary>GC.GetTotalMemory yalnız <c>false</c> ile çağrılır: okuma, toplama istemez.</summary>
    [Fact]
    public void GC_GetTotalMemory_is_never_asked_to_collect()
    {
        var hits = SourceGuard.ScanSrc("*.cs", ForcingTotalMemory, skipCommentLines: true);

        Assert.True(hits.Count == 0,
            "GC.GetTotalMemory must be called with the literal false (anything else may force a full collection); found:\n  "
            + string.Join("\n  ", hits));
    }

    /// <summary>
    /// Guard'ın KENDİ kanıtı: her okuma biçimi (düz, tam adlı, metot grubu, <c>K32</c> adı) satır numarasıyla raporlanır; bir yasağı
    /// ANLATAN yorum satırı ve daha uzun bir ad sayılmaz. <c>GetTotalMemory</c> kuralı <c>true</c>, değişken ve argümansız biçimi yakalar,
    /// <c>false</c>'u geçirir. Eski sayaç kuralı yalnız <c>PrivateMemorySize64</c>'ü yakalar.
    /// </summary>
    [Fact]
    public void The_rules_report_every_read_form_and_every_forcing_call_and_ignore_prose()
    {
        const string fake = """
            internal static class Fake
            {
                // GC.GetTotalMemory(true) burada yalnızca ANLATILIYOR
                private static long A() => Process.GetCurrentProcess().PrivateMemorySize64;
                private static object B() => System.GC.GetGCMemoryInfo();
                private static readonly Func<bool, long> C = GC.GetTotalMemory;
                private static long D() => GC.GetTotalMemory(false);
                private static long E() => GC.GetTotalMemory(true);
                private static long F(bool force) => GC.GetTotalMemory( force );
                private static long G() => GC.GetTotalMemory(  false  );
                private static object H() => GC.GetGCMemoryInfoExtra();
                private static bool I(nint p) => NativeMethods.GetProcessMemoryInfo(p, ref counters, size);
                private static bool J(nint p) => K32GetProcessMemoryInfo(p, ref counters, size);
                private static readonly Func<nint, bool> K = NativeMethods.GetProcessMemoryInfo;
                private static bool L(nint p) => GetProcessMemoryInfoEx(p);
            }
            """;

        var reads = SourceGuard.ScanText("Fake.cs", fake, MemoryRead, skipCommentLines: true);
        var processReads = SourceGuard.ScanText("Fake.cs", fake, ProcessMemoryInfo, skipCommentLines: true);
        var snapshots = SourceGuard.ScanText("Fake.cs", fake, SnapshotCounter, skipCommentLines: true);
        var forcing = SourceGuard.ScanText("Fake.cs", fake, ForcingTotalMemory, skipCommentLines: true);

        Assert.Equal(6, reads.Count); // B, C, D, E, F, G — H daha uzun bir ad, yorum satırı sayılmaz, A ve I-L başka kuralların
        Assert.StartsWith("Fake.cs:5: ", reads[0]);
        Assert.StartsWith("Fake.cs:6: ", reads[1]);
        Assert.StartsWith("Fake.cs:7: ", reads[2]);
        Assert.Equal(3, processReads.Count); // I (tam adlı), J (K32 adı), K (metot grubu) — L daha uzun bir ad
        Assert.StartsWith("Fake.cs:12: ", processReads[0]);
        Assert.StartsWith("Fake.cs:13: ", processReads[1]);
        Assert.StartsWith("Fake.cs:14: ", processReads[2]);
        Assert.Single(snapshots); // yalnız A
        Assert.StartsWith("Fake.cs:4: ", snapshots[0]);
        Assert.Equal(2, forcing.Count); // E (true) ve F (değişken); D ve G (boşluklu false) geçer
        Assert.StartsWith("Fake.cs:8: ", forcing[0]);
        Assert.StartsWith("Fake.cs:9: ", forcing[1]);
    }
}
