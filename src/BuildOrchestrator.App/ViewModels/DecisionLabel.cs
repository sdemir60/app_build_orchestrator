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
/// <param name="WillBuild">Etiket "derlenecek" mi diyor — yuvanın rengi bundan gelir.</param>
public readonly record struct RowDecision(string Word, string? Tail, string Title, bool WillBuild)
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
/// <para><b>Öncelik:</b> hiç derlenmemiş &gt; son derleme hata verdi &gt; kendi dosyası değişti &gt;
/// bağımlılığı değişti. İlk ikisi motorun gerekçesinden (<see cref="WillBuildReason"/>), sonraki ikisi
/// "kendi dosyası değişti mi" olgusundan (<see cref="BuildPreviewItem.OwnFilesChanged"/>) gelir.</para>
///
/// <para><b>Karar bilinmiyorsa yuva BOŞ kalır.</b> Sync yapılmadıysa, ya da satır koşu-zamanlama kuralıyla
/// (döngü kapsamı, yakınsamama hafızası) atlandıysa motorun bir gerekçesi yoktur — boş yuva "henüz
/// bilmiyorum"un doğru karşılığıdır ve uydurma bir sözcükten iyidir.</para>
/// </summary>
public static class DecisionLabel
{
    /// <param name="willBuild">Üç durumlu plan: true=derlenecek, false=atlanacak, null=bilinmiyor.</param>
    /// <param name="reason">Motorun gerekçesi (<see cref="BuildPreviewItem.Reason"/>).</param>
    /// <param name="ownFilesChanged">Projenin KENDİ girdi dosyaları değişti mi (motorun Fast geçişi).</param>
    /// <param name="lastBuiltAt">Son BAŞARILI derlemenin zamanı — <c>up to date</c> kuyruğu buradan.</param>
    /// <param name="now">Şimdi (yaş hesabı için).</param>
    public static RowDecision For(
        bool? willBuild, WillBuildReason? reason, bool? ownFilesChanged, DateTimeOffset? lastBuiltAt, DateTimeOffset now)
    {
        if (willBuild is not { } plan) return RowDecision.None;

        if (reason is WillBuildReason.NeverBuilt)
            return new("never built", null, "No build output on disk", WillBuild: true);

        if (reason is WillBuildReason.LastFailed)
            return new("failed", "retry", "The last build of this project failed", WillBuild: true);

        if (plan)
        {
            // DepIssue de buraya düşer: projenin kendi dosyaları durur, bayat olan bağımlılığının çıktısıdır —
            // yani kullanıcı açısından "etkilenmiş" satırın ta kendisi.
            return ownFilesChanged == true
                ? new("modified", null, "Its own files changed since the last build", WillBuild: true)
                : new("affected", null, "Its own files are unchanged — a dependency changed", WillBuild: true);
        }

        // Atlanacak: yalnız motorun İMZADAN verdiği "güncel" kararı bu sözcüğü hak eder. Koşu-zamanlama
        // kuralıyla atlanan satırlar (döngü kapsamı vb.) güncel DEĞİLDİR; onlarda yuva boş kalır.
        if (reason is not WillBuildReason.UpToDate) return RowDecision.None;

        string? age = AgeFormat.Age(lastBuiltAt, now);
        return new("up to date", age,
            age is null ? "Up to date" : $"Up to date — last built {age} ago", WillBuild: false);
    }
}
