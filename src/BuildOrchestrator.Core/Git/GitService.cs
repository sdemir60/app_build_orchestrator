using BuildOrchestrator.Core.Incremental;

namespace BuildOrchestrator.Core.Git;

/// <summary>
/// Command-line git wrapper (Section 2/6): branch listing, current branch/commit, working-tree
/// status, diff against a commit, and worktree preparation in the orchestrator's pool.
/// </summary>
public sealed class GitService
{
    private readonly string _repoRoot;

    public GitService(string repoRoot)
    {
        _repoRoot = repoRoot;
    }

    public string RepoRoot => _repoRoot;

    public async Task<bool> IsRepositoryAsync(CancellationToken ct = default)
    {
        var r = await Run(new[] { "rev-parse", "--is-inside-work-tree" }, ct).ConfigureAwait(false);
        return r.Success && r.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> GetCurrentBranchAsync(CancellationToken ct = default)
    {
        var r = await Run(new[] { "rev-parse", "--abbrev-ref", "HEAD" }, ct).ConfigureAwait(false);
        return r.StdOut.Trim();
    }

    public async Task<string> GetCurrentCommitAsync(string? rev = null, CancellationToken ct = default)
    {
        var r = await Run(new[] { "rev-parse", rev ?? "HEAD" }, ct).ConfigureAwait(false);
        return r.StdOut.Trim();
    }

    public async Task<IReadOnlyList<string>> ListBranchesAsync(CancellationToken ct = default)
    {
        var r = await Run(new[] { "for-each-ref", "--format=%(refname:short)", "refs/heads" }, ct)
            .ConfigureAwait(false);
        if (!r.Success)
        {
            return Array.Empty<string>();
        }

        return r.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    /// <summary>
    /// Working-tree changes from <c>git status --porcelain</c> as build-affecting <see cref="FileChange"/>s.
    /// Paths are repo-relative; the mapper resolves them against the repo root.
    /// </summary>
    public async Task<IReadOnlyList<FileChange>> GetStatusChangesAsync(string? workingDir = null, CancellationToken ct = default)
    {
        var r = await Run(new[] { "status", "--porcelain", "-z", "--untracked-files=all" }, ct, workingDir)
            .ConfigureAwait(false);
        if (!r.Success)
        {
            return Array.Empty<FileChange>();
        }

        return ParsePorcelainZ(r.StdOut).Select(p => new FileChange(p)).ToList();
    }

    /// <summary>Files changed between <paramref name="fromCommit"/> and HEAD (committed delta).</summary>
    public async Task<IReadOnlyList<FileChange>> GetDiffChangesAsync(string fromCommit, string toCommit = "HEAD", string? workingDir = null, CancellationToken ct = default)
    {
        var r = await Run(new[] { "diff", "--name-only", "-z", fromCommit, toCommit }, ct, workingDir)
            .ConfigureAwait(false);
        if (!r.Success)
        {
            return Array.Empty<FileChange>();
        }

        return r.StdOut
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => new FileChange(p))
            .ToList();
    }

    /// <summary>
    /// Ensures a worktree for <paramref name="branch"/> exists at <paramref name="worktreePath"/>
    /// and is updated to that branch's tip — without touching the user's VS working tree (Section 6).
    /// </summary>
    public async Task PrepareWorktreeAsync(string branch, string worktreePath, CancellationToken ct = default)
    {
        if (Directory.Exists(Path.Combine(worktreePath, ".git")) ||
            File.Exists(Path.Combine(worktreePath, ".git")))
        {
            // Existing worktree: make sure it points at the branch and is current.
            await Run(new[] { "-C", worktreePath, "checkout", branch }, ct).ConfigureAwait(false);
            await Run(new[] { "-C", worktreePath, "reset", "--hard", branch }, ct).ConfigureAwait(false);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(worktreePath.TrimEnd(Path.DirectorySeparatorChar))!);

        // Remove any stale registration then add fresh.
        await Run(new[] { "worktree", "prune" }, ct).ConfigureAwait(false);
        var add = await Run(new[] { "worktree", "add", "--force", worktreePath, branch }, ct).ConfigureAwait(false);
        if (!add.Success)
        {
            throw new InvalidOperationException($"git worktree add failed: {add.StdErr}");
        }
    }

    private static IEnumerable<string> ParsePorcelainZ(string output)
    {
        // Records are NUL-separated; each entry is "XY <path>" and renames add an extra path record.
        var parts = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.Length < 4)
            {
                continue;
            }

            var status = part.Substring(0, 2);
            var path = part.Substring(3);

            // For renames "R  old -> new" the porcelain -z form splits paths into separate records,
            // but defensively handle an inline arrow too.
            var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
            {
                path = path[(arrow + 4)..];
            }

            _ = status;
            yield return path;
        }
    }

    private Task<ProcessResult> Run(IEnumerable<string> args, CancellationToken ct, string? workingDir = null)
        => ProcessRunner.RunAsync("git", args, workingDir ?? _repoRoot, ct);
}
