using System.Text.RegularExpressions;
using BuildOrchestrator.Contracts.Model;

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

    /// <summary>csprojId → onu içeren solution'lar (ad + tam yol), Name'e göre OrdinalIgnoreCase sıralı. [T32]</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<SolutionRef>> MapRefs(
        IReadOnlyList<string> slnPaths, IReadOnlyList<string> csprojPaths)
    {
        var csprojSet = new HashSet<string>(csprojPaths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        var acc = csprojSet.ToDictionary(c => c, _ => new List<SolutionRef>(), StringComparer.OrdinalIgnoreCase);
        // determinizm [D8]: sln'leri OrdinalIgnoreCase sırayla gez.
        foreach (var sln in slnPaths.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            string full = Path.GetFullPath(sln);
            string slnDir = Path.GetDirectoryName(full)!;
            var slnRef = new SolutionRef(Path.GetFileNameWithoutExtension(full), full);
            foreach (Match m in ProjectLine().Matches(File.ReadAllText(sln)))
            {
                string proj = Path.GetFullPath(Path.Combine(slnDir, m.Groups[1].Value));
                if (acc.TryGetValue(proj, out var list) && !list.Any(r => r.Path.Equals(slnRef.Path, StringComparison.OrdinalIgnoreCase)))
                    list.Add(slnRef);
            }
        }
        return acc.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<SolutionRef>)kv.Value.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>0 solution → boş liste, >1 solution → çok değerli (sıralı) liste. [T32]</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Map(
        IReadOnlyList<string> slnPaths, IReadOnlyList<string> csprojPaths) =>
        MapRefs(slnPaths, csprojPaths).ToDictionary(
            kv => kv.Key,
            // MapRefs yol bazlı ayrıştırır; Map ad bazlı olmalı — aynı base filename'e sahip
            // farklı yollardaki .sln'ler (ör. iki branch'teki aynı "Osys.sln") burada tek ada
            // düşer (eski SortedSet<string> davranışıyla aynı, satır sırası zaten Name'e göredir).
            kv => (IReadOnlyList<string>)kv.Value.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StringComparer.OrdinalIgnoreCase);
}
