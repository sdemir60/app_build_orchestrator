namespace BuildOrchestrator.App.ViewModels;

/// <summary>[design v1.23.0 §2.12 · motor · Task 11 · K6] Restart ekranının adımı — tek adım: kapanış.</summary>
public enum UpdateRestartStep
{
    Closing,
}

/// <summary>Bir adım: süresi ve bittiğinde ulaşılan ilerleme yüzdesi (prototipin <c>[ms, end]</c> çifti).</summary>
public sealed record UpdateRestartStage(UpdateRestartStep Step, double DurationMs, double EndPercent);

/// <summary>Çizelgenin bir anı: okunan adım ve ilerleme yüzdesi (0..100).</summary>
public readonly record struct UpdateRestartFrame(UpdateRestartStep Step, double Percent);

/// <summary>
/// [design v1.23.0 §2.12 · §9 "Restart ekranı" · plan U4 · motor · Task 11 · K6] Restart ekranının zaman çizelgesi — saf
/// çekirdek (WPF yok; saat ve zamanlayıcı ekranın, sayılar burada). Adım içinde ilerleme doğrusaldır
/// (BuildApp.jsx:1757-1763); kapanışın süresi prototipin ilk adımınınkidir (BuildApp.jsx:1660).
///
/// <para><b>Tek adım: kapanış</b> (K6, kullanıcı kararı 2026-09-30). Windows çalışan bir programın dosyalarını
/// değiştirmeye izin vermez: kurulum ancak uygulama kapandıktan sonra Update.exe tarafından, penceresiz yapılır ve yeni
/// sürüm normal açılır. Uygulamanın kendi penceresinde gösterebileceği tek adım kapanıştır — prototipin kurulum ve açılış
/// adımları bu ekranda oynatılsaydı gerçekte olmayan bir şeyi anlatırdı. Çubuk kapanışın süresinde dolar ve pencere
/// kapanana dek dolu kalır; sönüş yoktur.</para>
/// </summary>
public static class UpdateRestartTimeline
{
    /// <summary>Kapanış adımının süresi (BuildApp.jsx:1660).</summary>
    public const double ClosingMs = 800;

    /// <summary>Kapanış bittiğinde ilerleme yüzdesi — çubuk dolar.</summary>
    public const double ClosingEndPercent = 100;

    /// <summary>Adımlar oynatıldıkları sırayla.</summary>
    public static IReadOnlyList<UpdateRestartStage> Stages { get; } =
    [
        new(UpdateRestartStep.Closing, ClosingMs, ClosingEndPercent),
    ];

    /// <summary>Adımların toplam süresi — çubuğun dolduğu an.</summary>
    public static double TotalMs { get; } = Stages.Sum(s => s.DurationMs);

    /// <summary>Oynatmanın başından <paramref name="elapsedMs"/> sonra okunan adım ve yüzde. Adım sınırında bir sonraki
    /// adım başlar; adım içinde yüzde önceki adımın bitişinden bu adımınkine doğrusal ilerler. Toplamın ötesi son adımda
    /// %100'de kalır (çıkış gecikirse çubuk dolu durur); negatif süre (saat geri giderse) başlangıç sayılır.</summary>
    public static UpdateRestartFrame At(double elapsedMs)
    {
        double at = Math.Clamp(elapsedMs, 0, TotalMs);
        double startMs = 0, startPercent = 0;
        foreach (var stage in Stages)
        {
            if (at < startMs + stage.DurationMs)
            {
                double progress = (at - startMs) / stage.DurationMs;
                return new(stage.Step, startPercent + (stage.EndPercent - startPercent) * progress);
            }
            startMs += stage.DurationMs;
            startPercent = stage.EndPercent;
        }
        return new(Stages[^1].Step, Stages[^1].EndPercent);
    }
}
