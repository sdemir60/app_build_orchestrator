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
/// <c>GC.Collect</c>'i adıyla çağırmak ya da <c>GCSettings</c> üzerinden LOH sıkıştırma kipi) ve <c>GC.GetTotalMemory(true)</c>:
/// o, bir okuma gibi görünen GİZLİ bir tam toplamadır ve <c>GC.Collect</c> yazmaz. Guard çağrının BİÇİMİNE bakar; niyetin denetimi
/// review'ın işidir. <c>GetTotalMemory</c> kapısı bellek-okuma guard'ındadır (<see cref="MemoryReadGuardTests"/>: çağrı yalnız
/// <c>MemoryLine.cs</c>'te ve yalnız <c>false</c> ile yazılır).</para>
///
/// <para><b>Test ağacı için ikinci kural:</b> testler gerçek agresif GC'yi koşturmaz. Koordinatör kuran her test trim dikişini
/// (<c>memoryTrim:</c>) açıkça verir — verilmeyen dikiş üretim varsayılanına, yani bloklayan bir GC'ye düşer ve bütün test sürecini
/// durdurur (<see cref="Every_RunCoordinator_the_tests_construct_gets_a_trim_seam"/>). "Gerçek GC yok" tanımının kendisi tek
/// yerdedir (<c>NoRealGc.Collection</c>).</para>
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
        // [idle-memory · final review 48] Tanım bir DOSYA değil bir ÇAĞRIDIR: aynı dosyaya eklenen ikinci bir GC.Collect
        // (başka nesil, başka kip) tek-dosya kuralını geçerdi ve parametreleri sessizce ayrıştırırdı.
        Assert.True(hits.Count == 1, $"GC.Collect must be written exactly once; found:\n  " + string.Join("\n  ", hits));
    }

    // ---------------------------------------------------------------- test ağacı: gerçek GC yok

    /// <summary>Koordinatörü açıkça kuran biçim: <c>new RunCoordinator(…)</c>.</summary>
    private static readonly Regex ExplicitConstruction = new(@"\bnew\s+RunCoordinator\s*\(", RegexOptions.Compiled);

    /// <summary>Hedef-tipli kurulum: tipi <c>RunCoordinator</c> olan bir üyenin/alanın/yerelin <c>=&gt; new(…)</c> ya da
    /// <c>= new(…)</c> ile kurulması — "new RunCoordinator(" metni hiç geçmeden de koordinatör kurulabilir.</summary>
    private static readonly Regex TargetTypedConstruction =
        new(@"\bRunCoordinator\s+\w+\s*(?:\([^)]*\))?\s*(?:=>|=)\s*new\s*\(", RegexOptions.Compiled);

    private static readonly Regex TrimSeamArgument = new(@"\bmemoryTrim\s*:", RegexOptions.Compiled);

    /// <summary><c>memoryTrim: null</c> dikişi verilmemiş sayılır: null üretim varsayılanına (gerçek GC) düşer.</summary>
    private static readonly Regex NullTrimSeam = new(@"\bmemoryTrim\s*:\s*null\b", RegexOptions.Compiled);

    /// <summary>
    /// [idle-memory · final review 54] Testlerde koordinatör kuran HER yer trim dikişini (<c>memoryTrim:</c>) açıkça geçirir.
    /// Dikiş verilmezse koordinatör üretim varsayılanıyla (<c>MemoryTrim.CollectAndRelease</c>, bloklayan ve sıkıştıran bir GC) kurulur
    /// ve her koşu/Sync/Clean/Optimize bitişi bütün test sürecini durdurur; süit yavaşlar, paralel zamanlama testleri bozulur. Dikişi
    /// UNUTMAK sessizdir (test yine geçer) — bu yüzden kaynakta aranır. Tek kurulum yeri artık <c>SupervisorHostHarness.NewIdleCoordinator</c>
    /// ile iki <c>Harness</c>'tır; yeni bir yer eklenirse burası onu da bağlar.
    /// </summary>
    [Fact]
    public void Every_RunCoordinator_the_tests_construct_gets_a_trim_seam()
    {
        var offenders = new List<string>();
        int constructions = 0;
        foreach (string file in RepoPaths.TestSourceFiles("*.cs"))
        {
            var (count, found) = ScanCoordinatorConstructions(Path.GetRelativePath(RepoPaths.TestsRoot, file), File.ReadAllText(file));
            constructions += count;
            offenders.AddRange(found);
        }

        // Boş bir tarama (ya da yeniden adlandırılmış tip) guard'ı sessizce yeşil bırakamaz: harness + iki Harness en az üç kurulum.
        Assert.True(constructions >= 3, $"the scan saw only {constructions} RunCoordinator constructions in the test tree");
        Assert.True(offenders.Count == 0,
            "every RunCoordinator built by a test must pass memoryTrim: (a real GC would stop the whole test process); found:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>Guard'ın KENDİ kanıtı: dikişsiz açık kurulum, dikişsiz hedef-tipli kurulum ve <c>memoryTrim: null</c> raporlanır;
    /// dikişli kurulum, yorum satırı ve string literali sayılmaz ya da ihlal olmaz.</summary>
    [Fact]
    public void The_trim_seam_rule_reports_each_unseamed_construction_and_ignores_prose()
    {
        const string fake = """
            internal static class Fake
            {
                // new RunCoordinator(planner: p) burada yalnızca ANLATILIYOR
                private const string Doc = "new RunCoordinator(planner: p)";
                private static RunCoordinator Ok() => new RunCoordinator(planner: p, console: c, memoryTrim: NoRealGc.Collection);
                private static RunCoordinator NoSeam() => new RunCoordinator(planner: (_, _) => throw new(), console: c);
                private static RunCoordinator TargetTyped(JobObject job) => new(planner: p, console: c, innerJob: job);
                private static RunCoordinator TargetTypedOk(JobObject job) => new(planner: p, console: c, memoryTrim: NoRealGc.Collection);
                private static RunCoordinator NullSeam() => new RunCoordinator(planner: p, memoryTrim: null);
            }
            """;

        var (count, offenders) = ScanCoordinatorConstructions("Fake.cs", fake);

        Assert.Equal(5, count);
        Assert.Equal(3, offenders.Count);
        Assert.StartsWith("Fake.cs:6: ", offenders[0]); // NoSeam
        Assert.StartsWith("Fake.cs:9: ", offenders[1]); // NullSeam
        Assert.StartsWith("Fake.cs:7: ", offenders[2]); // TargetTyped
    }

    /// <summary>Metindeki her koordinatör kurulumunu bulur ve dikişi (<c>memoryTrim:</c>, null değil) geçirmeyenleri raporlar.
    /// Yorum ve string literalleri taramadan ÇIKARILIR (<see cref="SourceLiterals.CodeOnly"/>: ofsetler korunur, satır numaraları
    /// gerçektir). Döner: bulunan kurulum sayısı (vakum meta-assert'i için) ve ihlaller (<c>dosya:satır: eşleşme</c>).</summary>
    private static (int Constructions, List<string> Offenders) ScanCoordinatorConstructions(string relative, string text)
    {
        string code = SourceLiterals.CodeOnly(text);
        var offenders = new List<string>();
        int constructions = 0;
        foreach (var regex in new[] { ExplicitConstruction, TargetTypedConstruction })
        {
            foreach (Match match in regex.Matches(code))
            {
                constructions++;
                string arguments = BalancedArguments(code, openParen: match.Index + match.Length - 1);
                if (TrimSeamArgument.IsMatch(arguments) && !NullTrimSeam.IsMatch(arguments)) continue;
                int line = code.AsSpan(0, match.Index).Count('\n') + 1;
                offenders.Add($"{relative}:{line}: {match.Value.Trim()}");
            }
        }
        return (constructions, offenders);
    }

    /// <summary><paramref name="openParen"/> konumundaki '(' ile eşleşen ')' arasındaki metin (yoksa sona kadar).</summary>
    private static string BalancedArguments(string code, int openParen)
    {
        int depth = 0;
        for (int i = openParen; i < code.Length; i++)
        {
            if (code[i] == '(') depth++;
            else if (code[i] == ')' && --depth == 0) return code[(openParen + 1)..i];
        }
        return code[(openParen + 1)..];
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
