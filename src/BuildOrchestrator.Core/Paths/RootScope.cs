namespace BuildOrchestrator.Core.Paths;

/// <summary>
/// "Bu yol şu workspace kökünün altında mı?" sorusunun TEK cevabı — kök-kapsamlı defter işlemlerinin ortak
/// kapısı. İki çağıran ailesi vardır: Clean'in <c>RemoveUnderRoot</c>'u (kök altındaki HER kaydı siler) ve
/// Optimize'ın <c>PruneMissingUnderRoot</c>'u (yalnız dosyası kaybolmuş kaydı siler). Normalizasyonun her
/// çağıranda yeniden yazılması, çağıran sayısı kadar farklı prefix tuzağı demek olurdu.
///
/// <para><b>Tuzak:</b> ham prefix karşılaştırması <c>C:\repo</c> köküne <c>C:\repo2\Y\Y.csproj</c>'yi de
/// katar — ad kökle BAŞLAR ama proje AYRI bir workspace'tedir. Bu yüzden kök daima sonuna ayraç eklenerek
/// normalize edilir. Karşılaştırma Windows dosya sistemine uygun şekilde OrdinalIgnoreCase'tir.</para>
///
/// <para><b>Never-throw:</b> çözülemeyen kök <c>null</c> döner; çağıran o durumda hiçbir şeye dokunmaz ve 0
/// raporlar. Defterlerin "hijyen adımı çağıranı asla düşürmez" sözleşmesi buna dayanır, bu yüzden catch
/// kümesi <see cref="System.IO.Path.GetFullPath(string)"/>'in üretebildiği her şeyi kapsar.</para>
/// </summary>
public static class RootScope
{
    /// <summary>Kökü mutlaklaştırır ve TEK bir sondaki ayraçla biten hâline getirir; çözülemeyen (boş/geçersiz)
    /// kök için <c>null</c> döner.</summary>
    public static string? NormalizeRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException
                                      or PathTooLongException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary><paramref name="path"/> <paramref name="normalizedRoot"/>'un (bkz. <see cref="NormalizeRoot"/>)
    /// ALTINDA mı? Kökün kendisi "altında" SAYILMAZ — defterlerin anahtarları hep dosya yollarıdır.</summary>
    public static bool Contains(string normalizedRoot, string path) =>
        path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
}
