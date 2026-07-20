namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/3b] Render dilimi (Ek A #16/#23): konsol yalnız SON <see cref="DefaultMaxLines"/> satırı render eder
/// (in-memory belge tamponu bu kadarla sınırlı; hacim/performans). "N lines" sayacı bu dilimden ETKİLENMEZ —
/// TAM mantıksal sayacı VM (<c>RunViewModel.GetActiveLineCount</c>) taşır (render dilimi DEĞİL).
///
/// <para>Bu SAF yardımcı yalnız metin kırpma matematiğidir (tohumlama için). Canlı append'te belge, aynı
/// sınırla <c>ConsoleView.AppendBatch</c> içinde baştan kırpılır.</para>
/// </summary>
public static class ConsoleRenderSlice
{
    /// <summary>design-v1 §2.5 / Ek A #16: konsol son 200 satır (tampon ~240 — burada render dilimi=200).</summary>
    public const int DefaultMaxLines = 200;

    /// <summary>Metnin son <paramref name="maxLines"/> (mantıksal, '\n' ile ayrılmış) satırını döner. Satır
    /// sayısı sınırın altındaysa metin OLDUĞU GİBİ döner. Sondaki '\n' korunur (satırlar '\n' sonekli birikir).</summary>
    public static string LastLines(string text, int maxLines = DefaultMaxLines)
    {
        if (string.IsNullOrEmpty(text) || maxLines <= 0) return text ?? "";

        // Sondan geriye maxLines adet '\n' say; (maxLines)'inci '\n'den SONRAki karakterden itibaren kes.
        // Metin "a\nb\nc\n" ise ve maxLines=2 → "b\nc\n" (son iki satır + boş sonek satırı).
        int newlinesNeeded = maxLines;
        int cut = text.Length;
        // Sondaki (varsa) '\n' bir sonraki BOŞ satırı başlatır — onu bir "satır" saymamak için atla.
        int i = text.Length - 1;
        if (i >= 0 && text[i] == '\n') i--;
        for (; i >= 0; i--)
        {
            if (text[i] == '\n')
            {
                if (--newlinesNeeded == 0) { cut = i + 1; return text[cut..]; }
            }
        }
        return text; // sınırın altında — tümü
    }
}
