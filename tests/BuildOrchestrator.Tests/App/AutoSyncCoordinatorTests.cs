using System.IO;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Faz 2/T7 · spec 2026-09-18 §6.1 · §6.2 · karar 11] <see cref="AutoSyncCoordinator"/>'ın kararı — saf, sahte
/// VM portuyla: çift Sync yok, branch değişimi yeni bölüm açar, commit sessiz Sync'ler, meşgulken gelen tetik
/// meşguliyet bitince BİR kez değerlendirilir, pencereye dönüş 5 s eşiğine uyar.
/// </summary>
public sealed class AutoSyncCoordinatorTests
{
    private const string ShaA = "1111111111111111111111111111111111111111";
    private const string ShaB = "2222222222222222222222222222222222222222";

    private sealed class FakePort : IAutoSyncPort
    {
        private bool _busy;

        public bool HasWorkspace { get; set; } = true;

        /// <summary>VM'deki gibi: workspace işi YA DA koşu uçuşta.</summary>
        public bool IsWorkspaceBusy { get => _busy || IsRunInFlight; set => _busy = value; }

        public bool IsRunInFlight { get; set; }
        public (string? Branch, string? HeadSha)? LastSyncHead { get; set; }
        public long? LastSyncAtMs { get; set; }
        public long Now { get; set; } = 100_000;
        public List<SilentSyncReason> Silent { get; } = [];

        /// <summary>Her BranchChange Sync'inin bölüm satırları, satır sonuyla birleştirilmiş.</summary>
        public List<string> BranchChanges { get; } = [];
        public List<string> ConsoleLines { get; } = [];
        public List<string> StreamLines { get; } = [];
        public int Interrupts { get; private set; }

        /// <summary>Kesilen koşunun özeti — VM'de kesme isteyen koşu bitince hazır olur; bir kez alınır.</summary>
        public string? Summary { get; set; }

        public long NowMs() => Now;

        /// <summary>[final review I1] VM'in Sync'i gönderip gönderemediği — <c>false</c> iken hiçbir Sync kaydedilmez ve
        /// port <c>false</c> döner (kapı kapalı ya da gönderim düştü).</summary>
        public bool Accepts { get; set; } = true;

        public Task<bool> SyncSilentlyAsync(SilentSyncReason reason)
        {
            if (!Accepts) return Task.FromResult(false);
            Silent.Add(reason);
            return Task.FromResult(true);
        }

        public Task<bool> SyncAfterExternalBranchChangeAsync(IReadOnlyList<string> sectionLines)
        {
            if (!Accepts) return Task.FromResult(false);
            BranchChanges.Add(string.Join("\n", sectionLines));
            return Task.FromResult(true);
        }

        public Task RequestInterruptAsync()
        {
            Interrupts++;
            return Task.CompletedTask;
        }

        public string? TakeInterruptedRunSummary()
        {
            string? summary = Summary;
            Summary = null;
            return summary;
        }

        public void AppendConsoleLine(string line) => ConsoleLines.Add(line);

        public void AppendStreamLine(string line) => StreamLines.Add(line);

        /// <summary>[T9] Yarıda bir git işlemi var mı — koordinatör testlerinde yok (VM testleri pinler).</summary>
        public bool WaitForGitOperation() => false;

        public int SyncCount => Silent.Count + BranchChanges.Count;

        /// <summary>Bir Sync tamamlandı: HEAD ve an kaydedilir (VM'in <c>OnSyncCompleted</c>'ının yaptığı).</summary>
        public void Synced(string? branch, string? sha)
        {
            LastSyncHead = (branch, sha);
            LastSyncAtMs = Now;
        }
    }

    /// <summary>Koordinatör + portu; HEAD değişkeni testin elinde, UI taşıması senkron.</summary>
    private sealed class Harness
    {
        public FakePort Port { get; } = new();
        public HeadState? Head { get; set; }
        public AutoSyncCoordinator Coordinator { get; }

