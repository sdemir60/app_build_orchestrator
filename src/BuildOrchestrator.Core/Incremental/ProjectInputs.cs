using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.Core.Incremental;

/// <summary>
/// Bir projenin TEK bir girdi dosyası — iki yolu vardır ve ikisi de gereklidir.
/// </summary>
/// <param name="LogicalPath">Kimlik yolu: imzanın yol terimi buradan türetilir. Worktree koşusunda da ANA
/// köke aittir (bkz. <see cref="BuildOrchestrator.Core.Planning.ProjectIdentityRebase"/>) — aynı içerik iki
/// modda AYNI imzayı üretsin diye.</param>
/// <param name="PhysicalPath">Diskte GERÇEKTEN okunacak yol. In-place koşuda <see cref="LogicalPath"/> ile
/// aynıdır; worktree koşusunda havuzdaki kopyayı gösterir.</param>
public readonly record struct ProjectInput(string LogicalPath, string PhysicalPath);

/// <summary>
/// [D2] Bir projenin İÇERİK KARARINA giren dosyalarının TAM kümesi. Karar diskten verildiği için bu küme
/// "neyin değişmesi projeyi bayatlatır" sorusunun tek cevabıdır.
///
/// <para><b>Küme dört kaynaktan gelir:</b> (1) <c>.csproj</c>'un kendisi; (2) csproj'un BİLDİRDİĞİ öğeler
/// (<see cref="EvaluatedProject.CompileFiles"/> ve <see cref="EvaluatedProject.ResourceFiles"/>) — bunlar
/// proje klasörünün DIŞINA link verilmiş dosyaları da yakalar; (3) proje klasörü altındaki derleme-etkileyen
/// uzantılı TÜM dosyalar (<c>obj</c>/<c>bin</c> hariç) — csproj'da bildirilmemiş ama derlemeye giren, git'e
/// eklenmemiş ya da gitignore'lanmış dosyalar ancak böyle görünür; (4) proje klasöründen yukarı yürürken
/// bulunan <c>Directory.Build.props</c> / <c>Directory.Build.targets</c> / <c>Directory.Packages.props</c>.</para>
///
/// <para><b>Yukarı yürüme MSBuild'in kuralını izler:</b> her ad için İLK bulunan dosya alınır ve o adın
/// araması orada biter. Arama, proje çalışma alanı kökünün altındaysa kökte durur; harici bir kökten gelen
/// proje için sürücü köküne kadar sürebilir (birkaç ucuz <c>File.Exists</c>).</para>
///
/// <para><b>Klasör taraması yuvalanmış projeleri de görür:</b> bir projenin klasörü başka bir projeyi
/// içeriyorsa alttakinin dosyaları üsttekinin kümesine de girer — üstteki gereğinden fazla derlenir
/// (güvenli taraf), asla az derlenmez. MSBuild'in SDK glob'u da aynı şekilde davranır.</para>
///
/// <para>Geçici derleme artıkları (WPF'in <c>*_wpftmp.csproj</c>'u) kümeye ALINMAZ: canlı bir build sırasında
/// var olup kaybolan bu dosyalar imzayı koşudan koşuya oynatırdı.</para>
/// </summary>
public static class ProjectInputs
{
    /// <summary>Proje klasöründen yukarı yürürken aranan MSBuild dosyaları — her ad için ilk bulunan alınır.</summary>
    public static readonly IReadOnlyList<string> DirectoryLevelFileNames =
        ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"];

