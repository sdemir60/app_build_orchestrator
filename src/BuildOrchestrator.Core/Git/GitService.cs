using System.ComponentModel;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.Git;

/// <summary>Tek bir branch girdisi — yerel (<c>refs/heads/*</c>) ya da remote-tracking (<c>refs/remotes/*</c>).</summary>
public sealed record GitBranchInfo(string Name, bool IsRemote, bool IsActive);

/// <summary>
/// Tek bir git sorgusunun tanımlı sonucu: exception YOK — başarı/hata her zaman veri olarak döner
/// (brief: "git missing / command error -> DEFINED error signal, NOT an unhandled exception").
/// </summary>
public sealed record GitResult<T>
{
    public bool Success { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }

    public static GitResult<T> Ok(T value) => new() { Success = true, Value = value };
    public static GitResult<T> Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// [T11] §4 kaynak-sinyali sağlayıcısı: "değişti mi" kararları İÇİN gereken tüm git olgularını
/// (HEAD commit, branch, dirty paths, branch listesi) patolojik repo durumlarına (no-commits, detached
/// HEAD, shallow clone) karşı toleranslı biçimde toplar. DLL/bin/obj timestamp'ına ASLA bakmaz — yalnız
/// git komut çıktısı okunur.
/// <para>
/// <see cref="TreatAsDirty"/> anlamı: bu bayrak true olan bir repo, IncrementalPlanner tarafından güvenli
/// tarafta kalınarak "değişmiş" varsayılmalıdır (edge durumunda commit/dirty karşılaştırması güvenilir
/// değildir → full rebuild'e düş).
/// </para>
/// </summary>
public sealed record GitRepoState
{
    public string? HeadCommit { get; init; }
    public string? Branch { get; init; }
    public bool IsDetached { get; init; }
    public bool HasNoCommits { get; init; }
    public bool IsShallow { get; init; }
    public bool TreatAsDirty { get; init; }
    public IReadOnlyList<string> DirtyPaths { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool HasError { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// [T11] <see cref="ProcessRunner"/> tabanlı git wrapper: HEAD commit, current branch, dirty paths, branch
/// listesi sorguları + edge tespiti (no-commits/detached HEAD/shallow clone). Fetch (Task 5) ve worktree
/// add (Task 9) burada YOK — yalnız read/query yüzeyi. Her metot process spawn edip git.exe'yi çağırır;
/// git bulunamazsa (<see cref="Win32Exception"/>) ya da beklenmeyen bir başlatma hatası olursa (<see
/// cref="InvalidOperationException"/>, bkz. <see cref="ProcessRunner.RunAsync"/>) exception YUKARI SIZMAZ
/// — <see cref="GitResult{T}.Fail"/> olarak döner.
/// </summary>
public sealed class GitService(ProcessRunner runner, string repoRoot, string gitExecutable = "git")
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    private readonly ProcessRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    private readonly string _repoRoot = repoRoot ?? throw new ArgumentNullException(nameof(repoRoot));
    private readonly string _gitExecutable = string.IsNullOrWhiteSpace(gitExecutable)
        ? throw new ArgumentException("gitExecutable boş olamaz.", nameof(gitExecutable))
        : gitExecutable;

    /// <summary>HEAD commit SHA'sı. Normal repo → 40-hex SHA. No-commits (unborn HEAD) → <c>Ok(null)</c> (hata DEĞİL, edge).</summary>
    public async Task<GitResult<string?>> GetHeadCommitAsync(CancellationToken ct = default)
    {
        var outcome = await TryRunGitAsync(["rev-parse", "--verify", "-q", "HEAD"], ct);
        if (!outcome.Ok) return GitResult<string?>.Fail(outcome.Error!);

        var r = outcome.Result!;
        if (r.ExitCode == 0)
        {
            string sha = r.StandardOutput.Trim();
            return IsFortyHexSha(sha)
                ? GitResult<string?>.Ok(sha)
                : GitResult<string?>.Fail($"beklenmeyen 'git rev-parse HEAD' çıktısı: '{sha}'");
        }

        // rev-parse --verify -q HEAD, unborn HEAD (henüz commit yok) durumunda ve YALNIZ bu durumda
        // sessizce exit=1 + BOŞ stderr döner. Bu tek sinyal "no-commits" anlamına gelir; başka HERHANGİ
        // bir exit kodu/stderr kombinasyonu (özellikle exit=128 — bozuk repo, izin hatası, vb.) gerçek
        // bir git hatasıdır ve Fail olarak yüzeye çıkarılmalı, "no-commits" ile karıştırılmamalıdır.
        if (IsUnbornHeadSignal(r)) return GitResult<string?>.Ok(null);

        return GitResult<string?>.Fail(DescribeGitFailure(r));
    }

    /// <summary>Checkout edilmiş branch adı. Normal → ad. Detached HEAD → <c>Ok(null)</c> (hata DEĞİL, edge).</summary>
    public async Task<GitResult<string?>> GetCurrentBranchAsync(CancellationToken ct = default)
    {
        var outcome = await TryRunGitAsync(["symbolic-ref", "--short", "-q", "HEAD"], ct);
        if (!outcome.Ok) return GitResult<string?>.Fail(outcome.Error!);

        var r = outcome.Result!;
        if (r.ExitCode == 0) return GitResult<string?>.Ok(r.StandardOutput.Trim());

        // symbolic-ref -q, HEAD detached olduğunda ve YALNIZ bu durumda sessizce exit=1 + BOŞ stderr
        // döner. Başka her kombinasyon (özellikle exit=128) gerçek bir git hatasıdır — Fail.
        if (IsUnbornHeadSignal(r)) return GitResult<string?>.Ok(null);

        return GitResult<string?>.Fail(DescribeGitFailure(r));
    }

    /// <summary>Working-tree + staged değişiklikler (`git status --porcelain`'den path listesi). Temiz repo → boş liste.</summary>
    public async Task<GitResult<IReadOnlyList<string>>> GetDirtyPathsAsync(CancellationToken ct = default)
    {
        var outcome = await TryRunGitAsync(["status", "--porcelain"], ct);
        if (!outcome.Ok) return GitResult<IReadOnlyList<string>>.Fail(outcome.Error!);

        var r = outcome.Result!;
        if (r.ExitCode != 0) return GitResult<IReadOnlyList<string>>.Fail(r.StandardError.Trim());

        return GitResult<IReadOnlyList<string>>.Ok(ParsePorcelainPaths(r.StandardOutput));
    }

    /// <summary>Repo shallow mu (`git rev-parse --is-shallow-repository`, git 2.15+).</summary>
    public async Task<GitResult<bool>> IsShallowRepositoryAsync(CancellationToken ct = default)
    {
        var outcome = await TryRunGitAsync(["rev-parse", "--is-shallow-repository"], ct);
        if (!outcome.Ok) return GitResult<bool>.Fail(outcome.Error!);

        var r = outcome.Result!;
        if (r.ExitCode != 0) return GitResult<bool>.Fail(r.StandardError.Trim());

        return GitResult<bool>.Ok(string.Equals(r.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Yerel + remote-tracking branch listesi (<c>refs/heads</c> + <c>refs/remotes</c>). <see cref="GitBranchInfo.IsActive"/> yalnız checkout edilmiş YEREL branch için true.</summary>
    public async Task<GitResult<IReadOnlyList<GitBranchInfo>>> ListBranchesAsync(CancellationToken ct = default)
    {
        var outcome = await TryRunGitAsync(["for-each-ref", "--format=%(refname)", "refs/heads", "refs/remotes"], ct);
        if (!outcome.Ok) return GitResult<IReadOnlyList<GitBranchInfo>>.Fail(outcome.Error!);

        var r = outcome.Result!;
        if (r.ExitCode != 0) return GitResult<IReadOnlyList<GitBranchInfo>>.Fail(r.StandardError.Trim());

        var currentBranch = await GetCurrentBranchAsync(ct);
        string? active = currentBranch.Success ? currentBranch.Value : null;

        var list = new List<GitBranchInfo>();
        using var reader = new StringReader(r.StandardOutput);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;

            if (line.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                string name = line["refs/heads/".Length..];
                list.Add(new GitBranchInfo(name, IsRemote: false, IsActive: active is not null && name == active));
            }
            else if (line.StartsWith("refs/remotes/", StringComparison.Ordinal))
            {
                string name = line["refs/remotes/".Length..];
                list.Add(new GitBranchInfo(name, IsRemote: true, IsActive: false));
            }
        }

        return GitResult<IReadOnlyList<GitBranchInfo>>.Ok(list);
    }

    /// <summary>
    /// Tüm sorguları toplar ve edge durumlarını (<see cref="GitRepoState.HasNoCommits"/>, <see
    /// cref="GitRepoState.IsDetached"/>, <see cref="GitRepoState.IsShallow"/>) <see
    /// cref="GitRepoState.TreatAsDirty"/> kararına indirger. Herhangi bir alt-sorgu hata dönerse
    /// (git yok/komut hatası) TreatAsDirty=true + HasError=true + uyarı eklenir — güvenli taraf.
    /// </summary>
    public async Task<GitRepoState> GetRepoStateAsync(CancellationToken ct = default)
    {
        var headResult = await GetHeadCommitAsync(ct);
        var branchResult = await GetCurrentBranchAsync(ct);
        var dirtyResult = await GetDirtyPathsAsync(ct);
        var shallowResult = await IsShallowRepositoryAsync(ct);

        if (!headResult.Success || !branchResult.Success || !dirtyResult.Success || !shallowResult.Success)
        {
            string err = headResult.Error ?? branchResult.Error ?? dirtyResult.Error ?? shallowResult.Error
                ?? "bilinmeyen git hatası";
            return new GitRepoState
            {
                HeadCommit = headResult.Success ? headResult.Value : null,
                Branch = branchResult.Success ? branchResult.Value : null,
                TreatAsDirty = true,
                HasError = true,
                Error = err,
                Warnings = [$"git sorgusu başarısız — treat-as-dirty: {err}"],
            };
        }

        bool hasNoCommits = headResult.Value is null;
        bool isDetached = branchResult.Value is null;
        bool isShallow = shallowResult.Value;
        var dirtyPaths = dirtyResult.Value!;

        var warnings = new List<string>();
        if (hasNoCommits) warnings.Add("repo henüz commit içermiyor (unborn HEAD) — treat-as-dirty.");
        if (isDetached) warnings.Add("HEAD detached — branch belirlenemedi.");
        if (isShallow) warnings.Add("repo shallow clone — treat-as-dirty.");

        return new GitRepoState
        {
            HeadCommit = headResult.Value,
            Branch = branchResult.Value,
            IsDetached = isDetached,
            HasNoCommits = hasNoCommits,
            IsShallow = isShallow,
            TreatAsDirty = hasNoCommits || isShallow || dirtyPaths.Count > 0,
            DirtyPaths = dirtyPaths,
            Warnings = warnings,
        };
    }

    private async Task<(bool Ok, ProcessResult? Result, string? Error)> TryRunGitAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        try
        {
            var result = await _runner.RunAsync(new ProcessSpec(_gitExecutable, args, _repoRoot, CommandTimeout), ct);
            return (true, result, null);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // git.exe PATH'te yok / başlatılamadı — tanımlı hata, exception yukarı sızmaz.
            return (false, null, $"git komutu çalıştırılamadı ('{_gitExecutable}'): {ex.Message}");
        }
    }

    /// <summary>
    /// "no-commits" / "detached HEAD" edge sinyalinin TEK doğru tanımı: exit=1 + BOŞ stderr. (Örn.
    /// "not a git repository" metnine bakmak yanlış ve dar bir yaklaşımdır — bkz. tip özeti.)
    /// </summary>
    private static bool IsUnbornHeadSignal(ProcessResult r) => r.ExitCode == 1 && string.IsNullOrEmpty(r.StandardError);

    private static string DescribeGitFailure(ProcessResult r)
        => string.IsNullOrEmpty(r.StandardError)
            ? $"git komutu beklenmeyen exit kodu ile sonlandı: {r.ExitCode}"
            : r.StandardError.Trim();

    private static bool IsFortyHexSha(string s) => s.Length == 40 && s.All(Uri.IsHexDigit);

    private static IReadOnlyList<string> ParsePorcelainPaths(string porcelainOutput)
    {
        var paths = new List<string>();
        using var reader = new StringReader(porcelainOutput);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length < 4) continue; // "XY " + path'ten kısa olamaz
            string rest = line[3..];
            int arrow = rest.IndexOf(" -> ", StringComparison.Ordinal); // rename: "old -> new"
            string path = arrow >= 0 ? rest[(arrow + 4)..] : rest;
            paths.Add(path.Trim('"'));
        }
        return paths;
    }
}
