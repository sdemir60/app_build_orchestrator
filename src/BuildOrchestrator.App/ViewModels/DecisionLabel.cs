using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Formatting;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [design v1.16.0 §2.4] Satırın sağ yuvasındaki KARAR ETİKETİ: bir sonraki koşuda bu projeye ne olacağını ve
/// NEDEN olacağını söyleyen iki sözcük.
/// </summary>
/// <param name="Word">Asıl sözcük (<c>modified</c>, <c>up to date</c>…) — boşsa yuva boş kalır.</param>
/// <param name="Tail">"·" sonrası kuyruk (<c>local</c>, <c>2h</c>); yoksa null. Her zaman soluk çizilir.</param>
/// <param name="Title">Uzun gerekçe — DS Tooltip DEĞİL, native <c>title</c> (ikon butonlarıyla aynı dil).</param>
/// <param name="Stale">Etiket BEKLEYEN İŞ mi söylüyor — yuvanın rengi bundan gelir (bekleyen iş
/// <c>text-secondary</c>, güncel <c>text-faint</c>). "Bu koşuda derlenecek" ile aynı şey DEĞİLDİR: kapsam
/// dışı bir döngü üyesi bayat olabilir ama bu koşuda derlenmez — o ayrımı uyarı üçgeni söyler.</param>
public readonly record struct RowDecision(string Word, string? Tail, string Title, bool Stale)
{
    /// <summary>Yuvanın boş hâli: karar henüz yok.</summary>
    public static readonly RowDecision None = new("", null, "", false);

    public bool IsEmpty => Word.Length == 0;
}

