using BuildOrchestrator.Core.Formatting;
using Xunit;

namespace BuildOrchestrator.Tests.Formatting;

/// <summary>
/// [optimize] Boyut metni <see cref="DurationFormat"/>'ın kardeşidir: saf, statik ve tüm sayı biçimlemesi
/// InvariantCulture ile — Türkçe Windows'ta <c>1,5 MB</c> tuzağı Core'a sızamaz.
///
/// <para><b>[DEĞİŞEN KURAL]</b> Optimize'ın ilk yazımında biçimleyici birim üstünde HER ZAMAN tek ondalık
/// yazıyordu (<c>15.0 KB</c>). Bu tip ortak kaynağa terfi edilirken Clean'in bugün ekranda görünen kuralı
/// korundu: ondalık yalnız 10'un ALTINDA yazılır (<c>1.5 KB</c> ama <c>15 KB</c>) — sayı zaten üç haneliyken
/// ondalık okumaya bir şey katmaz ve terfi, çalışan bir metni sessizce değiştirmemelidir. Negatif girdinin
/// 0'a clamp'lenmesi ise Optimize'dan DEVRALINDI: eski biçimleyici <c>-5 B</c> yazardı.</para>
/// </summary>
public class ByteFormatTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(15360, "15 KB")]        // 10 ve üstü: ondalık YAZILMAZ (Clean'in korunan kuralı)
    [InlineData(1048576, "1.0 MB")]
    [InlineData(3251404, "3.1 MB")]
    [InlineData(1073741824, "1.0 GB")]
    public void Size_uses_binary_units_with_one_decimal_below_ten(long bytes, string expected)
        => Assert.Equal(expected, ByteFormat.Size(bytes));

    [Fact] // negatif girdi anlamsızdır (silinen boyutların toplamı) — 0'a clamp'lenir, çirkin bir "-1 B" üretilmez
    public void Size_clamps_negative_input_to_zero()
        => Assert.Equal("0 B", ByteFormat.Size(-5));
}
