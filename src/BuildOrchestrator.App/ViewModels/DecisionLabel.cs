using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Formatting;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [design v1.16.0 §2.4] Satırın sağ yuvasındaki KARAR ETİKETİ: bir sonraki koşuda bu projeye ne olacağını ve
/// NEDEN olacağını söyleyen iki-üç sözcük.
/// </summary>
/// <param name="Word">Asıl sözcük (<c>modified</c>, <c>up to date</c>…) — boşsa yuva boş kalır.</param>
/// <param name="Tail">"·" sonrası kuyruk (<c>retry</c>, <c>2h</c>); yoksa null. Her zaman soluk çizilir.</param>
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
/// <para><b>Sözcükler SABİTTİR</b> (git + MSBuild'in ortak dili) ve beş tanedir: <c>modified</c> ·
/// <c>affected</c> · <c>never built</c> · <c>failed · retry</c> · <c>up to date · 2h</c>. Altıncı bir sözcük
/// eklemek (ör. zorlamalı kapsam için <c>forced</c>) bilinçli olarak REDDEDİLDİ: etiket bir DİSK OLGUSUDUR,
/// koşunun kapsamı değil. Rebuild'de içeriği güncel bir satır <c>up to date</c> yazmaya devam eder; kapsamı
/// şerit, nokta ve işaretleme dalgası anlatır.</para>
///
/// <para><b>Öncelik:</b> hiç derlenmemiş &gt; son derleme hata verdi &gt; güncel &gt; kendi dosyası değişti
/// &gt; bağımlılığı değişti. İlk üçü motorun gerekçesinden (<see cref="WillBuildReason"/>), son ikisi "kendi
/// dosyası değişti mi" olgusundan (<see cref="BuildPreviewItem.OwnFilesChanged"/>) gelir — ve o olgu deftere
/// yazılmış içerik özetiyle bugünkünün karşılaştırmasıdır (<c>BuildStateStore.OwnFilesChanged</c>), imzayla
/// DEĞİL: imza upstream'leri de taşır, yani bir bağımlılığın kaydı geçersizleşince kullanıcının hiç
/// dokunmadığı proje <c>modified</c> görünürdü.</para>
///
/// <para><b>Kapsam etiketi susturmaz.</b> Kapsam dışı bir döngü üyesi bu koşuda derlenmez ama dosyaları
/// değişmişse <c>modified</c> yazar: bayat olduğu doğrudur ve onu derleyecek şeyin <i>Resolve cycles</i>
/// olduğunu uyarı üçgeni söyler. Bu, "etiket bir disk olgusudur" kuralının aynısıdır — ölçüldü: gerçek bir
/// çalışma alanında 184 satırın 33'ü (tüm SCC üyeleri) hiçbir şey yazmıyordu.</para>
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
    /// <param name="now">Şimdi (yaş hesabı için).</param>
    public static RowDecision For(
        bool? willBuild, WillBuildReason? reason, bool? ownFilesChanged, DateTimeOffset? lastBuiltAt, DateTimeOffset now)
    {
        // Karar yok: Sync yapılmadı (willBuild null) ya da motor bu satır için gerekçe üretmedi.
        if (willBuild is null || reason is null) return RowDecision.None;

        switch (reason)
        {
            case WillBuildReason.NeverBuilt:
                return new("never built", null, "No build output on disk", Stale: true);

            case WillBuildReason.LastFailed:
                return new("failed", "retry", "The last build of this project failed", Stale: true);

            case WillBuildReason.UpToDate:
                string? age = AgeFormat.Age(lastBuiltAt, now);
                return new("up to date", age,
                    age is null ? "Up to date" : $"Up to date — last built {age} ago", Stale: false);

            default:
                // SignatureChanged ve DepIssue: ikisinde de proje bayattır, ayrımı "kendi dosyası değişti mi"
                // olgusu yapar. Bilinmiyorsa daha ihtiyatlı olan "affected" yazılır.
                return ownFilesChanged == true
                    ? new("modified", null, "Its own files changed since the last build", Stale: true)
                    : new("affected", null, "Its own files are unchanged — a dependency changed", Stale: true);
        }
    }
}
