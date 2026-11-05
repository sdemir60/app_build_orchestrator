using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.Git;

/// <summary>Bir harici git çalışma kopyasını güncelleme denemesinin sonucu.</summary>
public enum FastForwardStatus
{
    /// <summary>Fast-forward yapıldı; çalışma kopyası artık remote ile aynı.</summary>
    Updated,
    /// <summary>Çalışma kopyası zaten remote ile aynıydı — hiçbir şey değişmedi.</summary>
    AlreadyCurrent,
    /// <summary>Remote'a ulaşılamadı (ağ/kimlik); yerel sürümle devam edilir.</summary>
    DegradedOffline,
    /// <summary>Commit'lenmemiş yerel değişiklik var — güncelleme yapılmaz, koşu iptal edilir.</summary>
    Dirty,
    /// <summary>Yerel branch remote'tan ayrışmış; fast-forward mümkün değil — çalışma kopyasına dokunulmaz.</summary>
    Diverged,
    /// <summary>HEAD bir branch'e bağlı değil — neyin güncelleneceği belirsiz, dokunulmaz.</summary>
    Detached,
    /// <summary>Beklenmeyen git hatası (bozuk repo, git çalıştırılamadı, ...) — veri olarak döner, exception değil.</summary>
    Failed,
}

/// <param name="Status">Denemenin sonucu.</param>
/// <param name="Revision">Çalışma kopyasının güncelleme SONRASI HEAD sha'sı; okunamadıysa null.</param>
/// <param name="Detail">Kullanıcıya gösterilecek İngilizce açıklama (uyarı/hata metni); gerekmiyorsa null.</param>
/// <param name="Branch">[final review M5] Denemenin üzerinde çalıştığı (checkout edilmiş) yerel branch; branch
/// okunmadan düşen sonuçlarda (kir, detached, hata) null. Konsol satırları bu adı söyler — çağıranın bildiği ad
/// bayat olabilir.</param>
public sealed record FastForwardResult(FastForwardStatus Status, string? Revision, string? Detail, string? Branch = null);

/// <summary>
/// [D7][v1.16.0] Bir git çalışma kopyasını remote'una ilerletir — bu dosyanın (<c>RepositoryWriter.cs</c>)
/// üç mutasyon yüzeyinden biri; diğerleri <see cref="BranchSwitcher"/> (checkout + stash, §6.3/§6.6) ve
/// <see cref="WorktreeCreator"/> (Settings Save'in worktree'si). Harici
/// köklerin build öncesi güncellemesi ve ana reponun <c>N behind</c> chip'inden tetiklenen pull'u AYNI
/// ilkeli buradan geçer.
///
/// <para><b>Ana repo kuralı (bilinçli güncelleme).</b> "Araç ana repoda pull yapmaz" kuralı duruyor: araç
/// <b>kendiliğinden asla</b> ilerletmez — ne Sync, ne Build, ne bir arka plan işi bu sınıfı ana repo köküyle
/// çağırır. Tek istisna kullanıcının ALT BARDAKİ chip'e basmasıdır ve o yol da yalnız aktif branch'te, yalnız
/// fast-forward ile çalışır. Gerekçe: fetch zaten "3 commit gerideyim" diyorsa kullanıcıyı terminale
/// göndermek aracın işini yarıda bırakmaktır; fast-forward ise aracın üstlenebileceği tek git yazma işlemidir
/// — tarih yeniden yazılmaz, birleştirme kararı verilmez, kirli ağaca dokunulmaz ve sonuç geri alınabilir.</para>
///
/// <para>Sınır yine bir kaynak guard'ıyla çitlenir: mutasyon yapan git komutları BU DOSYANIN dışına
/// çıkamaz (bkz. <c>NoGitMutationOutsideTheWriterTests</c>).</para>
///
/// <para><b>Neden <c>pull</c> değil:</b> <c>pull</c> yapılandırmaya göre merge commit'i ya da rebase
/// üretebilir — ikisi de kullanıcının harici reposunu araç adına yeniden yazmak demektir. Bunun yerine akış
/// üç ayrı tipli adıma bölünür: kir kapısı, ref-only fetch, ve yalnız gerçekten fast-forward mümkünse
/// <c>merge --ff-only</c>. Fast-forward mümkün değilse çalışma kopyası olduğu gibi bırakılır.</para>
///
/// <para>Salt-okur sorgular <see cref="GitService"/>'e delege edilir (kopya yasak) — burada yalnız
/// mutasyona giden iki komut yaşar: <c>merge-base --is-ancestor</c> (karar) ve <c>merge --ff-only</c>
/// (uygulama). Hiçbir kararda lokalize stderr METNİ ayrıştırılmaz; sinyaller exit kodudur.</para>
/// </summary>
public sealed class FastForwardUpdater
{
    /// <summary>Karar sorgusu için — <see cref="GitService"/>'in salt-okur komutlarıyla aynı tavan.</summary>
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Fast-forward merge çalışma ağacını yeniden yazar; büyük bir harici repoda bu uzun sürebilir —
    /// tavan 5 dakika.</summary>
    private static readonly TimeSpan MergeTimeout = TimeSpan.FromMinutes(5);

