using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.Core.Incremental;

/// <summary>
/// [Task 19 wiring] Supervisor kompozisyon kökü (Program.cs) ile <see cref="IncrementalPlanner"/> arasındaki
/// glue: her projenin build-etkileyen dosyalarını (csproj + <see cref="EvaluatedProject.CompileFiles"/>)
/// <b>repo-relative, `/`-normalize edilmiş</b> yollara çevirip (deferred Task 7b) <see
/// cref="IncrementalPlanner.ComputeCommittedFingerprint"/>'e verir, working-tree dirty yollarını proje
/// dizinine göre projeye atfeder ve <see cref="IncrementalPlanner.ComputeWillBuildWithSignatures"/>'ı sürer.
/// <para>
/// §4: DLL/bin/obj timestamp'ı ASLA okunmaz — committed terim git blob SHA'sından (harita), dirty terim
/// yalnız <paramref name="dirtyRepoRelativePaths"/> ile filtrelenen kaynak dosyaların İÇERİĞİNDEN gelir.
/// </para>
/// <para>
/// <b>repoRoot varsayımı:</b> <paramref name="repoRoot"/> = git repo toplevel (workspace root ile aynı kabul
/// edilir — OSYS için doğru). Workspace root git toplevel'ın bir alt dizini ise committed fingerprint boşa
/// düşer → o proje never-committed sayılır → WillBuild=true (güvenli tarafta over-build; It-4'te toplevel
/// çözümü ile giderilecek).
/// </para>
/// </summary>
public static class IncrementalRunBinder
{
    /// <summary>Bir mutlak yolu repo köküne göre relative + `/`-normalize eder (git ls-tree / porcelain formatı ile eşleşsin).</summary>
    public static string ToRepoRelativeNormalized(string repoRoot, string absolutePath) =>
        Path.GetRelativePath(repoRoot, absolutePath).Replace('\\', '/');

    /// <summary>Bir projenin build-etkileyen dosyalarının (csproj + compile dosyaları) repo-relative, `/`-normalize edilmiş yolları.</summary>
    public static IReadOnlyList<string> RepoRelativeBuildFiles(string repoRoot, string projectId, EvaluatedProject? evaluated)
    {
        var files = new List<string> { projectId };
        if (evaluated is not null) files.AddRange(evaluated.CompileFiles);
        return files.Select(f => ToRepoRelativeNormalized(repoRoot, Path.GetFullPath(f))).ToList();
    }

    /// <summary>
    /// Planı incremental willBuild + imza haritası ile bağlar. <paramref name="evaluatedById"/> projectId (tam
    /// csproj yolu) → <see cref="EvaluatedProject"/>; <paramref name="dirtyRepoRelativePaths"/> git'ten gelen
    /// (repo-relative, `/`) working-tree dirty yolları. Dönen imza haritası yalnız <b>non-null</b> imzaları içerir
    /// (hollow/never-committed projeler persist edilmez).
    /// <para>[Task 11] <paramref name="buildCycles"/> kill switch'i buradan geçirilir — bkz.
    /// <see cref="IncrementalPlanner.ComputeWillBuild"/>. Varsayılanı YOKTUR: bu metodun İKİ çağıranı vardır
    /// (Supervisor'ın run planlayıcısı ve Sync'in analizi) ve ikisi de önizleme yayınlar; biri değeri sessizce
    /// atlarsa o yüzeydeki will-dot'lar motorla ayrışır.</para>
    /// </summary>
    public static (BuildPlan Plan, IReadOnlyDictionary<string, string> SignatureById) Bind(
        BuildPlan plan,
        IReadOnlyDictionary<string, EvaluatedProject> evaluatedById,
        string repoRoot,
        string? headCommit,
        IReadOnlyDictionary<string, string> trackedBlobHashes,
        IReadOnlyList<string> dirtyRepoRelativePaths,
        IReadOnlyDictionary<string, BuildState> state,
        bool inPlace,
        bool buildCycles,
        DependentMode mode)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(evaluatedById);
        ArgumentNullException.ThrowIfNull(repoRoot);
        ArgumentNullException.ThrowIfNull(trackedBlobHashes);
        ArgumentNullException.ThrowIfNull(dirtyRepoRelativePaths);
        ArgumentNullException.ThrowIfNull(state);

        // Dirty yolları BİR KEZ mutlak yola çevir (proje dizinine göre atfetmek için) — git porcelain repo-relative verir.
        var dirtyAbs = dirtyRepoRelativePaths
            .Select(p => Path.GetFullPath(Path.Combine(repoRoot, p.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();

        IReadOnlyList<string> DirtyFilesForNode(ProjectNode node)
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(node.Id));
            if (dir is null) return [];
            string prefix = dir.EndsWith(Path.DirectorySeparatorChar) ? dir : dir + Path.DirectorySeparatorChar;
            return dirtyAbs.Where(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        string? CommittedFingerprintForNode(ProjectNode node)
        {
            var repoRel = RepoRelativeBuildFiles(repoRoot, node.Id,
                evaluatedById.TryGetValue(node.Id, out var ev) ? ev : null);
            return IncrementalPlanner.ComputeCommittedFingerprint(trackedBlobHashes, repoRel);
        }

        var (boundPlan, signatures) = IncrementalPlanner.ComputeWillBuildWithSignatures(
            plan, headCommit, DirtyFilesForNode, File.ReadAllText, CommittedFingerprintForNode, state, inPlace,
            buildCycles, mode);

        var nonNull = signatures
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.OrdinalIgnoreCase);

        return (boundPlan, nonNull);
    }
}
