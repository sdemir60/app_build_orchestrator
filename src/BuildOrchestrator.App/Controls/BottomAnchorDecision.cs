namespace BuildOrchestrator.App.Controls;

/// <summary>Bir "alta yapışık" scroll'un durumu — <see cref="BottomAnchorDecision"/>'ın SAF (WPF'siz) girdi/çıktısı.</summary>
/// <param name="IsStuck">true = dipte (≤eşik) veya en son dibe programatik atlandı; içerik büyürse dibe yapışır.</param>
/// <param name="IsJumping">true iken bir programatik "dibe git" animasyonu uçuşta — 560ms pencere boyunca hem
/// içerik-büyümesi yakalaması HEM kullanıcı-scroll yeniden hesabı YOK SAYILIR (Ek A-15).</param>
public readonly record struct BottomAnchorState(bool IsStuck, bool IsJumping)
{
    public static BottomAnchorState Initial => new(IsStuck: true, IsJumping: false);
}

/// <summary>
/// [T59] "Alta yapışık scroll" kararının SAF (WPF'siz) çekirdeği — design-v1 §2.5/§2.6 birebir: kullanıcı dipten
/// 48px'ten fazla uzaklaşırsa serbest kalır; ≤48px iken içerik büyümesi dibe yapışık tutar; dip'e programatik bir
/// atlama (pill tıklaması) sırasında 560ms'lik bir "jumping" penceresi hem yeniden-hesabı HEM içerik-büyümesi
/// yakalamasını bastırır ki animasyon kendi scroll event'leriyle YARIŞMASIN (prototip: <c>BuildApp.jsx</c> satır
/// 543-568/665-699 — <c>jumping</c> ref'i + <c>setTimeout(...,560)</c>).
///
/// <para><b>İçerik-büyümesi vs kullanıcı-scroll ayrımı:</b> <c>ScrollChangedEventArgs.ExtentHeightChange &gt; 0</c>
/// = içerik büyüdü (yeni satır eklendi) — bu, "yapışıklığı" DEĞİŞTİRMEZ (zaten yapışıksa çağıran ANINDA dibe
/// tamamlar; değilse serbest kalmaya devam eder). <c>ExtentHeightChange == 0</c> (saf offset değişimi — kullanıcı
/// tekerleği VEYA bizim kendi programatik kaydırmamız) = dipten uzaklık YENİDEN hesaplanır.</para>
/// </summary>
public static class BottomAnchorDecision
{
    /// <summary>design-v1 §2.5: "kullanıcı 48px'ten fazla yukarı kaydırırsa serbest bırakılır".</summary>
    public const double DefaultThresholdPx = 48.0;

    /// <summary>Ek A-15: pill'in "560ms jumping penceresi" — aynı pencere BottomAnchor'ın geçiş guard'ı.</summary>
    public const double JumpingWindowMs = 560.0;

    /// <summary>
    /// Kullanıcı kaydırdıktan sonra "artık o sürmüyor" sayılana kadar geçen süre. Bu süre boyunca hiç
    /// dokunulmazsa panel dibe geri döner ve akışı yeniden izlemeye başlar.
    ///
    /// <para>Kural üç panelde de AYNIDIR ve TEK yerde durur: liste frontier takibini
    /// (<c>StickyLayerList.FrontierIdleResumeMs</c>) bu sabitten alır, konsol ve event stream de dibe
    /// dönüşü. Ayrı sayılar olsaydı üç panel farklı zamanlarda canlanır, ekran huzursuz olurdu.</para>
    ///
    /// <para><b>3 sn değil 5 sn (kullanıcı kararı):</b> derleme sürerken satırlar akmaya devam ettiği için
    /// üç saniye okumaya yetmiyordu — kullanıcı bir şeye bakmak için kaydırdığında panel elinden alınıyordu.
    /// Beş saniye "bıraktım" ile "hâlâ bakıyorum" arasını ayırmaya yetiyor.</para>
    /// </summary>
    public const long IdleResumeMs = 5000;

    /// <summary>Bir ScrollChanged olayının (ya da AvalonEdit için elle izlenen extent farkının) sonucu.</summary>
    public static BottomAnchorState OnScrollChanged(BottomAnchorState state, double extentHeightChange,
        double distanceFromBottom, double thresholdPx = DefaultThresholdPx)
    {
        if (state.IsJumping) return state; // uçuştaki programatik atlama SÜRERKEN yeniden-hesap YOK (Ek A-15)
        if (extentHeightChange > 0) return state; // içerik büyüdü — yapışıklık DEĞİŞMEZ, çağıran (stuck ise) tamamlar
        return state with { IsStuck = distanceFromBottom <= thresholdPx };
    }

    /// <summary>Programatik "dibe git" (pill tıklaması) başlar — hedef dip olduğundan IsStuck iyimser true, ve
    /// 560ms boyunca IsJumping guard'ı devrede.</summary>
    public static BottomAnchorState BeginJump(BottomAnchorState state) => state with { IsStuck = true, IsJumping = true };

    /// <summary>560ms pencere doldu — guard kalkar, dipte kalındığı varsayımı (IsStuck) korunur.</summary>
    public static BottomAnchorState EndJump(BottomAnchorState state) => state with { IsJumping = false };

    /// <summary>`⌄ latest` pill görünürlüğü (Ek A-15, konsol+stream ORTAK kural): dipten &gt;eşik VE uçuşta bir
    /// atlama YOKKEN görünür.</summary>
    public static bool ShouldShowPill(BottomAnchorState state, double distanceFromBottom, double thresholdPx = DefaultThresholdPx)
        => !state.IsJumping && distanceFromBottom > thresholdPx;
}
