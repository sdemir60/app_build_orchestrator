using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.Git;

/// <summary>[T29/K3] Bir <see cref="WorktreeManager.PlanWorktree"/> kararının sonucu — in-place mi worktree mi.</summary>
public enum WorktreeMode
{
    /// <summary>Aktif branch + toggle KAPALI: worktree YOK, mevcut çalışma ağacında (local değişiklikler dahil) derlenir.</summary>
    InPlace,

    /// <summary>Aktif branch + toggle AÇIK (committed state) VEYA farklı bir branch (K1 — zorunlu, aktif branch ASLA checkout edilmez).</summary>
    Worktree,
}

/// <summary>
/// [T29/K3] <see cref="WorktreeManager.PlanWorktree"/> çıktısı: 3-durum matrisinin kararı + (Worktree modunda)
/// Build anında kullanılacak hedef path + K3 niyet satırı. Bu bir NİYETTİR — üretildiği anda hiçbir git komutu
/// çalışmaz, hiçbir dizin oluşturulmaz (bkz. <see cref="WorktreeManager.PlanWorktree"/> XML doc).
/// </summary>
public sealed record WorktreePlan
{
    public required WorktreeMode Mode { get; init; }

    /// <summary>Worktree modunda Build anında <see cref="WorktreeManager.PrepareWorktreeAsync"/>'in kullanacağı hedef path. InPlace'de <c>null</c>.</summary>
    public string? Path { get; init; }

    /// <summary>
    /// K3 konsol niyet satırı. InPlace → boş string (niyet-worktree satırı yok). Worktree (aynı branch, toggle
    /// AÇIK) → tek satır <c>"branch target: &lt;name&gt; (&lt;sha&gt;) — worktree will be used at Build"</c>.
    /// Worktree (FARKLI branch, zorunlu) → yukarıdaki satır + <c>'\n'</c> + <c>"Branch changed: &lt;name&gt; —
    /// Sync required"</c> (branch gerçekten değiştiği için ikinci satır yalnız bu durumda eklenir).
    /// </summary>
    public required string IntentLine { get; init; }

    /// <summary>
    /// Planlamada seçilen branch adı — <see cref="WorktreeManager.PrepareWorktreeAsync"/>'in pool bookkeeping'i
    /// (branch metadata sidecar dosyası, bkz. <see cref="WorktreeManager.ListWorktreesAsync"/>) için taşınır.
    /// Git komutunun kendisi (`worktree add --detach &lt;path&gt; &lt;sha&gt;`) buna ihtiyaç duymaz — yalnız
    /// havuzun kendi "hangi worktree hangi branch'e ait" defterini tutmak için.
    /// </summary>
    public required string SelectedBranch { get; init; }

    /// <summary>
    /// [Review fix — Task 9] <see cref="PlanWorktree"/>'nin çözdüğü ve <see cref="IntentLine"/>'da (K3) kullanıcıya
    /// GÖSTERİLEN sha. <see cref="WorktreeManager.PrepareWorktreeAsync"/>, Build anında AYNI sha'yı kullanır —
    /// böylece K3 satırında gösterilen commit ile gerçekten build edilen commit GARANTİ OLARAK aynıdır (önceden
    /// <c>PrepareWorktreeAsync</c> ayrı bir <c>sha</c> parametresi alıyordu; caller yanlışlıkla farklı bir sha
    /// geçerse K3'te gösterilenden FARKLI bir commit build edilebilirdi).
    /// </summary>
    public required string Sha { get; init; }
}

/// <summary>[T14] Havuzdaki tek bir worktree'nin envanter kaydı — <see cref="WorktreeManager.ListWorktreesAsync"/> çıktısı.</summary>
public sealed record WorktreeInfo
{
    /// <summary>Havuz kökü altındaki dizin adı (ör. <c>"feature-x-1"</c>) — <see cref="WorktreeManager.DeleteAsync"/>'e verilecek kimlik.</summary>
    public required string Name { get; init; }

