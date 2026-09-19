using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Faz 3/Task 8 · spec 2026-09-18 §1-20] Karar 20: araç ürün adına (OSYS) sert bağlı DEĞİLDİR — bir kod
/// tanımlayıcısı (tip/üye/enum değeri/parametre/lokal adı) ürün adı TAŞIMAZ. Bu guard geri sızmayı çitler
/// (canlı örnek: <c>HintPathClass.ExternalOsysPlatform</c>, <c>ClassificationReport.OsysPlatformCount</c>).
///
/// <para><b>Yalnız KOD taranır.</b> Yorumlar (bu projenin yorumları TASARIM GEREĞİ Türkçedir, CLAUDE.md) ve
/// string/char literalleri (test/demo verisi — ör. bir spike penceresindeki örnek çözüm adı) DIŞARIDADIR:
/// <see cref="SourceGuard.ScanSrcIdentifiers"/> kuralı yalnız yorum/literal STRIPLENMİŞ koda uygular
/// (regex tabanlı, Roslyn YOK — diğer guard'larla aynı hız bütçesi).</para>
/// </summary>
public class NoProductNameInCodeTests
{
    /// <summary>Tanımlayıcı sınırı içinde (case-insensitive) "osys" geçen HERHANGİ bir kelime.</summary>
    private static readonly Regex ProductNameInIdentifier = new(@"\b\w*osys\w*\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void No_identifier_in_src_carries_the_product_name()
    {
        var offenders = SourceGuard.ScanSrcIdentifiers("*.cs", ProductNameInIdentifier);

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} yerde tanımlayıcı ürün adı taşıyor (spec 2026-09-18 §1-20 — sert bağlılık YASAK):"
            + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
    }

    // ================================================================================================
    // VAKUM KARŞITI: tarama GERÇEKTEN dosya gördü mü?
    // ================================================================================================

    [Fact]
    public void The_scan_really_reads_the_src_tree()
    {
        var files = SourceGuard.ScannedSrcFiles("*.cs");
        Assert.True(files.Count >= 150, $"yalnız {files.Count} .cs dosyası tarandı — ağaç kurulmamış olabilir.");
    }

    // ================================================================================================
    // AYIRT EDİCİLİK — sahte girdi ile: guard KODU yakalıyor, yorumu/literali YOK SAYIYOR mu?
    // ================================================================================================

    [Fact]
    public void The_guard_catches_the_identifier_but_ignores_the_same_text_in_a_comment_or_a_string_literal()
    {
        const string fake = """
            // Osys bir yorumda serbesttir — bu kural yalnız kodu bağlar.
            namespace Fake;
            public class Sample
            {
                public const string Label = "OSYS.Sales.Core"; // literal veri, ihlal DEĞİL
                public enum Kind { ExternalOsysPlatform }       // <- İHLAL
            }
            """;

        var offenders = SourceGuard.ScanCodeIdentifiers("Fake.cs", fake, ProductNameInIdentifier);

        string only = Assert.Single(offenders);
        Assert.Contains("ExternalOsysPlatform", only);
        Assert.Contains("Fake.cs:6", only);
    }
}
