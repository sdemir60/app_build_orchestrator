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
/// <para><b>Takibi YALNIZ kullanıcı bırakır.</b> Bir scroll olayı üç şeyden gelebilir: kullanıcının eli, akan
/// içerik, ya da yerleşim (viewport değişimi, yeniden ölçüm, bizim kendi programatik kaydırmamız). Karar
/// bunlardan yalnız BİRİNCİSİNİ dinler — <c>userDriven</c> ham girdiden gelir
/// (<see cref="BottomAnchorBehavior.NotifyUserScroll"/>), geometriden ÇIKARILMAZ.</para>
///
/// <para><b>[DEĞİŞEN KURAL]</b> Eskiden <c>extentHeightChange == 0</c> olan her olay "kullanıcı kaydırdı"
/// sayılır ve dipten uzaklık yeniden hesaplanırdı. Bu yanlıştı: offset'i kullanıcıdan başka şeyler de
/// oynatır ve o an offset henüz güncellenmemişken ölçülen uzaklık eşiği aşabiliyordu — panel kimse
/// dokunmadan takibi bırakıyor, <c>⌄ latest</c> pill'i çıkıyordu (sahada "event stream kendiliğinden focus
/// olmayı bırakıyor"). Uzaklık artık yalnız pill görünürlüğünü ve kullanıcının kendi hareketini yorumlar.</para>
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

    /// <summary>Bir ScrollChanged olayının sonucu. <paramref name="userDriven"/> = bu olay kullanıcının HAM
    /// girdisinden mi doğdu (tekerlek/çubuk/klavye) — geometriden çıkarılmaz, host bildirir.</summary>
    public static BottomAnchorState OnScrollChanged(BottomAnchorState state, double extentHeightChange,
        double distanceFromBottom, bool userDriven, double thresholdPx = DefaultThresholdPx)
    {
        if (state.IsJumping) return state; // uçuştaki programatik atlama SÜRERKEN yeniden-hesap YOK (Ek A-15)
        if (extentHeightChange > 0) return state; // içerik büyüdü — yapışıklık DEĞİŞMEZ, çağıran (stuck ise) tamamlar
        if (!userDriven) return state;            // yerleşim/programatik — takip yalnız KULLANICININ elindedir
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
