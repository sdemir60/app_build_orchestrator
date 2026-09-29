namespace BuildOrchestrator.App.ViewModels;

/// <summary>[design v1.23.0 §2.12] Restart ekranının üç adımı: kapanış · kurulum · açılış.</summary>
public enum UpdateRestartStep
{
    Closing,
    Installing,
    Starting,
}

/// <summary>Bir adım: süresi ve bittiğinde ulaşılan ilerleme yüzdesi (prototipin <c>[ms, end]</c> çifti).</summary>
public sealed record UpdateRestartStage(UpdateRestartStep Step, double DurationMs, double EndPercent);

/// <summary>Çizelgenin bir anı: okunan adım ve ilerleme yüzdesi (0..100).</summary>
public readonly record struct UpdateRestartFrame(UpdateRestartStep Step, double Percent);

/// <summary>
/// [design v1.23.0 §2.12 · §9 "Restart ekranı" · plan U4] Restart ekranının zaman çizelgesi — saf çekirdek (WPF yok;
/// saat ve zamanlayıcı ekranın, sayılar burada). Prototip: <c>UPDATE_STEPS = [[800,20],[1100,78],[800,100]]</c>,
/// <c>UPDATE_TOTAL</c> (BuildApp.jsx:1660-1661), adım içinde doğrusal ilerleme (BuildApp.jsx:1757-1763), sönüşün
/// başlangıcı <c>UPDATE_TOTAL + 120</c> (BuildApp.jsx:2661).
///
/// <para>Güncelleme motoru henüz YOK: çizelge gerçek bir kurulumun süresini ölçmez, tasarımın gösterdiği sırayı oynatır.
/// Motor yazıldığında adımları o sürer.</para>
/// </summary>
public static class UpdateRestartTimeline
{
    /// <summary>Kapanış adımının süresi (BuildApp.jsx:1660).</summary>
    public const double ClosingMs = 800;

    /// <summary>Kurulum adımının süresi.</summary>
    public const double InstallingMs = 1100;

    /// <summary>Açılış adımının süresi.</summary>
    public const double StartingMs = 800;

    /// <summary>Kapanış bittiğinde ilerleme yüzdesi.</summary>
    public const double ClosingEndPercent = 20;

    /// <summary>Kurulum bittiğinde ilerleme yüzdesi.</summary>
    public const double InstallingEndPercent = 78;

    /// <summary>Açılış bittiğinde ilerleme yüzdesi — çubuk dolar.</summary>
    public const double StartingEndPercent = 100;

    /// <summary>Son adım bittikten sonra ekranın sönmeye başlamasına kadar geçen süre (BuildApp.jsx:2661).</summary>
    public const double FadeOutDelayMs = 120;

    /// <summary>Adımlar oynatıldıkları sırayla.</summary>
    public static IReadOnlyList<UpdateRestartStage> Stages { get; } =
    [
        new(UpdateRestartStep.Closing, ClosingMs, ClosingEndPercent),
        new(UpdateRestartStep.Installing, InstallingMs, InstallingEndPercent),
        new(UpdateRestartStep.Starting, StartingMs, StartingEndPercent),
    ];

    /// <summary>Üç adımın toplam süresi (<c>UPDATE_TOTAL</c>).</summary>
    public static double TotalMs { get; } = Stages.Sum(s => s.DurationMs);

    /// <summary>Ekranın sönmeye başladığı an, oynatmanın başından itibaren.</summary>
    public static double FadeOutAtMs => TotalMs + FadeOutDelayMs;

    /// <summary>Oynatmanın başından <paramref name="elapsedMs"/> sonra okunan adım ve yüzde. Adım sınırında bir sonraki
    /// adım başlar; adım içinde yüzde önceki adımın bitişinden bu adımınkine doğrusal ilerler. Toplamın ötesi son adımda
    /// %100'de kalır; negatif süre (saat geri giderse) başlangıç sayılır.</summary>
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
