using System.IO;

namespace BuildOrchestrator.Tests;

/// <summary>
/// [T49] Kaynak dosyaların KENDİSİNİ okuyan testler (ör. NoHardcodedColorTests) için repo kökü. Çıktı
/// dizininden (<c>AppContext.BaseDirectory</c>) yukarı doğru <c>BuildOrchestrator.slnx</c> aranır — bu yüzden
/// derin/yeniden adlandırılmış bin yollarından ve çalışma dizininden bağımsızdır.
/// </summary>
internal static class RepoPaths
{
    private const string SolutionFileName = "BuildOrchestrator.slnx";

    /// <summary>Solution dosyasını içeren dizin (mutlak yol).</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>App projesinin kaynak kökü (mutlak yol).</summary>
    public static string AppSrcRoot { get; } = Path.Combine(RepoRoot, "src", "BuildOrchestrator.App");

    /// <summary>[T49 fix round 1 · A3] TÜM üretim projelerinin kökü — D8 (sleep yasağı) yalnız App'i değil
    /// Core/Supervisor/Contracts'ı da bağlar, bu yüzden o guard buradan beslenir.</summary>
    public static string SrcRoot { get; } = Path.Combine(RepoRoot, "src");

    /// <summary>Tüm <c>src/</c> ağacındaki kaynak dosyalar — derleme çıktıları (<c>bin</c>/<c>obj</c>) HARİÇ.</summary>
    public static IEnumerable<string> SrcSourceFiles(string searchPattern) =>
        Directory.EnumerateFiles(SrcRoot, searchPattern, SearchOption.AllDirectories)
                 .Where(f => !IsBuildOutput(SrcRoot, f));

    /// <summary>
    /// [T33 fix round 2] TÜM repo ağacındaki kaynak dosyalar — derleme çıktıları (<c>bin</c>/<c>obj</c>) ve
    /// <c>.git</c> HARİÇ. Gerekçe: bazı ayarlar <c>src/</c>'nin DIŞINDA yaşar — kökteki
    /// <c>Directory.Build.props</c> gibi. Yalnız <see cref="SrcSourceFiles"/> ile taranan bir guard, o dosyaya
    /// eklenen bir MSBuild property'sini GÖRMEZ (tarama sessizce sıfır dosya döner).
    /// </summary>
    public static IEnumerable<string> RepoSourceFiles(string searchPattern) =>
        Directory.EnumerateFiles(RepoRoot, searchPattern, SearchOption.AllDirectories)
                 .Where(f => !IsBuildOutput(RepoRoot, f) && !IsHiddenTree(RepoRoot, f));

    /// <summary>[T49 fix round 2] Test ağacının kökü — D8 testleri de bağlar ("testte gerçek zaman beklenmez").</summary>
    public static string TestsRoot { get; } = Path.Combine(RepoRoot, "tests");

    /// <summary>Tüm <c>tests/</c> ağacındaki kaynak dosyalar — derleme çıktıları HARİÇ.</summary>
    public static IEnumerable<string> TestSourceFiles(string searchPattern) =>
        Directory.EnumerateFiles(TestsRoot, searchPattern, SearchOption.AllDirectories)
                 .Where(f => !IsBuildOutput(TestsRoot, f));

    /// <summary>
    /// [T64] App kaynak ağacındaki dosyalar — derleme çıktıları (<c>bin</c>/<c>obj</c>) HARİÇ. Kaynağın
    /// KENDİSİNİ tarayan guard testleri (ikon fontu / font pack URI'si / ham renk literali) hepsi buradan
    /// beslenir: aynı filtreyi her test sınıfında yeniden yazmak (kopya YASAK, CLAUDE.md) yerine tek yer.
    /// </summary>
    public static IEnumerable<string> AppSourceFiles(string searchPattern) =>
        Directory.EnumerateFiles(AppSrcRoot, searchPattern, SearchOption.AllDirectories)
                 .Where(f => !IsBuildOutput(AppSrcRoot, f));

    /// <summary>Nokta ile başlayan araç dizinleri (<c>.git</c>, <c>.vs</c>, <c>.claude</c>, <c>.superpowers</c>) —
    /// kaynak değildir, taramaya girmez.</summary>
    private static bool IsHiddenTree(string root, string path) =>
        Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.StartsWith('.'));

    private static bool IsBuildOutput(string root, string path)
    {
        string[] segments = Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin") || segments.Contains("obj");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException(
            $"Repo kökü bulunamadı: '{AppContext.BaseDirectory}' ve üst dizinlerinde {SolutionFileName} yok.");
    }
}