    /// <summary>Bu worktree'nin oluşturulduğu branch adı (metadata sidecar'dan okunur) — worktree'ler DAİMA detached HEAD olduğundan git'in kendisi branch bilgisi vermez.</summary>
    public string? Branch { get; init; }

    public required string Path { get; init; }

    /// <summary>
    /// true ⇔ <see cref="Branch"/>, ana repodaki O ANKİ checkout edilmiş branch'e eşit. Bu bir GÖSTERİM/uyarı
    /// bilgisidir (havuz ekranı: "bu worktree senin şu an üzerinde olduğun branch'in kopyası").
    /// <para>
    /// [Fix wave 1 — Finding 1] <see cref="WorktreeManager.PruneToCapAsync"/> bunu ARTIK muafiyet olarak
    /// KULLANMAZ: en yaygın yol ("aktif branch'im için worktree aç") havuzdaki HER worktree'yi IsActive
    /// yapıyordu, aday listesi hep boş kalıyordu ve cap ne olursa olsun HİÇBİR ŞEY tahliye edilemiyordu.
    /// Tahliye muafiyeti artık "O ANDA hazırlanan/kullanılan worktree" ile sınırlıdır (bkz. o metodun
    /// <c>inUseName</c> parametresi).
    /// </para>
    /// </summary>
    public required bool IsActive { get; init; }

    /// <summary>Worktree dizini altındaki tüm dosyaların toplam boyutu (recursive).</summary>
    public required long SizeBytes { get; init; }

    /// <summary>Worktree içindeki en yeni dosya <c>LastWriteTimeUtc</c>'si — LRU sıralamasının girdisi.</summary>
    public required DateTime LastUsedUtc { get; init; }
}

