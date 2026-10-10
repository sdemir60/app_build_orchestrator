namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

/// <summary>
/// App'in CANLI geçişleri: bir koşu olayı ya da kullanıcı eylemi bir satırın kaydını değiştirdiği ANDA satır,
/// motorun bir sonraki önizlemesini (bir Sync'e kadar gelmeyebilir) beklemeden ne göstermeli? Cevap o
/// önizlemenin (<see cref="WillBuildEvaluator"/> + <see cref="ConditionalRebuild.ConditionalIds"/>) AYNEN kendisidir;
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
    /// da ilerlemeyen) bir SCC'nin yeşil üyesi. Motor bu başarıyı PERSIST ETMEZ, kaydı kanıtsız hata olarak
    /// geçersizleştirir — cevap <see cref="AfterUntrustedResult"/>'ındır (dep-issue'ya bakılmaz).
    /// <b>[DEĞİŞEN KURAL — final review I1]</b> Eskiden bu hâl <c>UpToDate</c> dönerdi ("defter hiçbir şey
    /// öğrenmedi, bugünkü olguya dön"); defter aslında kanıtsız hata yazdığı için satır canlıda yeşil, Sync
    /// sonrası gri idi.</para>
    ///
    /// <para><b>Dep-issue yoksa:</b> <see cref="WillBuildReason.UpToDate"/>.</para>
    ///
    /// <para><b>Dep-issue'lu, döngü üyesi DEĞİL:</b> <see cref="ConditionalRebuild.AppliesTo"/>'nun koşulları
    /// sağlanır — <c>WaitingForDependency</c>, <c>WillBuild=true</c> (hâlâ dirty), <c>Conditional=true</c>.</para>
    ///
    /// <para><b>Dep-issue'lu, döngü üyesi (yakınsamış grup):</b> defter GERÇEKTEN not+kök yazar; bir sonraki Sync
    /// bu üyeyi Build'in kararıyla (<see cref="CycleCompilation"/>) değerlendirir — <c>WaitingForDependency</c>,
    /// <c>WillBuild=true</c>, <c>Conditional=true</c>: üye tek başına değil GRUBUYLA koşulludur
    /// (<see cref="ConditionalRebuild.ConditionalIds"/>). Yakınsamış bir grubun her üyesi ya bu notu taşır (bekler) ya
    /// taşımaz (güncel); başka gerekçeyle kirli üye kalmadığından grup kapısı (<see cref="ConditionalRebuild.GroupAppliesTo"/>)
    /// tutar — Sync'in cevabı budur. <b>[DEĞİŞEN KURAL — final review M-2]</b> Eskiden <c>Conditional=false</c>
    /// dönerdi: grup üyesi koşullu kümeye hiç girmezdi.</para>
    /// </summary>
    public static (bool WillBuild, WillBuildReason Reason, bool Conditional) AfterSuccess(
        bool inCycle, bool trusted, IReadOnlyList<string>? depIssues)
    {
        if (!trusted) return AfterUntrustedResult(inCycle);
        if (depIssues is not { Count: > 0 }) return (false, WillBuildReason.UpToDate, false);
        bool willBuild = NextBuildCompiles(inCycle);
        return (willBuild, WillBuildReason.WaitingForDependency, willBuild);
    }

    /// <summary>
    /// [B3] Motorun arkasında DURMADIĞI bir sonuç: yakınsamayan grubun güvenilmez başarısı (<see cref="AfterSuccess"/>'ın
    /// <c>trusted: false</c> dalı) ya da hükmü verilmiş grupta derlenmeden kaydı atılan taşınan üyenin atlaması
    /// (<see cref="SkipReasons.CycleNonConvergent"/>). İkisinde de motor kaydı kanıtsız hata olarak geçersizleştirir
    /// (<c>LastResult=Failed</c>, <c>FailedSignature=null</c>) — <see cref="WillBuildEvaluator"/> bunu
    /// <see cref="WillBuildReason.NeverBuilt"/> okur; bir sonraki düz Build kirli grubu da derlediği için üye düz proje
    /// gibi <c>WillBuild=true</c>'dur. Koşullu değildir.
    /// </summary>
    public static (bool WillBuild, WillBuildReason Reason, bool Conditional) AfterUntrustedResult(bool inCycle) =>
        (NextBuildCompiles(inCycle), WillBuildReason.NeverBuilt, false);

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
    /// bir sonraki DÜZ Build'in cevabıdır: Build kirli döngü grubunu da derlediği için döngü üyesi dahil her proje
    /// <c>true</c>. Koşullu değildir. Clean koşusunun kendi önizlemesi bu bayrağı YAZMAZ (her projeye
    /// <c>true</c> verir, çünkü o koşu hepsini temizler) — bayrağı sonuç buradan yazar.
    /// </summary>
    public static (bool WillBuild, WillBuildReason Reason, bool Conditional) AfterClean(bool inCycle) =>
        (NextBuildCompiles(inCycle), WillBuildReason.NeverBuilt, false);

    /// <summary>[D8] Proje BU KOŞUDA sırası gelince <c>up to date</c> ile atlandı (yüzey kapısı — hiçbir doğrudan bağımlılığının
    /// API yüzeyi değişmemiş): defteri yeni bileşik imzayla yenilendi, bir sonraki Sync <see cref="WillBuildReason.UpToDate"/>
    /// der. Pre-skip'ler buraya gelmez (satır zaten WillBuild=false); kök bekleyen atlama (DependencyStillFailing) defterine
    /// dokunmaz ve buraya gelmez.</summary>
    public static (bool WillBuild, WillBuildReason Reason, bool Conditional) AfterUpToDateSkip() =>
        (false, WillBuildReason.UpToDate, false);

    /// <summary>Bir sonraki DÜZ Build bu projeyi derleme kapsamına alır mı — Sync'in sorduğu AYNI soru
    /// (<see cref="CycleCompilation"/> + <see cref="WillBuildEvaluator.OutOfScope"/>); kopya YASAK.</summary>
    private static bool NextBuildCompiles(bool inCycle) =>
        !WillBuildEvaluator.OutOfScope(inCycle, CycleCompilation.CompilesCycles(RunMode.Build));

    // [DEĞİŞEN KURAL — kullanıcı kararı 2026-09-29] Configuration değişiminin eşlemesi (AfterConfigurationChange)
    // burada DEĞİLDİR: o bir motor olgusu değil, bir tahmindi — "configuration imzaya girer, yani imza her kayıtta
    // değişir". Defter proje başına TEK imza tutar (projenin en son derlendiği configuration'ınkini) ve motor ona karşı
    // karar verir; Debug → Release → Debug dönüşünde motor UpToDate derken tahmin SignatureChanged diyordu. Geçiş
    // artık kendi Sync'ini başlatır ve satır o Sync'in cevabına kadar kararsızdır (RunViewModel.SetConfiguration).
}
