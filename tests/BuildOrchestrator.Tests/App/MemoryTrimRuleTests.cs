using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 1] <see cref="MemoryTrimRule"/>: işlem sonu bellek triminin SAF kararı. Kural WPF'siz ve saatsizdir —
/// tablo doğrudan bayraklarla sürülür; kabuk bağlama (zamanlayıcı, dispatcher, sinyallerin okunması) <c>MainWindow</c>'un testlerindedir.
///
/// <para><b>Tablonun mantığı:</b> bitmiş bir işlem olmalı; uçuşta işlem ya da bekleyen log yüklemesi varken HİÇ; pencere
/// görünürse bitiş finali oynamıyor olmalı; pencere gizliyse tepside biten koşunun gösterge çıkışı tamamlanmış olmalı
/// (bugünkü tepsi kuralı). Görünür pencerede gösterge hiç yoktur — "gösterge çıkışı bekleniyor" yalnız gizliyken sayılır;
/// gizliyken de final oynamaz (<c>SetSurfaceHidden</c> keser) — "final oynuyor" yalnız görünürken sayılır.</para>
/// </summary>
public class MemoryTrimRuleTests
{
    // (ended, inFlight, pendingLoad, hidden, finalePlaying, awaitingIndicator) → ShouldTrim
    [Theory]
    [InlineData(0, false, false, false, false, false, false)] // hiç işlem bitmedi
    [InlineData(1, true,  false, false, false, false, false)] // uçuşta işlem
    [InlineData(1, false, true,  false, false, false, false)] // bekleyen log yüklemesi
    [InlineData(1, false, false, false, true,  false, false)] // görünür, final oynuyor
    [InlineData(1, false, false, false, false, false, true)]  // görünür, final bitti
    [InlineData(1, false, false, true,  false, true,  false)] // gizli, gösterge çıkışı bekleniyor
    [InlineData(1, false, false, true,  false, false, true)]  // gizli, gösterge çıktı
    [InlineData(1, false, false, true,  true,  false, true)]  // gizli: final önemsiz
    // Tablonun ek satırları: her bayrağın yalnız KENDİ dalında sayıldığını pinler.
    [InlineData(1, false, false, false, false, true,  true)]  // görünür: gösterge çıkışı önemsiz (görünürde gösterge yok)
    [InlineData(1, false, false, false, true,  true,  false)] // görünür: final oynuyorsa gösterge ne derse desin bekler
    [InlineData(1, true,  false, true,  false, false, false)] // gizli ama uçuşta işlem
    [InlineData(1, false, true,  true,  false, false, false)] // gizli ama bekleyen log yüklemesi
    public void The_rule_table(int ended, bool inFlight, bool pending, bool hidden, bool finale, bool awaiting, bool expected)
    {
        var rule = new MemoryTrimRule();

        Assert.Equal(expected, rule.ShouldTrim(ended, inFlight, pending, hidden, finale, awaiting));
    }

    [Fact]
    public void Each_operation_is_trimmed_at_most_once()
    {
        var rule = new MemoryTrimRule();
        Assert.True(rule.ShouldTrim(3, false, false, false, false, false));

        rule.MarkTrimmed(3);

        Assert.False(rule.ShouldTrim(3, false, false, false, false, false));
        Assert.True(rule.ShouldTrim(4, false, false, false, false, false));
    }

    /// <summary>Sormak tüketmez, yalnız <c>MarkTrimmed</c> tüketir: toplama ZAMANLANDI ama YÜRÜTÜLMEDİYSE (arada yeni bir işlem
    /// başladı, log yüklemesi bekliyor) işlem sıradaki sinyale kadar uygun kalır. Çağıran bu yüzden işareti toplamanın
    /// yürütüldüğü anda koyar — zamanladığı anda değil.</summary>
    [Fact]
    public void Asking_does_not_consume_the_operation_only_marking_does()
    {
        var rule = new MemoryTrimRule();

        Assert.True(rule.ShouldTrim(2, false, false, false, false, false));
        Assert.True(rule.ShouldTrim(2, false, false, false, false, false)); // yürütme anında yeniden sorulabilir

        rule.MarkTrimmed(2);                                                   // toplama gerçekten yürütüldü

        Assert.False(rule.ShouldTrim(2, false, false, false, false, false));
    }

    /// <summary>Görünür pencerede trim finalin bitişinden sonra bu payla ertelenir (spec §6.1: 1,5 s). Pay TEK yerdedir: payı
    /// kullanan kabuk kodu kendi sayısını yazmaz, buradan okur.</summary>
    [Fact]
    public void The_grace_after_the_finale_is_one_and_a_half_seconds()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(1500), MemoryTrimRule.GraceAfterFinale);
    }
}
