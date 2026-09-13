using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [clean] <see cref="StepHold"/>: bir işlem dizisinin adımları arasındaki bekletme. Burada ZAMANSIZ kurallar
/// pinlenir — azaltılmış hareket ve pozitif olmayan süre; gerçek tick'i beklemek D8 ihlali olurdu (o, kabuk
/// kablajıdır, şeridin elapsed timer'ıyla aynı kategoride).
/// </summary>
public class StepHoldTests
{
    /// <summary>[§1.3 "tüm süreler 0"] Azaltılmış harekette HİÇ beklenmez: dizi kesintisiz akar, bir adımın
    /// estetik temposu için kullanıcının erişilebilirlik ayarı ezilmez.</summary>
    [Fact]
    public void Reduced_motion_skips_the_hold_entirely()
    {
        var hold = new StepHold(() => false);

        var task = hold.HoldAsync(440);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.False(hold.IsHolding);
    }

    /// <summary>Pozitif olmayan süre de beklemez — çağıran "kalan süre" hesabını negatife düşebilecek biçimde
    /// yapar (yavaş bir Clean adımı zaten görünmüştür) ve o dalın ayrıca kapı yazması gerekmez.</summary>
    [Theory]
    [InlineData(0d)]
    [InlineData(-390d)]
    public void A_non_positive_hold_completes_at_once(double ms)
    {
        var hold = new StepHold(() => true);

        Assert.True(hold.HoldAsync(ms).IsCompletedSuccessfully);
        Assert.False(hold.IsHolding);
    }

    /// <summary>Animasyonlar açıkken bekletme GERÇEKTEN kurulur (tick'i kabuk sayar) — ve sinyal CANLI okunur:
    /// aynı örnek, ayar sonradan kapanınca beklemez.</summary>
    [StaFact]
    public void The_motion_signal_is_read_live_on_every_hold()
    {
        bool animations = true;
        var hold = new StepHold(() => animations);

        var pending = hold.HoldAsync(440);
        Assert.False(pending.IsCompleted);
        Assert.True(hold.IsHolding);

        animations = false;
        var second = hold.HoldAsync(440);

        Assert.True(second.IsCompletedSuccessfully);
        Assert.False(hold.IsHolding);
        // Üst üste gelen bekletme öncekini ASILI BIRAKMAZ: yoksa düğme kalıcı kilitlenirdi.
        Assert.True(pending.IsCompleted);
    }
}
