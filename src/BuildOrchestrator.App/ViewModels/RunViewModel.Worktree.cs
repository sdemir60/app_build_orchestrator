using BuildOrchestrator.Contracts.Ipc;
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
///
/// <para><b>Kurulum Settings Save'dedir</b> (<see cref="ApplySettingsAsync"/>): kaydedilen klasör yoksa ya da boşsa — ve alan bu Save'de
/// değiştiyse ya da Settings alt bardaki düğmeden açıldıysa — motor onu ana kopyanın HEAD'inde detached kurar (<see cref="CreateWorktreeCommand"/>).
/// Kurulum bir workspace işidir (<see cref="WorktreeBusy"/>: düğmeler pasif, işlem pill'i, konsol satırı) ve Save'in tek Sync'i kurulumun
/// cevabını bekler; Settings alt bardaki düğmeden açıldıysa cevap başarılıysa worktree'ye geçilir.</para>
/// </summary>
public sealed partial class RunViewModel
{
    /// <summary>Settings → Workspace → <c>Worktree folder</c>; boş = worktree tanımsız. Yazıcıları açılış seed'i (MainWindow) ve
    /// Settings Save'dir (<see cref="ApplyWorktreeFolder"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveRoot))]
    private string _worktreeFolder = "";

    /// <summary>Uygulamanın çalıştığı kopya. Yazıcıları geçiş (<see cref="SwitchCopyCoreAsync"/>), açılış seed'i ve kopyayı repo'ya
    /// döndüren kök değişimleridir (<see cref="ReturnToRepositoryCopy"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveRoot))]
    private WorkingCopy _activeCopy = WorkingCopy.Repository;

    /// <summary>Motora giden her komutun ve git okumasının kökü: worktree aktifse worktree klasörü, değilse repo kökü.</summary>
    public string ActiveRoot => ActiveCopy == WorkingCopy.Worktree && WorktreeFolder.Length > 0 ? WorktreeFolder : RootPath;

    /// <summary>Alt bardaki anahtarın kapısı — Debug|Release segment'inin kapısının KENDİSİ (<see cref="CanSwitchConfiguration"/>): geçiş
    /// kendi Sync'ini başlatır, bu yüzden koşu, Sync ya da başka bir workspace işi uçuştayken ve motor erişilemezken kapalıdır. Bildirimi
    /// Sync komutununkidir (<c>SyncCommand.CanExecuteChanged</c>).</summary>
    public bool CanSwitchCopy => CanSwitchConfiguration;

    /// <summary>Worktree'ye geçiş istendi ama geçilecek bir worktree yok (tanımsız, diskte değil ya da bu reponun değil): kabuk
    /// Settings'i Worktree folder alanında açar.</summary>
    public event EventHandler? WorktreeSetupRequested;

    /// <summary>Ayardaki worktree diskte bu reponun linked worktree'si mi — geçişin ve açılışta worktree'de açılmanın TEK sorusu
    /// (<see cref="Core.Git.WorktreeFolder.Inspect"/>; saf dosya okuması).</summary>
    internal bool WorktreeReady => WorktreeState == WorktreeFolderState.Linked;

    /// <summary>Ayardaki worktree klasörü kurulmayı mı bekliyor (yok ya da boş) — Settings Save'in kurulum sorusu.</summary>
    private bool WorktreeNeedsCreating => WorktreeState == WorktreeFolderState.Creatable;

    /// <summary>Ayardaki klasörün durumu; worktree ya da workspace tanımsızsa <c>null</c>.</summary>
    private WorktreeFolderState? WorktreeState =>
        HasWorkspace && WorktreeFolder.Length > 0 ? InspectWorktreeFolder(RootPath, WorktreeFolder) : null;

    /// <summary>[test dikişi] Klasör denetimi — üretimde <see cref="Core.Git.WorktreeFolder.Inspect"/> (diskten). Diski olmayan sahte
    /// köklü kabuk testleri (motor uyandırma tablosu) kurulum yolunu bununla sürer.</summary>
    internal Func<string, string, WorktreeFolderState> InspectWorktreeFolder { get; set; } = Core.Git.WorktreeFolder.Inspect;

