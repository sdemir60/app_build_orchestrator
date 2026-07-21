using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace BuildOrchestrator.App.Console;

/// <summary>Bir satır İÇİNDE renkli bir aralık (line.Offset'e göre değil, satır-yerel offset) + brush.</summary>
public readonly record struct ConsoleColorSpan(int Offset, int Length, Brush Brush);

/// <summary>
/// [T56/3a] design-v1 §3.6 mimarisi: read-only AvalonEdit + <see cref="DocumentColorizingTransformer"/> ile
/// satır-offset bazlı renklendirme. Belge DÜZ metin tutar — renk yalnız GÖRSEL bir katmandır (kopyalanan metin
/// anlamlı kalsın diye asla markup gömülmez). Saat=faint, ▸=amber, gerisi satır tipine göre boyanır.
///
/// <para><see cref="ComputeSpans"/> SAF'tır (metin → offset+brush aralıkları) ve <see cref="ColorizeLine"/> ile
/// aynı hesabı kullanır — böylece render'sız (headless) test edilebilir. ColorizeLine yalnız <c>ChangeLinePart</c>
/// çağırır (görsel), belgeyi ASLA mutasyona uğratmaz.</para>
/// </summary>
public sealed class ConsoleColorizer : DocumentColorizingTransformer
{
    private readonly ConsolePalette _palette;

    public ConsoleColorizer(ConsolePalette palette) => _palette = palette;

    /// <summary>Verilen satır metni için offset-bazlı renk aralıkları (satır-yerel, ardışık, çakışmasız).
    /// clock=faint, ▸=amber, gerisi tip rengi. Boş metin → boş liste.</summary>
    public IReadOnlyList<ConsoleColorSpan> ComputeSpans(string? lineText)
    {
        lineText ??= "";
        if (lineText.Length == 0) return [];

        var layout = ConsoleLineParser.Layout(lineText);
        Brush body = _palette.ForType(layout.Type);
        var spans = new List<ConsoleColorSpan>(3);
        int pos = 0;

        if (layout.Clock is { } clock)
        {
            spans.Add(new ConsoleColorSpan(clock.Offset, clock.Length, _palette.Clock));
            pos = clock.Offset + clock.Length;
        }
        if (layout.Icon is { } icon)
        {
            if (icon.Offset > pos) spans.Add(new ConsoleColorSpan(pos, icon.Offset - pos, body));
            spans.Add(new ConsoleColorSpan(icon.Offset, icon.Length, _palette.Icon));
            pos = icon.Offset + icon.Length;
        }
        if (pos < lineText.Length) spans.Add(new ConsoleColorSpan(pos, lineText.Length - pos, body));
        return spans;
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
