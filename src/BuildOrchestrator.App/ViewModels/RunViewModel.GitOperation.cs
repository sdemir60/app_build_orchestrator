using System.IO;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Core.Git;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [Faz 2/T9 · spec 2026-09-18 §6.4 · karar 22] <see cref="RunViewModel"/>'in <b>yarıdaki git işlemi</b> yüzeyi: çalışma
/// ağacının git dizininde bir işlem işareti (<c>MERGE_HEAD</c>, <c>rebase-merge</c>/<c>rebase-apply</c>,
/// <c>CHERRY_PICK_HEAD</c>, <c>REVERT_HEAD</c>, <c>index.lock</c> — <see cref="GitOperationProbe"/>) durduğu sürece:
/// <list type="bullet">
/// <item>kendiliğinden Sync bekler (<see cref="WaitForGitOperation"/>; akışa işlem başına BİR satır);</item>
/// <item>checkout ve pull kilitlidir (<see cref="CanSwitchBranch"/>, <c>CanPullRepository</c>), chip'lerin tooltip'i
/// nedeni söyler (<see cref="GitOperationTooltip"/>) ve branch chip'inde amber nokta yanar;</item>
/// <item>Build engellenmez, planlamanın başına tek uyarı satırı düşer; Sync düğmesi çalışır ve ağacın yarım olduğunu
/// söyler;</item>
/// <item><c>index.lock</c> <see cref="GitOperationText.StuckLockSeconds"/> kesintisiz durursa konsola BİR kez takılı
/// kilit uyarısı düşer — araç kilidi SİLMEZ.</item>
/// </list>
///
/// <para><b>Yoklama yerleri</b> (<see cref="RefreshGitOperation"/>): kendiliğinden Sync'in her değerlendirmesi, pencereye
/// dönüş, kök değişimi, checkout/pull gönderiminden hemen önce (kapı yeniden sorulur), Build/Sync tıklaması ve
/// <see cref="GitOperationPollTimer"/>'ın tıkı. Zamanlayıcı YALNIZ işaret dururken çalışır (spec: "yalnız bu durumda
/// çalışan tek yoklama"); git dizini olmayan bir kökte işlem her zaman <see cref="GitOperation.None"/>'dır, yoklama
/// hiç kurulmaz.</para>
/// </summary>
public sealed partial class RunViewModel
{
    /// <summary>İşaret dururken yoklama aralığı (spec §6.4: "2 s'de bir") — TEK tanım.</summary>
    internal static readonly TimeSpan GitOperationPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>Çalışma ağacında yarıda duran git işlemi; yazıcısı yalnız <see cref="RefreshGitOperation"/>. Değişimi
    /// branch chip kapısını (<see cref="CanSwitchBranch"/>), pull komutunu ve tooltip'i duyurur.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwitchBranch))]
    [NotifyPropertyChangedFor(nameof(GitOperationTooltip))]
    [NotifyCanExecuteChangedFor(nameof(PullRepositoryCommand))]
    private GitOperation _gitOperation;

    /// <summary>Branch ve behind chip'lerinin git kilidi nedeni (<see cref="GitOperationText.Tooltip"/>); işlem yoksa
    /// <c>null</c>. Bar chip'lerin tooltip'ini buradan okur — metin kararı tek yerde.</summary>
    public string? GitOperationTooltip => GitOperationText.Tooltip(GitOperation);

    /// <summary>Test dikişi: kökten yarıdaki git işlemini okur. Varsayılan diskten (<see cref="GitDirectory.Resolve"/> +
    /// <see cref="GitOperationProbe.Inspect"/>); git dizini yoksa ya da okunamıyorsa <see cref="GitOperation.None"/>.</summary>
    internal Func<string, GitOperation> InspectGitOperation { get; set; } = InspectRootOnDisk;

    /// <summary>İşaret dururken tık atan zamanlayıcı — kabuk <see cref="DispatcherPollTimer"/> verir; <c>null</c> ise
    /// (çıplak VM testleri) yoklama yalnız olay anlarında olur. [fix round 1 · M2] Atama anında kök yoklanır: kabuk
    /// kayıtlı kökü zamanlayıcıdan ÖNCE uygular ve o anki yoklama zamanlayıcısız kalmıştır — tepsiden, merge yarıdayken
    /// açılışta da yoklama başlar. Eski zamanlayıcı (varsa) durdurulur.</summary>
    internal IPollTimer? GitOperationPollTimer
    {
        get => _gitOperationPollTimer;
        set
        {
            _gitOperationPollTimer?.Stop();
            _gitOperationPollTimer = value;
            RefreshGitOperation();
        }
    }

