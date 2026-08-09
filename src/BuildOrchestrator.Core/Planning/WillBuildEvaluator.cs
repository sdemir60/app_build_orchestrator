namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Model;

/// <summary>
/// [T53][A6][v7Δ-8] Pre-run willBuild karar mantığı: dirty=true, güncel=false, imza-yok/pre-Sync=null.
/// Saf karar fonksiyonu — imza hesaplama (BuildSignature, T25) It-3'te; burada yalnız enjekte edilen
/// currentSignature/state üzerinden karar verilir.
///
/// <para><b>[Task 11] BİLİNEN AYRIŞMA — yakınsamayan gruplar (açık bırakıldı).</b> Anahtar AÇIKKEN bu
/// fonksiyon bir SCC üyesini yalnız İMZASINA bakarak değerlendirir. Daha önceki bir Build'de <i>aynı bileşik
/// imzada</i> yakınsamamış (kalıcı kırık) bir grup ise koşuda hiç invoke edilmeden pre-skip edilir
/// (<c>RunCoordinator</c>, reason <c>"cycle did not converge at this signature"</c>).
/// <list type="bullet">
/// <item><b>Kullanıcının gördüğü:</b> o grubun üyeleri üzerinde amber "derlenecek" noktası — ama Build'e
/// basıldığında derlenmezler, "skipped" olarak geçerler. Yön İYİ HUYLUdur: fazla söz verip az teslim eder
/// (bozuk bir şeyi sağlıklı göstermez), bu yüzden bekleyebilir.</item>
/// <item><b>Neden kapatılmadı:</b> kararın kendisi GRUP düzeyinde bir predicate'tir ve iki girdisi de
/// Core'da YOKTUR — SCC üyeliğinin tamamı (<c>plan.Cycles</c> üzerinde <c>All</c>) ve
/// <c>BuildStateStore</c>'daki yakınsamama hafızası. Bu fonksiyon düğüm başına, saf ve state-store'suz
/// çalışır. Hafızayı buraya taşımak, bir koşu-zamanlama kuralını imza-tabanlı önizleme yoluna İKİNCİ kez
/// yazmak olurdu (tek doğruluk kaynağı ihlali).</item>
/// <item><b>Kapatmak için gereken:</b> önizlemeyi düğüm başına değil GRUP başına hesaplayan bir adım —
/// <c>BuildPreview</c>'in SCC'leri tek kalem olarak ele alması ve yakınsamama hafızasının önizleme
/// katmanına bir okuma dikişi (seam) olarak verilmesi; ya da simetrik olarak, pre-skip kararının
/// Supervisor'dan Core'a taşınması. İkisi de bu görevin kapsamı dışındadır.</item>
/// <item><b>İKİNCİ (çok daha dar) ayrışma, aynı kökten:</b> bir SCC'nin üyeleri KISMEN temiz olduğunda —
/// bileşik imza ortak olduğu için pratikte yalnız bir üyenin state kaydı hiç yokken — koordinatörün grup
/// kapısı (<c>All</c>) düşer, grup bütün olarak dispatch edilir ve önizlemenin GRİ çizdiği temiz üyeler de
/// derlenir. <c>Any</c>'ye gevşetmek bunu kapatmaz, DAHA KÖTÜSÜNÜ yapar: hiç derlenmemiş üyeleri de atlayıp
/// grubu yarım bırakırdı — yani <c>All</c> doğru quantifier'dır ve bu artık onun bilinen bedelidir.</item>
/// </list></para>
/// </summary>
public static class WillBuildEvaluator
{
    /// <param name="buildCycles">Kill switch: cycle üyeleri turlarla derleniyor mu. Kapalıyken cycle üyesi
    /// her zaman "derlenmeyecek" sayılır (eski davranış). VARSAYILAN DEĞER YOK — her çağıran açıkça geçer,
    /// böylece yeni bir çağrı yeri sessizce eski davranışa düşemez.</param>
    public static bool? Evaluate(bool inCycle, string? currentSignature, BuildState? state, bool buildCycles)
    {
        if (inCycle && !buildCycles) return false;                     // anahtar kapalı: cycle projesi derlenmez
        if (currentSignature is null) return null;                     // hollow: imza hesaplanamadı / Sync öncesi
        if (state?.BuiltSignature is null) return true;                // hiç başarıyla derlenmemiş
        if (state.LastResult != BuildResult.Succeeded) return true;    // son koşu başarısız/skip
        return !string.Equals(currentSignature, state.BuiltSignature, StringComparison.Ordinal); // dirty=true, güncel=false
    }
}
