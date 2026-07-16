namespace BuildOrchestrator.Core.Discovery;

/// <summary>
/// Workspace taramasının sonucu: bulunan .csproj ve .sln dosyalarının kanonik,
/// deterministik sırada (OrdinalIgnoreCase) yollarını taşır.
/// </summary>
public sealed record ScanResult(IReadOnlyList<string> CsprojPaths, IReadOnlyList<string> SlnPaths);

/// <summary>
/// Bir repo kökünü recursive olarak tarayıp .csproj ve .sln dosyalarını bulur.
/// bin/obj/.git/.vs/node_modules gibi klasörler atlanır.
/// </summary>
public sealed class WorkspaceScanner
{
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
        { ".git", "bin", "obj", "node_modules", ".vs" }; // [Global Constraints scan ignore]

    public ScanResult Scan(string root)
    {
        var csproj = new List<string>();
        var sln = new List<string>();
        Walk(root, csproj, sln);
        csproj.Sort(StringComparer.OrdinalIgnoreCase); // determinizm [D8]
        sln.Sort(StringComparer.OrdinalIgnoreCase);
        return new ScanResult(csproj, sln);
    }

    private static void Walk(string dir, List<string> csproj, List<string> sln)
    {
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            if (file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) csproj.Add(Path.GetFullPath(file));
            else if (file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)) sln.Add(Path.GetFullPath(file));
        }
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            if (Ignored.Contains(Path.GetFileName(sub))) continue;
            Walk(sub, csproj, sln);
        }
    }
}