        public Harness(Func<IHeadWatcher>? newWatcher = null) =>
            Coordinator = new AutoSyncCoordinator(Port, action => action(), _ => Head, newWatcher);

        /// <summary>Son Sync main@A'da bitti, HEAD hâlâ orada, üstünden eşik kadar zaman geçti.</summary>
        public static Harness SyncedOnMain(Func<IHeadWatcher>? newWatcher = null)
        {
            var h = new Harness(newWatcher) { Head = new HeadState("main", ShaA) };
            h.Port.Synced("main", ShaA);
            h.Port.Now += AutoSyncCoordinator.ActivationQuietMs + 1;
            return h;
        }
    }

    [Fact]
    public async Task An_unchanged_head_runs_no_sync()
    {
        var h = Harness.SyncedOnMain();

        await h.Coordinator.HeadTriggerAsync(HeadMove.Other);
        await h.Coordinator.HeadTriggerAsync(HeadMove.Commit);

        Assert.Equal(0, h.Port.SyncCount);
    }

    /// <summary>Dışarıdan branch değişimi yeni bölüm açar; ilk satır yeni branch + kısa sha + nereden gelindiği.</summary>
    [Fact]
    public async Task A_branch_switch_opens_a_new_section()
    {
        var h = Harness.SyncedOnMain();
        h.Head = new HeadState("feature", ShaB);

        await h.Coordinator.HeadTriggerAsync(HeadMove.BranchSwitch);

        Assert.Equal([PlanProgressLines.SwitchedBranch("main", "feature", RevisionText.Short(ShaB))], h.Port.BranchChanges);
        Assert.Empty(h.Port.Silent);
    }

    [Fact]
    public async Task A_commit_syncs_silently_with_its_line()
    {
        var h = Harness.SyncedOnMain();
        h.Head = new HeadState("main", ShaB);

        await h.Coordinator.HeadTriggerAsync(HeadMove.Commit);

        Assert.Equal([SilentSyncReason.Commit], h.Port.Silent);
        Assert.Empty(h.Port.BranchChanges);
    }

    /// <summary>Sync uçuştayken gelen tetikler tek bekleyen tetikte birleşir ve Sync bitince BİR kez değerlendirilir
    /// (Sync commit'ten önce başlamıştı: ölçtüğü HEAD eski — commit'in Sync'i hâlâ gerekli).</summary>
    [Fact]
    public async Task A_trigger_during_a_sync_runs_once_after_it()
    {
        var h = Harness.SyncedOnMain();
        h.Port.IsWorkspaceBusy = true;
        h.Head = new HeadState("main", ShaB);

        await h.Coordinator.HeadTriggerAsync(HeadMove.Commit);
        await h.Coordinator.HeadTriggerAsync(HeadMove.Commit);
        Assert.Equal(0, h.Port.SyncCount);

        h.Port.Synced("main", ShaA); // uçuştaki Sync commit'ten önceki HEAD'i ölçtü
        h.Port.Now += AutoSyncCoordinator.ActivationQuietMs + 1;
        h.Port.IsWorkspaceBusy = false;
        h.Coordinator.OnWorkspaceIdle();
        h.Coordinator.OnWorkspaceIdle();

        Assert.Equal([SilentSyncReason.Commit], h.Port.Silent);
    }

    /// <summary>[final review I1] Gönderilemeyen Sync (kapı kapalı ya da gönderim düştü) tetiği KAYBETMEZ: tetik bekler
    /// ve bir sonraki meşguliyet bitişinde (ör. motor yeniden hazır) yeniden değerlendirilir.</summary>
    [Fact]
    public async Task A_silent_sync_that_could_not_be_sent_stays_pending_for_the_next_idle()
    {
        var h = Harness.SyncedOnMain();
        h.Head = new HeadState("main", ShaB);
        h.Port.Accepts = false;

        await h.Coordinator.HeadTriggerAsync(HeadMove.Commit);
        Assert.NotNull(h.Coordinator.PendingTrigger);

        h.Port.Accepts = true;
        h.Coordinator.OnWorkspaceIdle();

        Assert.Equal([SilentSyncReason.Commit], h.Port.Silent);
        Assert.Null(h.Coordinator.PendingTrigger);
    }

