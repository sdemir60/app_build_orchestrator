using BuildOrchestrator.Core.ProcessControl;
using Xunit;

namespace BuildOrchestrator.Tests.ProcessControl;

/// <summary>
/// [T20-a] K11 perf tablosu: Full(6, cap yok, Normal) · Balanced(4, %70, BelowNormal) · Light(2, %40, Idle).
/// <see cref="PerfProfile"/> SAF'tır — Win32/WPF türü taşımaz, priority kendi enum'uyla
/// (<see cref="ProcessPriorityClassKind"/>) ifade edilir; bu sınıf da saftır (App/Win32 bağımlılığı YOK).
/// <para>Buradaki assert'ler yalnız ENUM değerini pinler; enum→Win32 sabit çevirisi
/// <c>JobCpuRateTests.Priority_write_maps_to_the_win32_class_...</c>'ta, App ile paralellik eşitliği ise
/// <c>App/PerfProfileParityTests</c>'te doğrulanır.</para>
/// </summary>
[Trait("Category", "ProcessControl")]
public class PerfProfileTests
{
    [Fact]
    public void Perf_table_matches_the_K11_decision_for_all_three_modes()
    {
        Assert.Equal(new PerfProfile(6, null, ProcessPriorityClassKind.Normal), PerfProfile.For(PerfMode.Full));
        Assert.Equal(new PerfProfile(4, 70, ProcessPriorityClassKind.BelowNormal), PerfProfile.For(PerfMode.Balanced));
        Assert.Equal(new PerfProfile(2, 40, ProcessPriorityClassKind.Idle), PerfProfile.For(PerfMode.Light));
    }

    /// <summary>
    /// [T20-b/P3] Copy fazı tabanının DEĞERİ pinlenir — türetme zinciri (<c>For(Balanced)</c>) DEĞİL. Gerekçe
    /// K11 kopya pini ile aynıdır: türetmeyi doğrulamak totolojidir, oysa burada korunan şey "sıkışan copy'yi
    /// açmak için job'ı ne kadar gevşetiyoruz" KARARIdır. Tablo değişirse bu assert bilerek kırılır ve karar
    /// yeniden gözden geçirilir.
    /// </summary>
    [Fact]
    public void Copy_phase_floor_is_70_percent_and_below_normal()
    {
        Assert.Equal(70, PerfProfile.CopyPhaseFloorPercent);
        Assert.Equal(ProcessPriorityClassKind.BelowNormal, PerfProfile.CopyPhaseFloorPriority);
    }

    [Theory]
    [InlineData("Full")]
    [InlineData("Balanced")]
    [InlineData("Light")]
    public void Try_parse_resolves_the_three_perf_mode_strings_the_app_uses(string text)
        => Assert.Equal(PerfProfile.For(Enum.Parse<PerfMode>(text)), PerfProfile.TryParse(text));

    [Theory]
    [InlineData("full")]   // eşleşme ordinal — App'in yazdığı string birebir gelir
    [InlineData("")]
    [InlineData("Turbo")]
    public void Try_parse_returns_null_for_an_unknown_perf_mode_text(string text)
        => Assert.Null(PerfProfile.TryParse(text));
}
