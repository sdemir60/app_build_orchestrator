namespace BuildOrchestrator.App.ViewModels;

/// <summary>[Exit] Kapanış ekranının Exit kipindeki adımı: işin durdurulması, ardından süreçlerin bitirilmesi.</summary>
public enum ExitClosingStep
{
    /// <summary>"Stopping build…" — VM işi durduruyor; çıkış hazır olana dek sürer.</summary>
    Stopping,

    /// <summary>"Ending 5 processes…" — kapanış motoru ve kalan her şeyi outer job'la bitirecek.</summary>
    Ending,
}

/// <summary>Çizelgenin bir anı: okunan adım, ilerleme yüzdesi (0..100) ve çizelgenin bitip bitmediği.</summary>
public readonly record struct ExitClosingFrame(ExitClosingStep Step, double Percent, bool Done);

/// <summary>
/// [Exit] Kapanış ekranının Exit kipindeki zaman çizelgesi — saf çekirdek (WPF yok; saat ve zamanlayıcı ekranın, sayılar burada).
/// <see cref="UpdateRestartTimeline"/>'ın kardeşidir; farkı ilk adımın süresinin önceden bilinmemesidir: işin durdurulması VM'in
/// <c>ExitReady</c>'sine dek sürer (motorun <c>runStopped</c>'ı, durdurma süresinin dolması, ya da kesilen bir işte hemen).
///
/// <para><b>İlk adım</b> çubuğu <see cref="StoppingMinMs"/>'de <see cref="StoppingEndPercent"/>'e doğrusal götürür ve çıkış hazır olana dek
/// orada bekler; çıkış hemen hazır olsa da adım bu süre kadar görünür — kullanıcı neyin durduğunu okuyabilsin. <b>İkinci adım</b> ilk adımın
/// en kısa süresi dolmuş VE çıkış hazırken başlar ve çubuğu <see cref="EndingMs"/>'de doldurur; çizelge orada biter (kabuk kapanışı o anda
/// ister — ekran ilk kez çizilmeden pencere kapanmasın diye). Süreler tasarımın değil uygulamanın sayılarıdır: tasarım yalnız iki adımı ve
/// çubuğu verir; bir saniyelik toplam, iki satırın okunduğu en kısa süredir.</para>
/// </summary>
public static class ExitClosingTimeline
{
    /// <summary>İlk adımın en kısa görünme süresi (ms) — çubuk bu sürede <see cref="StoppingEndPercent"/>'e varır.</summary>
    public const double StoppingMinMs = 500;

    /// <summary>İlk adımın sonunda çubuğun yüzdesi — çıkış hazır olana dek burada bekler.</summary>
    public const double StoppingEndPercent = 50;

    /// <summary>İkinci adımın süresi (ms) — çubuk %100'e varır ve çizelge biter.</summary>
    public const double EndingMs = 500;

    /// <summary>Oynatmanın başından <paramref name="elapsedMs"/> sonra okunan an. <paramref name="stoppedAtMs"/> çıkışın hazır olduğu andır
    /// (oynatmanın başından); henüz değilse <c>null</c> — ilk adım sürer. Negatif süre (saat geri giderse) başlangıç sayılır.</summary>
    public static ExitClosingFrame At(double elapsedMs, double? stoppedAtMs)
    {
        double at = Math.Max(0, elapsedMs);
        double endingStartMs = stoppedAtMs is { } stopped ? Math.Max(StoppingMinMs, stopped) : double.PositiveInfinity;
        if (at < endingStartMs)
            return new(ExitClosingStep.Stopping, StoppingEndPercent * Math.Min(at / StoppingMinMs, 1), Done: false);

        double progress = Math.Min((at - endingStartMs) / EndingMs, 1);
        return new(ExitClosingStep.Ending, StoppingEndPercent + (100 - StoppingEndPercent) * progress,
            Done: at >= endingStartMs + EndingMs);
    }
}