    /// <summary>[final review I1] Branch değişiminin bölümü de gönderilemezse kaybolmaz: bir sonraki meşguliyet bitişinde
    /// bölüm açılır.</summary>
    [Fact]
    public async Task A_branch_section_that_could_not_be_sent_stays_pending_for_the_next_idle()
    {
        var h = Harness.SyncedOnMain();
        h.Head = new HeadState("feature", ShaB);
        h.Port.Accepts = false;

        await h.Coordinator.HeadTriggerAsync(HeadMove.BranchSwitch);
        Assert.NotNull(h.Coordinator.PendingTrigger);

        h.Port.Accepts = true;
        h.Coordinator.OnWorkspaceIdle();

        Assert.Equal([PlanProgressLines.SwitchedBranch("main", "feature", RevisionText.Short(ShaB))], h.Port.BranchChanges);
    }

    [Fact]
    public async Task Activation_within_five_seconds_of_a_sync_does_nothing()
    {
        var h = Harness.SyncedOnMain();
        h.Port.Synced("main", ShaA);
        h.Port.Now += AutoSyncCoordinator.ActivationQuietMs - 1;

        await h.Coordinator.WindowActivatedAsync();

        Assert.Equal(0, h.Port.SyncCount);
    }

    /// <summary>Eşik geçtiyse dönüş sessiz Sync'ler — HEAD aynı olsa bile (dönüş dosya düzenlemelerini yakalar).</summary>
    [Fact]
    public async Task Activation_later_syncs_silently()
    {
        var h = Harness.SyncedOnMain();

        await h.Coordinator.WindowActivatedAsync();

        Assert.Equal([SilentSyncReason.Refresh], h.Port.Silent);
        Assert.Empty(h.Port.BranchChanges);
    }

    /// <summary>Aracın kendi checkout'u: checkout + onun BranchChange Sync'i uçuştayken izleyici de tetikler; Sync
    /// yeni HEAD'i ölçer ve bekleyen tetik atlanır — tek Sync (spec §6.1 "çift Sync yok").</summary>
    [Fact]
    public async Task Our_own_checkout_plus_the_watcher_is_one_sync()
    {
        var h = Harness.SyncedOnMain();
        h.Port.IsWorkspaceBusy = true; // checkout, ardından onun Sync'i
        h.Head = new HeadState("feature", ShaB);

        await h.Coordinator.HeadTriggerAsync(HeadMove.BranchSwitch);
        h.Port.Synced("feature", ShaB); // aracın kendi BranchChange Sync'i yeni HEAD'i ölçtü
        h.Port.IsWorkspaceBusy = false;
        h.Coordinator.OnWorkspaceIdle();

        Assert.Equal(0, h.Port.SyncCount);
    }

    // ---------------------------------------------------------------- [T8 · spec §6.1 · §6.2 · karar 10] koşu sırasında

    private static readonly string Summary = PlanProgressLines.RunInterruptedByBranchChange(1, 1, null);

    /// <summary>Koşu uçuşta: <see cref="AutoSyncCoordinator.OnWorkspaceIdle"/> koşunun başladığını görür (VM'de
    /// <c>PropagateRunLock</c> her kilit geçişinde bildirir).</summary>
    private static Harness RunningOnMain()
    {
        var h = Harness.SyncedOnMain();
        h.Port.IsRunInFlight = true;
        h.Coordinator.OnWorkspaceIdle();
        return h;
    }

    /// <summary>Koşu bitti: VM'in kilit düşüşü bildirimi.</summary>
    private static void EndRun(Harness h, string? summary = null)
    {
        h.Port.Summary = summary;
        h.Port.IsRunInFlight = false;
        h.Coordinator.OnWorkspaceIdle();
    }