    private readonly IProcessRunner _runner;
    private readonly string _rootPath;
    private readonly string _gitExecutable;
    private readonly GitService _git;

    /// <param name="runner">Process çalıştırıcı.</param>
    /// <param name="rootPath">İlerletilecek çalışma kopyasının kökü: harici bir kart ya da (yalnız kullanıcı
    /// chip'e bastığında) ana repo (bkz. tip özeti).</param>
    /// <param name="gitExecutable">git yürütülebiliri; testler için değiştirilebilir.</param>
    public FastForwardUpdater(IProcessRunner runner, string rootPath, string gitExecutable = "git")
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        _gitExecutable = string.IsNullOrWhiteSpace(gitExecutable)
            ? throw new ArgumentException("gitExecutable must not be empty.", nameof(gitExecutable))
            : gitExecutable;
        _git = new GitService(_runner, _rootPath, _gitExecutable);
    }

    /// <summary>
    /// Çalışma kopyasını remote'una ilerletmeyi dener. Hiçbir koşulda exception fırlatmaz — her sonuç
    /// (kir, ayrışma, offline, bozuk repo) tipli veri olarak döner.
    /// </summary>
    public async Task<FastForwardResult> UpdateAsync(CancellationToken ct = default)
    {
        // 1) Kir kapısı — fetch'ten bile ÖNCE: kullanıcının commit'lenmemiş çalışması varken bu araç o
        //    dizinde hiçbir şey yapmaz.
        var dirty = await _git.GetDirtyPathsAsync(ct);
        if (!dirty.Success) return Failed(dirty.Error);
        if (dirty.Value!.Count > 0)
            return new FastForwardResult(FastForwardStatus.Dirty, null,
                $"{dirty.Value.Count} uncommitted change(s) in the working copy");

        // 2) Branch — detached HEAD'de neyin ilerletileceği belirsizdir.
        var branch = await _git.GetCurrentBranchAsync(ct);
        if (!branch.Success) return Failed(branch.Error);
        if (branch.Value is null)
        {
            var detachedHead = await _git.GetHeadCommitAsync(ct);
            return new FastForwardResult(FastForwardStatus.Detached,
                detachedHead.Success ? detachedHead.Value : null, "HEAD is not on a branch");
        }

        // 3) Ref-only fetch — working tree'ye dokunmaz. Ulaşılamıyorsa yerel sürümle devam edilir.
        var fetch = await _git.FetchRefOnlyAsync(branch.Value, ct);
        var head = await _git.GetHeadCommitAsync(ct);
        if (!head.Success) return Failed(head.Error);

        if (fetch.Degraded)
            return new FastForwardResult(FastForwardStatus.DegradedOffline, head.Value, fetch.Warning, branch.Value);

        if (string.Equals(head.Value, fetch.TargetSha, StringComparison.Ordinal))
            return new FastForwardResult(FastForwardStatus.AlreadyCurrent, head.Value, null, branch.Value);

        // 4) Fast-forward mümkün mü? exit=0 → HEAD hedefin atası (ileri sarılabilir), exit=1 → ayrışmış.
        //    Karar exit kodundan okunur; stderr METNİ ASLA ayrıştırılmaz (lokalize olabilir).
        string trackingRef = $"refs/remotes/origin/{branch.Value}";
        var ancestry = await CommandLineTool.RunAsync(
            _runner, CommandLineTool.Git, _gitExecutable, ["merge-base", "--is-ancestor", "HEAD", trackingRef], _rootPath, QueryTimeout, ct);
        if (!ancestry.Success) return Failed(ancestry.Error);

        if (ancestry.Value!.ExitCode == 1)
            return new FastForwardResult(FastForwardStatus.Diverged, head.Value,
                $"the local branch '{branch.Value}' has diverged from origin — fast-forward is not possible", branch.Value);

        if (ancestry.Value.ExitCode != 0)
            return Failed(CommandLineTool.DescribeFailure(CommandLineTool.Git, ancestry.Value));

        // 5) Tek mutasyon: fast-forward. Merge commit'i üretmesi mümkün değildir (--ff-only).
        var merge = await CommandLineTool.RunAsync(
            _runner, CommandLineTool.Git, _gitExecutable, ["merge", "--ff-only", trackingRef], _rootPath, MergeTimeout, ct);
        if (!merge.Success) return Failed(merge.Error);
        if (merge.Value!.ExitCode != 0) return Failed(CommandLineTool.DescribeFailure(CommandLineTool.Git, merge.Value));

        var updatedHead = await _git.GetHeadCommitAsync(ct);
        if (!updatedHead.Success) return Failed(updatedHead.Error);

        return new FastForwardResult(FastForwardStatus.Updated, updatedHead.Value, null, branch.Value);
    }

    private static FastForwardResult Failed(string? detail)
        => new(FastForwardStatus.Failed, null, detail);
}

