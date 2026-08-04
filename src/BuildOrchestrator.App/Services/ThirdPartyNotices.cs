using System.IO;
using System.Reflection;

namespace BuildOrchestrator.App.Services;

/// <summary>[About] Bir üçüncü-taraf bileşen. <paramref name="AssemblyName"/> <c>null</c> ise yönetilen bir
/// assembly değildir (gömülü font) — sürüm alanı boş kalır.</summary>
public readonly record struct ThirdPartyComponent(
    string DisplayName, string? AssemblyName, string License, string Url);

/// <summary>
/// [About] Üçüncü-taraf atıfları. <b>Sürüm burada YAZILMAZ</b> — çalışma zamanında yüklü assembly'den okunur;
/// csproj'daki <c>Version</c> değerini UI'da ikinci kez yazmak kopya olurdu.
///
/// <para><see cref="ThirdPartyComponent.DisplayName"/> csproj'daki <c>PackageReference Include</c> ile AYNI
/// yazılır: bir paket eklenip atıfı unutulursa <c>ThirdPartyNoticesTests</c> yakalar.</para>
/// </summary>
public static class ThirdPartyNotices
{
    /// <summary>OFL'in "included in all copies" şartı dosya olarak karşılanır (<c>Assets/GEIST-LICENSE.txt</c>,
    /// csproj onu hem build hem publish çıktısına kopyalar); bu not atfı GÖRÜNÜR kılar.</summary>
    public const string FontLicenseNote =
        "The full Geist license text ships as GEIST-LICENSE.txt next to the application.";

    public static IReadOnlyList<ThirdPartyComponent> All { get; } =
    [
        new("AvalonEdit", "ICSharpCode.AvalonEdit", "MIT", "https://github.com/icsharpcode/AvalonEdit"),
        new("CommunityToolkit.Mvvm", "CommunityToolkit.Mvvm", "MIT", "https://github.com/CommunityToolkit/dotnet"),
        new("H.NotifyIcon.Wpf", "H.NotifyIcon.Wpf", "MIT", "https://github.com/HavenDV/H.NotifyIcon"),
        new("Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection", "MIT",
            "https://github.com/dotnet/runtime"),
        new("Geist · Geist Mono", null, "SIL Open Font License 1.1", "https://github.com/vercel/geist-font"),
    ];

    /// <summary>Yüklü assembly'nin sürümü — <c>InformationalVersion</c> tercih edilir, build metadata
    /// (<c>+sha</c>) kırpılır. Bulunamazsa <c>null</c>: satır sürümsüz çizilir, About bir paket yüzünden
    /// AÇILMAMAZLIK etmez.</summary>
    public static string? ResolveVersion(ThirdPartyComponent component)
    {
        if (component.AssemblyName is not { Length: > 0 } name) return null;
        try
        {
            var assembly = Assembly.Load(new AssemblyName(name));
            string? version =
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString(3);
            if (version is null) return null;

            int plus = version.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? version : version[..plus];
        }
        catch (Exception ex) when (ex is FileNotFoundException or BadImageFormatException or FileLoadException)
        {
            return null;
        }
    }
}
