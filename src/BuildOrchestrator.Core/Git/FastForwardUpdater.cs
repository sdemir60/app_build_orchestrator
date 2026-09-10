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
public sealed record FastForwardResult(FastForwardStatus Status, string? Revision, string? Detail);

/// <summary>
/// [D7][v1.16.0] Bir git çalışma kopyasını remote'una ilerletir — kod tabanındaki <b>TEK mutasyon yapan git
/// yüzeyi</b>. Harici köklerin build öncesi güncellemesi ve ana reponun <c>N behind</c> chip'inden tetiklenen
/// pull'u AYNI ilkeli buradan geçer.
///
/// <para><b>Ana repo kuralı (bilinçli güncelleme).</b> "Araç ana repoda pull yapmaz" kuralı duruyor: araç
/// <b>kendiliğinden asla</b> ilerletmez — ne Sync, ne Build, ne bir arka plan işi bu sınıfı ana repo köküyle
/// çağırır. Tek istisna kullanıcının ALT BARDAKİ chip'e basmasıdır ve o yol da yalnız aktif branch'te, yalnız
/// fast-forward ile çalışır. Gerekçe: fetch zaten "3 commit gerideyim" diyorsa kullanıcıyı terminale
/// göndermek aracın işini yarıda bırakmaktır; fast-forward ise aracın üstlenebileceği tek git yazma işlemidir
/// — tarih yeniden yazılmaz, birleştirme kararı verilmez, kirli ağaca dokunulmaz ve sonuç geri alınabilir.</para>
///
/// <para>Sınır yine bir kaynak guard'ıyla çitlenir: mutasyon yapan git komutları BU DOSYANIN dışına
/// çıkamaz (bkz. <c>NoGitMutationOutsideExternalsTests</c>).</para>
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
    /// <c>WorktreeManager</c>'ın mutasyon tavanıyla aynı 5 dakika.</summary>
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
            return new FastForwardResult(FastForwardStatus.DegradedOffline, head.Value, fetch.Warning);

        if (string.Equals(head.Value, fetch.TargetSha, StringComparison.Ordinal))
            return new FastForwardResult(FastForwardStatus.AlreadyCurrent, head.Value, null);

        // 4) Fast-forward mümkün mü? exit=0 → HEAD hedefin atası (ileri sarılabilir), exit=1 → ayrışmış.
        //    Karar exit kodundan okunur; stderr METNİ ASLA ayrıştırılmaz (lokalize olabilir).
        string trackingRef = $"refs/remotes/origin/{branch.Value}";
        var ancestry = await CommandLineTool.RunAsync(
            _runner, CommandLineTool.Git, _gitExecutable, ["merge-base", "--is-ancestor", "HEAD", trackingRef], _rootPath, QueryTimeout, ct);
        if (!ancestry.Success) return Failed(ancestry.Error);

        if (ancestry.Value!.ExitCode == 1)
            return new FastForwardResult(FastForwardStatus.Diverged, head.Value,
                $"the local branch '{branch.Value}' has diverged from origin — fast-forward is not possible");

        if (ancestry.Value.ExitCode != 0)
            return Failed(CommandLineTool.DescribeFailure(CommandLineTool.Git, ancestry.Value));

        // 5) Tek mutasyon: fast-forward. Merge commit'i üretmesi mümkün değildir (--ff-only).
        var merge = await CommandLineTool.RunAsync(
            _runner, CommandLineTool.Git, _gitExecutable, ["merge", "--ff-only", trackingRef], _rootPath, MergeTimeout, ct);
        if (!merge.Success) return Failed(merge.Error);
        if (merge.Value!.ExitCode != 0) return Failed(CommandLineTool.DescribeFailure(CommandLineTool.Git, merge.Value));

        var updatedHead = await _git.GetHeadCommitAsync(ct);
        if (!updatedHead.Success) return Failed(updatedHead.Error);

        return new FastForwardResult(FastForwardStatus.Updated, updatedHead.Value, null);
    }

    private static FastForwardResult Failed(string? detail)
        => new(FastForwardStatus.Failed, null, detail);
}
