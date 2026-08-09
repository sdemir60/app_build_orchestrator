using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Supervisor;
using static BuildOrchestrator.Tests.Supervisor.RunCoordinatorTests;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [cycle rounds] SCC (dairesel bağımlılık) tur döngüsü: üyeler artık pre-skip EDİLMEZ, tek iş kalemi olarak
/// dispatch edilip sıralı turlarla derlenirler. Bu dosya davranış sözleşmesinin dört ayağını pinler:
/// (1) turlar — tek yeşil tur yetmez, iki ardışık yeşil gerekir; (2) ara tur sonuçları YAYILMAZ; (3) üyeler
/// build-order sırasıyla ve SIRALI invoke edilir; (4) yakınsamayan grup hiçbir şey persist etmez.
///
/// Fixture: <see cref="RunCoordinatorTests"/>'in harness'ı, fake invoker'ı ve plan yardımcıları AYNEN
/// kullanılır (<c>using static</c>) — koordinatörün test host'u tek yerdedir, kopya YASAK (CLAUDE.md).
/// Gerçek MSBuild YOK, sleep/poll YOK [D8].
/// </summary>
public class CycleRoundsTests
{
    /// <summary>
    /// Tur-farkındalı fake script: her invoke'ta (proje adı, o projenin KAÇINCI turu) çifti script'e verilir
    /// ve çağrı sırası <c>"A#1"</c> biçiminde kaydedilir. Üye başına tur sayacı, SCC üyelerinin her turda tam
    /// bir kez invoke edilmesinden türetilir — testler böylece "tur 1'de patlar, tur 3'te düzelir" gibi
    /// senaryoları doğrudan yazabilir. Sıra listesi hem KAÇ tur koştuğunun hem de üyelerin build-order'da
    /// gidip gitmediğinin tek kanıtıdır.
    /// </summary>
    private sealed class RoundRecorder
    {
        private readonly List<string> _calls = [];
        private readonly Dictionary<string, int> _rounds = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> Calls { get { lock (_calls) return [.. _calls]; } }

        public FakeInvoker Invoker(Func<string, int, MsBuildInvokeResult> script) =>
            Invoker((name, round, _) => Task.FromResult(script(name, round)));

        public FakeInvoker Invoker(Func<string, int, CancellationToken, Task<MsBuildInvokeResult>> script) =>
            new(async (req, _, ct) =>
            {
                string name = NameOf(req.ProjectId);
                int round;
                lock (_calls)
                {
                    _rounds.TryGetValue(name, out int seen);
                    _rounds[name] = round = seen + 1;
                    _calls.Add($"{name}#{round}");
                }
                return await script(name, round, ct);
            });
    }

    /// <summary>A ↔ B: iki üyeli SCC (her biri diğerine bağımlı), build-order A → B.</summary>
    private static RunPlan TwoMemberCycle() =>
        CyclePlanOf(["A", "B"], Node("A", deps: ["B"], inCycle: true), Node("B", deps: ["A"], inCycle: true));

    /// <summary>"Dün yeşildi" kaydı: <paramref name="signature"/> imzasıyla Succeeded. Persist ile invalidate'i
    /// ayırt edebilmek için imza, run'ın <c>Incremental</c> imzasından ("sig") KASITLI olarak farklıdır —
    /// persist edilseydi "sig" + Succeeded yazılırdı, invalidate edilirse "old" + Failed kalır.</summary>
    private static void SeedGreen(BuildStateStore store, string name, string signature = "old") =>
        store.Upsert(new BuildState(Id(name), signature, "c0", BuildResult.Succeeded,
            DateTimeOffset.UtcNow, "main", LastDurationMs: 42));

    // ---------------------------------------------------------------- 1) iki yeşil tur

    [Fact]
    public async Task green_group_emits_one_result_per_member_after_two_rounds()
    {
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((_, _) => Ok());
        using var h = new Harness(TwoMemberCycle(), invoker);

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // TEK yeşil tur YETMEZ: tur 1 her üyenin public API'sini nihaileştirir, tur 2 herkesi NİHAİ API'lere
        // karşı yeniden derler (CycleRoundPolicy.BaselineRounds). Yakınsama iki ardışık yeşil turdur.
        Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls);