/// <summary>
/// Satır durumundan karar etiketini üreten saf fonksiyon — WPF'siz test edilir.
///
/// <para><b>Sözcükler SABİTTİR</b> (git + MSBuild'in ortak dili) ve BEŞ tanedir: <c>modified</c> ·
/// <c>affected</c> · <c>never built</c> · <c>failed</c> · <c>up to date</c>. Altıncı bir sözcük eklemek (ör.
/// zorlamalı kapsam için <c>forced</c>) bilinçli olarak REDDEDİLDİ: etiket bir DİSK OLGUSUDUR, koşunun kapsamı
/// değil. Rebuild'de içeriği güncel bir satır <c>up to date</c> yazmaya devam eder; kapsamı şerit, nokta ve
/// işaretleme dalgası anlatır.</para>
///
/// <para><b>Öncelik:</b> hiç derlenmemiş &gt; son derleme hata verdi &gt; güncel &gt; kendi dosyası değişti
/// &gt; bağımlılığı değişti. İlk üçü motorun gerekçesinden (<see cref="WillBuildReason"/>), son ikisi "kendi
/// dosyası değişti mi" olgusundan (<see cref="BuildPreviewItem.OwnFilesChanged"/>) gelir — ve o olgu deftere
/// yazılmış içerik özetiyle bugünkünün karşılaştırmasıdır (<c>BuildStateStore.OwnFilesChanged</c>), imzayla
/// DEĞİL: imza upstream'leri de taşır, yani bir bağımlılığın kaydı geçersizleşince kullanıcının hiç
/// dokunmadığı proje <c>modified</c> görünürdü.</para>
///
/// <para><b>Kapsam etiketi susturmaz, ama SÖZ DE VERDİRMEZ.</b> Kapsam dışı bir döngü üyesi bu koşuda
/// derlenmez; dosyaları değişmişse yine <c>modified</c> yazar (bayat olduğu doğrudur, onu derleyecek şeyin
/// <i>Resolve cycles</i> olduğunu uyarı üçgeni söyler). Gizlemek ölçüldü: gerçek bir çalışma alanında 184
/// satırın 33'ü (tüm SCC üyeleri) hiçbir şey yazmıyordu.</para>
///
/// <para><b>[DEĞİŞEN KURAL — design v1.20.0 §2.4]</b> Etiket eskiden iki bilgiyi daha taşıyordu ve ikisi de
/// kaldırıldı:
/// <list type="bullet">
/// <item><c>failed · retry</c> çifti: <c>retry</c> bir SÖZDÜ ("bir sonraki Build bunu yeniden deneyecek") ve
/// döngü üyesinde tutulmuyordu (ölçüldü: gerçek bir çalışma alanında 18 <c>failed</c> satırının 15'i döngü
/// üyesiydi). Sözcük artık her koşulda yalnız <c>failed</c> + KANITIN YAŞI (<see cref="WillBuildReason.LastFailed"/>
/// iken <c>failedAt</c>'in <see cref="AgeFormat.Age"/>'i) yazar — "yeniden denenecek mi" sorusunu artık uzun
/// gerekçe (döngü üyesinde <i>Resolve cycles</i>, değilse <i>Build</i>) cevaplar, kuyruk değil.</item>
/// <item><c>affected · up to date · &lt;yaş&gt;</c> üçlüsü (koşullu yeniden derleme, Task 4): motor bu koşuyu
/// gerçekten bekletiyorsa (<c>conditional</c>) yuva kökleri tooltip'inde tekrarlıyordu — ama aynı bilgi zaten
/// uyarı üçgeninin TEK SATIRLIK tooltip'inde vardı (kopya YASAK). <see cref="WillBuildReason.WaitingForDependency"/>
/// artık <see cref="WillBuildReason.UpToDate"/> ile BİREBİR aynı okunur: proje ÇIKTI olarak güncel, hangi kökün
/// beklendiğini yalnız üçgen (<c>RowWarning.WaitingForDependencyText</c>) anlatır. <c>conditional</c>,
/// <c>dependencyRoots</c> ve <c>namePrefix</c> parametreleri bu yüzden TAMAMEN kalktı — kapsamın zorlayıp
/// zorlamadığı ve hangi kökün beklendiği artık etiketin işi değil.</item>
/// </list></para>
///
/// <para><b>Karar bilinmiyorsa yuva BOŞ kalır</b> — yalnız gerçekten bilinmiyorsa: Sync yapılmadı ya da motor
/// bu satır için gerekçe üretmedi. Boş yuva "henüz bilmiyorum"un doğru karşılığıdır.</para>
/// </summary>
public static class DecisionLabel
{
    /// <param name="willBuild">Üç durumlu plan: true=derlenecek, false=atlanacak, null=bilinmiyor.</param>
    /// <param name="reason">Motorun gerekçesi (<see cref="BuildPreviewItem.Reason"/>).</param>
    /// <param name="ownFilesChanged">Projenin KENDİ girdi dosyaları değişti mi — defterdeki içerik özetiyle
    /// bugünkünün karşılaştırması (<c>BuildStateStore.OwnFilesChanged</c>); bilinmiyorsa <c>null</c>.</param>
    /// <param name="lastBuiltAt">Son BAŞARILI derlemenin zamanı — <c>up to date</c> kuyruğu buradan.</param>
    /// <param name="failedAt">[spec 2026-09-18 §1-14] Kanıtlı son hatanın zamanı — <c>failed</c> kuyruğu buradan
    /// (<see cref="WillBuildReason.LastFailed"/> dışında okunmaz).</param>
    /// <param name="localEdits">[spec 2026-09-18 §4 <c>local</c>] Projenin girdilerinden en az biri
    /// <c>git status</c>'ta kirli mi — yalnız <c>modified</c> satırında <c>local</c> kuyruğunu ekler.</param>
    /// <param name="now">Şimdi (yaş hesabı için).</param>
    /// <param name="inCycle">Proje bir bağımlılık döngüsünün üyesi mi — yalnız <c>failed</c> satırının uzun
    /// gerekçesini seçer (o satırı yeniden denemek <i>Resolve cycles</i>'ın işidir).</param>
    public static RowDecision For(
        bool? willBuild, WillBuildReason? reason, bool? ownFilesChanged, DateTimeOffset? lastBuiltAt,
        DateTimeOffset? failedAt, bool localEdits, DateTimeOffset now, bool inCycle = false)
    {
        // Karar yok: Sync yapılmadı (willBuild null) ya da motor bu satır için gerekçe üretmedi.
        if (willBuild is null || reason is null) return RowDecision.None;

        switch (reason)
        {
            case WillBuildReason.NeverBuilt:
                return new("never built", null, "No build output known to this tool", Stale: true);

            case WillBuildReason.LastFailed:
            {
                string? failAge = AgeFormat.Age(failedAt, now);
                string ageSuffix = failAge is null ? "" : $" {failAge} ago";
                string retryClause = inCycle ? "Resolve cycles will retry it" : "Build will retry it";
                return new("failed", failAge, $"Failed at this source{ageSuffix} — {retryClause}", Stale: true);
            }

            // [DEĞİŞEN KURAL — design v1.20.0 §2.4] Bkz. sınıf özeti: WaitingForDependency artık UpToDate ile
            // AYNI okunur — kapsamın gerçekten bekletip bekletmediği (eski "conditional") etiketi etkilemez,
            // hangi kökün beklendiğini yalnız uyarı üçgeni söyler.
            case WillBuildReason.UpToDate:
            case WillBuildReason.WaitingForDependency:
            {
                string? age = AgeFormat.Age(lastBuiltAt, now);
                return new("up to date", age,
                    age is null ? "Up to date" : $"Up to date — last built {age} ago", Stale: false);
            }

            default:
                // SignatureChanged ve DepIssue: ikisinde de proje bayattır, ayrımı "kendi dosyası değişti mi"
                // olgusu yapar. Bilinmiyorsa daha ihtiyatlı olan "affected" yazılır.
                if (ownFilesChanged != true)
                    return new("affected", null, "Its own files are unchanged — a dependency changed", Stale: true);

                return localEdits
                    ? new("modified", "local",
                        "Its own files changed since the last build — includes uncommitted edits", Stale: true)
                    : new("modified", null, "Its own files changed since the last build", Stale: true);
        }
    }
}
