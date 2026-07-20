namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/3b] Chunk loader'ın SAF scroll-telafi matematiği (feasibility §3.6). Proje logunun son
/// ~128-256KB'i diskten yüklenir; kullanıcı TEPEYE yaklaşınca önceki chunk prepend edilir ve viewport
/// zıplamasın diye <see cref="CompensatedOffset"/> ile <c>VerticalOffset</c>, prepend edilen içeriğin piksel
/// yüksekliği kadar artırılır.
///
/// <para>Dikişin kendisi (tekrar/kayıp yok) <see cref="ConsoleView.PrependPreviousChunk"/>'ta contiguous
/// (sequence-id bitişik) dilim insert'iyle sağlanır; disk-chunk ↔ canlı-tampon sınır dedup'ı ise
/// <c>RunViewModel.OnProjectLogChunk</c>'ta (LineNumber &gt; ThroughLineNumber) yapılır. Bu tip yalnız
/// render'sız, saf test edilebilir offset matematiğini taşır (piksel yüksekliği çağırandan gelir —
/// AvalonEdit <c>TextView.DefaultLineHeight</c> deltası).</para>
/// </summary>
public static class ChunkStitch
{
    /// <summary>Tepeye chunk prepend edildiğinde viewport'u sabit tutan yeni <c>VerticalOffset</c>: üstteki
    /// içerik <paramref name="prependedPixelHeight"/> kadar büyüdü → offset o kadar artırılır.</summary>
    public static double CompensatedOffset(double currentVerticalOffset, double prependedPixelHeight)
        => currentVerticalOffset + Math.Max(0.0, prependedPixelHeight);
}
