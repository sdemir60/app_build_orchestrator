using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.Core.Incremental;

/// <summary>
/// Bir projenin TEK bir girdi dosyası — imzanın yol terimi de içerik terimi de bu yoldan türetilir.
/// </summary>
/// <param name="Path">Dosyanın tam yolu (çalışma ağacında; okunan dosya da budur).</param>
public readonly record struct ProjectInput(string Path);

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

    /// <param name="projectFile">Projenin kimlik yolu (tam csproj yolu).</param>
    /// <param name="evaluated">Bu projenin değerlendirmesi; yoksa yalnız klasör taraması ve csproj kalır.</param>
    /// <param name="workspaceRoot">Çalışma alanı kökü — yukarı yürüme burada durur (proje kökün altındaysa).</param>
    /// <returns>Yola göre tekilleştirilmiş, sıralı (deterministik) girdi listesi.</returns>
    public static IReadOnlyList<ProjectInput> Collect(string projectFile, EvaluatedProject? evaluated, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(projectFile);
        ArgumentNullException.ThrowIfNull(workspaceRoot);

        var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string path)
        {
            string full = Path.GetFullPath(path);
            if (!BuildSignature.IsBuildAffecting(full) || WorkspaceScanner.IsTransientBuildArtifact(full)) return;
            paths.Add(full);
        }

        string project = Path.GetFullPath(projectFile);
        Add(project);

        if (evaluated is not null)
        {
            foreach (string f in evaluated.CompileFiles) Add(f);
            foreach (string f in evaluated.ResourceFiles) Add(f);
        }

        string projectDir = Path.GetDirectoryName(project)!;
        SweepFolder(projectDir, paths);
        WalkUp(projectDir, workspaceRoot, paths);

        return [.. paths.Select(p => new ProjectInput(p))];
    }

    /// <summary>
    /// Proje klasörünün altındaki derleme-etkileyen dosyalar.
    ///
    /// <para>Yürüyüş elle yapılır çünkü <c>obj</c>/<c>bin</c> dizinlerine HİÇ GİRİLMEMELİDİR: onları
    /// enumerate edip sonra elemek, bir derleme çıktısındaki binlerce dosyayı boşuna gezmek olurdu.</para>
    /// </summary>
    private static void SweepFolder(string projectDir, SortedSet<string> into)
    {
        var pending = new Stack<string>();
        pending.Push(projectDir);

        while (pending.Count > 0)
        {
            string dir = pending.Pop();

            try
            {
                foreach (string file in Directory.EnumerateFiles(dir))
                {
                    if (!BuildSignature.IsBuildAffecting(file) || WorkspaceScanner.IsTransientBuildArtifact(file)) continue;
                    into.Add(Path.GetFullPath(file));
                }

                foreach (string sub in Directory.EnumerateDirectories(dir))
                {
                    if (IsBuildOutputFolder(Path.GetFileName(sub))) continue;
                    pending.Push(sub);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Kaybolan/okunamayan alt dizin taramayı düşürmez — yalnız o dal eksik kalır (güvenli taraf:
                // eksik terim imzayı değiştirir, proje yeniden derlenir).
            }
        }
    }

    /// <summary>Directory.Build.* araması: proje klasöründen köke doğru yukarı yürünür.</summary>
    private static void WalkUp(string projectDir, string workspaceRoot, SortedSet<string> into)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        var remaining = new List<string>(DirectoryLevelFileNames);
        var dir = new DirectoryInfo(projectDir);

        while (dir is not null && remaining.Count > 0)
        {
            foreach (string name in remaining.ToList())
            {
                string candidate = Path.Combine(dir.FullName, name);
                if (!File.Exists(candidate)) continue;
                into.Add(candidate);
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
