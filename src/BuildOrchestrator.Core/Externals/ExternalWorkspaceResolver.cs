using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.Core.Externals;

/// <summary>Taraması çözülmüş bir harici çalışma alanı kökü.</summary>
/// <param name="Project">Ayarlar'daki kart (yol + kaynak).</param>
/// <param name="Name">Kullanıcıya görünen ad — yolun son parçası (uzantısız).</param>
/// <param name="SearchRoot">Çalışma kopyası kökü aramasının başladığı dizin (bkz. <see cref="VcsDetector"/>).</param>
/// <param name="Scan">Bu kökten bulunan projeler ve solution'lar.</param>
public sealed record ExternalRoot(ExternalProject Project, string Name, string SearchRoot, ScanResult Scan);

/// <summary>
/// Ana repo taraması + harici köklerin taramaları, TEK bir çalışma alanı olarak. Grafın, incremental kararın
/// ve derlemenin gördüğü tek küme budur.
/// </summary>
/// <param name="Scan">Birleşik tarama — ana kök ve tüm harici kökler, tekilleştirilmiş ve sıralı.</param>
/// <param name="VcsByProjectId">YALNIZ harici projelerin id → kaynak eşlemesi. İki iş yapar: düğüme basılan
/// rozet ve "bu proje harici mi" sorusunun tek cevabı (imza kaynağı, obj izolasyonu kapısı).</param>
/// <param name="Roots">Çözülen kökler — build anındaki VCS güncellemesi bunları gezer.</param>
/// <param name="Problems">Hiçbir projeye çözülemeyen kartlar. <b>Sync bunları uyarı olarak yazar ve devam
/// eder; Build durur</b> — yapılandırılmış bir haricinin sessizce düşmesi, bayat DLL'e link'lenmiş yeşil bir
/// build demektir. Metin BURADA kurulmaz: iki yüzeyin cümlesi farklıdır (uyarı vs. iptal gerekçesi) ve her
/// biri kendi tek kaynağından gelir — <see cref="Planning.PlanProgressLines.ExternalNotScanned"/> ve
/// <see cref="ExternalPreparationException.NotScanned"/>.</param>
public sealed record ExternalWorkspace(
    ScanResult Scan,
    IReadOnlyDictionary<string, VcsKind> VcsByProjectId,
    IReadOnlyList<ExternalRoot> Roots,
    IReadOnlyList<ExternalScanProblem> Problems);

/// <summary>Bir harici kartın taranamama nedeni.</summary>
/// <param name="Project">Ayarlar'daki kart.</param>
/// <param name="Name">Kullanıcıya görünen ad.</param>
/// <param name="Problem">Tek cümlelik, küçük harfle başlayan neden — satıra gömülmek üzere.</param>
public sealed record ExternalScanProblem(ExternalProject Project, string Name, string Problem);

/// <summary>
/// [design v1.14.0 §9] Ayarlar'daki harici kartları TARANABİLİR köklere çevirir ve ana taramayla birleştirir.
///
/// <para><b>Yol üç biçimden biridir</b> ve üçü de her koşuda yeniden çözülür (hiçbiri persist edilmez):
/// bir KLASÖR (ana kök gibi recursive taranır), bir <c>.sln</c> (yalnız o solution'ın listelediği projeler —
/// klasörü taramak solution dışı kardeşleri de içeri alırdı) ya da bir <c>.csproj</c> (tek proje).</para>
///
/// <para><b>Hiçbir VCS komutu çalıştırmaz.</b> Kaynak (Git/TFVC) yalnız rozete ve build-anı güncelleme
/// adımına gider; burada dosya sisteminden başka bir şeye dokunulmaz — Sync'in hızlı ve çevrimdışı-toleranslı
/// kalması buna bağlıdır.</para>
/// </summary>
public static class ExternalWorkspaceResolver
{
    private const string SolutionExtension = ".sln";
    private const string ProjectExtension = ".csproj";

