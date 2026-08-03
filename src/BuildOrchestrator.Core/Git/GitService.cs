using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.Git;

/// <summary>Tek bir branch girdisi — yerel (<c>refs/heads/*</c>) ya da remote-tracking (<c>refs/remotes/*</c>).</summary>
/// <param name="Sha">[A5/T69] Branch'in işaret ettiği commit — branch listesiyle AYNI <c>for-each-ref</c>
/// çağrısından gelir (branch başına ayrı <c>rev-parse</c> process'i spawn EDİLMEZ).</param>
public sealed record GitBranchInfo(string Name, string Sha, bool IsRemote, bool IsActive);

/// <summary>
/// [T69/K1] <see cref="GitService.FetchRefOnlyAsync"/> sonucu. Normal durumda <see cref="TargetSha"/>
/// fetch sonrası remote-tracking ref'ten gelir (<see cref="Degraded"/>=false). Fetch başarısız olursa
/// (offline/unreachable/invalid remote) hata YUTULUR — throw yok — <see cref="Degraded"/>=true olur,
/// sebep <see cref="Warning"/>'de veri olarak taşınır ve <see cref="TargetSha"/> yerel HEAD'e düşer (K1).
/// </summary>
public sealed record FetchResult
{
    public bool Degraded { get; init; }
    public string? TargetSha { get; init; }
    public string? Warning { get; init; }
}

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
/// [T11] <see cref="IProcessRunner"/> tabanlı git wrapper: HEAD commit, current branch, dirty paths, branch
/// listesi sorguları + edge tespiti (no-commits/detached HEAD/shallow clone) + [T69/K1] Sync'in ilk adımı
/// olan ref-only fetch (<see cref="FetchRefOnlyAsync"/>). Worktree add (Task 9) burada YOK. Her metot
/// process spawn edip git.exe'yi çağırır; git bulunamazsa (<see cref="Win32Exception"/>) ya da beklenmeyen
/// bir başlatma hatası olursa (<see cref="InvalidOperationException"/>, bkz. <see
/// cref="IProcessRunner.RunAsync"/>) exception YUKARI SIZMAZ — <see cref="GitResult{T}.Fail"/> olarak döner.
/// </summary>
public sealed class GitService(IProcessRunner runner, string repoRoot, string gitExecutable = "git")
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    private readonly IProcessRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    private readonly string _repoRoot = repoRoot ?? throw new ArgumentNullException(nameof(repoRoot));
    private readonly string _gitExecutable = string.IsNullOrWhiteSpace(gitExecutable)
        ? throw new ArgumentException(GitMessages.MustNotBeEmpty(nameof(gitExecutable)), nameof(gitExecutable))
        : gitExecutable;

    /// <summary>HEAD commit SHA'sı. Normal repo → 40-hex SHA. No-commits (unborn HEAD) → <c>Ok(null)</c> (hata DEĞİL, edge).</summary>
    public async Task<GitResult<string?>> GetHeadCommitAsync(CancellationToken ct = default)
    {
        var outcome = await GitCommandExecutor.RunAsync(_runner, _gitExecutable, ["rev-parse", "--verify", "-q", "HEAD"], _repoRoot, CommandTimeout, ct);
        if (!outcome.Success) return GitResult<string?>.Fail(outcome.Error!);

        var r = outcome.Value!;
        if (r.ExitCode == 0)
        {
            string sha = r.StandardOutput.Trim();
            return IsFortyHexSha(sha)
                ? GitResult<string?>.Ok(sha)
                : GitResult<string?>.Fail($"unexpected 'git rev-parse HEAD' output: '{sha}'");
        }

        // rev-parse --verify -q HEAD, unborn HEAD (henüz commit yok) durumunda ve YALNIZ bu durumda
        // sessizce exit=1 + BOŞ stderr döner. Bu tek sinyal "no-commits" anlamına gelir; başka HERHANGİ
        // bir exit kodu/stderr kombinasyonu (özellikle exit=128 — bozuk repo, izin hatası, vb.) gerçek
        // bir git hatasıdır ve Fail olarak yüzeye çıkarılmalı, "no-commits" ile karıştırılmamalıdır.
        if (IsUnbornHeadSignal(r)) return GitResult<string?>.Ok(null);

        return GitResult<string?>.Fail(GitCommandExecutor.DescribeGitFailure(r));
    }

    /// <summary>Checkout edilmiş branch adı. Normal → ad. Detached HEAD → <c>Ok(null)</c> (hata DEĞİL, edge).</summary>
    public async Task<GitResult<string?>> GetCurrentBranchAsync(CancellationToken ct = default)
    {
        var outcome = await GitCommandExecutor.RunAsync(_runner, _gitExecutable, ["symbolic-ref", "--short", "-q", "HEAD"], _repoRoot, CommandTimeout, ct);
        if (!outcome.Success) return GitResult<string?>.Fail(outcome.Error!);

        var r = outcome.Value!;
        if (r.ExitCode == 0) return GitResult<string?>.Ok(r.StandardOutput.Trim());

        // symbolic-ref -q, HEAD detached olduğunda ve YALNIZ bu durumda sessizce exit=1 + BOŞ stderr
        // döner. Başka her kombinasyon (özellikle exit=128) gerçek bir git hatasıdır — Fail.
        if (IsUnbornHeadSignal(r)) return GitResult<string?>.Ok(null);

        return GitResult<string?>.Fail(GitCommandExecutor.DescribeGitFailure(r));
    }

    /// <summary>Working-tree + staged değişiklikler (`git status --porcelain`'den path listesi). Temiz repo → boş liste.</summary>
    public async Task<GitResult<IReadOnlyList<string>>> GetDirtyPathsAsync(CancellationToken ct = default)
    {
        var outcome = await GitCommandExecutor.RunAsync(_runner, _gitExecutable, ["status", "--porcelain"], _repoRoot, CommandTimeout, ct);
        if (!outcome.Success) return GitResult<IReadOnlyList<string>>.Fail(outcome.Error!);

        var r = outcome.Value!;
        if (r.ExitCode != 0) return GitResult<IReadOnlyList<string>>.Fail(r.StandardError.Trim());

        return GitResult<IReadOnlyList<string>>.Ok(ParsePorcelainPaths(r.StandardOutput));
    }

    /// <summary>Repo shallow mu (`git rev-parse --is-shallow-repository`, git 2.15+).</summary>
    public async Task<GitResult<bool>> IsShallowRepositoryAsync(CancellationToken ct = default)
    {
        var outcome = await GitCommandExecutor.RunAsync(_runner, _gitExecutable, ["rev-parse", "--is-shallow-repository"], _repoRoot, CommandTimeout, ct);
        if (!outcome.Success) return GitResult<bool>.Fail(outcome.Error!);

        var r = outcome.Value!;
        if (r.ExitCode != 0) return GitResult<bool>.Fail(r.StandardError.Trim());

        return GitResult<bool>.Ok(string.Equals(r.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Yerel + remote-tracking branch listesi (<c>refs/heads</c> + <c>refs/remotes</c>). <see cref="GitBranchInfo.IsActive"/> yalnız checkout edilmiş YEREL branch için true.</summary>
    public async Task<GitResult<IReadOnlyList<GitBranchInfo>>> ListBranchesAsync(CancellationToken ct = default)
    {
        // [A5/T69] %09 = TAB: "<refname>\t<objectname>". Sha, ref adıyla AYNI çağrıdan gelir — branch başına
        // ayrı bir rev-parse process'i spawn etmek çok branch'li bir repoda gereksiz pahalı olurdu.
        var outcome = await GitCommandExecutor.RunAsync(_runner, _gitExecutable, ["for-each-ref", "--format=%(refname)%09%(objectname)", "refs/heads", "refs/remotes"], _repoRoot, CommandTimeout, ct);
        if (!outcome.Success) return GitResult<IReadOnlyList<GitBranchInfo>>.Fail(outcome.Error!);

        var r = outcome.Value!;
        if (r.ExitCode != 0) return GitResult<IReadOnlyList<GitBranchInfo>>.Fail(r.StandardError.Trim());

        var currentBranch = await GetCurrentBranchAsync(ct);
        string? active = currentBranch.Success ? currentBranch.Value : null;

        var list = new List<GitBranchInfo>();
        using var reader = new StringReader(r.StandardOutput);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;

            int tab = line.IndexOf('\t');
            if (tab < 0) continue; // beklenmeyen satır formatı — savunmacı biçimde atlanır (ParseLsTreeBlobHashes deseni)
            string refName = line[..tab];
            string sha = line[(tab + 1)..].Trim();

            if (refName.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                string name = refName["refs/heads/".Length..];
                list.Add(new GitBranchInfo(name, sha, IsRemote: false, IsActive: active is not null && name == active));
            }
            else if (refName.StartsWith("refs/remotes/", StringComparison.Ordinal))
            {
                string name = refName["refs/remotes/".Length..];
                list.Add(new GitBranchInfo(name, sha, IsRemote: true, IsActive: false));
            }
        }

        return GitResult<IReadOnlyList<GitBranchInfo>>.Ok(list);
    }

    /// <summary>
    /// [T69/K1] Sync akışının İLK adımı: yalnızca remote-tracking ref'i günceller (<c>git fetch origin
    /// &lt;branch&gt; --no-tags</c>). Checkout/pull/merge/switch KESİNLİKLE çağrılmaz — aktif branch ve
    /// working tree bu metotla ASLA değişmez (bkz. <c>SyncFetchTests</c>: HEAD + tracked dosya içeriği
    /// fetch öncesi/sonrası aynı kalır). Fetch başarısız olursa (offline, unreachable/invalid remote, git
    /// çalıştırılamadı) hata YUTULUR — throw YOK: <see cref="FetchResult.Degraded"/>=true + <see
    /// cref="FetchResult.Warning"/>, hedef SHA yerel HEAD'e düşer (K1 — Task 6/BuildSignature curSha ile
    /// aynı değere iner, güvenli taraf).
    /// </summary>
    public async Task<FetchResult> FetchRefOnlyAsync(string branch, CancellationToken ct = default)
    {
        var outcome = await GitCommandExecutor.RunAsync(_runner, _gitExecutable, ["fetch", "origin", branch, "--no-tags"], _repoRoot, CommandTimeout, ct);

        if (outcome.Success && outcome.Value!.ExitCode == 0)
        {
            var tracking = await GetRemoteTrackingShaAsync(branch, ct);
            if (tracking.Success && tracking.Value is not null)
                return new FetchResult { Degraded = false, TargetSha = tracking.Value };

            // fetch başarılı ama remote-tracking ref beklenen şekilde okunamadı — beklenmeyen durum,
            // güvenli tarafta kalınarak degrade edilir (throw yok).
            return await DegradeToLocalHeadAsync(
                $"git fetch succeeded but the remote-tracking ref could not be read: {tracking.Error ?? "unexpected empty result"}", ct);
        }

        string reason = outcome.Success ? GitCommandExecutor.DescribeGitFailure(outcome.Value!) : outcome.Error!;
        return await DegradeToLocalHeadAsync(
            $"git fetch failed (offline/unreachable/invalid remote) — falling back to the local HEAD: {reason}", ct);
    }

    /// <summary>
    /// [T69/K1] Fetch sonrası remote-tracking ref (<c>refs/remotes/origin/&lt;branch&gt;</c>) SHA'sı — Task
    /// 6/BuildSignature ve UI'daki curSha → targetSha kartı için hedef SHA kaynağı. Ref henüz mevcut değilse
    /// (hiç fetch edilmemiş / branch remote'ta yok) <c>Ok(null)</c> döner (hata DEĞİL, edge).
    /// </summary>
    public async Task<GitResult<string?>> GetRemoteTrackingShaAsync(string branch, CancellationToken ct = default)
    {
        var outcome = await GitCommandExecutor.RunAsync(_runner, _gitExecutable, ["rev-parse", "--verify", "-q", $"refs/remotes/origin/{branch}"], _repoRoot, CommandTimeout, ct);
        if (!outcome.Success) return GitResult<string?>.Fail(outcome.Error!);

        var r = outcome.Value!;
        if (r.ExitCode == 0)
        {
            string sha = r.StandardOutput.Trim();
            return IsFortyHexSha(sha)
                ? GitResult<string?>.Ok(sha)
                : GitResult<string?>.Fail($"unexpected 'git rev-parse refs/remotes/origin/{branch}' output: '{sha}'");
        }

        if (IsUnbornHeadSignal(r)) return GitResult<string?>.Ok(null); // ref yok — henüz fetch edilmemiş

        return GitResult<string?>.Fail(GitCommandExecutor.DescribeGitFailure(r));
    }

    /// <summary>
    /// [Fix wave 1 — Finding 4] YEREL branch (<c>refs/heads/&lt;branch&gt;</c>) SHA'sı — <see
    /// cref="GetRemoteTrackingShaAsync"/>'in birebir aynı desendeki (salt-okur <c>rev-parse --verify -q</c>)
    /// yerel karşılığı. Gerekçe: <see cref="ListBranchesAsync"/> kullanıcıya <c>refs/heads/*</c>'ı da listeler,
    /// yani UI yalnız yerelde var olan (henüz push edilmemiş) branch'leri de seçilebilir kılar; hedef commit'i
    /// SADECE remote-tracking ref'ten çözen bir okuyucu tam olarak o branch'leri çözemez. Ref yoksa
    /// <c>Ok(null)</c> döner (hata DEĞİL, edge — çağıran remote-tracking'e düşebilsin diye).
    /// <para><b>K1:</b> salt-okur — checkout/switch/reset YOK, aktif branch ve working tree DEĞİŞMEZ.</para>
    /// </summary>
    public async Task<GitResult<string?>> GetLocalBranchShaAsync(string branch, CancellationToken ct = default)
    {
        var outcome = await GitCommandExecutor.RunAsync(_runner, _gitExecutable, ["rev-parse", "--verify", "-q", $"refs/heads/{branch}"], _repoRoot, CommandTimeout, ct);
        if (!outcome.Success) return GitResult<string?>.Fail(outcome.Error!);

        var r = outcome.Value!;
        if (r.ExitCode == 0)
        {
            string sha = r.StandardOutput.Trim();
            return IsFortyHexSha(sha)
                ? GitResult<string?>.Ok(sha)
                : GitResult<string?>.Fail($"unexpected 'git rev-parse refs/heads/{branch}' output: '{sha}'");
        }

        if (IsUnbornHeadSignal(r)) return GitResult<string?>.Ok(null); // yerel ref yok — branch yalnız remote'ta olabilir

        return GitResult<string?>.Fail(GitCommandExecutor.DescribeGitFailure(r));
    }

    /// <summary>K1 fallback: fetch başarısız olduğunda hedef SHA yerel HEAD'e düşer; HEAD de okunamazsa null (güvenli taraf, throw yok).</summary>
    private async Task<FetchResult> DegradeToLocalHeadAsync(string warning, CancellationToken ct)
    {
        var head = await GetHeadCommitAsync(ct);
        return new FetchResult { Degraded = true, TargetSha = head.Success ? head.Value : null, Warning = warning };
    }

    /// <summary>
    /// [A6 refinement — Task 7b] HEAD'deki TÜM tracked dosyaların repo-relative path → blob SHA eşlemesi,
    /// TEK bir <c>git ls-tree -r HEAD</c> çağrısıyla (<c>&lt;mode&gt; &lt;type&gt; &lt;sha&gt;\t&lt;path&gt;</c>
    /// satırları parse edilir; yalnız <c>type=blob</c> satırları alınır — submodule girdileri (<c>type=commit</c>)
    /// dışlanır). Bu harita, per-project COMMITTED fingerprint'in kaynağıdır (bkz. <see
    /// cref="BuildOrchestrator.Core.Incremental.IncrementalPlanner.ComputeCommittedFingerprint"/>): eskiden
    /// TÜM projelere GLOBAL olarak enjekte edilen repo-HEAD commit SHA'sı yerine, her projenin YALNIZ kendi
    /// build-etkileyen dosyalarının committed blob içerik kimliği kullanılır (§4 — DLL/bin/obj timestamp'ı
    /// yine ASLA okunmaz, yalnız git'in kendi committed blob SHA'sı).
    /// <para>
    /// No-commits (unborn HEAD) repo → <c>Ok(boş map)</c> (hata DEĞİL, tanımlı edge — <see
    /// cref="GetHeadCommitAsync"/> ile tutarlı). Bu edge, <c>ls-tree -r HEAD</c>'in stderr METNİNE (İngilizce
    /// "fatal: Not a valid object name HEAD" vb.) BAKILARAK değil, <see cref="GetHeadCommitAsync"/> ile AYNI
    /// locale-bağımsız primitive'e (<c>rev-parse --verify -q HEAD</c>, <see cref="IsUnbornHeadSignal"/> — exit=1
    /// + BOŞ stderr) önden bir ön-kontrol (pre-check) olarak başvurularak tespit edilir: HEAD unborn ise ls-tree
    /// HİÇ ÇALIŞTIRILMAZ. [Review fix — Task 7b] Önceki sürüm ls-tree'nin İngilizce stderr metnini (`"Not a valid
    /// object name"`) `Contains` ile arıyordu — bu, TÜRKÇE (veya başka) git locale'inde çevrilmiş fatal mesajla
    /// sessizce kaçırılır ve no-commits repo yanlışlıkla <c>Fail</c> döndürürdü; tam olarak bu dosyada daha önce
    /// KASITLI olarak terk edilen yaklaşımın (bkz. <see cref="IsUnbornHeadSignal"/> yorumu) bir tekrarıydı.
    /// HEAD unborn DEĞİLSE ls-tree çalıştırılır ve ANY (herhangi bir) sıfır-olmayan exit kodu gerçek bir git
    /// hatasıdır — Fail.
    /// </para>
    /// </summary>
    public async Task<GitResult<IReadOnlyDictionary<string, string>>> GetTrackedBlobHashesAsync(CancellationToken ct = default)
    {
        // Locale-bağımsız no-commits ön-kontrolü: GetHeadCommitAsync ile AYNI primitive (rev-parse --verify -q
        // HEAD + IsUnbornHeadSignal). ls-tree'nin stderr metni ASLA parse edilmez (bkz. yukarıdaki tip özeti).
        var head = await GetHeadCommitAsync(ct);
        if (!head.Success) return GitResult<IReadOnlyDictionary<string, string>>.Fail(head.Error!);
        if (head.Value is null)
            return GitResult<IReadOnlyDictionary<string, string>>.Ok(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var outcome = await GitCommandExecutor.RunAsync(_runner, _gitExecutable, ["ls-tree", "-r", "HEAD"], _repoRoot, CommandTimeout, ct);
        if (!outcome.Success) return GitResult<IReadOnlyDictionary<string, string>>.Fail(outcome.Error!);

        var r = outcome.Value!;
        if (r.ExitCode != 0) return GitResult<IReadOnlyDictionary<string, string>>.Fail(GitCommandExecutor.DescribeGitFailure(r));

        // ExitCode==0 iken stdout parse edilir; stderr'de CRLF-dönüşüm UYARISI gibi zararsız satırlar
        // olabilir (deneysel doğrulandı) — başarı, YALNIZ ExitCode'a bakılarak belirlenir.
        return GitResult<IReadOnlyDictionary<string, string>>.Ok(ParseLsTreeBlobHashes(r.StandardOutput));
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
                ?? "unknown git error";
            return new GitRepoState
            {
                HeadCommit = headResult.Success ? headResult.Value : null,
                Branch = branchResult.Success ? branchResult.Value : null,
                TreatAsDirty = true,
                HasError = true,
                Error = err,
                Warnings = [$"git query failed — treat-as-dirty: {err}"],
            };
        }

        bool hasNoCommits = headResult.Value is null;
        bool isDetached = branchResult.Value is null;
        bool isShallow = shallowResult.Value;
        var dirtyPaths = dirtyResult.Value!;

        var warnings = new List<string>();
        if (hasNoCommits) warnings.Add("the repository has no commits yet (unborn HEAD) — treat-as-dirty.");
        if (isDetached) warnings.Add("HEAD detached — the branch could not be determined.");
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

    /// <summary>
    /// "no-commits" / "detached HEAD" edge sinyalinin TEK doğru tanımı: exit=1 + BOŞ stderr. (Örn.
    /// "not a git repository" metnine bakmak yanlış ve dar bir yaklaşımdır — bkz. tip özeti.)
    /// </summary>
    private static bool IsUnbornHeadSignal(ProcessResult r) => r.ExitCode == 1 && string.IsNullOrEmpty(r.StandardError);

    private static bool IsFortyHexSha(string s) => s.Length == 40 && s.All(Uri.IsHexDigit);

    /// <summary>
    /// <c>git ls-tree -r HEAD</c> çıktısını (<c>&lt;mode&gt; &lt;type&gt; &lt;sha&gt;\t&lt;path&gt;</c> satırları)
    /// path → blob SHA haritasına indirger; yalnız <c>type=blob</c> (submodule/<c>commit</c> girdileri dışlanır).
    /// Path git tarafından tırnaklanmış olabilir (özel karakterler) — <see cref="ParsePorcelainPaths"/> ile
    /// tutarlı biçimde kırpılır.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ParseLsTreeBlobHashes(string lsTreeOutput)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(lsTreeOutput);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;

            int tab = line.IndexOf('\t');
            if (tab < 0) continue; // beklenmeyen satır formatı — savunmacı biçimde atlanır

            string[] meta = line[..tab].Split(' ', 3); // "<mode> <type> <sha>"
            if (meta.Length != 3) continue;

            string type = meta[1];
            string sha = meta[2];
            if (!string.Equals(type, "blob", StringComparison.Ordinal)) continue; // submodule (commit) vb. dışlanır

            string path = line[(tab + 1)..].Trim('"');
            map[path] = sha;
        }
        return map;
    }

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
