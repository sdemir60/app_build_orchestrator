using System.Text.RegularExpressions;

namespace BuildOrchestrator.Core.Discovery;

/// <summary>
/// Her csproj'u onu içeren solution(lar)a eşler. [T32]
/// 0 solution → boş liste, >1 solution → çok değerli (sıralı) liste.
/// </summary>
public static partial class SolutionMapper
{
    // .sln satır formatı: Project("{guid}") = "Name", "relative\path.csproj", "{guid}"
    // 2. tırnaklı alanı (csproj göreli yolu) yakalar.
    [GeneratedRegex(@"Project\(""\{[^}]*\}""\)\s*=\s*""[^""]*"",\s*""([^""]+\.csproj)""", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectLine();

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Map(
        IReadOnlyList<string> slnPaths, IReadOnlyList<string> csprojPaths)
    {
        var csprojSet = new HashSet<string>(csprojPaths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        var acc = csprojSet.ToDictionary(c => c, _ => new SortedSet<string>(StringComparer.OrdinalIgnoreCase),
                                         StringComparer.OrdinalIgnoreCase);
        // determinizm [D8]: sln'leri OrdinalIgnoreCase sırayla gez.
        foreach (var sln in slnPaths.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            string slnDir = Path.GetDirectoryName(Path.GetFullPath(sln))!;
            string name = Path.GetFileNameWithoutExtension(sln);
            foreach (Match m in ProjectLine().Matches(File.ReadAllText(sln)))
            {
                string rel = m.Groups[1].Value;
                string full = Path.GetFullPath(Path.Combine(slnDir, rel));
                if (acc.TryGetValue(full, out var set)) set.Add(name);
            }
        }
        return acc.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.ToList(), StringComparer.OrdinalIgnoreCase);
    }
}
