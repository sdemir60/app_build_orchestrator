namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/3a] Hibrit aktif-satır daktilosu için SAF, Stopwatch-bazlı (tick-sayısından bağımsız) tempo hesabı —
/// design-v1 prototipinden (BuildApp.jsx <c>TypingLine</c>) birebir taşınan tempo: satır başına ≤ ~250ms.
///
/// <para>Prototip: her 11ms'de <c>ceil(len/22)</c> karakter açılır → en fazla ~22 adım × 11ms = 242ms (≤250ms).
/// Burada adım SAYISI değil, GEÇEN SÜRE ölçülür (DispatcherTimer ~15.6ms çözünürlüğü tick sayısını güvenilmez
/// kılar — motion sözleşmesi): <see cref="RevealedAt"/> elapsed'ten türetilir, monotondur, <see cref="Duration"/>'da
/// tamamlanır.</para>
///
/// <para>Reduced-motion (<c>animationsEnabled=false</c>): <see cref="Instant"/> — satır t=0'da TAM görünür,
/// daktilo/blink YOK (motion sözleşmesi: "AnimationsEnabled false iken typewriter INSTANT").</para>
/// </summary>
public sealed class TypewriterScheduler
{
    /// <summary>Prototipteki setInterval periyodu (11ms).</summary>
    public const double StepMs = 11.0;
    /// <summary>Prototipteki üst sınır adım sayısı (satır uzunluğu / 22) — toplam süreyi ≤242ms tutar.</summary>
    public const int MaxSteps = 22;

    /// <summary>
    /// [A13/B3 · k3] Yazım bittikten sonra imlecin sönmeye başlamadan önce SABİT kaldığı süre —
    /// otorite <c>BuildApp.jsx:91</c> (<c>doneT = setTimeout(onDone, 420)</c>), design-v1 §2.5
    /// "imleç ~420ms sonra söner".
    ///
    /// <para><b>TEK TANIM burasıdır.</b> Değer önce ÜÇ yerde ayrı ayrı yazılıydı (<c>ConsoleView</c>,
    /// <c>EventStreamView</c>, <c>EventStreamRow</c>) ve yalnız biri pinliydi — yani iki kopya sessizce
    /// sürüklenebilirdi. Üç sahip de artık derleme-zamanı alias tutar (<c>RevealStagger.RevealMs</c> ve
    /// <c>StickyLayerList.RevealHeroKey</c> ile AYNI desen), dolayısıyla ayrışmaları OLANAKSIZDIR.</para>
    ///
    /// <para>Buraya ait olmasının gerekçesi: hold, daktilonun BİTİŞİNE göre ölçülür — üç sahibin üçü de
    /// <c>_scheduler.Duration + CursorHoldMs</c> yazar, yani sabit bu tipin tempo tablosunun parçasıdır
    /// (<see cref="StepMs"/>/<see cref="MaxSteps"/> ile aynı otorite satırından gelir).</para>
    /// </summary>
    public const double CursorHoldMs = 420.0;

    private readonly int _length;
    private readonly int _charsPerStep;

    public bool Instant { get; }

    public TypewriterScheduler(int textLength, bool animationsEnabled)
    {
        _length = Math.Max(0, textLength);
        Instant = !animationsEnabled;
        _charsPerStep = Math.Max(1, (int)Math.Ceiling(_length / (double)MaxSteps));
    }

    /// <summary>Verilen geçen sürede açığa çıkmış karakter sayısı — monoton, [0, length] arasında.
    /// Instant iken her elapsed'te tam uzunluk.</summary>
    public int RevealedAt(TimeSpan elapsed)
    {
        if (Instant) return _length;
        if (elapsed <= TimeSpan.Zero) return 0;
        long steps = (long)(elapsed.TotalMilliseconds / StepMs);
        long revealed = _charsPerStep * steps;
        return (int)Math.Clamp(revealed, 0, _length);
    }

    public bool IsCompleteAt(TimeSpan elapsed) => RevealedAt(elapsed) >= _length;

    /// <summary>Daktilonun tamamlanma süresi — Instant iken 0, aksi halde <c>ceil(len/charsPerStep) × 11ms</c>
    /// (her zaman ≤242ms).</summary>
    public TimeSpan Duration => Instant
        ? TimeSpan.Zero
        : TimeSpan.FromMilliseconds(Math.Ceiling(_length / (double)_charsPerStep) * StepMs);
}
