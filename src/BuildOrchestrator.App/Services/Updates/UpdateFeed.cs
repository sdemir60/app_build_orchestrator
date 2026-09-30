using System.IO;
using Velopack.Sources;

namespace BuildOrchestrator.App.Services.Updates;

/// <summary>[motor] Güncelleme beslemesinin TEK yeri: GitHub Releases (public repo, token gerekmez). Dev/test kapıları
/// ortam değişkeninden (mevcut <c>BO_*</c> deseni): <see cref="SourceOverrideVariable"/> bir klasör (yerel feed —
/// GitHub'sız uçtan uca prova) ya da URL; <see cref="PrereleaseVariable"/>=1 prerelease'leri de görür (aşamalı yayın:
/// <c>vpk upload github --pre</c>). UI'da ayar yoktur.</summary>
public static class UpdateFeed
{
    public const string RepositoryUrl = "https://github.com/sdemir60/app_build_orchestrator";
    public const string SourceOverrideVariable = "BO_UPDATE_SOURCE";
    public const string PrereleaseVariable = "BO_UPDATE_PRERELEASE";

    /// <summary>Ölçüm testlerinin kapısıyla aynı kural: yalnız tam olarak "1".</summary>
    public static bool IsEnabled(string? flag) => flag == "1";

    /// <summary>Hiçbir Windows yolunda bulunamayacak karakterler. Windows'ta <c>Path.GetInvalidPathChars()</c> yalnız '|', NUL
    /// ve denetim karakterlerini verir; '"', '&lt;', '&gt;' ve '*' da hiçbir yol bileşeninde yer alamaz ama listede yoktur.
    /// ('?' bilerek yok: <c>\\?\</c> uzun yol önekinde geçerlidir.)</summary>
    private static readonly char[] NeverInAPath = [.. Path.GetInvalidPathChars(), '"', '<', '>', '*'];

    /// <summary>Kaynağı kurar. <b>Bozuk bir kapı değeri yok sayılır ve GitHub kaynağına dönülür</b> (ön sürüm kapısı
    /// geçerli kalır): kaynak, pencere görününce <c>UpdateService</c> çözümlenirken kurulur ve orada yakalayan yoktur —
    /// kırık bir ortam değişkeni yüzünden açılmayan bir uygulama, yanlış kaynaktan güncelleme almaktan kötüdür. Sessiz
    /// geri dönüş bilinçlidir: kapı yalnız geliştirme/test içindir, kullanıcıya görünen bir ayarı yoktur. Var olmayan ama
    /// geçerli bir klasör bozuk SAYILMAZ (yerel feed provası o klasörü ister; kontrol sessizce sonuçsuz kalır).</summary>
    public static IUpdateSource CreateSource(string? overrideSource, bool prerelease)
    {
        if (!string.IsNullOrWhiteSpace(overrideSource) && TryCreateOverride(overrideSource, prerelease) is { } custom) return custom;
        return new GithubSource(RepositoryUrl, accessToken: null, prerelease);
    }

    /// <summary>Kapı değerinden kaynak; değer bozuksa (geçersiz yol karakteri, çözümlenemeyen URL) <c>null</c>.</summary>
    private static IUpdateSource? TryCreateOverride(string value, bool prerelease)
    {
        try
        {
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return value.Contains("github.com/", StringComparison.OrdinalIgnoreCase)
                    ? new GithubSource(value, accessToken: null, prerelease)
                    : new SimpleWebSource(value);
            }
            // .NET Core DirectoryInfo yalnız NUL'u reddeder; diğerleri geçer ve kaynak sessizce hiçbir zaman bulunamayacak bir
            // yola işaret ederdi — açık denetim.
            if (value.IndexOfAny(NeverInAPath) >= 0) return null;
            return new SimpleFileSource(new DirectoryInfo(value)); // UpdateManager(string) de böyle çözer
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }
}
