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
    /// <summary>[Global Constraints scan ignore] MSBuild derleme çıktısı klasörleri.</summary>
    public static readonly IReadOnlyList<string> BuildOutputFolderNames = ["bin", "obj"];

    /// <summary>
    /// IDE/VCS/paket yöneticisi klasörleri — kaynak değil, hiçbir tarama İÇİNE girmez. Tek tanım burada
    /// (kopya YASAK, CLAUDE.md): <see cref="BuildOrchestrator.Core.Incremental.ProjectInputs"/>'in proje-içi
    /// klasör taraması da bu adları <see cref="IsSkippedFolder"/> üzerinden buradan okur — eskiden ayrı bir
    /// atlama listesi tutuyordu ve yalnız <see cref="BuildOutputFolderNames"/>'i biliyordu; VS'nin sürekli
    /// yazdığı <c>.vs</c> bu yüzden zaman kararına (<c>OutputEvidence</c>) sızıyordu (Task T1).
    /// </summary>
    public static readonly IReadOnlyList<string> ExternalToolingFolderNames = [".git", ".vs", "node_modules"];

    private static readonly HashSet<string> Ignored =
        new(BuildOutputFolderNames.Concat(ExternalToolingFolderNames), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Bu yol, canlı bir build'in saniyeler içinde sileceği geçici bir artefakt mı — kalıcı bir kaynak dosya
    /// DEĞİL. Bugün tek örneği WPF'in <c>MarkupCompilePass</c>'inin proje klasöründe (obj DEĞİL) ürettiği
    /// <c>&lt;Ad&gt;_&lt;8hex&gt;_wpftmp.csproj</c>'tur.
    ///
    /// <para><b>Neden ortak (public) bir karar:</b> dosyayı LİSTELEYEN ile OKUYAN arasında bir yarış vardır —
    /// enumerate ile okuma arasında dosya silinir ve okuma patlar. Bu tuzağa yalnız üretim taraması değil,
    /// repo ağacını okuyan kaynak guard'ları da düşer; kararın iki yerde yazılması ikisinin sessizce
    /// ayrışması demekti (kopya YASAK, CLAUDE.md).</para>
    /// </summary>
    public static bool IsTransientBuildArtifact(string path) =>
        path.EndsWith("_wpftmp.csproj", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bu klasör adı (yalnız ad, tam yol değil) — derleme çıktısı ya da IDE/VCS/paket yöneticisi
    /// klasörü — hiçbir taramanın İÇİNE girmediği adlardan biri mi.</summary>
    public static bool IsSkippedFolder(string folderName) => Ignored.Contains(folderName);

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
            if (file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                if (IsTransientBuildArtifact(file)) continue; // canlı build artefaktı — bkz. metodun doc'u
                csproj.Add(Path.GetFullPath(file));
            }
            else if (file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)) sln.Add(Path.GetFullPath(file));
        }
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            if (IsSkippedFolder(Path.GetFileName(sub))) continue;
            Walk(sub, csproj, sln);
        }
    }
}
