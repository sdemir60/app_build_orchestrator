using System.Windows;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [kullanıcı kararı 2026-09-29] Sistem diyaloglarının (dosya seçici) konumu — SAF hesap, Win32 kancası
/// (<see cref="CenteredDialog"/>) yalnız sonucu uygular. Koordinatlar fiziksel pikseldir.
/// </summary>
internal static class DialogPlacement
{
    /// <summary>Verilen boyuttaki diyaloğu sahibinin (<paramref name="owner"/>) üzerinde ortalar ve çalışma alanının
    /// (görev çubuğu hariç monitör) içine kelepçeler: sahip ekran kenarına taşmışsa diyalog ekranda kalır; diyalog
    /// çalışma alanından büyükse sol üst köşesi alanın başına oturur (başlık çubuğu hep erişilebilir).</summary>
    /// <returns>Diyaloğun sol üst köşesi.</returns>
    public static (int X, int Y) CenterOver(Int32Rect owner, int width, int height, Int32Rect workArea)
    {
        int x = owner.X + (owner.Width - width) / 2;
        int y = owner.Y + (owner.Height - height) / 2;
        return (Clamp(x, workArea.X, workArea.X + workArea.Width - width),
                Clamp(y, workArea.Y, workArea.Y + workArea.Height - height));
    }

    // Math.Clamp min > max'ta fırlatır; diyalog alandan büyükse başlangıç kazanır.
    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(value, max));
}