/// <summary>
/// [T29 · T14 · K3] Branch-driven worktree modeli: branch SEÇİMİ bir NİYETTİR — seçim anında hiçbir git
/// komutu çalışmaz (bkz. <see cref="PlanWorktree"/>). Gerçek <c>git worktree add --detach</c> YALNIZ Build
/// anında (<see cref="PrepareWorktreeAsync"/>) çalışır. Aktif branch bu sınıfın HİÇBİR metodu tarafından ASLA
/// checkout/switch edilmez (K1/v6Δ-1) — ANA REPO'da yalnız <c>worktree add/remove/list</c> komutları çalıştırılır.
/// <para>
/// [Fix wave 1 — Finding 2] Tek istisna <see cref="ReuseWorktreeAsync"/>'in <c>reset --hard</c>'ıdır ve o komutun
/// çalışma dizini HER ZAMAN havuzun İÇİNDEKİ (bizim açtığımız, DAİMA detached) bir worktree'dir — ana repo değil.
/// </para>
/// <para>
/// Havuz (T14, <see cref="ListWorktreesAsync"/>/<see cref="PruneToCapAsync"/>/<see cref="DeleteAsync"/>)
/// kökü varsayılan olarak <see cref="DefaultPoolRoot"/> (<c>%LOCALAPPDATA%\BuildOrchestrator\worktrees\</c>,
/// N3/D12 — kalıcı, run'lar arası korunur). Naming = Task 3 <see cref="PathSanitizer.NextWorktreeName"/>.
/// </para>
/// <para>
/// Worktree'ler DAİMA <c>--detach</c> ile oluşturulur (K1 — hiçbir branch bir worktree'ye "checkout edilmiş"
/// olarak bağlı kalmaz), bu yüzden <c>git worktree list</c>'in kendi <c>branch</c> alanı bizim worktree'lerimiz
/// için hiçbir zaman dolu gelmez (hep <c>detached</c>). Bu yüzden "bu worktree hangi branch içindi" bilgisi
/// küçük bir metadata sidecar dosyasıyla (<see cref="MetadataFileName"/>, <see cref="PrepareWorktreeAsync"/>'in
/// yazdığı) ayrıca tutulur — <see cref="WorktreeInfo.IsActive"/> kararı buna dayanır.
/// </para>
/// </summary>
public sealed class WorktreeManager(IProcessRunner runner, string repoRoot, string poolRoot, string gitExecutable = "git")
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5); // worktree add/remove büyük repo'da 30s'i aşabilir (GitService'in salt-okur 30s'inden kasıtlı farklı)
    private const string MetadataFileName = ".bo-worktree-branch.txt";

    private readonly IProcessRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    private readonly string _repoRoot = repoRoot ?? throw new ArgumentNullException(nameof(repoRoot));
    private readonly string _poolRoot = poolRoot ?? throw new ArgumentNullException(nameof(poolRoot));
    private readonly string _gitExecutable = string.IsNullOrWhiteSpace(gitExecutable)
        ? throw new ArgumentException("gitExecutable boş olamaz.", nameof(gitExecutable))
        : gitExecutable;

    // "Aktif branch" sorgusu için mevcut, test edilmiş GitService mantığı (detached HEAD edge dahil) YENİDEN
    // KULLANILIR — burada elle tekrar implement edilmez.
    private readonly GitService _gitService = new(runner, repoRoot, gitExecutable);

    /// <summary>[N3/D12] Havuzun varsayılan kalıcı kökü. Testler kendi izole temp dizinlerini kurucuya verir.</summary>
    public static string DefaultPoolRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BuildOrchestrator", "worktrees");

    /// <summary>
    /// [K3] 3-durum matrisi — SALT KARAR, hiçbir git komutu ÇALIŞTIRILMAZ, hiçbir dizin OLUŞTURULMAZ:
    /// <list type="bullet">
    /// <item>aktif branch + <paramref name="useWorktreeToggle"/>=false → <see cref="WorktreeMode.InPlace"/>
    /// (worktree yok, <see cref="WorktreePlan.Path"/>=null, <see cref="WorktreePlan.IntentLine"/>="").</item>
    /// <item>aktif branch + <paramref name="useWorktreeToggle"/>=true → <see cref="WorktreeMode.Worktree"/>
    /// (committed state'te worktree; branch değişmediği için niyet satırı TEK satır).</item>
    /// <item><paramref name="selectedBranch"/> ≠ <paramref name="activeBranch"/> → <see cref="WorktreeMode.Worktree"/>
    /// ZORUNLU (K1 — aktif branch ASLA checkout edilmez); niyet satırı İKİ satır (+ "Branch changed... Sync required").</item>
    /// </list>
    /// Worktree modunda hedef <see cref="WorktreePlan.Path"/>, havuz kökündeki MEVCUT alt dizinlere bakılarak
    /// (salt dosya sistemi okuması — git komutu değil) Task 3 <see cref="PathSanitizer.NextWorktreeName"/> ile
    /// deterministik hesaplanır; bu path, Build anında <see cref="PrepareWorktreeAsync"/>'e AYNEN verilir.
    /// </summary>
    public WorktreePlan PlanWorktree(string activeBranch, string selectedBranch, bool useWorktreeToggle, string selectedSha)
    {
        if (string.IsNullOrWhiteSpace(activeBranch)) throw new ArgumentException("activeBranch boş olamaz.", nameof(activeBranch));
        if (string.IsNullOrWhiteSpace(selectedBranch)) throw new ArgumentException("selectedBranch boş olamaz.", nameof(selectedBranch));
        if (string.IsNullOrWhiteSpace(selectedSha)) throw new ArgumentException("selectedSha boş olamaz.", nameof(selectedSha));

        bool differentBranch = !string.Equals(activeBranch, selectedBranch, StringComparison.Ordinal);

        if (!differentBranch && !useWorktreeToggle)
        {
            return new WorktreePlan { Mode = WorktreeMode.InPlace, Path = null, IntentLine = string.Empty, SelectedBranch = selectedBranch, Sha = selectedSha };
        }

        var existingNames = Directory.Exists(_poolRoot)
            ? Directory.EnumerateDirectories(_poolRoot).Select(d => Path.GetFileName(d) ?? string.Empty)
            : Enumerable.Empty<string>();
        string name = PathSanitizer.NextWorktreeName(selectedBranch, existingNames);
        string path = Path.Combine(_poolRoot, name);

        // [Review fix — Task 9] K3 satırında gösterilen sha ile Build anında PrepareWorktreeAsync'in kullanacağı
        // sha AYNI kaynaktan (selectedSha) geliyor ve plan.Sha'da taşınıyor — ikisinin ayrışması imkansız.
        string intentLine = $"branch target: {selectedBranch} ({selectedSha}) — worktree will be used at Build";
        if (differentBranch)
            intentLine = intentLine + "\n" + $"Branch changed: {selectedBranch} — Sync required";

        return new WorktreePlan { Mode = WorktreeMode.Worktree, Path = path, IntentLine = intentLine, SelectedBranch = selectedBranch, Sha = selectedSha };
    }

    /// <summary>
    /// [K3] Build ANI: gerçek <c>git worktree add --detach &lt;plan.Path&gt; &lt;plan.Sha&gt;</c> — bu, GİT
    /// WORKTREE AÇAN TEK YER. <c>switch</c>/<c>checkout</c> ASLA çağrılmaz. <paramref name="plan"/> <see
    /// cref="WorktreeMode.InPlace"/> ise (hazırlanacak bir worktree yok) tanımlı hata döner (throw yok).
    /// [Review fix — Task 9] sha ayrı bir parametre DEĞİL — <see cref="WorktreePlan.Sha"/> kullanılır: bu,
    /// <see cref="PlanWorktree"/>'nin K3 <see cref="WorktreePlan.IntentLine"/>'ında kullanıcıya GÖSTERDİĞİ AYNI
    /// sha'dır, böylece caller'ın yanlışlıkla farklı bir sha ile farklı bir commit build etmesi YAPISAL OLARAK
    /// imkansız hale gelir. Başarı sonrası, havuz bookkeeping'i için küçük bir branch-metadata sidecar dosyası
    /// yazılır (best-effort — yazım başarısız olsa bile worktree'nin kendisi zaten oluşturulmuştur, hata yutulur).
    /// </summary>
    public async Task<GitResult<string>> PrepareWorktreeAsync(WorktreePlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Mode != WorktreeMode.Worktree || plan.Path is null)
            return GitResult<string>.Fail("InPlace plan için worktree hazırlanamaz (PrepareWorktreeAsync yalnız Worktree modunda kullanılabilir).");

        Directory.CreateDirectory(_poolRoot);

        var outcome = await GitCommandExecutor.RunAsync(_runner, _gitExecutable, ["worktree", "add", "--detach", plan.Path, plan.Sha], _repoRoot, CommandTimeout, ct);
        if (!outcome.Success) return GitResult<string>.Fail(outcome.Error!);

        var r = outcome.Value!;
        if (r.ExitCode != 0) return GitResult<string>.Fail(GitCommandExecutor.DescribeGitFailure(r));

        try
        {
            File.WriteAllText(Path.Combine(plan.Path, MetadataFileName), plan.SelectedBranch);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // worktree başarıyla oluşturuldu — branch metadata'sı olmadan da kullanılabilir (yalnız pool/LRU
            // envanterinde Branch/IsActive bilgisi eksik kalır), bu yüzden burası fail DÖNDÜRMEZ.
        }

        return GitResult<string>.Ok(plan.Path);
    }

    /// <summary>
    /// [T14] Havuzdaki (yalnız <see cref="_poolRoot"/> altındaki, ana çalışma ağacı HARİÇ) worktree'lerin
    /// envanteri: <c>git worktree list --porcelain</c> ile path'ler (otoriter kaynak — gerçekten var olan git
    /// worktree'leri), her biri için disk boyutu + son kullanım zamanı (recursive dosya taraması) ve branch
    /// metadata sidecar'ından <see cref="WorktreeInfo.Branch"/>. <see cref="WorktreeInfo.IsActive"/>, o worktree'nin
    /// branch'i ana repoda O ANDA checkout edilmiş branch'e eşitse true (bu worktree'ler her zaman detached
    /// olduğundan git'in kendi <c>branch</c> alanına değil, bizim sidecar'ımıza dayanır).
    /// </summary>
    public async Task<GitResult<IReadOnlyList<WorktreeInfo>>> ListWorktreesAsync(CancellationToken ct = default)
    {
        var entriesResult = await ListPoolEntriesAsync(ct);
        if (!entriesResult.Success) return GitResult<IReadOnlyList<WorktreeInfo>>.Fail(entriesResult.Error!);

        var activeBranchResult = await _gitService.GetCurrentBranchAsync(ct);
        string? activeBranch = activeBranchResult.Success ? activeBranchResult.Value : null;

        var list = new List<WorktreeInfo>();
        foreach (var entry in entriesResult.Value!)
        {
            var (size, lastUsed) = ComputeDirStats(entry.Path);
            bool isActive = activeBranch is not null && entry.Branch is not null && string.Equals(entry.Branch, activeBranch, StringComparison.Ordinal);

            list.Add(new WorktreeInfo { Name = entry.Name, Branch = entry.Branch, Path = entry.Path, IsActive = isActive, SizeBytes = size, LastUsedUtc = lastUsed });
        }

        return GitResult<IReadOnlyList<WorktreeInfo>>.Ok(list);
    }

    /// <summary>
    /// [Fix wave 1 — Finding 2] Havuzda AYNI branch için zaten bir worktree varsa onu YENİDEN KULLANIR ve
    /// <paramref name="sha"/>'ya günceller; yoksa <c>Ok(null)</c> döner (çağıran yenisini açar).
    /// <para>
    /// <b>Neden:</b> <see cref="PlanWorktree"/> her çağrıda BİR SONRAKİ kullanılmamış adı üretir
    /// (<c>main-1</c>, <c>main-2</c>, …), yani her Build sıfırdan bir dizin açardı. Bunun bedeli yalnız disk
    /// değildir: <c>BaseIntermediateOutputPath</c> proje kimliğinden (tam csproj YOLU) türetildiği için obj HER
    /// run'da SOĞUK olur — havuzun kalıcı olmasının (N3/D12) tek amacı olan sıcak-obj faydası hiç gerçekleşmez.
    /// </para>
    /// <para>
    /// <b>K1:</b> güncelleme YALNIZ havuz worktree'sinin İÇİNDE yapılır (<c>git reset --hard &lt;sha&gt;</c>,
    /// çalışma dizini = o worktree). Ana repo hiçbir komutun hedefi değildir: aday yollar git'in kendi
    /// <c>worktree list --porcelain</c> çıktısından gelir ve havuz kökü ALTINDA olmayanlar (yani ana çalışma
    /// ağacı) <see cref="ListPoolEntriesAsync"/> tarafından zaten elenir. Ek bir güvenlik kapısı olarak
    /// worktree'nin HEAD'inin DETACHED olduğu doğrulanır: attached bir HEAD'e <c>reset --hard</c> atmak bir
    /// BRANCH ref'ini oynatırdı — o durumda yeniden kullanım yapılmaz (çağıran yeni worktree açar).
    /// </para>
    /// <para>
    /// [Fix wave 2 — Fix 4] <b>Kullanıcıya görünen sonuç:</b> <c>reset --hard</c> havuz worktree'sindeki
    /// TRACKED dosyalardaki değişiklikleri sessizce ve geri döndürülemez şekilde atar. Bu, app'in kendi
    /// scratch ağacı için doğru davranıştır (obj sıcak kalır, kaynaklar hedef commit'e eşitlenir) — ama bir
    /// kullanıcı bir havuz worktree'sini editörde açıp orada elle düzenleme yaparsa, bir sonraki yeniden
    /// kullanımda o değişiklikler kaybolur.
    /// </para>
    /// </summary>
    /// <param name="preferredName">Kullanıcının açıkça istediği havuz worktree'si (<c>StartRunCommand.WorktreeName</c>);
    /// verilirse ve o ad aynı branch'in adayları arasındaysa O seçilir, aksi halde yok sayılır.</param>
    public async Task<GitResult<string?>> ReuseWorktreeAsync(
        string selectedBranch, string sha, string? preferredName = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(selectedBranch)) return GitResult<string?>.Fail("selectedBranch boş olamaz.");
        if (string.IsNullOrWhiteSpace(sha)) return GitResult<string?>.Fail("sha boş olamaz.");

        var entriesResult = await ListPoolEntriesAsync(ct);
        if (!entriesResult.Success) return GitResult<string?>.Fail(entriesResult.Error!);

        // Aday = sidecar branch'i seçilen branch'e eşit olan havuz worktree'leri. Sıralama ada göre ORDINAL:
        // deterministik olsun (aynı girdi → aynı worktree) diye; LRU burada anlamsızdır, hepsi aynı branch'in
        // aynı derecede geçerli kopyasıdır.
        var candidates = entriesResult.Value!
            .Where(e => e.Branch is not null && string.Equals(e.Branch, selectedBranch, StringComparison.Ordinal))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0) return GitResult<string?>.Ok(null); // aday yok → çağıran yeni açar

        var chosen = (PathSanitizer.IsSafeSegment(preferredName ?? string.Empty)
            ? candidates.FirstOrDefault(e => string.Equals(e.Name, preferredName, StringComparison.OrdinalIgnoreCase))
            : null) ?? candidates[0];

        // [Fix wave 2 — Fix 3] Belt-and-braces: ListPoolEntriesAsync'in havuz-üyeliği kontrolü `Path.GetFullPath`
        // ile normalize eder — bu, symlink/junction ÇÖZMEZ. Havuz kökü altına konan bir junction ana repoyu
        // GÖSTERİYORSA metinsel prefix kontrolünü geçer ve aşağıdaki `reset --hard` ANA REPO'nun içinde koşardı
        // (K1 ihlali). App'in kendi ürettiği yollarla bu ERİŞİLMEZ — savunma amaçlı son bir kapı.
        if (string.Equals(NormalizeForCompare(chosen.Path), NormalizeForCompare(_repoRoot), StringComparison.OrdinalIgnoreCase))
            return GitResult<string?>.Fail($"havuz worktree'si ('{chosen.Name}') ana repo köküyle çakışıyor — güvenlik için yeniden kullanılmıyor.");

        // K1 güvenlik kapısı — bkz. tip özeti: attached HEAD'e reset atmak bir branch ref'ini oynatırdı.
        var branchAtWorktree = await new GitService(_runner, chosen.Path, _gitExecutable).GetCurrentBranchAsync(ct);
        if (!branchAtWorktree.Success)
            return GitResult<string?>.Fail($"havuz worktree'sinin ('{chosen.Name}') HEAD'i okunamadı: {branchAtWorktree.Error}");
        if (branchAtWorktree.Value is { } attached)
            return GitResult<string?>.Fail($"havuz worktree'si ('{chosen.Name}') detached değil ('{attached}' checkout edilmiş) — yeniden kullanılmıyor.");

        var outcome = await GitCommandExecutor.RunAsync(_runner, _gitExecutable, ["reset", "--hard", sha], chosen.Path, CommandTimeout, ct);
        if (!outcome.Success) return GitResult<string?>.Fail(outcome.Error!);

        var r = outcome.Value!;
        if (r.ExitCode != 0) return GitResult<string?>.Fail(GitCommandExecutor.DescribeGitFailure(r));

        // Sidecar zaten doğru branch'i taşıyor; best-effort tazeleme (dosya silinmişse geri gelir).
        try { File.WriteAllText(Path.Combine(chosen.Path, MetadataFileName), selectedBranch); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        return GitResult<string?>.Ok(chosen.Path);
    }

    /// <summary>
    /// [T14] Toplam havuz boyutu <paramref name="maxBytes"/>'ı aşarsa, EN AZ kullanılan (LRU —
    /// <see cref="WorktreeInfo.LastUsedUtc"/> artan sırada) worktree'lerden başlayarak cap altına inene kadar
    /// siler. Silinen worktree'lerin adlarını döner. Ana çalışma ağacı zaten <see cref="ListWorktreesAsync"/>
    /// tarafından hiç listelenmez, bu yüzden ayrıca korunmasına gerek yoktur.
    /// <para>
    /// [Fix wave 1 — Finding 1] Muafiyet <see cref="WorktreeInfo.IsActive"/> DEĞİL, <paramref name="inUseName"/>'dir:
    /// "branch'i aktif branch'e eşit" olan worktree'leri muaf tutmak, en yaygın yolu (aktif branch için worktree
    /// açmak) TÜMÜYLE tahliye edilemez kılıyordu — aday listesi hep boştu ve cap hiçbir zaman iş yapamıyordu.
    /// Korunması gereken tek şey O ANDA hazırlanan/kullanılan worktree'dir.
    /// </para>
    /// <para>
    /// [Fix wave 1 — Finding 6] UCUZ ÖN-KONTROL: havuzda <paramref name="inUseName"/> dışında hiçbir dizin
    /// yoksa tahliye edilecek aday da yoktur — bu durumda ne git çağrısı ne de <see cref="ComputeDirStats"/>'in
    /// rekürsif dosya taraması yapılır. Cap gerçekten iş yapabilir hale geldiği için (yukarı bkz.) bu taramanın
    /// bedeli artık her worktree Build'inde ödenirdi; tek-worktree havuzunda (yeniden kullanım sonrası tipik
    /// durum) tamamen atlanır.
    /// </para>
    /// </summary>
    /// <param name="inUseName">O anda hazırlanan/kullanılan havuz worktree'sinin adı — ASLA silinmez. Yeni bir
    /// worktree açılmadan ÖNCE çağrılıyorsa <c>null</c> geçilir (henüz kullanımda olan bir şey yoktur).</param>
    public async Task<GitResult<IReadOnlyList<string>>> PruneToCapAsync(
        long maxBytes, string? inUseName = null, CancellationToken ct = default)
    {
        if (!HasEvictableDirectory(inUseName)) return GitResult<IReadOnlyList<string>>.Ok([]);

        var listResult = await ListWorktreesAsync(ct);
        if (!listResult.Success) return GitResult<IReadOnlyList<string>>.Fail(listResult.Error!);

        var all = listResult.Value!;
        long total = all.Sum(w => w.SizeBytes);
        var candidates = all
            .Where(w => !string.Equals(w.Name, inUseName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => w.LastUsedUtc).ToList();

        var removed = new List<string>();
        foreach (var w in candidates)
        {
            if (total <= maxBytes) break;

            var del = await DeleteAsync(w.Name, ct);
            if (!del.Success) return GitResult<IReadOnlyList<string>>.Fail(del.Error!);

            removed.Add(w.Name);
            total -= w.SizeBytes;
        }

        return GitResult<IReadOnlyList<string>>.Ok(removed);
    }

    /// <summary>[T14] Tek bir havuz worktree'sini <c>git worktree remove --force</c> ile siler (build artefact'ları untracked olabileceğinden --force gerekir).</summary>
    public async Task<GitResult<bool>> DeleteAsync(string name, CancellationToken ct = default)
    {
        if (!PathSanitizer.IsSafeSegment(name))
            return GitResult<bool>.Fail($"güvensiz worktree adı: '{name}'.");

        string path = Path.Combine(_poolRoot, name);
        var outcome = await GitCommandExecutor.RunAsync(_runner, _gitExecutable, ["worktree", "remove", "--force", path], _repoRoot, CommandTimeout, ct);
        if (!outcome.Success) return GitResult<bool>.Fail(outcome.Error!);

        var r = outcome.Value!;
        if (r.ExitCode != 0) return GitResult<bool>.Fail(GitCommandExecutor.DescribeGitFailure(r));

        return GitResult<bool>.Ok(true);
    }

    /// <summary>[Fix wave 1] Havuzdaki bir worktree'nin UCUZ kaydı: yalnız git listesi + sidecar okuması — disk taraması YOK.</summary>
    private sealed record PoolEntry(string Name, string Path, string? Branch);

    /// <summary>
    /// [Fix wave 1] <see cref="ListWorktreesAsync"/>'in disk-taramasız çekirdeği: <c>git worktree list
    /// --porcelain</c> (otoriter kaynak) → havuz kökü altındakiler → ad + sidecar branch. Boyut/LRU
    /// (<see cref="ComputeDirStats"/>) YALNIZ gerçekten gereken yerde (cap/envanter) eklenir; yeniden kullanım
    /// (<see cref="ReuseWorktreeAsync"/>) bunlara ihtiyaç duymadığı için her Build'de rekürsif tarama ödemez.
    /// </summary>
    private async Task<GitResult<IReadOnlyList<PoolEntry>>> ListPoolEntriesAsync(CancellationToken ct)
    {
        var outcome = await GitCommandExecutor.RunAsync(_runner, _gitExecutable, ["worktree", "list", "--porcelain"], _repoRoot, CommandTimeout, ct);
        if (!outcome.Success) return GitResult<IReadOnlyList<PoolEntry>>.Fail(outcome.Error!);

        var r = outcome.Value!;
        if (r.ExitCode != 0) return GitResult<IReadOnlyList<PoolEntry>>.Fail(GitCommandExecutor.DescribeGitFailure(r));

        string poolRootNormalized = NormalizeForCompare(_poolRoot);
        var list = new List<PoolEntry>();
        foreach (string rawPath in ParseWorktreePaths(r.StandardOutput))
        {
            string normalized = NormalizeForCompare(rawPath);
            if (!normalized.StartsWith(poolRootNormalized + "/", StringComparison.OrdinalIgnoreCase)) continue; // ana çalışma ağacı / havuz dışı worktree — atla

            string fullPath = Path.GetFullPath(rawPath);
            string name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            list.Add(new PoolEntry(name, fullPath, ReadBranchMetadata(fullPath)));
        }

        return GitResult<IReadOnlyList<PoolEntry>>.Ok(list);
    }

    /// <summary>
    /// [Fix wave 1 — Finding 6] Cap'in ucuz ön-kontrolü: havuz kökünde <paramref name="inUseName"/> dışında en
    /// az bir dizin var mı (tek seviye enumerasyon — rekürsif tarama YOK). false ⇒ tahliye adayı yok, pahalı
    /// yol hiç çalıştırılmaz. Enumerasyon hatasında (yarışan silme/erişim) SAVUNMACI olarak true döner: karar
    /// pahalı ama DOĞRU yola bırakılır.
    /// </summary>
    private bool HasEvictableDirectory(string? inUseName)
    {
        try
        {
            if (!Directory.Exists(_poolRoot)) return false;
            foreach (string dir in Directory.EnumerateDirectories(_poolRoot))
                if (!string.Equals(Path.GetFileName(dir), inUseName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static IEnumerable<string> ParseWorktreePaths(string porcelainOutput)
    {
        using var reader = new StringReader(porcelainOutput);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
                yield return line["worktree ".Length..];
        }
    }

    private static string NormalizeForCompare(string path) => Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');

    private static string? ReadBranchMetadata(string worktreePath)
    {
        string metaPath = Path.Combine(worktreePath, MetadataFileName);
        try
        {
            return File.Exists(metaPath) ? File.ReadAllText(metaPath).Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // best-effort — bookkeeping eksikse Branch/IsActive null/false'a düşer, throw yok
        }
    }

    private static (long SizeBytes, DateTime LastUsedUtc) ComputeDirStats(string path)
    {
        if (!Directory.Exists(path)) return (0, DateTime.MinValue);

        long size = 0;
        DateTime last = Directory.GetLastWriteTimeUtc(path);
        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(file);
                size += info.Length;
                if (info.LastWriteTimeUtc > last) last = info.LastWriteTimeUtc;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // taranırken silinmiş/kilitli dosya — best-effort tarama, es geç
            }
        }
        return (size, last);
    }
}
