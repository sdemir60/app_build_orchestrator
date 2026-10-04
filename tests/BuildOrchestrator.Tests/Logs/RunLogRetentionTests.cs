using System.IO;
using System.Text.RegularExpressions;
using BuildOrchestrator.Core.Logs;
using Xunit;

namespace BuildOrchestrator.Tests.Logs;

/// <summary>
/// [PERF Faz C/C2 · karar 1] Koşu logları <see cref="RunLogRetention.KeepFor"/> kadar saklanır; EN SON koşunun klasörü ne
/// kadar eski olursa olsun kalır. Saat enjekte edilir (<c>now</c> parametresi) — gerçek zaman ve uyku yok; klasör adları
/// <see cref="RunLogWriter"/>'ın kullandığı <see cref="RunLogPaths.RunDirName"/> ile üretilir (ad kalıbı tek yerde).
/// Budama gerçek bir geçici klasörde sınanır; motor süreci başlatılmaz (Supervisor izolasyon guard'ı) — motorun
/// budamayı çağırdığı yer kaynak guard'ıyla pinlenir.
/// </summary>
public sealed class RunLogRetentionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bo-retention-").FullName;

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch (IOException) { } }

    // Sabit "şimdi" (temmuz ortası): damga YEREL saattir (RunCoordinator DateTimeOffset.Now verir) ve üç günlük pencere
    // hiçbir saat diliminin DST geçişini kesmez — testler hangi makinede koşarsa koşsun aynı sonucu verir.
    private static readonly DateTimeOffset Now = LocalTime(new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Unspecified));
    private static readonly TimeSpan Keep = RunLogRetention.KeepFor;

    private static DateTimeOffset LocalTime(DateTime wall) => new(wall, TimeZoneInfo.Local.GetUtcOffset(wall));

    private static TimeSpan Days(int n) => TimeSpan.FromDays(n);

    private static TimeSpan Hours(int n) => TimeSpan.FromHours(n);

    /// <summary>Koşu klasörünün adı: koşunun başladığı an, yazıcının aldığı gibi yerel saatle damgalanır.</summary>
    private static string Name(DateTimeOffset startedAt) => RunLogPaths.RunDirName(startedAt.ToLocalTime());

    /// <summary>Bu koşunun klasör YOLU (diskte oluşturulmaz — <see cref="RunLogRetention.Select"/> yalnız adlara bakar).</summary>
    private string At(DateTimeOffset startedAt) => Path.Combine(_root, Name(startedAt));

    /// <summary>Gerçek bir koşu klasörü: içinde decision.log ve bir proje logu — silme İÇERİĞİYLE birlikte olmalı.</summary>
    private string MakeRun(DateTimeOffset startedAt)
    {
        string dir = At(startedAt);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "decision.log"), "decision");
        File.WriteAllText(Path.Combine(dir, "0123456789abcdef.log"), "project");
        return dir;
    }

    // karar 1: üç gün. README (State on disk) ile ARCHITECTURE (§8.5, §16) aynı süreyi anlatır — biri değişirse üçü birlikte değişir.
    [Fact]
    public void Run_logs_are_kept_for_three_days()
    {
        Assert.Equal(TimeSpan.FromDays(3), RunLogRetention.KeepFor);
    }

    // ---------------------------------------------------------------- Select

    [Fact]
    public void Select_picks_every_folder_older_than_the_window_oldest_first_and_keeps_the_recent_ones()
    {
        // Eski olanlar pencerenin 1 gün ötesinden başlar, saat saat daha eskiye gider (0 = en yeni eski, 4 = en eski).
        var old = Enumerable.Range(0, 5).Select(i => At(Now - Keep - Days(1) - Hours(i))).ToArray();
        var recent = new[] { At(Now - Hours(2)), At(Now - Hours(30)) };

        var picked = RunLogRetention.Select([.. recent, .. old], Now); // karışık sıra: sonuç girişin sırasına bağlı değil

        Assert.Equal(old.OrderBy(p => p, StringComparer.Ordinal).ToArray(), picked.ToArray()); // beşi de, EN ESKİ ÖNCE
    }

    [Fact]
    public void Select_keeps_the_newest_folder_even_when_every_folder_is_older_than_the_window()
    {
        var all = Enumerable.Range(0, 4).Select(i => At(Now - Keep - Days(10 + i))).ToArray(); // [0] en yeni

        var picked = RunLogRetention.Select(all, Now);

        Assert.DoesNotContain(all[0], picked);
        Assert.Equal(all.Skip(1).OrderBy(p => p, StringComparer.Ordinal).ToArray(), picked.ToArray());
    }

    [Fact]
    public void Select_never_picks_the_only_folder_there_is_and_copes_with_an_empty_list()
    {
        Assert.Empty(RunLogRetention.Select([At(Now - Keep - Days(400))], Now));
        Assert.Empty(RunLogRetention.Select([], Now));
    }

    [Fact]
    public void Select_never_picks_a_name_outside_the_run_folder_pattern_and_never_lets_one_shield_a_run()
    {
        string newestRun = At(Now - Keep - Days(10));
        string olderRun = At(Now - Keep - Days(11));
        string[] strays =
        [
            Path.Combine(_root, "keep-me"),                          // başka bir klasör
            Path.Combine(_root, "run-20200101-000000"),              // milisaniyesiz
            Path.Combine(_root, "run-20201340-000000-000"),          // geçersiz tarih (13. ay)
            Path.Combine(_root, "run-garbage"),
            newestRun + ".bak",                                      // yedek kopya: adın sonuna ek
            Path.Combine(_root, "x" + Name(Now - Days(400))),        // adın başına ek
            Path.Combine(_root, Name(Now - Hours(1)) + "-2"),        // damgası BUGÜN ama kalıp değil: "en yeni" sayılmamalı
        ];

        var picked = RunLogRetention.Select([.. strays, newestRun, olderRun], Now);

        // Yalnız kalıba uyan iki klasör var: yeni olanı kalır (en yeni koşu), eskisi gider. Kalıp dışı hiçbiri seçilmez.
        Assert.Equal(new[] { olderRun }, picked.ToArray());
    }

    [Fact]
    public void Select_reads_the_stamp_from_the_last_path_segment_for_bare_names_and_trailing_separators()
    {
        string old = Name(Now - Keep - Days(2));
        string recent = Name(Now - Hours(1));
        string withSeparator = Path.Combine(_root, Name(Now - Keep - Days(3))) + Path.DirectorySeparatorChar;

        var picked = RunLogRetention.Select([old, recent, withSeparator], Now);

        Assert.Equal(new[] { withSeparator, old }, picked.ToArray()); // girişteki yazımıyla, en eski önce
    }

    [Fact]
    public void Select_a_folder_exactly_at_the_cutoff_stays_and_one_millisecond_older_goes()
    {
        string atCutoff = At(Now - Keep);
        string justOver = At(Now - Keep - TimeSpan.FromMilliseconds(1));
        string newest = At(Now);

        Assert.Equal(new[] { justOver }, RunLogRetention.Select([atCutoff, justOver, newest], Now).ToArray());
    }

    [Fact]
    public void Select_never_picks_a_folder_stamped_after_now_and_counts_it_as_the_newest()
    {
        // Etkin koşunun klasörü ya da saati geri alınmış bir makine: damgası now'dan YENİ → silinmez, en yeni sayılır.
        string future = At(Now + Hours(1));
        string olderA = At(Now - Keep - Days(1));
        string olderB = At(Now - Keep - Days(2));

        var picked = RunLogRetention.Select([olderA, future, olderB], Now);

        Assert.Equal(new[] { olderB, olderA }, picked.ToArray()); // en yeni gelecekteki olduğundan eski İKİSİ de gider
    }

    // ---------------------------------------------------------------- Prune

    [Fact]
    public void Prune_deletes_old_run_folders_with_their_contents_counts_them_and_writes_one_line()
    {
        string[] old = [MakeRun(Now - Keep - Days(1)), MakeRun(Now - Keep - Days(2)), MakeRun(Now - Keep - Days(30))];
        string recent = MakeRun(Now - Hours(3));
        string newest = MakeRun(Now - Hours(1));
        string stray = Directory.CreateDirectory(Path.Combine(_root, "keep-me")).FullName;
        string lookalikeFile = Path.Combine(_root, Name(Now - Keep - Days(60)));
        File.WriteAllText(lookalikeFile, "bir DOSYA: klasör değil");                      // adı kalıba uyan bir dosya
        var lines = new List<string>();

        int removed = RunLogRetention.Prune(_root, Now, lines.Add);

        Assert.Equal(3, removed);
        Assert.All(old, p => Assert.False(Directory.Exists(p)));
        Assert.True(Directory.Exists(recent));
        Assert.True(Directory.Exists(newest));
        Assert.True(Directory.Exists(stray));            // kalıba uymayan klasöre dokunulmaz
        Assert.True(File.Exists(lookalikeFile));         // kalıba uyan ama klasör olmayana dokunulmaz
        Assert.Equal($"run logs: removed 3 folders older than {Keep.TotalDays} days", Assert.Single(lines));
    }

    [Fact]
    public void Prune_removes_at_most_the_per_call_limit_oldest_first_and_the_next_call_continues()
    {
        int limit = RunLogRetention.MaxFoldersPerPrune;
        // olds[0] en eski ... olds[limit + 4] eskilerin en yenisi; hepsi pencerenin çok ötesinde.
        var olds = Enumerable.Range(0, limit + 5)
            .Select(i => MakeRun(Now - Keep - Days(30) + TimeSpan.FromMinutes(i))).ToList();
        string fresh = MakeRun(Now - Hours(1));

        Assert.Equal(limit, RunLogRetention.Prune(_root, Now, _ => { }));
        Assert.All(olds.Take(limit), p => Assert.False(Directory.Exists(p)));   // en eskiler gitti
        Assert.All(olds.Skip(limit), p => Assert.True(Directory.Exists(p)));    // en yeni 5 eski sıradaki çağrıya kaldı

        Assert.Equal(5, RunLogRetention.Prune(_root, Now, _ => { }));
        Assert.All(olds, p => Assert.False(Directory.Exists(p)));
        Assert.True(Directory.Exists(fresh));
    }

    [Fact]
    public void Prune_swallows_an_io_error_keeps_going_and_reports_it_in_the_one_line()
    {
        string locked = MakeRun(Now - Keep - Days(2));
        string free = MakeRun(Now - Keep - Days(3));
        string fresh = MakeRun(Now - Hours(1));
        // Bir editörde açık kalmış log: paylaşımsız açık dosya, klasörün silinmesini engeller.
        using var hold = new FileStream(Path.Combine(locked, "decision.log"), FileMode.Open, FileAccess.Read, FileShare.None);
        var lines = new List<string>();

        int removed = RunLogRetention.Prune(_root, Now, lines.Add);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(free));            // hata sıradakini durdurmaz
        Assert.True(Directory.Exists(locked));           // kilitli klasör bir sonraki açılışa kalır
        Assert.True(Directory.Exists(fresh));
        Assert.Contains("removed 1 of 2 folders", Assert.Single(lines));
    }

    [Fact]
    public void Prune_is_silent_when_there_is_nothing_to_remove_or_the_logs_root_is_missing()
    {
        MakeRun(Now - Hours(1));
        var lines = new List<string>();

        Assert.Equal(0, RunLogRetention.Prune(_root, Now, lines.Add));
        Assert.Equal(0, RunLogRetention.Prune(Path.Combine(_root, "missing"), Now, lines.Add));

        Assert.Empty(lines); // motor açılışı her seferinde bir satır basmaz
    }

    // ---------------------------------------------------------------- motor bağlantısı

    [Fact]
    public void The_engine_prunes_run_logs_in_the_background_right_after_the_host_is_built()
    {
        string source = File.ReadAllText(Path.Combine(RepoPaths.SrcRoot, "BuildOrchestrator.Supervisor", "Program.cs"));
        int host = source.IndexOf("new SupervisorHost(", StringComparison.Ordinal);
        int prune = source.IndexOf("RunLogRetention.Prune(", StringComparison.Ordinal);
        int run = source.IndexOf("host.RunAsync()", StringComparison.Ordinal);

        // Host kurulduktan SONRA, motor döngüsü başlamadan ÖNCE: motor hazır olmadan değil, ama onu da bekletmeden.
        Assert.True(host >= 0 && prune > host && run > prune,
            "the retention sweep must start after the host is built and before host.RunAsync()");
        // Arka planda (sonucu beklenmez), şimdi UTC, satır stderr'e (stdout YALNIZ NDJSON).
        Assert.Matches(
            new Regex(@"_\s*=\s*Task\.Run\(\s*\(\)\s*=>\s*RunLogRetention\.Prune\(logsRoot,\s*DateTimeOffset\.UtcNow,\s*Console\.Error\.WriteLine\)\s*\)\s*;"),
            source);
    }
}
