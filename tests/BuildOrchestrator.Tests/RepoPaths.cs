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

    /// <summary>
    /// [T64] App kaynak ağacındaki dosyalar — derleme çıktıları (<c>bin</c>/<c>obj</c>) HARİÇ. Kaynağın
    /// KENDİSİNİ tarayan guard testleri (ikon fontu / font pack URI'si / ham renk literali) hepsi buradan
    /// beslenir: aynı filtreyi her test sınıfında yeniden yazmak (kopya YASAK, CLAUDE.md) yerine tek yer.
    /// </summary>
    public static IEnumerable<string> AppSourceFiles(string searchPattern) =>
        Directory.EnumerateFiles(AppSrcRoot, searchPattern, SearchOption.AllDirectories)
                 .Where(f => !IsBuildOutput(f));

    private static bool IsBuildOutput(string path)
    {
        string[] segments = Path.GetRelativePath(AppSrcRoot, path)
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
