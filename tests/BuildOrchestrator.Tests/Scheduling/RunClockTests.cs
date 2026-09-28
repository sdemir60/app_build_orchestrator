using BuildOrchestrator.Core.Scheduling;

namespace BuildOrchestrator.Tests.Scheduling;

// [T55] RunClock: bir koşunun süre saati, zaman kaynağı enjekte edilir (Func<long>).
// Thread.Sleep / poll-until-elapsed YASAK [D8] — testler sahte bir sayacı elle ilerletir.
public class RunClockTests
{
    [Fact]
    public void segment_accumulation_pause_gap_does_not_count()
    {
        // 0 → Start; 1500 → Pause (elapsed=1500); 5000 (paused, hâlâ 1500) → Start; 6000 → elapsed=2500.
        long now = 0;
        var clock = new RunClock(() => now);

        clock.Start();
        now = 1500;
        clock.Pause();
        Assert.Equal(1500, clock.ElapsedMs);

        now = 5000; // paused sırasında geçen süre sayılmamalı
        Assert.Equal(1500, clock.ElapsedMs);

        clock.Start();
        now = 6000;
        Assert.Equal(2500, clock.ElapsedMs);
    }

    // [DEĞİŞEN KURAL] Eskiden: "elapsed, accumulatedMs tohumunun üzerine mevcut segmentin süresi eklenerek
    // hesaplanır" — tohum Continue'nun önceki segmentten devraldığı ElapsedMs'ti. Neden değişti: Continue modu
    // a2ff12e ile koddan kalktı, RunClock'u tohumlayan tek üretici oydu — ctor artık böyle bir parametre almıyor.
    // Bugünkü kural: RunClock her koşu için TAZE kurulur (ctor'da seed yok), elapsed yalnız Start'tan bu yana
    // geçen süredir.
    [Fact]
    public void elapsed_while_running_is_only_the_time_since_start_no_seed()
    {
        long now = 100;
        var clock = new RunClock(() => now);

        clock.Start();
        now = 900;
        Assert.Equal(800, clock.ElapsedMs); // UI çalışırken de okuyabilmeli — yalnız bu koşunun geçen süresi
    }

    // [DEĞİŞEN KURAL] Eskiden: "Start'tan önceki değer accumulatedMs tohumuydu" (Continue'da UI'nin süre
    // sayacı sıfırlanmadan kaldığı yerden gösterilirdi). Neden değişti: yukarıdaki testle aynı gerekçe —
    // tohum üreticisi Continue modu a2ff12e ile kalktı. Bugünkü kural: Start hiç çağrılmadıysa geçen süre
    // sıfırdır — bir sonraki koşu her zaman sıfırdan sayar.
    [Fact]
    public void elapsed_before_first_start_is_zero()
    {
        var clock = new RunClock(() => 999_999);

        Assert.Equal(0, clock.ElapsedMs);
    }

    [Fact]
    public void start_on_already_started_clock_is_idempotent_and_does_not_reset_segment()
    {
        long now = 0;
        var clock = new RunClock(() => now);

        clock.Start();
        now = 1000;
        clock.Start(); // ikinci Start no-op olmalı — segment başlangıcı 1000'e kaymamalı
        now = 1500;

        Assert.Equal(1500, clock.ElapsedMs);
    }

    [Fact]
    public void pause_on_already_paused_clock_is_idempotent_and_does_not_double_count()
    {
        long now = 0;
        var clock = new RunClock(() => now);

        clock.Start();
        now = 1000;
        clock.Pause();
        clock.Pause(); // ikinci Pause no-op olmalı — tekrar (now - segmentStart) eklenmemeli

        Assert.Equal(1000, clock.ElapsedMs);
    }

    [Fact]
    public async Task elapsed_readable_from_a_different_thread_than_the_one_that_started_it()
    {
        long now = 0;
        var clock = new RunClock(() => System.Threading.Interlocked.Read(ref now));

        clock.Start();
        System.Threading.Interlocked.Exchange(ref now, 3000);

        var readFromOtherThread = await Task.Run(() => clock.ElapsedMs);

        Assert.Equal(3000, readFromOtherThread);
    }

    [Fact]
    public void nowMs_null_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RunClock(null!));
    }
}