/// <param name="Status">Denemenin sonucu.</param>
/// <param name="Branch">Denemeden SONRA aktif olan yerel branch adı (başarı/AlreadyOn) ya da denemeden ÖNCEKİ
/// aktif branch (Dirty/StashFailed/Failed); okunamadıysa null.</param>
/// <param name="Revision">Başarılı bir checkout sonrası yeni HEAD sha'sı; diğer durumlarda null.</param>
/// <param name="DirtyCount">Denemeden önce kirli olan yol sayısı; kir kapısı hiç tetiklenmediyse (AlreadyOn) 0.</param>
/// <param name="StashMessage">Stash gerçekten yapıldıysa (<see cref="BranchSwitcher.StashMessage"/>) o mesaj;
/// aksi hâlde null. Checkout stash sonrası başarısız olsa bile bu alan dolu kalır — stash geri alınmaz.</param>
/// <param name="Detail">Kullanıcıya gösterilecek İngilizce açıklama (uyarı/hata metni); gerekmiyorsa null.</param>
public sealed record CheckoutResult(
    CheckoutStatus Status, string? Branch, string? Revision, int DirtyCount, string? StashMessage, string? Detail);

/// <summary>
/// [Faz 2/Task 2][§6.3] Aracın branch chip'inden tetiklenen checkout yüzeyi — <see cref="FastForwardUpdater"/>
/// ile AYNI dosyada yaşar (§6.6, kaynak guard'ı <c>NoGitMutationOutsideTheWriterTests</c>). Task 5 bunu yeni
/// bir Supervisor komutundan çağırır.
///
/// <para><b>Sıra:</b> (1) hedef zaten aktif branch ise <see cref="CheckoutStatus.AlreadyOn"/> — git'e HİÇ
/// dokunulmaz (okuma dahil dirty kontrolü bile çalışmaz). (1b) [worktree] hedef başka bir çalışma kopyasında açıksa
/// <see cref="CheckoutStatus.CheckedOutElsewhere"/> — kir kapısından ve stash'ten önce, hiçbir şey yazılmaz. (2) Kir kapısı (<see
/// cref="GitService.GetDirtyPathsAsync"/>): kirli ve <c>stashIfDirty=false</c> → <see
/// cref="CheckoutStatus.Dirty"/>, hiçbir şey yapılmaz. (3) kirli ve <c>stashIfDirty=true</c> → <c>stash push
/// -u</c>; başarısızsa <see cref="CheckoutStatus.StashFailed"/>, checkout hiç denenmez. (4) Hedef: yerel ise
/// doğrudan <c>checkout &lt;target&gt;</c>; uzak (<c>origin/x</c>) ise yerelde <c>x</c> zaten varsa <c>checkout
/// x</c> (<c>--track</c> DENENMEZ — git zaten var olan bir yerel branch adıyla reddeder), yoksa <c>checkout
/// --track &lt;target&gt;</c>. (5) exit≠0 → <see cref="CheckoutStatus.Failed"/> (stash yapıldıysa mesajı
/// sonuçta yine taşınır — stash GERİ UYGULANMAZ, kullanıcının işi kaybolmaz ama araç da onu geri almaz).
/// (6) başarı → <see cref="CheckoutStatus.Switched"/> + yeni HEAD.</para>
///
/// <para>Salt-okur sorgular <see cref="GitService"/>'e delege edilir (kopya yasak) — burada yalnız mutasyona
/// giden iki komut yaşar: <c>stash push</c> ve <c>checkout</c>. Hiçbir kararda lokalize stderr METNİ
/// ayrıştırılmaz; sinyaller exit kodudur (<see cref="CommandLineTool.DescribeFailure"/>).</para>
/// </summary>
public sealed class BranchSwitcher
{
    /// <summary>Stash/checkout çalışma ağacını yeniden yazar; büyük bir repoda uzun sürebilir —
    /// <c>FastForwardUpdater</c>'ın merge tavanıyla aynı 5 dakika.</summary>
    private static readonly TimeSpan MutationTimeout = TimeSpan.FromMinutes(5);

