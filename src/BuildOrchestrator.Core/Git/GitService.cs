using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.Git;

/// <summary>
/// Command-line git operations (Section 2 / 6): branches, status, current commit, worktree.
/// </summary>
public sealed class GitService
{
    private const string Git = "git";

    /// <summary>Find the repository root (the folder containing .git) at or above <paramref name="startPath"/>.</summary>
    public async Task<string?> FindRepoRootAsync(string startPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(startPath) || !Directory.Exists(startPath))
            return null;

        var result = await ProcessRunner.RunAsync(
            Git, "rev-parse --show-toplevel", startPath, ct).ConfigureAwait(false);
        if (!result.Success)
            return null;
        var path = result.StdOut.Trim();
        return string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path);
    }

    public async Task<string?> GetCurrentBranchAsync(string repoRoot, CancellationToken ct = default)
    {
        var result = await ProcessRunner.RunAsync(
            Git, "rev-parse --abbrev-ref HEAD", repoRoot, ct).ConfigureAwait(false);
        return result.Success ? result.StdOut.Trim() : null;
    }

    public async Task<List<BranchInfo>> ListBranchesAsync(string repoRoot, CancellationToken ct = default)
    {
        var branches = new List<BranchInfo>();
        var current = await GetCurrentBranchAsync(repoRoot, ct).ConfigureAwait(false);

        var result = await ProcessRunner.RunAsync(
            Git, "branch --format=%(refname:short)", repoRoot, ct).ConfigureAwait(false);
        if (!result.Success)
            return branches;

        foreach (var line in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = line.Trim();
            if (name.Length == 0 || name.StartsWith("(HEAD"))
                continue;
            branches.Add(new BranchInfo
            {
                Name = name,
                IsCurrent = string.Equals(name, current, StringComparison.Ordinal)
            });
        }
        return branches;
    }

    /// <summary>HEAD commit sha of a branch (or current HEAD when branch is null/empty).</summary>
    public async Task<string?> GetCommitAsync(string repoRoot, string? branch = null, CancellationToken ct = default)
    {
        var rev = string.IsNullOrWhiteSpace(branch) ? "HEAD" : branch!;
        var result = await ProcessRunner.RunAsync(
            Git, $"rev-parse {rev}", repoRoot, ct).ConfigureAwait(false);
        return result.Success ? result.StdOut.Trim() : null;
    }

    /// <summary>
    /// Absolute paths of files changed in the working tree (staged + unstaged + untracked),
    /// used for dirty-project detection (Section 6).
    /// </summary>
    public async Task<List<string>> GetChangedFilesAsync(string repoRoot, CancellationToken ct = default)
    {
        var changed = new List<string>();
        var result = await ProcessRunner.RunAsync(
            Git, "status --porcelain=v1 --untracked-files=all", repoRoot, ct).ConfigureAwait(false);
        if (!result.Success)
            return changed;

        foreach (var line in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4)
                continue;
            // Format: "XY <path>" or "XY <old> -> <new>" for renames.
            var pathPart = line[3..].Trim();
            var arrow = pathPart.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
                pathPart = pathPart[(arrow + 4)..];
            pathPart = pathPart.Trim('"');
            changed.Add(Path.GetFullPath(Path.Combine(repoRoot, pathPart.Replace('/', Path.DirectorySeparatorChar))));
        }
        return changed;
    }

    /// <summary>
    /// Ensure a worktree for <paramref name="branch"/> exists at <paramref name="worktreePath"/>
    /// and is updated to the branch tip (Section 6 / branch yönetimi). The user's main working
    /// tree is never touched.
    /// </summary>
    public async Task EnsureWorktreeAsync(
        string repoRoot, string branch, string worktreePath, CancellationToken ct = default)
    {
        if (Directory.Exists(Path.Combine(worktreePath, ".git")) || File.Exists(Path.Combine(worktreePath, ".git")))
        {
            // Existing worktree: fetch + checkout + hard reset to the branch tip.
            await ProcessRunner.RunAsync(Git, $"checkout {branch}", worktreePath, ct).ConfigureAwait(false);
            await ProcessRunner.RunAsync(Git, $"reset --hard {branch}", worktreePath, ct).ConfigureAwait(false);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(worktreePath)!);
        // Create the worktree pinned to the branch. --force tolerates a pre-existing empty dir.
        await ProcessRunner.RunAsync(
            Git, $"worktree add --force \"{worktreePath}\" {branch}", repoRoot, ct).ConfigureAwait(false);
    }
}
