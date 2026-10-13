namespace BuildOrchestrator.App.Console;

/// <summary>
/// [idle-memory · dalga 1 · spec §6.2] Koşu anlatısının bellekteki TEK sahibi. VM her satırı buraya ekler (IPC thread'i),
/// konsol görünümü dilim ister (UI thread'i); görünüm kendi kopyasını tutmaz (eski <c>_runText</c> + <c>_backlogLines</c>
/// çiftinin ölçülen bedeli: 83 k satırlık koşuda 55 + 59 MB). Satırlar '\n' içermez; <see cref="Join"/> '\n' sonekli metin
/// üretir (append/dikiş sözleşmesi).
///
/// <para><b>Bir giriş = belgede bir satır.</b> Görünüm belgenin ilk satırını store indeksiyle izler; canlı akışta kırpılan
/// belge satırlarını o indekse ekler. Bu yüzden çok satırlı bir ekleme (çok satırlı hata mesajı, git stderr) belgenin
/// göreceği satırlara bölünür — kural <see cref="ConsoleLines.Split"/>'tedir (AvalonEdit'in ayraç kuralı).</para>
///
/// <para><b>Kilit:</b> her erişim iç kilidi alır. VM ekleme yaparken kendi <c>_gate</c>'ini tutar (ekleme ile pompaya
/// gönderim aynı bölümdedir — reseed nesil guard'ı ona dayanır); sıra her yerde VM <c>_gate</c> → store kilididir, tersi
/// yoktur: store dışarı çağırmaz.</para>
/// </summary>
public sealed class NarrativeStore : IConsoleBacklog
{
    private readonly object _gate = new();
    private readonly List<string> _lines = [];
    private long _chars;

    public int Count { get { lock (_gate) return _lines.Count; } }

    /// <summary>Saklanan karakter sayısı (ayraçlar hariç) — bellek bütçesinin ölçüsü.</summary>
    public long CharCount { get { lock (_gate) return _chars; } }

    /// <summary>
    /// Bir anlatı satırı ekler. Satır ayraç taşımıyorsa olduğu gibi saklanır — sıcak yol, O(1): her MSBuild satırı. Taşıyorsa
    /// belgenin göreceği satırlara bölünür: pompa her satırın ardına '\n' koyar, belge eklemeyi <c>line + "\n"</c> olarak görür
    /// (sondaki tek '\r' o '\n' ile TEK ayraç olur).
    /// <para><c>null</c> boş satırdır: eski tampon (<c>StringBuilder.Append(null)</c>) ve pompa da öyle basar; IPC thread'inde
    /// fırlatmak koşunun olay akışını keserdi.</para>
    /// </summary>
    public void Append(string? line)
    {
        line ??= "";
        if (line.AsSpan().IndexOfAny('\r', '\n') < 0)
        {
            lock (_gate) { _lines.Add(line); _chars += line.Length; }
            return;
        }
        var pieces = ConsoleLines.Split(line + "\n");
        lock (_gate)
        {
            foreach (var piece in pieces) { _lines.Add(piece); _chars += piece.Length; }
        }
    }

    public void Clear() { lock (_gate) { _lines.Clear(); _chars = 0; } }

    public string Join(int from, int to)
    {
        lock (_gate) return ConsoleLines.Join(_lines, from, to);
    }

    /// <summary>[from, to) satırlarının KOPYASI (sınırlar kırpılır).</summary>
    public IReadOnlyList<string> Slice(int from, int to)
    {
        lock (_gate)
        {
            to = Math.Min(to, _lines.Count);
            from = Math.Max(0, from);
            return from >= to ? [] : _lines.GetRange(from, to - from);
        }
    }

    /// <summary>Store kırpılan satırları zaten taşır — kopya tutulmaz (<see cref="IConsoleBacklog.AcceptTrimmed"/>).</summary>
    public void AcceptTrimmed(IReadOnlyList<string> lines) { }
}
