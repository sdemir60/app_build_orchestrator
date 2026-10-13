using System.IO;
using System.Text.RegularExpressions;
using BuildOrchestrator.Tests.App;

namespace BuildOrchestrator.Tests.Diagnostics;

/// <summary>
/// [bellek trimi] Agresif toplamanın TEK tanımının kaynak-tarayan guard'ı: <c>GC.Collect</c> yalnız
/// <c>Core/Diagnostics/MemoryTrim.cs</c>'te yazılır. App (boşta toplama) ve Supervisor (işlem sonu) aynı çağrıyı
/// <c>MemoryTrim.CollectAndRelease()</c> üzerinden kullanır; ikinci bir toplama çağrısı parametreleri (nesil, kip,
/// <c>blocking</c>, <c>compacting</c>) sessizce ayrıştırabilir. <c>GCCollectionMode.Aggressive</c> yalnız
/// <c>blocking: true</c> ile geçerlidir (<c>blocking: false</c> her çağrıda <see cref="ArgumentException"/> fırlatır) — bu
/// bilgi de tek yerde, <c>MemoryTrim</c>'in doc'unda durur.
///
/// <para><b>Taranan:</b> TÜM üretim projeleri (<see cref="SourceGuard.ScanSrc"/>), yorum satırları hariç. Test ağacı
/// taranmaz: ölçüm yardımcıları (<c>PerfMeasure</c>) kendi toplamalarını yazabilir, o üretim yolu değildir.</para>
///
/// <para><b>YAKALAYAMADIĞI (bilinçli sınır):</b> toplamanın başka bir yoldan istenmesi (ör. yansıma ile
/// <c>GC.Collect</c>'i adıyla çağırmak ya da <c>GCSettings</c> üzerinden LOH sıkıştırma kipi). Guard çağrının BİÇİMİNE
/// bakar; niyetin denetimi review'ın işidir.</para>
/// </summary>
public sealed class MemoryTrimGuardTests
{
    /// <summary>Toplama çağrısının her biçimi: <c>GC.Collect(...)</c>, <c>System.GC.Collect(...)</c> ve parantezsiz metot
    /// grubu (<c>GC.Collect</c> bir delege olarak). <c>GC.CollectionCount</c> gibi OKUMA çağrıları eşleşmez: <c>\b</c>,
    /// "Collect" ile "ionCount" arasında sınır görmez.</summary>
    private static readonly Regex GcCollect = new(@"(?<![A-Za-z0-9_])GC\.Collect\b", RegexOptions.Compiled);

    /// <summary>Çağrının MEŞRU tek yeri (src köküne göre).</summary>
    private static readonly string MemoryTrimFile = Path.Combine("BuildOrchestrator.Core", "Diagnostics", "MemoryTrim.cs");

    [Fact]
    public void GC_Collect_is_written_in_exactly_one_file()
    {
        var hits = SourceGuard.ScanSrc("*.cs", GcCollect, skipCommentLines: true);
        var files = hits.Select(hit => hit[..hit.IndexOf(':')]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Hem "başka dosyada yok" hem "tanım gerçekten orada": boş bir tarama (ya da silinen/taşınan MemoryTrim.cs)
        // guard'ı sessizce yeşil bırakamaz.
        Assert.True(files.Count == 1 && string.Equals(files[0], MemoryTrimFile, StringComparison.OrdinalIgnoreCase),
            $"GC.Collect must be written only in {MemoryTrimFile}; found:\n  " + string.Join("\n  ", hits));
    }

    /// <summary>
    /// Guard'ın KENDİ kanıtı: her çağrı biçimi (düz, tam adlı, metot grubu) satır numarasıyla raporlanır; bir yasağı
    /// ANLATAN yorum satırı ile <c>GC.CollectionCount</c> gibi okuma çağrıları sayılmaz.
    /// </summary>
    [Fact]
    public void The_rule_reports_every_call_form_and_ignores_prose_and_reads()
    {
        const string fake = """
            internal static class Fake
            {
                // GC.Collect(2) burada yalnızca ANLATILIYOR
                private static void A() => GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                private static void B() => System.GC.Collect();
                private static readonly Action C = GC.Collect;
                private static int D() => GC.CollectionCount(2);
            }
            """;

        var hits = SourceGuard.ScanText("Fake.cs", fake, GcCollect, skipCommentLines: true);

        Assert.Equal(3, hits.Count);
        Assert.StartsWith("Fake.cs:4: ", hits[0]);
        Assert.StartsWith("Fake.cs:5: ", hits[1]);
        Assert.StartsWith("Fake.cs:6: ", hits[2]);
    }
}
