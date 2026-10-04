using System.Text.RegularExpressions;
using BuildOrchestrator.Tests.App;

namespace BuildOrchestrator.Tests.Guards;

/// <summary>
/// [PERF Faz C/C1] İki büyük defter (<c>evaluation-cache.json</c>, <c>source-hash-cache.json</c>; gerçek OSYS'te
/// birkaç MB'lık JSON) AKIŞLA okunur ve yazılır: <c>File.OpenRead</c> + <c>JsonSerializer.Deserialize(stream)</c>,
/// <c>File.Create(tmp)</c> + <c>JsonSerializer.Serialize(stream)</c>. Tüm dosyayı tek string'e çeviren çağrılar
/// (<c>ReadAllText</c>/<c>WriteAllText</c>) dosyanın İKİ katı UTF-16 ara bellek üretir ve büyük nesne yığınına
/// (LOH) her işlemde yük bindirir; geri gelmeleri motorun bellek tabanını sessizce yükseltir.
///
/// <para>Kural YALNIZ bu iki dosyaya bağlıdır — küçük dosyalar (ör. <c>build-state.json</c>) string yolunu
/// kullanabilir. Yorum satırları sayılmaz: yasağı ANLATAN doküman, yasağı İHLAL eden kod değildir.</para>
/// </summary>
public sealed class LedgerStreamingGuardTests
{
    private static readonly Regex WholeFileString = new(
        @"\b(?:ReadAllText|WriteAllText)(?:Async)?\b", RegexOptions.Compiled);

    [Theory]
    [InlineData("EvaluationCache.cs")]
    [InlineData("SourceHashCache.cs")]
    public void A_ledger_never_goes_through_a_whole_file_string(string fileName)
    {
        // Vakum kapısı: tarama GERÇEKTEN o dosyayı görmeli — aksi hâlde guard sessizce yeşil kalırdı.
        Assert.Single(SourceGuard.ScannedSrcFiles(fileName));

        Assert.Empty(SourceGuard.ScanSrc(fileName, WholeFileString, skipCommentLines: true));
    }

    [Fact]
    public void The_rule_recognises_a_whole_file_string_call_and_ignores_a_comment_that_explains_it()
    {
        Assert.Single(SourceGuard.ScanText(
            "Fake.cs", "File.WriteAllText(tmp, JsonSerializer.Serialize(_entries, Json));", WholeFileString, skipCommentLines: true));
        Assert.Single(SourceGuard.ScanText(
            "Fake.cs", "var d = JsonSerializer.Deserialize<X>(File.ReadAllText(path), Json);", WholeFileString, skipCommentLines: true));
        Assert.Empty(SourceGuard.ScanText(
            "Fake.cs", "/// ReadAllText yerine akış kullanılır", WholeFileString, skipCommentLines: true));
    }
}
