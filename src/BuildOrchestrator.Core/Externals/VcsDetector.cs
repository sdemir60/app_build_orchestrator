using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Core.Externals;

/// <summary>Bir harici projenin çalışma kopyası kökü ve o kökün sürüm kontrol türü.</summary>
/// <param name="Kind">Bulunan sürüm kontrol türü; işaret yoksa <see cref="VcsKind.Unknown"/>.</param>
/// <param name="RootPath">Çalışma kopyasının kökü; <see cref="VcsKind.Unknown"/> ise null.</param>
public sealed record VcsRoot(VcsKind Kind, string? RootPath);

/// <summary>
/// [D3] Kök keşfi: kullanıcı harici projenin dizinini verir, çalışma kopyasının kökü oradan yukarı
/// yürünerek bulunur. İlk rastlanan işaret kazanır — iç içe çalışma kopyalarında EN YAKIN kök doğrudur.
///
/// <para>Sonuç hiçbir yerde persist EDİLMEZ: her koşuda diskten yeniden bulunur, böylece kullanıcı projeyi
/// taşıdığında ya da çalışma kopyasını yeniden kurduğunda bayat bir kök kalmaz.</para>
/// </summary>
public static class VcsDetector
{
    /// <summary>git'in çalışma kopyası işareti. Linked worktree'lerde bu bir DOSYADIR (gitdir yönlendirmesi),
    /// normal klonlarda dizin — ikisi de geçerli kök işaretidir.</summary>
    private const string GitMarker = ".git";

    /// <summary>TFVC local workspace'in metadata dizini.</summary>
    private const string TfvcMarker = "$tf";

    /// <summary>
    /// <paramref name="startDirectory"/>'den başlayıp sürücü köküne kadar yukarı yürür ve ilk işaretin
    /// türüyle birlikte onu taşıyan dizini döner. Boş, var olmayan ya da hiçbir işaret barındırmayan bir
    /// yol için <c>(Unknown, null)</c> döner — çağıran bunu "sürüm kontrolü yok" diye okur.
    /// </summary>
    public static VcsRoot DetectRoot(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory)) return new VcsRoot(VcsKind.Unknown, null);

        DirectoryInfo? current;
        try
        {
            current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new VcsRoot(VcsKind.Unknown, null); // ayrıştırılamayan yol = işaret yok
        }

        if (!current.Exists) return new VcsRoot(VcsKind.Unknown, null);

        for (; current is not null; current = current.Parent)
        {
            // .git dizin VEYA dosya olabilir — ikisi de aynı anlama gelir.
            if (Directory.Exists(Path.Combine(current.FullName, GitMarker))
                || File.Exists(Path.Combine(current.FullName, GitMarker)))
                return new VcsRoot(VcsKind.Git, current.FullName);

            if (Directory.Exists(Path.Combine(current.FullName, TfvcMarker)))
                return new VcsRoot(VcsKind.Tfvc, current.FullName);
        }

        return new VcsRoot(VcsKind.Unknown, null);
    }
}
