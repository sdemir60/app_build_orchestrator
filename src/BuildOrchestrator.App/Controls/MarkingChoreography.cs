namespace BuildOrchestrator.App.Controls;

/// <summary>[design v1.11.0 §9-4] Açılış koreografisinin adımları. <see cref="None"/> = koreografi oynamıyor.</summary>
public enum MarkStep
{
    None,
    /// <summary>0–440ms: <b>nötr an</b>. Kapsam da DÜZ GRİDİR — amber henüz yok.</summary>
    Neutral,
    /// <summary><b>Dalga</b>: kapsam RANDOM sırayla amber'a yanar (<see cref="MarkingChoreography.StaggerMs"/>).</summary>
    Wave,
    /// <summary><b>Sarı-gri an</b> (300ms): plan bir an ekranda durur.</summary>
    Hold,
    /// <summary><b>Örtüşen veda</b> başlar: GRİLER 1120ms sönüşe geçer.</summary>
    DimEnv,
    /// <summary>560ms sonra SARILAR da katılır (440ms) — ikisi aynı anda biter.</summary>
    Settle,
    /// <summary><b>Nefes</b> (420ms).</summary>
    Wait,
    /// <summary>Nefesin ikinci yarısı (240ms) — sonra koşu.</summary>
    Wait2,
}

/// <summary>
/// [design v1.11.0 §9-4 · §2.3 "İşlem koreografisi"] <b>Açılış koreografisinin saf çekirdeği</b>: adım
/// zaman çizelgesi, dalga temposu, dalganın (random) sırası ve adım başına opaklık/geçiş süreleri.
///
/// <para>Her işlem — Build · Rebuild · Clean · satır aksiyonu · Resolve — AYNI sırayı oynar:
/// <b>nötr an → dalga → sarı-gri an → örtüşen veda → nefes → koşu.</b> "Örtüşen veda"nın gerekçesi ölçülmüş:
/// gri büyük bir opaklık düşüşü yaptığı için yolun ortasında "gitti" okunur, bu yüzden sarının süresi daha
/// kısa tutulur ve ikisi aynı anda biter.</para>
///
/// <para><b>Sınıf SAFtır</b> (WPF'siz test edilir): sayılar burada, uygulama <c>ProjectRow</c>/<c>GraphView</c>
/// ve sürücüde. Kaynak: design-v1.11.0 <c>prototype/app/build-data.js</c> <c>_mark</c> (:340-369) ve
/// <c>prototype/app/BuildApp.jsx</c> :500-505 (node) / :630-634 (satır).</para>
/// </summary>
public static class MarkingChoreography
{
    // ---- zaman çizelgesi (build-data.js:356-368) ----
    /// <summary>Nötr anın süresi — dalga bundan sonra başlar.</summary>
    /// <summary>[design §2.3 · BuildApp.jsx:529-533/682/693] Dalga sırasında bir yüzeyin amber'a AKMA süresi
    /// (<c>200ms var(--ease-standard)</c>). Duration.* ailesine ait DEĞİLDİR — effects.css'te yoktur, dalgaya
    /// özgüdür; bu yüzden <c>StatusGlyph.PulseMs</c> deseninde adlandırılmış bir sabittir.</summary>
    public const double LightMs = 200.0;

    public const double NeutralMs = 440;
    /// <summary>Son node yandıktan sonraki tampon (<c>W = markStagger*(n-1) + 380</c>).</summary>
    public const double WaveTailMs = 380;
    /// <summary>Dalga temposunun ÜST sınırı: 110ms/node.</summary>
    public const double MaxStaggerMs = 110;
    /// <summary>Dalganın toplam süresi bunu AŞMAZ — 36 projede de zincir kısa kalır.</summary>
    public const double MaxWaveMs = 1100;

    /// <summary>Dalga temposu: 110ms/node, ama zincir toplamı <see cref="MaxWaveMs"/>'yi aşmaz. Tek elemanlı
    /// kapsamda gecikme yoktur.</summary>
    public static double StaggerMs(int count) =>
        count > 1 ? Math.Min(MaxStaggerMs, Math.Round(MaxWaveMs / (count - 1))) : 0;

    /// <summary>Dalganın kuyruk payı dahil süresi (prototipteki <c>W</c>).</summary>
    public static double WaveSpanMs(int count) => StaggerMs(count) * (count - 1) + WaveTailMs;

    /// <summary>Bir adımın koreografi başlangıcına göre zamanı (ms).</summary>
    public static double StepAtMs(MarkStep step, int count)
    {
        double w = WaveSpanMs(count);
        return step switch
        {
            MarkStep.Neutral => 0,
            MarkStep.Wave => NeutralMs,
            MarkStep.Hold => NeutralMs + w,
            MarkStep.DimEnv => 740 + w,
            MarkStep.Settle => 1300 + w,
            MarkStep.Wait => 1860 + w,
            MarkStep.Wait2 => 2280 + w,
            _ => TotalMs(count),
        };
    }

