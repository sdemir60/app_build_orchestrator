using System.IO;
using System.Text.RegularExpressions;
using BuildOrchestrator.Tests.App;

namespace BuildOrchestrator.Tests.Diagnostics;

/// <summary>
/// [idle-memory · final review (Tasks 9-10)] Süreç belleğinin TEK okuma yerinin kaynak-tarayan guard'ı: <c>PrivateMemorySize64</c>,
/// <c>GC.GetGCMemoryInfo</c> ve <c>GC.GetTotalMemory</c> yalnız <c>Core/Diagnostics/MemoryLine.cs</c>'te yazılır
/// (<c>MemoryLine.Read</c>). Motorun bellek satırı da App'in tanı satırları (About → Environment) da o okumayı kullanır; ikinci bir
/// okuma iki yüzeyin sayılarını sessizce ayrıştırır (hangi sayaç, toplamadan önce mi sonra mı, hangi birim).
///
/// <para><b>GC.GetTotalMemory'nin bir yüzü daha var:</b> <c>GC.GetTotalMemory(true)</c> bir OKUMA gibi görünür ama GİZLİ bir tam
/// toplamadır ve <c>GC.Collect</c> yazmaz — <see cref="MemoryTrimGuardTests"/> onu göremez. Bu yüzden çağrı yalnız <c>MemoryLine.cs</c>'te
/// yazılabilir VE yalnız <c>false</c> argümanıyla: başka her argüman (<c>true</c>, bir değişken) toplama istemek olabilir.</para>
///
/// <para><b>Taranan:</b> TÜM üretim projeleri (<see cref="SourceGuard.ScanSrc"/>), yorum satırları hariç. Test ağacı taranmaz: ölçüm
/// yardımcıları kendi okumalarını yazabilir, o üretim yolu değildir.</para>
///
/// <para><b>YAKALAYAMADIĞI (bilinçli sınır):</b> belleğin başka bir sayaçla okunması (ör. <c>WorkingSet64</c>, <c>PerformanceCounter</c>)
/// ya da çağrının yansımayla yapılması. Guard çağrının BİÇİMİNE bakar; yeni bir ölçünün gerekip gerekmediği review'ın işidir.</para>
/// </summary>
public sealed class MemoryReadGuardTests
{
    /// <summary>Okumanın her biçimi: ad, tam adlı ya da metot grubu olarak. <c>\b</c> ve ön-bakış, daha uzun bir adın parçasını
    /// (<c>GetGCMemoryInfoExtra</c> gibi) eşlemez.</summary>
    private static readonly Regex MemoryRead = new(
        @"(?<![A-Za-z0-9_])(?:PrivateMemorySize64|GetGCMemoryInfo|GC\.GetTotalMemory)(?![A-Za-z0-9_])", RegexOptions.Compiled);

    /// <summary><c>GC.GetTotalMemory(</c> çağrısı, <c>false</c> DIŞINDA bir argümanla ya da argümansız yazılmışsa: gizli bir tam
    /// toplama olabilir. Boşluk grubu ATOMİKTİR: geri izleme boşluğu kısaltıp <c>(  false  )</c>'u yanlışlıkla eşlemesin.</summary>
    private static readonly Regex ForcingTotalMemory = new(
        @"(?<![A-Za-z0-9_])GC\.GetTotalMemory\s*\((?>\s*)(?!false\s*\))", RegexOptions.Compiled);

    /// <summary>Okumanın MEŞRU tek yeri (src köküne göre).</summary>
    private static readonly string MemoryLineFile = Path.Combine("BuildOrchestrator.Core", "Diagnostics", "MemoryLine.cs");

    [Fact]
    public void The_process_memory_is_read_in_exactly_one_file()
    {
        var hits = SourceGuard.ScanSrc("*.cs", MemoryRead, skipCommentLines: true);
        var files = hits.Select(hit => hit[..hit.IndexOf(':')]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Hem "başka dosyada yok" hem "okuma gerçekten orada": boş bir tarama (ya da silinen/taşınan MemoryLine.cs) guard'ı
        // sessizce yeşil bırakamaz.
        Assert.True(files.Count == 1 && string.Equals(files[0], MemoryLineFile, StringComparison.OrdinalIgnoreCase),
            $"the process memory must be read only in {MemoryLineFile}; found:\n  " + string.Join("\n  ", hits));
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
    /// Guard'ın KENDİ kanıtı: her okuma biçimi (düz, tam adlı, metot grubu) satır numarasıyla raporlanır; bir yasağı ANLATAN yorum
    /// satırı ve daha uzun bir ad sayılmaz. <c>GetTotalMemory</c> kuralı <c>true</c>, değişken ve argümansız biçimi yakalar,
    /// <c>false</c>'u geçirir.
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
                private static long H() => GC.GetGCMemoryInfoExtra();
            }
            """;

        var reads = SourceGuard.ScanText("Fake.cs", fake, MemoryRead, skipCommentLines: true);
        var forcing = SourceGuard.ScanText("Fake.cs", fake, ForcingTotalMemory, skipCommentLines: true);

        Assert.Equal(7, reads.Count); // A, B, C, D, E, F, G — H daha uzun bir ad, yorum satırı sayılmaz
        Assert.StartsWith("Fake.cs:4: ", reads[0]);
        Assert.StartsWith("Fake.cs:5: ", reads[1]);
        Assert.StartsWith("Fake.cs:6: ", reads[2]);
        Assert.Equal(2, forcing.Count); // E (true) ve F (değişken); D ve G (boşluklu false) geçer
        Assert.StartsWith("Fake.cs:8: ", forcing[0]);
        Assert.StartsWith("Fake.cs:9: ", forcing[1]);
    }
}