    /// <summary>Koşu sırasında checkout/pull/reset koşuyu BİR kez nazikçe keser; tetik koşu bitene dek bekler.</summary>
    [Fact]
    public async Task A_branch_switch_mid_run_interrupts_once()
    {
        var h = RunningOnMain();
        h.Head = new HeadState("feature", ShaB);

        await h.Coordinator.HeadTriggerAsync(HeadMove.BranchSwitch);
        await h.Coordinator.HeadTriggerAsync(HeadMove.Other);
        await h.Coordinator.HeadTriggerAsync(HeadMove.BranchSwitch);

        Assert.Equal(1, h.Port.Interrupts);
        Assert.Equal(0, h.Port.SyncCount);
        Assert.NotNull(h.Coordinator.PendingTrigger);
    }

    /// <summary>Koşu sırasında commit hiçbir şey yapmaz: kesmez, beklemez (spec §6.1).</summary>
    [Fact]
    public async Task A_commit_mid_run_does_nothing()
    {
        var h = RunningOnMain();
        h.Head = new HeadState("main", ShaB);

        await h.Coordinator.HeadTriggerAsync(HeadMove.Commit);

        Assert.Equal(0, h.Port.Interrupts);
        Assert.Null(h.Coordinator.PendingTrigger);
        Assert.Equal(0, h.Port.SyncCount);
    }

    /// <summary>Kesilen koşu bitince branch değiştiyse yeni bölüm: ilk satır koşunun özeti, sonra switch satırı.</summary>
    [Fact]
    public async Task After_the_interrupted_run_a_new_section_starts_with_its_summary()
    {
        var h = RunningOnMain();
        h.Head = new HeadState("feature", ShaB);
        await h.Coordinator.HeadTriggerAsync(HeadMove.BranchSwitch);

        EndRun(h, Summary);

        string switched = PlanProgressLines.SwitchedBranch("main", "feature", RevisionText.Short(ShaB));
        Assert.Equal([Summary + "\n" + switched], h.Port.BranchChanges);
        Assert.Empty(h.Port.Silent);
        Assert.Empty(h.Port.StreamLines);
    }

    /// <summary>Branch aynı kaldıysa (aynı branch'te pull/reset) yeni bölüm YOK: özet akışa tek satır, ardından sessiz
    /// Sync — HEAD son Sync'tekiyle aynı olsa da (kesilen koşunun sonuçları defterde değişti).</summary>
    [Fact]
    public async Task An_interrupted_run_on_the_same_branch_streams_its_summary_and_syncs_silently()
    {
        var h = RunningOnMain();
        await h.Coordinator.HeadTriggerAsync(HeadMove.Other); // HEAD main@A — ör. reset aynı commit'e

        EndRun(h, Summary);

        Assert.Equal([Summary], h.Port.StreamLines);
        Assert.Equal([SilentSyncReason.Refresh], h.Port.Silent);
        Assert.Empty(h.Port.BranchChanges);
    }

    /// <summary>Güvenlik ağı: izleyici HEAD değişimini kaçırdıysa koşu bitince yakalanır (spec §6.1).</summary>
    [Fact]
    public void A_head_change_missed_by_the_watcher_is_caught_when_the_run_ends()
    {
        var h = RunningOnMain();
        h.Head = new HeadState("main", ShaB);

        EndRun(h);

        Assert.Equal([SilentSyncReason.Refresh], h.Port.Silent);
    }

    /// <summary>Güvenlik ağı her koşudan sonra Sync üretmez: HEAD aynıysa ya da okunamıyorsa hiçbir şey.</summary>
    [Fact]
    public void A_run_that_ends_on_an_unchanged_or_unreadable_head_syncs_nothing()
    {
        var h = RunningOnMain();
        EndRun(h);
        Assert.Equal(0, h.Port.SyncCount);

        h.Port.IsRunInFlight = true;
        h.Coordinator.OnWorkspaceIdle();
        h.Head = null;
        EndRun(h);
        Assert.Equal(0, h.Port.SyncCount);
    }