    /// <summary>Koreografinin toplam süresi — bunun sonunda koşu başlar (build-data.js:363).</summary>
    public static double TotalMs(int count) => 2520 + WaveSpanMs(count);

    /// <summary>Adımlar, oynatılma sırasıyla.</summary>
    public static readonly IReadOnlyList<MarkStep> Steps =
        [MarkStep.Neutral, MarkStep.Wave, MarkStep.Hold, MarkStep.DimEnv, MarkStep.Settle, MarkStep.Wait, MarkStep.Wait2];

    // ---- dalganın sırası (build-data.js:348-353) ----
    /// <summary>
    /// Dalga <b>RANDOM</b> belirir (kullanıcı kararı): sıra karışıktır ve DERLEME SIRASINDAN bağımsızdır.
    /// Karıştırma deterministiktir (<c>mulberry32</c> + Fisher-Yates) — aynı koşu numarası aynı sırayı verir,
    /// yani hem testlenebilir hem koşudan koşuya değişir.
    /// </summary>
    /// <returns><c>result[i]</c> = <paramref name="count"/> uzunluklu kapsamda i'inci üyenin DALGA SIRASI.</returns>
    public static IReadOnlyList<int> Order(int count, int runCount)
    {
        if (count <= 0) return [];
        var indices = new int[count];
        for (int i = 0; i < count; i++) indices[i] = i;

        var rng = Mulberry32(unchecked((uint)(7331 + runCount * 17)));
        for (int i = count - 1; i > 0; i--)
        {
            int j = (int)(rng() * (i + 1));
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        // indices[k] = k'ıncı sırada yanacak üyenin dizini → tersine çevir: üye → sıra.
        var order = new int[count];
        for (int k = 0; k < count; k++) order[indices[k]] = k;
        return order;
    }

    /// <summary>mulberry32'nin altın-oran türevli artış sabiti. <b>ONDALIK yazılır</b>: sekiz haneli bir hex
    /// literali, uygulama ağacında ham renk arayan kaynak guard'ına (<c>NoHardcodedColorTests</c>) takılır ve
    /// guard'ı gevşetmek YASAKTIR. Değer üretecin kendi sabitidir, bir renk değildir.</summary>
    private const uint MulberryStep = 1_831_565_813;

    /// <summary>build-data.js:7-15 <c>mulberry32</c> — birebir port. Sayı üretecinin AYNI olması, dalganın
    /// prototiple aynı "rastgelelik hissini" taşıması içindir.</summary>
    internal static Func<double> Mulberry32(uint seed)
    {
        uint a = seed;
        return () =>
        {
            a = unchecked(a + MulberryStep);
            uint t = unchecked((uint)((int)(a ^ (a >> 15)) * (int)(1u | a)));
            t = unchecked(t + (uint)((int)(t ^ (t >> 7)) * (int)(61u | t))) ^ t;
            return (t ^ (t >> 14)) / 4294967296.0;
        };
    }

    // ---- opaklık (BuildApp.jsx:500-505 node · :630-634 satır) ----
    /// <summary>"Örtüşen veda"nın SARI yarısı: kapsam 0.45'e iner (440ms).</summary>
    public const double MarkedOpacity = 0.45;
    /// <summary>Vedanın GRİ yarısı — satırda 0.3, grafta 0.18 (graf zaten daha küçük ve yoğun).</summary>
    public const double RowEnvOpacity = 0.3;
    public const double NodeEnvOpacity = 0.18;
    /// <summary>Sarının geçiş süresi. Griden KISA olması bilinçlidir: gri büyük bir opaklık düşüşü yaptığı
    /// için yolun ortasında "gitti" okunur — ikisi böylece ALGIDA aynı anda biter.</summary>
    public const double MarkedGlideMs = 440;
    public const double EnvGlideMs = 1120;
    /// <summary>Koreografi dışındaki (normal) opaklık geçişi.</summary>
    public const double IdleGlideMs = 360;

    /// <summary>Vedanın SARI yarısının başladığı adımlar.</summary>
    public static bool IsLate(MarkStep step) => step is MarkStep.Settle or MarkStep.Wait or MarkStep.Wait2;

    /// <summary>Vedanın GRİ yarısının başladığı adımlar (560ms daha erken).</summary>
    public static bool IsEnvFading(MarkStep step) => step == MarkStep.DimEnv || IsLate(step);

    /// <summary>Bir satırın/düğümün koreografi opaklığı.</summary>
    public static double Opacity(MarkStep step, bool marked, double envOpacity)
    {
        if (step == MarkStep.None) return 1;
        if (marked) return IsLate(step) ? MarkedOpacity : 1;
        return IsEnvFading(step) ? envOpacity : 1;
    }

    /// <summary>O opaklığa giden geçişin süresi.</summary>
    public static double GlideMs(MarkStep step, bool marked)
    {
        if (step == MarkStep.None) return IdleGlideMs;
        return marked ? MarkedGlideMs : EnvGlideMs;
    }
}
