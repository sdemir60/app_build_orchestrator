namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [design v1.19.0 §2.11] CSS <c>position: sticky; top: 0</c>'ın SAF (WPF'siz, test edilebilir) kararı. WPF'te
/// sticky yoktur; What's new'in sürüm kimliği kolonu gövdenin <c>ScrollChanged</c>'inde bu ofsetle
/// <c>TranslateTransform</c> alır (bkz. <see cref="Views.NotesDialog"/>).
/// </summary>
public static class StickyColumn
{
    /// <summary>Kolonun bloğu içindeki dikey kayması: <c>clamp(scrollTop − blockTop, 0, blockHeight − columnHeight)</c>.
    /// Bloğun üstü görünür oldukça 0; blok yukarı kaydıkça viewport'un üstüne yapışır; bloğun altını hiç aşmaz.
    /// Blok kolondan kısaysa üst sınır negatiftir ve sonuç 0'dır (<see cref="Math.Clamp(double, double, double)"/>
    /// bu durumda istisna atardı — sınırlar bu yüzden elle uygulanır).</summary>
    /// <param name="scrollTop">Gövdenin dikey kayma ofseti.</param>
    /// <param name="blockTop">Bloğun kayan içerikteki üst kenarı.</param>
    /// <param name="blockHeight">Bloğun yüksekliği.</param>
    /// <param name="columnHeight">Yapışan kolonun yüksekliği.</param>
    public static double Offset(double scrollTop, double blockTop, double blockHeight, double columnHeight)
        => Math.Max(0, Math.Min(scrollTop - blockTop, blockHeight - columnHeight));
}
