namespace BuildOrchestrator.Core.Planning;

/// <summary>Bir SCC turu bittikten sonraki karar.</summary>
public enum CycleRoundDecision
{
    /// <summary>Bir tur daha.</summary>
    Continue,
    /// <summary>İki ardışık yeşil tur — tüm üyeler nihai API'lere karşı derlendi.</summary>
    Converged,
    /// <summary>Tur eklemek sonucu değiştiremez: ya aynı küme iki turdur patlıyor (klasik kanıt) ya da
    /// patlayan bir üyenin okuduğu grup-içi yüzeyler oturmuş (yüzey kanıtı — tur 1'de bile verilebilir).</summary>
    NoProgress,
    /// <summary>Tavana dayanıldı; çıktılar bir kuşak geride olabilir.</summary>
    CapReached,
}

/// <summary>
/// SCC tur döngüsünün durma kuralı. SAF: I/O, saat, log YOK [D3].
///
/// Neden tek yeşil tur yetmez: turlar arasında KAYNAK DEĞİŞMEZ, ama tur 1'de A diskteki ESKİ B.dll'e karşı
/// derlenir. Yeşil geçse bile A.dll eski imzaya bağlanmış olabilir (çalışma anında MissingMethodException).
/// Tur 1 her üyenin public API'sini nihaileştirir; tur 2 herkesi nihai API'lere karşı yeniden derler.
/// Bu yüzden yüzey kanıtı YOKKEN yakınsama ölçütü İKİ ARDIŞIK yeşil turdur — ve YALNIZ kanıtsızken: kural her
/// turun herkesi derlediği varsayımına dayanır, kanıt varken sonraki turlar yalnız bayat üyeleri derler.
///
/// [API kısa devresi] <c>staleNow</c> verildiğinde aynı iddia KANITLA ve daha erken kurulur, yakınsamanın TEK
/// ölçütü de odur: kaynak sabitken bir üyenin sonucunu yalnız okuduğu grup-içi yüzeyin değişmesi değiştirebilir.
/// Herkes yeşil + kimse bayat değil ⇒ herkes nihai API'lere bağlandı ⇒ tur 1'de bile Converged. İki ardışık yeşil
/// tur kanıt varken YETMEZ: tur 2'de derlenen bir üyenin yüzeyi başka bir üyenin okuduğu sabiti oynatabilir
/// (A'nın sabiti B'nin yüzeyine girer ⇒ B oynar, A bayat kalır); yeşil-yeşil sonrası Converged denseydi A eski
/// yüzeyiyle güvenilir persist edilirdi. Bayat üye tavana kadar kalırsa CapReached. Patlayan üyenin girdisi
/// değişmedi ⇒ aynı derleme aynı hatayı verir; grup hep birlikte persist ettiğinden tek kanıtlı-umutsuz üye
/// grubun kaderidir ⇒ tur 1'de bile NoProgress. Kanıt yarımsa (staleNow null) eski kurallar tek başına geçerlidir.
///
/// Neden tavan 3 yeterli: kanıtsız kurallarda tur 1-2 yeşilse Converged zaten 2'de olur; tur 1-2 aynı kümede
/// patlarsa NoProgress 2'de durur. 3. tur yalnız "tur 1 patladı, sonra düzeldi" dalı için vardır. Kanıtla bayat
/// bir üyenin peşinden koşan turlar da aynı bütçeye tabidir. Turlar diskteki duruma göre idempotent olduğu için
/// düşük tavan bilgi kaybettirmez — sonraki Cycles koşusu kaldığı yerden devam eder.
/// </summary>
public static class CycleRoundPolicy
{
    /// <summary>Bir SCC için tek bir run'da yürütülecek azami tur sayısı.</summary>
    public const int RoundCap = 3;

    /// <summary>Yakınsama için gereken asgari tur sayısı (iki ardışık yeşil).</summary>
    public const int BaselineRounds = 2;

    /// <param name="round">Biten turun 1-tabanlı numarası.</param>
    /// <param name="failedNow">Bu turda derlemesi başarısız olan üyeler.</param>
    /// <param name="failedPrevious">Bir önceki turunki; ilk turda <c>null</c>.</param>
    /// <param name="staleNow">[API kısa devresi] Son derlemesinin OKUDUĞU grup-içi çıktı yüzeyi bu tur sonunda
    /// DEĞİŞMİŞ olan üyeler — yani bir tur daha derlemenin sonucunu değiştirebileceği üyeler. <c>null</c> ⇒
    /// yüzey bilgisi yok (eski kurallar tek başına geçerli).</param>
    public static CycleRoundDecision Decide(int round, IReadOnlySet<string> failedNow,
                                            IReadOnlySet<string>? failedPrevious,
                                            IReadOnlySet<string>? staleNow = null)
    {
        ArgumentNullException.ThrowIfNull(failedNow);

        // Sıra ÖNEMLİ: Converged, NoProgress'ten ve tavandan ÖNCE değerlendirilir.
        // İki ardışık yeşil tur YALNIZ yüzey kanıtı YOKKEN yakınsamadır: kanıt varken tur ≥ 2 yalnız bayat üyeleri
        // derler ve derlenen bir üyenin yüzeyi başka bir üyenin okuduğu sabiti oynatabilir — yeşil-yeşil, bayat kalan
        // bir üyenin nihai API'ye bağlandığını göstermez. Kanıt varken yakınsamayı yalnız aşağıdaki kural söyler.
        if (staleNow is null && round >= BaselineRounds && failedPrevious is not null
            && failedNow.Count == 0 && failedPrevious.Count == 0)
            return CycleRoundDecision.Converged;

        // [API kısa devresi] Yüzey kanıtı varken yakınsamanın TEK ölçütü: kaynak turlar arasında değişmez,
        // dolayısıyla bir üyenin sonucunu yalnız OKUDUĞU grup-içi yüzeyin değişmesi değiştirebilir. Herkes yeşil ve
        // kimse bayat bağlanmamışken tur eklemek hiçbir şeyi değiştiremez ⇒ Converged (tur 1'de bile).
        if (staleNow is not null && failedNow.Count == 0 && staleNow.Count == 0)
            return CycleRoundDecision.Converged;

        // Aynı kanıtın karanlık yüzü: girdisi DEĞİŞMEMİŞ bir üyeye aynı derleme aynı hatayı verir. Grup hep
        // birlikte persist ettiği için tek bir kanıtlı-umutsuz üye grubun kaderidir — diğer üyelerin hâlâ
        // düzelebilecek olması sonucu değiştirmez, koşu ERKEN keser (kullanıcı "olmayacaksa devam etme" der).
        if (staleNow is not null && failedNow.Count > 0)
            foreach (string member in failedNow)
                if (!staleNow.Contains(member))
                    return CycleRoundDecision.NoProgress;

        // Boş küme "aynı hata kümesi" DEĞİLDİR: kanıt varken bayat üyeli yeşil-yeşil turlar buraya düşer ve
        // ilerlemesizlik sayılmaz — bir tur daha ya da tavan.
        if (round >= BaselineRounds && failedPrevious is not null && failedNow.Count > 0
            && failedNow.SetEquals(failedPrevious))
            return CycleRoundDecision.NoProgress;

        return round >= RoundCap ? CycleRoundDecision.CapReached : CycleRoundDecision.Continue;
    }
}
