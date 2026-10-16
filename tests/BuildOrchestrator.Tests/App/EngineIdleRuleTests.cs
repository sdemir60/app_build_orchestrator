using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 3 · spec §6.4 · karar 13] <see cref="EngineIdleRule"/>: boştaki motorun uyutulması kararının SAF kuralı. Kural WPF'siz ve
/// saatsizdir — tablo doğrudan girdilerle sürülür; tik bağlaması (VM'in tek tanımlarının okunması, uyutma, tek uçuş) <c>EngineSleepTests</c>'tedir.
///
/// <para><b>Tablonun mantığı:</b> ayar açık olmalı; uçuşta işlem ya da cevabı beklenen proje log yüklemesi varken HİÇ; motor hazır olmalı —
/// başlarken (açılış, yeniden başlatma), uyku ya da uyanma sürerken (geçiş: tek uçuş), uyurken, ölmüşken ya da hiç doğamamışken HİÇ; ve son motor
/// etkinliğinden bu yana en az <see cref="EngineIdleRule.IdleAfter"/> geçmiş olmalı (eşik dahil).</para>
/// </summary>
public class EngineIdleRuleTests
{
    private static readonly long IdleAfterMs = EngineSleepProbe.IdleAfterMs; // ms hâlinin TEK tanımı ortak sahtededir

    /// <summary>Son motor etkinliği — 0 DEĞİL: "hiç yazılmamış damga" ile "t=0'da etkinlik" karışmasın.</summary>
    private const long LastActivityMs = 1_000;

    // (overMs, settingOn, operationInFlight, pendingLogLoad, engine) → ShouldSleep; geçen süre = IdleAfter + overMs
    [Theory]
    [InlineData(0,  true,  false, false, EngineState.Ready,       true)]  // beş işsiz dakika doldu (eşik dahil)
    [InlineData(-1, true,  false, false, EngineState.Ready,       false)] // bir ms eksik
    [InlineData(0,  false, false, false, EngineState.Ready,       false)] // ayar kapalı
    [InlineData(0,  true,  true,  false, EngineState.Ready,       false)] // uçuşta işlem
    [InlineData(0,  true,  false, true,  EngineState.Ready,       false)] // cevabı beklenen proje log yüklemesi
    [InlineData(0,  true,  false, false, EngineState.Starting,    false)] // hazır değil: açılış ya da yeniden başlatma sürüyor (C1)
    [InlineData(0,  true,  false, false, EngineState.Sleeping,    false)] // geçiş sürüyor: uyku uçuşta — ikinci uyku yok (C3)
    [InlineData(0,  true,  false, false, EngineState.Asleep,      false)] // zaten uyuyor
    [InlineData(0,  true,  false, false, EngineState.Waking,      false)] // geçiş sürüyor: uyanma uçuşta (Task 21) — pin, kural değişmedi
    [InlineData(0,  true,  false, false, EngineState.Dead,        false)] // kendiliğinden öldü: toparlamak "Restart engine"in işi
    [InlineData(0,  true,  false, false, EngineState.Unavailable, false)] // hiç doğamadı (kurulum)
    public void The_rule_table(long overMs, bool settingOn, bool operationInFlight, bool pendingLogLoad, EngineState engine, bool expected)
    {
        long now = LastActivityMs + IdleAfterMs + overMs;

        Assert.Equal(expected, EngineIdleRule.ShouldSleep(now, LastActivityMs, settingOn, operationInFlight, pendingLogLoad, engine));
    }

    /// <summary>Yalnız hazır motor uyutulur — durumların HEPSİ dolaşılır: sonradan eklenen bir geçiş durumu (ör. uyandırma) kuralı
    /// değiştirmeden kendiliğinden "uyutma" tarafına düşer. Tablo bugünkü durumları tek tek gerekçelendirir; bu test kuralın biçimini
    /// (durumlar arasında tek istisna) pinler.</summary>
    [Fact]
    public void Only_a_ready_engine_is_put_to_sleep()
    {
        long now = LastActivityMs + IdleAfterMs * 10;

        foreach (var engine in Enum.GetValues<EngineState>())
            Assert.Equal(engine == EngineState.Ready, EngineIdleRule.ShouldSleep(now, LastActivityMs, true, false, false, engine));
    }

    /// <summary>[karar 11 · karar 29] "Beş dakika"nın TEK sahibi — spec §6.4 beş dakikayı söyler. <c>EngineHostTests.Kill_exit_wait_stays_one_second</c>
    /// deseni: sabit sessizce kayarsa (ör. 5 → 50 dk) motor beklenenden çok sonra uyur ve kullanıcıya söylenen süre yalan olur. Sabitin
    /// sahibi kuralın sınıfı olduğu için pin de burada durur (Task 19'da ayarın testleriyle doğmuştu); iki kullanıcı metninin sabitle
    /// uyuşması ayarın testindedir (<c>StopEngineWhenIdleSettingsTests.The_catalog_text_and_the_on_note_say_the_window_the_rule_uses</c>).
    /// <para>KIRMIZI (geçici bozma M4, Task 19: <c>IdleAfter</c> 6 dk): <c>Expected: 00:05:00 / Actual: 00:06:00</c>.</para></summary>
    [Fact]
    public void The_idle_window_stays_five_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), EngineIdleRule.IdleAfter);
    }
}
