using System.Globalization;

namespace BuildOrchestrator.App.Console;

/// <summary>
/// [A13/T3 fix-1 · P3] Duvar-saati damgasının (<c>HH:mm:ss</c>) TEK biçimlendiricisi.
///
/// <para>design-v1 §2.5 anlatı satırı ve §2.6 stream satırı aynı damgayı taşır (<c>BuildApp.jsx</c>
/// <c>NarrLine</c> → <c>{time}</c> span'i, kaynak <c>eng.wall()</c>). Aynı ifade
/// (<c>ToString("HH:mm:ss", InvariantCulture)</c>) üç ayrı üretim dosyasında tekrarlanıyordu; P3 dördüncüsünü
/// yazacaktı — tek yere toplandı (kopya YASAK, CLAUDE.md).</para>
///
/// <para><b>Kültür:</b> InvariantCulture ZORUNLU (Global Constraint) — kullanıcının takvim/kültür ayarı damgayı
/// 12 saatlik biçime ya da yerel rakamlara kaydıramaz.</para>
/// </summary>
public static class WallClockFormat
{
    /// <summary>design-v1 §2.5/§2.6 damgası — saat:dakika:saniye, sıfır dolgulu, 24 saat.</summary>
    public const string Pattern = "HH:mm:ss";

    public static string Of(DateTimeOffset instant) => instant.ToString(Pattern, CultureInfo.InvariantCulture);
}
