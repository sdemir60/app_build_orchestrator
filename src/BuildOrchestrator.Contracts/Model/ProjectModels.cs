using System.Linq;

namespace BuildOrchestrator.Contracts.Model;

// It-1 domain DTO'ları — A9 şeklini sabitler. Core → Contracts referansı üzerinden Core bu tipleri üretir.
// It-3: depIssues (ProjectSucceededEvent/ProjectFailedEvent) ve RunRequest.mode genişlemesi (Build/RetryFailed,
// DependentMode) artık IpcMessages.cs'de sabit; BranchRef/Worktree git-yüzeyi DTO'ları burada.

public enum HintPathClass { Edge, ExternalThirdParty, ExternalOsysPlatform, Unclassified }
public enum BuildResult { Succeeded, Failed, Skipped }

public sealed record SolutionRef(string Name, string Path);

// Ham HintPath + basename + sınıf + (varsa) üretici projectId
public sealed record HintPathRef(string Raw, string BaseName, HintPathClass Class, string? ProducerProjectId);

public sealed record ProjectNode(
    string Id,                              // kanonik tam csproj yolu
    string Name,                            // AssemblyName türevi kısa ad
    string ProjectPath,                     // == Id (A9 şekli)
    IReadOnlyList<string> SolutionNames,    // T32
    IReadOnlyList<string> Dependencies,     // üretici projectId'ler (graf kenarları, deduped, sıralı)
    int BuildOrder,
    int? LayerIndex,
    string? LayerName,
    bool InCycle,
    bool? WillBuild)                        // T53: dirty=true, güncel=false, imza-yok/pre-Sync=null
{
    // Derleyicinin ürettiği record eşitliği, IReadOnlyList<string> alanlarında EqualityComparer<T>.Default
    // kullanır; List<string> Equals'ı override etmediği için bu referans eşitliğine düşer (JSON round-trip
    // sonrası her zaman farklı liste örneği). SolutionNames/Dependencies için sıralı içerik eşitliği gerekli.
    public bool Equals(ProjectNode? other) =>
        other is not null
        && Id == other.Id
        && Name == other.Name
        && ProjectPath == other.ProjectPath
        && SolutionNames.SequenceEqual(other.SolutionNames)
        && Dependencies.SequenceEqual(other.Dependencies)
        && BuildOrder == other.BuildOrder
        && LayerIndex == other.LayerIndex
        && LayerName == other.LayerName
        && InCycle == other.InCycle
        && WillBuild == other.WillBuild;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Name);
        hash.Add(ProjectPath);
        foreach (string s in SolutionNames) hash.Add(s);
        foreach (string d in Dependencies) hash.Add(d);
        hash.Add(BuildOrder);
        hash.Add(LayerIndex);
        hash.Add(LayerName);
        hash.Add(InCycle);
        hash.Add(WillBuild);
        return hash.ToHashCode();
    }
}

public sealed record BuildPlan(
    IReadOnlyList<ProjectNode> Nodes,                       // build-order'da
    IReadOnlyList<IReadOnlyList<string>> Cycles,            // her biri bir SCC (>1 üye), üyeler sıralı
    string Configuration);

public sealed record BuildState(
    string ProjectId,
    string? BuiltSignature,
    string? BuiltCommit = null,
    BuildResult? LastResult = null,
    DateTimeOffset? LastRunAt = null,
    string? LastBranch = null,
    long? LastDurationMs = null);           // T70 (It-3) burada alan olarak hazır

/// <summary>Bir git branch/ref bilgisi (GitService.ListBranches / BranchListEvent). [It-3]</summary>
public sealed record BranchRef(string Name, string Sha, bool IsActive, bool IsRemoteTracking);

/// <summary>Bir git worktree bilgisi (GitService.ListWorktrees). IPC yüzeyi minimal — bu DTO It-3'te yalnız
/// GitService tarafında kullanılır; tam listWorktrees/deleteWorktree komutları It-4 UI'a ertelendi. [It-3]</summary>
public sealed record Worktree(string Name, string Branch, string Path, bool IsActive, long? DiskSizeBytes);