    /// <summary>Kesme koşu başınadır: sonraki koşuda gelen branch değişimi yine keser.</summary>
    [Fact]
    public async Task The_next_run_can_be_interrupted_again()
    {
        var h = RunningOnMain();
        h.Head = new HeadState("feature", ShaB);
        await h.Coordinator.HeadTriggerAsync(HeadMove.BranchSwitch);
        EndRun(h, Summary);
        h.Port.Synced("feature", ShaB);

        h.Port.IsRunInFlight = true;
        h.Coordinator.OnWorkspaceIdle();
        h.Head = new HeadState("main", ShaA);
        await h.Coordinator.HeadTriggerAsync(HeadMove.BranchSwitch);

        Assert.Equal(2, h.Port.Interrupts);
    }

    /// <summary>İzleyici kurulamazsa konsola kök başına BİR satır; aynı köke yeniden bağlanmak satırı tekrarlamaz.</summary>
    [Fact]
    public void An_unavailable_watcher_is_reported_once_per_root()
    {
        using var dir = GitRoot();
        var h = new Harness(() => new FakeHeadWatcher { Starts = false });

        h.Coordinator.Attach(dir.Path);
        h.Coordinator.Attach("");
        h.Coordinator.Attach(dir.Path);

        Assert.Equal([PlanProgressLines.HeadWatcherUnavailable(FakeHeadWatcher.Reason)], h.Port.ConsoleLines);
    }

    /// <summary>[review M3] Değiştirilen ya da kapatılan izleyicinin (UI kuyruğunda bekleyen) geri çağrısı düşer;
    /// yalnız GÜNCEL izleyicinin tetiği Sync'ler.</summary>
    [Fact]
    public void A_callback_from_a_replaced_or_disposed_watcher_is_dropped()
    {
        using var first = GitRoot();
        using var second = GitRoot();
        var watchers = new List<FakeHeadWatcher>();
        var h = Harness.SyncedOnMain(() => { var w = new FakeHeadWatcher(); watchers.Add(w); return w; });
        h.Head = new HeadState("main", ShaB);

        h.Coordinator.Attach(first.Path);
        h.Coordinator.Attach(second.Path);
        watchers[0].OnSettled!(HeadMove.Commit);
        Assert.Equal(0, h.Port.SyncCount);

        watchers[1].OnSettled!(HeadMove.Commit);
        Assert.Equal([SilentSyncReason.Commit], h.Port.Silent);

        h.Coordinator.Dispose();
        watchers[1].OnSettled!(HeadMove.Commit);
        Assert.Single(h.Port.Silent);
    }

    /// <summary>[review M4] Henüz reflog'u olmayan depo (ilk commit'ten önce <c>logs</c> yok): pencereye dönüş
    /// izleyiciyi yeniden dener; ilk commit'ten sonra kurulur. Satır kök başına yine BİR kez.</summary>
    [Fact]
    public async Task Activation_retries_a_watcher_that_could_not_start()
    {
        using var dir = GitRoot();
        var watchers = new List<FakeHeadWatcher>();
        var h = Harness.SyncedOnMain(() =>
        {
            var w = new FakeHeadWatcher { Starts = watchers.Count > 0 };
            watchers.Add(w);
            return w;
        });

        h.Coordinator.Attach(dir.Path);
        await h.Coordinator.WindowActivatedAsync();
        await h.Coordinator.WindowActivatedAsync();

        Assert.Equal(2, watchers.Count); // bir başarısız, bir başarılı; başarılıdan sonra yeniden denenmez
        Assert.NotNull(watchers[1].OnSettled);
        Assert.Single(h.Port.ConsoleLines);
    }

    /// <summary>İçinde <c>.git</c> klasörü olan geçici kök — <see cref="GitDirectory.Resolve"/> onu git deposu sayar.</summary>
    private static TempDir GitRoot()
    {
        var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, ".git"));
        return dir;
    }
}
