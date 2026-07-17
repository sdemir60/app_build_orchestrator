using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Core.MsBuild;

/// <summary>
/// packages.config restore'un İSTEDİĞİ `-p:SolutionDir` değerini üretir [SPIKE S2-a]: sln bağlamı
/// olmadan `-t:restore` "Çözüm bulunamadı" verir. Determinizm [D8]: >1 sln'de Name'e göre ordinal
/// ilk sln kazanır (T32'nin çok-değerli listesi korunur; kullanıcıya seçtirme yalnız "Open in VS", It-4).
/// </summary>
public static class SolutionDirResolver
{
    public static string Resolve(string projectId, IReadOnlyList<SolutionRef> refs)
    {
        var chosen = refs.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        return chosen is null
            ? Path.GetDirectoryName(Path.GetFullPath(projectId))!
            : Path.GetDirectoryName(Path.GetFullPath(chosen.Path))!;
    }
}
