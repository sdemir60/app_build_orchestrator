using System.Globalization;
using BuildOrchestrator.Core.Formatting;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Core.Incremental;

/// <summary>
/// [T70][A6/Δ8] "Kalan süre" (ETA) tahmini — BAĞLAYICI v7 A6/Δ8 formülü, harfiyen:
/// <list type="bullet">
/// <item><b>Ham tahmin</b> = (queued projelerin süre tahminleri toplamı + building projelerin kalan süreleri) /
/// <c>parallelism</c>, ARTI (herhangi bir proje building ise) sabit <see cref="BuildingOverheadMs"/> (400ms),
/// ARTI queued cycle (SCC) üyelerinin süre tahminleri toplamının <see cref="CycleRoundPolicy.BaselineRounds"/>
/// katı. Overhead paralelliğe BÖLÜNMEDEN, bölümden SONRA eklenir (bkz. design-v1 prototype <c>SimEngine.eta()</c>:
/// <c>remain / Math.max(1,maxPar) + (building.length ? 400 : 0)</c>). <b>Cycle katkısı da paralelliğe
/// BÖLÜNMEZ</b> [cycle rounds/Task 10]: bir SCC'nin üyeleri motor tarafında SIRALI invoke edilir (biri diğerinin
/// az önce yazdığı DLL'i okur — eşzamanlı OLAMAZ) ve küme en az <see cref="CycleRoundPolicy.BaselineRounds"/> tur
/// çalışır (iki ardışık yeşil tur yakınsama şartı — bkz. <see cref="CycleRoundPolicy"/>); bu yüzden queued
/// cycle süresi paralellik varsayımının ve tek-tur varsayımının DIŞINDA, kendi terimiyle eklenir.</item>
/// <item><b>EMA yumuşatma</b> ardışık tick'ler arası: <c>newEta = 0.75·previousEta + 0.25·rawEstimate</c>
/// (<see cref="PreviousWeight"/>/<see cref="RawWeight"/>). İlk tick (previousEta yok) → doğrudan ham tahmin.</item>
/// <item><b>Gösterim:</b> en yakın 5 saniyeye yuvarlanır; <see cref="AlmostDoneThresholdMs"/> (4000ms) ALTI →
/// "· almost done" (numerik YOK); üstü → HAM SANİYE olarak "~Ns left" (InvariantCulture) — design-v1 prototype
/// <c>BuildApp.jsx:761-763</c>'ün birebir portu (<c>Math.round(eta/5000)*5 + 's left'</c>, ör. 125000ms →
/// "~125s left"); dakikaya ASLA çevrilmez — mm:ss formatı (<see cref="DurationFormat.Elapsed"/>) yalnız elapsed/
/// no-history dalında (bkz. aşağı) kullanılır, ETA'da DEĞİL.</item>
/// <item><b>İlk koşu / bilinmeyen süre fallback:</b> bir projenin <c>BuildState.LastDurationMs</c>'i yoksa
/// (null) — TÜM projeler (queued+building+queued cycle) arasında bilinen (non-null) sürelerin ORTALAMASI o
/// proje için temsili tahmin olarak kullanılır [cycle rounds/Task 10]: ortalama artık ÜÇ listeyi BİRLİKTE
/// kapsar, tek bilinen süre cycle listesinde bile olsa ortalama hesaplanır. Hiçbir yerde bilinen süre YOKSA
/// (ortalama hesaplanamaz) <see cref="ComputeRawEstimateMs"/> <c>null</c> döner — çağıran bu durumda ETA
/// NUMARASI GÖSTERMEMELİ, yalnız X/N · elapsed süre (bkz. <see cref="FormatDisplay"/> null-eta dalı).</item>
/// </list>
/// <para>
/// Saf/stateless: previousEta (EMA state'i) çağıran tarafından (VM/tick loop) taşınır — burada hiçbir alan/saat
/// TUTULMAZ [D3 — no internal clock]. Supervisor/App wiring (queued/building listelerinin her tick'te nereden
/// geldiği, previousEta'nın nerede saklandığı) bu görevin kapsamı DIŞINDA — bkz. task-11-brief.md "Task 12/13
/// wiring ile" notu (Task 10'un obj-isolation wiring'i gibi, seam burada hazır bırakılıyor).
/// </para>
/// </summary>
public static class EtaCalculator
{
    /// <summary>EMA'da ÖNCEKİ smoothed değere verilen ağırlık. [Δ8] BAĞLAYICI sabit.</summary>
    public const double PreviousWeight = 0.75;