    /// <summary>[worktree] Karar sorgusu (hangi kopya hangi branch'te) — <see cref="GitService"/>'in salt-okur tavanıyla aynı.</summary>
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30);

    private readonly IProcessRunner _runner;
    private readonly string _rootPath;
    private readonly string _gitExecutable;
    private readonly GitService _git;

    /// <param name="runner">Process çalıştırıcı.</param>
    /// <param name="rootPath">Checkout uygulanacak çalışma kopyasının kökü — ana repo.</param>
    /// <param name="gitExecutable">git yürütülebiliri; testler için değiştirilebilir.</param>
    public BranchSwitcher(IProcessRunner runner, string rootPath, string gitExecutable = "git")
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        _gitExecutable = string.IsNullOrWhiteSpace(gitExecutable)
            ? throw new ArgumentException("gitExecutable must not be empty.", nameof(gitExecutable))
            : gitExecutable;
        _git = new GitService(_runner, _rootPath, _gitExecutable);
    }

    /// <param name="target">Branch chip'inin verdiği ad — yerel branch (<c>isRemote=false</c>) ya da
    /// <c>origin/&lt;ad&gt;</c> biçiminde bir uzak-izleme branch'i (<c>isRemote=true</c>,
    /// <see cref="GitService.ListBranchesAsync"/>'in <c>refs/remotes/</c> ön ekini kırptığı biçim).</param>
    /// <param name="isRemote">Hedefin uzak-izleme branch'i mi (<c>origin/x</c>) yoksa yerel mi olduğu.</param>
    /// <param name="stashIfDirty">Kirli ağaçta kullanıcının Settings → General ayarı: true ise stash'lenip
    /// devam edilir, false ise kirli ağaç checkout'u reddeder.</param>
    public async Task<CheckoutResult> SwitchAsync(
        string target, bool isRemote, bool stashIfDirty, CancellationToken ct = default)
    {
        string localName = isRemote ? RemoteLocalName(target) : target;

        // 0) Hedef zaten aktif branch mi — evetse git'e HİÇ dokunulmaz (dirty kontrolü dahil).
        var currentBranch = await _git.GetCurrentBranchAsync(ct);
        if (!currentBranch.Success) return Failed(currentBranch.Error);
        if (currentBranch.Value is not null && string.Equals(currentBranch.Value, localName, StringComparison.Ordinal))
            return new CheckoutResult(CheckoutStatus.AlreadyOn, currentBranch.Value, null, 0, null, null);

        // 0b) [worktree] Hedef başka bir çalışma kopyasında açık mı — git aynı branch'i iki kopyada açtırmaz. Kir kapısından ve
        //     stash'ten ÖNCE: stash'ten sonra düşen checkout kullanıcının değişikliklerini boşuna stash'e gönderirdi.
        if (await HolderOfAsync(localName, ct) is { } holder)
            return new CheckoutResult(CheckoutStatus.CheckedOutElsewhere, currentBranch.Value, null, 0, null, holder);

        // 1) Kir kapısı.
        var dirty = await _git.GetDirtyPathsAsync(ct);
        if (!dirty.Success) return Failed(dirty.Error);
        int dirtyCount = dirty.Value!.Count;

        if (dirtyCount > 0 && !stashIfDirty)
            return new CheckoutResult(CheckoutStatus.Dirty, currentBranch.Value, null, dirtyCount, null, null);

        // 2) Kirli ve stash isteniyorsa — checkout'tan ÖNCE.
        string? stashMessage = null;
        if (dirtyCount > 0 && stashIfDirty)
        {
            stashMessage = StashMessage(await CurrentLabelAsync(currentBranch.Value, ct), target);
            var stash = await CommandLineTool.RunAsync(
                _runner, CommandLineTool.Git, _gitExecutable, ["stash", "push", "-u", "-m", stashMessage], _rootPath, MutationTimeout, ct);
            if (!stash.Success)
                return new CheckoutResult(CheckoutStatus.StashFailed, currentBranch.Value, null, dirtyCount, stashMessage, stash.Error);
            if (stash.Value!.ExitCode != 0)
                return new CheckoutResult(CheckoutStatus.StashFailed, currentBranch.Value, null, dirtyCount, stashMessage,
                    CommandLineTool.DescribeFailure(CommandLineTool.Git, stash.Value));
        }

        // 3) Checkout argümanları: yerel doğrudan, uzak — yerelde varsa yerel, yoksa --track.
        IReadOnlyList<string> checkoutArgs;
        if (!isRemote)
        {
            checkoutArgs = ["checkout", target];
        }
        else
        {
            var localSha = await _git.GetLocalBranchShaAsync(localName, ct);
            if (!localSha.Success)
                return new CheckoutResult(CheckoutStatus.Failed, currentBranch.Value, null, dirtyCount, stashMessage, localSha.Error);
            checkoutArgs = localSha.Value is not null ? ["checkout", localName] : ["checkout", "--track", target];
        }

        // 4) Tek mutasyon (stash sonrası): checkout.
        var checkout = await CommandLineTool.RunAsync(
            _runner, CommandLineTool.Git, _gitExecutable, checkoutArgs, _rootPath, MutationTimeout, ct);
        if (!checkout.Success)
            return new CheckoutResult(CheckoutStatus.Failed, currentBranch.Value, null, dirtyCount, stashMessage, checkout.Error);
        if (checkout.Value!.ExitCode != 0)
            return new CheckoutResult(CheckoutStatus.Failed, currentBranch.Value, null, dirtyCount, stashMessage,
                CommandLineTool.DescribeFailure(CommandLineTool.Git, checkout.Value));

        var newHead = await _git.GetHeadCommitAsync(ct);
        if (!newHead.Success)
            return new CheckoutResult(CheckoutStatus.Failed, currentBranch.Value, null, dirtyCount, stashMessage, newHead.Error);

        return new CheckoutResult(CheckoutStatus.Switched, localName, newHead.Value, dirtyCount, stashMessage, null);
    }

    /// <summary>[worktree] <paramref name="localName"/>'i HEAD'inde tutan BAŞKA çalışma kopyasının kökü (ana kopya ya da bir
    /// worktree); yoksa ya da sorulamadıysa <c>null</c> — o hâlde karar git'in kendi kuralına kalır. Git'e sorulur
    /// (<c>worktree list --porcelain</c>), dosyadan okunmaz: reftable biçimli repoda HEAD dosyası yalnız bir yer tutucudur.
    /// Porcelain biçimi lokalize değildir ve sürümler arasında kararlıdır; kopya bloğu <c>worktree &lt;yol&gt;</c> ile başlar,
    /// branch'i <c>branch refs/heads/&lt;ad&gt;</c> satırındadır (detached kopyada bu satır yoktur).</summary>
    private async Task<string?> HolderOfAsync(string localName, CancellationToken ct)
    {
        var list = await CommandLineTool.RunAsync(
            _runner, CommandLineTool.Git, _gitExecutable, ["worktree", "list", "--porcelain"], _rootPath, QueryTimeout, ct);
        if (!list.Success || list.Value!.ExitCode != 0) return null;

        string self = NormalizePath(_rootPath);
        string branchLine = "branch refs/heads/" + localName;
        string? copy = null;
        foreach (string raw in list.Value.StandardOutput.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("worktree ", StringComparison.Ordinal)) copy = NormalizePath(line["worktree ".Length..]);
            else if (copy is not null && string.Equals(line, branchLine, StringComparison.Ordinal)
                     && !string.Equals(copy, self, StringComparison.OrdinalIgnoreCase))
                return copy;
        }
        return null;
    }

    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>Stash mesajının TEK kaynağı (kopya yasak) — konsol satırı ve <c>stash push -m</c> argümanı
    /// AYNI metni kullanır.</summary>
    public static string StashMessage(string from, string to) => $"build-orchestrator: leaving {from} for {to}";

    /// <summary>Stash mesajındaki "&lt;cur&gt;" — normal durumda branch adı, detached HEAD'de kısaltılmış sha.</summary>
    private async Task<string> CurrentLabelAsync(string? currentBranch, CancellationToken ct)
    {
        if (currentBranch is not null) return currentBranch;
        var head = await _git.GetHeadCommitAsync(ct);
        return head.Success && head.Value is not null ? RevisionText.Short(head.Value) : "HEAD";
    }

    /// <summary><c>origin/feature</c> → <c>feature</c> (ilk <c>/</c>'den sonrası; branch listesindeki
    /// <c>refs/remotes/</c> kırpması ile AYNI biçim, bkz. <see cref="GitService.ListBranchesAsync"/>).</summary>
    private static string RemoteLocalName(string remoteBranch)
    {
        int slash = remoteBranch.IndexOf('/');
        return slash < 0 ? remoteBranch : remoteBranch[(slash + 1)..];
    }

    private static CheckoutResult Failed(string? detail) => new(CheckoutStatus.Failed, null, null, 0, null, detail);
}

