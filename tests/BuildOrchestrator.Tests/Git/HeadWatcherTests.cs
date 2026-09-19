using System.Collections.Concurrent;
using System.IO;
using BuildOrchestrator.Core.Git;

namespace BuildOrchestrator.Tests.Git;

/// <summary>
/// [Faz 2/T7 · spec 2026-09-18 §6.1] <see cref="HeadWatcher"/>: gerçek git + gerçek dosya bildirimi — git'in bir
/// işlemdeki ardışık yazımları <see cref="HeadWatcher.SettleDelay"/> sessizlikle TEK çağrıya iner ve çağrı
/// penceredeki en güçlü hareketi taşır. Debounce'un kendisi enjekte saatle saf sınanır
/// (<see cref="Writes_within_the_window_collapse_into_one"/>).
/// </summary>
public sealed class HeadWatcherTests
{
    /// <summary>Gerçek-FS testlerinin tavanı: ilk çağrı bu süre içinde gelmeli.</summary>
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(5);

    /// <summary>İlk çağrıdan sonra ikinci bir çağrının gelmediğini görmek için beklenen ek süre.</summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1);

    private static string GitDir(GitTestRepo repo) => Path.Combine(repo.RootPath, ".git");

    private sealed class Recorder
    {
        public ConcurrentQueue<HeadMove> Calls { get; } = new();
        public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void On(HeadMove move)
        {
            Calls.Enqueue(move);
            First.TrySetResult();
        }
    }

    /// <summary>Checkout + ardışık iki commit tek işlem gibi okunur: tek çağrı, branch değişimi (penceredeki en güçlü
    /// hareket — son satır bir commit olsa da branch değişmiştir).</summary>
    [Fact]
    public async Task A_checkout_settles_into_one_branch_switch()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "1");
        repo.CommitAll("init");
        repo.CreateBranch("feature");
        var recorder = new Recorder();
        using var watcher = new HeadWatcher();
        Assert.True(watcher.Start(GitDir(repo), recorder.On), watcher.UnavailableReason);

        repo.Checkout("feature");
        repo.WriteFile("a.txt", "2");
        repo.CommitAll("c1");
        repo.WriteFile("a.txt", "3");
        repo.CommitAll("c2");

        await recorder.First.Task.WaitAsync(Ceiling);
        await Task.Delay(Quiet);
        Assert.Equal([HeadMove.BranchSwitch], recorder.Calls.ToArray());
    }

    [Fact]
    public async Task A_commit_settles_into_commit()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "1");
        repo.CommitAll("init");
        var recorder = new Recorder();
        using var watcher = new HeadWatcher();
        Assert.True(watcher.Start(GitDir(repo), recorder.On), watcher.UnavailableReason);

        repo.WriteFile("a.txt", "2");
        repo.CommitAll("c1");

        await recorder.First.Task.WaitAsync(Ceiling);
        Assert.Equal([HeadMove.Commit], recorder.Calls.ToArray());
    }

    /// <summary>Reflog klasörü yoksa (henüz hiç ref hareketi olmamış bir git dizini) izleyici kurulmaz ve gerekçesini söyler.</summary>
    [Fact]
    public void A_missing_logs_folder_reports_unavailable()
    {
        using var dir = new TempDir();
        using var watcher = new HeadWatcher();

        Assert.False(watcher.Start(dir.Path, _ => { }));
        Assert.False(string.IsNullOrWhiteSpace(watcher.UnavailableReason));
    }

    /// <summary>Pencere içindeki dürtmeler tek bekleyişte birleşir: öncekiler iptal edilir, yalnız sonuncusu tamamlanınca
    /// tek çağrı yapılır — saat enjekte (gerçek bekleme yok).</summary>
    [Fact]
    public void Writes_within_the_window_collapse_into_one()
    {
        var delays = new List<(TimeSpan Window, TaskCompletionSource Tcs)>();
        Task Delay(TimeSpan window, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource();
            ct.Register(() => tcs.TrySetCanceled(ct));
            delays.Add((window, tcs));
            return tcs.Task;
        }
        int settled = 0;
        using var debouncer = new SettleDebouncer(HeadWatcher.SettleDelay, Delay, () => settled++);

        debouncer.Poke();
        debouncer.Poke();
        debouncer.Poke();
        foreach (var (_, tcs) in delays) tcs.TrySetResult();

        Assert.Equal(3, delays.Count);
        Assert.All(delays, d => Assert.Equal(HeadWatcher.SettleDelay, d.Window));
        Assert.Equal(1, settled);
    }
}
