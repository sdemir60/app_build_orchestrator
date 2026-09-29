using System.Diagnostics;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [§3/D8 kabul · kopya YASAK] Process ağacı testlerinin ortak yardımcıları — tek yer. "App ölür → ≤2s, orphan yok"
/// kabulünü üç test ölçer: <see cref="CascadeKillTests"/> (sentetik ağaç, outer Job'un kapanışı),
/// <see cref="KillMidBuildTests"/> (gerçek MSBuild ağacı, outer Job'un kapanışı) ve <c>SafeExitProcessTests</c>
/// (güvenli çıkış, OnExit'in disposal bekleyişi). Bekleyiş ve iddia üçünde de AYNIdır; farkları yalnız saatin
/// başladığı andır (tetik) ve onu çağıran seçer.
/// </summary>
internal static class ProcessTree
{
    /// <summary>
    /// Tetikten (<paramref name="since"/>, tetik anında başlatılmış saat) itibaren <paramref name="budget"/> içinde
    /// <paramref name="handles"/>'ın HEPSİ çıkmış olmalı. Bekleyiş olaya bağlıdır: her handle'ın
    /// <c>WaitForExitAsync</c>'i bütçenin KALANIYLA beklenir (sleep yok — D8). Aşım istisna olarak değil, geride
    /// kalanları adıyla sayan bir iddia olarak düşer. Orphan sorusu handle'ın kendisine sorulur
    /// (<see cref="Process.HasExited"/>): PID araması, yeniden kullanılmış bir PID'de yabancı bir process'i okuyabilirdi.
    /// </summary>
    /// <param name="trigger">Saati başlatan olay — iki mesajda da geçer ("the outer job closed", "the app exited").</param>
    public static async Task AssertNoOrphansAsync(IReadOnlyCollection<Process> handles, TimeSpan budget,
        Stopwatch since, string trigger)
    {
        TimeSpan left = budget - since.Elapsed;
        using (var cts = new CancellationTokenSource(left > TimeSpan.Zero ? left : TimeSpan.Zero))
        {
            try { foreach (var p in handles) await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { /* aşım — aşağıdaki iddia geride kalanları adlandırır */ }
        }
        long elapsedMs = since.ElapsedMilliseconds;
        var orphans = handles.Where(p => !p.HasExited).Select(p => $"{NameOfProcess(p.Id)}({p.Id})").ToList();
        Assert.True(orphans.Count == 0, $"orphans {elapsedMs} ms after {trigger}: {string.Join(", ", orphans)}");
        Assert.True(elapsedMs <= budget.TotalMilliseconds,
            $"the tree took {elapsedMs} ms to die after {trigger} (budget {budget.TotalMilliseconds} ms)");
    }

    /// <summary>Process'in adı — tanı mesajları için. Canlıyken okunur; çıkmışsa ayırt edilebilir bir yer tutucu döner
    /// (<c>GetProcessById</c> <see cref="ArgumentException"/>, ad sorgusu <see cref="InvalidOperationException"/>
    /// atar). <c>KillMidBuildTests.IsMsBuildProcess</c> ile aynı desen.</summary>
    public static string NameOfProcess(int pid)
    {
        try { return Process.GetProcessById(pid).ProcessName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return "(exited)"; }
    }
}
