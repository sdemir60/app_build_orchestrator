namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/3b] Render dilimi (Ek A #16/#23): konsol yalnız SON <see cref="DefaultMaxLines"/> satırı render eder
/// (in-memory belge tamponu bu kadarla sınırlı; hacim/performans). "N lines" sayacı bu dilimden ETKİLENMEZ —
/// TAM mantıksal sayacı VM (<c>RunViewModel.GetActiveLineCount</c>) taşır (render dilimi DEĞİL).
///
/// <para>Sınırın TEK tanımıdır. Belge her kurulumda backlog'un son diliminden kurulur (<c>ConsoleView.ReplaceRunDocument</c>,
/// <c>ConsoleView.ReplaceProjectDocument</c>), canlı append'te ve takip geri gelince aynı sınırla baştan kırpılır
/// (<c>ConsoleView.AppendBatch</c>). [idle-memory · final review 90] Metin kırpan eski yardımcı (<c>LastLines</c>) kalktı: tohumlama
/// metinden değil backlog'dan yapıldığından üretimde çağıranı yoktu.</para>
/// </summary>
public static class ConsoleRenderSlice
{
    /// <summary>design-v1 §2.5 / Ek A #16: konsol son 200 satır (tampon ~240 — burada render dilimi=200).</summary>
    public const int DefaultMaxLines = 200;
}
