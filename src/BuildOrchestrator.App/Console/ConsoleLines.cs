using System.Text;

namespace BuildOrchestrator.App.Console;

/// <summary>
/// [idle-memory · dalga 1] Konsolun satır sözleşmesinin TEK tanımı (kopya YASAK): metni satırlara bölme kuralı ve satırları
/// '\n' SONEKLİ birleştirme. Anlatı store'u (<see cref="NarrativeStore"/>), görünümün satır listesi (<see cref="ListBacklog"/>),
/// konsol görünümü (kırpılan canlı kuyruk) ve kabuğun proje sayfası (logun sayfa satırları — <c>MainWindow.ProjectDocumentLines</c>)
/// bu iki yardımcıyı paylaşır.
///
/// <para><b>Ayraç kuralı AvalonEdit'inkidir:</b> <c>"\r\n"</c> TEK ayraçtır, tek başına <c>'\r'</c> ve <c>'\n'</c> birer
/// ayraçtır. Belge satırları bu kuralla sayılır; backlog indeksleri belge satır numaralarıyla birebir yürüdüğü için bölme
/// kuralı başka olamaz (ayrışırsa chunk loader yanlış dilimi getirir).</para>
/// </summary>
public static class ConsoleLines
{
    /// <summary><paramref name="lines"/>[from, to) satırlarını '\n' SONEKLİ birleştirir (append/dikiş sözleşmesi — metin tam
    /// satırla biter). Sınırlar listeye kırpılır; boş aralık ⇒ <c>""</c>.</summary>
    public static string Join(IReadOnlyList<string> lines, int from, int to)
    {
        to = Math.Min(to, lines.Count);
        from = Math.Max(0, from);
        if (from >= to) return "";
        var sb = new StringBuilder();
        for (int i = from; i < to; i++) sb.Append(lines[i]).Append('\n');
        return sb.ToString();
    }

    /// <summary>Metni satırlara böler; ayraçlar ATILIR (<see cref="Join"/> onları '\n' olarak geri koyar). Bir ayraç önündeki
    /// satırı BİTİRİR: ayraçla biten metnin ardında boş bir satır doğmaz — canlı metin '\n' SONEKLİDİR, son ayraç bir satır değil
    /// bir sonektir. Ayraçla bitmeyen son parça da bir satırdır (savunmacı). Boş metin → boş liste.</summary>
    public static List<string> Split(string text)
    {
        var lines = new List<string>();
        int start = 0;
        while (start < text.Length)
        {
            int separator = text.AsSpan(start).IndexOfAny('\r', '\n');
            if (separator < 0) { lines.Add(text[start..]); break; }
            int end = start + separator;
            lines.Add(text[start..end]);
            bool crlf = text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n';
            start = end + (crlf ? 2 : 1); // "\r\n" TEK ayraç
        }
        return lines;
    }
}
