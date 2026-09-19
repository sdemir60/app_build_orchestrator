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
        public bool HasWorkspace { get; set; } = true;
        public bool IsWorkspaceBusy { get; set; }
        public (string? Branch, string? HeadSha)? LastSyncHead { get; set; }
        public long? LastSyncAtMs { get; set; }
        public long Now { get; set; } = 100_000;
        public List<SilentSyncReason> Silent { get; } = [];
        public List<string> BranchChanges { get; } = [];
        public List<string> ConsoleLines { get; } = [];

        public long NowMs() => Now;

        public Task<bool> SyncSilentlyAsync(SilentSyncReason reason)
        {
            Silent.Add(reason);
            return Task.FromResult(true);
        }

        public Task<bool> SyncAfterExternalBranchChangeAsync(string sectionLine)
        {
            BranchChanges.Add(sectionLine);
            return Task.FromResult(true);
        }

        public void AppendConsoleLine(string line) => ConsoleLines.Add(line);

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
        public static Harness SyncedOnMain()
        {
            var h = new Harness { Head = new HeadState("main", ShaA) };
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

    /// <summary>İzleyici kurulamazsa konsola kök başına BİR satır; aynı köke yeniden bağlanmak satırı tekrarlamaz.</summary>
    [Fact]
    public void An_unavailable_watcher_is_reported_once_per_root()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, ".git"));
        var h = new Harness(() => new UnavailableWatcher());

        h.Coordinator.Attach(dir.Path);
        h.Coordinator.Attach("");
        h.Coordinator.Attach(dir.Path);

        Assert.Equal([PlanProgressLines.HeadWatcherUnavailable(UnavailableWatcher.Reason)], h.Port.ConsoleLines);
    }

    private sealed class UnavailableWatcher : IHeadWatcher
    {
        public const string Reason = "access denied";
        public string? UnavailableReason { get; private set; }

        public bool Start(string gitDir, Action<HeadMove> onSettled)
        {
            UnavailableReason = Reason;
            return false;
        }

        public void Dispose() { }
    }
}
