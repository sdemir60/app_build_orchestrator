using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.State;
using static BuildOrchestrator.Tests.Supervisor.CycleRoundsTests;
using static BuildOrchestrator.Tests.Supervisor.RunCoordinatorTests;

using BuildOrchestrator.Core.Planning;
namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [PERF Faz E1] Resolve turlarının <c>decision.log</c> izi. Eskiden grup yalnız NİHAİ kararını yazardı
/// ("cycle A: converged (2 members)"): hangi turda kimin neden bayat kaldığı, hangi yüzeyin kaydığı, turun ve
/// hash'in ne kadar sürdüğü kayda geçmezdi; koşu başında okunamayan bir üretici dosyası ise grubu tam-tur kipine
/// SESSİZCE düşürürdü — "Resolve neden iki tur sürdü / kısa devre neden devreye girmedi" sorusu logdan
/// cevaplanamıyordu. Satır biçimlerinin tek sahibi Core'daki <c>CycleDecisionLines</c>'tır (biçim orada
/// pinli); burada pinlenen, koordinatörün o satırları DOĞRU ANDA ve DOĞRU değerlerle yazmasıdır. Süreler
/// gerçek saattir: değerleri değil yerleri doğrulanır (<c>{ms}</c> deliği).
/// Host ve sahte yüzey diski tek yerdedir (<see cref="RunCoordinatorTests.Harness"/>, CycleRoundsTests) — kopya YASAK.
/// </summary>
public class CycleDecisionLogTests
{
    private const string MsHole = "{ms}";

    /// <summary>[Fix round 1 — M3] decision.log satır eşleştiricisinin TEK hâli (CycleRoundsTests de bunu kullanır):
    /// metin aynen eşleşir, her <c>{ms}</c> deliği bir süre yakalar; satır yoksa logun tamamıyla düşer.</summary>
    private static Match FindLine(string log, string line)
    {
        string pattern = Regex.Escape(line).Replace(Regex.Escape(MsHole), @"(\d+)", StringComparison.Ordinal) + @"\r?$";
        var match = Regex.Match(log, pattern, RegexOptions.Multiline);
        Assert.True(match.Success, $"decision.log has no line '{line}'\n--- decision.log ---\n{log}");
        return match;
    }

    /// <summary>Satırın decision.log'daki yeri.</summary>
    internal static int IndexOf(string log, string line) => FindLine(log, line).Index;

    /// <summary>Satırdaki İLK <c>{ms}</c> deliğinin yakaladığı süre.</summary>
    internal static long MsOf(string log, string line) =>
        long.Parse(FindLine(log, line).Groups[1].Value, CultureInfo.InvariantCulture);