    /// <summary>Harici kart yoksa ana tarama olduğu gibi geçer — tek bir dizin bile okunmaz.</summary>
    public static ExternalWorkspace Resolve(
        ScanResult mainScan, IReadOnlyList<ExternalProject>? externals, WorkspaceScanner scanner)
    {
        ArgumentNullException.ThrowIfNull(mainScan);
        ArgumentNullException.ThrowIfNull(scanner);

        if (externals is not { Count: > 0 })
            return new ExternalWorkspace(mainScan, EmptyVcsMap(), [], []);

        var roots = new List<ExternalRoot>();
        var problems = new List<ExternalScanProblem>();
        var vcsByProjectId = new Dictionary<string, VcsKind>(StringComparer.OrdinalIgnoreCase);
        var csproj = new List<string>(mainScan.CsprojPaths);
        var sln = new List<string>(mainScan.SlnPaths);

        foreach (var project in externals)
        {
            string name = DisplayName(project.Path);
            var (scan, problem) = ScanOne(project.Path, scanner);
            if (scan is null)
            {
                problems.Add(new ExternalScanProblem(project, name, problem!));
                continue;
            }

            roots.Add(new ExternalRoot(project, name, SearchRootOf(project.Path), scan));
            foreach (string path in scan.CsprojPaths)
            {
                csproj.Add(path);
                // Aynı proje iki kartta görünürse İLK kartın kaynağı kazanır — ikinci bir yazım sessizce
                // rozeti değiştirirdi; sıra kullanıcının listesidir ve öngörülebilir olmalıdır.
                vcsByProjectId.TryAdd(path, project.Vcs);
            }
            sln.AddRange(scan.SlnPaths);
        }

        return new ExternalWorkspace(
            new ScanResult(Canonical(csproj), Canonical(sln)), vcsByProjectId, roots, problems);
    }

    /// <summary>
    /// Çalışma kopyası kökü aramasının başlayacağı dizin — yol bir DOSYAYSA onu içeren dizin, aksi halde
    /// yolun kendisi. Build-anı güncelleme adımı bunu HAM yol üzerinden çağırır: güncelleme taramadan ÖNCE
    /// koşar (bir fast-forward yeni proje dosyaları getirebilir), yani o anda henüz çözülmüş bir kök yoktur.
    /// </summary>
    public static string SearchRootOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path ?? string.Empty;

        try
        {
            string full = Path.GetFullPath(path.Trim());
            return File.Exists(full) ? Path.GetDirectoryName(full) ?? full : full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.Trim();
        }
    }

    /// <summary>Çözülemeyen bir yolun bile listede/uyarıda bir adı olmalı: yolun son parçası, uzantısız.</summary>
    public static string DisplayName(string path)
    {
        string trimmed = (path ?? string.Empty).Trim()
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string leaf = Path.GetFileName(trimmed);
        if (leaf.Length == 0) return trimmed;
        return IsTargetFile(leaf) ? Path.GetFileNameWithoutExtension(leaf) : leaf;
    }

    private static (ScanResult? Scan, string? Problem) ScanOne(string path, WorkspaceScanner scanner)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, "the path is empty");

        string full;
        try { full = Path.GetFullPath(path.Trim()); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (null, "the path is not valid");
        }

        try
        {
            if (File.Exists(full)) return ScanFile(full);
            if (!Directory.Exists(full)) return (null, "the path was not found");

            var scan = scanner.Scan(full);
            return scan.CsprojPaths.Count > 0
                ? (scan, null)
                : (null, "no project file was found in the folder");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, "the path could not be read");
        }
    }

    private static (ScanResult? Scan, string? Problem) ScanFile(string full)
    {
        string extension = Path.GetExtension(full);

        if (extension.Equals(SolutionExtension, StringComparison.OrdinalIgnoreCase))
        {
            var projects = SolutionMapper.ProjectsOf(full);
            return projects.Count > 0
                ? (new ScanResult(projects, [full]), null)
                : (null, "the solution lists no project files");
        }

        return extension.Equals(ProjectExtension, StringComparison.OrdinalIgnoreCase)
            ? (new ScanResult([full], []), null)
            : (null, "the path is not a solution or project file");
    }

    private static bool IsTargetFile(string file)
    {
        string extension = Path.GetExtension(file);
        return extension.Equals(SolutionExtension, StringComparison.OrdinalIgnoreCase)
            || extension.Equals(ProjectExtension, StringComparison.OrdinalIgnoreCase);
    }

    // Determinizm [D8]: WorkspaceScanner'ın kendi sözleşmesiyle AYNI — tekil ve OrdinalIgnoreCase sıralı.
    private static IReadOnlyList<string> Canonical(IEnumerable<string> paths) =>
        [.. paths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)];

    private static IReadOnlyDictionary<string, VcsKind> EmptyVcsMap() =>
        new Dictionary<string, VcsKind>(StringComparer.OrdinalIgnoreCase);
}
