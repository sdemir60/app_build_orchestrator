namespace BuildOrchestrator.App.Controls;

/// <summary>[design v1.11.0 §9-5] Bitiş koreografisinin adımları. <see cref="None"/> = koreografi oynamıyor
/// (final görünüm).</summary>
public enum EndStep
{
    None,
    /// <summary>900ms: sarılar gitti, <b>hepsi soluk</b> bekler.</summary>
    Hold,
    /// <summary><b>Neon tutuşma</b>: yalnız bu koşuda derlenenler RANDOM sırayla, floresan lamba gibi
    /// düzensiz titreyerek yanar.</summary>
    Neon,
    /// <summary>700ms nefes.</summary>
    Breath,
    /// <summary>Kalan TÜM griler (atlanan + dokunulmamış) <b>hep birlikte</b> 980ms belirginleşir.</summary>
    Grey,
}

/// <summary>
/// [design v1.11.0 §9-5 · §2.3 "Bitiş koreografisi"] <b>"Neon tutuşma"nın saf çekirdeği.</b>
///
/// <para>Koşu bitince: <c>hold</c> (hepsi soluk) → <c>neon</c> (yalnız DERLENENLER random sırayla titreyerek
/// tutuşur) → nefes → <c>grey</c> (kalan griler hep birlikte belirginleşir) → final.</para>
///
/// <para><b>Koreografi YALNIZ graf düğümlerinde yaşar</b> — proje listesi bitişte sabit kalır (kullanıcı
/// kararı). Derlenen proje yoksa hiç oynamaz.</para>
///
/// <para>Kaynak: <c>prototype/app/build-data.js</c> <c>_runEndFinale</c> (:951-973) ve
/// <c>prototype/app/BuildApp.jsx</c> :484-499 + <c>bo-neon</c> keyframe'i (:40).</para>
/// </summary>
public static class EndFinale
{
    /// <summary>Sarılar gittikten sonraki soluk bekleyiş.</summary>
    public const double HoldMs = 900;
    /// <summary>Bir düğümün neon tutuşması (<c>bo-neon</c>).</summary>
    public const double NeonMs = 1150;
    /// <summary>Neondan sonraki nefes.</summary>
    public const double BreathMs = 700;
    /// <summary>Grilerin birlikte belirginleşme süresi.</summary>
    public const double GreyMs = 980;
    /// <summary>Neon zincirinin node başına ÜST sınırı.</summary>
    public const double MaxStaggerMs = 150;
    /// <summary>Zincirin toplam üst sınırı — 36 projede de ~1.5s'te biter.</summary>
    public const double MaxChainMs = 1500;

    /// <summary>Koreografi boyunca soluk kalanların opaklığı.</summary>
    public const double DimOpacity = 0.16;
    /// <summary>Soluk/parlak arası geçiş (derlenenler için).</summary>
    public const double LitGlideMs = 300;

    /// <summary>Neon zincirinin node başına gecikmesi.</summary>
    public static double StaggerMs(int builtCount) =>
        builtCount > 1 ? Math.Min(MaxStaggerMs, Math.Round(MaxChainMs / (builtCount - 1))) : 0;

    /// <summary>Zincirin toplam uzunluğu (ilk node ile son node arası).</summary>
    public static double ChainMs(int builtCount) => StaggerMs(builtCount) * (builtCount - 1);

    /// <summary>Bir adımın koreografi başlangıcına göre zamanı (ms).</summary>
    public static double StepAtMs(EndStep step, int builtCount)
    {
        double chain = ChainMs(builtCount);
        return step switch
        {
            EndStep.Hold => 0,
            EndStep.Neon => HoldMs,
            EndStep.Breath => HoldMs + chain + NeonMs,
            EndStep.Grey => HoldMs + chain + NeonMs + BreathMs,
            _ => TotalMs(builtCount),
        };
    }

    /// <summary>Koreografinin toplam süresi — sonunda final görünüme (hepsi tam opak) dönülür.</summary>
    public static double TotalMs(int builtCount) =>
        HoldMs + ChainMs(builtCount) + NeonMs + BreathMs + GreyMs + 420;

    /// <summary>Adımlar, oynatılma sırasıyla.</summary>
    public static readonly IReadOnlyList<EndStep> Steps = [EndStep.Hold, EndStep.Neon, EndStep.Breath, EndStep.Grey];

    /// <summary>Neonun random sırası — <see cref="MarkingChoreography.Order"/> ile AYNI üreteç, FARKLI tohum
    /// (iki koreografi aynı koşuda aynı sırayı oynamasın).</summary>
    public static IReadOnlyList<int> Order(int builtCount, int runCount)
    {
        if (builtCount <= 0) return [];
        var indices = new int[builtCount];
        for (int i = 0; i < builtCount; i++) indices[i] = i;

        var rng = MarkingChoreography.Mulberry32(unchecked((uint)(5501 + runCount * 29)));
        for (int i = builtCount - 1; i > 0; i--)
        {
            int j = (int)(rng() * (i + 1));
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        var order = new int[builtCount];
        for (int k = 0; k < builtCount; k++) order[indices[k]] = k;
        return order;
    }

    /// <summary>Bir düğümün opaklığı. <paramref name="built"/> = bu koşuda DERLENDİ (succeeded ∪ failed).</summary>
    public static double Opacity(EndStep step, bool built)
    {
        if (step == EndStep.None) return 1;
        if (built) return step is EndStep.Neon or EndStep.Breath or EndStep.Grey ? 1 : DimOpacity;
        return step == EndStep.Grey ? 1 : DimOpacity;
    }

    /// <summary>O opaklığa giden geçişin süresi: derlenenler kısa (300ms), griler uzun ve BİRLİKTE (980ms).</summary>
    public static double GlideMs(bool built) => built ? LitGlideMs : GreyMs;

    /// <summary>
    /// <c>bo-neon</c> keyframe'leri (BuildApp.jsx:40) — floresan lambanın düzensiz tutuşması.
    /// Değerler <c>(yüzde, opaklık)</c>; süre <see cref="NeonMs"/>.
    /// </summary>
    public static readonly IReadOnlyList<(double Percent, double Opacity)> NeonKeyframes =
    [
        (0.00, 0.12), (0.08, 1.0), (0.16, 0.20), (0.24, 0.90),
        (0.32, 0.14), (0.46, 1.0), (0.56, 0.50), (0.70, 1.0), (1.00, 1.0),
    ];
}
