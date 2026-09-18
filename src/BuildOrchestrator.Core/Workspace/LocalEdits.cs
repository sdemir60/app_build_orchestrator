using BuildOrchestrator.Core.Incremental;

namespace BuildOrchestrator.Core.Workspace;

/// <summary>
/// [Task 3] Sync önizlemesindeki <c>LocalEdits</c> işaretinin SAF hesap yeri: <c>git status --porcelain</c>'in
/// verdiği kök-göreli yollarla bir projenin girdi kümesini (<see cref="IncrementalRunBinder.InputsOf"/>)
/// kesiştirir. Disk'e/GİT'e DOKUNMAZ — girdiler ve dirty yollar çağıran tarafından toplanmıştır (D1: karar
/// tek kaynaktan; <c>SyncWorkspaceService</c> bu hesabı ÇAĞIRIR, kopyalamaz).
///
/// <para><b>Harici kökler bu fazda hep <c>false</c>:</b> <c>dirtyPaths</c> YALNIZ ana repo kökünün
/// (<paramref name="root"/>) <c>git status</c> çıktısından gelir. Harici bir karttan taranan projenin girdileri
/// kendi kökünün ALTINDA yaşar — ana repo köküyle birleştirilmiş bir yol o kökü asla üretmez, dolayısıyla hiç
/// eşleşmez. Ayrı bir git sorgusu bu faz için YOKTUR; bu bilinçli bir sınırdır (bkz. task brief).</para>
///
/// <para><b>Dizin öneki:</b> <c>git status --porcelain</c> (<c>-uall</c> OLMADAN) yeni bir untracked klasörü
/// TEK bir <c>dir/</c> satırıyla bildirir — altındaki dosyalar ayrı satır ALMAZ. <c>/</c> ile biten bir dirty
/// yol bu yüzden bir DİZİN ÖNEKİ sayılır: o dizinin altındaki her girdi dirty sayılır.</para>
/// </summary>
public static class LocalEdits
{
    /// <param name="dirtyPaths"><see cref="BuildOrchestrator.Core.Git.GitService.GetDirtyPathsAsync"/>'in
    /// döndürdüğü kök-göreli, <c>/</c>-ayraçlı yollar (rename → yeni yol). Sorgu başarısızsa çağıran BOŞ liste
    /// verir — bu durumda hiçbir proje işaretlenmez.</param>
    /// <param name="inputsById">Proje kimliği → <see cref="IncrementalRunBinder.InputsOf"/>'un döndürdüğü girdi
    /// kümesi (mantıksal yollar TAM yoldur).</param>
    /// <param name="root">Sync'in çalıştığı repo kökü — dirty yollar buna göre birleştirilip normalize edilir.</param>
    public static IReadOnlySet<string> ProjectsWithLocalEdits(
        IReadOnlyList<string> dirtyPaths,
        IReadOnlyDictionary<string, IReadOnlyList<ProjectInput>> inputsById,
        string root)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (dirtyPaths.Count == 0) return result;

        string fullRoot = Path.GetFullPath(root);
        var dirtyFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dirtyDirs = new List<string>();
        foreach (string dirty in dirtyPaths)
        {
            bool isDirectory = dirty.EndsWith('/');
            string relative = dirty.Replace('/', Path.DirectorySeparatorChar);
            string combined = Path.GetFullPath(Path.Combine(fullRoot, relative));
            if (isDirectory)
                dirtyDirs.Add(Path.TrimEndingDirectorySeparator(combined) + Path.DirectorySeparatorChar);
            else
                dirtyFiles.Add(combined);
        }

        foreach (var (projectId, inputs) in inputsById)
        {
            bool dirty = inputs.Any(i => dirtyFiles.Contains(i.LogicalPath)
                || dirtyDirs.Exists(d => i.LogicalPath.StartsWith(d, StringComparison.OrdinalIgnoreCase)));
            if (dirty) result.Add(projectId);
        }

        return result;
    }
}
