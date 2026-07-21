namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/3b] Proje-log kaskatının SAF, Stopwatch/elapsed-bazlı tempo hesabı — design-v1 §2.5 + prototip
/// (BuildApp.jsx <c>RevealLog</c>) birebir: satırlar remount ile <b>26ms'de 3 satır</b> açığa çıkar
/// (başlangıçta 2 satır), her satır <b>140ms opacity-fade</b> ile belirir. AvalonEdit satır-bazlı
/// translateY+scale desteklemez → feasibility §3.6/A13.1 kabulü: <b>opacity-fade eşdeğeri</b> (tempo birebir).
///
/// <para><b>Flash yok:</b> bir satır açığa çıkmadan opacity 0'dır; açıldığı ANda 0'dan başlar (t=reveal'da 0,
/// reveal+140ms'de 1) — böylece gecikme sırasında tam-opak bir flash olmaz.</para>
///
/// <para><b>Reduced-motion</b> (<c>animationsEnabled=false</c>): <see cref="Instant"/> — tüm satırlar t=0'da
/// tam görünür (opacity 1), kaskat/fade YOK (motion sözleşmesi).</para>
/// </summary>
public sealed class CascadeScheduler
{
    /// <summary>Prototip tempo: her <see cref="TempoMs"/>'de <see cref="LinesPerStep"/> satır açığa çıkar.</summary>
    public const double TempoMs = 26.0;
    public const int LinesPerStep = 3;
    /// <summary>Prototip: <c>useState(2)</c> — ilk karede zaten 2 satır açık (sonra +3/26ms).</summary>
    public const int InitialLines = 2;
    /// <summary>Satır başına opacity-fade süresi (prototip <c>bo-pop-in .14s</c>).</summary>
    public const double FadeMs = 140.0;

    private readonly int _total;

    public CascadeScheduler(int totalLines, bool animationsEnabled)
    {
        _total = Math.Max(0, totalLines);
        Instant = !animationsEnabled;
    }

    /// <summary>Reduced-motion iken true — her sorgu anında tam açık/opak döner.</summary>
    public bool Instant { get; }

    /// <summary>Kaskatın kapsadığı toplam satır sayısı.</summary>
    public int Total => _total;

    /// <summary>Verilen geçen sürede belgede AÇIĞA ÇIKMIŞ (görünür alana giren) satır sayısı — monoton,
    /// [min(2,total)..total]. Instant iken her elapsed'te tam sayı.</summary>
    public int RevealedAt(TimeSpan elapsed)
    {
        if (Instant) return _total;
        if (elapsed <= TimeSpan.Zero) return Math.Min(InitialLines, _total);
        long steps = (long)(elapsed.TotalMilliseconds / TempoMs);
        long revealed = InitialLines + LinesPerStep * steps;
        return (int)Math.Clamp(revealed, 0, _total);
    }

    /// <summary>Satır index'inin (0-bazlı) açığa çıktığı elapsed anı. İlk <see cref="InitialLines"/> satır t=0'da.</summary>
    public TimeSpan RevealTimeOf(int lineIndex)
    {
        if (Instant || lineIndex < InitialLines) return TimeSpan.Zero;
        // en küçük steps: InitialLines + LinesPerStep*steps >= lineIndex+1
        long steps = (long)Math.Ceiling((lineIndex + 1 - InitialLines) / (double)LinesPerStep);
        return TimeSpan.FromMilliseconds(steps * TempoMs);
    }

    /// <summary>Satırın verilen elapsed'teki opacity'si [0,1]. Açığa çıkmadan 0 (flash yok); açıldıktan sonra
    /// <see cref="FadeMs"/>'de 1'e ramp. Kaskat kapsamı DIŞINDA (canlı sonradan eklenen) satır tam opak (1).</summary>
    public double OpacityOf(int lineIndex, TimeSpan elapsed)
    {
        if (Instant || lineIndex >= _total) return 1.0;
        var reveal = RevealTimeOf(lineIndex);
        if (elapsed < reveal) return 0.0;
        double t = (elapsed - reveal).TotalMilliseconds / FadeMs;
        return Math.Clamp(t, 0.0, 1.0);
    }

    /// <summary>Kaskatın tamamlanma süresi — son satırın açığa çıkışı + fade. Instant/boş iken 0.</summary>
    public TimeSpan Duration => Instant || _total <= 0
        ? TimeSpan.Zero
        : RevealTimeOf(_total - 1) + TimeSpan.FromMilliseconds(FadeMs);

    public bool IsComplete(TimeSpan elapsed) => Instant || elapsed >= Duration;
}
