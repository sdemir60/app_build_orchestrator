using System.IO;
using System.Text;
using BuildOrchestrator.Core.MsBuild;
using Xunit;

namespace BuildOrchestrator.Tests.MsBuild;

/// <summary>
/// Task 15 (It-2 devir §5 mojibake): bu toolchain'de (VS18 Enterprise, .NET Framework MSBuild.exe + Roslyn
/// csc.exe) redirected stdout/stderr UTF-8 yazılıyor — eski kod bunu sistem ANSI codepage'i (bu makinede
/// CP1254 Türkçe) sanıp mojibake üretiyordu (`başlatıldı`→`baÅŸlatÄ±ldÄ±`, `dosyası`→`dosyasÄ±`,
/// `içermiyor`→`iÃ§ermiyor`). Bu bozulma U+FFFD ÜRETMEZ (imza `Ã`/`Ä±`/`ÅŸ`/`ÄŸ` bigram'larıdır). D8: deterministic —
/// tüm bayt dizileri elle inşa edilir / Encoding.UTF8.GetBytes ile üretilir, dış kaynak yok.
/// </summary>
[Trait("Category", "MsBuild")]
public class MsBuildOutputEncodingTests
{
    [Theory]
    [InlineData("Oluşturma başlatıldı: 17.07.2026 21:20:33.")]
    [InlineData("Çıktı dosyası bulunamadı.")]
    [InlineData("Belirtilen dosya bağımsız değişken içermiyor.")]
    public void Utf8Bytes_TurkishText_DecodesToCorrectString_NotMojibake(string expected)
    {
        // Redirected pipe'ın gerçekte yazdığı baytlar: metnin UTF-8 encode'u (elle inşa — Encoding.UTF8 .NET'in
        // kendi doğrulanmış UTF-8 encoder'ı, "dış kaynak" değil, D8 ile çelişmez).
        byte[] utf8Bytes = Encoding.UTF8.GetBytes(expected);

        string decoded = MsBuildOutputEncoding.Value.GetString(utf8Bytes);

        Assert.Equal(expected, decoded);

        // Eski (ANSI/CP1254) decode ile karıştırılmadığını da netleştir: eski yol bu baytları decode etseydi
        // 'ı'/'ş'/'ğ'/'ç' iki-bayt UTF-8 dizileri "Ã"/"Ä"/"Å" gibi mojibake bigram'larına dönüşürdü.
        Assert.DoesNotContain("Ã", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("Ä±", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("ÅŸ", decoded, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CSC : error CS0006: Metadata file 'X.dll' could not be found")]
    [InlineData("MSB3027: Could not copy \"obj\\Debug\\Foo.dll\" to \"bin\\Debug\\Foo.dll\". Exceeded retry count.")]
    [InlineData("Build succeeded.")]
    public void AsciiLines_PassThroughUnchanged(string asciiLine)
    {
        // ASCII (0-127) baytları UTF-8'de ve herhangi bir ANSI codepage'de birebir aynıdır — retry/sınıflandırma
        // bu token'lara (MSB302x, "error CS") dayanır, bu yüzden byte-for-byte korunmalı.
        byte[] asciiBytes = Encoding.ASCII.GetBytes(asciiLine);

        string decoded = MsBuildOutputEncoding.Value.GetString(asciiBytes);

        Assert.Equal(asciiLine, decoded);
        Assert.Equal(asciiBytes, MsBuildOutputEncoding.Value.GetBytes(decoded));
    }

    [Fact]
    public void Utf8Bom_IsConsumed_ByStreamReader_WithValueAsFallbackEncoding()
    {
        // MsBuildInvoker.PumpLinesAsync tam olarak bu şekilde kullanıyor: StreamReader(stream, Value,
        // detectEncodingFromByteOrderMarks: true, ...). BOM'lu bir UTF-8 akışı simüle et; BOM (EF BB BF) satıra
        // sızmamalı.
        const string line = "Oluşturma başlatıldı";
        byte[] bom = [0xEF, 0xBB, 0xBF];
        byte[] body = Encoding.UTF8.GetBytes(line + "\n");
        using var stream = new MemoryStream([.. bom, .. body]);

        using var reader = new StreamReader(stream, MsBuildOutputEncoding.Value,
            detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        string? readLine = reader.ReadLine();

        // BOM (U+FEFF) satırın başına sızmadıysa readLine tam "line" ile eşleşir — sızsaydı ilk karakter
        // görünmez U+FEFF olur ve bu eşitlik başarısız olurdu.
        Assert.Equal(line, readLine);
    }

    [Fact]
    public void InvalidUtf8Bytes_DoNotThrow_ProducesReplacementCharacter()
    {
        // [D-Task15] Detect-and-ANSI-fallback UYGULANMADI (pure-UTF-8 seçildi — bkz. task-15-report.md).
        // Bu yüzden gerçekten geçersiz UTF-8 baytları (örn. CP1254'te tek-baytlık 'ı' = 0xFD, tek başına geçerli
        // bir UTF-8 lead/continuation baytı DEĞİL) exception FIRLATMAMALI (throwOnInvalidBytes:false) — aksi
        // halde MsBuildInvoker.PumpLinesAsync'in yakalamadığı bir DecoderFallbackException pump thread'ini/
        // Supervisor'ı düşürebilirdi. Bunun yerine U+FFFD (replacement char) üretir.
        byte[] genuinelyInvalidUtf8 = [0xFD];

        string decoded = MsBuildOutputEncoding.Value.GetString(genuinelyInvalidUtf8);

        Assert.Contains('�', decoded);
    }

    [Fact]
    public void Value_CodePage_IsUtf8_AndEmitsNoPreambleOnEncode()
    {
        Assert.Equal(Encoding.UTF8.CodePage, MsBuildOutputEncoding.Value.CodePage);
        Assert.Empty(MsBuildOutputEncoding.Value.GetPreamble());
    }
}