/// <param name="Created">Klasörde artık bu reponun worktree'si var mı (kuruldu ya da zaten oradaydı).</param>
/// <param name="Revision">Worktree varsa HEAD sha'sı; aksi hâlde null.</param>
/// <param name="Detail">Kurulmadıysa nedeni (İngilizce); kurulduysa null.</param>
public sealed record WorktreeCreateResult(bool Created, string? Revision, string? Detail);

/// <summary>
/// [worktree] Settings Save'in worktree oluşturması — bu dosyanın üçüncü mutasyon yüzeyi (<see cref="FastForwardUpdater"/> ve
/// <see cref="BranchSwitcher"/>'ın yanında; kaynak guard'ları <c>NoGitMutationOutsideTheWriterTests</c> ve
/// <c>NoWorktreeSurfaceTests</c>).
///
/// <para><b>Yalnız kullanıcının açık kararıyla:</b> komut yalnız Settings'te bir worktree klasörü kaydedildiğinde ve klasör yoksa ya
/// da boşsa gelir; ne Sync, ne Build, ne bir arka plan işi bu sınıfı çağırır.</para>
///
/// <para><b>Sıra:</b> (1) klasör yeniden denetlenir (<see cref="WorktreeFolder.Inspect"/>) — App'in kapısıyla motorun yürütmesi
/// arasında klasör değişmiş olabilir: <c>Creatable</c> değilse git'e hiç dokunulmaz ve neden döner; zaten bu reponun worktree'siyse
/// iş yapılmış sayılır. (2) Ana repo kökünde <c>git worktree add --detach &lt;klasör&gt;</c>: worktree ana kopyanın HEAD
/// commit'inde, branch'siz doğar — git aynı branch'i iki kopyada açtırmaz, yeni bir branch açmak da kullanıcının vermediği bir
/// karar olurdu. (3) Yeni worktree'nin HEAD'i okunur. Hiçbir koşulda exception fırlatmaz; sinyal exit kodudur, lokalize stderr
/// METNİ ayrıştırılmaz.</para>
/// </summary>
public sealed class WorktreeCreator
{
    /// <summary>Worktree eklemek tüm ağacı yazar; büyük bir repoda uzun sürebilir — checkout'la aynı 5 dakika.</summary>
    private static readonly TimeSpan MutationTimeout = TimeSpan.FromMinutes(5);

