using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Core.Externals;

/// <summary>
/// [D3 · design v1.14.0 §9] Kök keşfi: kullanıcı harici projenin yolunu ve sürüm kontrol türünü verir (Source
/// seçimi), çalışma kopyasının kökü o yoldan yukarı yürünerek bulunur — ama yalnız SEÇİLEN türün işareti
/// aranır. Tür tespit EDİLMEZ: kullanıcı "TFVC" dediyse yolun üstündeki bir <c>.git</c> dizini önemsizdir;
/// aranan yalnız <c>$tf</c>'dir. İlk rastlanan işaret kazanır — iç içe çalışma kopyalarında EN YAKIN kök doğrudur.
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
    /// <paramref name="startDirectory"/>'den başlayıp sürücü köküne kadar yukarı yürür ve <paramref name="kind"/>
    /// türünün ilk işaretini taşıyan dizini döner. Boş, var olmayan ya da o işareti hiç barındırmayan bir yol
    /// için <c>null</c> döner — çağıran bunu "seçilen türde çalışma kopyası yok" diye okur (güncelleme ve kir
    /// kapısı çalışmaz, proje olduğu gibi derlenir).
    /// </summary>
    public static string? FindRoot(string startDirectory, VcsKind kind)
    {
        if (string.IsNullOrWhiteSpace(startDirectory)) return null;

        DirectoryInfo? current;
        try
        {
            current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null; // ayrıştırılamayan yol = işaret yok
        }

        if (!current.Exists) return null;

        for (; current is not null; current = current.Parent)
            if (HasMarker(current.FullName, kind)) return current.FullName;

        return null;
    }

    private static bool HasMarker(string directory, VcsKind kind) => kind switch
    {
        // .git dizin VEYA dosya olabilir — ikisi de aynı anlama gelir.
        VcsKind.Git => Directory.Exists(Path.Combine(directory, GitMarker)) || File.Exists(Path.Combine(directory, GitMarker)),
        VcsKind.Tfvc => Directory.Exists(Path.Combine(directory, TfvcMarker)),
        _ => false,
    };
}
