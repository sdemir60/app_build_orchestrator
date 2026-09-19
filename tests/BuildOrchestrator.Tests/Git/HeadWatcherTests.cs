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

    /// <summary>[review M5] Git'in yazımı yarıdayken pencere kapanırsa yarım satır OKUNMAZ (okuma konumu son tam
    /// satırda kalır); satır tamamlanınca bütün hâliyle sınıflanır.</summary>
    [Fact]
    public void A_half_written_line_is_read_only_once_it_is_complete()
    {
        using var dir = new TempDir();
        string logs = Path.Combine(dir.Path, "logs");
        Directory.CreateDirectory(logs);
        string headLog = Path.Combine(logs, "HEAD");
        File.WriteAllText(headLog, "");
        using var watcher = new HeadWatcher((_, ct) => Task.Delay(Timeout.Infinite, ct)); // pencere hiç kapanmaz
        Assert.True(watcher.Start(dir.Path, _ => { }), watcher.UnavailableReason);
        const string Prefix = "0000000000000000000000000000000000000000 " +
            "1111111111111111111111111111111111111111 Test User <test@example.com> 0 +0000\t";

        File.AppendAllText(headLog, Prefix + "comm");
        Assert.Null(watcher.ReadNewMove());

        File.AppendAllText(headLog, "it: add feature\n");
        Assert.Equal(HeadMove.Commit, watcher.ReadNewMove());
    }

    /// <summary>Pencere içindeki dürtmeler tek bekleyişte birleşir: öncekiler iptal edilir, yalnız sonuncusu tamamlanınca
    /// tek çağrı yapılır — saat enjekte (gerçek bekleme yok).
    /// <para>[T8 fix round 1 · I2] Çağrı, bekleyişin DEVAMINDA gelir ve devam satır içi koşmak zorunda değildir (tam
    /// süit yükünde thread-pool'a düşer): sayaç eskiden devamı beklemeden okunuyordu ve test yükte yarışlıydı. Şimdi
    /// çağrı bir sinyalle beklenir; ardından aynı enjekte saatle İKİNCİ bir pencere açılıp o da beklenir — sayaç tam
    /// 2 ise ilk pencerenin iptal edilmiş bekleyişlerinden gecikmiş bir çağrı gelmemiştir (uyku yok).</para></summary>
    [Fact]
    public async Task Writes_within_the_window_collapse_into_one()
    {
        var delays = new List<(TimeSpan Window, TaskCompletionSource Tcs)>();
        Task Delay(TimeSpan window, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource();
            ct.Register(() => tcs.TrySetCanceled(ct));
            lock (delays) delays.Add((window, tcs));
            return tcs.Task;
        }
        int settled = 0;
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var debouncer = new SettleDebouncer(HeadWatcher.SettleDelay, Delay, () =>
        {
            Interlocked.Increment(ref settled);
            Volatile.Read(ref signal).TrySetResult();
        });

        debouncer.Poke();
        debouncer.Poke();
        debouncer.Poke();
        foreach (var (_, tcs) in delays.ToList()) tcs.TrySetResult();

        Assert.True(await Arrives(signal.Task), "the settled callback never came");
        Assert.Equal(3, delays.Count);
        Assert.All(delays, d => Assert.Equal(HeadWatcher.SettleDelay, d.Window));
        Assert.Equal(1, Volatile.Read(ref settled));

        // Sessizlik kontrolü: ikinci pencere — gecikmiş bir üçüncü çağrı olsaydı sayaç 2'yi aşardı.
        Volatile.Write(ref signal, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        debouncer.Poke();
        delays[^1].Tcs.TrySetResult();
        Assert.True(await Arrives(Volatile.Read(ref signal).Task), "the second window's callback never came");
        Assert.Equal(2, Volatile.Read(ref settled));
    }

    /// <summary>Görev <see cref="Ceiling"/> içinde tamamlandı mı — bekleme sınırı zaman aşımı değil assertion olsun diye.</summary>
    private static async Task<bool> Arrives(Task task)
    {
        try { await task.WaitAsync(Ceiling); return true; }
        catch (TimeoutException) { return false; }
    }
}
