namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [design v1.19.0 · prototip <c>DialogShell</c>] Modal çerçevesinin host'a göre kelepçesinin SAF (WPF'siz)
/// hesabı — tasarımın <c>maxWidth/maxHeight: calc(100% - 48px)</c> kuralı. Hesap TEK yerdedir (kopya YASAK);
/// çağıran (<see cref="ModalDialog"/>) host yeniden boyutlandıkça bunu yeniden uygular.
/// </summary>
public static class DialogSize
{
    /// <summary>Host kenarları ile dialog arasında toplamda bırakılan boşluk (her yanda 24px).</summary>
    public const double HostGutter = 48.0;

    /// <summary>Tasarım ölçüsü ile <c>host − 48</c>'in KÜÇÜK olanı. <paramref name="design"/> <c>NaN</c> ise
    /// (ölçü içerikten doğar) sonuç doğrudan <c>host − 48</c>'dir; host oluktan küçükse 0'ın altına inilmez.</summary>
    public static double Clamp(double design, double host)
    {
        double room = Math.Max(0, host - HostGutter);
        return double.IsNaN(design) ? room : Math.Min(design, room);
    }
}
