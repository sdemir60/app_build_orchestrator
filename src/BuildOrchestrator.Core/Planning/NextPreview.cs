namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Model;

/// <summary>
/// App'in CANLI geçişleri: bir koşu olayı ya da kullanıcı eylemi bir satırın kaydını değiştirdiği ANDA satır,
/// motorun bir sonraki önizlemesini (bir Sync'e kadar gelmeyebilir) beklemeden ne göstermeli? Cevap o
/// önizlemenin (<see cref="WillBuildEvaluator"/> + <see cref="ConditionalRebuild.AppliesTo"/>) AYNEN kendisidir;
/// App kendi kopyasını TÜRETMEZ, burayı sorar. Eşlemeler saf ve tek yerdedir (kopya YASAK): her biri, motorun
/// o anda deftere yazdığı kaydın <see cref="WillBuildEvaluator"/>'da neye düştüğünü söyler — satır ile bir
/// sonraki Sync ayrışmaz.
/// </summary>
public static class NextPreview
{
    /// <summary>
    /// Proje BU KOŞUDA başarıyla bitti. Üçü BİRLİKTE döner çünkü üçü de AYNI kayıttan (bu başarının deftere ne
    /// yazdığından) türer ve ayrı ayrı sorulursa sessizce ayrışabilirler.
    ///
    /// <para><b>Güvenilmez başarı (<paramref name="trusted"/> <c>false</c>):</b> yakınsamayan (tavana dayanan ya
    /// da ilerlemeyen) bir SCC'nin yeşil üyesi. Motor bu başarıyı PERSIST ETMEZ,
    /// kaydı kanıtsız hata olarak geçersizleştirir (<c>LastResult=Failed</c>, <c>FailedSignature=null</c>) —
    /// <see cref="WillBuildEvaluator"/> bunu <see cref="WillBuildReason.NeverBuilt"/> okur; döngü üyesi bir
    /// sonraki Sync'in kapsamı dışında olduğu için <c>WillBuild=false</c>'tur. Koşullu değildir.
    /// <b>[DEĞİŞEN KURAL — final review I1]</b> Eskiden bu hâl <c>UpToDate</c> dönerdi ("defter hiçbir şey
    /// öğrenmedi, bugünkü olguya dön"); defter aslında kanıtsız hata yazdığı için satır canlıda yeşil, Sync
    /// sonrası gri idi.</para>
    ///
    /// <para><b>Dep-issue yoksa:</b> <see cref="WillBuildReason.UpToDate"/>.</para>
    ///
    /// <para><b>Dep-issue'lu, döngü üyesi DEĞİL:</b> <see cref="ConditionalRebuild.AppliesTo"/>'nun koşulları
    /// sağlanır — <c>WaitingForDependency</c>, <c>WillBuild=true</c> (hâlâ dirty), <c>Conditional=true</c>.</para>
    ///
    /// <para><b>Dep-issue'lu, döngü üyesi (yakınsamış grup):</b> defter GERÇEKTEN not+kök yazar, ama bir sonraki
    /// Sync bu üyeyi <c>buildCycles:false</c> ile değerlendirir — <c>WillBuild=false</c> ZORLANIR, gerekçe yine
    /// de <c>WaitingForDependency</c>'dir ("etiket bir disk olgusudur"). <see cref="ConditionalRebuild.AppliesTo"/>
    /// <c>WillBuild==true</c> gerektirdiğinden <c>Conditional=false</c> kalır — üye TEK BAŞINA hiçbir zaman
    /// koşullu değildir (grubuyla derlenir).</para>
    /// </summary>
    public static (bool WillBuild, WillBuildReason Reason, bool Conditional) AfterSuccess(
        bool inCycle, bool trusted, IReadOnlyList<string>? depIssues)
    {
        if (!trusted) return (!inCycle, WillBuildReason.NeverBuilt, false);
        if (depIssues is not { Count: > 0 }) return (false, WillBuildReason.UpToDate, false);
        return inCycle
            ? (false, WillBuildReason.WaitingForDependency, false)
            : (true, WillBuildReason.WaitingForDependency, true);
    }

    /// <summary>
    /// Proje BU KOŞUDA patladı. <paramref name="evidence"/> motorun kanıt kararıdır
    /// (<c>ProjectFailedEvent.Evidence</c>, defter yazımıyla AYNI kapı): kanıtlıysa defterde hata anındaki imza
    /// bugünküdür ⇒ <see cref="WillBuildReason.LastFailed"/> (kırmızı); kanıtsızsa (timeout · Stop · invoke
    /// hatası · Clean · yakınsamayan SCC üyesi) defter <c>FailedSignature</c>'ı boşaltır ⇒
    /// <see cref="WillBuildReason.NeverBuilt"/> (gri). Metin burada yeniden sınıflandırılmaz.
    /// </summary>
    public static WillBuildReason AfterFailure(bool evidence) =>
        evidence ? WillBuildReason.LastFailed : WillBuildReason.NeverBuilt;

    /// <summary>
    /// Proje BU KOŞUDA temizlendi — ya da temizliği patladı. İki yolda da defterde bu projenin başarısı kalmaz:
    /// Clean'in başarısı "derlendi" değil "çıktıları silindi"dir ve motor kaydı siler (<c>BuildStateStore.Remove</c>);
    /// patlayan bir <c>-t:Clean</c> derleyiciyi hiç çağırmadığı için kanıt sayılmaz ve kayıt kanıtsız hata olur.
    /// <see cref="WillBuildEvaluator"/> ikisini de <see cref="WillBuildReason.NeverBuilt"/> okur. <c>WillBuild</c>
    /// bir sonraki DÜZ Build'in cevabıdır: döngü üyesi onun kapsamı dışında olduğu için <c>false</c>, diğer her
    /// proje <c>true</c>. Koşullu değildir. Clean koşusunun kendi önizlemesi bu bayrağı YAZMAZ (her projeye
    /// <c>true</c> verir, çünkü o koşu hepsini temizler) — bayrağı sonuç buradan yazar.
    /// </summary>
    public static (bool WillBuild, WillBuildReason Reason, bool Conditional) AfterClean(bool inCycle) =>
        (!WillBuildEvaluator.OutOfScope(inCycle, buildCycles: false), WillBuildReason.NeverBuilt, false);

    // [DEĞİŞEN KURAL — kullanıcı kararı 2026-09-29] Configuration değişiminin eşlemesi (AfterConfigurationChange)
    // burada DEĞİLDİR: o bir motor olgusu değil, bir tahmindi — "configuration imzaya girer, yani imza her kayıtta
    // değişir". Defter proje başına TEK imza tutar (projenin en son derlendiği configuration'ınkini) ve motor ona karşı
    // karar verir; Debug → Release → Debug dönüşünde motor UpToDate derken tahmin SignatureChanged diyordu. Geçiş
    // artık kendi Sync'ini başlatır ve satır o Sync'in cevabına kadar kararsızdır (RunViewModel.SetConfiguration).
}