        var events = h.Events;
        // SONUÇ üye başına TAM BİR KEZ — ara turda hiçbir şey yayılmadı.
        Assert.Equal([Id("A"), Id("B")], events.OfType<ProjectSucceededEvent>().Select(e => e.ProjectId));
        Assert.Empty(events.OfType<ProjectFailedEvent>());
        Assert.Empty(events.OfType<ProjectSkippedEvent>());              // cycle pre-skip'i ARTIK YOK
        // projectStarted ise HER turda yayılır: o an gerçekten derleniyor.
        Assert.Equal(4, events.OfType<ProjectStartedEvent>().Count());

        // Tur göstergesinin YAYINCISI pinlenir (round-trip testi yalnız elle kurulmuş bir örneği görür):
        // tur başına bir olay, 1-tabanlı numara, lider = build-order'daki İLK üye, tavan ve üye sayısı.
        var rounds = events.OfType<CycleRoundStartedEvent>().ToList();
        Assert.Equal([1, 2], rounds.Select(r => r.Round));
        Assert.All(rounds, r =>
        {
            Assert.Equal(Id("A"), r.ProjectId);
            Assert.Equal(2, r.MemberCount);
            Assert.Equal(CycleRoundPolicy.RoundCap, r.RoundCap); // literal 3 DEĞİL — tek kaynak policy'dir
        });

