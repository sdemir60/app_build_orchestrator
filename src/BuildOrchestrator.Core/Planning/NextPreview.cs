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
    /// Proje BU KOŞUDA başarıyla TEMİZLENDİ. Clean'in başarısı "derlendi" değil "çıktıları silindi"dir: motor
    /// defter kaydını siler (<c>BuildStateStore.Remove</c>) ve kayıt yoksa <see cref="WillBuildEvaluator"/>
    /// <see cref="WillBuildReason.NeverBuilt"/> okur.
    /// </summary>
    public static WillBuildReason AfterClean => WillBuildReason.NeverBuilt;

    /// <summary>
    /// Configuration değişti: configuration imzaya girer, yani imza her kayıtta değişir. Kaydında bir başarı olan
    /// (<c>BuiltSignature</c> dolu) her proje <see cref="WillBuildReason.SignatureChanged"/>; hiç başarısı olmayan
    /// <see cref="WillBuildReason.NeverBuilt"/>. App <c>BuiltSignature</c>'ı görmez — yalnız
    /// <see cref="WillBuildReason.LastFailed"/> satırı iki tarafa da düşebilir ve orada başarı izi olarak
    /// önizlemenin <c>BuiltCommit</c>'i okunur (<paramref name="builtCommit"/>; defterde onu yalnız başarı yazar).
    /// <c>LastBuiltAt</c> ayırıcı DEĞİLDİR: son koşu başarısızsa her LastFailed satırında null'dır
    /// (<c>BuildStateStore.LastBuiltAtOf</c>).
    /// <para><b>Bilinen ve KABUL EDİLEN boşluk (kullanıcı kararı):</b> commit'i kaydedilmemiş bir başarının
    /// (git dışı bir kök ya da revizyonu okunamayan harici proje) ardından patlayan proje burada
    /// <c>never built</c> okunur, motor ise <c>SignatureChanged</c> diyecektir; ikisi de gridir, yalnız etiket
    /// farklıdır ve bir sonraki Sync düzeltir. Kesin ayırıcı (<c>BuiltSignature</c> var mı) önizlemede
    /// taşınmıyor; onu taşımak için sözleşme değişikliği bilerek yapılmadı.</para>
    /// </summary>
    /// <para><b>[Faz 3 — spec 2026-09-18 §5, Task 7]</b> <see cref="WillBuildReason.OutputMissing"/> de
    /// <c>NeverBuilt</c> gibi okunur: motor zaten "bu projeye ait derleme kanıtı yok" diyor, configuration
    /// değişimini <c>SignatureChanged</c> okumak "bir şey değişti, yeniden derlenecek" der ki bu YANLIŞTIR —
    /// proje hiç derlenmemiş gibi kalmalı. Diğer üç yeni gerekçe (<c>BuiltOutside</c>, <c>OutputStale</c>,
    /// <c>OutputReplaced</c>) bugünkü düşüşü izler: <c>SignatureChanged</c>.</para>
    public static WillBuildReason AfterConfigurationChange(WillBuildReason reason, string? builtCommit) =>
        reason == WillBuildReason.LastFailed && builtCommit is null ? WillBuildReason.NeverBuilt
        : reason is WillBuildReason.NeverBuilt or WillBuildReason.OutputMissing ? WillBuildReason.NeverBuilt
        : WillBuildReason.SignatureChanged;
}
