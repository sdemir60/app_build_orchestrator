namespace BuildOrchestrator.Core.Graph;

using BuildOrchestrator.Core.Discovery;

/// <summary>
/// DLL adı (lower, ör. "osys.b.dll") → onu üreten proje Id'si eşlemesi.
/// AmbiguousDlls: birden fazla proje aynı AssemblyName'e sahipse buraya kaydedilir
/// ve DllToProducer'dan çıkarılır (determinizm [D8/D11] — belirsiz DLL kenar üretmez).
/// </summary>
public sealed record ProducerMap(
    IReadOnlyDictionary<string, string> DllToProducer,
    IReadOnlyList<string> AmbiguousDlls);

/// <summary>
/// Projelerin AssemblyName'inden producer map çıkarır. MSBuild çalıştırılmaz;
/// yalnız <see cref="EvaluatedProject.AssemblyName"/> kullanılır [Global Constraints raw-XML].
/// </summary>
public static class ProducerMapBuilder
{
    public static ProducerMap Build(IReadOnlyList<EvaluatedProject> projects)
    {
        // Determinizm [D8]: proje sırası OrdinalIgnoreCase Path'e göre.
        var multi = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in projects.OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase))
        {
            string dll = p.AssemblyName.ToLowerInvariant() + ".dll";
            (multi.TryGetValue(dll, out var l) ? l : multi[dll] = new()).Add(p.Path);
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new List<string>();
        foreach (var (dll, producers) in multi.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (producers.Count == 1) map[dll] = producers[0];
            else ambiguous.Add(dll); // determinizm: belirsizi kenar yapma [D8/D11]
        }

        return new ProducerMap(map, ambiguous);
    }
}