        var done = Assert.IsType<RunCompletedEvent>(events[^1]);
        Assert.Equal(2, done.Succeeded);
        Assert.Equal(0, done.Skipped);
        // Queued=0 ⇒ Complete her üye için tam bir kez çağrıldı; biri atlansaydı IsDone asla true olmaz ve
        // run bu satıra hiç gelmezdi (WaitAsync(Limit) timeout).
        Assert.Equal(0, done.Queued);
    }

    // ---------------------------------------------------------------- 2) ara tur sonucu yayılmaz

    [Fact]
    public async Task intermediate_round_failure_is_not_reported()
    {
        var rec = new RoundRecorder();
        // A yalnız TUR 1'de patlar (bayat B.dll'e karşı derlendi), sonra düzelir.
        var invoker = rec.Invoker((name, round) => name == "A" && round == 1 ? Exit(1) : Ok());
        using var h = new Harness(TwoMemberCycle(), invoker);

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // tur1 {A} kirli → tur2 temiz ama ÖNCEKİ tur kirli (henüz iki ardışık yeşil yok) → tur3 temiz+temiz
        // ⇒ Converged. Üç tur koşar.
        Assert.Equal(["A#1", "B#1", "A#2", "B#2", "A#3", "B#3"], rec.Calls);

        var events = h.Events;
        // ARA TUR SONUCU YAYILMAZ: A tur 1'de patladı ama HİÇ projectFailed çıkmaz — SCC tek bir derleme
        // birimidir; yarı bitmiş bir üyeyi "başarısız" diye raporlamak progress'i geri götürürdü.
        Assert.Empty(events.OfType<ProjectFailedEvent>());
        Assert.Equal([Id("A"), Id("B")], events.OfType<ProjectSucceededEvent>().Select(e => e.ProjectId));
        Assert.Equal(2, Assert.IsType<RunCompletedEvent>(events[^1]).Succeeded);
    }

    // ---------------------------------------------------------------- 3) NoProgress

    [Fact]
    public async Task no_progress_stops_after_two_rounds_and_reports_failed()
    {
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, _) => name == "B" ? Exit(1) : Ok()); // aynı küme iki turdur patlıyor
        using var h = new Harness(TwoMemberCycle(), invoker);

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // NoProgress: tur eklemek çözmez → ÜÇÜNCÜ tur YOK (tavan 3 olmasına rağmen).
        Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls);

        var events = h.Events;
        var failed = Assert.Single(events.OfType<ProjectFailedEvent>());
        Assert.Equal(Id("B"), failed.ProjectId);
        Assert.Equal("exit 1", failed.Reason);
        Assert.Equal(Id("A"), Assert.Single(events.OfType<ProjectSucceededEvent>()).ProjectId);

        var done = Assert.IsType<RunCompletedEvent>(events[^1]);
        Assert.Equal(1, done.Succeeded);
        Assert.Equal(1, done.Failed);
        Assert.Equal(0, done.Queued);
    }

    // ---------------------------------------------------------------- 4) süre = turların toplamı

    [Fact]
    public async Task reported_duration_is_the_sum_of_rounds()
    {
        var rec = new RoundRecorder();
        // A: tur 1 = 100ms, tur 2 = 200ms ⇒ 300ms. B: her turda 5ms ⇒ 10ms.
        var invoker = rec.Invoker((name, round) =>
            new MsBuildInvokeResult(ExitCode: 0, DurationMs: name == "A" ? round * 100 : 5, TimedOut: false, Killed: false));
        using var h = new Harness(TwoMemberCycle(), invoker);

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // Raporlanan süre GERÇEK maliyettir: son turunki değil, turların TOPLAMI.
        var succeeded = h.Events.OfType<ProjectSucceededEvent>().ToList();
        Assert.Equal(300, Assert.Single(succeeded, e => e.ProjectId == Id("A")).DurationMs);
        Assert.Equal(10, Assert.Single(succeeded, e => e.ProjectId == Id("B")).DurationMs);
    }

    // ---------------------------------------------------------------- 5) sıralı + build-order

    [Fact]
    public async Task members_are_invoked_sequentially_in_build_order()
    {
        // Üç üyeli SCC, parallelism 4: grup TEK iş kalemidir, diğer worker'lar park eder.
        var plan = CyclePlanOf(["A", "B", "C"],
            Node("A", deps: ["C"], inCycle: true), Node("B", deps: ["A"], inCycle: true), Node("C", deps: ["B"], inCycle: true));
        var rec = new RoundRecorder();
        // Her invoke'ta GERÇEK bir await noktası var: eşzamanlı (Task.WhenAll) bir uygulama olsaydı ikinci üye
        // ilk üye askıdayken başlar ve MaxConcurrent 1'i aşardı.
        var invoker = rec.Invoker(async (_, _, _) => { await Task.Yield(); return Ok(); });
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(parallelism: 4), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // Sıra build-order'dır ve turlar iç içe GEÇMEZ; paralellik doğruluk sorunudur: A, B.dll'i okurken
        // B aynı dosyayı yazıyor olurdu.
        Assert.Equal(["A#1", "B#1", "C#1", "A#2", "B#2", "C#2"], rec.Calls);
        Assert.Equal(1, invoker.MaxConcurrent);
        Assert.Equal(3, Assert.IsType<RunCompletedEvent>(h.Events[^1]).Succeeded);
    }

    // ---------------------------------------------------------------- 6) yakınsamayan grup persist etmez

    [Fact]
    public async Task non_converged_group_persists_nothing_even_for_green_members()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            SeedGreen(store, "A");   // "dün" ikisi de yeşildi, "old" imzasıyla kaydedildi
            SeedGreen(store, "B");
            var plan = TwoMemberCycle() with { Incremental = RunCoordinatorTests.Incremental("A", "B") };
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((name, _) => name == "B" ? Exit(1) : Ok()); // NoProgress
            using var h = new Harness(plan, invoker, stateStore: store);

            await h.Sut.StartAsync(Start(parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls);
            // A HER TURDA YEŞİLDİ ama grup yakınsamadı: turlar bir bütündür. Taze imza yazılsaydı bir sonraki
            // Build A'yı "güncel" sayıp atlar, grup yarım kalmış hâlde TEMİZ görünürdü (§4: DLL/bin timestamp
            // okunmadığı için bunu yakalayacak başka mekanizma yok).
            var a = store.Load()[Id("A")];
            Assert.Equal(BuildResult.Failed, a.LastResult);   // yeşil görünen üye bile GEÇERSİZLEŞTİRİLİR
            Assert.Equal("old", a.BuiltSignature);            // taze imza ("sig") YAZILMADI ⇒ persist YOK
            Assert.Equal(BuildResult.Failed, store.Load()[Id("B")].LastResult);
            // Kontrol: A kullanıcıya yine Succeeded raporlanır — invalidasyon SONUCU maskelemez.
            Assert.Equal(Id("A"), Assert.Single(h.Events.OfType<ProjectSucceededEvent>()).ProjectId);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    // ---------------------------------------------------------------- 6b) KONTROL GRUBU: yakınsayan persist EDER

    [Fact]
    public async Task a_converged_group_persists_a_fresh_signature_for_every_member()
    {
        // Kural 3'ün POZİTİF yarısı. Diğer testlerin hepsi "persist ETMEZ" tarafını görür; bu kontrol grubu
        // olmadan `trustedResult`'ı sabit false yazmak — yani döngüleri KALICI olarak non-incremental yapmak,
        // özelliğin en sessiz bozulma yönü — tüm süiti YEŞİL bırakırdı. (Kardeş kuralın testindeki "Solo
        // kontrol grubudur … aksi halde test önemsizce geçerdi" deseninin aynısı.)
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            SeedGreen(store, "A");   // "old" imza: persist GERÇEKTEN yazdı mı, ayırt edilebilsin
            SeedGreen(store, "B");
            var plan = TwoMemberCycle() with { Incremental = RunCoordinatorTests.Incremental("A", "B") };
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((_, _) => Ok());
            using var h = new Harness(plan, invoker, stateStore: store);

            await h.Sut.StartAsync(Start(parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls);   // iki ardışık yeşil ⇒ Converged
            var built = store.Load();
            foreach (string name in new[] { "A", "B" })
            {
                Assert.Equal("sig", built[Id(name)].BuiltSignature);            // TAZE imza yazıldı ("old" ezildi)
                Assert.Equal(BuildResult.Succeeded, built[Id(name)].LastResult); // bir sonraki Build atlayabilir
            }
            // Yakınsama tavana dayanmak DEĞİLDİR: "oturmamış döngü" bayrağı taşınmaz.
            Assert.All(h.Events.OfType<ProjectSucceededEvent>(), e => Assert.False(e.CycleUnsettled));
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    // ---------------------------------------------------------------- 7) stop ortasında

    [Fact]
    public async Task stopped_group_invalidates_every_member()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            SeedGreen(store, "A");
            SeedGreen(store, "B");
            var plan = TwoMemberCycle() with { Incremental = RunCoordinatorTests.Incremental("A", "B") };
            using var cts = new CancellationTokenSource();
            var rec = new RoundRecorder();
            // A tur 1'de yeşil bitti; B'nin tur 1 invoke'unun ORTASINDA koşu iptal edildi.
            var invoker = rec.Invoker((name, _, ct) =>
            {
                if (name != "B") return Task.FromResult(Ok());
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(Ok());
            });
            using var h = new Harness(plan, invoker, stateStore: store);

            await h.Sut.StartAsync(Start(parallelism: 1), cts.Token);
            // Complete her üye için (iptal yolunda da) çağrıldı ⇒ run ASILMAZ. Kaçırılsaydı bu satır timeout'a
            // düşerdi: IsDone, InFlight==0 şartını asla sağlamazdı.
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["A#1", "B#1"], rec.Calls);          // iptalden sonra YENİ tur yok
            var a = store.Load()[Id("A")];
            Assert.Equal(BuildResult.Failed, a.LastResult);   // yarım grup bir sonraki Build'de "güncel" görünmez
            Assert.Equal("old", a.BuiltSignature);
            Assert.Equal(BuildResult.Failed, store.Load()[Id("B")].LastResult);
            // NOT: bu testte YAYILAN olaylar sorgulanamaz — run'ın ct'si iptal edildiği an event pump'ı
            // "broken" işaretler (RunCoordinator.PumpEventsAsync) ve sonraki satırlar stdout'a hiç yazılmaz.
            // Raporlama kanalının kendisi bir sonraki testte pinlenir.
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    [Fact]
    public async Task a_cancelled_round_publishes_failed_for_every_member_even_the_one_that_was_green()
    {
        // Yukarıdaki testin RAPORLAMA yarısı. İptal buradaki fake tarafından FIRLATILIR ama run'ın ct'si
        // İPTAL EDİLMEZ: aksi halde event pump'ı ilk yazımda "broken" olur ve pinlemek istediğimiz olaylar
        // hiç yazılmaz. Koordinatörün gördüğü uyarıcı aynıdır (catch (OperationCanceledException)).
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, _, _) => name == "B"
            ? Task.FromException<MsBuildInvokeResult>(new OperationCanceledException())
            : Task.FromResult(Ok()));
        using var h = new Harness(TwoMemberCycle(), invoker);

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A#1", "B#1"], rec.Calls);
        // A tur 1'de YEŞİLDİ ama grup yarıda kesildi: yarım kalmış bir grupta hiçbir üyenin sonucunun
        // arkasında durulamaz. A'nın tur-1 sonucunu "başarılı" diye yayınlamak, tam da Kural 2'nin yasakladığı
        // şeydir (ara tur sonucu nihai sonuç olarak yayılıyor) ve tekil proje yolunun iptalde daima
        // Failed("stopped") raporlamasıyla çelişirdi.
        Assert.Empty(h.Events.OfType<ProjectSucceededEvent>());
        Assert.Equal([Id("A"), Id("B")], h.Events.OfType<ProjectFailedEvent>().Select(e => e.ProjectId));
        Assert.All(h.Events.OfType<ProjectFailedEvent>(), e => Assert.Equal("stopped", e.Reason));
        var done = Assert.IsType<RunCompletedEvent>(h.Events[^1]);
        Assert.Equal(0, done.Succeeded);   // yarıda kesilen üye "başarılı" sayılıp sayaca girmez
        Assert.Equal(2, done.Failed);
        Assert.Equal(0, done.Queued);
    }

    // ---------------------------------------------------------------- 7b) stop sonrası YENİ tur açılmaz

    [Fact]
    public async Task a_stop_during_a_round_opens_no_new_round_and_the_group_never_counts_as_converged()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            SeedGreen(store, "A");
            SeedGreen(store, "B");
            var plan = TwoMemberCycle() with { Incremental = RunCoordinatorTests.Incremental("A", "B") };
            var rec = new RoundRecorder();
            // Hard stop: inner job terminate edilir, in-flight child ölür ve invoke sıradan bir "exit N" gibi
            // döner (ReasonFor bunu "stopped"a çevirir). Tur döngüsü stop'u GÖRMEZSE bir sonraki tur TAZE
            // MSBuild child'ları doğurur — üstelik onlar yeşile dönerse grup "yakınsadı" sayılıp durdurulmuş
            // bir koşu taze imza persist edebilirdi.
            RunCoordinator? sut = null;
            var invoker = rec.Invoker((name, round) =>
            {
                if (name == "A" && round == 1) Assert.True(sut!.TryRequestStop(StopKind.Hard));
                return Exit(1);
            });
            using var h = new Harness(plan, invoker, stateStore: store);
            sut = h.Sut;

            await h.Sut.StartAsync(Start(parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["A#1", "B#1"], rec.Calls);   // tur 1 tamamlanır, İKİNCİ tur AÇILMAZ
            Assert.Equal(["stopped", "stopped"],
                h.Events.OfType<ProjectFailedEvent>().Select(e => e.Reason));
            Assert.Equal(BuildResult.Failed, store.Load()[Id("A")].LastResult);
            Assert.Equal("old", store.Load()[Id("A")].BuiltSignature);
            Assert.Contains(h.Events, e => e is RunStoppedEvent);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    // ---------------------------------------------------------------- 8) tavan: oturmamış döngü bayrağı

    [Fact]
    public async Task cap_reached_flags_successful_members_as_cycle_unsettled_without_faking_a_dep_issue()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            SeedGreen(store, "A");
            SeedGreen(store, "B");
            var plan = TwoMemberCycle() with { Incremental = RunCoordinatorTests.Incremental("A", "B") };
            var rec = new RoundRecorder();
            // tur1 {A} kirli, tur2 {B} kirli (küme DEĞİŞTİ ⇒ NoProgress değil), tur3 temiz. İki ardışık yeşil
            // hiç olmadı ve tavana dayanıldı ⇒ CapReached: çıktılar bir kuşak geride OLABİLİR.
            var invoker = rec.Invoker((name, round) => (name, round) switch
            {
                ("A", 1) => Exit(1),
                ("B", 2) => Exit(1),
                _ => Ok(),
            });
            using var h = new Harness(plan, invoker, stateStore: store);

            await h.Sut.StartAsync(Start(parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["A#1", "B#1", "A#2", "B#2", "A#3", "B#3"], rec.Calls); // tavan: 4. tur YOK
            var succeeded = h.Events.OfType<ProjectSucceededEvent>().ToList();
            Assert.Equal([Id("A"), Id("B")], succeeded.Select(e => e.ProjectId));
            Assert.All(succeeded, e => Assert.True(e.CycleUnsettled));
            // Dep-issue listesine SAHTE isim enjekte EDİLMEZ: o liste "hangi bağımlılık patladı" sorusunun
            // cevabıdır — ikinci bir anlam yüklenirse ▲ N sayacı ile filtre chip'i yanlış sayar.
            Assert.All(succeeded, e => Assert.Null(e.DepIssues));
            // CapReached de yakınsama DEĞİLDİR: persist yok, herkes invalidate.
            Assert.Equal(BuildResult.Failed, store.Load()[Id("A")].LastResult);
            Assert.Equal("old", store.Load()[Id("A")].BuiltSignature);
            Assert.Equal(BuildResult.Failed, store.Load()[Id("B")].LastResult);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    // ---------------------------------------------------------------- 9) yakınsamama hafızası [Task 7]

    [Fact]
    public async Task a_group_remembered_as_non_convergent_is_not_invoked_again_at_the_same_signature()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            var plan = TwoMemberCycle() with { Incremental = RunCoordinatorTests.Incremental("A", "B") };
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((name, _) => name == "B" ? Exit(1) : Ok()); // aynı küme iki turdur patlıyor ⇒ NoProgress
            using var h = new Harness(plan, invoker, stateStore: store);

            // Run 1 (Build): hiç hafıza yok → turlar GERÇEKTEN koşar, NoProgress ile biter.
            await h.Sut.StartAsync(Start(RunMode.Build, runId: "r1"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls);

            // Run 2 (Build, AYNI imza "sig"): grup daha önce bu imzada yakınsamadı → bir daha tur harcanmaz,
            // MSBuild HİÇ çağrılmaz — hiçbir yeni invoke.
            await h.Sut.StartAsync(Start(RunMode.Build, runId: "r2"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls);   // run 2 HİÇBİR ŞEY eklemedi

            // Pre-skip, up-to-date pre-skip'iyle AYNI kanaldan (DecideSkipped) raporlanır.
            var skipped = h.Events.OfType<ProjectSkippedEvent>().ToList();
            Assert.Equal([Id("A"), Id("B")], skipped.Select(e => e.ProjectId));
            Assert.All(skipped, e => Assert.Equal("cycle did not converge at this signature", e.Reason));

            var run2Completed = h.Events.OfType<RunCompletedEvent>().Last();
            Assert.Equal(0, run2Completed.Failed);
            Assert.Equal(2, run2Completed.Skipped);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    [Fact]
    public async Task a_remembered_non_convergent_group_is_invoked_again_once_its_signature_changes()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            bool sourceChanged = false;
            RunPlan Planner(StartRunCommand _, Action<string> __) => TwoMemberCycle() with
            {
                Incremental = sourceChanged
                    ? new IncrementalPlan(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        { [Id("A")] = "sig2", [Id("B")] = "sig2" }, "headsha", "main")
                    : RunCoordinatorTests.Incremental("A", "B"),
            };
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((name, _) => name == "B" ? Exit(1) : Ok()); // hep NoProgress
            using var h = new Harness(TwoMemberCycle(), invoker, planner: Planner, stateStore: store);

            // Run 1 ("sig"): hafıza yok → turlar koşar, NoProgress ⇒ hafıza "sig" ile yazılır.
            await h.Sut.StartAsync(Start(RunMode.Build, runId: "r1"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls);

            // Run 2 ("sig", DEĞİŞMEDİ): hafıza eşleşir → pre-skip, yeni invoke YOK (kontrol).
            await h.Sut.StartAsync(Start(RunMode.Build, runId: "r2"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls);

            // Run 3 ("sig2", KAYNAK DEĞİŞTİ): imza artık eşleşmiyor → grup YENİDEN turlarla denenir.
            sourceChanged = true;
            await h.Sut.StartAsync(Start(RunMode.Build, runId: "r3"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.Equal(["A#1", "B#1", "A#2", "B#2", "A#3", "B#3", "A#4", "B#4"], rec.Calls);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }
}
