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

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException(
            $"Repo kökü bulunamadı: '{AppContext.BaseDirectory}' ve üst dizinlerinde {SolutionFileName} yok.");
    }
}