    /// <param name="projectFile">Projenin KİMLİK yolu (tam csproj yolu).</param>
    /// <param name="evaluated">Bu projenin değerlendirmesi; yoksa yalnız klasör taraması ve csproj kalır.</param>
    /// <param name="workspaceRoot">Çalışma alanı kökü — yukarı yürüme burada durur (proje kökün altındaysa).</param>
    /// <param name="toPhysical">Kimlik yolu → diskteki gerçek yol. <c>null</c> ⇒ in-place (birebir).</param>
    /// <returns>Kimlik yoluna göre tekilleştirilmiş, sıralı (deterministik) girdi listesi.</returns>
    public static IReadOnlyList<ProjectInput> Collect(
        string projectFile, EvaluatedProject? evaluated, string workspaceRoot, Func<string, string>? toPhysical = null)
    {
        ArgumentNullException.ThrowIfNull(projectFile);
        ArgumentNullException.ThrowIfNull(workspaceRoot);

        Func<string, string> physical = toPhysical ?? (p => p);
        var byLogical = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Add(string logical)
        {
            string full = Path.GetFullPath(logical);
            if (!BuildSignature.IsBuildAffecting(full) || WorkspaceScanner.IsTransientBuildArtifact(full)) return;
            byLogical[full] = Path.GetFullPath(physical(full));
        }

        string project = Path.GetFullPath(projectFile);
        Add(project);

        if (evaluated is not null)
        {
            foreach (string f in evaluated.CompileFiles) Add(f);
            foreach (string f in evaluated.ResourceFiles) Add(f);
        }

        string logicalDir = Path.GetDirectoryName(project)!;
        SweepFolder(logicalDir, Path.GetFullPath(physical(logicalDir)), byLogical);
        WalkUp(logicalDir, workspaceRoot, physical, byLogical);

        return [.. byLogical.Select(kv => new ProjectInput(kv.Key, kv.Value))];
    }

    /// <summary>
    /// Proje klasörünün altındaki derleme-etkileyen dosyalar — FİZİKSEL ağaç taranır, kimlikler mantıksal
    /// klasörle yeniden kurulur (worktree koşusunda derlenen ağaç odur).
    ///
    /// <para>Yürüyüş elle yapılır çünkü <c>obj</c>/<c>bin</c> dizinlerine HİÇ GİRİLMEMELİDİR: onları
    /// enumerate edip sonra elemek, bir derleme çıktısındaki binlerce dosyayı boşuna gezmek olurdu.</para>
    /// </summary>
    private static void SweepFolder(string logicalDir, string physicalDir, SortedDictionary<string, string> into)
    {
        // (mantıksal, fiziksel) çiftleri birlikte yürür — iki ağaç aynı göreli yapıdadır.
        var pending = new Stack<(string Logical, string Physical)>();
        pending.Push((logicalDir, physicalDir));

        while (pending.Count > 0)
        {
            var (logical, disk) = pending.Pop();

            try
            {
                foreach (string file in Directory.EnumerateFiles(disk))
                {
                    if (!BuildSignature.IsBuildAffecting(file) || WorkspaceScanner.IsTransientBuildArtifact(file)) continue;
                    into[Path.Combine(logical, Path.GetFileName(file))] = file;
                }

                foreach (string sub in Directory.EnumerateDirectories(disk))
                {
                    string name = Path.GetFileName(sub);
                    if (IsBuildOutputFolder(name)) continue;
                    pending.Push((Path.Combine(logical, name), sub));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Kaybolan/okunamayan alt dizin taramayı düşürmez — yalnız o dal eksik kalır (güvenli taraf:
                // eksik terim imzayı değiştirir, proje yeniden derlenir).
            }
        }
    }

    /// <summary>Directory.Build.* araması: mantıksal ve fiziksel ağaçta AYNI ADIMLARLA yukarı yürünür.</summary>
    private static void WalkUp(
        string logicalDir, string workspaceRoot, Func<string, string> physical, SortedDictionary<string, string> into)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        var remaining = new List<string>(DirectoryLevelFileNames);
        var dir = new DirectoryInfo(logicalDir);

        while (dir is not null && remaining.Count > 0)
        {
            foreach (string name in remaining.ToList())
            {
                string logical = Path.Combine(dir.FullName, name);
                string onDisk = Path.GetFullPath(physical(logical));
                if (!File.Exists(onDisk)) continue;
                into[logical] = onDisk;
                remaining.Remove(name);
            }

            if (string.Equals(Path.TrimEndingDirectorySeparator(dir.FullName), root, StringComparison.OrdinalIgnoreCase))
                return;
            dir = dir.Parent;
        }
    }

    private static bool IsBuildOutputFolder(string folderName) =>
        folderName.Equals("obj", StringComparison.OrdinalIgnoreCase)
        || folderName.Equals("bin", StringComparison.OrdinalIgnoreCase);
}
