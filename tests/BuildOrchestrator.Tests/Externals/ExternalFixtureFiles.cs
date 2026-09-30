using System.IO;
using System.Linq;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// Bu klasördeki testlerin PAYLAŞTIĞI dosya yazıcıları — kopya YASAK, tek kaynak (<see
/// cref="ExternalWorkspaceResolverTests"/> ve <see cref="ExternalSyncIntegrationTests"/> aynı solution
/// biçimini kullanır).
/// </summary>
internal static class ExternalFixtureFiles
{
    /// <summary><paramref name="directory"/> içine, verilen projeleri göreli yollarıyla listeleyen bir
    /// <c>.sln</c> yazar; dönen değer solution'ın yoludur.</summary>
    public static string WriteSolution(string directory, string name, params string[] csprojPaths)
    {
        string sln = Path.Combine(directory, name + ".sln");
        File.WriteAllText(sln, string.Concat(csprojPaths.Select(p =>
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"" + Path.GetFileNameWithoutExtension(p)
            + "\", \"" + Path.GetRelativePath(directory, p) + "\", \"{1}\"\nEndProject\n")));
        return sln;
    }
}
