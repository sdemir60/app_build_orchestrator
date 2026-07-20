namespace BuildOrchestrator.App.Console;

/// <summary>Bir log satırı + onun sequence-id'si (proje logunda satır numarası). Chunk dikişinin birimi.</summary>
public readonly record struct StitchLine(int SequenceId, string Text);

/// <summary>
/// [T56/3b] Chunk loader'ın SAF dikiş + scroll-telafi matematiği (feasibility §3.6). Proje logunun son
/// ~128-256KB'i diskten yüklenir; kullanıcı TEPEYE yaklaşınca önceki chunk prepend edilir ve viewport
/// zıplamasın diye <see cref="CompensatedOffset"/> ile <c>VerticalOffset</c>, prepend edilen içeriğin piksel
/// yüksekliği kadar artırılır. Chunk'lar <b>sequence-id</b> ile dikilir → tekrar YOK, kayıp YOK.
///
/// <para>Saf ve render'sız test edilebilir (offset piksel yüksekliği çağırandan gelir — AvalonEdit
/// <c>TextView.DocumentHeight</c> deltası; matematik burada).</para>
/// </summary>
public static class ChunkStitch
{
    /// <summary>Verilen parçaları (her biri artan sequence-id'li satırlar) sequence-id'ye göre birleştirir:
    /// sonuç artan-id sıralı ve her id yalnız BİR kez (aynı id'li ikinci satır yok sayılır — tekrar yok).
    /// Prepend (eski chunk, düşük id) ve append (yeni chunk, yüksek id) yönünden BAĞIMSIZ çalışır: sıradan
    /// bağımsız, kararlı, boşluk/tekrar üretmez.</summary>
    public static IReadOnlyList<StitchLine> Merge(params IReadOnlyList<StitchLine>[] parts)
    {
        var byId = new SortedDictionary<int, string>();
        foreach (var part in parts)
            foreach (var line in part)
                byId[line.SequenceId] = line.Text; // aynı id → üzerine yaz (tek kopya kalır)
        var result = new List<StitchLine>(byId.Count);
        foreach (var kv in byId) result.Add(new StitchLine(kv.Key, kv.Value));
        return result;
    }

    /// <summary>Tepeye chunk prepend edildiğinde viewport'u sabit tutan yeni <c>VerticalOffset</c>: üstteki
    /// içerik <paramref name="prependedPixelHeight"/> kadar büyüdü → offset o kadar artırılır.</summary>
    public static double CompensatedOffset(double currentVerticalOffset, double prependedPixelHeight)
        => currentVerticalOffset + Math.Max(0.0, prependedPixelHeight);
}