    private IPollTimer? _gitOperationPollTimer;

    /// <summary>Kesintisiz <see cref="GitOperation.CommandRunning"/>'in ilk görüldüğü an (enjekte saat, ms); başka bir
    /// durumda <c>null</c>.</summary>
    private long? _gitLockSinceMs;

    /// <summary>Bu kilit bölümünde takılı kilit satırı yazıldı mı — bölüm başına BİR kez.</summary>
    private bool _stuckLockReported;

    /// <summary>Bu işlem bölümünde "waiting for git" satırı yazıldı mı — işaret kalkınca sıfırlanır.</summary>
    private bool _gitWaitAnnounced;

    /// <summary>Kökü yoklar, <see cref="GitOperation"/>'ı günceller ve sonucu döner. Takılı kilit ölçümü ve
    /// zamanlayıcının açılıp kapanması buradan sürülür — her yoklama aynı yoldan geçer.</summary>
    internal GitOperation RefreshGitOperation()
    {
        var operation = RootPath.Length == 0 ? GitOperation.None : InspectGitOperation(RootPath);
        TrackStuckLock(operation);
        GitOperation = operation;
        if (operation == GitOperation.None) GitOperationPollTimer?.Stop();
        else GitOperationPollTimer?.Start(GitOperationPollInterval, () => RefreshGitOperation());
        return operation;
    }

    /// <summary>İşaret kalktı: bekleme satırı hakkı yenilenir ve bekleyen kendiliğinden Sync tetiği meşguliyet
    /// bildiriminin TEK noktasından (<see cref="NotifyAutoSyncGate"/>) yeniden sorulur — normal yol.</summary>
    partial void OnGitOperationChanged(GitOperation value)
    {
        if (value != GitOperation.None) return;
        _gitWaitAnnounced = false;
        NotifyAutoSyncGate();
    }

    /// <summary><c>index.lock</c> <see cref="GitOperationText.StuckLockSeconds"/> kesintisiz durdu mu — durduysa konsola
    /// BİR kez <see cref="GitOperationText.StuckLock"/>. Kilide dokunulmaz: git'in çalışıp çalışmadığını araç bilemez.</summary>
    private void TrackStuckLock(GitOperation operation)
    {
        if (operation != GitOperation.CommandRunning)
        {
            _gitLockSinceMs = null;
            _stuckLockReported = false;
            return;
        }
        long now = _nowMs();
        _gitLockSinceMs ??= now;
        if (_stuckLockReported || now - _gitLockSinceMs.Value < GitOperationText.StuckLockSeconds * 1000L) return;
        _stuckLockReported = true;
        AppendRunLine(GitOperationText.StuckLock);
    }

    /// <summary>Kendiliğinden Sync'in git kapısı (<see cref="IAutoSyncPort.WaitForGitOperation"/>): yarıda bir işlem varsa
    /// <c>true</c> ve akışa işlem bölümü başına BİR kez <see cref="StreamText.WaitingForGit"/>.</summary>
    private bool WaitForGitOperation()
    {
        var operation = RefreshGitOperation();
        if (operation == GitOperation.None) return false;
        if (!_gitWaitAnnounced)
        {
            _gitWaitAnnounced = true;
            PushStream(StreamKind.Info, null, StreamText.WaitingForGit(operation));
        }
        return true;
    }

    /// <summary>Sync düğmesinin temizlikten sonraki ilk satırları: ağaç yarıdaysa tek satır
    /// (<see cref="GitOperationText.SyncWarning"/>), değilse hiç.</summary>
    private IReadOnlyList<string> MidOperationSyncLines() =>
        GitOperationText.SyncWarning(RefreshGitOperation()) is { } warning ? [warning] : [];

    private static GitOperation InspectRootOnDisk(string root)
    {
        try
        {
            return GitDirectory.Resolve(root) is { } gitDir ? GitOperationProbe.Inspect(gitDir) : GitOperation.None;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return GitOperation.None; }
    }
}
