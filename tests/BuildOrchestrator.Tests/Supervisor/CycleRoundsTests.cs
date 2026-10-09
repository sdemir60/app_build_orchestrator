using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Supervisor;
using static BuildOrchestrator.Tests.Supervisor.RunCoordinatorTests;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [cycle rounds] SCC (dairesel bağımlılık) tur döngüsü: üyeler artık pre-skip EDİLMEZ, tek iş kalemi olarak
/// dispatch edilip turlarla derlenirler. Bu dosya davranış sözleşmesinin dört ayağını pinler:
/// (1) turlar — yüzey kanıtı yoksa tek yeşil tur yetmez, iki ardışık yeşil gerekir; (2) ara tur sonuçları
/// YAYILMAZ (yalnız her derlemenin iki ucu ilan edilir); (3) üyeler build-order'da bariyerli dalgalarla
/// invoke edilir — doğrudan komşular asla aynı anda; (4) yakınsamayan grup yalnız oturmuş (son turda bayat olmayan)
/// üyelerini persist eder — yüzey kanıtı yoksa hiçbirini, kesilen grup hiçbirini [D3].
///
/// Fixture: <see cref="RunCoordinatorTests"/>'in harness'ı, fake invoker'ı ve plan yardımcıları AYNEN
/// kullanılır (<c>using static</c>) — koordinatörün test host'u tek yerdedir, kopya YASAK (CLAUDE.md).
/// Gerçek MSBuild YOK, sleep/poll YOK [D8].
///
/// <para><b>Ölçüm (gerçek OSYS, 2026-10-08, Balanced/paralellik 4 — kullanıcının 2026-10-07 tabanıyla aynı ayar):</b>
/// Clean sonrası TEK bir Build 187 projeyi 7 cycle grubu (33 üye, hepsi tur 1'de yakınsadı) dahil 194 sn'de derledi;
/// taban aynı işi iki koşuda yapıyordu (Resolve cycles 197 sn + Build 187 sn). Değişiklik olmadan ikinci Build
/// 1,2 sn — her grup <c>up to date</c> atlandı (ayrıntı: .claude/outputs/2026-10-08-08-02-build-compiles-dirty-cycles-measurement.md).</para>
/// <para><b>Ölçüm (gerçek OSYS, 2026-10-09 — D2/D3, paralellik 1):</b> VS'de <c>OSYS.UI.Service.WorkOrder</c> gövde
/// değişikliği + VS proje ya da solution build'i sonrası Build'de UI grubu (17 üye) tur 1'de 1 derleme + 16 taşınan üyeyle
/// 0,8 sn'de yakınsadı (taban 2026-10-08: 16 üye "output built outside this tool" ile, 17 derleme, tur 234 sn). Aynı
/// üyede kopya kilidi tur 1'de NoProgress verdi, 16 kardeş güvenilir kaldı ve kilit kalkınca takip Build'i yalnız patlayan
/// üyeyi derledi (8,5 sn; eski kural 17 üyeyi derliyordu, tur 153 sn) (ayrıntı:
/// .claude/outputs/2026-10-09-11-04-cycle-trust-measurement.md).</para>
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
    internal sealed class RoundRecorder
    {
        private readonly List<string> _calls = [];
        private readonly Dictionary<string, int> _rounds = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> Calls { get { lock (_calls) return [.. _calls]; } }

        public FakeInvoker Invoker(Func<string, int, MsBuildInvokeResult> script) =>
            Invoker((name, round, _, _) => Task.FromResult(script(name, round)));

        public FakeInvoker Invoker(Func<string, int, CancellationToken, Task<MsBuildInvokeResult>> script) =>
            Invoker((name, round, _, ct) => script(name, round, ct));

        /// <summary>Script invoke'un satır kanalını da alır — derleyici komut satırı yayan senaryolar için
        /// (<see cref="CompilerLine"/>). Tur sayacı ve çağrı kaydı yalnız burada tutulur.</summary>
        public FakeInvoker Invoker(
            Func<string, int, Action<string>, CancellationToken, Task<MsBuildInvokeResult>> script) =>
            new(async (req, onLine, ct) =>
            {
                string name = NameOf(req.ProjectId);
                int round;
                lock (_calls)
                {
                    _rounds.TryGetValue(name, out int seen);
                    _rounds[name] = round = seen + 1;
                    _calls.Add($"{name}#{round}");
                }
                return await script(name, round, onLine, ct);
            });
    }

    /// <summary>A ↔ B: iki üyeli SCC (her biri diğerine bağımlı), build-order A → B.</summary>
    internal static RunPlan TwoMemberCycle() =>
        CyclePlanOf(["A", "B"], Node("A", deps: ["B"], inCycle: true), Node("B", deps: ["A"], inCycle: true));

    /// <summary>"Dün yeşildi" kaydı: <paramref name="signature"/> imzasıyla Succeeded. Persist ile invalidate'i
    /// ayırt edebilmek için imza, run'ın <c>Incremental</c> imzasından ("sig") KASITLI olarak farklıdır —
    /// persist edilseydi "sig" + Succeeded yazılırdı, invalidate edilirse "old" + Failed kalır.</summary>
    private static void SeedGreen(BuildStateStore store, string name, string signature = "old") =>
        store.Upsert(new BuildState(Id(name), signature, "c0", BuildResult.Succeeded,
            DateTimeOffset.UtcNow, "main", LastDurationMs: 42));

    // ---------------------------------------------------------------- 0) kapsam: hangi mod SCC derler

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — Build cycle derler.]</b> Eski iddia (<c>a_build_run_pre_skips_every_member_with_the_original_reason_and_runs_no_rounds</c>):
    /// "Build bir SCC'yi ASLA derlemez" — üyeler tek bir tur bile koşmadan <c>"in dependency cycle"</c> gerekçesiyle
    /// pre-skip edilirdi; turlar kendi moduna (Cycles) taşınmıştı, çünkü Build'e gömülü turlar ölçülmüş ve iki
    /// dakikalık bir Build on beş dakikaya çıkmıştı.
    /// <para><b>Değişme gerekçesi (ölçüm):</b> o ölçüm tur-öncesi kanıt mekanizmasından (<see cref="CycleMemberNeed"/>,
    /// yüzey kısa devresi) öncedir. 2026-10-07'de iki Cycles koşusunda her grup 1. turda yakınsadı; tur mekanizmasının
    /// kendi payı saniyelik hash'lerdir. Aynı gün 13:17'deki Build, pull ile değişen iki Types cycle üyesini atladı ve
    /// bağımlıları eski DLL'e karşı derlenip CS1061 verdi — satırda ve logda sebep yoktu (ARCHITECTURE §8.1).
    /// Build artık kirli grubu plandaki bir düğüm gibi turlarla derler; bağımlıları grubun bitmesini bekler. Build
    /// için yazılmış AYRI bir kod yolu yine YOKTUR: koordinatör scheduler'a aynı <c>CycleGroups</c> haritasını
    /// geçer (<see cref="CycleCompilation"/>), gerisi Cycles'ınkiyle birebir aynı tur döngüsüdür.</para>
    /// </summary>
    [Fact]
    public async Task a_build_run_compiles_a_dirty_cycle_in_rounds_and_its_dependent_after_the_group()
    {
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((_, _) => Ok());
        // A ↔ B kirli grup; Z gruba bağımlı ve kirli → grup bittikten SONRA, taze çıktıya karşı derlenir.
        var plan = CyclePlanOf(["A", "B"],
            Node("A", deps: ["B"], inCycle: true, willBuild: true),
            Node("B", deps: ["A"], inCycle: true, willBuild: true),
            Node("Z", deps: ["A"], willBuild: true));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(RunMode.Build, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A#1", "B#1", "A#2", "B#2", "Z#1"], rec.Calls); // kanıtsız grup: iki yeşil tur, sonra Z
        Assert.Empty(h.Events.OfType<ProjectSkippedEvent>());         // "in dependency cycle" YOK
        Assert.Equal([1, 2], h.Events.OfType<CycleRoundStartedEvent>().Select(e => e.Round));
        var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
        Assert.Equal(CycleOutcome.Converged, completed.Outcome);
        Assert.Equal(3, h.Events.OfType<ProjectSucceededEvent>().Count());
        var done = Assert.Single(h.Events.OfType<RunCompletedEvent>());
        Assert.Equal(3, done.Succeeded);
        Assert.Equal(0, done.Skipped);
        // Önizleme koşunun GERÇEKTEN yapacağını gösterir: üyeler de dalgada yanar.
        var preview = Assert.Single(h.Events.OfType<BuildPreviewEvent>());
        Assert.All(preview.Items, i => Assert.True(i.WillBuild));
    }

    /// <summary>Build de grup düzeyinde INCREMENTAL'dır: bileşik imzası temiz grup (<c>WillBuild == false</c> gelen
    /// üyeler) tek tur bile koşmadan sıradan "güncel" skip'iyle atlanır; bağımlısı yine derlenir. Karar GRUP
    /// düzeyindedir (<c>All</c>) — Cycles koşusuyla aynı kapı, aynı kod.</summary>
    [Fact]
    public async Task a_build_run_skips_a_cycle_whose_composite_signature_is_clean_as_up_to_date()
    {
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((_, _) => Ok());
        var plan = CyclePlanOf(["A", "B"],
            Node("A", deps: ["B"], inCycle: true, willBuild: false),
            Node("B", deps: ["A"], inCycle: true, willBuild: false),
            Node("Z", deps: ["A"], willBuild: true));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(RunMode.Build, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["Z#1"], rec.Calls);                                 // grup HİÇ derlenmedi, Z derlendi
        var skipped = h.Events.OfType<ProjectSkippedEvent>().ToList();
        Assert.Equal([Id("A"), Id("B")], skipped.Select(e => e.ProjectId));
        Assert.All(skipped, e => Assert.Equal(SkipReasons.UpToDate, e.Reason));
        Assert.All(skipped, e => Assert.False(e.CycleUnconverged));
        Assert.Empty(h.Events.OfType<CycleRoundStartedEvent>());
    }

    /// <summary>Yüzey kanıtı mod tanımaz: Build'in derlediği grup da kimsenin okuduğu yüzey değişmediyse TEK turda
    /// yakınsar, persist edilir ve güvenilir raporlanır (Cycles koşusundaki kardeş testle aynı sahne:
    /// <see cref="a_green_group_whose_surfaces_did_not_change_converges_in_one_round"/>).</summary>
    [Fact]
    public async Task a_build_run_with_surface_evidence_converges_a_green_group_in_one_round()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            SeedGreen(store, "A");
            SeedGreen(store, "B");
            var disk = new SurfaceDisk();
            disk.Set("A", "a1");
            disk.Set("B", "b1");
            var plan = HashModePlan(TwoMemberCycle(), "A", "B");
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((name, _) => { disk.Set(name, name == "A" ? "a1" : "b1"); return Ok(); });
            using var h = new Harness(plan, invoker, stateStore: store, apiSurface: disk.Read);

            await h.Sut.StartAsync(Start(RunMode.Build, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["A#1", "B#1"], rec.Calls);
            var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
            Assert.Equal(CycleOutcome.Converged, completed.Outcome);
            Assert.Equal(1, completed.Rounds);
            foreach (string name in new[] { "A", "B" })
            {
                Assert.Equal("sig", store.Load()[Id(name)].BuiltSignature);
                Assert.Equal(BuildResult.Succeeded, store.Load()[Id(name)].LastResult);
            }
            Assert.All(h.Events.OfType<ProjectSucceededEvent>(), e => Assert.True(e.Trusted));
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    /// <summary>Build'de yakınsamayan grup bağımlısını BLOKLAMAZ: Z, patlayan üyenin son başarılı çıktısına karşı
    /// derlenir ve bunu logunun başında söyler (§8.3) — sessiz bayat referans YOK. Cycles koşusundaki NoProgress
    /// kuralı aynen: aynı küme iki turdur patlıyor, üçüncü tur yok.</summary>
    [Fact]
    public async Task a_build_run_with_a_no_progress_group_still_builds_the_dependent_with_a_dependency_issue()
    {
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, _) => name == "B" ? Exit(1) : Ok());
        var plan = CyclePlanOf(["A", "B"],
            Node("A", deps: ["B"], inCycle: true, willBuild: true),
            Node("B", deps: ["A"], inCycle: true, willBuild: true),
            Node("Z", deps: ["B"], willBuild: true));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(RunMode.Build, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A#1", "B#1", "A#2", "B#2", "Z#1"], rec.Calls);
        var failed = Assert.Single(h.Events.OfType<ProjectFailedEvent>());
        Assert.Equal(Id("B"), failed.ProjectId);
        var z = Assert.Single(h.Events.OfType<ProjectSucceededEvent>(), e => e.ProjectId == Id("Z"));
        Assert.NotEmpty(z.DepIssues ?? []);
        Assert.Contains("warning: B failed in this run — last successful output referenced (B)", LogTextsFor(h, "Z"));
    }

    /// <summary>
    /// Daha önce koşmuş bir <see cref="RunMode.Cycles"/> run'ı bu SCC'yi "yakınsamıyor" diye hatırlamış olabilir.
    /// Hafıza BLOKLAMAZ, yalnız RAPORLAR ([Task 7 · DEĞİŞEN KURAL]); Build de grubu derlediği için aynı kural Build'de
    /// geçerlidir: decision.log hafızayı anar (<see cref="CycleDecisionLines.Retrying"/>) ve grup tur 1'den yeniden
    /// denenir — hiçbir üye pre-skip edilmez.
    /// <para><b>[DEĞİŞEN KURAL — Build cycle derler]</b> Eski ad/iddia:
    /// <c>a_build_run_ignores_non_convergence_memory_left_behind_by_a_cycles_run</c> — Build hafızayı OKUMAZDI ve
    /// üyeler <c>"in dependency cycle"</c> ile pre-skip edilirdi (hafıza okuması modun kendisiyle kapılıydı).
    /// Değişme gerekçesi (ölçüm, 2026-10-07 13:17 koşusu, ARCHITECTURE §8.1): Build kirli grubu derler; hafıza,
    /// grubu derleyen her koşuda okunur ve yalnız raporlanır.</para>
    /// </summary>
    [Fact]
    public async Task a_build_run_reports_non_convergence_memory_left_behind_by_a_cycles_run_and_retries_the_group()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            var plan = TwoMemberCycle() with { Incremental = RunCoordinatorTests.Incremental("A", "B") };
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((name, _) => name == "B" ? Exit(1) : Ok()); // NoProgress

            // Run 1 (Cycles): turlar koşar, yakınsamaz, hafıza "sig" ile YAZILIR.
            using var h = new Harness(plan, invoker, stateStore: store);
            await h.Sut.StartAsync(Start(RunMode.Cycles, runId: "r1"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.Equal("sig", store.Load()[Id("A")].NonConvergentSignature); // sanity: hafıza gerçekten var
            int callsAfterRun1 = rec.Calls.Count;

            // Run 2 (Build, AYNI imza). Hafıza raporlanır, grup yine denenir.
            await h.Sut.StartAsync(Start(RunMode.Build, runId: "r2"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.True(rec.Calls.Count > callsAfterRun1, "Build grubu yeniden denemedi");
            Assert.Contains("cycle A: retrying — did not converge at this signature (sig)", h.DecisionLog,
                StringComparison.Ordinal);
            var run2 = h.Events.SkipWhile(e => e is not RunStartedEvent { RunId: "r2" }).ToList();
            Assert.Empty(run2.OfType<ProjectSkippedEvent>());
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    /// <summary>
    /// <b>Cycles koşusunun kapsamı SCC üyeleri + onların TRANSİTİF UPSTREAM'idir.</b> Kirli bir upstream (X)
    /// gerçekten derlenir ve gruptan ÖNCE gelir; kapsamın tamamen dışında kalan proje (Z, gruba BAĞLI olan
    /// dependent) kendi gerekçesiyle pre-skip edilir.
    ///
    /// <para><b>Neden upstream kapsamda:</b> üye kirli bir X'in bir önceki nesil DLL'ine karşı derlenseydi
    /// derleme yeşil olur, çıktı bayat olurdu — ve koşu sonunda üyenin imzası (X'in KAYNAK terimini zaten
    /// içerir) persist edildiği için bir sonraki Build onu "güncel" sayıp bir daha derlemezdi. Proje kalıcı
    /// olarak bayat bir binary'e link'li kalırdı; çıktı aracın kendisinin olduğundan defter kipinde okunur ve
    /// orada çıktının tarihi eşleşen imzayı bozmaz (ARCHITECTURE §7.6) — bunu yakalayacak başka bir mekanizma
    /// yok.</para>
    ///
    /// <para><b>Neden downstream DEĞİL:</b> Z'yi de almak kapsamı sessizce tüm repoya genişletirdi (bir
    /// çekirdek kütüphanenin dependent kümesi pratikte her şeydir). Z'yi Build derler — düğmenin sırası
    /// Build'den ÖNCEdir.</para>
    ///
    /// <para>Önizleme iddiası ayrı bir kapris değil, kullanıcının gördüğü ekranın konusudur: kapsam dışı bir
    /// satır "derlenecek" diye amber yanıp hemen ardından "skipped" olarak geçmez.</para>
    /// </summary>
    [Fact]
    public async Task a_cycles_run_builds_the_dirty_upstream_of_a_cycle_and_pre_skips_everything_else()
    {
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((_, _) => Ok());
        // X: döngünün UPSTREAM'i ve kirli (WillBuild=true) → derlenmeli, hem de gruptan ÖNCE.
        // Z: döngünün DOWNSTREAM'i ve o da kirli → yine de kapsam DIŞI (aksi halde iddia önemsiz olurdu:
        //    "kirli olan derlenir" değil, "kapsamdaki kirli olan derlenir" pinleniyor).
        var plan = CyclePlanOf(["A", "B"],
            Node("X", willBuild: true),
            Node("A", deps: ["X", "B"], inCycle: true),
            Node("B", deps: ["A"], inCycle: true),
            Node("Z", deps: ["A"], willBuild: true));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["X#1", "A#1", "B#1", "A#2", "B#2"], rec.Calls); // X önce, sonra grup iki tur

        var skipped = Assert.Single(h.Events.OfType<ProjectSkippedEvent>());
        Assert.Equal(Id("Z"), skipped.ProjectId);
        // [DEĞİŞEN KURAL/Task 2] Eski iddia: reason "skipped — not needed by a dependency cycle" — öneki
        // İÇİNDE taşırdı. Ölçülen sorun: decision.log formülü ("{name}: skipped — {reason}") bu önekli
        // reason'la birleşince çift önek basıyordu, stream katmanı da kendi önekini eklerdi. Reason artık
        // YALIN (tek kaynak SkipReasons.OutOfCycleScope) — "skipped — " önekini yalnız onu basan katman ekler.
        Assert.Equal(SkipReasons.OutOfCycleScope, skipped.Reason);
        Assert.False(skipped.CycleUnconverged);

        // Önizleme koşunun GERÇEKTEN yapacağını gösterir: kapsam dışı Z gri, kapsamdaki X amber kalır.
        var preview = Assert.Single(h.Events.OfType<BuildPreviewEvent>());
        Assert.False(Assert.Single(preview.Items, i => i.ProjectId == Id("Z")).WillBuild);
        Assert.True(Assert.Single(preview.Items, i => i.ProjectId == Id("X")).WillBuild);
    }

    [Fact] // Kapsamdaki upstream de INCREMENTAL'dır: temizse (WillBuild=false) sıradan "güncel" skip'i alır.
    public async Task a_clean_upstream_inside_the_scope_is_skipped_as_up_to_date_not_as_out_of_scope()
    {
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((_, _) => Ok());
        var plan = CyclePlanOf(["A", "B"],
            Node("X", willBuild: false),
            Node("A", deps: ["X", "B"], inCycle: true),
            Node("B", deps: ["A"], inCycle: true));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls);   // X derlenmedi
        var skipped = Assert.Single(h.Events.OfType<ProjectSkippedEvent>());
        Assert.Equal(Id("X"), skipped.ProjectId);
        // [DEĞİŞEN KURAL/Task 2] bkz. Task-2 notu — reason artık YALIN, tek kaynak SkipReasons.
        Assert.Equal(SkipReasons.UpToDate, skipped.Reason);    // kapsam dışı DEĞİL — sıradan incremental
    }

    /// <summary>Cycles koşusu da INCREMENTAL'dır: bileşik imzası TEMİZ (state'teki <c>BuiltSignature</c> ile
    /// birebir) bir SCC — yani <c>WillBuild == false</c> gelen üyeler — pre-skip edilir ve sıradan bir "güncel"
    /// skip'iyle AYNI gerekçeyi taşır.
    /// <para><b>Neden gerekli:</b> yakınsamış bir gruptan sonra düğmeye ikinci kez basmak bedava olmalıdır;
    /// aksi halde her basış aynı turları baştan harcardı.</para>
    /// <para>Karar GRUP düzeyindedir (<c>All</c>): bileşik imza üyeler arasında ORTAK olduğu için ya hepsi
    /// temizdir ya hiçbiri; kısmi bir durumda (ör. bir üyenin state'i hiç yok) grubun HİÇBİR üyesi pre-skip
    /// edilmez — yarısı Skipped tohumlanmış bir grubu dispatch etmek bozuk olurdu.</para></summary>
    [Fact]
    public async Task a_cycles_run_pre_skips_a_cycle_whose_composite_signature_is_clean()
    {
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((_, _) => Ok());
        // Önizlemenin (IncrementalPlanner) ürettiği sonuç plan'a BAĞLIDIR — burada doğrudan enjekte edilir:
        // bileşik imza temiz ⇒ her iki üye de WillBuild=false.
        var plan = CyclePlanOf(["A", "B"],
            Node("A", deps: ["B"], inCycle: true, willBuild: false),
            Node("B", deps: ["A"], inCycle: true, willBuild: false));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(RunMode.Cycles), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Empty(rec.Calls);   // temiz grup: tek tur bile koşmaz
        var skipped = h.Events.OfType<ProjectSkippedEvent>().ToList();
        Assert.Equal([Id("A"), Id("B")], skipped.Select(e => e.ProjectId));
        // Sıradan "güncel" skip'iyle AYNI gerekçe — bu bir döngü ARIZASI değil, incremental'in normal işleyişi.
        // [DEĞİŞEN KURAL/Task 2] bkz. Task-2 notu — reason artık YALIN, tek kaynak SkipReasons.
        Assert.All(skipped, e => Assert.Equal(SkipReasons.UpToDate, e.Reason));
        Assert.All(skipped, e => Assert.False(e.CycleUnconverged));
    }

    [Fact] // Kontrol grubu: üyelerden BİRİ bile temiz değilse grup pre-skip EDİLMEZ (yarım tohumlama YOK).
    public async Task a_cycles_run_still_builds_a_cycle_when_only_some_members_look_clean()
    {
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((_, _) => Ok());
        var plan = CyclePlanOf(["A", "B"],
            Node("A", deps: ["B"], inCycle: true, willBuild: false),
            Node("B", deps: ["A"], inCycle: true, willBuild: true));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(RunMode.Cycles), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls);   // grup TAMAMEN derlenir
        Assert.Empty(h.Events.OfType<ProjectSkippedEvent>());
    }

    // ---------------------------------------------------------------- 1) iki yeşil tur

    [Fact]
    public async Task green_group_emits_one_result_per_member_after_two_rounds()
    {
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((_, _) => Ok());
        using var h = new Harness(TwoMemberCycle(), invoker);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
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

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
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

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
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

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
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

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 4), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // Sıra build-order'dır ve turlar iç içe GEÇMEZ; paralellik doğruluk sorunudur: A, B.dll'i okurken
        // B aynı dosyayı yazıyor olurdu.
        Assert.Equal(["A#1", "B#1", "C#1", "A#2", "B#2", "C#2"], rec.Calls);
        Assert.Equal(1, invoker.MaxConcurrent);
        Assert.Equal(3, Assert.IsType<RunCompletedEvent>(h.Events[^1]).Succeeded);
    }

    // ---------------------------------------------------------------- 6) kanıtsız yakınsamayan grup persist etmez

    [Fact]
    public async Task non_converged_group_persists_nothing_even_for_green_members()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            SeedGreen(store, "A");   // "dün" ikisi de yeşildi, "old" imzasıyla kaydedildi
            SeedGreen(store, "B");
            // [spec 2026-09-18 §1-14/Task 2] A'ya ÖNCEDEN kanıtlı bir hata yazılmış olsun (başka bir eski
            // koşudan kalma): bu koşuda A yeşil görünse BİLE grup yakınsamadığı için trustedResult=false —
            // eski kanıt da bugünkü koşudan kanıt DEVRALAMAZ, düşürülmeli.
            store.Upsert(store.Load()[Id("A")] with { FailedSignature = "stale", FailedAt = DateTimeOffset.UtcNow.AddDays(-1) });
            var plan = TwoMemberCycle() with { Incremental = RunCoordinatorTests.Incremental("A", "B") };
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((name, _) => name == "B" ? Exit(1) : Ok()); // NoProgress
            using var h = new Harness(plan, invoker, stateStore: store);

            await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls);
            // A HER TURDA YEŞİLDİ ama grup yakınsamadı: turlar bir bütündür. Taze imza yazılsaydı bir sonraki
            // Build A'yı "güncel" sayıp atlar, grup yarım kalmış hâlde TEMİZ görünürdü (defter kipinde çıktının
            // tarihi eşleşen imzayı bozmaz — ARCHITECTURE §7.6; bunu yakalayacak başka mekanizma yok).
            var a = store.Load()[Id("A")];
            Assert.Equal(BuildResult.Failed, a.LastResult);   // yeşil görünen üye bile GEÇERSİZLEŞTİRİLİR
            Assert.Equal("old", a.BuiltSignature);            // taze imza ("sig") YAZILMADI ⇒ persist YOK
            // [spec 2026-09-18 §1-14/Task 2] A_green_member_of_an_unconverged_group_records_no_failed_signature:
            // A'nın sonucu Succeeded'tir (trustedResult=false yüzünden invalidate edilir) — reason zaten null,
            // ama KANIT KAPISININ İKİNCİ yarısı (trustedResult) burada asıl testtir: grup yakınsamadığı için A
            // KANITLI sayılmaz — önceden yazılmış "stale" kanıt da BU koşudan devralınamaz, düşürülmeli
            // (kanıtsız kırmızı YASAK).
            Assert.Null(a.FailedSignature);
            Assert.Null(a.FailedAt);
            var b = store.Load()[Id("B")];
            Assert.Equal(BuildResult.Failed, b.LastResult);
            // [R-M4b] B derleyici hatasıyla ("exit 1") patladı ama grup yakınsamadı: sonuç arkasında durulabilir
            // DEĞİL — defter kanıt yazmaz VE olay da kanıt demez (aynı kapı). Metinden sınıflandıran bir App
            // burada kırmızı boyardı, bir sonraki Sync griye çevirirdi.
            Assert.Null(b.FailedSignature);
            var bFailed = Assert.Single(h.Events.OfType<ProjectFailedEvent>());
            Assert.Equal(Id("B"), bFailed.ProjectId);
            Assert.StartsWith("exit ", bFailed.Reason, StringComparison.Ordinal);
            Assert.False(bFailed.Evidence);
            // Kontrol: A kullanıcıya yine Succeeded raporlanır — invalidasyon SONUCU maskelemez.
            var aSucceeded = Assert.Single(h.Events.OfType<ProjectSucceededEvent>());
            Assert.Equal(Id("A"), aSucceeded.ProjectId);
            // [final review I1] ...ama olay defterle AYNI kararı taşır: motor bu başarının arkasında DURMUYOR
            // (defter "kanıtsız hata" yazdı) — App satırı yeşil bıraksaydı bir sonraki Sync onu griye çevirirdi.
            Assert.False(aSucceeded.Trusted);
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

            await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
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
            // [final review I1] Kontrol grubu: yakınsayan grubun başarısı GÜVENİLİRDİR (defter imzayı yazdı).
            Assert.All(h.Events.OfType<ProjectSucceededEvent>(), e => Assert.True(e.Trusted));
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    // ---------------------------------------------------------------- 7) stop ortasında

    /// <summary>[Build cycle derler] Stop semantiği moda bağlı değildir — Build'in grubu da aynı yoldan kesilir: her
    /// üye tam bir kez <c>Complete</c> edilir, yarım grup geçersizlenir, yakınsamama hafızası yazılmaz.</summary>
    [Theory]
    [InlineData(RunMode.Cycles)]
    [InlineData(RunMode.Build)]
    public async Task stopped_group_invalidates_every_member(RunMode mode)
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

            await h.Sut.StartAsync(Start(mode, parallelism: 1), cts.Token);
            // Complete her üye için (iptal yolunda da) çağrıldı ⇒ run ASILMAZ. Kaçırılsaydı bu satır timeout'a
            // düşerdi: IsDone, InFlight==0 şartını asla sağlamazdı.
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["A#1", "B#1"], rec.Calls);          // iptalden sonra YENİ tur yok
            var a = store.Load()[Id("A")];
            Assert.Equal(BuildResult.Failed, a.LastResult);   // yarım grup bir sonraki Build'de "güncel" görünmez
            Assert.Equal("old", a.BuiltSignature);
            Assert.Equal(BuildResult.Failed, store.Load()[Id("B")].LastResult);
            // [Task 7 review — I2] Yarıda kesilme (decision hâlâ Continue) "bu SCC asla yakınsamaz" DEMEK
            // DEĞİLDİR: yakınsamama hafızası YAZILMAMALI, aksi halde iptal edilmiş/durdurulmuş bir koşu bir
            // sonraki Build'i sonsuza dek pre-skip ederdi.
            Assert.Null(a.NonConvergentSignature);
            Assert.Null(store.Load()[Id("B")].NonConvergentSignature);
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

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
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

    // ---------------------------------------------------------------- 7b) stop'tan sonra YENİ dispatch yok

    /// <summary>
    /// Stop düştüğü anda grubun KALAN üyeleri de dispatch EDİLMEZ — ve grup asla yakınsamış sayılmaz.
    ///
    /// <para><b>[DEĞİŞEN KURAL] Eski iddia:</b> stop'un düştüğü tur TAMAMLANIRDI (<c>["A#1","B#1"]</c>) ve
    /// yalnız bir SONRAKİ tur açılmazdı. <b>Neden değişti:</b> graceful stop'un sözleşmesi (ARCHITECTURE §4.5)
    /// "yeni hiçbir şey dispatch edilmez, in-flight <c>MSBuild.exe</c> child'ları biter"dir; turun kalan her
    /// üyesi ise YENİ bir child demektir. Sıradan Build bunu scheduler'ın stop kapısıyla zaten sağlıyordu
    /// (<c>ReadySetScheduler.RequestStop</c>); SCC turu kendi üye döngüsünü koştuğu için o kapının DIŞINDA
    /// kalıyordu — kullanıcı bunu "Resolve'da stop'a basıyorum ama sürekli yenileri derlenmeye devam ediyor"
    /// diye tarif etti. Turu yarıda kesmenin bir bedeli YOKTUR: yarıda kesilen grup zaten her üyesini Failed'a
    /// çevirir ve hiçbir şey persist etmez — tamamlanan tur da çöpe gidiyordu.</para>
    ///
    /// <para>Hard stop seçilir çünkü orada in-flight child ÖLDÜRÜLÜR ama job yeni process kabul etmeye devam
    /// eder: kapı yoksa kesilen turun üstüne TAZE child'lar doğar.</para>
    /// </summary>
    [Fact]
    public async Task a_stop_dispatches_no_further_member_and_the_group_never_counts_as_converged()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            SeedGreen(store, "A");
            SeedGreen(store, "B");
            var plan = TwoMemberCycle() with { Incremental = RunCoordinatorTests.Incremental("A", "B") };
            var rec = new RoundRecorder();
            RunCoordinator? sut = null;
            var invoker = rec.Invoker((name, round) =>
            {
                if (name == "A" && round == 1) Assert.True(sut!.TryRequestStop(StopKind.Hard));
                return Exit(1);
            });
            using var h = new Harness(plan, invoker, stateStore: store);
            sut = h.Sut;

            await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["A#1"], rec.Calls);          // B HİÇ dispatch edilmez, ikinci tur da açılmaz
            Assert.Equal(["stopped", "stopped"],       // hiç derlenmeyen üye de yarıda kesilmiş sayılır
                h.Events.OfType<ProjectFailedEvent>().Select(e => e.Reason));
            Assert.Equal(BuildResult.Failed, store.Load()[Id("A")].LastResult);
            Assert.Equal("old", store.Load()[Id("A")].BuiltSignature);
            Assert.Contains(h.Events, e => e is RunStoppedEvent);
            // [Task 7 review — I2] Hard stop de aynı ilkeye tabidir: decision hâlâ Continue'da kaldığı için
            // hafıza YAZILMAZ — bir sonraki Build yine gerçek bir deneme hakkı alır.
            Assert.Null(store.Load()[Id("A")].NonConvergentSignature);
            Assert.Null(store.Load()[Id("B")].NonConvergentSignature);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    /// <summary>
    /// Yarıda kesilen bir tur KARARA sokulmaz. Bu, yukarıdaki kuralın en tehlikeli köşesidir: üye döngüsünden
    /// çıkıp turun sonundaki <see cref="CycleRoundPolicy.Decide"/>'a devam etmek, ikinci turda ve o ana kadarki
    /// üyeler yeşilken <see cref="CycleRoundDecision.Converged"/> üretirdi — hiç derlenmemiş üyeler olduğu
    /// hâlde grup "yakınsadı" sayılır ve <b>durdurulmuş bir koşu TAZE imza persist ederdi</b>. Yani stop, bir
    /// sonraki Build'e "bu SCC güncel" diye yalan söylerdi.
    ///
    /// <para>Senaryo: tur 1 tamamen yeşil (karar Continue, tur 2 açılır), stop tur 2'nin İLK üyesinde düşer.
    /// Kapı yoksa <c>Decide(2, {}, {})</c> → Converged.</para>
    /// <para>[Build cycle derler — final inceleme] Build de grubu aynı tur döngüsüyle derler; graceful Stop'un tur
    /// ortasındaki kapısı moddan bağımsızdır. Test eskiden yalnız Cycles'ta koşuyordu; Theory iki modu da pinler.</para>
    /// </summary>
    [Theory]
    [InlineData(RunMode.Cycles)]
    [InlineData(RunMode.Build)]
    public async Task a_round_cut_short_by_a_stop_is_never_judged_converged(RunMode mode)
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            SeedGreen(store, "A");
            SeedGreen(store, "B");
            var plan = TwoMemberCycle() with { Incremental = RunCoordinatorTests.Incremental("A", "B") };
            var rec = new RoundRecorder();
            RunCoordinator? sut = null;
            var invoker = rec.Invoker((name, round) =>
            {
                // Tur 1 tamamen yeşil → Continue. Stop tur 2'nin ilk üyesinde düşer.
                if (name == "A" && round == 2) Assert.True(sut!.TryRequestStop(StopKind.Graceful));
                return Ok();
            });
            using var h = new Harness(plan, invoker, stateStore: store);
            sut = h.Sut;

            await h.Sut.StartAsync(Start(mode, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["A#1", "B#1", "A#2"], rec.Calls);   // B tur 2'de HİÇ dispatch edilmez
            // Grup yakınsamadı: hiçbir üye Succeeded yayınlamaz ve hiçbir imza persist edilmez.
            Assert.Empty(h.Events.OfType<ProjectSucceededEvent>());
            Assert.DoesNotContain(h.Events, e => e is CycleCompletedEvent);
            Assert.All(h.Events.OfType<ProjectFailedEvent>(), e => Assert.Equal("stopped", e.Reason));
            Assert.Equal(BuildResult.Failed, store.Load()[Id("A")].LastResult);
            Assert.Equal("old", store.Load()[Id("A")].BuiltSignature); // "sig" YAZILMADI
            Assert.Equal("old", store.Load()[Id("B")].BuiltSignature);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    /// <summary>
    /// [spec 2026-09-18 §6.1 · karar 10] Branch kesmesi SCC üyelerine de uygulanır — üyeler aynı tek kapıdan
    /// (<c>ReportProjectResult</c>) geçer. Kesme tur 2'nin SON üyesi derlenirken düşer: tur tamamlanır ve
    /// <c>Decide(2, {}, {})</c> Converged verir; kapı olmasaydı grup güvenilir sayılır ve "sig" persist edilirdi.
    /// </summary>
    [Fact]
    public async Task a_cycle_member_finishing_after_an_interrupt_is_not_recorded_as_built()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            SeedGreen(store, "A");
            SeedGreen(store, "B");
            // [fix round 1 · M4] Eski bir yakınsamama hafızası: kesilen koşunun "Converged" kararı onu SİLMEMELİ.
            store.Upsert(store.Load()[Id("A")] with { NonConvergentSignature = "mem" });
            var plan = TwoMemberCycle() with { Incremental = RunCoordinatorTests.Incremental("A", "B") };
            var rec = new RoundRecorder();
            RunCoordinator? sut = null;
            var invoker = rec.Invoker((name, round) =>
            {
                if (name == "B" && round == 2) Assert.True(sut!.TryRequestStop(StopKind.Interrupt));
                return Ok();
            });
            using var h = new Harness(plan, invoker, stateStore: store);
            sut = h.Sut;

            await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls); // tur 2 tamamlandı (yakınsama kararı verildi)
            Assert.All(h.Events.OfType<ProjectSucceededEvent>(), e => Assert.False(e.Trusted));
            Assert.Equal(BuildResult.Failed, store.Load()[Id("A")].LastResult);
            Assert.Equal("old", store.Load()[Id("A")].BuiltSignature); // "sig" YAZILMADI
            Assert.Equal("old", store.Load()[Id("B")].BuiltSignature);
            // [M4] Kesilen koşunun tur kararı yayılmaz ve hafızaya yazılmaz — Stop'un "Continue" kuralıyla aynı.
            Assert.DoesNotContain(h.Events, e => e is CycleCompletedEvent);
            Assert.Equal("mem", store.Load()[Id("A")].NonConvergentSignature);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    [Fact]
    public async Task a_stop_mid_group_publishes_no_success_even_for_the_members_that_finished_green()
    {
        // [I1] Yukarıdaki testin kaçırdığı delik: ORADA her iki üye de exit!=0 döndüğü için "yarıda kesilen
        // grupta hiçbir üyenin sonucunun arkasında durulamaz" kuralı hiç sınanmıyordu. Burada A ve B turu
        // YEŞİL bitirir ve stop C'nin üstüne düşer; kural yalnız iptal/beklenmeyen-hata yollarında değil,
        // STOP (break) yolunda da geçerli olmalıdır — üçü de aynı gövdeden geçer.
        var plan = CyclePlanOf(["A", "B", "C"],
            Node("A", deps: ["C"], inCycle: true), Node("B", deps: ["A"], inCycle: true), Node("C", deps: ["B"], inCycle: true));
        var rec = new RoundRecorder();
        RunCoordinator? sut = null;
        var invoker = rec.Invoker((name, round) =>
        {
            if (name != "C" || round != 1) return Ok();
            Assert.True(sut!.TryRequestStop(StopKind.Hard)); // hard stop tam C derlenirken düşer
            return Exit(1);
        });
        using var h = new Harness(plan, invoker);
        sut = h.Sut;

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A#1", "B#1", "C#1"], rec.Calls);   // tur 1 biter, İKİNCİ tur açılmaz
        // A ve B tur 1'i YEŞİL bitirdi — ama grup yakınsamadı. Bir ara turun kararı NİHAİ sonuç olarak
        // yayılamaz: tek bir projectSucceeded bile çıkmamalı.
        Assert.Empty(h.Events.OfType<ProjectSucceededEvent>());
        Assert.Equal([Id("A"), Id("B"), Id("C")], h.Events.OfType<ProjectFailedEvent>().Select(e => e.ProjectId));
        Assert.All(h.Events.OfType<ProjectFailedEvent>(), e => Assert.Equal("stopped", e.Reason));
        var done = Assert.IsType<RunCompletedEvent>(h.Events[^1]);
        Assert.Equal(0, done.Succeeded);
        Assert.Equal(3, done.Failed);
        Assert.Equal(0, done.Queued);
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

            await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["A#1", "B#1", "A#2", "B#2", "A#3", "B#3"], rec.Calls); // tavan: 4. tur YOK
            var succeeded = h.Events.OfType<ProjectSucceededEvent>().ToList();
            Assert.Equal([Id("A"), Id("B")], succeeded.Select(e => e.ProjectId));
            Assert.All(succeeded, e => Assert.True(e.CycleUnsettled));
            // [final review I1] Tavan da yakınsama DEĞİLDİR: olay "güvenilmez başarı" der — aşağıdaki
            // invalidate ile AYNI karar. [D3] Kanıtsız grupta (Incremental.OutputsById yok) D3 devreye girmez: hiçbir
            // yeşil üye güvenilmez, grup bütünüyle yeniden derlenir.
            Assert.All(succeeded, e => Assert.False(e.Trusted));
            // Dep-issue listesine SAHTE isim enjekte EDİLMEZ: o liste "hangi bağımlılık patladı" sorusunun
            // cevabıdır — ikinci bir anlam yüklenirse ▲ N sayacı ile filtre chip'i yanlış sayar.
            Assert.All(succeeded, e => Assert.Null(e.DepIssues));
            // CapReached de yakınsama DEĞİLDİR: persist yok, herkes invalidate.
            Assert.Equal(BuildResult.Failed, store.Load()[Id("A")].LastResult);
            Assert.Equal("old", store.Load()[Id("A")].BuiltSignature);
            Assert.Equal(BuildResult.Failed, store.Load()[Id("B")].LastResult);
            // [DEĞİŞEN KURAL] Bu test eskiden `NonConvergentSignature == "sig"` bekliyordu (gerekçe: "tavana
            // dayanmak da gerçek bir yakınsama kararıdır, NoProgress ile AYNI kapıdan geçer"). O kural
            // tavanın KENDİ gerekçesini geçersiz kılıyordu: tavan "bilgi kaybettirmez, çünkü turlar diskteki
            // duruma göre idempotenttir ve bir sonraki Build kaldığı yerden devam eder" diyerek meşrulaşır —
            // ama hafıza yazıldığı an o "sonraki Build" pre-skip edilir ve devam HİÇ gelmez. Buradaki senaryo
            // tam olarak odur: tur1 {A}, tur2 {B}, tur3 temiz — grup GERÇEKTEN yakınsıyor, yalnız bütçesi
            // bitiyor. Artık ayrım kanıta göredir: NoProgress "aynı küme iki kez patladı" (daha fazla tur
            // ÇÖZMEZ) demektir ve hatırlanır; CapReached "bütçe bitti ama hâlâ hareket var" demektir ve
            // hatırlanmaz. Bedeli, dört-altı tur isteyen bir döngünün oturana dek birkaç Build daha tur
            // harcaması; kazancı, bir tur kala donmuş bir grubun ASLA bitememesinin ortadan kalkması.
            Assert.Null(store.Load()[Id("A")].NonConvergentSignature);
            Assert.Null(store.Load()[Id("B")].NonConvergentSignature);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    [Fact]
    public async Task a_capped_group_is_invoked_again_by_the_next_Build_at_the_same_signature()
    {
        // Yukarıdaki kural değişiminin DAVRANIŞ karşılığı — asıl kazanılan şey budur: tavanın "bir sonraki
        // Build kaldığı yerden devam eder" güvencesi artık GERÇEKTEN geçerli. Grup üç turda yakınsayamadı ama
        // ilerliyordu; kaynak DEĞİŞMEDEN koşulan ikinci Build ona dördüncü turu verir ve grup oturur.
        // [D3] Kanıtsız grupta (Incremental.OutputsById yok) D3 devreye girmez: hiçbir yeşil üye güvenilmez, grup
        // bütünüyle yeniden derlenir.
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            var plan = TwoMemberCycle() with { Incremental = RunCoordinatorTests.Incremental("A", "B") };
            var rec = new RoundRecorder();
            // Tur sayacı ÜYE BAŞINA ve run'lar arası birikimlidir: 1-3 arası turlar run 1'e, 4+ run 2'ye aittir.
            var invoker = rec.Invoker((name, round) => (name, round) switch
            {
                ("A", 1) => Exit(1),
                ("B", 2) => Exit(1),
                _ => Ok(),
            });
            using var h = new Harness(plan, invoker, stateStore: store);

            // Run 1: tur1 {A}, tur2 {B}, tur3 temiz ⇒ iki ardışık yeşil YOK, tavan ⇒ CapReached.
            await h.Sut.StartAsync(Start(RunMode.Cycles, runId: "r1"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.Equal(["A#1", "B#1", "A#2", "B#2", "A#3", "B#3"], rec.Calls);
            // Tavan HATIRLANMAZ. Soru, pre-skip'in KENDİ okuyucusuyla sorulur: hiç kayıt açılmamış olması da
            // ("hafıza yazan tek yol buydu") geçerli bir "hatırlanmıyor" hâlidir ve okuyucu ikisini ayırmaz.
            Assert.False(BuildStateStore.IsCycleNonConvergent(store.Load(), Id("A"), "sig"));
            Assert.False(BuildStateStore.IsCycleNonConvergent(store.Load(), Id("B"), "sig"));

            // Run 2 (Build, imza HÂLÂ "sig"): pre-skip YOK — grup gerçekten yeniden derlenir ve bu kez
            // iki ardışık yeşil turla yakınsar.
            await h.Sut.StartAsync(Start(RunMode.Cycles, runId: "r2"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.Equal(["A#1", "B#1", "A#2", "B#2", "A#3", "B#3", "A#4", "B#4", "A#5", "B#5"], rec.Calls);
            Assert.DoesNotContain(h.Events.OfType<ProjectSkippedEvent>(),
                e => e.Reason == SkipReasons.CycleNonConvergent);
            // Ve yakınsadığı için ARTIK taze imza persist edilir — üçüncü bir Build boşuna tur harcamaz.
            Assert.Equal("sig", store.Load()[Id("A")].BuiltSignature);
            Assert.Equal("sig", store.Load()[Id("B")].BuiltSignature);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    // ---------------------------------------------------------------- 9) yakınsamama hafızası [Task 7]

    /// <summary>
    /// [DEĞİŞEN KURAL] Eski iddia: "daha önce bu imzada yakınsamamış bir grup BİR DAHA tur harcamaz" — run 2
    /// tüm üyeleri <c>CycleNonConvergent</c> ile pre-skip eder, MSBuild hiç çağrılmazdı.
    ///
    /// <para>Neden değişti: o kapıya giden tek yol kullanıcının <b>Resolve cycles</b> düğmesine basmasıdır —
    /// turları kendiliğinden harcayan otomatik bir akış yok. Kapı tasarrufu yalnız AÇIK bir komutu sessizce
    /// yutarak sağlıyordu; kullanıcı düğmeye basıyor ve hiçbir şey olmuyordu. Aynı gerekçe kod tabanında
    /// zaten yazılıydı: <c>CapReached</c> bilerek hatırlanmaz, çünkü "pre-skip edilen bir grupta devam HİÇ
    /// gelmez". Ayrıca imza yalnız kaynakları kapsar — paket restore'u, döngü dışı bir bağımlılığın çıktısı
    /// ya da ortam değişmiş olabilir. Açık basış artık bir komuttur: grup tur 1'den taze denenir.</para>
    ///
    /// <para>Hafıza YAZILMAYA devam eder ve okunur; yalnız bloklamak yerine decision.log'a bir "retrying"
    /// satırı düşürür (aşağıdaki assert).</para>
    /// </summary>
    [Fact]
    public async Task an_explicit_resolve_run_retries_a_group_remembered_as_non_convergent()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            var plan = TwoMemberCycle() with { Incremental = RunCoordinatorTests.Incremental("A", "B") };
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((name, _) => name == "B" ? Exit(1) : Ok()); // aynı küme iki turdur patlıyor ⇒ NoProgress
            using var h = new Harness(plan, invoker, stateStore: store);

            // Run 1: hiç hafıza yok → turlar koşar, NoProgress ile biter ve hafıza "sig" ile yazılır.
            await h.Sut.StartAsync(Start(RunMode.Cycles, runId: "r1"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls);
            Assert.Equal("sig", store.Load()[Id("A")].NonConvergentSignature); // ön-koşul: hafıza GERÇEKTEN var

            // Run 2 (AYNI imza): kullanıcı düğmeye yeniden bastı → grup tur 1'den taze denenir.
            await h.Sut.StartAsync(Start(RunMode.Cycles, runId: "r2"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.Equal(["A#1", "B#1", "A#2", "B#2", "A#3", "B#3", "A#4", "B#4"], rec.Calls);

            // Hafıza artık pre-skip üretmez.
            Assert.DoesNotContain(h.Events.OfType<ProjectSkippedEvent>(),
                e => e.Reason == SkipReasons.CycleNonConvergent);

            // Ama sessiz de değildir: operatör grubun neden yine tur harcadığını decision.log'dan görür.
            Assert.Contains("cycle A: retrying — did not converge at this signature (sig)", h.DecisionLog,
                StringComparison.Ordinal);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    /// <summary>
    /// [DEĞİŞEN KURAL] Bu test eskiden "hafıza AYNI imzada pre-skip eder, imza DEĞİŞİNCE grup yeniden
    /// denenir" ayrımını pinliyordu. Hafıza artık bloklamadığı için ayrımın bloklama tarafı kalmadı; testin
    /// koruduğu şey hafızanın <b>imzaya bağlı</b> olmayı sürdürmesidir: aynı imzada "retrying" satırı düşer,
    /// kaynak değişince o satır DÜŞMEZ (hafıza artık bu imza için geçerli değildir). Her iki durumda da grup
    /// turlarını koşar — o yüzden invoke sayısı ikisinde de artar.
    /// </summary>
    [Fact]
    public async Task the_non_convergence_memory_stops_reporting_once_the_signature_changes()
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
            await h.Sut.StartAsync(Start(RunMode.Cycles, runId: "r1"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls);

            // Run 2 ("sig", DEĞİŞMEDİ): hafıza eşleşir → grup yine koşar, ama log "retrying" der.
            await h.Sut.StartAsync(Start(RunMode.Cycles, runId: "r2"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.Equal(["A#1", "B#1", "A#2", "B#2", "A#3", "B#3", "A#4", "B#4"], rec.Calls);
            Assert.Contains("retrying — did not converge at this signature (sig)", h.DecisionLog, StringComparison.Ordinal);

            // Run 3 ("sig2", KAYNAK DEĞİŞTİ): hafıza bu imza için geçerli değil → "retrying" satırı da düşmez.
            // (decision.log run başına yeniden açılır, yani aşağıdaki log YALNIZ run 3'ündür.)
            sourceChanged = true;
            await h.Sut.StartAsync(Start(RunMode.Cycles, runId: "r3"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.Equal(["A#1", "B#1", "A#2", "B#2", "A#3", "B#3", "A#4", "B#4", "A#5", "B#5", "A#6", "B#6"], rec.Calls);
            Assert.DoesNotContain("retrying", h.DecisionLog, StringComparison.Ordinal);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    [Fact]
    public async Task a_converged_group_clears_its_non_convergent_memory_so_a_later_run_at_the_same_signature_still_invokes_it()
    {
        // [Task 7 review — I1] Yakınsayan grup hafızasını bırakmaz: aynı imzada koşan bir sonraki Cycles run'ı
        // onu pre-skip ETMEZ. Silme, hafızanın kendi yazıcısında AÇIKÇA yapılır (PersistBuildStateOnSuccess'in
        // yan etkisine BIRAKILMAZ) — bu test o SONUCU pinler.
        //
        // [DEĞİŞEN KURAL] Eskiden orta adım Rebuild'di: "Rebuild pre-skip bilmez, hafızalı grubu yine dener"
        // deniyordu. Turlar bir dönem yalnız kendi modunda (RunMode.Cycles) koştuğu için o kaçış yolu kapanmış ve
        // orta adım gerçek hayattaki çıkış yoluna çevrilmişti: KAYNAK DEĞİŞİR (imza "sig" → "sig2"), grup yeniden
        // denenir ve bu kez yakınsar. Sonra imza "sig"e geri döner; hafıza temizlenmiş olduğu için grup yine invoke
        // edilir. Rebuild bugün yine grupları derler (CycleCompilation); test gerçekçi yolu korur, iddia aynı.
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            string signature = "sig";
            RunPlan Planner(StartRunCommand _, Action<string> __) => TwoMemberCycle() with
            {
                Incremental = new IncrementalPlan(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    { [Id("A")] = signature, [Id("B")] = signature }, "headsha", "main"),
            };
            bool converges = false;
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((name, _) => name == "B" && !converges ? Exit(1) : Ok());
            using var h = new Harness(TwoMemberCycle(), invoker, planner: Planner, stateStore: store);

            // Run 1 ("sig"): NoProgress ⇒ hafıza "sig" ile yazılır.
            await h.Sut.StartAsync(Start(RunMode.Cycles, runId: "r1"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls);
            Assert.Equal("sig", store.Load()[Id("A")].NonConvergentSignature);

            // Run 2 ("sig2", kaynak değişti): hafıza eşleşmez → grup denenir ve GERÇEKTEN yakınsar.
            signature = "sig2";
            converges = true;
            await h.Sut.StartAsync(Start(RunMode.Cycles, runId: "r2"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.Equal(["A#1", "B#1", "A#2", "B#2", "A#3", "B#3", "A#4", "B#4"], rec.Calls);
            Assert.Null(store.Load()[Id("A")].NonConvergentSignature); // yakınsama hafızayı SİLDİ
            Assert.Null(store.Load()[Id("B")].NonConvergentSignature);

            // Run 3 ("sig"e geri dönüldü): hafıza temizlendiği için pre-skip EDİLMEZ — grup invoke edilir.
            signature = "sig";
            int before = invoker.Requests.Count;
            await h.Sut.StartAsync(Start(RunMode.Cycles, runId: "r3"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.True(invoker.Requests.Count > before); // MSBuild GERÇEKTEN çağrıldı, pre-skip edilmedi
            Assert.DoesNotContain(h.Events.OfType<ProjectSkippedEvent>(),
                e => e.Reason == SkipReasons.CycleNonConvergent);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    /// <summary>
    /// Yakınsayan grubun yakınsamama hafızası, üyelerin başarısı depIssue TAŞISA bile silinir: silme hafızanın kendi
    /// yazıcısında AÇIKÇA yapılır (<c>UpdateCycleNonConvergenceMemory</c>), persist'in yan etkisine BIRAKILMAZ —
    /// depIssue'lu başarı notla persist edilir ve o yol hafızaya dokunmaz.
    /// <para><b>[GERİ GELEN TEST — Build cycle derler]</b> Bu test silinmişti; gerekçe "senaryo üretilemez: grubu
    /// derleyen tek koşuda döngü dışı her proje pre-skip edilir, hiçbir zaman FAILED olmaz" idi. Build grubu da döngü
    /// dışındaki upstream'i de aynı koşuda derler: X patlarsa üyeler X'in son başarılı çıktısına karşı derlenir ve
    /// başarıları depIssue taşır.</para>
    /// </summary>
    [Fact]
    public async Task convergence_clears_the_memory_even_for_a_member_whose_success_carries_a_dep_issue()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            // X → (A ↔ B): X döngü dışı upstream. Run 1: grup ilerlemez (B patlar) ⇒ hafıza "sig". Run 2: X patlar,
            // grup yakınsar — iki üyenin başarısı da X'i kök taşır.
            var plan = CyclePlanOf(["A", "B"],
                Node("X"),
                Node("A", deps: ["X", "B"], inCycle: true),
                Node("B", deps: ["X", "A"], inCycle: true)) with { Incremental = RunCoordinatorTests.Incremental("X", "A", "B") };
            bool secondRun = false;
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((name, _) =>
                secondRun ? (name == "X" ? Exit(1) : Ok()) : (name == "B" ? Exit(1) : Ok()));
            using var h = new Harness(plan, invoker, stateStore: store);

            await h.Sut.StartAsync(Start(RunMode.Build, runId: "r1"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.Equal("sig", store.Load()[Id("A")].NonConvergentSignature); // ön-koşul: hafıza yazıldı

            secondRun = true;
            await h.Sut.StartAsync(Start(RunMode.Build, runId: "r2"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            var run2 = h.Events.SkipWhile(e => e is not RunStartedEvent { RunId: "r2" }).ToList();
            Assert.Equal(CycleOutcome.Converged, Assert.Single(run2.OfType<CycleCompletedEvent>()).Outcome);
            var successes = run2.OfType<ProjectSucceededEvent>().ToList();
            Assert.Equal([Id("A"), Id("B")], successes.Select(e => e.ProjectId).Order());
            Assert.All(successes, e => Assert.NotEmpty(e.DepIssues ?? []));     // A ve B, X'i kök taşır
            Assert.Null(store.Load()[Id("A")].NonConvergentSignature);           // yakınsama hafızayı SİLDİ
            Assert.Null(store.Load()[Id("B")].NonConvergentSignature);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    // ---------------------------------------------------------------- 10) karar decision.log'a düşer [M1]

    [Fact]
    public async Task the_final_round_decision_is_written_to_the_decision_log_with_the_remembered_signature()
    {
        // [M1] Log'da yalnız üye-başına "failed — exit 1" satırları olsaydı operatör grubun NEDEN durduğunu
        // (tavan mı, ilerleme yokluğu mu, yakınsama mı) o koşunun logundan ASLA çıkaramazdı. Karar, KESİNLEŞTİĞİ
        // yerde tek satırda yazılır; hafıza yazıldıysa imzası da aynı satırdadır.
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            var plan = TwoMemberCycle() with { Incremental = RunCoordinatorTests.Incremental("A", "B") };
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((name, _) => name == "B" ? Exit(1) : Ok()); // NoProgress
            using var h = new Harness(plan, invoker, stateStore: store);

            await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            // Grup, tur göstergesiyle AYNI adla (build-order'daki ilk üye) anılır.
            // [DEĞİŞEN KURAL — metin] Eski satır "the same members failed twice" idi; yüzey kanıtı NoProgress'i
            // TEK turda da verebildiği için "twice" artık yanlışlanabilir bir iddiaydı (tek turda kesilen grupta
            // kimse iki kez patlamadı). Metin iki kanıt yolunu da kapsayan gerçeği söyler: tur eklemek sonucu
            // değiştiremez.
            Assert.Contains("cycle A: no progress — another round could not change the result (2 members)",
                h.DecisionLog, StringComparison.Ordinal);
            Assert.Contains("non-convergence remembered at sig", h.DecisionLog, StringComparison.Ordinal);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    [Fact]
    public async Task a_converged_group_logs_its_verdict_without_a_signature()
    {
        // Kontrol grubu: yakınsayan grup da kararını YAZAR (aksi halde logda sessizce kaybolurdu), ama
        // hafızaya hiçbir imza yazılmadığı için satırın imza eki YOKTUR.
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((_, _) => Ok());
        using var h = new Harness(TwoMemberCycle(), invoker);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // [DEĞİŞEN KURAL — RESOLVE 3.4] Eski iddia: satır "cycle A: converged (2 members)" idi. Tur 1 artık yalnız
        // gereken üyeleri derlediği için üye sayısı tek başına grubun ne kadar iş yaptığını söylemez; satır derlenen
        // sayıyı da taşır. Bu grupta yüzey kanıtı ve üye terimi yok ⇒ üye kararı verilmez, herkes derlenir.
        Assert.Contains(CycleDecisionLines.Verdict("A", CycleRoundDecision.Converged, members: 2, compiled: 2, rememberedAt: null), h.DecisionLog,
            StringComparison.Ordinal);
        Assert.DoesNotContain("remembered at", h.DecisionLog, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- 11) CycleCompletedEvent [Task 3]

    /// <summary>[Task 3] decision.log'a yazılan karar, ekrana da TİPLİ bir event olarak düşer — kullanıcı bugüne
    /// dek koşunun NEDEN öyle bittiğini yalnız log dosyasından okuyabiliyordu. Yakınsayan grup: iki tur, sıfır
    /// son-tur başarısızlığı.</summary>
    [Fact]
    public async Task a_converged_group_emits_a_cycle_completed_event()
    {
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((_, _) => Ok());
        using var h = new Harness(TwoMemberCycle(), invoker);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var e = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
        Assert.Equal(Id("A"), e.ProjectId);          // lider — CycleRoundStartedEvent'in lideriyle AYNI
        Assert.Equal(CycleOutcome.Converged, e.Outcome);
        Assert.Equal(2, e.MemberCount);
        Assert.Equal(2, e.Rounds);
        Assert.Equal(0, e.FailedCount);               // son tur (2) hiçbir üye başarısız değildi
    }

    /// <summary>[Task 3] NoProgress: aynı küme iki tur üst üste patlıyor — son turun başarısız üye sayısı
    /// (burada yalnız B) sabit bir kanıttır, sonraki turların YOKLUĞU değil.</summary>
    [Fact]
    public async Task a_no_progress_group_emits_a_cycle_completed_event_with_its_failed_count()
    {
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, _) => name == "B" ? Exit(1) : Ok()); // aynı küme iki turdur patlıyor
        using var h = new Harness(TwoMemberCycle(), invoker);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var e = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
        Assert.Equal(Id("A"), e.ProjectId);
        Assert.Equal(CycleOutcome.NoProgress, e.Outcome);
        Assert.Equal(2, e.MemberCount);
        Assert.Equal(2, e.Rounds);
        Assert.Equal(1, e.FailedCount);               // yalnız B son turda da başarısız
    }

    /// <summary>[Task 3] Yarıda kesilen grupta HİÇBİR karar YOKTUR (decision hâlâ Continue) — decision.log'a
    /// hiçbir satır yazılmadığı gibi event de HİÇ yayılmaz.</summary>
    [Fact]
    public async Task an_interrupted_group_never_emits_a_cycle_completed_event()
    {
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, _, _) => name == "B"
            ? Task.FromException<MsBuildInvokeResult>(new OperationCanceledException())
            : Task.FromResult(Ok()));
        using var h = new Harness(TwoMemberCycle(), invoker);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Empty(h.Events.OfType<CycleCompletedEvent>());
    }

    // ---------------------------------------------------------------- 12) API kısa devresi (yüzey kanıtı)
    // Kaynak turlar arasında DEĞİŞMEZ; bir üyenin sonucunu yalnız OKUDUĞU grup-içi çıktı yüzeyinin değişmesi
    // değiştirebilir. Koordinatör her üyenin invoke ANINDA okuduğu kardeş yüzeylerini kaydeder, tur sonunda
    // güncel yüzeyle karşılaştırır ve CycleRoundPolicy'ye staleNow olarak verir: tur 2 yalnız bayat bağlanan
    // üyelere daralır, hiç bayat yoksa TEK turda kanıtlı Converged, girdisi oturmuş bir hata TEK turda
    // NoProgress. Yüzey bilgisi eksik/okunamaz ise grup bugünkü tam-tur davranışına düşer (aşağıda pinli).

    /// <summary>Sahte yüzey diski: üretici → çıktı yolu → o anki yüzey metni. Invoker script'i başarıyla
    /// "derlediği" üyenin yüzeyini buraya yazar; koordinatörün enjekte edilen <c>apiSurface</c>'ı buradan okur.
    /// Gerçek PE YOK — yüzey özetinin kendisi gerçek metadata ile <c>ApiSurfaceHashTests</c>'te pinlidir;
    /// burada pinlenen, koordinatörün o özetle kurduğu TUR kararlarıdır.</summary>
    internal sealed class SurfaceDisk
    {
        private readonly Dictionary<string, string> _byPath = new(StringComparer.OrdinalIgnoreCase);

        public static string PathOf(string name) => @"X:\surface\" + name + ".dll";

        /// <summary>Üyenin paylaşılan klasördeki kopyası (post-build'in beslediği, tüketicinin HintPath'i).</summary>
        public static string SharedPathOf(string name) => @"X:\shared\" + name + ".dll";

        public void Set(string name, string api) { lock (_byPath) _byPath[PathOf(name)] = api; }

        public void SetShared(string name, string api) { lock (_byPath) _byPath[SharedPathOf(name)] = api; }

        public string? Read(string path)
        { lock (_byPath) return _byPath.TryGetValue(path, out string? api) ? api : ApiSurfaceHash.Absent; }

        /// <summary>Verilen üyeler için kanıt yolu haritası — <see cref="IncrementalPlan.OutputsById"/>'a gider.</summary>
        public static IReadOnlyDictionary<string, ProjectOutputs> OutputsFor(params string[] names) =>
            names.ToDictionary(Id, n => new ProjectOutputs(PathOf(n), FedCandidates: []),
                StringComparer.OrdinalIgnoreCase);

        /// <summary>Kanıt yolu + paylaşılan kopya: Clean'in yalnız ilkini sildiği OSYS düzeni.</summary>
        public static IReadOnlyDictionary<string, ProjectOutputs> OutputsWithSharedCopies(params string[] names) =>
            names.ToDictionary(Id, n => new ProjectOutputs(PathOf(n), FedCandidates: [SharedPathOf(n)]),
                StringComparer.OrdinalIgnoreCase);
    }

    internal static RunPlan HashModePlan(RunPlan plan, params string[] names) =>
        plan with { Incremental = RunCoordinatorTests.Incremental(names) with { OutputsById = SurfaceDisk.OutputsFor(names) } };

    private static RunPlan SharedCopyPlan(RunPlan plan, params string[] names) =>
        plan with
        {
            Incremental = RunCoordinatorTests.Incremental(names)
                with { OutputsById = SurfaceDisk.OutputsWithSharedCopies(names) },
        };

    /// <summary>Derleyicinin MSBuild çıktısına yazdığı komut satırı (gerçek logdaki biçim: boşluklu yol
    /// tırnaklı). Koordinatör üyenin bir kardeşten hangi dosyayı okuduğunu buradan öğrenir.</summary>
    private static string CompilerLine(params string[] references) =>
        @"  C:\VS\MSBuild\Current\Bin\Roslyn\csc.exe /noconfig /nowarn:1701,1702 "
        + string.Join(" ", references.Select(r => r.Contains(' ') ? $"/reference:\"{r}\"" : "/reference:" + r))
        + @" /out:obj\Debug\Member.dll";

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — iki tur her zaman değil.]</b> Eski iddia "yakınsama iki ardışık yeşil turdur" idi ve
    /// KOŞULSUZDU; gövdesi değişip yüzeyi değişmeyen tipik commit'te ikinci tur, birincinin birebir tekrarıydı
    /// (gerçek OSYS'te 17 üyeli grupta tur ~8-14 dk ölçüldü). İkinci turun tek işlevi "tur 1'de eski nesil
    /// API'ye bağlanmış olabilir" şüphesini kapatmaktı — aynı şüphe yüzey karşılaştırmasıyla KANITLA kapanır:
    /// kimsenin okuduğu yüzey değişmediyse herkes NİHAİ API'lere bağlanmıştır. Kanıt gevşetilmedi,
    /// ucuzlatıldı; yüzey bilgisi olmayan grupta eski kural aynen yürürlükte (aşağıdaki fallback testleri).
    /// </summary>
    [Fact]
    public async Task a_green_group_whose_surfaces_did_not_change_converges_in_one_round()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            SeedGreen(store, "A");
            SeedGreen(store, "B");
            var disk = new SurfaceDisk();
            disk.Set("A", "a1");                              // eski nesil çıktılar diskte, yüzeyleri oturmuş
            disk.Set("B", "b1");
            var plan = HashModePlan(TwoMemberCycle(), "A", "B");
            var rec = new RoundRecorder();
            // Gövde değişti (yeniden derlendi) ama YÜZEY aynı kaldı — tipik "metot gövdesi düzeltildi" commit'i.
            var invoker = rec.Invoker((name, _) => { disk.Set(name, name == "A" ? "a1" : "b1"); return Ok(); });
            using var h = new Harness(plan, invoker, stateStore: store, apiSurface: disk.Read);

            await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["A#1", "B#1"], rec.Calls);          // ikinci tur SATIN ALINMADI
            var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
            Assert.Equal(CycleOutcome.Converged, completed.Outcome);
            Assert.Equal(1, completed.Rounds);
            // Kanıtlı yakınsama TAM yakınsamadır: persist edilir ve güvenilir raporlanır — unsettled DEĞİL.
            foreach (string name in new[] { "A", "B" })
            {
                Assert.Equal("sig", store.Load()[Id(name)].BuiltSignature);
                Assert.Equal(BuildResult.Succeeded, store.Load()[Id(name)].LastResult);
            }
            Assert.All(h.Events.OfType<ProjectSucceededEvent>(), e => Assert.True(e.Trusted));
            Assert.All(h.Events.OfType<ProjectSucceededEvent>(), e => Assert.False(e.CycleUnsettled));
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    [Fact] // Yüzeyi değişen üretici DEĞİL, onu ESKİ nesliyle okumuş TÜKETİCİLER ikinci turu öder — o kadar.
    public async Task only_the_consumers_of_a_changed_surface_pay_a_second_round()
    {
        var disk = new SurfaceDisk();
        disk.Set("A", "a1");
        disk.Set("B", "b-old");
        disk.Set("C", "c1");
        // Build-order A → B → C. A, B'yi turun BAŞINDA (eski nesil, b-old) okur; C ise B'den SONRA geldiği
        // için B'nin taze yüzeyini okur. B'nin yüzeyi bu turda değişir (b-old → b-new).
        var plan = HashModePlan(CyclePlanOf(["A", "B", "C"],
            Node("A", deps: ["B"], inCycle: true),
            Node("B", deps: ["C"], inCycle: true),
            Node("C", deps: ["A"], inCycle: true)), "A", "B", "C");
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, _) =>
        { disk.Set(name, name == "B" ? "b-new" : name == "A" ? "a1" : "c1"); return Ok(); });
        using var h = new Harness(plan, invoker, apiSurface: disk.Read);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // Tur 2 yalnız A'yı derler (b-old okumuştu); C, B'nin NİHAİ yüzeyini zaten okudu — ikinci tur ödemez.
        Assert.Equal(["A#1", "B#1", "C#1", "A#2"], rec.Calls);
        // Tur olayı O TURDA derlenecek üye sayısını taşır (alanın sözleşmesi) — şerit/konsol doğru sayıyı okur.
        Assert.Equal([3, 1], h.Events.OfType<CycleRoundStartedEvent>().Select(e => e.MemberCount));
        var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
        Assert.Equal(CycleOutcome.Converged, completed.Outcome);
        Assert.Equal(2, completed.Rounds);
        Assert.Equal(3, h.Events.OfType<ProjectSucceededEvent>().Count());
    }

    /// <summary>Kullanıcının "olmayacaksa devam etme"si: patlayan üyenin okuduğu HİÇBİR grup-içi yüzey
    /// değişmediyse aynı derleme aynı hatayı verir — NoProgress kararı TEK turda çıkar (eskiden aynı kümenin
    /// iki kez patlaması beklenirdi) ve yakınsamama hafızası aynen yazılır.
    /// <para><b>[DEĞİŞEN KURAL — D3]</b> Eski iddia: yakınsamayan grup persist ETMEZ — A <c>Failed</c>/<c>old</c> kalır, olay
    /// <c>Trusted=false</c>. Değişme gerekçesi: A'nın okuduğu yüzeyler son turda oturmuştu; kaydı doğrudur ve tek kardeşinin
    /// hatası onu zehirlemez (kullanıcı senaryosu 2026-10-08: tek copy-lock 16 kaydı düşürüyordu).</para></summary>
    [Fact]
    public async Task a_failure_whose_inputs_are_settled_is_no_progress_after_one_round()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            SeedGreen(store, "A");
            SeedGreen(store, "B");
            var disk = new SurfaceDisk();
            disk.Set("A", "a1");
            disk.Set("B", "b1");
            var plan = HashModePlan(TwoMemberCycle(), "A", "B");
            var rec = new RoundRecorder();
            // A yüzeyini değiştirmeden yeşil biter; B, A'nın NİHAİ yüzeyine karşı derlenmişken patlar.
            var invoker = rec.Invoker((name, _) =>
            {
                if (name == "B") return Exit(1);              // başarısız üye yeni çıktı YAZMAZ
                disk.Set("A", "a1");
                return Ok();
            });
            using var h = new Harness(plan, invoker, stateStore: store, apiSurface: disk.Read);

            await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["A#1", "B#1"], rec.Calls);          // ikinci tur HİÇ açılmadı
            var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
            Assert.Equal(CycleOutcome.NoProgress, completed.Outcome);
            Assert.Equal(1, completed.Rounds);
            Assert.Equal(1, completed.FailedCount);
            // [D3] Oturmuş yeşil üye güvenilir persist edilir; hafıza yine yazılır (yalnız raporlar) — iki alan bağımsız.
            Assert.Equal(BuildResult.Succeeded, store.Load()[Id("A")].LastResult);
            Assert.Equal("sig", store.Load()[Id("A")].BuiltSignature);
            Assert.Equal("sig", store.Load()[Id("A")].NonConvergentSignature);
            Assert.Equal("sig", store.Load()[Id("B")].NonConvergentSignature);
            var succeeded = Assert.Single(h.Events.OfType<ProjectSucceededEvent>());
            Assert.True(succeeded.Trusted);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    [Fact] // Patlayan üyenin girdisi DEĞİŞTİYSE bir tur daha hak eder — ve turlar yalnız gerekeni derler.
    public async Task a_stale_failure_gets_another_round_and_the_group_still_converges_selectively()
    {
        var disk = new SurfaceDisk();
        disk.Set("A", "a-old");
        disk.Set("B", "b-old");
        // Build-order B → A: B, A'yı ESKİ nesliyle okur (a-old). A'nın yüzeyi tur 1'de değişir (a-old → a-new).
        var plan = HashModePlan(CyclePlanOf(["B", "A"],
            Node("B", deps: ["A"], inCycle: true),
            Node("A", deps: ["B"], inCycle: true)), "A", "B");
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, round) =>
        {
            if (name == "A") { disk.Set("A", "a-new"); return Ok(); }
            if (round == 1) return Exit(1);                   // B, a-old'a karşı patlar
            disk.Set("B", "b-new");                           // a-new'e karşı düzelir
            return Ok();
        });
        using var h = new Harness(plan, invoker, apiSurface: disk.Read);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // Tur 1: B (patlar) + A (yüzeyi değişir). Tur 2: yalnız B (bayat + başarısız) — düzelir, yüzeyi değişir.
        // Tur 3: yalnız A (B'yi b-old ile okumuştu). Kimse dördüncü kez derlenmez; tavanda kanıtlı Converged.
        Assert.Equal(["B#1", "A#1", "B#2", "A#2"], rec.Calls);
        Assert.Equal([2, 1, 1], h.Events.OfType<CycleRoundStartedEvent>().Select(e => e.MemberCount));
        var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
        Assert.Equal(CycleOutcome.Converged, completed.Outcome);
        Assert.Equal(3, completed.Rounds);
        Assert.Equal(2, h.Events.OfType<ProjectSucceededEvent>().Count());
        Assert.Empty(h.Events.OfType<ProjectFailedEvent>());  // B'nin tur-1 hatası ARA sonuçtur, yayılmaz
    }

    // ---------------------------------------------------------------- 12b) kısa devrenin düştüğü yerler

    [Fact] // Kanıt YARIM olmaz: tek bir üyenin bile çıktı kanıtı yoksa grup bugünkü tam-tur davranışında kalır.
    public async Task a_member_without_output_evidence_keeps_the_group_on_full_rounds()
    {
        var disk = new SurfaceDisk();
        disk.Set("A", "a1");                                  // B için kanıt yolu YOK (OutputsFor yalnız A)
        var plan = TwoMemberCycle() with
        { Incremental = RunCoordinatorTests.Incremental("A", "B") with { OutputsById = SurfaceDisk.OutputsFor("A") } };
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, _) => { if (name == "A") disk.Set("A", "a1"); return Ok(); });
        using var h = new Harness(plan, invoker, apiSurface: disk.Read);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls); // eski kural aynen: iki ardışık yeşil tur
        Assert.Equal(CycleOutcome.Converged, Assert.Single(h.Events.OfType<CycleCompletedEvent>()).Outcome);
    }

    [Fact] // Okunamayan yüzey (kilitli/bozuk dosya) kanıt DEĞİLDİR — grup tam-tur davranışına düşer, karar değişmez.
    public async Task an_unreadable_surface_falls_back_to_full_rounds()
    {
        var disk = new SurfaceDisk();
        disk.Set("A", "a1");
        disk.Set("B", "b1");
        var plan = HashModePlan(TwoMemberCycle(), "A", "B");
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((_, _) => Ok());
        using var h = new Harness(plan, invoker,
            apiSurface: path => path.Contains("B", StringComparison.OrdinalIgnoreCase) ? null : disk.Read(path));

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls);
        Assert.Equal(CycleOutcome.Converged, Assert.Single(h.Events.OfType<CycleCompletedEvent>()).Outcome);
    }

    /// <summary>
    /// [PERF Faz E2] Grup başı yüzey hash'i üreticiler arasında PARALELDİR (derece: <c>IoParallelism.Degree</c>).
    /// Eskiden üreticiler SIRAYLA okunurdu ve gerçek bir Resolve'da grup başı hash turun önünde tek başına
    /// saniyeler tutuyordu. Sahte yüzey her çağrıda en çok 50 ms bekler; bekleme BAŞKA bir çağrının içeride olmasıyla
    /// biter (sabit uyku değil, koşul + tavan [D8]): sıralı okumada hiçbir çağrı eşini görmez ve sekiz üreticinin
    /// hash'i 8×50 ms'yi bulur, paralel okumada çağrılar buluşur ve beklemez. Süre E1'in grup başlığından okunur.
    ///
    /// <para><b><c>LocalOnly</c>:</b> buluşma, iş parçacığı havuzunun paralel döngüye 50 ms içinde İKİNCİ bir thread
    /// vermesine bağlıdır. Paylaşılan CI runner'ında (4 vCPU, süitin geri kalanı yanında) havuz bunu veremedi ve test
    /// "never read two producers at once" ile düştü (develop CI koşusu 37731072772; ne bu test ne grup başı hash kodu o
    /// koşuda değişmişti). Aynı hata lokalde süreç tek çekirdeğe sabitlenince birebir üretildi (<c>start /affinity 1</c>),
    /// tüm çekirdeklerle geçti — kusur kodda değil, ortamın paralellik kapasitesinde. Eşik GEVŞETİLMEZ (CLAUDE.md):
    /// yalnız bu metot CI filtresinden çıkar, lokal tam süit (yayının kapısı) onu koşturmaya devam eder.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "LocalOnly")]
    public async Task the_group_start_surface_hash_reads_the_producers_in_parallel()
    {
        const int Producers = 8;
        var ceiling = TimeSpan.FromMilliseconds(50);
        string[] names = [.. Enumerable.Range(1, Producers).Select(i => "P" + i)];
        // Halka: P1 → P2 → … → P8 → P1 — her üye bir kardeşin bağımlılığıdır, yani sekizi de üreticidir.
        var plan = HashModePlan(CyclePlanOf(names,
            names.Select((name, i) => Node(name, deps: [names[(i + 1) % Producers]], inCycle: true)).ToArray()), names);
        var disk = new SurfaceDisk();
        int invoked = 0, inside = 0, peak = 0;
        using var met = new ManualResetEventSlim(false);
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((_, _) => { Interlocked.Exchange(ref invoked, 1); return Ok(); });
        using var h = new Harness(plan, invoker, apiSurface: path =>
        {
            if (Volatile.Read(ref invoked) == 0) // yalnız grup başı hash'i: ilk invoke'tan önce
            {
                int now = Interlocked.Increment(ref inside);
                int seen;
                while (now > (seen = Volatile.Read(ref peak)) && Interlocked.CompareExchange(ref peak, now, seen) != seen) { }
                if (now > 1) met.Set();
                met.Wait(ceiling);
                Interlocked.Decrement(ref inside);
            }
            return disk.Read(path);
        });

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.True(Volatile.Read(ref peak) > 1, "the group-start hash never read two producers at once");
        // Süre E1'in grup başlığından, testlerin TEK satır eşleştiricisiyle okunur (CycleDecisionLogTests.MsOf).
        long groupStartHashMs = CycleDecisionLogTests.MsOf(h.DecisionLog,
            "cycle P1: 8 members, 8 producers, evidence on, hash {ms} ms");
        Assert.True(groupStartHashMs < Producers * (long)ceiling.TotalMilliseconds,
            $"the group-start hash took {groupStartHashMs} ms");
    }

    /// <summary>
    /// [PERF Faz E2] Derleme sonrası yüzey hash'i MSBuild sırasını (slotu) TUTMAZ: üye derlemesi bitince slotu
    /// bırakır, hash'i SONRA okur — sırada bekleyen seviye arkadaşı o arada derlemeye başlar. Eskiden hash slot
    /// tutularak okunurdu; büyük bir üreticinin okunması koşunun MSBuild kapasitesinden düşerdi. "Bitti, grubunu
    /// bekliyor" ilanı (<see cref="CycleMemberHeldEvent"/>) yine slot bırakılmadan ÖNCE yazılır. Paralellik 1:
    /// yıldızın üç uydusu aynı seviyede, tek slot için sıradadır. İlk uydunun derleme sonrası hash'i en çok 200 ms
    /// bekler; bekleme başka bir uydunun derlemeye başlamasıyla biter (koşul + tavan [D8]) — slot tutulsaydı o uydu
    /// hash bitmeden başlayamazdı.
    /// </summary>
    [Fact]
    public async Task the_post_compile_surface_hash_runs_after_the_build_slot_is_released()
    {
        var disk = StableStarDisk();
        var plan = HashModePlan(StarCycle(), "Hub", "S1", "S2", "S3");
        int satelliteStarts = 0, firstHashTaken = 0;
        bool overlapped = false;
        var compiled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var anotherStarted = new ManualResetEventSlim(false);
        var rec = new RoundRecorder();
        var invoker = rec.Invoker(async (name, round, _) =>
        {
            if (round == 1 && name.StartsWith('S') && Interlocked.Increment(ref satelliteStarts) >= 2)
                anotherStarted.Set();
            await Task.Yield(); // gerçek derleme gibi askıda: seviye arkadaşları slot sırasına girer
            lock (compiled) compiled.Add(name);
            return Ok();
        });
        using var h = new Harness(plan, invoker, apiSurface: path =>
        {
            string name = Path.GetFileNameWithoutExtension(path);
            bool postCompile;
            lock (compiled) postCompile = compiled.Contains(name);
            if (postCompile && name.StartsWith('S') && Interlocked.Exchange(ref firstHashTaken, 1) == 0)
                Volatile.Write(ref overlapped, anotherStarted.Wait(TimeSpan.FromMilliseconds(200)));
            return disk.Read(path);
        });

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.True(Volatile.Read(ref overlapped),
            "no level mate could start while the first satellite's surface was hashed — the hash held the build slot");
        // İlan sırası korunur: ilk uydunun "bitti" ilanı, sıradaki uydunun "derleniyor" ilanından ÖNCE yazılır.
        var events = h.Events.ToList();
        string first = events.OfType<ProjectStartedEvent>().First(e => NameOf(e.ProjectId).StartsWith('S')).ProjectId;
        int held = events.FindIndex(e => e is CycleMemberHeldEvent m && m.ProjectId == first);
        int next = events.FindIndex(e => e is ProjectStartedEvent s && s.ProjectId != first && NameOf(s.ProjectId).StartsWith('S'));
        Assert.True(held >= 0 && held < next, $"held={held}, next={next}");
        Assert.Equal(CycleOutcome.Converged, Assert.Single(h.Events.OfType<CycleCompletedEvent>()).Outcome);
    }

    // ---------------------------------------------------------------- 12c) okunan dosya kanıtı
    // Bir kardeşin çıktısı birden çok dosyada durur (kendi bin'i + post-build'in beslediği paylaşılan kopyalar).
    // Tüketicinin gerçekte hangisini okuduğunu derleyicinin komut satırı kesin söyler; tur sonu kararı o dosyaya
    // bakar. Komut satırı yoksa, derleme başarısızsa ya da okunan dosya bilinen kopyalardan biri değilse karar
    // eskisi gibi TÜM kopyaların toplamına bakar (aşağıdaki koruma testleri).

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — okunan dosya kanıtı.]</b> Eski kural: bir kardeşin yüzeyi, kanıt yolu ile beslenen
    /// kopyaların TOPLAMIYDI; tüketici hangisini okursa okusun ikisi birlikte izlenirdi. Clean üreticinin kendi
    /// bin çıktısını silip paylaşılan kopyaya dokunmadığı için "yok → var" geçişi tüketiciyi bayat sayıyor, API'si
    /// hiç değişmemiş grup ikinci turu ödüyordu. Sahada ölçüldü: Clean sonrası Resolve'da 17 üyeli UI grubunun
    /// 14 üyesi ~111 sn boyunca ikinci kez derlendi; hepsi kardeşi paylaşılan kopyadan okuyordu.
    /// </summary>
    [Fact]
    public async Task after_a_clean_a_group_that_compiled_against_the_shared_copies_converges_in_one_round()
    {
        var disk = new SurfaceDisk();
        disk.SetShared("A", "a1");                            // Clean: kendi bin çıktıları yok, kopyalar duruyor
        disk.SetShared("B", "b1");
        var plan = SharedCopyPlan(TwoMemberCycle(), "A", "B");
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, _, onLine, _) =>
        {
            onLine(CompilerLine(SurfaceDisk.SharedPathOf(name == "A" ? "B" : "A"))); // kardeşi kopyasından okudu
            string api = name == "A" ? "a1" : "b1";           // gövde yeniden derlendi, yüzey aynı
            disk.Set(name, api);                              // kendi bin çıktısı geri geldi
            disk.SetShared(name, api);                        // post-build kopyası
            return Task.FromResult(Ok());
        });
        using var h = new Harness(plan, invoker, apiSurface: disk.Read);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A#1", "B#1"], rec.Calls);              // okunan kopya değişmedi: ikinci tur yok
        var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
        Assert.Equal(CycleOutcome.Converged, completed.Outcome);
        Assert.Equal(1, completed.Rounds);
    }

    [Fact] // Komut satırı yoksa (derleyici koşmadı ya da satır okunamadı) hangi dosyanın okunduğu bilinmez: eski kural.
    public async Task without_a_compiler_line_a_reader_is_still_judged_on_every_copy()
    {
        var disk = new SurfaceDisk();
        disk.SetShared("A", "a1");
        disk.SetShared("B", "b1");
        var plan = SharedCopyPlan(TwoMemberCycle(), "A", "B");
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, _) =>
        {
            string api = name == "A" ? "a1" : "b1";
            disk.Set(name, api);
            disk.SetShared(name, api);
            return Ok();
        });
        using var h = new Harness(plan, invoker, apiSurface: disk.Read);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // A, B'nin kendi çıktısı yokken derlendi; o dosya sonra geldi: A ikinci turu öder.
        Assert.Equal(["A#1", "B#1", "A#2"], rec.Calls);
        Assert.Equal(CycleOutcome.Converged, Assert.Single(h.Events.OfType<CycleCompletedEvent>()).Outcome);
    }

    /// <summary>Kanıt tahmin değil, derleyicinin okuduğu dosyadır: A, B'yi kendi bin çıktısından okudu
    /// (ör. ProjectReference) ve B'nin derlemesi o dosyanın API'sini değiştirdi — paylaşılan kopya değişmese de
    /// A bayattır. csproj'daki HintPath'e bakıp kopyayı izleyen bir kural bunu kaçırırdı.</summary>
    [Fact]
    public async Task a_reader_that_compiled_against_the_siblings_own_output_is_judged_on_that_file()
    {
        var disk = new SurfaceDisk();
        disk.Set("A", "a1");
        disk.SetShared("A", "a1");
        disk.Set("B", "b-old");
        disk.SetShared("B", "b-lib");                         // B'nin derlemesinin tazelemediği bir kopya
        var plan = SharedCopyPlan(TwoMemberCycle(), "A", "B");
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, _, onLine, _) =>
        {
            if (name == "A")
            {
                onLine(CompilerLine(SurfaceDisk.PathOf("B")));
                disk.Set("A", "a1");
                disk.SetShared("A", "a1");
            }
            else
            {
                onLine(CompilerLine(SurfaceDisk.SharedPathOf("A")));
                disk.Set("B", "b-new");                       // yalnız kendi çıktısı değişir
            }
            return Task.FromResult(Ok());
        });
        using var h = new Harness(plan, invoker, apiSurface: disk.Read);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A#1", "B#1", "A#2"], rec.Calls);
        Assert.Equal(CycleOutcome.Converged, Assert.Single(h.Events.OfType<CycleCompletedEvent>()).Outcome);
    }

    /// <summary>Başarısız üye kardeşlerinin TÜM kopyalarıyla yargılanır: Clean sonrası patlayan A'nın bir tur
    /// daha hakkı kalır (B'nin kendi çıktısı bu turda geldi). Okunan kopyaya daraltılsaydı A "girdisi oturmuş
    /// hata" sayılır, grup ilk turda no progress olur ve A kanıtlı kırmızı yanardı.</summary>
    [Fact]
    public async Task a_failed_reader_is_still_judged_on_every_copy_of_its_sibling()
    {
        var disk = new SurfaceDisk();
        disk.SetShared("A", "a1");
        disk.SetShared("B", "b1");
        var plan = SharedCopyPlan(TwoMemberCycle(), "A", "B");
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, round, onLine, _) =>
        {
            onLine(CompilerLine(SurfaceDisk.SharedPathOf(name == "A" ? "B" : "A")));
            if (name == "A" && round == 1) return Task.FromResult(Exit(1)); // başarısız üye çıktı yazmaz
            string api = name == "A" ? "a1" : "b1";
            disk.Set(name, api);
            disk.SetShared(name, api);
            return Task.FromResult(Ok());
        });
        using var h = new Harness(plan, invoker, apiSurface: disk.Read);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A#1", "B#1", "A#2"], rec.Calls.Take(3));
        Assert.Equal(CycleOutcome.Converged, Assert.Single(h.Events.OfType<CycleCompletedEvent>()).Outcome);
        Assert.Empty(h.Events.OfType<ProjectFailedEvent>());
    }

    [Fact] // Derleyici kardeşin adını taşıyan ama bilinen kopyalardan olmayan bir dosya okuduysa: eski kural.
    public async Task a_reference_outside_the_known_copies_keeps_the_reader_on_every_copy()
    {
        var disk = new SurfaceDisk();
        disk.SetShared("A", "a1");
        disk.SetShared("B", "b1");
        var plan = SharedCopyPlan(TwoMemberCycle(), "A", "B");
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, _, onLine, _) =>
        {
            onLine(CompilerLine(@"X:\elsewhere\" + (name == "A" ? "B" : "A") + ".dll"));
            string api = name == "A" ? "a1" : "b1";
            disk.Set(name, api);
            disk.SetShared(name, api);
            return Task.FromResult(Ok());
        });
        using var h = new Harness(plan, invoker, apiSurface: disk.Read);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A#1", "B#1", "A#2"], rec.Calls);
    }

    // ---------------------------------------------------------------- 13) restore-once

    // ---------------------------------------------------------------- 14) umutsuz üyenin hatası KANITTIR

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — suçlu artık kırmızı.]</b> Eski kural: yakınsamayan grupta HİÇBİR sonuca güvenilmez,
    /// <c>exit N</c> ile patlayan üye bile kanıtsızdır — hatası bayat bir kardeş DLL'inden kaynaklanıyor
    /// olabilirdi ve araç bunu AYIRT EDEMİYORDU (satır kırmızı olsa bir sonraki Sync griye çevirirdi).
    /// Yüzey kanıtı belirsizliği kaldırdı: okuduğu her grup-içi yüzey OTURMUŞKEN derleyici hatası veren üyenin
    /// girdileri bir sonraki denemede de BİREBİR aynı olacak — hatası sıradan bir Build hatasıyla aynı kalitede
    /// kanıttır. Böyle bir üye artık kanıtlı FAILED'dır: olay <c>Evidence=true</c> taşır (satır kırmızı, etiket
    /// <c>failed</c>) ve defter <c>FailedSignature</c> yazar (karar Sync/restart sonrası da AYNI kalır —
    /// sahada kullanıcı suçluyu ekrandan bulamıyordu, iki üye tıpatıp aynı görünüyordu).
    /// <b>[DEĞİŞEN KURAL — D3]</b> Eski iddia: suçsuz yeşil eş kanıtsız invalidate edilirdi (gri never built, tek Resolve ile
    /// geri gelirdi). Değişme gerekçesi: eşin okuduğu yüzeyler oturmuştu — kaydı doğrudur, yeşil kalır; bir sonraki koşu
    /// yalnız suçluyu derler.
    /// </summary>
    [Fact]
    public async Task a_hopeless_members_failure_is_evidence_and_its_settled_green_sibling_is_trusted()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            SeedGreen(store, "A");
            SeedGreen(store, "B");
            var disk = new SurfaceDisk();
            disk.Set("A", "a1");
            disk.Set("B", "b1");
            var plan = HashModePlan(TwoMemberCycle(), "A", "B");
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((name, _) =>
            {
                if (name == "B") return Exit(1);              // suçlu: nihai yüzeye karşı derleyici hatası
                disk.Set("A", "a1");                          // suçsuz eş: yeşil, yüzeyi oturmuş
                return Ok();
            });
            using var h = new Harness(plan, invoker, stateStore: store, apiSurface: disk.Read);

            await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["A#1", "B#1"], rec.Calls);
            // Suçlu: kanıtlı kırmızı — olay VE defter aynı kapıdan.
            var failed = Assert.Single(h.Events.OfType<ProjectFailedEvent>());
            Assert.Equal(Id("B"), failed.ProjectId);
            Assert.True(failed.Evidence);
            var culprit = store.Load()[Id("B")];
            Assert.Equal("sig", culprit.FailedSignature);     // bir sonraki Sync LastFailed okur → etiket `failed`
            Assert.NotNull(culprit.FailedAt);
            Assert.Equal("sig", culprit.NonConvergentSignature); // grup hafızası AYRICA yazılır — iki alan bağımsız
            // Suçsuz eş: yüzeyleri oturmuş yeşil üye — güvenilir başarı, taze kayıt, üç döngü alanı yazılı.
            var green = Assert.Single(h.Events.OfType<ProjectSucceededEvent>());
            Assert.Equal(Id("A"), green.ProjectId);
            Assert.True(green.Trusted);
            var sibling = store.Load()[Id("A")];
            Assert.Equal((BuildResult.Succeeded, "sig"), (sibling.LastResult, sibling.BuiltSignature));
            Assert.Null(sibling.FailedSignature);
            Assert.NotNull(sibling.CycleReadSurfaces);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    [Fact] // Kanıt ÜYEYE özeldir: girdisi DEĞİŞMİŞ başarısız üye kanıt almaz — bir tur daha onu düzeltebilirdi.
    public async Task a_stale_failed_member_carries_no_evidence_even_when_the_group_is_hopeless()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            var disk = new SurfaceDisk();
            disk.Set("A", "a-old");
            disk.Set("B", "b1");
            disk.Set("D", "d1");
            // Build-order D → A → B. D, A'yı ESKİ nesliyle okur (a-old) ve patlar; A yeşildir ama yüzeyi
            // DEĞİŞİR (a-old → a-new) ⇒ D bayat (bir tur daha hak ederdi). B, A'nın NİHAİ yüzeyini (a-new)
            // okuyup patlar ⇒ umutsuz — grubun kaderini B belirler (NoProgress, tur 1).
            // B'nin D'yi de okuması fikstürün parçasıdır: dalga planı en çok okunanı öne alır; D, A kadar
            // okunmasa A önce derlenir ve D taze a-new'i okurdu — senaryonun "bayat okuyan" üyesi kalmazdı.
            var plan = HashModePlan(CyclePlanOf(["D", "A", "B"],
                Node("D", deps: ["A"], inCycle: true),
                Node("A", deps: ["D", "B"], inCycle: true),
                Node("B", deps: ["A", "D"], inCycle: true)), "A", "B", "D");
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((name, _) =>
            {
                if (name != "A") return Exit(1);
                disk.Set("A", "a-new");
                return Ok();
            });
            using var h = new Harness(plan, invoker, stateStore: store, apiSurface: disk.Read);

            await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["D#1", "A#1", "B#1"], rec.Calls);   // NoProgress tur 1'de — B umutsuz
            var failedById = h.Events.OfType<ProjectFailedEvent>().ToDictionary(e => e.ProjectId);
            Assert.True(failedById[Id("B")].Evidence);        // nihai yüzeye karşı patladı: kanıt
            Assert.False(failedById[Id("D")].Evidence);       // bayat yüzeye karşı patladı: kanıt DEĞİL
            Assert.Equal("sig", store.Load()[Id("B")].FailedSignature);
            Assert.Null(store.Load()[Id("D")].FailedSignature);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    [Fact] // Yüzey kanıtı YOKKEN eski kural aynen sürer: suçlu ayırt edilemez, exit N bile kanıtsız kalır.
    public async Task a_no_progress_group_without_surface_info_still_writes_no_failure_evidence()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            SeedGreen(store, "A");
            SeedGreen(store, "B");
            var plan = TwoMemberCycle() with { Incremental = RunCoordinatorTests.Incremental("A", "B") };
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((name, _) => name == "B" ? Exit(1) : Ok());
            using var h = new Harness(plan, invoker, stateStore: store);

            await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls); // eski iki-tur kanıtı
            Assert.False(Assert.Single(h.Events.OfType<ProjectFailedEvent>()).Evidence);
            Assert.Null(store.Load()[Id("B")].FailedSignature);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    // ---------------------------------------------------------------- 16) seviyeli paralellik

    /// <summary>Yıldız SCC: Hub ↔ S1/S2/S3 — uydular yalnız Hub'ı okur, birbirine komşu değiller.
    /// Seviye planı [Hub], [S1,S2,S3]'tür (CycleRoundLevelsTests'te pinli).</summary>
    private static RunPlan StarCycle() => CyclePlanOf(["Hub", "S1", "S2", "S3"],
        Node("Hub", deps: ["S1", "S2", "S3"], inCycle: true),
        Node("S1", deps: ["Hub"], inCycle: true),
        Node("S2", deps: ["Hub"], inCycle: true),
        Node("S3", deps: ["Hub"], inCycle: true));

    private static SurfaceDisk StableStarDisk()
    {
        var disk = new SurfaceDisk();
        foreach (string name in new[] { "Hub", "S1", "S2", "S3" }) disk.Set(name, name + "-api");
        return disk;
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — tur içi seviyeli paralellik.]</b> Eski kural üyeleri KOŞULSUZ sıralı derliyordu;
    /// gerekçesi ("A, B.dll'i okurken B aynı dosyayı yazıyor olurdu") yalnız DOĞRUDAN kenar komşuları için
    /// geçerlidir. Sahada ölçüldü: 17 üyeli UI grubunun iç grafında kritik yol 6 — tur, 17 ardışık derleme
    /// yerine ~6 bariyerli dalgada koşabilirken 3 worker boş bekliyordu (~98 sn tek worker'da). Üyeler artık
    /// <c>CycleRoundLevels</c>'ın bariyerli seviyeleriyle derlenir: komşu olmayan üyeler AYNI seviyede
    /// eşzamanlı, komşular asla (torn read yapısal olarak imkânsız — bariyerler örtüşmez). Turun anlamı,
    /// durma kuralları, stop sözleşmesi ve raporlama değişmez.
    /// </summary>
    [Fact]
    public async Task satellites_of_one_level_compile_concurrently_between_hub_barriers()
    {
        var disk = StableStarDisk();
        var plan = HashModePlan(StarCycle(), "Hub", "S1", "S2", "S3");
        var trio = Signal();
        int arrived = 0;
        var rec = new RoundRecorder();
        // Üç uydu BİRBİRİNİ bekler: eşzamanlılık deterministik kanıtlanır (sıralı bir uygulamada ilk uydu
        // diğerleri hiç başlamadığı için sonsuza dek bekler → Limit testi hataya düşürür; sleep/poll yok [D8]).
        var invoker = rec.Invoker(async (name, _, _) =>
        {
            if (name == "Hub") return Ok();
            if (Interlocked.Increment(ref arrived) >= 3) trio.TrySetResult();
            await trio.Task;
            return Ok();
        });
        using var h = new Harness(plan, invoker, apiSurface: disk.Read);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 4), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // Bariyer: Hub uydulardan ÖNCE ve TEK BAŞINA; uydular üçü BİRDEN uçuşta; yüzeyler oturduğu için tek tur.
        Assert.Equal(4, rec.Calls.Count);
        Assert.Equal("Hub#1", rec.Calls[0]);
        Assert.Equal(["S1#1", "S2#1", "S3#1"], rec.Calls.Skip(1).Order(StringComparer.Ordinal));
        Assert.Equal(3, invoker.MaxConcurrent);
        var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
        Assert.Equal(CycleOutcome.Converged, completed.Outcome);
        Assert.Equal(1, completed.Rounds);
        Assert.Equal(4, h.Events.OfType<ProjectSucceededEvent>().Count());
        Assert.All(h.Events.OfType<ProjectSucceededEvent>(), e => Assert.True(e.Trusted));
    }

    [Fact] // KONTROL: seviye eşzamanlılığı koşunun paralellik tavanına uyar — grup tek worker'da diye tavan aşılmaz.
    public async Task level_concurrency_respects_the_run_parallelism_cap()
    {
        var disk = StableStarDisk();
        var plan = HashModePlan(StarCycle(), "Hub", "S1", "S2", "S3");
        var rec = new RoundRecorder();
        var invoker = rec.Invoker(async (_, _, _) => { await Task.Yield(); return Ok(); });
        using var h = new Harness(plan, invoker, apiSurface: disk.Read);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 2), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.True(invoker.MaxConcurrent <= 2,
            $"parallelism 2 iken {invoker.MaxConcurrent} eşzamanlı invoke gözlendi");
        Assert.Equal(CycleOutcome.Converged, Assert.Single(h.Events.OfType<CycleCompletedEvent>()).Outcome);
    }

    [Fact] // [en çok okunan önce] Build-order'da sonda duran merkez ilk dalgada derlenir; okuyucuları taze çıktıyı okur.
    public async Task the_most_read_member_compiles_first_even_when_it_comes_last_in_build_order()
    {
        var plan = CyclePlanOf(["S1", "S2", "Hub"],
            Node("S1", deps: ["Hub"], inCycle: true),
            Node("S2", deps: ["Hub"], inCycle: true),
            Node("Hub", deps: ["S1", "S2"], inCycle: true));
        var rec = new RoundRecorder();
        using var h = new Harness(plan, rec.Invoker((_, _) => Ok()));

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["Hub#1", "S1#1", "S2#1"], rec.Calls.Take(3));
    }

    /// <summary>
    /// [paylaşılan kopya çakışması] Adı bir başka projenin adının noktalı öneki olan üye ("Sales" ↔ "Sales.Print"),
    /// yaygın <c>copy $(TargetName).*</c> post-build'iyle o projenin paylaşılan kopyasını da yeniden yazar (sahada:
    /// UI.General → UI.General.Common, UI.NewSales → NewSales.Stock/Pricing). Kopya yazılırken aynı dosyayı okuyan
    /// derleyici ya kilide takılır ya yarım dosya görür; kenar olmasa da ikisi aynı dalgada derlenmez.
    /// </summary>
    [Fact]
    public async Task a_member_that_may_rewrite_a_copy_another_member_reads_never_compiles_beside_it()
    {
        var plan = CyclePlanOf(["Hub", "Sales", "Report"],
            Node("Sales.Print"),
            Node("Hub", deps: ["Sales", "Report"], inCycle: true),
            Node("Sales", deps: ["Hub"], inCycle: true),
            Node("Report", deps: ["Hub", "Sales.Print"], inCycle: true));
        var rec = new RoundRecorder();
        // Gerçek bir await noktası: aynı dalgadaki iki üye birlikte uçuşa girer ve MaxConcurrent bunu yakalar.
        var invoker = rec.Invoker(async (_, _, _) => { await Task.Yield(); return Ok(); });
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 4), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(1, invoker.MaxConcurrent);                // Sales ile Report ayrı dalgalarda
        Assert.Equal(4, Assert.IsType<RunCompletedEvent>(h.Events[^1]).Succeeded);
    }

    // ---------------------------------------------------------------- 15) grup koşullu atlama

    /// <summary>Up kökü kırık; A↔B üyelerinin İKİSİ de yalnız "kökü bekliyor" (WaitingForDependency + defter
    /// notunda kök Up). Ledger + plan bu durumu kurar; Up bu koşuda yine patlar.</summary>
    private static (BuildStateStore Store, RunPlan Plan) WaitingCyclePlan(string cacheRoot,
        WillBuildReason bReason = WillBuildReason.WaitingForDependency)
    {
        var store = new BuildStateStore(cacheRoot);
        store.Upsert(new BuildState(Id("Up"), "up-sig", LastResult: BuildResult.Failed,
            LastRunAt: DateTimeOffset.UtcNow.AddDays(-1)));
        store.Upsert(new BuildState(Id("A"), "old", "sha", BuildResult.Succeeded,
            DateTimeOffset.UtcNow.AddDays(-1), DepIssue: true, DepIssueRoots: [Id("Up")]));
        store.Upsert(new BuildState(Id("B"), "old", "sha", BuildResult.Succeeded,
            DateTimeOffset.UtcNow.AddDays(-1), DepIssue: true, DepIssueRoots: [Id("Up")]));
        var plan = CyclePlanOf(["A", "B"],
            Node("Up", willBuild: true) with { WillBuildReason = WillBuildReason.LastFailed },
            Node("A", deps: ["Up", "B"], inCycle: true, willBuild: true)
                with { WillBuildReason = WillBuildReason.WaitingForDependency },
            Node("B", deps: ["A"], inCycle: true, willBuild: true) with { WillBuildReason = bReason })
            with { Incremental = RunCoordinatorTests.Incremental("Up", "A", "B") };
        return (store, plan);
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — grup koşullu atlama.]</b> Eski kural: bir SCC üyesi ASLA koşullu değerlendirilmez
    /// ("bir üyeyi atlayıp diğerlerini derlemek grubu yarım bırakırdı") — dolayısıyla kökü kırık diye bekleyen
    /// bir grup, kök düzelmeden de HER Resolve basışında baştan derlenirdi. Sahada ölçüldü: PRM kökü kırıkken
    /// 17 üyeli UI grubu her basışta ~98 sn boşuna yeniden derleniyordu — üyeler aynı bayat köke yeniden
    /// link'lenmekten başka hiçbir şey kazanmıyordu. Tekil projenin kuralı (§8.3: kök düzelince derle, hâlâ
    /// kırıksa atla) GRUBUN TAMAMINA atomik uygulanınca "yarım grup" itirazı ortadan kalkar: ya herkes atlanır
    /// ya herkes derlenir. Defter kayıtlarına DOKUNULMAZ — kök düzeldiği ilk koşuda grup normal derlenir
    /// (aşağıdaki kontrol testi). [Build cycle derler] Build de grubu derlediği için aynı kural Build'de de geçerlidir;
    /// Rebuild'de geçerli DEĞİLDİR (<see cref="a_rebuild_compiles_a_group_waiting_for_a_still_failing_root"/>).
    /// </summary>
    [Theory]
    [InlineData(RunMode.Cycles)]
    [InlineData(RunMode.Build)]
    public async Task a_cycle_group_only_waiting_for_a_still_failing_root_is_skipped_without_a_single_round(RunMode mode)
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var (store, plan) = WaitingCyclePlan(cacheRoot);
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((name, _) => name == "Up" ? Exit(1) : Ok()); // kök yine patlıyor
            using var h = new Harness(plan, invoker, stateStore: store);

            await h.Sut.StartAsync(Start(mode, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["Up#1"], rec.Calls);                 // grup HİÇ derlenmedi — tek tur bile yok
            Assert.Empty(h.Events.OfType<CycleRoundStartedEvent>());
            // [final review M-2] Koşunun kendi önizlemesi bekleyen üyeleri GRUPLA koşullu yazar: dalga, kuyruk ve
            // payda onları kesin saymaz — grup dispatch anında atlanabilir. Kök (Up) kesindir.
            var preview = Assert.Single(h.Events.OfType<BuildPreviewEvent>()).Items.ToDictionary(i => i.ProjectId);
            Assert.True(preview[Id("A")].Conditional);
            Assert.True(preview[Id("B")].Conditional);
            Assert.False(preview[Id("Up")].Conditional);
            var skips = h.Events.OfType<ProjectSkippedEvent>().ToList();
            Assert.Equal([Id("A"), Id("B")], skips.Select(e => e.ProjectId));
            Assert.All(skips, e => Assert.Equal(SkipReasons.DependencyStillFailing, e.Reason));
            Assert.Contains("A: skipped — dependency still failing (Up)", h.DecisionLog, StringComparison.Ordinal);
            // Defter kaydı OLDUĞU GİBİ kalır: not, kökler, imza — kök düzelince aynı soru yeniden sorulacak.
            var a = store.Load()[Id("A")];
            Assert.Equal(BuildResult.Succeeded, a.LastResult);
            Assert.Equal("old", a.BuiltSignature);
            Assert.Equal([Id("Up")], a.DepIssueRoots);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    [Fact] // Kontrol: kök bu koşuda DÜZELDİYSE grup normal derlenir — atlama yalnız "hâlâ kırık" kanıtına bağlı.
    public async Task the_waiting_group_builds_normally_once_its_root_recovers()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var (store, plan) = WaitingCyclePlan(cacheRoot);
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((_, _) => Ok());          // Up bu koşuda yeşil
            using var h = new Harness(plan, invoker, stateStore: store);

            await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["Up#1", "A#1", "B#1", "A#2", "B#2"], rec.Calls); // eski davranış aynen
            Assert.Empty(h.Events.OfType<ProjectSkippedEvent>());
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    [Fact] // Kontrol: TEK üye bile başka bir gerekçeyle kirliyse (imza değişti) grup DERLENİR — güvenli yön.
    public async Task a_member_dirty_for_its_own_reason_keeps_the_whole_group_building()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var (store, plan) = WaitingCyclePlan(cacheRoot, bReason: WillBuildReason.SignatureChanged);
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((name, _) => name == "Up" ? Exit(1) : Ok());
            using var h = new Harness(plan, invoker, stateStore: store);

            await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Contains("A#1", rec.Calls);                  // grup dispatch edildi
            Assert.Contains("B#1", rec.Calls);
            // [final review M-2] Grup kesin derlenecek: hiçbir üyesi koşullu yazılmaz.
            var preview = Assert.Single(h.Events.OfType<BuildPreviewEvent>()).Items;
            Assert.DoesNotContain(preview, i => i.Conditional);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    /// <summary>[ara inceleme I1 — Build cycle derler] Rebuild "önbelleği yok say"dır: kökünü bekleyen grup Rebuild'de
    /// grup düzeyinde koşullu ATLANMAZ — tekil bekleyen projenin Rebuild'de koşulsuz derlenmesiyle aynı
    /// (<see cref="ConditionalRebuild.AppliesTo"/> yalnız Build ve Cycles'ta değerlendirir). Kusur: grup kapısı
    /// (<see cref="ConditionalRebuild.GroupAppliesTo"/>) modu okumuyordu; Rebuild grupları derlemeye başlayınca
    /// (CycleCompilation) aynı köke bekleyen grup "dependency still failing" ile atlanıyor, tekil proje ise
    /// derleniyordu.</summary>
    [Fact]
    public async Task a_rebuild_compiles_a_group_waiting_for_a_still_failing_root()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var (store, plan) = WaitingCyclePlan(cacheRoot);
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((name, _) => name == "Up" ? Exit(1) : Ok()); // kök yine patlıyor
            using var h = new Harness(plan, invoker, stateStore: store);

            await h.Sut.StartAsync(Start(RunMode.Rebuild, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Contains("A#1", rec.Calls);                  // grup derlendi — atlanmadı
            Assert.Contains("B#1", rec.Calls);
            Assert.DoesNotContain(h.Events.OfType<ProjectSkippedEvent>(),
                e => e.Reason == SkipReasons.DependencyStillFailing);
            // [final review M-2] Rebuild hiçbir şeyi koşullu değerlendirmez — bekleyen grubun üyeleri de kesin.
            Assert.DoesNotContain(Assert.Single(h.Events.OfType<BuildPreviewEvent>()).Items, i => i.Conditional);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    /// <summary>[restore-once] Turlar arasında ne kaynak ne <c>packages.config</c> değişebilir: bir önceki
    /// turu BAŞARILI biten üyenin sonraki invoke'u restore prologunu taşımaz (başarılı invoke restore'u da
    /// içeriyordu). Başarısız üye yeniden restore ALIR — patlayan şey restore'un kendisi olabilir. packages.config'i
    /// olmayan üye zaten hiç taşımaz.</summary>
    [Fact]
    public async Task a_member_that_succeeded_last_round_does_not_repeat_the_restore_prologue()
    {
        const string px = "RestoreOnceX";
        const string py = "RestoreOnceY";
        await WithPackagesConfigAsync(px, async () =>
        {
            var plan = CyclePlanOf([px, py],
                Node(px, deps: [py], inCycle: true),
                Node(py, deps: [px], inCycle: true));
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((_, _) => Ok());        // yüzey bilgisi yok ⇒ eski kural: iki tam tur
            using var h = new Harness(plan, invoker);

            await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal([$"{px}#1", $"{py}#1", $"{px}#2", $"{py}#2"], rec.Calls);
            Assert.Equal([true, false],                       // tur 1 restore'lu, tur 2 restore'suz
                invoker.Requests.Where(r => r.ProjectId == Id(px)).Select(r => r.NeedsRestore));
            Assert.Equal([false, false],                      // packages.config'i olmayan üye zaten taşımaz
                invoker.Requests.Where(r => r.ProjectId == Id(py)).Select(r => r.NeedsRestore));
        });
    }

    /// <summary>[restore-once fixture] <paramref name="projectName"/> projesinin klasörüne packages.config bırakır, gövdeyi
    /// koşar ve klasörü her durumda siler. Ad BENZERSİZ olmalıdır: PlanRoot paylaşılan bir temp köküdür ve "A"/"B" adlarını
    /// başka testler de kullanır — oraya packages.config bırakmak paralel koşan testlerin NeedsRestore'unu sessizce çevirirdi.
    /// Restore-once testi ve "taşınan üyenin ilk derlemesi" testi aynı kurulumu buradan alır.</summary>
    private static async Task WithPackagesConfigAsync(string projectName, Func<Task> body)
    {
        string projectDir = Path.GetDirectoryName(Id(projectName))!;
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "packages.config"), "<packages />");
        try { await body(); }
        finally { Directory.Delete(projectDir, recursive: true); }
    }

    // ---------------------------------------------------------------- 17) "derleniyor" ilanı = tutulan slot
    // App bir satırı ProjectStartedEvent ile "derleniyor"a alır, sonuçla ya da CycleMemberHeldEvent ile
    // "derleniyor"dan çıkarır. Dalgalı turda bu iki ilan ekranın TEK kaynağıdır: kardeşin başlaması artık
    // "sıra ondan geçti" demek değildir (aynı dalgadaki üyeler birlikte derlenir). Bu bölüm ilanların
    // gerçeği söylediğini pinler: her derlemenin iki ucu ilan edilir ve ilan edilen derleme sayısı hiçbir
    // anda koşunun paralelliğini aşmaz.

    /// <summary>App'in "derleniyor" dediği küme — <see cref="ProjectStartedEvent"/> almış, henüz
    /// <see cref="CycleMemberHeldEvent"/> ya da sonuç almamış projeler (App'in <c>IsCompiling</c> kuralının
    /// olay akışındaki karşılığı). Akışın HER önekindeki en büyük boyunu döner.</summary>
    private static int PeakAnnouncedCompiles(IReadOnlyList<IpcEvent> events)
    {
        var compiling = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int peak = 0;
        foreach (var e in events)
        {
            switch (e)
            {
                case ProjectStartedEvent s:
                    compiling.Add(s.ProjectId);
                    peak = Math.Max(peak, compiling.Count);
                    break;
                case CycleMemberHeldEvent held: compiling.Remove(held.ProjectId); break;
                case ProjectSucceededEvent done: compiling.Remove(done.ProjectId); break;
                case ProjectFailedEvent done: compiling.Remove(done.ProjectId); break;
            }
        }
        return peak;
    }

    /// <summary>
    /// [dalga görünürlüğü] Her üye, turdaki derlemesi BİTTİĞİ anda "grubunu bekliyor" diye ilan edilir —
    /// sonucu açıklanmadan (ara tur yine yayılmaz). Eskiden bu an hiç yayılmazdı ve App onu "kardeşi başladıysa
    /// sırası geçmiştir" diye TAHMİN ediyordu; tahmin yalnız sıralı turda doğruydu. Dalgalı turda aynı anda
    /// derlenen üyeler "bekliyor" görünüyor, işi biten üye ise sonraki dalga başlayana dek "derleniyor"
    /// görünmeye devam ediyordu.
    /// </summary>
    [Fact]
    public async Task every_member_is_announced_held_the_moment_its_compile_ends()
    {
        var rec = new RoundRecorder();
        using var h = new Harness(TwoMemberCycle(), rec.Invoker((_, _) => Ok()));

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // Her invoke'un iki ucu da ilan edilir; sonuçlar ise grup bitince, üye başına TEK kez gelir.
        Assert.Equal(
            ["cycleRound:A:1", "projectStarted:A", "cycleMemberHeld:A", "projectStarted:B", "cycleMemberHeld:B",
             "cycleRound:A:2", "projectStarted:A", "cycleMemberHeld:A", "projectStarted:B", "cycleMemberHeld:B",
             "projectSucceeded:A", "projectSucceeded:B"],
            h.Events.Where(e => e is CycleRoundStartedEvent or ProjectStartedEvent or CycleMemberHeldEvent
                or ProjectSucceededEvent or ProjectFailedEvent).Select(Describe));
    }

    /// <summary>
    /// [dalga görünürlüğü] Üye ancak MSBuild sırasını (slot) TUTARKEN "derleniyor" diye ilan edilir ve "bitti"
    /// ilanı slot bırakılmadan önce gider — ilan edilen derleme sayısı hiçbir anda koşunun paralelliğini
    /// aşamaz. Eskiden ilan slot beklenmeden yapılıyordu: beş uydulu bir dalga paralellik 2'de beşini birden
    /// "derleniyor" gösterirdi, oysa aynı anda yalnız ikisi derlenebilir.
    /// </summary>
    [Fact]
    public async Task a_wave_never_announces_more_compiles_than_the_run_parallelism()
    {
        string[] names = ["Hub", "S1", "S2", "S3", "S4", "S5"];
        var plan = HashModePlan(CyclePlanOf(names,
            Node("Hub", deps: ["S1", "S2", "S3", "S4", "S5"], inCycle: true),
            Node("S1", deps: ["Hub"], inCycle: true), Node("S2", deps: ["Hub"], inCycle: true),
            Node("S3", deps: ["Hub"], inCycle: true), Node("S4", deps: ["Hub"], inCycle: true),
            Node("S5", deps: ["Hub"], inCycle: true)), names);
        var disk = new SurfaceDisk();
        foreach (string name in names) disk.Set(name, name + "-api"); // yüzeyler oturmuş: tek tur
        var bothSlotsTaken = Signal();
        var release = Signal();
        int arrived = 0;
        var rec = new RoundRecorder();
        // İlk iki uydu iki slotu da tutarken testin iznini bekler: dalganın kalanı bu sırada sıraya girer.
        // Zamanlamaya bağlı değil — sıraya giren üyenin ilanı ya slottan ÖNCE (kusur) ya SONRA yapılır.
        var invoker = rec.Invoker(async (name, _, _) =>
        {
            if (name == "Hub") return Ok();
            if (Interlocked.Increment(ref arrived) == 2) bothSlotsTaken.TrySetResult();
            await release.Task;
            return Ok();
        });
        using var h = new Harness(plan, invoker, apiSurface: disk.Read);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 2), default);
        await bothSlotsTaken.Task.WaitAsync(Limit);
        release.TrySetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(6, rec.Calls.Count);
        Assert.True(invoker.MaxConcurrent <= 2, $"parallelism 2 iken {invoker.MaxConcurrent} eşzamanlı invoke gözlendi");
        int peak = PeakAnnouncedCompiles(h.Events);
        Assert.True(peak <= 2, $"parallelism 2 iken {peak} proje aynı anda 'derleniyor' ilan edildi");
    }

    /// <summary>
    /// [dalga görünürlüğü] Tekil proje de ancak bir MSBuild slotu TUTARKEN dispatch edilir: worker slotu işi
    /// istemeden ÖNCE alır, sonucu yazdıktan SONRA bırakır. Eskiden proje slot beklenmeden dispatch ediliyor
    /// ve "derleniyor" ilan ediliyordu — koşan bir dalga slotları doldurmuşken sıradaki proje ekranda
    /// derleniyor görünür, sayaç paralelliği aşardı. Senaryo sahadaki kalıptır: bir grubun dalgası koşarken
    /// başka bir grubun upstream'i (U) hazır hâle gelir.
    /// </summary>
    [Fact]
    public async Task a_project_is_dispatched_only_while_it_holds_a_build_slot()
    {
        // G0 = H0 ↔ T1..T4 (yıldız), G1 = X ↔ Y; X, U'ya bağlı, U da P'ye. P ile G0 aynı anda başlar.
        var plan = CyclesPlanOf([["H0", "T1", "T2", "T3", "T4"], ["X", "Y"]],
            Node("H0", deps: ["T1", "T2", "T3", "T4"], inCycle: true),
            Node("T1", deps: ["H0"], inCycle: true), Node("T2", deps: ["H0"], inCycle: true),
            Node("T3", deps: ["H0"], inCycle: true), Node("T4", deps: ["H0"], inCycle: true),
            Node("P"), Node("U", deps: ["P"]),
            Node("X", deps: ["U", "Y"], inCycle: true), Node("Y", deps: ["X"], inCycle: true));
        var t1Compiling = Signal();
        var uCompiling = Signal();
        var rec = new RoundRecorder();
        // T1 dalgada bir slotu U derlenmeye başlayana dek tutar; P, T1 slotunu alana dek sürer. P bitince
        // U hazırdır ama iki slottan biri T1'de, diğeri dalganın sıradaki üyesinde: U sırasını beklemelidir.
        var invoker = rec.Invoker(async (name, round, _) =>
        {
            if (name == "T1" && round == 1) { t1Compiling.TrySetResult(); await uCompiling.Task; }
            if (name == "P") await t1Compiling.Task;
            if (name == "U") uCompiling.TrySetResult();
            return Ok();
        });
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 2), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(9, Assert.IsType<RunCompletedEvent>(h.Events[^1]).Succeeded);
        Assert.True(invoker.MaxConcurrent <= 2, $"parallelism 2 iken {invoker.MaxConcurrent} eşzamanlı invoke gözlendi");
        int peak = PeakAnnouncedCompiles(h.Events);
        Assert.True(peak <= 2, $"parallelism 2 iken {peak} proje aynı anda 'derleniyor' ilan edildi");
    }

    /// <summary>
    /// [dalga görünürlüğü · §4.5] Slot beklerken Stop düşen dalga üyesi BAŞLATILMAZ. Stop kapısı üye başlamadan
    /// önce kontrol edilir ve dalgalı turda "başlamak" slotun alındığı andır. Kapı yalnız slottan önce dursaydı,
    /// sırasını bekleyen üye Stop'tan SONRA yeni bir MSBuild.exe başlatırdı — "Stop'a basıyorum ama yenileri
    /// derlenmeye devam ediyor" kusurunun (bkz. 7b) dalgadaki hâli.
    /// </summary>
    [Fact]
    public async Task a_member_waiting_for_a_build_slot_is_not_started_after_a_stop()
    {
        var plan = HashModePlan(StarCycle(), "Hub", "S1", "S2", "S3");
        var disk = StableStarDisk();
        var rec = new RoundRecorder();
        RunCoordinator? sut = null;
        // Paralellik 1: S1 tek slotu tutarken S2 ve S3 kapıdan geçip sıraya girer; Stop S1 derlenirken düşer.
        var invoker = rec.Invoker(async (name, _, _) =>
        {
            if (name == "S1")
            {
                await Task.Yield();
                Assert.True(sut!.TryRequestStop(StopKind.Graceful));
            }
            return Ok();
        });
        using var h = new Harness(plan, invoker, apiSurface: disk.Read);
        sut = h.Sut;

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["Hub#1", "S1#1"], rec.Calls);   // sırada bekleyen S2 ve S3 hiç invoke EDİLMEZ
        // Yarıda kesilen grup: her üye "stopped" raporlanır, hiçbir şey persist edilmez (bkz. 7).
        Assert.Equal(4, h.Events.OfType<ProjectFailedEvent>().Count(e => e.Reason == "stopped"));
    }

    // ---------------------------------------------------------------- 14) [RESOLVE 3.4] tur 1 yalnız gereken üyeleri derler
    //
    // Önceki Resolve yakınsamışsa defterde her üyenin terimi, okuduğu kardeş yüzeyleri ve motor parmak izi vardır
    // (karar 2). Tur 1 yalnız CycleMemberNeed'in "gerekli" dediği üyeleri derler; taşınan üye tur sonu bayatlık
    // sorusuna kayıtlı yüzeyleriyle girer (karar 3) ve hiç derlenmeden yakınsarsa "up to date (carried)" raporlanır,
    // defteri yenilenir. Kesilen koşu yeni alan yazmaz; yakınsamayan grupta yalnız oturmuş üyeler yazar (D3).

    /// <summary>Aracın kendi derlediği, kanıtı ve beslenen kopyaları sağlam çıktı (defter kipi).</summary>
    private static readonly OutputCheck IntactOutput =
        new(EvidenceMode.Ledger, EvidenceMissing: false, FedIntact: true, Time: null, EvidenceAt: null);

    /// <summary>Üye düzeyi atlamanın girdileri TEK yerde: yüzey kanıtı (<see cref="HashModePlan"/>'ın çıktı haritası),
    /// bileşik imza (bir üyenin terimi değişince gerçek planlayıcıda da değişir), üye terimleri ve her üyenin sağlam
    /// çıktı kanıtı. Testler tek bir girdiyi bozar (<see cref="WithCheck"/>, <c>with</c>).</summary>
    internal static RunPlan MemberSkipPlan(RunPlan plan, string signature, params (string Name, string Term)[] members)
    {
        string[] names = [.. members.Select(m => m.Name)];
        var hashMode = HashModePlan(plan, names);
        return hashMode with
        {
            Incremental = hashMode.Incremental! with
            {
                SignatureById = names.ToDictionary(Id, _ => signature, StringComparer.OrdinalIgnoreCase),
                MemberTermById = members.ToDictionary(m => Id(m.Name), m => m.Term, StringComparer.OrdinalIgnoreCase),
                ChecksById = names.ToDictionary(Id, _ => IntactOutput, StringComparer.OrdinalIgnoreCase),
            },
        };
    }

    /// <summary>A ↔ B'de tur 1'in girdileri: bileşik imza ve iki üye terimi.</summary>
    private static RunPlan TwoMembers(string signature, string termA, string termB) =>
        MemberSkipPlan(TwoMemberCycle(), signature, ("A", termA), ("B", termB));

    /// <summary>Tek üyenin çıktı kanıtını değiştirir (K1 silinmiş çıktı, K2/K3 araç dışında derlenmiş çıktı).</summary>
    private static RunPlan WithCheck(RunPlan plan, string name, OutputCheck check) =>
        plan with
        {
            Incremental = plan.Incremental! with
            {
                ChecksById = new Dictionary<string, OutputCheck>(plan.Incremental!.ChecksById!, StringComparer.OrdinalIgnoreCase)
                    { [Id(name)] = check },
            },
        };

    /// <summary>A → {B, C}, B → A, C → A: A'nın iki grup içi bağımlılığı var; bildirim sırası parametredir.</summary>
    private static RunPlan HubCycle(params string[] hubDeps) => CyclePlanOf(["A", "B", "C"],
        Node("A", deps: hubDeps, inCycle: true), Node("B", deps: ["A"], inCycle: true), Node("C", deps: ["A"], inCycle: true));

    /// <summary>Geçici önbellek kökünde (defter) koşan test gövdesi; kök her durumda silinir.</summary>
    private static async Task InCacheRootAsync(Func<string, Task> body)
    {
        string cacheRoot = NewCacheRoot();
        try { await body(cacheRoot); }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    /// <summary>Aynı defter ve sahte disk üzerinde bir Resolve koşusu (tek işçi); bitmesini bekler. <paramref name="mode"/>
    /// başka modu (Build, Rebuild) aynı koşturucuyla sürer — yüzey kapısı testleri onu kullanır, ikinci bir koşturucu
    /// YAZILMAZ (kopya YASAK).</summary>
    internal static async Task<Harness> ResolveAsync(BuildStateStore store, SurfaceDisk disk, RunPlan plan,
        FakeInvoker invoker, string? customBeforeTargetsPath = null, CancellationToken ct = default,
        RunMode mode = RunMode.Cycles)
    {
        var h = new Harness(plan, invoker, stateStore: store, apiSurface: disk.Read,
            customBeforeTargetsPath: customBeforeTargetsPath);
        try
        {
            await h.Sut.StartAsync(Start(mode, parallelism: 1), ct);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            return h;
        }
        catch { h.Dispose(); throw; }
    }

    /// <summary>Önceki yakınsamış Resolve: kayıt yok ⇒ herkes derlenir; Converged her üyenin terimini, okuduğu
    /// yüzeyleri ve motor parmak izini yazar. Derleme yüzeyleri değiştirmez.</summary>
    private static async Task ConvergeOnceAsync(BuildStateStore store, SurfaceDisk disk, RunPlan plan,
        string? customBeforeTargetsPath = null)
    {
        using var h = await ResolveAsync(store, disk, plan, new RoundRecorder().Invoker((_, _) => Ok()),
            customBeforeTargetsPath);
        Assert.Equal(CycleOutcome.Converged, Assert.Single(h.Events.OfType<CycleCompletedEvent>()).Outcome);
    }

    /// <summary>A ↔ B, önceki Resolve yakınsamış: terimler a1/b1, yüzeyler a1/b1, bileşik imza sig1.</summary>
    private static async Task<(BuildStateStore Store, SurfaceDisk Disk)> ConvergedTwoMemberCycleAsync(string cacheRoot)
    {
        var store = new BuildStateStore(cacheRoot);
        var disk = new SurfaceDisk();
        disk.Set("A", "a1");
        disk.Set("B", "b1");
        await ConvergeOnceAsync(store, disk, TwoMembers("sig1", "a1", "b1"));
        return (store, disk);
    }

    /// <summary>[karar 2] Yalnız A'nın kendi girdileri değişik: tur 1 yalnız A'yı derler. B taşınır — <c>skipped — up
    /// to date</c> raporlanır, defteri YENİ bileşik imzayla yenilenir (süresi ve döngü kanıtı aynen kalır); karar
    /// satırı ve olay derlenen sayısını söyler.</summary>
    [Fact]
    public Task round_one_compiles_only_the_member_whose_own_inputs_changed() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        var bBefore = store.Load()[Id("B")];
        var rec = new RoundRecorder();
        using var h = await ResolveAsync(store, disk, TwoMembers("sig2", "a2", "b1"), rec.Invoker((_, _) => Ok()));

        Assert.Equal(["A#1"], rec.Calls);
        var skipped = Assert.Single(h.Events.OfType<ProjectSkippedEvent>());
        Assert.Equal((Id("B"), SkipReasons.UpToDate, false), (skipped.ProjectId, skipped.Reason, skipped.CycleUnconverged));
        Assert.Equal(Id("A"), Assert.Single(h.Events.OfType<ProjectSucceededEvent>()).ProjectId);
        var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
        Assert.Equal((CycleOutcome.Converged, 2, 1, 1),
            (completed.Outcome, completed.MemberCount, completed.CompiledCount, completed.Rounds));
        var b = store.Load()[Id("B")];
        Assert.Equal(("sig2", BuildResult.Succeeded), (b.BuiltSignature, b.LastResult));
        Assert.Equal(bBefore.LastDurationMs, b.LastDurationMs);              // derlenmedi: süre bir önceki derlemenin
        Assert.Equal(bBefore.CycleMemberTerm, b.CycleMemberTerm);            // döngü kanıtı aynen
        Assert.Equal(bBefore.CycleReadSurfaces, b.CycleReadSurfaces);
        Assert.Equal(bBefore.CycleEngineFingerprint, b.CycleEngineFingerprint);
        string log = h.DecisionLog;
        Assert.Contains(CycleDecisionLines.RoundOneNeed("A", CycleMemberNeed.OwnInputsChangedReason), log, StringComparison.Ordinal);
        Assert.DoesNotContain(CycleDecisionLines.RoundOneNeed("B", ""), log, StringComparison.Ordinal);
        Assert.Contains($"B: skipped — {SkipReasons.UpToDate} ({CycleDecisionLines.CarriedDetail})", log, StringComparison.Ordinal);
        Assert.Contains(CycleDecisionLines.Verdict("A", CycleRoundDecision.Converged, members: 2, compiled: 1, rememberedAt: null), log, StringComparison.Ordinal);
    });

    /// <summary>[Build cycle derler] Rebuild önbelleği yok sayar: güvenilir kaydı olan, terimi değişmemiş üye de tur 1'de
    /// derlenir (<see cref="CycleMemberNeed"/> sorulmaz). Yüzey kanıtı yine okunur: yüzeyler oturmuşsa grup tek turda
    /// yakınsar. Aynı sahne Resolve'da B'yi taşır (kardeş test
    /// <see cref="round_one_compiles_only_the_member_whose_own_inputs_changed"/>); burada herkes invoke edilir.</summary>
    [Fact]
    public Task a_rebuild_compiles_every_member_in_round_one_even_with_trusted_records() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        var rec = new RoundRecorder();
        using var h = new Harness(TwoMembers("sig2", "a2", "b1"), rec.Invoker((_, _) => Ok()), stateStore: store,
            apiSurface: disk.Read);

        await h.Sut.StartAsync(Start(RunMode.Rebuild, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A#1", "B#1"], rec.Calls);                          // kimse taşınmadı
        Assert.Empty(h.Events.OfType<ProjectSkippedEvent>());
        var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
        Assert.Equal((CycleOutcome.Converged, 2, 2, 1),
            (completed.Outcome, completed.MemberCount, completed.CompiledCount, completed.Rounds));
    });

    /// <summary>[karar 3] Taşınan B, A'nın ESKİ yüzeyini okumuş kayıtla tur sonu sorusuna girer; A'nın yüzeyi tur 1'de
    /// oynayınca B bayatlar ve tur 2'de derlenir — skipped DEĞİL; defteri tur 2'de okuduğu taze yüzeyi taşır.</summary>
    [Fact]
    public Task a_carried_member_whose_read_surface_moves_in_round_one_is_compiled_in_round_two() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        var rec = new RoundRecorder();
        using var h = await ResolveAsync(store, disk, TwoMembers("sig2", "a2", "b1"),
            rec.Invoker((name, _) => { if (name == "A") disk.Set("A", "a2"); return Ok(); }));

        Assert.Equal(["A#1", "B#1"], rec.Calls);                              // tur 1: A · tur 2: B'nin İLK derlemesi
        Assert.Equal([1, 1], h.Events.OfType<CycleRoundStartedEvent>().Select(e => e.MemberCount));
        var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
        Assert.Equal((CycleOutcome.Converged, 2, 2), (completed.Outcome, completed.Rounds, completed.CompiledCount));
        Assert.Empty(h.Events.OfType<ProjectSkippedEvent>());
        // [R3c2 · pin] Süre yalnız tur 2'nin derlemesidir: taşındığı tur 1 süre katmaz.
        Assert.Equal(Ok().DurationMs,
            Assert.Single(h.Events.OfType<ProjectSucceededEvent>(), e => e.ProjectId == Id("B")).DurationMs);
        var read = Assert.Single(store.Load()[Id("B")].CycleReadSurfaces!);
        Assert.Equal((Id("A"), "a2"), (read.Producer, read.Hash));
    });

    /// <summary>[R3c3 · tembel log] Hiç derlenmeyen taşınan üyenin bu koşuda proje logu YOKTUR: sıradan bir güncel atlama
    /// gibi dosya almaz. Eskiden grup başında HER üyenin logu açılırdı ve taşınan üye koşunun log klasöründe boş bir dosya
    /// bırakırdı. Log ilk derlemede açılır: derlenen A'nın logu vardır.</summary>
    [Fact]
    public Task a_carried_member_that_is_never_compiled_leaves_no_project_log() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        using var h = await ResolveAsync(store, disk, TwoMembers("sig2", "a2", "b1"),
            new RoundRecorder().Invoker((_, _) => Ok()));

        var logs = h.LogWriters[^1];
        Assert.True(File.Exists(logs.ProjectLogPath(Id("A"))));            // derlenen üye: log var
        Assert.False(File.Exists(logs.ProjectLogPath(Id("B"))));           // taşınan, hiç derlenmeyen üye: dosya YOK
    });

    /// <summary>[R3c3 · tembel log] Tur 2'de İLK kez derlenen taşınan üyenin logu da açılır ve TEK dosyadır: yalnız o
    /// derlemenin satırlarını taşır. Açıklama satırı yazılmaz — projenin ilk satırı gerçek MSBuild komut satırıdır: B'nin
    /// logu, bir kez derlenen A'nınkiyle aynı satır sayısındadır.</summary>
    [Fact]
    public Task a_carried_member_first_compiled_in_round_two_has_one_project_log() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        using var h = await ResolveAsync(store, disk, TwoMembers("sig2", "a2", "b1"),
            new RoundRecorder().Invoker((name, _) => { if (name == "A") disk.Set("A", "a2"); return Ok(); }));

        var logs = h.LogWriters[^1];
        string[] aLog = File.ReadAllLines(logs.ProjectLogPath(Id("A")));
        string[] bLog = File.ReadAllLines(logs.ProjectLogPath(Id("B")));
        Assert.NotEmpty(bLog);
        Assert.Equal(aLog.Length, bLog.Length);
        // [R3 final · M3] İlk satır DOĞRUDAN pinlenir: satır sayısı eşitliği, TÜM tembel açılışlara eklenen bir açıklama
        // satırını yakalamazdı (A ve B birlikte +1 olur). Her projenin ilk satırı ilk invoke'un GERÇEK komut satırıdır.
        Assert.Equal(ExpectedBuildCommandLine(Id("B")), bLog[0]);
        Assert.Equal(ExpectedBuildCommandLine(Id("A")), aLog[0]);
    });

    [Fact] // eski defter (döngü alanları yok) ⇒ bugünkü davranış: herkes derlenir, nedeni decision.log'da
    public Task a_member_without_cycle_fields_in_its_record_is_compiled() => InCacheRootAsync(async cacheRoot =>
    {
        var store = new BuildStateStore(cacheRoot);
        SeedGreen(store, "A", "sig1");
        SeedGreen(store, "B", "sig1");
        var disk = new SurfaceDisk();
        disk.Set("A", "a1");
        disk.Set("B", "b1");
        var rec = new RoundRecorder();
        using var h = await ResolveAsync(store, disk, TwoMembers("sig1", "a1", "b1"), rec.Invoker((_, _) => Ok()));

        Assert.Equal(["A#1", "B#1"], rec.Calls);
        Assert.Equal(2, Assert.Single(h.Events.OfType<CycleCompletedEvent>()).CompiledCount);
        foreach (string name in new[] { "A", "B" })
            Assert.Contains(CycleDecisionLines.RoundOneNeed(name, CycleMemberNeed.NoTrustedRecordReason), h.DecisionLog,
                StringComparison.Ordinal);
    });

    [Fact] // K1: çıktısı silinmiş (kanıt eksik), kaynağı aynı üye derlenir; kardeşi taşınır
    public Task a_member_whose_output_evidence_is_missing_is_compiled_even_if_its_term_is_unchanged() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        var rec = new RoundRecorder();
        using var h = await ResolveAsync(store, disk,
            WithCheck(TwoMembers("sig1", "a1", "b1"), "B", IntactOutput with { EvidenceMissing = true }),
            rec.Invoker((_, _) => Ok()));

        Assert.Equal(["B#1"], rec.Calls);
        Assert.Contains(CycleDecisionLines.RoundOneNeed("B", CycleMemberNeed.OutputEvidenceMissingReason), h.DecisionLog,
            StringComparison.Ordinal);
        Assert.Equal(Id("A"), Assert.Single(h.Events.OfType<ProjectSkippedEvent>()).ProjectId);
    });

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — D2]</b> Eski iddia (<c>a_member_whose_output_is_in_time_mode_is_compiled</c>, K2/K3):
    /// zaman kipindeki üye tur 1'de derlenir, kardeşi taşınır. Değişme gerekçesi <see cref="CycleMemberNeed"/>'in
    /// testinde (ölçüm: VS'de derlenen UI grubunda 16/17 üye yalnız bu kuralla derleniyordu). Çıktısı kendi girdilerinden
    /// yeni (Fresh) üye kalan kurallarla sınanır: terim ve okuduğu yüzeyler aynıysa taşınır, grup derlemeden yakınsar.
    /// </summary>
    [Fact]
    public Task a_member_whose_output_is_in_time_mode_is_carried_when_its_term_and_surfaces_are_unchanged() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        var rec = new RoundRecorder();
        using var h = await ResolveAsync(store, disk,
            WithCheck(TwoMembers("sig1", "a1", "b1"), "B", IntactOutput with { Mode = EvidenceMode.Time, Time = TimeVerdict.Fresh }),
            rec.Invoker((_, _) => Ok()));

        Assert.Empty(rec.Calls);
        Assert.Equal(2, h.Events.OfType<ProjectSkippedEvent>().Count(e => e.Reason == SkipReasons.UpToDate));
        var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
        Assert.Equal((CycleOutcome.Converged, 0), (completed.Outcome, completed.CompiledCount));
        Assert.Equal(BuildResult.Succeeded, store.Load()[Id("B")].LastResult);
    });

    [Fact] // (v) daraltılmış hâli: kendi girdisi çıktıdan yeni üye yine derlenir, kardeşi taşınır
    public Task a_member_whose_output_is_older_than_its_inputs_is_compiled() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        var rec = new RoundRecorder();
        using var h = await ResolveAsync(store, disk,
            WithCheck(TwoMembers("sig1", "a1", "b1"), "B", IntactOutput with { Mode = EvidenceMode.Time, Time = TimeVerdict.OwnNewer }),
            rec.Invoker((_, _) => Ok()));

        Assert.Equal(["B#1"], rec.Calls);
        Assert.Contains(CycleDecisionLines.RoundOneNeed("B", "output older than its inputs"), h.DecisionLog, StringComparison.Ordinal);
    });

    /// <summary>[Review Focus 1] VS paralel derlemesi: B'nin çıktısı yeni (zaman kipi) ama okuduğu A'nın yüzeyi kayıttan
    /// farklı — B eski API'ye bağlanmış olabilir ⇒ tur 1 B'yi derler (kural ii), zaman kipi onu kurtarmaz.</summary>
    [Fact]
    public Task a_time_mode_member_whose_read_surface_moved_is_compiled() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        disk.Set("A", "a2"); // kardeşin API'si koşular arasında değişti (VS derledi)
        var rec = new RoundRecorder();
        using var h = await ResolveAsync(store, disk,
            WithCheck(TwoMembers("sig1", "a1", "b1"), "B", IntactOutput with { Mode = EvidenceMode.Time, Time = TimeVerdict.Fresh }),
            rec.Invoker((_, _) => Ok()));

        Assert.Contains("B#1", rec.Calls);
        Assert.Contains(CycleDecisionLines.RoundOneNeed("B", CycleMemberNeed.ReadSurfaceMovedPrefix + SurfaceDisk.PathOf("A")),
            h.DecisionLog, StringComparison.Ordinal);
    });

    [Fact] // K9d: motor parmak izi her derleme isteğinin targets yolunu kapsar — yol değişince grupta herkes derlenir
    public Task a_different_engine_fingerprint_compiles_every_member() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        var rec = new RoundRecorder();
        using var h = await ResolveAsync(store, disk, TwoMembers("sig1", "a1", "b1"), rec.Invoker((_, _) => Ok()),
            customBeforeTargetsPath: @"X:\engine\wpf-temporary-assembly.targets");

        Assert.Equal(["A#1", "B#1"], rec.Calls);
        foreach (string name in new[] { "A", "B" })
            Assert.Contains(CycleDecisionLines.RoundOneNeed(name, CycleMemberNeed.EngineChangedReason), h.DecisionLog,
                StringComparison.Ordinal);
    });

    [Fact] // K2 ikinci yüz: kardeşin yüzeyi koşular arasında araç dışında değişti ⇒ onu okumuş üye derlenir
    public Task a_recorded_surface_that_differs_from_the_disk_at_group_start_compiles_the_reader() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        disk.Set("B", "b2");
        var rec = new RoundRecorder();
        using var h = await ResolveAsync(store, disk, TwoMembers("sig1", "a1", "b1"), rec.Invoker((_, _) => Ok()));

        Assert.Equal(["A#1"], rec.Calls);
        Assert.Contains(CycleDecisionLines.RoundOneNeed("A",
                CycleMemberNeed.ReadSurfaceMovedPrefix + CycleDecisionLines.MovedTerm([SurfaceDisk.PathOf("B")])),
            h.DecisionLog, StringComparison.Ordinal);
        Assert.Equal(Id("B"), Assert.Single(h.Events.OfType<ProjectSkippedEvent>()).ProjectId);
    });

    /// <summary>[karar 2/6] Converged her üyenin üç döngü alanını yazar: terim bu koşunun terimi; okunan yüzeyler
    /// kanonik sırada (Producer, sonra File) ve her grup içi bağımlılık için en az bir girdi (boş kayıt bir hatadır);
    /// parmak izi her derleme isteğine giren AYNI MSBuild.exe ve targets yolundan. Taşınan üyenin alanları aynen kalır,
    /// derlenen üyeninki yenilenir.</summary>
    [Fact]
    public Task converged_group_writes_term_surfaces_and_fingerprint_for_compiled_and_carried_members() => InCacheRootAsync(async cacheRoot =>
    {
        const string targets = @"X:\engine\wpf-temporary-assembly.targets";
        var store = new BuildStateStore(cacheRoot);
        var disk = new SurfaceDisk();
        foreach (string name in new[] { "A", "B", "C" }) disk.Set(name, name.ToLowerInvariant() + "1");
        // A iki kardeşini TERS sırada bildirir: yazım sırası bildirimden değil kanonik kuraldan gelir.
        var cycle = HubCycle("C", "B");
        await ConvergeOnceAsync(store, disk, MemberSkipPlan(cycle, "sig1", ("A", "a1"), ("B", "b1"), ("C", "c1")), targets);

        string fingerprint = EngineFingerprint.Compute(FakeMsBuildExe,
            (project, configuration) => MsBuildArguments.Build(project, configuration, customBeforeTargets: targets));
        var ledger = store.Load();
        foreach (var (name, deps) in new[] { ("A", new[] { "B", "C" }), ("B", new[] { "A" }), ("C", new[] { "A" }) })
        {
            var record = ledger[Id(name)];
            Assert.Equal(name.ToLowerInvariant() + "1", record.CycleMemberTerm);
            Assert.Equal(fingerprint, record.CycleEngineFingerprint);
            var surfaces = Assert.IsAssignableFrom<IReadOnlyList<CycleReadSurface>>(record.CycleReadSurfaces);
            Assert.Equal(deps.Select(Id), surfaces.Select(s => s.Producer).Distinct(StringComparer.OrdinalIgnoreCase));
            Assert.Equal(surfaces.OrderBy(s => s.Producer, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.File, StringComparer.OrdinalIgnoreCase), surfaces);
            Assert.All(surfaces, s => Assert.Equal(disk.Read(s.File), s.Hash));
        }

        // İkinci koşu: yalnız A değişik ⇒ B ve C taşınır, alanları AYNEN kalır; A'nın terimi yenilenir.
        using var h = await ResolveAsync(store, disk, MemberSkipPlan(cycle, "sig2", ("A", "a2"), ("B", "b1"), ("C", "c1")),
            new RoundRecorder().Invoker((_, _) => Ok()), targets);
        var after = store.Load();
        Assert.Equal("a2", after[Id("A")].CycleMemberTerm);
        foreach (string name in new[] { "B", "C" })
        {
            Assert.Equal("sig2", after[Id(name)].BuiltSignature);
            Assert.Equal(ledger[Id(name)].CycleMemberTerm, after[Id(name)].CycleMemberTerm);
            Assert.Equal(ledger[Id(name)].CycleReadSurfaces, after[Id(name)].CycleReadSurfaces);
            Assert.Equal(ledger[Id(name)].CycleEngineFingerprint, after[Id(name)].CycleEngineFingerprint);
        }
    });

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — D3]</b> Eski iddia (<c>non_converged_and_stopped_groups_write_no_cycle_fields</c>, karar 4):
    /// yakınsamayan (NoProgress, CapReached) ya da kesilen koşu HİÇ KİMSEYİ persist etmez — taşınan üye dahil her üye
    /// geçersizlenir, takip koşusu herkesi derler.
    /// <para><b>Değişme gerekçesi (kullanıcı senaryosu, 2026-10-08):</b> 17 üyeli UI grubunda tek üyenin copy-lock'u
    /// NoProgress verip 16 yeşil kaydı geçersizledi; bir sonraki Build 17 üyeyi yeniden derledi (~6 dk). Yüzey kanıtı
    /// varken oturmuş (son turda bayat olmayan) yeşil ya da taşınan üyenin çıktısı nihai API'lere bağlıdır ve kaydı
    /// doğrudur; yalnız bayat üye geçersizlenir, böylece grup kirli kalır ve takip koşusu yalnız onu derler. Kesilen
    /// (Stop) koşu bugünkü gibi hiçbir şey persist etmez.</para>
    /// Senaryo (ChainPlan X→M, N→X, M↔R, M,R→N; yalnız N değişik, tur 1 yalnız N'yi derler, X/M/R taşınır):
    /// · no progress: N oturmuşken patlar (tur 1) — X, M, R taşınmış ve oturmuş ⇒ üçü de sig2 ile yenilenir, N kanıtlı hata.
    /// · stopped: hiçbir şey yenilenmez (herkes Failed, sig1).
    /// · cap reached: tur 1 N (yüzeyi oynar ⇒ M ve R bayat); tur 2 M (R'nin eski yüzeyini okur, patlar) ve R (yüzeyi oynar ⇒
    ///   M bayat); tur 3 M (yüzeyi oynar ⇒ X ve R bayat) ⇒ tavan. Son turda X ve R bayattır ⇒ geçersizlenir (sig1, Failed);
    ///   N ve M oturmuş ⇒ güvenilir (sig2, Succeeded); takip koşusu X ve R'yi derler.
    /// </summary>
    [Theory]
    [InlineData("no progress")]
    [InlineData("stopped")]
    [InlineData("cap reached")]
    public Task a_group_without_a_verdict_keeps_only_its_settled_members(string outcome) => InCacheRootAsync(async cacheRoot =>
    {
        string[] names = ["X", "N", "M", "R"];
        var store = new BuildStateStore(cacheRoot);
        var disk = new SurfaceDisk();
        foreach (string name in names) disk.Set(name, name.ToLowerInvariant() + "1");
        await ConvergeOnceAsync(store, disk, ChainPlan("sig1", "n1"));
        using var cts = new CancellationTokenSource();
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, round, ct) =>
        {
            if (outcome == "stopped") { cts.Cancel(); ct.ThrowIfCancellationRequested(); }
            if (outcome == "no progress" || (name == "M" && round == 1)) return Task.FromResult(Exit(1));
            disk.Set(name, name.ToLowerInvariant() + "2");
            return Task.FromResult(Ok());
        });
        // RoundRecorder üye BAŞINA sayar: M'nin ilk derlemesi grup turu 2'de, ikincisi turu 3'tedir.
        string[] expectedCalls = outcome == "cap reached" ? ["N#1", "M#1", "R#1", "M#2"] : ["N#1"];
        using (var h = await ResolveAsync(store, disk, ChainPlan("sig2", "n2"), invoker, ct: cts.Token))
        {
            Assert.Equal(expectedCalls, rec.Calls);
            if (outcome != "stopped") // kesilen koşuda olay akışı sorgulanamaz (stopped_group_invalidates_every_member notu)
                Assert.Equal(outcome == "cap reached" ? CycleOutcome.CapReached : CycleOutcome.NoProgress,
                    Assert.Single(h.Events.OfType<CycleCompletedEvent>()).Outcome);
            if (outcome == "no progress")
            {
                Assert.Equal(3, h.Events.OfType<ProjectSkippedEvent>().Count(e => e.Reason == SkipReasons.UpToDate));
                Assert.True(Assert.Single(h.Events.OfType<ProjectFailedEvent>()).Evidence);
            }
            if (outcome == "cap reached")
            {
                var succeeded = h.Events.OfType<ProjectSucceededEvent>().ToDictionary(e => NameOf(e.ProjectId));
                Assert.True(succeeded["N"].Trusted); Assert.False(succeeded["N"].CycleUnsettled);
                Assert.True(succeeded["M"].Trusted);
                Assert.False(succeeded["R"].Trusted); Assert.True(succeeded["R"].CycleUnsettled);
                Assert.False(succeeded["X"].Trusted);
                Assert.Empty(h.Events.OfType<ProjectSkippedEvent>()); // bayat taşınan X "up to date" raporlanmaz
            }
        }
        var ledger = store.Load();
        var expected = outcome switch
        {
            "no progress" => new[] { ("X", "sig2", BuildResult.Succeeded), ("N", "sig1", BuildResult.Failed), ("M", "sig2", BuildResult.Succeeded), ("R", "sig2", BuildResult.Succeeded) },
            "cap reached" => new[] { ("X", "sig1", BuildResult.Failed), ("N", "sig2", BuildResult.Succeeded), ("M", "sig2", BuildResult.Succeeded), ("R", "sig1", BuildResult.Failed) },
            _ => names.Select(n => (n, "sig1", BuildResult.Failed)).ToArray(),
        };
        foreach (var (name, sig, result) in expected)
            Assert.Equal((sig, result), (ledger[Id(name)].BuiltSignature, ledger[Id(name)].LastResult));
        if (outcome == "no progress") Assert.Equal("sig2", ledger[Id("N")].FailedSignature);
        if (outcome == "cap reached") Assert.Equal("m1", ledger[Id("M")].CycleMemberTerm); // derlenen oturmuş üyenin kanıtı yazıldı
        // Eski pinin KESİN terim değeri korunur: güvenilmeyen (patlayan ya da kesilen) N'nin yeni terimi n2 deftere girmez;
        // oturmuş N'ninki (tavan) girer.
        Assert.Equal(outcome == "cap reached" ? "n2" : "n1", ledger[Id("N")].CycleMemberTerm);

        var follow = new RoundRecorder();
        using var next = await ResolveAsync(store, disk, ChainPlan("sig2", "n2"), follow.Invoker((_, _) => Ok()));
        string[] expectedFollow = outcome switch
        {
            "no progress" => ["N#1"],
            "cap reached" => ["R#1", "X#1"],
            _ => ["M#1", "N#1", "R#1", "X#1"],
        };
        Assert.Equal(expectedFollow, follow.Calls.Order(StringComparer.Ordinal));
        foreach (string call in expectedFollow) // takip koşusunun derlediği her üyenin nedeni: güvenilir kayıt yok
            Assert.Contains(CycleDecisionLines.RoundOneNeed(call[..^2], CycleMemberNeed.NoTrustedRecordReason), next.DecisionLog,
                StringComparison.Ordinal);
    });

    /// <summary>[D3 · tavan] Tavana dayanan grupta HİÇ bayat olmamış taşınan üye oturmuştur: <c>skipped — up to date
    /// (carried)</c> raporlanır, kaydı yeni bileşik imzayla yenilenir — NoProgress'teki taşınan oturmuş üyeyle aynı yol
    /// (<see cref="a_group_without_a_verdict_keeps_only_its_settled_members"/>'ın tavan kolunda taşınan X bayattır).
    /// Grup [A, B, C, D] iki ayrık çifttir, A ↔ B ve C ↔ D (koordinatör grubu plandan alır). Yalnız A değişik; A ve B her
    /// derlemede yüzeyini oynatır: tur 1 A ⇒ B bayat; tur 2 B ⇒ A bayat; tur 3 A ⇒ B bayat ⇒ tavan. C ve D tur 1'de taşındı
    /// ve okudukları yüzey hiç oynamadı; A'nın okuduğu B yüzeyi nihaidir ⇒ güvenilir; son turda bayat B geçersizlenir.</summary>
    [Fact]
    public Task a_cap_reached_group_reports_its_never_stale_carried_members_up_to_date() => InCacheRootAsync(async cacheRoot =>
    {
        var cycle = CyclePlanOf(["A", "B", "C", "D"],
            Node("A", deps: ["B"], inCycle: true), Node("B", deps: ["A"], inCycle: true),
            Node("C", deps: ["D"], inCycle: true), Node("D", deps: ["C"], inCycle: true));
        var store = new BuildStateStore(cacheRoot);
        var disk = new SurfaceDisk();
        foreach (string name in new[] { "A", "B", "C", "D" }) disk.Set(name, name.ToLowerInvariant() + "1");
        await ConvergeOnceAsync(store, disk, MemberSkipPlan(cycle, "sig1", ("A", "a1"), ("B", "b1"), ("C", "c1"), ("D", "d1")));
        var rec = new RoundRecorder();
        // Derlenen her üye yüzeyini oynatır (A1, B1, A2); C ve D hiç derlenmez.
        using var h = await ResolveAsync(store, disk,
            MemberSkipPlan(cycle, "sig2", ("A", "a2"), ("B", "b1"), ("C", "c1"), ("D", "d1")),
            rec.Invoker((name, round) => { disk.Set(name, name + round); return Ok(); }));

        Assert.Equal(["A#1", "B#1", "A#2"], rec.Calls);
        var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
        Assert.Equal((CycleOutcome.CapReached, 3), (completed.Outcome, completed.Rounds));
        Assert.Equal([(Id("C"), SkipReasons.UpToDate, false), (Id("D"), SkipReasons.UpToDate, false)],
            h.Events.OfType<ProjectSkippedEvent>().Select(e => (e.ProjectId, e.Reason, e.CycleUnconverged))
                .OrderBy(e => e.ProjectId, StringComparer.Ordinal));
        foreach (string carried in new[] { "C", "D" })
            Assert.Contains($"{carried}: skipped — {SkipReasons.UpToDate} ({CycleDecisionLines.CarriedDetail})", h.DecisionLog,
                StringComparison.Ordinal);
        var succeeded = h.Events.OfType<ProjectSucceededEvent>().ToDictionary(e => NameOf(e.ProjectId));
        Assert.Equal(["A", "B"], succeeded.Keys.Order(StringComparer.Ordinal));
        Assert.True(succeeded["A"].Trusted);
        Assert.False(succeeded["B"].Trusted); Assert.True(succeeded["B"].CycleUnsettled);
        var ledger = store.Load();
        foreach (var (name, sig, result) in new[] { ("A", "sig2", BuildResult.Succeeded), ("B", "sig1", BuildResult.Failed),
                     ("C", "sig2", BuildResult.Succeeded), ("D", "sig2", BuildResult.Succeeded) })
            Assert.Equal((sig, result), (ledger[Id(name)].BuiltSignature, ledger[Id(name)].LastResult));
    });

    [Fact] // hashMode kapalı (çıktı haritası yok) ⇒ bugünkü davranış: güvenilir kayıt olsa da üye kararı yok, herkes derlenir
    public Task without_surface_evidence_round_one_still_compiles_everyone() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        var plan = TwoMembers("sig2", "a2", "b1");
        plan = plan with { Incremental = plan.Incremental! with { OutputsById = null } };
        var rec = new RoundRecorder();
        using var h = await ResolveAsync(store, disk, plan, rec.Invoker((_, _) => Ok()));

        Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls);               // kanıtsız: iki ardışık yeşil tur
        Assert.Equal(2, Assert.Single(h.Events.OfType<CycleCompletedEvent>()).CompiledCount);
        Assert.Empty(h.Events.OfType<ProjectSkippedEvent>());
        Assert.DoesNotContain(CycleDecisionLines.RoundOneNeed("A", ""), h.DecisionLog, StringComparison.Ordinal);
    });

    [Fact] // Fast benzeri koşu: üye terim haritası BOŞ (null değil) ⇒ düşmez, herkes "no member term" ile derlenir
    public Task an_empty_member_term_map_compiles_every_member_without_failing() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        var plan = TwoMembers("sig1", "a1", "b1");
        plan = plan with { Incremental = plan.Incremental! with { MemberTermById = new Dictionary<string, string>() } };
        var rec = new RoundRecorder();
        using var h = await ResolveAsync(store, disk, plan, rec.Invoker((_, _) => Ok()));

        Assert.Equal(["A#1", "B#1"], rec.Calls);
        Assert.Equal(CycleOutcome.Converged, Assert.Single(h.Events.OfType<CycleCompletedEvent>()).Outcome);
        foreach (string name in new[] { "A", "B" })
        {
            Assert.Contains(CycleDecisionLines.RoundOneNeed(name, CycleMemberNeed.NoMemberTermReason), h.DecisionLog,
                StringComparison.Ordinal);
            Assert.Null(store.Load()[Id(name)].CycleMemberTerm);             // terim yok ⇒ alan null (KeyNotFound DEĞİL)
        }
    });

    /// <summary>[R3b fix round 2] Grup içi bağımlılık kümesi okuma durumunu besleyen AYNI kardeş haritasından gelir:
    /// önceki yakınsamış koşunun kaydından bir kardeşin okuma girdisi düşmüşse üye, içeriği değişmese de tur 1'de
    /// derlenir (eksik kayıt güvenilmez).</summary>
    [Fact]
    public Task a_member_whose_record_dropped_a_sibling_read_is_compiled_in_round_one() => InCacheRootAsync(async cacheRoot =>
    {
        var store = new BuildStateStore(cacheRoot);
        var disk = new SurfaceDisk();
        foreach (string name in new[] { "A", "B", "C" }) disk.Set(name, name.ToLowerInvariant() + "1");
        var plan = MemberSkipPlan(HubCycle("B", "C"), "sig1", ("A", "a1"), ("B", "b1"), ("C", "c1"));
        await ConvergeOnceAsync(store, disk, plan);
        var a = store.Load()[Id("A")];
        store.Upsert(a with
        {
            CycleReadSurfaces = [.. a.CycleReadSurfaces!.Where(s => !string.Equals(s.Producer, Id("C"), StringComparison.OrdinalIgnoreCase))],
        });
        var rec = new RoundRecorder();
        using var h = await ResolveAsync(store, disk, plan, rec.Invoker((_, _) => Ok()));

        Assert.Equal(["A#1"], rec.Calls);
        Assert.Contains(CycleDecisionLines.RoundOneNeed("A", CycleMemberNeed.NoTrustedRecordReason), h.DecisionLog,
            StringComparison.Ordinal);
    });

    /// <summary>Döngü dışı başarı persist'i taze kayıt kurar ve döngü alanlarını NULL yazar ("mevcudu koru" DEĞİL):
    /// döngü kanıtı taşıyan kaydın projesi Rebuild'de derlenince eski kanıt silinir, bir sonraki Resolve onu gerekli
    /// sayar (güvenli taraf).</summary>
    [Fact]
    public Task a_non_cycle_success_writes_the_cycle_fields_as_null() => InCacheRootAsync(async cacheRoot =>
    {
        var store = new BuildStateStore(cacheRoot);
        store.Upsert(new BuildState(Id("A"), "old", "c0", BuildResult.Succeeded, DateTimeOffset.UtcNow, "main",
            CycleMemberTerm: "a1", CycleReadSurfaces: [new CycleReadSurface(Id("B"), SurfaceDisk.PathOf("B"), "b1")],
            CycleEngineFingerprint: "engine"));
        var plan = PlanOf(Node("A")) with { Incremental = RunCoordinatorTests.Incremental("A") };
        using var h = new Harness(plan, new RoundRecorder().Invoker((_, _) => Ok()), stateStore: store);
        await h.Sut.StartAsync(Start(RunMode.Rebuild), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var a = store.Load()[Id("A")];
        Assert.Equal("sig", a.BuiltSignature);
        Assert.Null(a.CycleMemberTerm);
        Assert.Null(a.CycleReadSurfaces);
        Assert.Null(a.CycleEngineFingerprint);
    });

    // ---------------------------------------------------------------- 15) [R3c2] seçici tur 1'in sertleştirmesi

    /// <summary>X → M, N → X, M ↔ R, M ve R → N (build order X, N, M, R): X yalnız M'yi okur; N değişince M ve R bayatlar.
    /// M ile R birbirini okur: ikisi aynı turda derlenirken önce M girer (okunma ve komşu sayısı eşit ⇒ build order,
    /// <c>CycleRoundLevels</c>) ve R'nin ESKİ yüzeyini okur. Terimler x1/m1/r1, N'ninki parametre.</summary>
    private static RunPlan ChainPlan(string signature, string termN) => MemberSkipPlan(CyclePlanOf(["X", "N", "M", "R"],
            Node("X", deps: ["M"], inCycle: true), Node("N", deps: ["X"], inCycle: true),
            Node("M", deps: ["N", "R"], inCycle: true), Node("R", deps: ["N", "M"], inCycle: true)),
        signature, ("X", "x1"), ("N", termN), ("M", "m1"), ("R", "r1"));

    /// <summary>
    /// [kanıt varken yakınsama yalnız kanıtla] Yüzey kanıtı varken iki ardışık yeşil tur yakınsama DEĞİLDİR: tur 2'den
    /// itibaren yalnız bayat üyeler derlenir ve derlenen bir üyenin yüzeyi başka bir üyenin okuduğu sabiti oynatabilir.
    /// TAM tur 1 (taşınan yok, build-order B → A) yeşil biter; B, A'yı ESKİ yüzeyiyle okuduğundan tur 2 yalnız B'yi
    /// derler ve B'nin KENDİ yüzeyi de oynar (A'nın sabiti B'nin yüzeyine girer) ⇒ A, B'nin tur-1 yüzeyine bağlı kalır:
    /// bayat. İki tur da yeşildir; yine de A, B'nin eski yüzeyine bağlı bir çıktıdır.
    /// <para><b>[DEĞİŞEN KURAL] Eski iddia:</b> iki ardışık yeşil tur yakınsamadır — kanıt olsun olmasın. Bu senaryo tur 2'de
    /// "converged; stale=1 [A]" ile biterdi ve A, B'nin eski yüzeyiyle güvenilir persist edilirdi (sonraki Build SCC'yi
    /// derlemezdi). <b>Neden değişti:</b> kanıt varken yeşil-yeşil "herkes nihai API'ye bağlandı" demez; bunu yalnız kanıt
    /// söyler (<see cref="CycleRoundPolicy"/>). Doğrusu: tur 3 A'yı derler ve kimse bayat kalmayınca <c>stale=0</c> ile
    /// yakınsar.</para>
    /// </summary>
    [Fact]
    public async Task two_green_rounds_with_a_stale_member_do_not_converge_the_group()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            SeedGreen(store, "A");
            SeedGreen(store, "B");
            var disk = new SurfaceDisk();
            disk.Set("A", "a-old");
            disk.Set("B", "b-old");
            var plan = HashModePlan(CyclePlanOf(["B", "A"],
                Node("B", deps: ["A"], inCycle: true),
                Node("A", deps: ["B"], inCycle: true)), "A", "B");
            var rec = new RoundRecorder();
            // A yalnız tur 1'de oynar (a-old → a-new): ondan ÖNCE derlenen B eski yüzeyi okumuştur. B tur 1'de yüzeyini
            // korur, tur 2'de DEĞİŞTİRİR (b-old → b2): A tur-1 yüzeyini (b-old) okumuştu.
            var invoker = rec.Invoker((name, round) =>
            {
                disk.Set(name, name == "A" ? "a-new" : round == 1 ? "b-old" : "b2");
                return Ok();
            });
            using var h = new Harness(plan, invoker, stateStore: store, apiSurface: disk.Read);

            await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            // Tur 1: ikisi de. Tur 2: yalnız B (a-old okumuştu). Tur 3: yalnız A (b-old okumuştu).
            Assert.True(rec.Calls.SequenceEqual(["B#1", "A#1", "B#2", "A#2"]),
                string.Join(", ", rec.Calls) + "\n" + h.DecisionLog);
            Assert.Equal([2, 1, 1], h.Events.OfType<CycleRoundStartedEvent>().Select(e => e.MemberCount));
            var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
            Assert.Equal((CycleOutcome.Converged, 3), (completed.Outcome, completed.Rounds));
            // Karar günlüğü: tur 2 yeşil-yeşildir ama A bayat ⇒ devam; yakınsama yalnız kimse bayat kalmayınca (tur 3).
            string log = h.DecisionLog;
            Assert.Contains("round 2: continue; stale=1 [A]", log);
            Assert.Contains("round 3: converged; stale=0 []", log);
            // A'nın kaydı B'nin NİHAİ yüzeyini taşır, bayat tur-1 yüzeyini DEĞİL — sonraki Build bu kayda güvenir.
            var aRead = Assert.Single(store.Load()[Id("A")].CycleReadSurfaces!);
            Assert.Equal((Id("B"), "b2"), (aRead.Producer, aRead.Hash));
            Assert.All(h.Events.OfType<ProjectSucceededEvent>(), e => Assert.True(e.Trusted));
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    /// <summary>[karar 3 · Important I1] Taşınan üyeli tur 1 "iki ardışık yeşil tur" kuralına girmez. A değişir ve yüzeyi
    /// oynar, B taşınır ⇒ tur 2 B'yi derler; B'nin KENDİ yüzeyi de oynar (A'dan gelen bir sabit gibi) ⇒ A bayat. Taşınan
    /// B'nin kayıttan gelen Succeeded'ı tur 1'i "yeşil" saydırsaydı tur 2 "converged; stale=1 [A]" derdi ve A, B'nin eski
    /// yüzeyiyle güvenilir persist edilirdi. Doğrusu: tur 3 A'yı derler; iki kayıt da NİHAİ kardeş yüzeyini taşır.
    /// <para><b>[DEĞİŞEN KURAL]</b> Bu sonucu eskiden yalnız <c>previousFailed = null</c> koruması sağlıyordu: policy'nin
    /// iki-yeşil kuralı <c>staleNow</c>'a bakmıyordu. İki-yeşil kuralı artık yalnız yüzey kanıtı YOKKEN çalışır (yukarıdaki
    /// test): kanıt varken bu sonucu policy de sağlar; koruma <c>staleNow</c>'ın null olduğu turlar için yerinde kalır.</para></summary>
    [Fact]
    public Task a_round_one_with_carried_members_does_not_count_toward_two_green_rounds() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        var rec = new RoundRecorder();
        using var h = await ResolveAsync(store, disk, TwoMembers("sig2", "a2", "b1"),
            rec.Invoker((name, _) => { disk.Set(name, name == "A" ? "a2" : "b2"); return Ok(); }));

        Assert.True(rec.Calls.SequenceEqual(["A#1", "B#1", "A#2"]), string.Join(", ", rec.Calls) + "\n" + h.DecisionLog);
        var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
        Assert.Equal((CycleOutcome.Converged, 3), (completed.Outcome, completed.Rounds));
        var aRead = Assert.Single(store.Load()[Id("A")].CycleReadSurfaces!);
        Assert.Equal((Id("B"), "b2"), (aRead.Producer, aRead.Hash));
        var bRead = Assert.Single(store.Load()[Id("B")].CycleReadSurfaces!);
        Assert.Equal((Id("A"), "a2"), (bRead.Producer, bRead.Hash));
        // [R3c3] Log ilk derlemede açılır ve turlar boyunca AÇIK kalır (yeniden açmak truncate ederdi): A iki kez, B bir kez
        // derlendi ⇒ A'nın logu B'ninkinin iki katı satır taşır, ikisi de TEK dosyada.
        var logs = h.LogWriters[^1];
        string[] aLog = File.ReadAllLines(logs.ProjectLogPath(Id("A")));
        string[] bLog = File.ReadAllLines(logs.ProjectLogPath(Id("B")));
        Assert.NotEmpty(bLog);
        Assert.Equal(2 * bLog.Length, aLog.Length);
        // [R3 final · M3] İlk satır DOĞRUDAN pinlenir (satır sayısı oranı, tüm tembel açılışlara eklenen bir satırı yakalamaz):
        // tur 2'de ilk kez derlenen taşınan B'nin ve tur 1'de derlenen A'nın ilk satırı gerçek komut satırıdır.
        Assert.Equal(ExpectedBuildCommandLine(Id("B")), bLog[0]);
        Assert.Equal(ExpectedBuildCommandLine(Id("A")), aLog[0]);
    });

    /// <summary>[restore kapısı] Tur 2'de İLK kez derlenen taşınan üye restore kararından geçer: kayıttan gelen Succeeded'ı
    /// "önceki turda derlendi" sayılmaz. Kanıt: Converged sonrası kaydın <c>PackagesConfigHash</c>'i dolu (karar anında
    /// okunan özet). Kurulum restore-once testiyle ORTAK: <see cref="WithPackagesConfigAsync"/>.</summary>
    [Fact]
    public Task a_carried_member_first_compiled_in_round_two_goes_through_the_restore_decision() => InCacheRootAsync(async cacheRoot =>
    {
        const string px = "RestoreCarriedX";                  // packages.config'li, tur 1'de taşınan
        const string py = "RestoreCarriedY";
        await WithPackagesConfigAsync(px, async () =>
        {
            var cycle = CyclePlanOf([px, py], Node(px, deps: [py], inCycle: true), Node(py, deps: [px], inCycle: true));
            var store = new BuildStateStore(cacheRoot);
            var disk = new SurfaceDisk();
            disk.Set(px, "x1");
            disk.Set(py, "y1");
            await ConvergeOnceAsync(store, disk, MemberSkipPlan(cycle, "sig1", (px, "x1"), (py, "y1")));
            Assert.NotNull(store.Load()[Id(px)].PackagesConfigHash);
            // Y değişti ve yüzeyi oynar ⇒ X tur 1'de taşınır, tur 2'de bayat olarak İLK kez derlenir.
            var rec = new RoundRecorder();
            using var h = await ResolveAsync(store, disk, MemberSkipPlan(cycle, "sig2", (px, "x1"), (py, "y2")),
                rec.Invoker((name, _) => { if (name == py) disk.Set(py, "y2"); return Ok(); }));

            Assert.Equal([$"{py}#1", $"{px}#1"], rec.Calls);
            Assert.Equal(CycleOutcome.Converged, Assert.Single(h.Events.OfType<CycleCompletedEvent>()).Outcome);
            Assert.NotNull(store.Load()[Id(px)].PackagesConfigHash);
        });
    });

    /// <summary>[kesme kapısı] Branch kesmesinden sonra biten grupta hiçbir sonucun arkasında durulmaz (ReportProjectResult'taki
    /// kapı) — grup kanıtla yakınsamış olsa bile: derlenen ve oturmuş A'nın başarısı GÜVENİLMEZ raporlanır ve defterde kanıtsız
    /// hata olur (bu koşunun imzası yazılmaz); taşınan B'nin defteri YENİLENMEZ — taşınan üyenin defter yenilemesi de o
    /// bayrağa uyar.</summary>
    [Fact]
    public Task an_interrupted_run_trusts_no_member_of_a_converged_group() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        var bBefore = store.Load()[Id("B")];
        var inFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rec = new RoundRecorder();
        // Yalnız A değişik: A derlenirken branch kesmesi gelir; A biter, grup tur 1'de kanıtla yakınsar, B taşınır.
        var invoker = rec.Invoker(async (_, _, _) => { inFlight.TrySetResult(); await release.Task; return Ok(); });
        using var h = new Harness(TwoMembers("sig2", "a2", "b1"), invoker, stateStore: store, apiSurface: disk.Read);

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await inFlight.Task.WaitAsync(Limit);
        Assert.True(h.Sut.TryRequestStop(StopKind.Interrupt));
        release.SetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A#1"], rec.Calls);
        Assert.Contains("round 1: converged", h.DecisionLog, StringComparison.Ordinal); // A oturmuştu: tek engel kesme
        var aSucceeded = Assert.Single(h.Events.OfType<ProjectSucceededEvent>());
        Assert.Equal((Id("A"), false), (aSucceeded.ProjectId, aSucceeded.Trusted));
        var a = store.Load()[Id("A")];
        Assert.Equal((BuildResult.Failed, "sig1", (string?)null), (a.LastResult, a.BuiltSignature, a.FailedSignature));
        Assert.Equal(bBefore, store.Load()[Id("B")]);                        // taşınan üyenin kaydı aynen
    });

    /// <summary>[kesilme garantisi] Grup başı bloğu (yüzey hash'i, başlık satırları) üyelerin raporlanmasını garanti eden
    /// try'ın İÇİNDEDİR: orada fırlayan beklenmeyen bir istisna da her üyeyi Failed raporlatır, grup tamamlanır ve koşu
    /// biter — asılmaz (sınırlı bekleme). Neden metni <c>group start failed: iç mesaj</c>'dır.
    /// <para>[R3c3] Eski iddia: neden istisna metniydi ve genel catch'ten <c>invoke error: One or more errors occurred.
    /// (…)</c> olarak çıkıyordu — "invoke error" bir yüzey hash'i hatasını anlatmaz, Parallel.ForEach'in AggregateException
    /// sarmalı da iç mesajı gizler. Metin artık grup başı önekiyle ve açılmış iç mesajla pinlidir.</para></summary>
    [Fact]
    public async Task an_exception_at_group_start_fails_every_member_and_the_run_completes()
    {
        var rec = new RoundRecorder();
        using var h = new Harness(HashModePlan(TwoMemberCycle(), "A", "B"), rec.Invoker((_, _) => Ok()),
            apiSurface: _ => throw new InvalidOperationException("surface reader exploded"));

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Empty(rec.Calls);
        var failed = h.Events.OfType<ProjectFailedEvent>().ToList();
        Assert.Equal([Id("A"), Id("B")], failed.Select(e => e.ProjectId));
        Assert.All(failed, e => Assert.Equal("group start failed: surface reader exploded", e.Reason));
        Assert.Empty(h.Events.OfType<CycleCompletedEvent>());
    }

    /// <summary>[R3c3] Grubun ilk dispatch'inden SONRA fırlayan beklenmeyen istisna "invoke error" olarak kalır (grup başı
    /// hatasından AYRI): her üye Failed raporlanır, neden istisnanın iç mesajıdır.</summary>
    [Fact]
    public async Task an_exception_after_the_first_dispatch_is_reported_as_an_invoke_error()
    {
        var rec = new RoundRecorder();
        using var h = new Harness(TwoMemberCycle(),
            rec.Invoker((_, _) => throw new InvalidOperationException("compiler host exploded")));

        await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var failed = h.Events.OfType<ProjectFailedEvent>().ToList();
        Assert.Equal([Id("A"), Id("B")], failed.Select(e => e.ProjectId));
        Assert.All(failed, e => Assert.Equal("invoke error: compiler host exploded", e.Reason));
    }

    /// <summary>
    /// [D3] Kullanıcı senaryosu: 16 üye yeşil, tek üyede copy-lock ⇒ NoProgress. Eski kural her üyeyi geçersizliyordu ve
    /// bir sonraki Build 17'sini derliyordu. Yeni kural: yüzeyleri oturmuş (son turun bayat kümesinde olmayan) yeşil ya
    /// da taşınan üye GÜVENİLİR persist edilir; yalnız patlayan üye kanıtlı hata alır; takip koşusu yalnız onu derler.
    /// </summary>
    [Fact]
    public Task a_hopeless_member_does_not_poison_its_settled_siblings() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        var bBefore = store.Load()[Id("B")];
        var rec = new RoundRecorder();
        // Yalnız A değişik; A patlar (girdileri oturmuş ⇒ tur 1'de NoProgress). B taşınır ve oturmuştur.
        using (var h = await ResolveAsync(store, disk, TwoMembers("sig2", "a2", "b1"), rec.Invoker((name, _) => name == "A" ? Exit(1) : Ok())))
        {
            Assert.Equal(["A#1"], rec.Calls);
            Assert.Equal(CycleOutcome.NoProgress, Assert.Single(h.Events.OfType<CycleCompletedEvent>()).Outcome);
            var failed = Assert.Single(h.Events.OfType<ProjectFailedEvent>());
            Assert.Equal((Id("A"), true), (failed.ProjectId, failed.Evidence));
            var skipped = Assert.Single(h.Events.OfType<ProjectSkippedEvent>());
            Assert.Equal((Id("B"), SkipReasons.UpToDate), (skipped.ProjectId, skipped.Reason));
        }
        var ledger = store.Load();
        Assert.Equal((BuildResult.Failed, "sig2"), (ledger[Id("A")].LastResult, ledger[Id("A")].FailedSignature));
        Assert.Equal((BuildResult.Succeeded, "sig2"), (ledger[Id("B")].LastResult, ledger[Id("B")].BuiltSignature));
        Assert.Equal(bBefore.CycleReadSurfaces, ledger[Id("B")].CycleReadSurfaces); // taşınan üyenin kanıtı aynen

        // Takip koşusu (kaynak değişmedi): yalnız A derlenir, B yine taşınır.
        var follow = new RoundRecorder();
        using var next = await ResolveAsync(store, disk, TwoMembers("sig2", "a2", "b1"), follow.Invoker((_, _) => Ok()));
        Assert.Equal(["A#1"], follow.Calls);
        Assert.Contains(CycleDecisionLines.RoundOneNeed("A", CycleMemberNeed.NoTrustedRecordReason), next.DecisionLog, StringComparison.Ordinal);
        Assert.DoesNotContain(CycleDecisionLines.RoundOneNeed("B", ""), next.DecisionLog, StringComparison.Ordinal);
    });

    /// <summary>[D3 · Review Focus 3] Son turun bayat kümesi yoksa yakınsamayan grupta hiçbir yeşil güvenilmez — bugünkü
    /// kural aynen. İki yol: kanıt hiç yok (çıktı haritası yok) ve kanıt koşu ortasında düştü (A'nın derleme sonrası yüzeyi
    /// okunamadı — kilitli dosya ⇒ grup tam-tur davranışına döner, NoProgress iki turla gelir). Build modunda pinlenir;
    /// Cycles karşılığı <see cref="non_converged_group_persists_nothing_even_for_green_members"/>.</summary>
    [Theory]
    [InlineData("no evidence")]
    [InlineData("evidence lost")]
    public Task without_surface_evidence_a_non_converged_group_trusts_no_success(string evidence) => InCacheRootAsync(async cacheRoot =>
    {
        var store = new BuildStateStore(cacheRoot);
        SeedGreen(store, "A"); SeedGreen(store, "B");
        var disk = new SurfaceDisk();
        disk.Set("A", "a1");
        disk.Set("B", "b1");
        var plan = evidence == "no evidence"
            ? TwoMemberCycle() with { Incremental = RunCoordinatorTests.Incremental("A", "B") } // kanıt yok
            : HashModePlan(TwoMemberCycle(), "A", "B");
        int aCompiled = 0;
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, _) =>
        {
            if (name == "B") return Exit(1);
            Interlocked.Exchange(ref aCompiled, 1);
            return Ok();
        });
        // A derlendikten sonra yüzeyi okunamaz: grup başı hash'i kanıtı açar, tur 1'deki derleme sonrası okuma onu düşürür.
        using var h = new Harness(plan, invoker, stateStore: store,
            apiSurface: path => Volatile.Read(ref aCompiled) == 1 && path == SurfaceDisk.PathOf("A") ? null : disk.Read(path));
        await h.Sut.StartAsync(Start(RunMode.Build), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A#1", "B#1", "A#2", "B#2"], rec.Calls); // kanıtsız NoProgress: aynı küme iki kez patladı
        Assert.Equal(CycleOutcome.NoProgress, Assert.Single(h.Events.OfType<CycleCompletedEvent>()).Outcome);
        Assert.False(Assert.Single(h.Events.OfType<ProjectSucceededEvent>()).Trusted);
        Assert.Equal((BuildResult.Failed, "old"), (store.Load()[Id("A")].LastResult, store.Load()[Id("A")].BuiltSignature));
        if (evidence == "evidence lost") // kanıt gerçekten açıktı ve tur 1'de düştü: kayıp satırı A'nın dosyasını adlandırır
            Assert.Contains(CycleDecisionLines.EvidenceUnavailable("A", "A", SurfaceDisk.PathOf("A"), CycleDecisionLines.UnreadableReason),
                h.DecisionLog, StringComparison.Ordinal);
    });

    // ---------------------------------------------------------------- 16) [D7-b] grup dışı upstream'in yüzeyi

    /// <summary>U (grup dışı, güncel — pre-skip) → A ↔ B; yalnız A, U'yu okur.</summary>
    private static RunPlan UpstreamCycle() => CyclePlanOf(["A", "B"],
        Node("U", willBuild: false), Node("A", deps: ["B", "U"], inCycle: true), Node("B", deps: ["A"], inCycle: true));

    private static Dictionary<string, string> SigOf(string signature, params string[] names) =>
        names.ToDictionary(Id, _ => signature, StringComparer.OrdinalIgnoreCase);

    /// <summary>[D7-b] Grup dışı upstream U'nun yüzeyi değişmedi (gövde değişikliği): üyelerin kendi terimi aynı, U'nun yüzeyi
    /// kayıttakiyle aynı ⇒ herkes taşınır, grup 0 derlemeyle yakınsar. U'nun yüzeyi oynadıysa yalnız U'yu okuyan A derlenir.</summary>
    [Theory]
    [InlineData(false, new string[0])]
    [InlineData(true, new[] { "A#1" })]
    public Task an_outside_upstream_change_compiles_only_the_members_whose_read_surface_moved(bool surfaceMoved, string[] expectedCalls) => InCacheRootAsync(async cacheRoot =>
    {
        var store = new BuildStateStore(cacheRoot);
        var disk = new SurfaceDisk();
        foreach (string n in new[] { "U", "A", "B" }) disk.Set(n, n.ToLowerInvariant() + "1");
        var plan = MemberSkipPlan(UpstreamCycle(), "sig1", ("A", "a1"), ("B", "b1"));
        plan = plan with { Incremental = plan.Incremental! with { OutputsById = SurfaceDisk.OutputsFor("U", "A", "B") } };
        await ConvergeOnceAsync(store, disk, plan); // U pre-skip (willBuild false), A ve B derlenir; A'nın kaydı U'nun yüzeyini taşır
        Assert.Equal("u1", Assert.Single(store.Load()[Id("A")].DependencySurfaces!).Hash);

        if (surfaceMoved) disk.Set("U", "u2"); // U koşular arasında (satırdan / VS'de) derlendi ve API'si değişti
        var rec = new RoundRecorder();
        using var h = await ResolveAsync(store, disk, plan with { Incremental = plan.Incremental! with { SignatureById = SigOf("sig2", "A", "B") } },
            rec.Invoker((_, _) => Ok()));

        Assert.Equal(expectedCalls, rec.Calls);
        Assert.Equal(CycleOutcome.Converged, Assert.Single(h.Events.OfType<CycleCompletedEvent>()).Outcome);
        if (surfaceMoved)
            Assert.Contains(CycleDecisionLines.RoundOneNeed("A", CycleMemberNeed.DependencySurfaceMovedPrefix + SurfaceDisk.PathOf("U")),
                h.DecisionLog, StringComparison.Ordinal);
    });
}
