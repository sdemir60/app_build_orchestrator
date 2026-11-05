using BuildOrchestrator.Core.Git;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [worktree] <see cref="RunViewModel"/>'in <b>ikinci kök</b> yüzeyi: Settings'te tanımlı tek bir linked worktree ve alt bardan
/// Repo ↔ Worktree geçişi.
///
/// <para><b>İki kök:</b> <see cref="RootPath"/> Settings'teki repo köküdür — Settings onu gösterir, kalıcı durumun
/// <c>RepositoryRoot</c>'u odur ve worktree ondan eklenir; worktree aktifken de değişmez. <see cref="ActiveRoot"/> uygulamanın
/// çalıştığı köktür: motora giden her komut (Sync, koşu, branch envanteri, Clean, Optimize, checkout, pull), HEAD izleyicisi ve
/// git işlem kapısı onu taşır (kaynak guard'ı <c>WorktreeRootGuardTests</c>). Worktree bir mod değildir: motor hangi kökte
/// çalıştığını bilmez, defter ve önbellekler tam yola göre anahtarlıdır, iki kopya birbirine karışmaz.</para>
///
/// <para><b>Geçiş bir kök değişimidir</b> (<see cref="SwitchCopyAsync"/>): eski kopyanın satırları, topolojisi ve son Sync'i gider
/// (proje kimlikleri yola bağlıdır — worktree'nin projeleri başka satırlardır), konsol yeni bir bölüm açar ve yeni kökte
/// <see cref="SyncMode.BranchChange"/> Sync'i koşar (fetch'siz: <c>.git</c> ortaktır, uzak uçlar zaten bilinir).</para>
/// </summary>
public sealed partial class RunViewModel
{
    /// <summary>Settings → Workspace → <c>Worktree folder</c>; boş = worktree tanımsız. Yazıcıları açılış seed'i (MainWindow) ve
    /// Settings Save'dir (<see cref="ApplySettingsAsync"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveRoot))]
    private string _worktreeFolder = "";

    /// <summary>Uygulamanın çalıştığı kopya. Yazıcıları geçiş (<see cref="SwitchCopyAsync"/>), açılış seed'i ve kopyayı repo'ya
    /// döndüren kök değişimleridir (<see cref="ReturnToRepositoryCopy"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveRoot))]
    private WorkingCopy _activeCopy = WorkingCopy.Repository;

    /// <summary>Motora giden her komutun ve git okumasının kökü: worktree aktifse worktree klasörü, değilse repo kökü.</summary>
    public string ActiveRoot => ActiveCopy == WorkingCopy.Worktree && WorktreeFolder.Length > 0 ? WorktreeFolder : RootPath;

    /// <summary>Alt bardaki anahtarın kapısı — Debug|Release segment'iyle aynı soru (<see cref="CanSwitchConfiguration"/>): geçiş kendi
    /// Sync'ini başlatır, bu yüzden koşu, Sync ya da başka bir workspace işi uçuştayken ve motor erişilemezken kapalıdır. Bildirimi
    /// Sync komutununkidir (<c>SyncCommand.CanExecuteChanged</c>).</summary>
    public bool CanSwitchCopy => HasWorkspace && CanSync();

    /// <summary>Worktree'ye geçiş istendi ama geçilecek bir worktree yok (tanımsız, diskte değil ya da bu reponun değil): kabuk
    /// Settings'i Worktree folder alanında açar.</summary>
    public event EventHandler? WorktreeSetupRequested;

    /// <summary>Ayardaki worktree diskte bu reponun linked worktree'si mi — geçişin ve açılışta worktree'de açılmanın TEK sorusu
    /// (<see cref="Core.Git.WorktreeFolder.Inspect"/>; saf dosya okuması).</summary>
    internal bool WorktreeReady => HasWorkspace && WorktreeFolder.Length > 0
        && Core.Git.WorktreeFolder.Inspect(RootPath, WorktreeFolder) == WorktreeFolderState.Linked;

    /// <summary>
    /// Alt bardaki anahtar: <paramref name="copy"/> kopyasına geçer. Aynı kopyaya geçiş ve kapı kapalıyken istek hiçbir şey yapmaz.
    /// Worktree'ye geçilecek bir worktree yoksa komut gitmez, <see cref="WorktreeSetupRequested"/> yükselir.
    /// <para>Sıra: kopya değişir (izleyici ve git kapısı yeni köke taşınır — <see cref="OnActiveCopyChanged"/>), son Sync ve plan
    /// yüzeyi unutulur (<see cref="ClearPlanSurface"/>: eski kopyanın satırları yeni kopyada hiçbir projeye karşılık gelmez), sonra
    /// <see cref="SyncMode.BranchChange"/> Sync'i konsolu temizleyip bölümün ilk satırını (<see cref="WorkingCopyText.Switched"/>)
    /// yazar ve yeni kökte koşar.</para>
    /// </summary>
    public async Task SwitchCopyAsync(WorkingCopy copy)
    {
        if (copy == ActiveCopy || !CanSwitchCopy) return;
        if (copy == WorkingCopy.Worktree && !WorktreeReady)
        {
            WorktreeSetupRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        ActiveCopy = copy;
        ForgetLastSync(); // eski kopyanın HEAD'i yeni kopyada kıyas tabanı olamaz
        ClearPlanSurface();
        await SyncCoreAsync(SyncMode.BranchChange, sectionLines: [WorkingCopyText.Switched(copy, ActiveRoot)]);
    }

    /// <summary>Aktif kök değişti: HEAD izleyicisi ve git işlem kapısı yeni köke taşınır (<c>OnRootPathChanged</c>'in ikizi).</summary>
    partial void OnActiveCopyChanged(WorkingCopy value)
    {
        AttachAutoSync(ActiveRoot);
        RefreshGitOperation();
    }

    /// <summary>Kopyayı Sync'siz repo'ya döndürür — repo kökünün değişmesi (worktree eski reponundur), workspace'in kapanması ve
    /// worktree klasörünün değişmesi. Çağıran kendi Sync'ini (ya da hiçbirini) gönderir.</summary>
    private void ReturnToRepositoryCopy()
    {
        if (ActiveCopy != WorkingCopy.Repository) ActiveCopy = WorkingCopy.Repository;
    }
}
