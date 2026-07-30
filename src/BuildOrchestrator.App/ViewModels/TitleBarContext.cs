namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [A13/T2 · 2.1] Title bar'ın mono bağlam metninin TEK kaynağı — SAF, WPF'siz test edilir
/// (<see cref="InteractionText"/>/<see cref="RibbonText"/> deseni: karar burada, uygulama <c>MainWindow</c>'da).
///
/// <para><b>Otorite — design-v1 §2.1 (birebir):</b> <i>"…ardından mono 11px <c>text-dim</c> bağlam:
/// <c>OSYS · main</c> — worktree aktifse <c>· main-2</c> eklenir (<c>text-faint</c>). Repo yokken:
/// <c>no repository</c>."</i> Prototipte repo adı sabit bir <c>'OSYS'</c> literalidir
/// (<c>BuildApp.jsx:1438</c>); gerçek karşılığı repo KÖKÜNÜN KLASÖR ADIDIR — bu kavram T2'den önce üretimde
/// hiç hesaplanmıyordu.</para>
///
/// <para><b>Neden iki parça:</b> gövde ve worktree eki FARKLI tonlardadır (text-dim / text-faint), tek bir
/// <c>TextBlock.Text</c> literaliyle verilemezler. Prototip de onları iki ayrı <c>span</c> olarak yazar
/// (<c>BuildApp.jsx:1437</c> gövde, <c>:1440-1443</c> ek). Metinlerin İKİSİ de buradan üretilir.</para>
/// </summary>
public static class TitleBarContext
{
    /// <summary>Repo seçilmemişken gösterilen bağlam — verbatim (BuildApp.jsx:1438).</summary>
    public const string NoRepository = "no repository";

    /// <summary>Gövde: repo yoksa <see cref="NoRepository"/>; varsa <c>{repo} · {branch}</c>.
    /// <para>Branch HENÜZ bilinmiyorsa (<c>Branch</c> envanter/UiState gelmeden boştur — bkz. madde 2.2) yalnız
    /// repo adı döner: sallantıda bir <c>" · "</c> ayracı bırakmak design-v1'in "sakin, kesin" tonuna aykırıdır
    /// ve kullanıcıya bir şey ANLATMAZ.</para></summary>
    public static string Compose(string rootPath, string branch)
    {
        string repo = RepositoryName(rootPath);
        if (repo.Length == 0) return NoRepository;
        return string.IsNullOrEmpty(branch) ? repo : $"{repo} · {branch}";
    }

    /// <summary>Worktree eki (<c>· main-2</c>) — yalnız repo VARKEN ve worktree AÇIKKEN. Boş dize = ek yok.
    /// <para>Kaynak, action bar'ın worktree chip'iyle AYNIdır (<c>ActionBar.RefreshBranchWorktree</c>:
    /// <c>UseWorktree</c> + <c>EffectiveWorktreeName</c>) — ikinci bir "worktree aktif mi" kavramı İCAT EDİLMEZ,
    /// aksi halde chip "off" derken başlıkta bir worktree adı durabilirdi.</para></summary>
    public static string WorktreeSuffix(string rootPath, bool useWorktree, string? worktreeName)
    {
        if (RepositoryName(rootPath).Length == 0 || !useWorktree) return "";
        return string.IsNullOrEmpty(worktreeName) ? "" : $"· {worktreeName}";
    }

    /// <summary>Repo kökünün klasör adı. Disk'e DOKUNMAZ (saf dize işlemi): <c>DirectoryInfo</c> geçersiz
    /// karakterlerde fırlatırdı ve bu karar bir görüntüleme kararıdır. Sondaki ayraç(lar) yok sayılır
    /// (<c>D:\Projects\OSYS\</c> → <c>OSYS</c>); ayraç hiç yoksa (sürücü kökü / göreli ad) dizenin kendisi döner.</summary>
    public static string RepositoryName(string rootPath)
    {
        if (string.IsNullOrEmpty(rootPath)) return "";
        string trimmed = rootPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0) return "";
        int slash = trimmed.LastIndexOfAny([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar]);
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }
}
