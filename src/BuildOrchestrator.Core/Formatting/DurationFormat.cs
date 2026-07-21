using System.Globalization;

namespace BuildOrchestrator.Core.Formatting;

/// <summary>
/// [T12/C2] Süre metni biçimleyici — prototipin <c>fmtDur</c> (build-data.js:16-23) ve <c>fmtElapsed</c>
/// (BuildApp.jsx:76-80) portudur. <b>Saf, statik, UI'sız</b> ve tüm sayı biçimlemesi
/// <see cref="CultureInfo.InvariantCulture"/> iledir (Core UI'dan bağımsızdır; Türkçe Windows'ta <c>4,2s</c>
/// tuzağı buraya sızamaz).
/// </summary>
public static class DurationFormat
{
    /// <summary>Bir proje/koşu süresi: null → <c>"—"</c>; &lt;9950ms → tek ondalık saniye (<c>"4.2s"</c>);
    /// &lt;60s → tam saniye (<c>"12s"</c>); aksi halde <c>"1m 12s"</c> (saniye 2 hane pad).</summary>
    public static string Duration(long? ms)
    {
        if (ms is not { } v) return "—";
        // [Fix wave 1/C2 review Finding 3] Yuvarlama AÇIKÇA yarı-yukarı (AwayFromZero) — JS'in toFixed(1)'iyle
        // paritede: ör. 250ms → "0.3s". ToString("0.0") biçimlendirmesine ÖRTÜK olarak bırakılmaz (implicit
        // yuvarlama modu .NET sürümleri arasında garanti değildir); tam sayıya (0.1 birim) burada AÇIKÇA
        // yuvarlanır, biçimlendirme yalnız GÖSTERİM içindir.
        if (v < 9950) return Math.Round(v / 1000.0, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture) + "s";
        long s = (long)Math.Round(v / 1000.0, MidpointRounding.AwayFromZero);
        return s < 60
            ? s.ToString(CultureInfo.InvariantCulture) + "s"
            : $"{(s / 60).ToString(CultureInfo.InvariantCulture)}m {(s % 60).ToString(CultureInfo.InvariantCulture).PadLeft(2, '0')}s";
    }

    /// <summary>Canlı geçen süre: &lt;60s → tam saniye (<c>"24s"</c>); aksi halde <c>"1m 05s"</c>
    /// (dakika + 2 hane pad saniye). Negatif girdi 0'a clamp'lenir.</summary>
    public static string Elapsed(long ms)
    {
        long s = Math.Max(0, ms / 1000);
        return s < 60
            ? s.ToString(CultureInfo.InvariantCulture) + "s"
            : $"{(s / 60).ToString(CultureInfo.InvariantCulture)}m {(s % 60).ToString(CultureInfo.InvariantCulture).PadLeft(2, '0')}s";
    }
}