    /// <summary>EMA'da YENİ ham tahmine verilen ağırlık. [Δ8] BAĞLAYICI sabit.</summary>
    public const double RawWeight = 0.25;

    /// <summary>Herhangi bir proje building ise ham tahmine eklenen sabit overhead (ms). [Δ8] BAĞLAYICI sabit.</summary>
    public const long BuildingOverheadMs = 400;

    /// <summary>Bu eşiğin (ms) ALTINDAKİ smoothed ETA "· almost done" gösterilir — numerik değer YOK.</summary>
    public const long AlmostDoneThresholdMs = 4_000;

    /// <summary>Gösterim yuvarlama birimi — en yakın 5 saniyeye (ms cinsinden).</summary>
    public const long DisplayRoundingMs = 5_000;

    /// <summary>Şu an building olan bir projenin girdisi: bu tick'e kadar geçen süre + (varsa) bilinen tahmini süresi.</summary>
    public readonly record struct BuildingProject(long ElapsedMs, long? LastDurationMs);

    /// <summary>
    /// Ham (smoothing UYGULANMAMIŞ) tahmini hesaplar.
    /// </summary>
    /// <param name="queuedDurationEstimatesMs">Her queued (cycle-DIŞI) projenin <c>BuildState.LastDurationMs</c>'i — bilinmiyorsa <c>null</c>.</param>
    /// <param name="building">Şu an building olan projeler (elapsed + varsa bilinen süre).</param>
    /// <param name="parallelism">Eşzamanlı build slotu sayısı; 1'den küçükse 1'e clamp edilir (savunmacı — sıfıra bölme YOK).</param>
    /// <param name="cycleQueuedDurationEstimatesMs">
    /// [cycle rounds/Task 10] Henüz başlamamış cycle (SCC) üyelerinin süre tahminleri — bilinmiyorsa <c>null</c>.
    /// <paramref name="queuedDurationEstimatesMs"/>'ten AYRI tutulur: bu üyeler paralel DEĞİL sıralı invoke
    /// edilir ve küme en az <see cref="CycleRoundPolicy.BaselineRounds"/> tur çalışır, bu yüzden katkıları
    /// <paramref name="parallelism"/>'e bölünmeden, <see cref="CycleRoundPolicy.BaselineRounds"/> ile çarpılarak
    /// eklenir (bkz. sınıf özeti).
    /// </param>
    /// <returns>
    /// Ham tahmin (ms), YUVARLANMIŞ (<see cref="Math.Round(double, MidpointRounding)"/>, AwayFromZero). Hiçbir
    /// queued/building/queued-cycle projenin bilinen bir <c>LastDurationMs</c>'i YOKSA (ortalama hesaplanamaz —
    /// ilk koşu) <c>null</c> döner.
    /// </returns>
    public static long? ComputeRawEstimateMs(
        IReadOnlyList<long?> queuedDurationEstimatesMs,
        IReadOnlyList<BuildingProject> building,
        int parallelism,
        IReadOnlyList<long?> cycleQueuedDurationEstimatesMs)
    {
        ArgumentNullException.ThrowIfNull(queuedDurationEstimatesMs);
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(cycleQueuedDurationEstimatesMs);

        int par = Math.Max(1, parallelism); // savunmacı — sıfır/negatif paralellik sıfıra bölmeye yol açmasın

        // Fallback ortalaması: queued, building VE queued-cycle arasında bilinen (non-null) TÜM süreler
        // [cycle rounds/Task 10] — tek bilinen süre cycle listesinde bile olsa ortalama hesaplanabilir.
        var known = new List<long>();
        foreach (long? d in queuedDurationEstimatesMs) if (d is { } v) known.Add(v);
        foreach (var b in building) if (b.LastDurationMs is { } v) known.Add(v);
        foreach (long? d in cycleQueuedDurationEstimatesMs) if (d is { } v) known.Add(v);
        if (known.Count == 0) return null; // hiçbir yerde bilinen süre yok — ilk koşu, ETA hesaplanamaz

        double average = Average(known);

        double queuedSum = 0;
        foreach (long? d in queuedDurationEstimatesMs) queuedSum += d ?? average;

        double cycleQueuedSum = 0;
        foreach (long? d in cycleQueuedDurationEstimatesMs) cycleQueuedSum += d ?? average;

        double buildingRemaining = 0;
        foreach (var b in building)
        {
            double estimate = b.LastDurationMs ?? average;
            buildingRemaining += Math.Max(0.0, estimate - b.ElapsedMs);
        }

        double raw = (queuedSum + buildingRemaining) / par;
        if (building.Count > 0) raw += BuildingOverheadMs; // [Δ8] bölümden SONRA eklenir, paralelliğe bölünmez
        // [cycle rounds/Task 10] Cycle katkısı paralelliğe BÖLÜNMEZ (sıralı invoke) ve BaselineRounds ile
        // çarpılır (yakınsama için gereken asgari tur sayısı) — tek doğruluk kaynağı CycleRoundPolicy'dir.
        raw += cycleQueuedSum * CycleRoundPolicy.BaselineRounds;

        return RoundToLong(raw);
    }

