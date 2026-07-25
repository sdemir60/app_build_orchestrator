using System.Globalization;

namespace BuildOrchestrator.Core.ProcessControl;

/// <summary>
/// [T20-b/K11] Perf profilinin KOPYA METNİ — App'in konsol notu ile Supervisor'ın run-başı satırları AYNI
/// sözlüğü kullanmak zorundadır. İki assembly'de iki formatlayıcı tutmak, T49'un token-drift'inin motor
/// karşılığıdır: biri "cpu cap 70%" derken diğeri "cpu 70" demeye başlar ve kimse fark etmez.
/// <para>Sayı formatlaması <see cref="CultureInfo.InvariantCulture"/>'dır (yüzde ayracı locale'e göre kaymaz).</para>
/// </summary>
public static class PerfNoteText
{
    /// <summary>Cap'i olmayan (Full) profilin değer terimi.</summary>
    public const string CapValueOff = "off";

    /// <summary>Perf modu HİÇ bildirilmemiş run'ların değer terimi. "off" DEĞİLDİR: tanıda "kapatıldı" ile
    /// "hiç istenmedi" aynı şey değildir (yalnız Supervisor'ın run-başı satırlarında görülür).</summary>
    public const string CapValueUnset = "unset";

    /// <summary>Cap'in DEĞER yarısı: <c>"70%"</c> · <c>"off"</c>.</summary>
    public static string CapValue(int? capPercent) => capPercent is { } percent
        ? string.Format(CultureInfo.InvariantCulture, "{0}%", percent)
        : CapValueOff;

    /// <summary>Cap terimi (prose): <c>"cpu cap 70%"</c> · <c>"cpu cap off"</c>.</summary>
    public static string CapText(int? capPercent) => "cpu cap " + CapValue(capPercent);

    /// <summary>Perf modu bildirilmemiş run'ın cap terimi: <c>"cpu cap unset"</c>.</summary>
    public static string CapTextUnset => "cpu cap " + CapValueUnset;

    /// <summary>
    /// [T20-b/P3] Copy-contention penceresinde cap'in geçici olarak tabana yükseltildiğini bildiren TANI notu
    /// (retry satırının sonuna eklenir → proje logu + konsol). Yüzde <see cref="PerfProfile.CopyPhaseFloorPercent"/>
    /// üzerinden TÜRETİLİR: cap metninin ikinci bir formatlayıcısı yoktur.
    /// </summary>
    public static string CopyFloorNote => string.Format(CultureInfo.InvariantCulture,
        "Copy fazı için cpu cap geçici olarak {0} tabanına yükseltildi.", CapValue(PerfProfile.CopyPhaseFloorPercent));

    /// <summary>
    /// [K11 BİREBİR] Perf chip'inin konsol notu: <c>parallelism: 4 · cpu cap 70%</c> · cap'siz (Full) profilde
    /// <c>parallelism: 6 · cpu cap off</c>. Ayraç U+00B7 (boşluklu).
    /// </summary>
    public static string Note(PerfProfile profile) => string.Format(CultureInfo.InvariantCulture,
        "parallelism: {0} · {1}", profile.Parallelism, CapText(profile.CpuCapPercent));
}
