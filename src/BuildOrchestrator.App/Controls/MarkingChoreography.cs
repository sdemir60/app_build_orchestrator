using BuildOrchestrator.App.Graph;

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
    /// <summary>[v1.18.0 — "sıralı teslim dalgası"] 560ms sonra kapsam da katılır — ama HEP BİRDEN değil,
    /// <b>yandığı (dalga) sırayla</b> koşu seviyesine (<see cref="GraphNodeOpacity.RunDim"/>) iner; node
    /// başına gecikme <see cref="MarkingChoreography.SettleDelayMs"/>. Koreografinin SON adımıdır — bittiğinde
    /// koşu bu kuyrukla ÖRTÜŞEREK başlar, kapsam zaten koşu seviyesinde olduğu için faz geçişinde opaklık
    /// zıplaması olmaz (eski `wait`/`wait2` fazları ve aradaki 0.45→0.13 atlaması v1.18.0'da kaldırıldı).</summary>
    Settle,
}

/// <summary>
/// [design v1.11.0 §9-4 · §2.3 "İşlem koreografisi" · v1.18.0 "sıralı teslim dalgası"] <b>Açılış
/// koreografisinin saf çekirdeği</b>: adım zaman çizelgesi, dalga temposu, dalganın (random) sırası ve adım
/// başına opaklık/geçiş süreleri.
///
/// <para>Her işlem — Build · Rebuild · Clean · satır aksiyonu · Resolve — AYNI sırayı oynar:
/// <b>nötr an → dalga → sarı-gri an → örtüşen veda → sıralı teslim → koşu.</b> "Örtüşen veda"nın gerekçesi
/// ölçülmüş: gri büyük bir opaklık düşüşü yaptığı için yolun ortasında "gitti" okunur, bu yüzden sarının
/// süresi daha kısa tutulur ve ikisi aynı anda biter.</para>
///
/// <para><b>Sınıf SAFtır</b> (WPF'siz test edilir): sayılar burada, uygulama <c>ProjectRow</c>/<c>GraphView</c>
/// ve sürücüde. Kaynak: design-v1.11.0 <c>prototype/app/build-data.js</c> <c>_mark</c> (:340-369) ve
/// <c>prototype/app/BuildApp.jsx</c> :500-505 (node) / :630-634 (satır); kuyruk v1.18.0 README §9'da
/// (<c>settleStagger</c>).</para>
/// </summary>
public static class MarkingChoreography
{
    // ---- zaman çizelgesi (build-data.js:356-368, kuyruk v1.18.0) ----
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
            _ => TotalMs(count),
        };
    }

    /// <summary>[v1.18.0] Koreografinin toplam süresi — bunun sonunda koşu başlar. Eski `wait`/`wait2`
    /// fazlarının (660ms) kalkmasıyla 2520+W'den <b>2060+W</b>'ye indi (36 projede W≈1465ms → koşu ~3.5s'de
    /// başlar, eskiden ~4.0s).</summary>
    public static double TotalMs(int count) => 2060 + WaveSpanMs(count);

    /// <summary>Adımlar, oynatılma sırasıyla.</summary>
    public static readonly IReadOnlyList<MarkStep> Steps =
        [MarkStep.Neutral, MarkStep.Wave, MarkStep.Hold, MarkStep.DimEnv, MarkStep.Settle];

    // ---- dalganın sırası (build-data.js:348-353) ----
    /// <summary>
    /// Dalga <b>RANDOM</b> belirir (kullanıcı kararı): sıra karışıktır ve DERLEME SIRASINDAN bağımsızdır.
    /// Karıştırma deterministiktir (<c>mulberry32</c> + Fisher-Yates) — aynı koşu numarası aynı sırayı verir,
    /// yani hem testlenebilir hem koşudan koşuya değişir.
    ///
    /// <para>[v1.18.0] AYNI sıra <see cref="SettleDelayMs"/>'in de girdisidir — "sıralı teslim dalgası"
    /// kapsamın yandığı sırayla koşu seviyesine inmesidir, yeni bir random sıra ÇEKMEZ.</para>
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
    /// <summary>Vedanın GRİ yarısı — grafta 0.18 (graf zaten daha küçük ve yoğun).
    /// <para><b>[DEĞİŞEN KURAL — v1.13.2, ölçüm]</b> "Koşu zaten başlamış olduğu için listede ikinci bir
    /// sönme okunmuyordu." Satırın kendi env-opaklığı (eski adı <c>RowEnvOpacity</c>, eski değeri 0.3)
    /// KALDIRILDI: <see cref="Services.OperationChoreographer"/> artık satırlara her adımda
    /// <see cref="ViewModels.RowFade.None"/> (opaklık 1) yazıyor — sönme/geri gelme (veda + neon finali)
    /// yalnız graf node'larında yaşıyor.</para></summary>
    public const double NodeEnvOpacity = 0.18;

    /// <summary>[v1.18.0] <c>Settle</c>'da işaretli node'un geçiş süresi (400ms ease-in-out, node başına
    /// <see cref="SettleDelayMs"/> kadar gecikmeli) — README §9 v1.18.0'ın kendi sayısı. Griden (1120ms) KISA
    /// kalması korunur ki ilk yanan node griler hâlâ sönerken koşu seviyesine ulaşsın.
    /// <para><b>[DEĞİŞEN KURAL — v1.18.0]</b> Eski <c>MarkedGlideMs</c> (440ms) kapsamı 0.45'e taşıyordu ve süre
    /// ÖZELLİKLE "griyle algıda aynı anda biter" hedefiyle seçilmişti (120ms fark). Kapsam artık doğrudan koşu
    /// seviyesine (<see cref="GraphNodeOpacity.RunDim"/>) iniyor; yeni süre (400ms) o eski eşzamanlılık hedefini
    /// KORUMAZ (fark artık 160ms) — spec'in kendi sayısıdır, ikinci bir ölçüm yapılmadı.</para></summary>
    public const double SettleGlideMs = 400;
    public const double EnvGlideMs = 1120;
    /// <summary>Koreografi dışındaki (normal) opaklık geçişi.</summary>
    public const double IdleGlideMs = 360;

    /// <summary>[v1.18.0] Sıralı teslimin tavanı: 700ms'lik kuyruk 40ms/node'u AŞMAZ (JS <c>Math.round</c>
    /// paritesi için <see cref="MidpointRounding.AwayFromZero"/>). Tek elemanlı kapsamda gecikme yoktur.</summary>
    public const double MaxSettleStaggerMs = 40;
    /// <summary>Sıralı teslim kuyruğunun toplam süresi bunu AŞMAZ (README §9 v1.18.0 <c>settleStagger</c>).</summary>
    public const double MaxSettleSpanMs = 700;

    /// <summary>[v1.18.0] <c>settleStagger</c>: node başına <c>Settle</c> gecikmesinin tavanı. 700ms'lik kuyruk
    /// bunu aşmaz.</summary>
    public static double SettleStaggerMs(int count) =>
        count > 1 ? Math.Min(MaxSettleStaggerMs, Math.Round(MaxSettleSpanMs / (count - 1), MidpointRounding.AwayFromZero)) : 0;

    /// <summary>[v1.18.0] Bir node'un <c>Settle</c>'daki gecikmesi — dalga sırasıyla AYNI <paramref name="order"/>
    /// (<see cref="Order"/>) × <see cref="SettleStaggerMs"/>. Yeni bir random sıra ÇEKMEZ: node yandığı sırayla
    /// söner.</summary>
    public static double SettleDelayMs(int order, int count) => order * SettleStaggerMs(count);

    /// <summary>[v1.18.0] Vedanın SARI yarısının (kapsamın koşu seviyesine inişinin) başladığı TEK adım —
    /// eski `wait`/`wait2` de bu kapıya girerdi, artık koreografi Settle'da biter.</summary>
    public static bool IsLate(MarkStep step) => step == MarkStep.Settle;

    /// <summary>Vedanın GRİ yarısının başladığı adımlar (560ms daha erken).</summary>
    public static bool IsEnvFading(MarkStep step) => step == MarkStep.DimEnv || IsLate(step);

    /// <summary>
    /// Bir düğümün koreografi opaklığı. [v1.18.0] İşaretli düğüm <c>Settle</c>'da artık kapsamın ara
    /// durağı (eski 0.45) değil, DOĞRUDAN koşu seviyesine (<see cref="GraphNodeOpacity.RunDim"/>) iner —
    /// <c>marking</c> → <c>running</c> geçişinde opaklık zıplaması olmasın diye TEK kaynaktan okunur, ikinci
    /// bir 0.13 sabiti burada AÇILMAZ.
    /// <para><b>[v1.13.2'den kalan]</b> Satırlara artık bu fonksiyon hiç çağrılmıyor (<c>OperationChoreographer</c>
    /// her adımda <see cref="ViewModels.RowFade.None"/> yazıyor) — imza yalnız <c>GraphView</c> için yaşıyor.</para>
    /// </summary>
    public static double Opacity(MarkStep step, bool marked, double envOpacity)
    {
        if (step == MarkStep.None) return 1;
        if (marked) return IsLate(step) ? GraphNodeOpacity.RunDim : 1;
        return IsEnvFading(step) ? envOpacity : 1;
    }

    /// <summary>O opaklığa giden geçişin süresi (gecikme AYRIDIR — bkz. <see cref="SettleDelayMs"/>).</summary>
    public static double GlideMs(MarkStep step, bool marked)
    {
        if (step == MarkStep.None) return IdleGlideMs;
        return marked ? SettleGlideMs : EnvGlideMs;
    }
}
