using System.Globalization;

namespace BuildOrchestrator.Core.Formatting;

/// <summary>
/// İnsan-okur bayt metni — <see cref="DurationFormat"/>'ın kardeşi ve boyut biçimlemenin TEK kaynağı.
/// Konsol satırları (Clean ve Optimize) ve App'in stream özetleri hep buradan geçer.
///
/// <para><b>Biçim:</b> 1024'ün altı ham bayttır (<c>"512 B"</c>); üstünde birim büyütülür ve yalnız küçük
/// değerlerde ondalık yazılır (<c>"1.5 KB"</c> ama <c>"15 KB"</c>) — ondalık, sayı zaten üç haneliyken
/// okumaya bir şey katmaz. Kültürden bağımsızdır (<see cref="CultureInfo.InvariantCulture"/>): metin telde ve
/// konsolda aynı görünür.</para>
///
/// <para>Negatif girdi <c>"0 B"</c>'dir. Boyut toplamları negatif olamaz; olursa bu bir sayım hatasıdır ve
/// kullanıcıya eksi bayt göstermek onu bilgilendirmez.</para>
/// </summary>
public static class ByteFormat
{
    public static string Size(long bytes)
    {
        if (bytes <= 0) return "0 B";

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0
            ? $"{bytes} {units[unit]}"
            : $"{value.ToString(value < 10 ? "0.0" : "0", CultureInfo.InvariantCulture)} {units[unit]}";
    }
}
