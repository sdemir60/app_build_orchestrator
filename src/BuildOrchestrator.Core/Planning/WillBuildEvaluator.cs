namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Model;

/// <summary>
/// [T53][A6][v7Δ-8] Pre-run willBuild karar mantığı: dirty=true, güncel=false, imza-yok/pre-Sync=null.
/// Saf karar fonksiyonu — imza hesaplama (BuildSignature, T25) It-3'te; burada yalnız enjekte edilen
/// currentSignature/state üzerinden karar verilir.
///
/// <para><b>SCC üyeleri ve <paramref name="buildCycles"/>.</b> Bir SCC'yi derleyen tek koşu <c>RunMode.Cycles</c>'tır;
/// Build/Rebuild üyeleri <c>"in dependency cycle"</c> ile atlar. Bu yüzden bayrak bir kullanıcı tercihi DEĞİL,
/// koşunun kapsamının yansımasıdır: Sync'in önizlemesi ve Build'in planlaması <c>false</c> geçer (üye her zaman
/// "derlenmeyecek"), Cycles koşusu <c>true</c> geçer ve üyeler sıradan imza/state mantığına tabi olur —
/// SCC'nin bileşik imzası tüm üyeler için ORTAK olduğundan grup ya bütün olarak "derlenecek" ya bütün olarak
/// "güncel" görünür.</para>
///
/// <para><b>Bağımlılığı başarısız olan başarı (<see cref="BuildState.DepIssue"/>).</b> Böyle bir proje
/// derlendi ama bağımlılığının BAYAT çıktısına link'lidir; "başarılı" olması binary'nin güncel olduğu
/// anlamına gelmez. Bağımlılık kaynak DEĞİŞMEDEN düzelirse (ör. zehirli obj temizliği) bu projenin imzası
/// da değişmez — not olmasaydı bir sonraki Build onu "güncel" sayıp atlar ve proje sonsuza dek bayat
/// binary'e link'li kalırdı. §4 gereği DLL/bin timestamp'i OKUNMADIĞI için bunu yakalayacak başka bir
/// mekanizma yoktur.</para>
///
/// <para>Bu güvenlik eskiden koordinatörde, "böyle bir başarıyı deftere HİÇ yazma" biçiminde duruyordu.
/// Ölçüldü ki o kural defterin ilerlemesini tamamen durduruyor: depIssue zincir boyunca miras alındığı
/// için birkaç gerçek hata tüm grafı zehirliyor (bir koşuda 24 hata → 96 proje → 74 başarının 0'ı
/// yazıldı) ve incremental derleme fiilen devre dışı kalıyordu. Kayıt artık yazılıyor, karar BURADA
/// veriliyor — yeniden derlenecek küme aynı, ama defter ve kartın sha çifti gerçeği söylüyor.</para>
///
/// <para><b>Bilinen dar ayrışma (Cycles koşusu içinde):</b> bir SCC'nin üyeleri KISMEN temiz olduğunda —
/// bileşik imza ortak olduğu için pratikte yalnız bir üyenin state kaydı hiç yokken — koordinatörün grup
/// kapısı (<c>All</c>) düşer, grup bütün olarak dispatch edilir ve önizlemenin GRİ çizdiği temiz üyeler de
/// derlenir. <c>Any</c>'ye gevşetmek bunu kapatmaz, DAHA KÖTÜSÜNÜ yapar: hiç derlenmemiş üyeleri de atlayıp
/// grubu yarım bırakırdı — yani <c>All</c> doğru quantifier'dır ve bu onun bilinen bedelidir.</para>
/// </summary>
public static class WillBuildEvaluator
{
    /// <param name="buildCycles">Bu koşu SCC üyelerini derliyor mu (yalnız <c>RunMode.Cycles</c>). <c>false</c>
    /// iken cycle üyesi her zaman "derlenmeyecek" sayılır. VARSAYILAN DEĞER YOK — her çağıran kararı açıkça
    /// yazar, böylece yeni bir çağrı yeri sessizce yanlış kapsama düşemez.</param>
    public static bool? Evaluate(bool inCycle, string? currentSignature, BuildState? state, bool buildCycles)
        => EvaluateWithReason(inCycle, currentSignature, state, buildCycles).WillBuild;

    /// <summary>
    /// <see cref="Evaluate"/> ile AYNI karar, ek olarak GEREKÇESİ. Karar buradadır ve <see cref="Evaluate"/>
    /// buna delege eder — iki yüzey sessizce ayrışamaz (kopya YASAK).
    ///
    /// <para>Gerekçe kullanıcıya gösterilir (will-build noktasının tooltip'i): kart üstünde nokta (plan) ile
    /// sha çifti (commit) yan yana durduğu için "commit aynı ama neden derlenecek?" sorusu doğuyordu; cevabı
    /// motor biliyor ama eskiden IPC sınırında düşüyordu.</para>
    ///
    /// <para>İki hâlde gerekçe YOKTUR: hollow (bilinmiyor) ve kapsam-dışı cycle üyeliği — ikincisinde üyelik
    /// kanalı (döngü rozeti) zaten konuşur ve bir plan gerekçesi orada yanıltıcı olurdu.</para>
    /// </summary>
    public static (bool? WillBuild, WillBuildReason? Reason) EvaluateWithReason(
        bool inCycle, string? currentSignature, BuildState? state, bool buildCycles)
    {
        if (inCycle && !buildCycles) return (false, null);                 // kapsam dışı: cycle projesi derlenmez
        if (currentSignature is null) return (null, null);                 // hollow: imza yok / Sync öncesi
        if (state?.BuiltSignature is null) return (true, WillBuildReason.NeverBuilt);
        if (state.LastResult != BuildResult.Succeeded) return (true, WillBuildReason.LastFailed);
        if (state.DepIssue) return (true, WillBuildReason.DepIssue);       // bayat bağımlılığa link'li (yukarıdaki nota bak)
        return string.Equals(currentSignature, state.BuiltSignature, StringComparison.Ordinal)
            ? (false, WillBuildReason.UpToDate)
            : (true, WillBuildReason.SignatureChanged);
    }
}
