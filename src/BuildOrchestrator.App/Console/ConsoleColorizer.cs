using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace BuildOrchestrator.App.Console;

/// <summary>Bir satır İÇİNDE renkli bir aralık (line.Offset'e göre değil, satır-yerel offset) + brush.</summary>
public readonly record struct ConsoleColorSpan(int Offset, int Length, Brush Brush);

/// <summary>
/// [T56/3a] design §3.6 mimarisi: read-only AvalonEdit + <see cref="DocumentColorizingTransformer"/> ile
/// satır-offset bazlı renklendirme. Belge DÜZ metin tutar — renk yalnız GÖRSEL bir katmandır (kopyalanan metin
/// anlamlı kalsın diye asla markup gömülmez).
///
/// <para><b>[design v1.7.0 §2.5]</b> Satır TEK parçadır: saat ve <c>▸</c> kolonları kaldırıldığı için artık
/// satır içinde ayrı renkli aralık yoktur, tüm satır tip rengiyle boyanır. <see cref="ComputeSpans"/> yine de
/// aralık listesi döndürür (çağıran sözleşmesi ve render'sız test edilebilirlik korunur).</para>
/// </summary>
public sealed class ConsoleColorizer : DocumentColorizingTransformer
{
    private readonly ConsolePalette _palette;

    public ConsoleColorizer(ConsolePalette palette) => _palette = palette;

    /// <summary>Verilen satır metni için offset-bazlı renk aralıkları. Boş metin → boş liste; aksi halde
    /// satırın TAMAMINI kaplayan tek aralık (tip rengi).</summary>
    public IReadOnlyList<ConsoleColorSpan> ComputeSpans(string? lineText)
    {
        lineText ??= "";
        if (lineText.Length == 0) return [];
        return [new ConsoleColorSpan(0, lineText.Length, _palette.ForType(ConsoleLineClassifier.Classify(lineText)))];
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.Length == 0) return;
        string text = CurrentContext.Document.GetText(line);
        foreach (var span in ComputeSpans(text))
        {
            int start = line.Offset + span.Offset;
            int end = start + span.Length;
            Brush brush = span.Brush;
            ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}
