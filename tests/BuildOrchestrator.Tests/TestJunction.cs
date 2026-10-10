using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace BuildOrchestrator.Tests;

/// <summary>
/// Junction (dizin bağlantısı) kuran ORTAK test yardımcısı: silme yüzeylerinin bağlantı güvenliğini pinleyen testler
/// (Clean'in bin/obj silmesi, koşu logu saklama budaması) aynı yolu kullanır — kopya YASAK. Symlink'in aksine
/// yükseltilmiş hak gerektirmez ama yine de her ortamda çalışmaz: başarısızlık testi ATLATIR (<c>Skip.IfNot</c>),
/// gizlice yeşile boyamaz.
/// </summary>
internal static class TestJunction
{
    /// <summary>
    /// <c>mklink /J</c> ile <paramref name="link"/> yoluna <paramref name="target"/>'ı gösteren bir junction kurar ve
    /// kurulduysa <c>true</c> döner. Bağlantının üst klasörü yoksa yaratılır.
    /// </summary>
    public static bool TryCreate(string link, string target)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            process.WaitForExit(10_000);
            return process.HasExited && process.ExitCode == 0 && Directory.Exists(link);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Bağlantıyı (hedefine dokunmadan) kaldırır; yoksa sessizce geçer. Temizlik içindir: <c>Directory.Delete(recursive)</c>
    /// içinde junction olan bir klasörde bu makinede (Windows, .NET 10) <see cref="UnauthorizedAccessException"/> fırlatır;
    /// junction'lı bir test klasörü silinmeden önce bağlantı buradan kaldırılır.
    /// </summary>
    public static void Remove(string link)
    {
        if (Directory.Exists(link)) Directory.Delete(link, recursive: false);
    }
}
