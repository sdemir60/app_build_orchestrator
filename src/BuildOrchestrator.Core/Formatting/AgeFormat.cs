using System.Globalization;

namespace BuildOrchestrator.Core.Formatting;

/// <summary>
/// [design v1.16.0 §2.4] Bir olayın ÜSTÜNDEN GEÇEN süre — satırın <c>up to date · 2h</c> etiketindeki kuyruk
/// ve proje logu başlığındaki <c>Last successful build: 2h ago</c> aynı biçimi kullanır (kopya YASAK).
///
/// <para>Biçim kasıtlı olarak KABADIR: tek birim, ondalık yok (<c>just now</c> · <c>14m</c> · <c>2h</c> ·
/// <c>3d</c>). Kullanıcının sorusu "ne kadar bayat" — dakikası değil mertebesi.</para>
///
/// <para><b>Saf, statik, UI'sız</b> ve sayı biçimlemesi <see cref="CultureInfo.InvariantCulture"/> iledir.</para>
/// </summary>
public static class AgeFormat
{
    /// <summary>Bir dakikadan yeni her şey — saniye göstermek "az önce"yi anlatmaktan daha az bilgi verirdi.</summary>
    public const string JustNow = "just now";

    /// <param name="at">Olayın zamanı; <c>null</c> ⇒ olay hiç yaşanmadı, çağıran kendi metnini seçer.</param>
    /// <param name="now">Şimdi — testler sabit bir an verir (D8: gerçek zaman beklenmez).</param>
    /// <returns>Yaş metni; <paramref name="at"/> null ise <c>null</c>. Gelecekteki bir zaman (saat kayması)
    /// <see cref="JustNow"/>'a düşer — negatif yaş yazmak anlamsızdır.</returns>
    public static string? Age(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is not { } moment) return null;

        var elapsed = now - moment;
        if (elapsed < TimeSpan.FromMinutes(1)) return JustNow;
        if (elapsed < TimeSpan.FromHours(1)) return Unit(elapsed.TotalMinutes, "m");
        if (elapsed < TimeSpan.FromDays(1)) return Unit(elapsed.TotalHours, "h");
        return Unit(elapsed.TotalDays, "d");
    }

    private static string Unit(double value, string suffix) =>
        ((long)value).ToString(CultureInfo.InvariantCulture) + suffix;
}