    private readonly IProcessRunner _runner;
    private readonly string _repositoryRoot;
    private readonly string _gitExecutable;

    /// <param name="runner">Process çalıştırıcı.</param>
    /// <param name="repositoryRoot">Ana reponun kökü — worktree her zaman buradan eklenir.</param>
    /// <param name="gitExecutable">git yürütülebiliri; testler için değiştirilebilir.</param>
    public WorktreeCreator(IProcessRunner runner, string repositoryRoot, string gitExecutable = "git")
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _repositoryRoot = repositoryRoot ?? throw new ArgumentNullException(nameof(repositoryRoot));
        _gitExecutable = string.IsNullOrWhiteSpace(gitExecutable)
            ? throw new ArgumentException("gitExecutable must not be empty.", nameof(gitExecutable))
            : gitExecutable;
    }

    public async Task<WorktreeCreateResult> CreateAsync(string folder, CancellationToken ct = default)
    {
        var state = WorktreeFolder.Inspect(_repositoryRoot, folder);
        if (state == WorktreeFolderState.Linked) return new WorktreeCreateResult(true, await HeadOfAsync(folder, ct), null);
        if (state != WorktreeFolderState.Creatable) return new WorktreeCreateResult(false, null, WorktreeFolder.Describe(state));

        var add = await CommandLineTool.RunAsync(
            _runner, CommandLineTool.Git, _gitExecutable, ["worktree", "add", "--detach", folder], _repositoryRoot, MutationTimeout, ct);
        if (!add.Success) return new WorktreeCreateResult(false, null, add.Error);
        if (add.Value!.ExitCode != 0)
            return new WorktreeCreateResult(false, null, CommandLineTool.DescribeFailure(CommandLineTool.Git, add.Value));

        return new WorktreeCreateResult(true, await HeadOfAsync(folder, ct), null);
    }

    private async Task<string?> HeadOfAsync(string folder, CancellationToken ct)
    {
        var head = await new GitService(_runner, folder, _gitExecutable).GetHeadCommitAsync(ct);
        return head.Success ? head.Value : null;
    }
}