    /// <summary>
    /// Alt bardaki anahtar: <paramref name="copy"/> kopyasına geçer. Aynı kopyaya geçiş ve kapı kapalıyken istek hiçbir şey yapmaz.
    /// Worktree'ye geçilecek bir worktree yoksa komut gitmez, <see cref="WorktreeSetupRequested"/> yükselir.
    /// </summary>
    public async Task SwitchCopyAsync(WorkingCopy copy)
    {
        if (copy == ActiveCopy || !CanSwitchCopy) return;
        if (copy == WorkingCopy.Worktree && !WorktreeReady)
        {
            WorktreeSetupRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        await SwitchCopyCoreAsync(copy);
    }

    /// <summary>Geçişin TEK gövdesi (kapısız — çağıran sorar): kopya değişir (son Sync, git yüzeyi ve kart unutulur, izleyici ve git kapısı
    /// yeni köke taşınır — <see cref="OnActiveCopyChanged"/>), plan yüzeyi boşalır (<see cref="ClearPlanSurface"/>: eski kopyanın satırları
    /// yeni kopyada hiçbir projeye karşılık gelmez), sonra <see cref="SyncMode.BranchChange"/> Sync'i konsolu temizleyip bölümü
    /// yazar ve yeni kökte koşar.</summary>
    /// <param name="leading">Bölümde geçiş satırından ÖNCE gelen satırlar (kurulumun sonucu) — geçişin konsol temizliği onları silmez.</param>
    /// <param name="trailing">Geçiş satırından SONRA gelen satırlar (o kopyada yapılan checkout'un sonucu).</param>
    /// <param name="release">Önceki işin yüzeyini bırakan adım: Sync kapıyı DEVRALDIKTAN sonra çağrılır (<see cref="SyncThenReleaseAsync"/>
    /// deseni) — arada düğmeler bir kare bile açılmaz.</param>
    private async Task SwitchCopyCoreAsync(WorkingCopy copy, IReadOnlyList<string>? leading = null, IReadOnlyList<string>? trailing = null,
        Action? release = null)
    {
        ActiveCopy = copy;
        ClearPlanSurface();
        var sync = SyncCoreAsync(SyncMode.BranchChange,
            sectionLines: [.. leading ?? [], WorkingCopyText.Switched(copy, ActiveRoot), .. trailing ?? []]);
        release?.Invoke();
        await sync;
    }

    /// <summary>Aktif kopya değişti — kopyayı değiştiren HER yolun (alt bardaki geçiş, kurulumdan sonra geçiş, diğer kopyada checkout,
    /// klasör ya da kök değişiminde ve workspace kapanırken repo'ya dönüş) yan etkileri TEK yerde. Eski kopyanın son Sync'i unutulur
    /// (HEAD'i yeni kopyada kıyas tabanı olsaydı ilk HEAD tetiği sahte bir "Switched to" bölümü açardı) ve git yüzeyi de (branch envanteri,
    /// checkout edilmiş branch, <c>N behind</c>): detached bir worktree'de repo'nun branch'i chip'te kalır ve Sync onu yedek branch diye
    /// taşırdı. Ardından kök değişiminin ortak adımları (<see cref="OnActiveRootChanged"/>).</summary>
    partial void OnActiveCopyChanged(WorkingCopy value)
    {
        ForgetLastSync();
        ForgetGitSurface();
        OnActiveRootChanged();
    }

    /// <summary>Aktif kök değişti — repo kökü (<c>OnRootPathChanged</c>) ya da kopya (<see cref="OnActiveCopyChanged"/>): HEAD izleyicisi
    /// ve git işlem kapısı yeni köke taşınır, açık branch kartı kapanır (konusu eski kökün kirli ağacıydı; açık kalsaydı kararı yeni köke
    /// uygulanırdı).</summary>
    private void OnActiveRootChanged()
    {
        AttachAutoSync(ActiveRoot);
        RefreshGitOperation();
        SetPendingBranchSwitch(null);
    }

    /// <summary>Kopyayı Sync'siz repo'ya döndürür — repo kökünün değişmesi (worktree eski reponundur), workspace'in kapanması ve
    /// worktree klasörünün değişmesi. Çağıran kendi Sync'ini (ya da hiçbirini) gönderir.</summary>
    private void ReturnToRepositoryCopy()
    {
        if (ActiveCopy != WorkingCopy.Repository) ActiveCopy = WorkingCopy.Repository;
    }

    // ---------------------------------------------------------------- Settings Save

    /// <summary>Settings'ten gelen klasör GERÇEKTEN farklı mı (<see cref="SamePath"/>) — Save'in uygulama kapısı ve mid-run erteleme notunun
    /// koşulu aynı soruyu sorar.</summary>
    private bool IsWorktreeChange(string folder) => !SamePath(folder, WorktreeFolder);

    /// <summary>Settings Save: yeni worktree klasörünü uygular ve konsola not düşer. Aktif kopya worktree idiyse artık o değildir:
    /// kopya repo'ya döner ve <c>true</c> döner — çağıranın Sync'i plan yüzeyini boşaltır (eski kopyanın satırları).</summary>
    private bool ApplyWorktreeFolder(string folder)
    {
        if (!IsWorktreeChange(folder)) return false;
        bool wasActive = ActiveCopy == WorkingCopy.Worktree;
        ReturnToRepositoryCopy();
        WorktreeFolder = folder;
        AppendSaveNote(WorkingCopyText.FolderChanged(folder));
        if (wasActive) AppendSaveNote(WorkingCopyText.Switched(WorkingCopy.Repository, RootPath));
        return wasActive;
    }

    /// <summary>Uçuştaki Settings Save'in notları (katman, harici liste, General anahtarları, kök, worktree klasörü); Save'in senkron gövdesi
    /// dışında <c>null</c>. Yazıcısı <see cref="AppendSaveNote"/>.</summary>
    private List<string>? _saveNotes;

    /// <summary>Settings Save'in konsol notu: konsola yazılır ve Save'in not listesine eklenir. Save worktree'ye geçerse geçişin bölümü konsolu
    /// temizler; liste bölümün ilk satırları olur (<see cref="SwitchCopyCoreAsync"/>'in <c>leading</c>'i) ve notlar kaybolmaz — aynı Save geçiş
    /// yapmasaydı konsolda kalacaklardı.</summary>
    private void AppendSaveNote(string line)
    {
        AppendRunLine(line);
        _saveNotes?.Add(line);
    }

    /// <summary>[worktree] Settings Save'in kurulumu uçuşta: istekten cevaba (<see cref="WorktreeCreatedEvent"/> ya da
    /// <see cref="WorktreeErrorCodes"/>'tan bir hata) kadar. Motor kurulum boyunca komut döngüsünü bloklar ve başlangıç olayı yayınlamaz,
    /// bu yüzden istek ve uçuş penceresi TEK bayraktır (<see cref="CheckoutBusy"/> deseni).</summary>
    public bool WorktreeBusy { get; private set; }

    /// <summary>Uçuştaki kurulumun Save'den taşıdıkları: plan yüzeyi boşalmalı mı (kök ya da kopya değişti), başarıda worktree'ye geçilsin
    /// mi ve Save'in notları (geçişin bölümü onlarla başlar).</summary>
    private sealed record PendingWorktree(bool PlanGone, bool SwitchAfter, IReadOnlyList<string> Notes);

    private PendingWorktree? _pendingWorktree;

    /// <summary><see cref="WorktreeBusy"/>'nin TEK yazıcısı: değer değiştiğinde meşguliyete bağlı her kapı duyurulur.</summary>
    private void SetWorktreeBusy(bool busy)
    {
        if (WorktreeBusy == busy) return;
        WorktreeBusy = busy;
        OnPropertyChanged(nameof(WorktreeBusy));
        NotifySyncGatedCommands();
    }

    /// <summary>Kurulum isteği: konsol satırı, işlem pill'i, meşguliyet ve komut. Gönderim düştüyse (motor hazır değil) cevap
    /// gelmeyecektir — kilit hemen açılır.</summary>
    private async Task RequestWorktreeAsync(bool planGone, bool switchAfter, IReadOnlyList<string> notes)
    {
        _pendingWorktree = new PendingWorktree(planGone, switchAfter, notes);
        AppendRunLine(WorkingCopyText.Creating(WorktreeFolder));
        CurrentOperation = OperationLabel.CreatingWorktree;
        SetWorktreeBusy(true); // kapı GÖNDERİMDEN önce kapanır (checkout'un deseni)
        ArmEngineWatchdog();
        if (!await TrySendAsync(new CreateWorktreeCommand(RootPath, WorktreeFolder), "createWorktree"))
        {
            _pendingWorktree = null;
            SetWorktreeBusy(false);
            CurrentOperation = null;
        }
    }

    /// <summary>Kurulumun cevabı: sonuç satırı, sonra Save'in Sync'i — başarıda ve Settings alt bardaki düğmeden açıldıysa worktree'ye
    /// geçişin Sync'i (bölüm Save'in notları ve sonuç satırıyla başlar). Kilit, Sync kapıyı devraldıktan SONRA bırakılır
    /// (<see cref="SyncThenReleaseAsync"/>). Bekleyen bir istek yoksa (motor yeniden başladı, cevap sahipsiz) yalnız satır yazılır ve kilit
    /// açılır.</summary>
    private async Task OnWorktreeCreatedAsync(WorktreeCreatedEvent e)
    {
        var pending = _pendingWorktree;
        _pendingWorktree = null;
        CurrentOperation = null;
        string result = e.Created
            ? WorkingCopyText.Created(e.Folder, ShortSha(e.Revision))
            : WorkingCopyText.NotCreated(e.Detail ?? "unknown error");

        if (pending is null) { AppendRunLine(result); ReleaseWorktree(); return; }
        if (e.Created && pending.SwitchAfter && WorktreeReady)
        {
            await SwitchCopyCoreAsync(WorkingCopy.Worktree, leading: [.. pending.Notes, result], release: ReleaseWorktree);
            return;
        }
        AppendRunLine(result);
        await FinishSaveAsync(pending);
    }

    /// <summary>Kurulum geçişsiz bitti (başarısız, ya da Settings'ten kuruldu): Save'in ertelenen Sync'i gider — kök ya da kopya değiştiyse
    /// plan yüzeyi önce boşalır. Kilit, Sync kapıyı devraldıktan SONRA bırakılır.</summary>
    private async Task FinishSaveAsync(PendingWorktree pending)
    {
        if (pending.PlanGone) ClearPlanSurface();
        await SyncThenReleaseAsync(ReleaseWorktree, SyncMode.Appended);
    }

    /// <summary>Kurulum kilidini bırakır — <see cref="SyncThenReleaseAsync"/>'e verilen adım.</summary>
    private void ReleaseWorktree() => SetWorktreeBusy(false);

    /// <summary>Dönüş değeri = "bu hata kuruluma aittir, run state'ine DOKUNMA" (<see cref="TryConsumeCheckoutFailure"/>'ın ikizi); hata satırı
    /// <c>OnError</c>'ın ilk satırıdır, pill düşer. Motorun reddi (<c>worktreeRejected</c> — koşu uçuşta) kilidi açar ve Sync göndermez: motor
    /// meşgul. Beklenmeyen hata (<c>worktreeFailed</c>) başarısız bir kurulumdur: Save'in ertelenen Sync'i yine gider (<see cref="FinishSaveAsync"/>).</summary>
    private bool TryConsumeWorktreeFailure(string code)
    {
        if (!WorktreeErrorCodes.Contains(code) || !WorktreeBusy) return false;
        var pending = _pendingWorktree;
        _pendingWorktree = null;
        CurrentOperation = null;
        if (code == WorktreeFailedCode && pending is not null) _ = FinishSaveAsync(pending);
        else ReleaseWorktree();
        return true;
    }

    /// <summary>Kurulumun beklenmeyen hatasının kodu.</summary>
    private const string WorktreeFailedCode = "worktreeFailed";

    /// <summary>Kurulumun yayınlayabildiği hata kodları: <c>worktreeRejected</c> (koşu uçuşta) ve <see cref="WorktreeFailedCode"/> (beklenmeyen
    /// hata). Run-bitiren kodlarla KESİŞMEZ.</summary>
    private static readonly HashSet<string> WorktreeErrorCodes = new(StringComparer.Ordinal) { WorktreeFailedCode, "worktreeRejected" };

    /// <summary>Motor kurulumun ortasında öldü: cevap gelmeyecek — kilit ve bekleyen istek bırakılır
    /// (<see cref="RunViewModel.ReleaseAfterEngineLoss"/>).</summary>
    private void ReleaseWorktreeAfterLoss()
    {
        _pendingWorktree = null;
        SetWorktreeBusy(false);
    }
}