    private static async Task<string> RunCyclesAsync(Harness h)
    {
        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);
        return h.DecisionLog;
    }

    /// <summary>
    /// [Fix round 1 — I1] Bir grup decision.log'da TEK adla anılır: build-order'daki ilk üyenin adı. Retry satırı
    /// eskiden grubu imza temsilcisinin (ordinal en küçük üye) dosya adıyla anardı; build-order'da önde olmayan üye
    /// ordinal olarak en küçükse aynı grup başlıkta "B", retry satırında "A" diye geçerdi.
    /// </summary>
    [Fact]
    public async Task the_retry_line_names_the_group_like_its_header()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            // B → A: build-order lideri B; imza temsilcisi (ordinal en küçük) A.
            var plan = CyclePlanOf(["B", "A"], Node("B", deps: ["A"], inCycle: true), Node("A", deps: ["B"], inCycle: true))
                with { Incremental = RunCoordinatorTests.Incremental("A", "B") };
            var rec = new RoundRecorder();
            using var h = new Harness(plan, rec.Invoker((name, _) => name == "A" ? Exit(1) : Ok()),
                stateStore: new BuildStateStore(cacheRoot));

            await h.Sut.StartAsync(Start(RunMode.Cycles, runId: "r1"), default); // NoProgress ⇒ hafıza yazılır
            await h.Sut.RunCompletion.WaitAsync(Limit);
            await h.Sut.StartAsync(Start(RunMode.Cycles, runId: "r2"), default); // aynı imza ⇒ retry satırı
            await h.Sut.RunCompletion.WaitAsync(Limit);

            string log = h.DecisionLog;
            IndexOf(log, "cycle B: 2 members, 2 producers, evidence off, hash {ms} ms");
            IndexOf(log, "cycle B: retrying — did not converge at this signature (sig) on an earlier run");
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    [Fact] // tur 1'den ÖNCE grup başlığı: üye/üretici sayısı, kanıt durumu, grup başı hash süresi
    public async Task the_group_header_precedes_round_one_and_the_verdict()
    {
        var disk = new SurfaceDisk();
        disk.Set("A", "a1");
        disk.Set("B", "b1");
        var rec = new RoundRecorder();
        using var h = new Harness(HashModePlan(TwoMemberCycle(), "A", "B"), rec.Invoker((_, _) => Ok()),
            apiSurface: disk.Read);

        string log = await RunCyclesAsync(h);

        int header = IndexOf(log, "cycle A: 2 members, 2 producers, evidence on, hash {ms} ms");
        int round1 = IndexOf(log, "cycle A round 1: converged; stale=0 []; moved=none; levels=2; round {ms} ms; hash {ms} ms");
        // [DEĞİŞEN KURAL — RESOLVE 3.4] Eski iddia: karar satırı "cycle A: converged (2 members)" idi. Tur 1 yalnız
        // gereken üyeleri derlediği için satır derlenen sayıyı da taşır; üye terimi yok ⇒ herkes derlendi.
        int verdict = IndexOf(log, CycleDecisionLines.ConvergedVerdict("A", members: 2, compiled: 2));
        Assert.True(header < round1 && round1 < verdict, log);
    }

    [Fact] // her tur sonu: karar, bayat üyeler, kayan yüzey dosyaları, seviye sayısı, tur ve hash süresi
    public async Task every_round_ends_with_its_decision_the_stale_members_and_the_moved_surfaces()
    {
        var disk = new SurfaceDisk();
        disk.Set("A", "a1");
        disk.Set("B", "b-old");
        disk.Set("C", "c1");
        // A → B → C → A: A, B'yi eski nesliyle (b-old) okur; B'nin yüzeyi tur 1'de kayar (b-new) ⇒ tur 2 yalnız A.
        var plan = HashModePlan(CyclePlanOf(["A", "B", "C"],
            Node("A", deps: ["B"], inCycle: true),
            Node("B", deps: ["C"], inCycle: true),
            Node("C", deps: ["A"], inCycle: true)), "A", "B", "C");
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, _) =>
        { disk.Set(name, name == "B" ? "b-new" : name == "A" ? "a1" : "c1"); return Ok(); });
        using var h = new Harness(plan, invoker, apiSurface: disk.Read);

        string log = await RunCyclesAsync(h);

        Assert.Equal(["A#1", "B#1", "C#1", "A#2"], rec.Calls);
        int header = IndexOf(log, "cycle A: 3 members, 3 producers, evidence on, hash {ms} ms");
        int round1 = IndexOf(log, "cycle A round 1: continue; stale=1 [A]; moved=" + SurfaceDisk.PathOf("B")
                                  + "; levels=3; round {ms} ms; hash {ms} ms");
        int round2 = IndexOf(log, "cycle A round 2: converged; stale=0 []; moved=none; levels=1; round {ms} ms; hash {ms} ms");
        // [DEĞİŞEN KURAL — RESOLVE 3.4] eski: "cycle A: converged (3 members)"; artık derlenen sayısıyla (terimsiz grup).
        int verdict = IndexOf(log, CycleDecisionLines.ConvergedVerdict("A", members: 3, compiled: 3));
        Assert.True(header < round1 && round1 < round2 && round2 < verdict, log);
    }

    [Fact] // kanıt yoksa satırlar bunu söyler; kaybedilecek kanıt olmadığı için kayıp satırı da yoktur
    public async Task without_surface_evidence_the_lines_say_evidence_off_and_name_no_stale_set()
    {
        var rec = new RoundRecorder();
        using var h = new Harness(TwoMemberCycle(), rec.Invoker((_, _) => Ok()));

        string log = await RunCyclesAsync(h);

        Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls);
        int header = IndexOf(log, "cycle A: 2 members, 2 producers, evidence off, hash {ms} ms");
        int round1 = IndexOf(log, "cycle A round 1: continue; stale=n/a; moved=n/a; levels=2; round {ms} ms; hash {ms} ms");
        int round2 = IndexOf(log, "cycle A round 2: converged; stale=n/a; moved=n/a; levels=2; round {ms} ms; hash {ms} ms");
        Assert.True(header < round1 && round1 < round2, log);
        Assert.DoesNotContain("surface evidence unavailable", log, StringComparison.Ordinal);
    }

    [Fact] // koşu başında okunamayan üretici dosyası: üretici, dosya ve neden adıyla — ve YALNIZ ilk kayıp
    public async Task a_producer_surface_unreadable_at_group_start_is_named_with_its_file_and_reason()
    {
        var disk = new SurfaceDisk();
        disk.Set("A", "a1");
        disk.Set("B", "b1");
        var rec = new RoundRecorder();
        using var h = new Harness(HashModePlan(TwoMemberCycle(), "A", "B"), rec.Invoker((_, _) => Ok()),
            apiSurface: path => path.Equals(SurfaceDisk.PathOf("B"), StringComparison.OrdinalIgnoreCase) ? null : disk.Read(path));

        string log = await RunCyclesAsync(h);

        Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls); // kanıtsız grup tam turlarda kalır (karar değişmez)
        int header = IndexOf(log, "cycle A: 2 members, 2 producers, evidence off, hash {ms} ms");
        int loss = IndexOf(log, "cycle A: surface evidence unavailable — producer B, file " + SurfaceDisk.PathOf("B")
                                + ": unreadable (locked or corrupt) — full rounds");
        int round1 = IndexOf(log, "cycle A round 1: continue; stale=n/a; moved=n/a; levels=2; round {ms} ms; hash {ms} ms");
        Assert.True(header < round1 && loss < round1, log);
        Assert.Single(Regex.Matches(log, "surface evidence unavailable"));
    }

    [Fact] // iki üretici birden okunamaz: kanıt İLK hatada kapanır, kayıp TEK satırdır
    public async Task when_every_producer_is_unreadable_only_the_first_loss_is_written()
    {
        var rec = new RoundRecorder();
        using var h = new Harness(HashModePlan(TwoMemberCycle(), "A", "B"), rec.Invoker((_, _) => Ok()),
            apiSurface: _ => null);

        string log = await RunCyclesAsync(h);

        IndexOf(log, "cycle A: 2 members, 2 producers, evidence off, hash {ms} ms");
        Assert.Single(Regex.Matches(log, "surface evidence unavailable"));
    }

    [Fact] // üreticinin kanıt yolu türetilemedi (OutputsById'de yok): dosya yok, neden "türetilemedi"
    public async Task a_producer_without_a_derivable_evidence_path_is_named_with_the_reason()
    {
        var plan = TwoMemberCycle() with
        {
            Incremental = RunCoordinatorTests.Incremental("A", "B") with { OutputsById = SurfaceDisk.OutputsFor("A") },
        };
        var rec = new RoundRecorder();
        using var h = new Harness(plan, rec.Invoker((_, _) => Ok()), apiSurface: new SurfaceDisk().Read);

        string log = await RunCyclesAsync(h);

        IndexOf(log, "cycle A: surface evidence unavailable — producer B, file (none): evidence path could not be derived — full rounds");
        Assert.Single(Regex.Matches(log, "surface evidence unavailable"));
    }

    /// <summary>
    /// <b>[DEĞİŞEN METİN]</b> Derleme SONRASI okunamayan yüzey eskiden "cycle A: output surface unreadable —
    /// continuing with full rounds" yazardı: hangi üreticinin hangi dosyası, neden — yazılmazdı ve koşu başındaki
    /// kayıpla ayrı bir dil konuşurdu. Kanıt kaybı artık iki anda da TEK biçimdir (üretici, dosya, neden).
    /// </summary>
    [Fact]
    public async Task a_surface_lost_after_a_compile_is_named_with_the_same_line()
    {
        var disk = new SurfaceDisk();
        disk.Set("A", "a1");
        disk.Set("B", "b1");
        int bCompiled = 0;
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, _) =>
        {
            if (name == "B") Interlocked.Exchange(ref bCompiled, 1);
            return Ok();
        });
        using var h = new Harness(HashModePlan(TwoMemberCycle(), "A", "B"), invoker,
            apiSurface: path => Volatile.Read(ref bCompiled) == 1 && path.Equals(SurfaceDisk.PathOf("B"), StringComparison.OrdinalIgnoreCase)
                ? null
                : disk.Read(path));

        string log = await RunCyclesAsync(h);

        IndexOf(log, "cycle A: 2 members, 2 producers, evidence on, hash {ms} ms");
        IndexOf(log, "cycle A: surface evidence unavailable — producer B, file " + SurfaceDisk.PathOf("B")
                     + ": unreadable (locked or corrupt) — full rounds");
        Assert.DoesNotContain("output surface unreadable", log, StringComparison.Ordinal);
    }
}
