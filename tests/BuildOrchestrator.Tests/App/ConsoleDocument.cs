using ICSharpCode.AvalonEdit.Document;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · group B re-review] Konsol belgesini satır listesi olarak okumanın TEK yeri (kopya YASAK): anlatı store'unun
/// "bir giriş = belgede bir satır" kuralını AvalonEdit'in kendisine karşı (<c>NarrativeStoreTests</c>) ve proje sayfasının dikişini
/// bir kâhin belgeye karşı (<c>ConsoleModesTests</c>) sınayan iki test aynı ifadeyi ayrı ayrı yazmıştı.
/// </summary>
internal static class ConsoleDocument
{
    /// <summary>Belgenin gösterdiği satırlar — son satır ('\n' sonrası boş satır) hariç; ayraçlar atılır.</summary>
    public static List<string> LinesOf(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return [.. document.Lines.Take(document.LineCount - 1).Select(document.GetText)];
    }
}
