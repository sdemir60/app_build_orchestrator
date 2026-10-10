using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [PERF Faz D / karar 10] Dosya IO paralelliğinin derecesi TEK sabitten gelir (<c>IoParallelism.Degree</c>): içerik özeti
/// ilk doldurma, iki bağlama geçişi ve çıktı kontrolleri aynı derece ile koşar. Dağınık bir <c>16</c> literal'i aynı
/// değeri beş yerde tanımlamaktır (kopya YASAK) ve biri değişince diğerleri sessizce ayrışır — guard bunu kapatır.
/// Yorum ve metin literalleri taranmaz (<see cref="SourceGuard.ScanSrcIdentifiers"/>), yalnız KOD.
/// </summary>
public class IoParallelismGuardTests
{
    private static readonly Regex LiteralDegree = new(
        @"WithDegreeOfParallelism\(\s*16\s*\)|MaxDegreeOfParallelism\s*=\s*16\b", RegexOptions.CultureInvariant);

    [Fact]
    public void No_source_file_spells_the_io_degree_as_a_literal()
    {
        Assert.Empty(SourceGuard.ScanSrcIdentifiers("*.cs", LiteralDegree));
    }

    [Fact]
    public void The_io_consumers_take_their_degree_from_the_single_constant()
    {
        // Negatif guard tek başına yetmez: literal'i yerel bir sabitle değiştirmek de kopyadır. İki tüketici de
        // sabitin KENDİSİNİ okumak zorunda; ayrıca tarama bu iki dosyayı gerçekten görüyor (boş tarama vakumu yok).
        var scanned = SourceGuard.ScannedSrcFiles("*.cs");
        foreach (string name in new[] { "SourceHashCache.cs", "IncrementalRunBinder.cs" })
        {
            string relative = Assert.Single(scanned, f => f.EndsWith(Path.Combine("Incremental", name), StringComparison.OrdinalIgnoreCase));
            string code = SourceLiterals.CodeOnly(File.ReadAllText(Path.Combine(RepoPaths.SrcRoot, relative)));

            Assert.Contains("IoParallelism.Degree", code);
        }
    }

    [Fact]
    public void The_guard_catches_code_and_ignores_comments_and_text()
    {
        const string fake = """
            // MaxDegreeOfParallelism = 16 (yorum)
            var message = "WithDegreeOfParallelism(16)";
            var a = items.AsParallel().WithDegreeOfParallelism(16);
            var b = new ParallelOptions { MaxDegreeOfParallelism = 16 };
            var ok = new ParallelOptions { MaxDegreeOfParallelism = IoParallelism.Degree };
            """;

        var offenders = SourceGuard.ScanCodeIdentifiers("fake.cs", fake, LiteralDegree);

        Assert.Equal(2, offenders.Count);
        Assert.StartsWith("fake.cs:3:", offenders[0], StringComparison.Ordinal);
        Assert.StartsWith("fake.cs:4:", offenders[1], StringComparison.Ordinal);
    }
}
