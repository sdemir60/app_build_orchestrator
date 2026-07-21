using System.Globalization;
using BuildOrchestrator.Core.Formatting;

namespace BuildOrchestrator.Tests.Formatting;

/// <summary>
/// [T12/C2] <see cref="DurationFormat"/>: prototipin <c>fmtDur</c> (build-data.js:16-23) ve <c>fmtElapsed</c>
/// (BuildApp.jsx:76-80) portu. Sayı formatı <see cref="CultureInfo.InvariantCulture"/>'dır — Türkçe Windows'ta
/// <c>4,2s</c> tuzağı buraya sızmamalıdır.
/// </summary>
public class DurationFormatTests
{
    [Theory]
    [InlineData(null, "—")]
    [InlineData(4200L, "4.2s")]
    [InlineData(9949L, "9.9s")]     // 9950 esigi ALTI → ondalik
    [InlineData(250L, "0.3s")]      // [Fix wave 1/C2] yarı-yukarı yuvarlama (JS toFixed(1) paritesi) — banker's rounding "0.2s" verirdi
    [InlineData(9950L, "10s")]      // esik ve USTU → tam saniye
    [InlineData(59_000L, "59s")]
    [InlineData(72_000L, "1m 12s")]
    public void Duration_matches_the_prototype_formatter(long? ms, string expected)
        => Assert.Equal(expected, DurationFormat.Duration(ms));

    [Theory]
    [InlineData(24_000L, "24s")]
    [InlineData(72_000L, "1m 12s")]
    [InlineData(65_000L, "1m 05s")]  // saniye 2 hane pad
    public void Elapsed_matches_the_prototype_formatter(long ms, string expected)
        => Assert.Equal(expected, DurationFormat.Elapsed(ms));

    [Fact]
    public void Formatting_is_invariant_under_a_comma_decimal_culture()
    {
        var prev = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = new CultureInfo("tr-TR"); Assert.Equal("4.2s", DurationFormat.Duration(4200)); }
        finally { CultureInfo.CurrentCulture = prev; }
    }
}