    /// <summary>
    /// [Δ8] EMA: <c>previousEtaMs</c> yoksa (ilk tick) ham tahmin AYNEN döner; aksi halde
    /// <c>0.75·previous + 0.25·raw</c> (yuvarlanmış).
    /// </summary>
    public static long Smooth(long? previousEtaMs, long rawEstimateMs) =>
        previousEtaMs is null
            ? rawEstimateMs
            : RoundToLong(PreviousWeight * previousEtaMs.Value + RawWeight * rawEstimateMs);

    /// <summary>
    /// Gösterim metni.
    /// <list type="bullet">
    /// <item><paramref name="smoothedEtaMs"/> <c>null</c> ise (<see cref="ComputeRawEstimateMs"/> null döndüğü
    /// için hiç smooth edilecek bir şey yok — ilk koşu/no-history) → <c>"{completed}/{total} · {elapsed}"</c>,
    /// ETA NUMARASI YOK.</item>
    /// <item><paramref name="smoothedEtaMs"/> &lt; <see cref="AlmostDoneThresholdMs"/> → <c>"· almost done"</c>.</item>
    /// <item>Aksi halde en yakın 5 saniyeye yuvarlanmış HAM SANİYE olarak <c>"~Ns left"</c> — design-v1
    /// prototype'ın <c>BuildApp.jsx:761-763</c> davranışının birebir portu; DAKİKAYA ÇEVRİLMEZ (ör. 125000ms →
    /// "~125s left", "~2m 05s left" DEĞİL).</item>
    /// </list>
    /// Tüm formatlama <see cref="CultureInfo.InvariantCulture"/> ile.
    /// </summary>
    public static string FormatDisplay(long? smoothedEtaMs, int completedCount, int totalCount, long elapsedMs)
    {
        if (smoothedEtaMs is null)
            return string.Format(CultureInfo.InvariantCulture, "{0}/{1} · {2}", completedCount, totalCount, DurationFormat.Elapsed(elapsedMs));

        if (smoothedEtaMs.Value < AlmostDoneThresholdMs)
            return "· almost done";

        long roundedTotalSec = RoundToLong(smoothedEtaMs.Value / (double)DisplayRoundingMs) * 5;
        return string.Format(CultureInfo.InvariantCulture, "~{0}s left", roundedTotalSec);
    }

    // [C2/D2 fold] Eski FormatDuration (fmtElapsed portu) SİLİNDİ: DurationFormat.Elapsed ile BİREBİR ikiz
    // mantıktı (ikisi de <60s→"Ns", ≥60s→"Mm SSs"). TEK kanonik yer DurationFormat.Elapsed'dir (Core/Formatting);
    // no-history dalı (FormatDisplay yukarıda) artık onu çağırır. Kopya kaldırıldı (CLAUDE.md).

    private static double Average(List<long> values)
    {
        long sum = 0;
        foreach (long v in values) sum += v;
        return sum / (double)values.Count;
    }

    private static long RoundToLong(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);
}
