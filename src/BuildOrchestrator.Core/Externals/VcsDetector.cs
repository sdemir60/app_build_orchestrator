namespace BuildOrchestrator.Core.Externals;

/// <summary>
/// [D3 · design v1.14.0 §9] Kök keşfi: kullanıcı harici projenin yolunu verir, çalışma kopyasının kökü o
/// yoldan yukarı yürünerek bulunur. İlk rastlanan <c>.git</c> kazanır — iç içe çalışma kopyalarında EN YAKIN
/// kök doğrudur.
///
/// <para><b>[DEĞİŞEN KURAL] Yalnız git aranır.</b> Kart eskiden bir kaynak türü (Git/TFVC) taşıyordu ve
/// aranan işaret o seçime göre <c>.git</c> ya da <c>$tf</c> oluyordu. TFVC kolu kaldırıldı (bkz.
/// <see cref="Contracts.Model.ExternalProject"/>), dolayısıyla tür parametresi de kalktı.</para>
///
/// <para>Sonuç hiçbir yerde persist EDİLMEZ: her koşuda diskten yeniden bulunur, böylece kullanıcı projeyi
/// taşıdığında ya da çalışma kopyasını yeniden kurduğunda bayat bir kök kalmaz.</para>
/// </summary>
public static class VcsDetector
{
    /// <summary>git'in çalışma kopyası işareti. Linked worktree'lerde bu bir DOSYADIR (gitdir yönlendirmesi),
    /// normal klonlarda dizin — ikisi de geçerli kök işaretidir.</summary>
    private const string GitMarker = ".git";

    /// <summary>
    /// <paramref name="startDirectory"/>'den başlayıp sürücü köküne kadar yukarı yürür ve ilk <c>.git</c>
    /// taşıyan dizini döner. Boş, var olmayan ya da hiç <c>.git</c> barındırmayan bir yol için <c>null</c>
    /// döner — çağıran bunu "çalışma kopyası yok" diye okur (güncelleme ve kir kapısı çalışmaz, proje olduğu
    /// gibi derlenir).
    /// </summary>
    public static string? FindRoot(string startDirectory)
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
            if (HasMarker(current.FullName)) return current.FullName;

        return null;
    }

    // .git dizin VEYA dosya olabilir — ikisi de aynı anlama gelir.
    private static bool HasMarker(string directory) =>
        Directory.Exists(Path.Combine(directory, GitMarker)) || File.Exists(Path.Combine(directory, GitMarker));
}
