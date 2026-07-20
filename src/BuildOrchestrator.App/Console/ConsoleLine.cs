namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/3a] Anlatı (narrative) konsol satırının görsel tipi — design-v1 §2.5 NARR_COLORS ile birebir:
/// cmd=text-primary, info=text-secondary, dim=text-faint, success/warn/error=ilgili <c>-text</c> tonu.
/// Renk eşlemesi <see cref="ConsolePalette"/>'tedir (token brush'ları; hardcode YASAK).
/// </summary>
public enum ConsoleLineType { Cmd, Info, Dim, Success, Warn, Error }

/// <summary>Satır metni İÇİNDE bir aralık (offset+length). Colorizer bunları line.Offset'e göre mutlaklaştırır.</summary>
public readonly record struct ConsoleSpan(int Offset, int Length);

/// <summary>Bir satırın renklendirme düzeni: gövde tipi + (varsa) saat aralığı + (varsa) <c>▸</c> ikon aralığı.
/// Belge DÜZ metin kalır — bu yalnız görsel bir katmanın hesap sonucudur (kopyalanan metne dokunulmaz).</summary>
public readonly record struct ConsoleLineLayout(ConsoleLineType Type, ConsoleSpan? Clock, ConsoleSpan? Icon);

/// <summary>
/// [T56/3a] Bir konsol satırının DÜZ METNİNDEN görsel düzenini (saat / ▸ ikon / gövde tipi) türeten SAF ayrıştırıcı.
///
/// <para><b>Line-type metadata kararı (task 3a):</b> VM düz string post ediyor; colorizer için satır TİPİ gerek.
/// Seçim: tipi <b>satır metninden türet</b> (batch/marshal sözleşmesine DOKUNMA). Böylece <see cref="ConsoleBatcher.Post"/>
/// imzası ve marshal-free <c>OnProjectLog</c> yolu değişmez; her belge (doküman swap / reseed sonrası bile) KENDİNİ
/// tanımlar — offset'lerle senkron tutulan paralel bir tip dizisi gerekmez. Açık semantik tip taşımak istenirse
/// (ileride) yalnız bu tek nokta (<see cref="ConsoleLineClassifier"/>) değişir — temiz seam.</para>
/// </summary>
public static class ConsoleLineParser
{
    /// <summary>Komut önekindeki amber ok (design-v1 §2.5 "cmd satırında amber ▸").</summary>
    public const char CommandIcon = '▸'; // ▸

    public static ConsoleLineLayout Layout(string? text)
    {
        text ??= "";
        var clock = TryClock(text);
        int scanStart = clock is { } c ? c.Offset + c.Length : 0;
        var icon = TryIcon(text, scanStart);
        var type = icon is not null ? ConsoleLineType.Cmd : ConsoleLineClassifier.ClassifyBody(text);
        return new ConsoleLineLayout(type, clock, icon);
    }

    // Satır başındaki HH:MM:SS (8 karakter, dd:dd:dd) — sahte duvar saati (design-v1 §2.5), text-faint boyanır.
    private static ConsoleSpan? TryClock(string text)
    {
        if (text.Length < 8) return null;
        for (int i = 0; i < 8; i++)
        {
            char ch = text[i];
            bool ok = i is 2 or 5 ? ch == ':' : char.IsAsciiDigit(ch);
            if (!ok) return null;
        }
        return new ConsoleSpan(0, 8);
    }

    // Saatten sonra ilk boşluk-olmayan karakter ▸ ise ikon aralığı; değilse ikon yok.
    private static ConsoleSpan? TryIcon(string text, int from)
    {
        for (int i = from; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == CommandIcon) return new ConsoleSpan(i, 1);
            if (ch != ' ') return null;
        }
        return null;
    }
}

/// <summary>
/// [T56/3a] Satır düz metninden <see cref="ConsoleLineType"/> türeten SAF sınıflandırıcı — bkz.
/// <see cref="ConsoleLineParser"/> tip dokümanındaki metadata kararı. Kural sırası önemlidir; güvenilir
/// sinyallere dayanır (▸ öneki, VM'in <c>[hata]</c> öneki, MSBuild <c>error</c>/<c>warning</c> anahtarları).
/// Tanınmayan satır <see cref="ConsoleLineType.Info"/>'dur (dim/success gibi ince ayrımlar açık anlatı
/// üreticisi geldiğinde bu tek noktadan zenginleşir — seam).
/// </summary>
public static class ConsoleLineClassifier
{
    public static ConsoleLineType Classify(string? text) => ConsoleLineParser.Layout(text).Type;

    /// <summary>▸ ikon tespiti HARİÇ, gövde metninden anahtar-kelime bazlı tip. <see cref="ConsoleLineParser.Layout"/>
    /// bunu yalnız ikon yokken çağırır (ikon varsa tip zaten Cmd).</summary>
    internal static ConsoleLineType ClassifyBody(string text)
    {
        if (string.IsNullOrEmpty(text)) return ConsoleLineType.Info;
        string content = text.AsSpan(ClockPrefixLength(text)).TrimStart().ToString();

        if (content.StartsWith(ConsoleLineParser.CommandIcon)) return ConsoleLineType.Cmd;
        if (content.StartsWith("[hata]", StringComparison.Ordinal) ||
            content.StartsWith("[error]", StringComparison.OrdinalIgnoreCase))
            return ConsoleLineType.Error;

        // "warning" ÖNCE gelir: design-v1'in "warning: OSYS.Sales.Core failed in this run …" bağımlılık uyarısı
        // hem "warning" hem "failed" içerir ve warn (turuncu) olmalı — error (kırmızı) değil.
        if (content.Contains("warning", StringComparison.OrdinalIgnoreCase))
            return ConsoleLineType.Warn;

        if (content.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            content.Contains(": error", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("error CS", StringComparison.OrdinalIgnoreCase) ||
            content.Contains('✗')) // ✗
            return ConsoleLineType.Error;

        if (content.Contains("succeeded", StringComparison.OrdinalIgnoreCase) || content.Contains('✓')) // ✓
            return ConsoleLineType.Success;

        return ConsoleLineType.Info;
    }

    // Baştaki HH:MM:SS varsa 8, yoksa 0 — keyword taraması saat rakamlarını görmez.
    private static int ClockPrefixLength(string text)
    {
        if (text.Length < 8) return 0;
        for (int i = 0; i < 8; i++)
        {
            char ch = text[i];
            bool ok = i is 2 or 5 ? ch == ':' : char.IsAsciiDigit(ch);
            if (!ok) return 0;
        }
        return 8;
    }
}
