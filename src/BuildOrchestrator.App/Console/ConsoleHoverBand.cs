namespace BuildOrchestrator.App.Console;

/// <summary>
/// [Task 6/design v1.17.0 §9 "3 — Konsol ve event stream"] İmlecin altındaki konsol satırının hover bandı için
/// hedef Y aralığını hesaplayan SAF çekirdek. AvalonEdit'in <c>VisualLine</c> tipine bağlı DEĞİLDİR — yalnız
/// (Top, Height) çiftleri alır; renderer (<see cref="ConsoleView"/>) bunu gerçek <c>TextView.VisualLines</c>'tan
/// besler, testler saf listelerle çağırır (renderer'sız test edilebilirlik, brief §"Testler").
/// </summary>
public static class ConsoleHoverBand
{
    /// <summary>
    /// <paramref name="documentY"/> (kaydırma dahil, belgenin başından itibaren dikey konum) hangi satırın
    /// (Top, Height) aralığına düşüyorsa onu döner — <c>Top &lt;= documentY &lt; Top + Height</c>. Hiçbir satır
    /// kapsamıyorsa (mouse belge dışında/boş listede) <c>null</c>.
    /// </summary>
    public static (double Top, double Height)? LineAt(IReadOnlyList<(double Top, double Height)> lines, double documentY)
    {
        foreach (var line in lines)
            if (documentY >= line.Top && documentY < line.Top + line.Height)
                return line;
        return null;
    }
}
