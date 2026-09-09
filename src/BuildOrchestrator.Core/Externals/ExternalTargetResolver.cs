using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Core.Externals;

/// <summary>Bir harici projenin Ayarlar'daki yolundan çözülen derleme hedefi.</summary>
/// <param name="Name">Görünen ad — hedef dosyanın uzantısız adı (liste ve node etiketi).</param>
/// <param name="Directory">Hedefin dizini; çalışma kopyası kökü aramasının başladığı yer (bkz. <see cref="VcsDetector"/>).</param>
/// <param name="TargetPath">Derlenecek <c>.sln</c>/<c>.csproj</c>'un tam yolu — bu projenin kimliği.</param>
public sealed record ExternalTarget(string Name, string Directory, string TargetPath);

/// <summary>Çözümün sonucu: ya bir hedef ya da kullanıcıya söylenecek tek cümlelik bir sorun.</summary>
/// <param name="Target">Çözülen hedef; sorun varsa null.</param>
/// <param name="Problem">Kullanıcıya görünen İngilizce sorun cümlesi (küçük harfle başlar, satıra gömülür); hedef varsa null.</param>
public sealed record ExternalTargetResolution(ExternalTarget? Target, string? Problem);

/// <summary>
/// [design v1.14.0 §9] Ayarlar'daki bir harici kart "bir klasör, bir solution ya da bir proje dosyası" verir;
/// derlenecek hedef her koşuda o yoldan yeniden çözülür (hiçbir şey persist edilmez, bayatlayamaz):
/// <list type="bullet">
/// <item>Yol bir <c>.sln</c>/<c>.csproj</c> DOSYASIYSA hedef odur.</item>
/// <item>Yol bir KLASÖRSE hedef o klasördeki (alt dizinlere İNMEDEN) tek <c>.sln</c>, yoksa tek <c>.csproj</c>'dur.</item>
/// <item>Aksi her durum — yol yok, dosya solution/proje değil, klasörde aday yok ya da birden çok solution var —
/// bir SORUNDUR: tahmin yürütüp yanlış projeyi derletmektense kullanıcıdan yolu hedefe işaret ettirmek istenir.</item>
/// </list>
/// </summary>
public static class ExternalTargetResolver
{
    private const string SolutionExtension = ".sln";
    private const string ProjectExtension = ".csproj";

    public static ExternalTargetResolution Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Problem("the path is empty");

        string full;
        try
        {
            full = Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Problem("the path is not valid");
        }

        if (File.Exists(full))
            return IsTarget(full)
                ? Resolved(full)
                : Problem("the path is not a solution or project file");

        if (!Directory.Exists(full)) return Problem("the path was not found");

        try
        {
            string[] solutions = Directory.GetFiles(full, "*" + SolutionExtension, SearchOption.TopDirectoryOnly);
            if (solutions.Length == 1) return Resolved(solutions[0]);
            if (solutions.Length > 1) return Problem("the folder holds more than one solution — point the path at the one to build");

            string[] projects = Directory.GetFiles(full, "*" + ProjectExtension, SearchOption.TopDirectoryOnly);
            if (projects.Length == 1) return Resolved(projects[0]);
            return projects.Length > 1
                ? Problem("the folder holds more than one project — point the path at the one to build")
                : Problem("no solution or project file was found in the folder");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Problem("the folder could not be read");
        }
    }

    /// <summary>Çözülemeyen bir yolun bile listede bir adı olmalı (hollow satır): yolun son parçası, uzantısız.</summary>
    public static string DisplayName(string path)
    {
        string trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string leaf = Path.GetFileName(trimmed);
        if (leaf.Length == 0) return trimmed;
        return IsTarget(leaf) ? Path.GetFileNameWithoutExtension(leaf) : leaf;
    }

    private static bool IsTarget(string file)
    {
        string ext = Path.GetExtension(file);
        return ext.Equals(SolutionExtension, StringComparison.OrdinalIgnoreCase)
            || ext.Equals(ProjectExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static ExternalTargetResolution Resolved(string targetPath) => new(
        new ExternalTarget(Path.GetFileNameWithoutExtension(targetPath), Path.GetDirectoryName(targetPath)!, targetPath), null);

    private static ExternalTargetResolution Problem(string problem) => new(null, problem);
}
