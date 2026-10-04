using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [PERF Faz C/C1] İki büyük defter (<c>evaluation-cache.json</c>, <c>source-hash-cache.json</c>; gerçek OSYS'te birkaç
/// MB'lık JSON) dosyaya YALNIZ <c>AtomicFile</c>'ın akış varyantlarıyla dokunur: <c>OpenReadSharingDelete</c> +
/// <c>JsonSerializer.Deserialize(stream)</c>, <c>Write</c> + <c>JsonSerializer.Serialize(stream)</c>. Kural iki şeyin sessizce
/// geri gelmesini önler: (1) tüm dosyayı tek string'e ya da diziye çeviren çağrılar (<c>ReadAllText</c>, <c>ReadAllBytes</c>,
/// <c>SerializeToUtf8Bytes</c>…) dosyanın birkaç katı ara bellek üretir ve büyük nesne yığınına (LOH) her işlemde yük
/// bindirir; (2) defterin kendi <c>File.OpenRead</c>/<c>File.Create</c>/<c>File.Move</c> kopyası <c>AtomicFile</c>'ın paylaşım
/// ve retry kuralını (Delete-share'li okuma, bütçeli rename) atlar — eşzamanlı yazımda defter güncellemesi kaybolur.
///
/// <para>Kural YALNIZ bu iki dosyaya bağlıdır — küçük dosyalar (ör. <c>build-state.json</c>) string yolunu
/// kullanabilir. Yorum satırları sayılmaz: yasağı ANLATAN doküman, yasağı İHLAL eden kod değildir. <c>ReadAllBytes</c> yalnız
/// KAYNAK dosyanın özetini almak için serbesttir (<c>SHA256.HashData(File.ReadAllBytes(...))</c>): o çağrı defteri değil
/// bir kaynak dosyayı okur. Kardeş kaynak guard'ları gibi <see cref="SourceGuard"/> ile taranır.</para>
/// </summary>
public sealed class LedgerStreamingGuardTests
{
    private static readonly Regex OwnFileIo = new(
        // tüm dosyayı tek string'e/diziye çeviren okuma ve yazma
        @"\b(?:ReadAllText|WriteAllText|ReadAllLines|WriteAllLines|WriteAllBytes|ReadToEnd|SerializeToUtf8Bytes)(?:Async)?\b"
        // ReadAllBytes yalnız kaynak dosyanın özeti için serbest
        + @"|(?<!SHA256\.HashData\()\bFile\.ReadAllBytes(?:Async)?\b"
        // dosyayı kendisi açan/yaratan/taşıyan kod: bunu yalnız AtomicFile yapar
        + @"|\bFile\.(?:OpenRead|OpenWrite|Open|Create|Move)\b|\bnew\s+FileStream\b",
        RegexOptions.Compiled);

    private static readonly Regex AtomicRead = new(@"\bAtomicFile\.OpenReadSharingDelete\(", RegexOptions.Compiled);
    private static readonly Regex AtomicWrite = new(@"\bAtomicFile\.Write\(", RegexOptions.Compiled);

    [Theory]
    [InlineData("EvaluationCache.cs")]
    [InlineData("SourceHashCache.cs")]
    public void A_ledger_never_bypasses_AtomicFile_or_turns_the_file_into_one_string(string fileName)
    {
        // Vakum kapısı: tarama GERÇEKTEN o dosyayı görmeli — aksi hâlde guard sessizce yeşil kalırdı.
        Assert.Single(SourceGuard.ScannedSrcFiles(fileName));

        Assert.Empty(SourceGuard.ScanSrc(fileName, OwnFileIo, skipCommentLines: true));
    }

    [Theory]
    [InlineData("EvaluationCache.cs")]
    [InlineData("SourceHashCache.cs")]
    public void A_ledger_reads_and_writes_its_file_through_AtomicFile(string fileName)
    {
        Assert.Single(SourceGuard.ScannedSrcFiles(fileName));

        Assert.NotEmpty(SourceGuard.ScanSrc(fileName, AtomicRead, skipCommentLines: true));
        Assert.NotEmpty(SourceGuard.ScanSrc(fileName, AtomicWrite, skipCommentLines: true));
    }

    [Theory]
    [InlineData("File.WriteAllText(tmp, text);")]
    [InlineData("var d = JsonSerializer.Deserialize<X>(File.ReadAllText(path), Json);")]
    [InlineData("var d = JsonSerializer.Deserialize<X>(File.ReadAllBytes(path), Json);")]
    [InlineData("File.WriteAllBytes(tmp, bytes);")]
    [InlineData("var bytes = JsonSerializer.SerializeToUtf8Bytes(_entries, Json);")]
    [InlineData("string json = new StreamReader(stream).ReadToEnd();")]
    [InlineData("var lines = File.ReadAllLines(path);")]
    [InlineData("File.WriteAllLines(tmp, lines);")]
    [InlineData("using var stream = File.OpenWrite(tmp);")]
    [InlineData("using var stream = File.Open(path, FileMode.Open);")]
    [InlineData("var text = await File.ReadAllTextAsync(path);")]
    [InlineData("var bytes = await File.ReadAllBytesAsync(path);")]
    [InlineData("using var stream = File.OpenRead(path);")]
    [InlineData("using var stream = File.Create(tmp);")]
    [InlineData("File.Move(tmp, cachePath, overwrite: true);")]
    [InlineData("using var stream = new FileStream(path, FileMode.Open);")]
    public void The_rule_recognises_a_call_that_bypasses_AtomicFile(string line)
    {
        Assert.Single(SourceGuard.ScanText("Fake.cs", line, OwnFileIo, skipCommentLines: true));
    }

    [Theory]
    [InlineData("/// ReadAllText yerine akış kullanılır")]
    [InlineData("string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));")]
    [InlineData("using var stream = AtomicFile.OpenReadSharingDelete(path);")]
    [InlineData("AtomicFile.Write(cachePath, stream => JsonSerializer.Serialize(stream, _entries, Json), delay);")]
    [InlineData("if (!File.Exists(path)) return empty;")]
    public void The_rule_ignores_a_comment_the_source_hash_read_and_the_AtomicFile_stream_calls(string line)
    {
        Assert.Empty(SourceGuard.ScanText("Fake.cs", line, OwnFileIo, skipCommentLines: true));
    }
}
