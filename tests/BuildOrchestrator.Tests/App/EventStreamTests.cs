using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [D3/T?] design-v1 Event stream paneli. SAF çekirdek (<see cref="StreamComposer"/>) fırtına/hata/aktif-satır
/// kararları <c>[Fact]</c>; görünüm (<see cref="EventStreamView"/>: parıltı-once, sayaç, seçim şeridi) GERÇEKTEN
/// kurulur (ekran dışı pencere + merge zinciri) <c>[StaFact]</c>.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class EventStreamTests
{
    // ============================================================ SAF ÇEKİRDEK ([Fact])

    [Fact]
    public void Burst_events_under_three_hundred_forty_milliseconds_are_printed_instantly()
    {
        var c = new StreamComposer();
        var first = c.Push(isFail: false, nowMs: 1000);  // ilk emit → fırtına DEĞİL → daktilo (instant=false)
        var burst = c.Push(isFail: false, nowMs: 1100);  // 100ms sonra (<340) → fırtına → ANINDA

        Assert.False(first.Instant);
        Assert.True(burst.Instant);
    }

    [Fact]
    public void Failure_events_skip_the_typewriter_entirely()
    {
        var c = new StreamComposer();
        c.Push(isFail: false, nowMs: 1000);
        var fail = c.Push(isFail: true, nowMs: 5000); // 4000ms sonra → fırtına DEĞİL, ama hata → ANINDA

        Assert.True(fail.Instant);
    }

    [Fact]
    public void The_active_line_jumps_to_the_most_recently_started_building_project()
    {
        var c = new StreamComposer();
        c.StartBuilding("A", "A", 1000);
        c.StartBuilding("B", "B", 1100);
        c.StartBuilding("C", "C", 1200);
        Assert.Equal("C", c.ActiveProjectId); // en son başlayan aktif satırdır

        c.FinishBuilding("C", 1300);
        Assert.Equal("B", c.ActiveProjectId);   // izlenen bitince → en son başlayan hâlâ building projeye ATLAR
        Assert.Equal("B building…", c.ActiveText);
    }

    // ============================================================ GÖRÜNÜM ([StaFact])

    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    // [Task 4] WorkspaceTopologyEvent düğümü — _cycleGroups üyeliğini kurmak için Cycles listesiyle birlikte kullanılır.
    private static ProjectNode Node(string id, string name, int buildOrder, bool inCycle = false) =>
        new(id, name, id, SolutionNames: [], Dependencies: [], buildOrder, LayerIndex: null, LayerName: null, inCycle, WillBuild: null);

    private static (EventStreamView view, Window window, System.Windows.Controls.Border host) Realize(
        RunViewModel vm, bool forceAnimations = false)
    {
        var host = DsResources.NewHost();
        var view = new EventStreamView { AnimationsEnabledProvider = () => forceAnimations, DataContext = vm };
        var window = DsResources.Realize(host, view);
        return (view, window, host);
    }

    [StaFact]
    public void Event_counter_reports_the_full_buffer_not_the_rendered_slice()
    {
        var vm = NewVm();
        // 160 skipped olay → tampon 160 (≤260), render dilimi 150 ile sınırlı.
        for (int i = 0; i < 160; i++)
            vm.OnEvent(new ProjectSkippedEvent("r1", $@"C:\p\proj{i}.csproj", SkipReasons.UpToDate));

        var (view, window, _) = Realize(vm);

        Assert.Equal("160 events", view.Counter.Text);              // TAM tampon (render dilimi DEĞİL)
        Assert.Equal(StreamComposer.RenderSlice, view.Rows.Count);  // yalnız 150 satır render edildi
        Assert.Equal(160, vm.StreamEventCount);
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T4 · n6 · fix-1 · B3] design-v1 README:48 "DAİMA tabular rakam" — panelin aktif-satır metni
    /// (<c>PART_ActiveText</c>, <c>EventStreamView.xaml:41</c>) VE her satırın kendi akan metni
    /// (<c>EventStreamRow._text</c>, <c>EventStreamView.xaml.cs</c>'in <c>_text</c> için yaptığı
    /// <c>NumeralAlignment</c> ataması — fix-1'de eklenen altıncı yer, önceki
    /// sürüm bunu kaçırıyordu) mono taşıyan üretim yerlerindendir. Envanter/kapsam kararı XML doc'u:
    /// <see cref="ProjectRowTests.The_project_row_sha_and_duration_columns_are_tabular"/>.</summary>
    [StaFact]
    public void The_active_line_and_row_text_are_tabular()
    {
        var vm = NewVm();
        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\a.csproj", SkipReasons.UpToDate));
        var (view, window, _) = Realize(vm);

        Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(view.ActiveText));
        Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(view.Rows[0].Text));
        GC.KeepAlive(window);
    }

    /// <summary>[DEĞİŞEN KURAL] Atlanan satır <c>text-faint</c> (#54545c) değil <c>text-dim</c> (#76767e) —
    /// geri planda kalsın ama okunabilsin (kullanıcı kararı; prototip BuildApp.jsx:635-638'den bilinçli sapma).
    /// <c>ok</c> satırı değişmedi, yani skip hâlâ onun altındadır.</summary>
    [Fact]
    public void A_skipped_line_is_dim_not_faint()
    {
        var vm = NewVm();
        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\skipped.csproj", SkipReasons.UpToDate));

        Assert.Equal("Brush.TextDim", vm.StreamEvents[^1].TextBrushKey);
    }

    [StaFact]
    public void Selected_row_gets_a_two_pixel_amber_stripe_and_a_raised_surface()
    {
        const string id = @"C:\p\a.csproj";
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", id, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", id, 1200)); // → tıklanabilir "A built (1.2s)" ok satırı

        var (view, window, host) = Realize(vm);
        var row = view.Rows.Single(r => r.ViewModel?.ProjectId == id);

        Assert.Equal(Visibility.Collapsed, row.SelectionStripe.Visibility); // seçili değil → şerit yok

        vm.SelectProject(id);
        view.UpdateLayout();

        Assert.Equal(Visibility.Visible, row.SelectionStripe.Visibility);
        Assert.Equal(2.0, row.SelectionStripe.Width);                                    // sol 2px şerit
        Assert.Equal(DsResources.TokenColor(host, "Brush.Amber"), DsResources.ColorOf(row.SelectionStripe.Fill)); // amber
        Assert.Equal(DsResources.TokenColor(host, "Brush.SurfaceRaised"), DsResources.ColorOf(row.Background));    // raised zemin
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Done_row_glows_once_and_never_replays_after_container_recycling()
    {
        var vm = NewVm();
        var (view, window, _) = Realize(vm, forceAnimations: true);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 0, 4, "Debug", 0));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 0, 0, 0, 0, 100)); // hatasız "Completed …" done satırı

        var row = view.Rows.Last();
        DispatcherPump.PumpUntil(() => row.GlowPlayCount >= 1, TimeSpan.FromSeconds(2));

        Assert.True(row.ViewModel!.GlowEligible);
        Assert.Equal(1, row.GlowPlayCount);
        Assert.True(row.ViewModel!.GlowPlayed);

        // Container recycle taklidi: aynı VM yeniden bağlanır → GlowPlayed guard'ı TEKRAR oynatmaz.
        row.SimulateContainerRecycle();
        Assert.Equal(1, row.GlowPlayCount);
        GC.KeepAlive(window);
    }

    // ============================================================ §9 — prompt satırı jump'ta ANINDA tazelenir

    /// <summary>
    /// [DEĞİŞEN KURAL] Aktif proje değişince prompt satırı yeni metni ANINDA gösterir — daktilo ETMEZ.
    ///
    /// <para>Eski iddia: satır atladığında harf harf yazılır (ve bir dönem fırtına kapısı bunu yanlışlıkla
    /// instant'a çeviriyordu — bu testin ilk hâli o kusuru yakalıyordu). Değişme gerekçesi: yazım prototipin
    /// §6 modeline döndü, yani daktilo EN YENİ TAMPON SATIRINA aittir; prompt satırı bir göstergedir ve
    /// hiçbir şey yazmaz. İki yerde birden hareket olması sahada "ne olduğu belli değil" diye okunuyordu.
    /// Atlamanın kendisi (aktif satırın gerçekten değişmesi) hâlâ pinlenir.</para>
    /// </summary>
    [StaFact]
    public void Active_line_refreshes_instantly_when_it_jumps_on_a_real_completion_path()
    {
        const string a = @"C:\p\a.csproj";
        const string b = @"C:\p\b.csproj";
        var vm = NewVm();
        var (view, window, _) = Realize(vm, forceAnimations: true);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", a, "A"));
        vm.OnEvent(new ProjectStartedEvent("r1", b, "B"));    // aktif satır → "B building…"
        // Gerçek yol: ProjectSucceeded ÖNCE PushStream SONRA FinishBuilding çağırır. Aktif proje (B) bittiği
        // için aktif satır "A building…"e ATLAR.
        vm.OnEvent(new ProjectSucceededEvent("r1", b, 1200));

        Assert.Equal("A building…", vm.ActiveLineText);        // aktif satır gerçekten atladı
        Assert.Equal("A building…", view.ActiveText.Text);     // ve prompt satırı onu ANINDA, tam gösterir
        GC.KeepAlive(window);
    }

    // ============================================================ §10 — "Build started" will-build sayısı (skip'li)

    [Fact]
    public void Build_started_line_uses_the_will_build_count_not_the_full_plan()
    {
        var vm = NewVm();
        var items = new List<BuildPreviewItem>();
        for (int i = 0; i < 8; i++) items.Add(new BuildPreviewItem($@"C:\p\build{i}.csproj", $"B{i}", true));
        for (int i = 0; i < 28; i++) items.Add(new BuildPreviewItem($@"C:\p\skip{i}.csproj", $"S{i}", false));

        // TotalProjects=36 (plan.Nodes.Count, skip'ler DAHİL) ama yalnız 8 proje will-build.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 36, 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([.. items]));

        var line = vm.StreamEvents.Single(s => s.Text.StartsWith("Build started"));
        // ESKİ kod: "Build started — 36 projects…" (RED). Fix sonrası: will-build sayısı 8 (GREEN).
        Assert.Equal("Build started — 8 projects, parallelism 4", line.Text);
    }

    // [KALDIRILDI — design v1.7.0 §3.1] Eski test: "Continue satırı kalan will-build sayısını yazar".
    // Continue modu kaldırıldı; sürdürme ayrı bir segment değil, YENİ bir Build koşusudur ve onun satırı
    // yukarıdaki Build_started_line_uses_the_will_build_count tarafından zaten pinlenir.

    // ============================================================ §11 — StreamText 9 şablonu (birebir fidelity)

    [Fact]
    public void StreamText_templates_match_the_prototype_verbatim()
    {
        Assert.Equal("A built (1.2s)", StreamText.Built("A", 1200));
        Assert.Equal("B built — dependency issue (3.4s)", StreamText.BuiltDependencyIssue("B", 3400));
        Assert.Equal("C failed — exit 1 (0.8s)", StreamText.Failed("C", "exit 1", 800));
        Assert.Equal($"D skipped — {SkipReasons.UpToDate}", StreamText.Skipped("D", SkipReasons.UpToDate));
        Assert.Equal("Sync — 8 to build, 28 up to date", StreamText.Sync(8, 28));
        Assert.Equal("Build started — 8 projects, parallelism 4", StreamText.BuildStarted(8, 4));
        Assert.Equal("Stopped — 5 remaining projects queued", StreamText.Stopped(5));
        Assert.Equal("Completed — 12 succeeded · 3 skipped · 45s", StreamText.Completed(0, 12, 3, 0, 45000));
        Assert.Equal("Completed — 2 failed · 10 succeeded · 1 skipped · 1m 12s · 3 dependency-affected",
            StreamText.Completed(2, 10, 1, 3, 72000));
        // [DEĞİŞEN KURAL — Task 4] Eski iddia: tur satırı "{leaderName} (+{n-1} more)" taşırdı; tek lider adı
        // grubu temsil etmiyordu. Koşunun maliyetini üye sayısı anlatır; ProjectId event'te durduğu için satır
        // hâlâ lidere tıklatır (CycleRoundStartedEvent — RunViewModel.Stream).
        Assert.Equal("cycle round 2/3 — 15 members", StreamText.CycleRound(2, 3, 15));
        // [DEĞİŞEN KURAL — Task 4] Eski iddia: açılış satırı TEK toplam proje sayısı taşırdı; toplamın çoğu
        // upstream (prerequisite) olabiliyordu ve kullanıcı ekrandan "neden bu kadar proje derleniyor"u
        // okuyamıyordu — satır artık üye/prerequisite kırılımını ayrı ayrı söyler (tavan yine
        // CycleRoundPolicy.RoundCap'ten, literal DEĞİL).
        Assert.Equal(
            $"Cycles started — 6 cycle members · 2 prerequisites · up to {BuildOrchestrator.Core.Planning.CycleRoundPolicy.RoundCap} rounds",
            StreamText.CyclesStarted(6, 2));
    }

    /// <summary>[cycles] Ve koşu GERÇEKTEN o satırı yayar: mod, ertelenen açılış satırının hangi metni
    /// seçeceğini belirler (satır <c>buildPreview</c>'a ertelenir — will-build sayısı orada hazırdır).
    /// [Task 4] Kırılım will-build ∩ üyelik'ten (<c>_cycleGroups.IsMember</c>) hesaplanır — will-build'daki
    /// upstream/prerequisite projeler üye SAYILMAZ.</summary>
    [Fact]
    public async Task A_cycles_run_opens_the_stream_with_its_own_line_not_the_build_one()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string upstreamId = @"C:\p\u.csproj";
        const string m1Id = @"C:\p\m1.csproj";
        const string m2Id = @"C:\p\m2.csproj";
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(upstreamId, "U", 0), Node(m1Id, "M1", 1, inCycle: true), Node(m2Id, "M2", 2, inCycle: true)],
            [[m1Id, m2Id]], [], []));

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, TotalProjects: 3, Parallelism: 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(upstreamId, "U", true),
            new BuildPreviewItem(m1Id, "M1", true),
            new BuildPreviewItem(m2Id, "M2", true),
        ]));

        Assert.Contains(vm.StreamEvents, l => l.Text == StreamText.CyclesStarted(2, 1)); // 2 üye (M1/M2) · 1 prerequisite (U)
        Assert.DoesNotContain(vm.StreamEvents, l => l.Text.StartsWith("Build started", StringComparison.Ordinal));
    }

    // ============================================================ [cycle rounds/Task 8] — tur göstergesi

    /// <summary>CycleRoundStartedEvent → stream tampon satırı, üye SAYISIYLA. Satır grubun LİDERİNE bağlıdır
    /// (ProjectId=lider) — diğer proje-özel satırlar (built/failed/skipped) gibi tıklanabilir.
    /// [DEĞİŞEN KURAL — Task 4] bkz. <see cref="StreamText_templates_match_the_prototype_verbatim"/>'daki not:
    /// eski satır lider adı taşırdı, artık üye sayısı taşır.</summary>
    [Fact]
    public void Cycle_round_started_pushes_the_round_indicator_line_with_the_member_count()
    {
        var vm = NewVm();
        const string leaderId = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", leaderId, "A")); // Projects listesine "A" adıyla satır ekler

        vm.OnEvent(new CycleRoundStartedEvent("r1", leaderId, Round: 2, RoundCap: 3, MemberCount: 4));

        var line = vm.StreamEvents.Last();
        Assert.Equal("cycle round 2/3 — 4 members", line.Text);
        Assert.Equal(leaderId, line.ProjectId); // ProjectId event'te durur — satır hâlâ lidere tıklatır
    }

    // ============================================================ [Task 4] — aktif satırda üye/tur ilerlemesi

    /// <summary>[Task 4] Aktif satır bir SCC üyesinin ProjectStarted'ında "member i/N · round r/cap" detayını
    /// taşır — kullanıcı hangi üyenin/turun koştuğunu ekrandan okur. Üye OLMAYAN (upstream/prerequisite) bir
    /// proje düz "{name} building…" kalır (detay YOK) — kırılım yalnız gerçek grup üyelerine (<c>_cycleGroups</c>)
    /// aittir, ve round henüz başlamadan (upstream'ler önce build eder) da detay eklenmez.</summary>
    [Fact]
    public void Active_line_shows_member_index_and_round_for_a_cycle_member_but_not_for_upstream()
    {
        var vm = NewVm();
        const string aId = @"C:\p\a.csproj";
        const string bId = @"C:\p\b.csproj";
        const string uId = @"C:\p\u.csproj";
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(uId, "U", 0), Node(aId, "A", 1, inCycle: true), Node(bId, "B", 2, inCycle: true)],
            [[aId, bId]], [], []));

        // Upstream proje round başlamadan ÖNCE build eder — düz "building…" (detaysız).
        vm.OnEvent(new ProjectStartedEvent("r1", uId, "U"));
        Assert.Equal("U building…", vm.ActiveLineText);

        vm.OnEvent(new CycleRoundStartedEvent("r1", aId, Round: 1, RoundCap: 3, MemberCount: 15));
        vm.OnEvent(new ProjectStartedEvent("r1", aId, "A")); // üye 1
        vm.OnEvent(new ProjectStartedEvent("r1", bId, "B")); // üye 2

        Assert.Equal("B building… · member 2/15 · round 1/3", vm.ActiveLineText);
    }

    /// <summary>[Task 4] Yeni bir tur başladığında üye sayacı 1'e döner — önceki turun ilerlemesi bir sonraki
    /// turu ETKİLEMEZ (her <c>CycleRoundStartedEvent</c> sayacı sıfırlar).</summary>
    [Fact]
    public void A_new_cycle_round_resets_the_member_index_to_one()
    {
        var vm = NewVm();
        const string aId = @"C:\p\a.csproj";
        const string bId = @"C:\p\b.csproj";
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(aId, "A", 0, inCycle: true), Node(bId, "B", 1, inCycle: true)],
            [[aId, bId]], [], []));

        vm.OnEvent(new CycleRoundStartedEvent("r1", aId, Round: 1, RoundCap: 3, MemberCount: 2));
        vm.OnEvent(new ProjectStartedEvent("r1", aId, "A"));
        vm.OnEvent(new ProjectStartedEvent("r1", bId, "B"));
        Assert.Equal("B building… · member 2/2 · round 1/3", vm.ActiveLineText);

        vm.OnEvent(new CycleRoundStartedEvent("r1", aId, Round: 2, RoundCap: 3, MemberCount: 2));
        vm.OnEvent(new ProjectStartedEvent("r1", aId, "A"));
        Assert.Equal("A building… · member 1/2 · round 2/3", vm.ActiveLineText); // sayaç 1'e döndü
    }

    /// <summary>[Task 4] <see cref="StreamComposer"/> çekirdeği: <c>StartBuilding</c>'in <c>detail</c> parametresi
    /// aktif metne eklenir; AYNI proje üzerinde yalnız detay değişirse generation ARTMAZ (daktilo yeniden
    /// koşmasın — id/ad DEĞİŞMEDİ). Farklı bir projeye geçişte (id/ad değişimi) generation her zamanki gibi artar.</summary>
    [Fact]
    public void StartBuilding_detail_updates_the_active_text_without_bumping_generation_on_detail_only_change()
    {
        var c = new StreamComposer();
        c.StartBuilding("A", "A", 1000, "member 1/2 · round 1/3");
        Assert.Equal("A building… · member 1/2 · round 1/3", c.ActiveText);
        long gen1 = c.ActiveGeneration;

        c.StartBuilding("A", "A", 1100, "member 1/2 · round 2/3"); // aynı proje, yalnız detay değişti
        Assert.Equal("A building… · member 1/2 · round 2/3", c.ActiveText);
        Assert.Equal(gen1, c.ActiveGeneration); // generation ARTMADI

        c.StartBuilding("B", "B", 1200); // farklı proje, detay YOK
        Assert.Equal("B building…", c.ActiveText);
        Assert.True(c.ActiveGeneration > gen1); // generation ARTTI
    }

    // ============================================================ [Review fix — Finding 1] grup-scoped üye takibi

    /// <summary>[Review fix — Finding 1] Motor (RunCoordinator) eşzamanlı SCC'ler arasında SERİLEŞTİRME yapmaz
    /// ve Cycles modunda paralellik kelepçelenmez (RunCoordinator.cs:1062-1077/:873/:984 — motor DEĞİŞMEZ) — ≥2
    /// grup ve parallelism ≥2 iken iki grubun <c>CycleRoundStartedEvent</c>'i ve <c>ProjectStartedEvent</c>'leri
    /// İÇ İÇE gelebilir. Global <c>_cycleGroups.IsMember</c> kapısı hangi GRUBUN üyesi olduğunu AYIRT ETMEZ — B
    /// grubunun üyesi A grubunun tek sayaç/tur alanlarını (o an ekranda yazan) tüketirdi (yanlış "member 3/2"
    /// gibi). Fix: turu başlatan LİDERİN kimliği ayrıca yakalanır; detay yalnız O LİDERİN grubuna
    /// (<c>MembersOf(leaderId)</c>) ait üyelere verilir — "tek aktif grup" varsayımı düşer. Eşzamanlı ikinci
    /// grubun üyeleri düz "{name} building…" görür (detaysız ama DOĞRU — yanlış sayıdan iyi bir gerileme).</summary>
    [Fact]
    public void Active_line_detail_is_scoped_to_the_round_leaders_own_group_under_concurrent_cycles()
    {
        var vm = NewVm();
        const string a1 = @"C:\p\a1.csproj";
        const string a2 = @"C:\p\a2.csproj";
        const string b1 = @"C:\p\b1.csproj";
        const string b2 = @"C:\p\b2.csproj";
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(a1, "A1", 0, inCycle: true), Node(a2, "A2", 1, inCycle: true),
             Node(b1, "B1", 2, inCycle: true), Node(b2, "B2", 3, inCycle: true)],
            [[a1, a2], [b1, b2]], [], []));

        // A grubunun turu başladı (lider a1) — motor B grubunu da PARALEL dispatch edebilir; B kendi
        // CycleRoundStartedEvent'ini HENÜZ yollamadı.
        vm.OnEvent(new CycleRoundStartedEvent("r1", a1, Round: 1, RoundCap: 3, MemberCount: 2));

        // B1, B grubunun üyesi — A'nın (lider a1) turuna AİT DEĞİL. Eski kod (global IsMember) burada "member
        // 1/2 · round 1/3" basardı (RED); doğrusu: B'nin kendi tur bilgisi yok, detay YOK.
        vm.OnEvent(new ProjectStartedEvent("r1", b1, "B1"));
        Assert.Equal("B1 building…", vm.ActiveLineText);

        // A2, lider a1'in GERÇEK grup üyesi — sayaç B1'den ETKİLENMEMİŞ olmalı: bu turun İLK üyesi (1/2).
        vm.OnEvent(new ProjectStartedEvent("r1", a2, "A2"));
        Assert.Equal("A2 building… · member 1/2 · round 1/3", vm.ActiveLineText);
    }

    // ============================================================ [Task 3/cycles] — grup kararı

    /// <summary>[Task 3] CycleCompletedEvent → stream satırı, üç metin varyantından biri + kind eşlemesi
    /// (Converged→Ok, NoProgress→Fail, CapReached→Info). ProjectId lidere bağlanır — tıklanabilir kalır.</summary>
    [Theory]
    [InlineData(CycleOutcome.Converged, StreamKind.Ok)]
    [InlineData(CycleOutcome.NoProgress, StreamKind.Fail)]
    [InlineData(CycleOutcome.CapReached, StreamKind.Info)]
    public void Cycle_completed_pushes_the_verdict_line_with_the_outcome_kind_mapped(
        CycleOutcome outcome, StreamKind expectedKind)
    {
        var vm = NewVm();
        const string leaderId = @"C:\p\a.csproj";

        vm.OnEvent(new CycleCompletedEvent("r1", leaderId, outcome, MemberCount: 2, Rounds: 2, FailedCount: 1, DurationMs: 4200));

        var line = vm.StreamEvents.Last();
        Assert.Equal(StreamText.CycleCompleted(outcome, 2, 2, 1, 4200), line.Text);
        Assert.Equal(expectedKind, line.Kind);
        Assert.Equal(leaderId, line.ProjectId);
    }

    // ============================================================ §13 — skip gerekçesi görünür + kapsam-dışı fırtınası tek satır

    /// <summary>[Task 2] <c>ProjectSkippedEvent.Reason</c> artık stream satırına AYNEN taşınır — eskiden
    /// <c>StreamText.Skipped(name)</c> reason'ı YOK SAYIP sabit "up to date" basıyordu.</summary>
    [Fact]
    public void Skipped_line_shows_the_actual_reason_from_the_event_not_a_hardcoded_one()
    {
        var vm = NewVm();
        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\a.csproj", SkipReasons.InDependencyCycle));

        var line = Assert.Single(vm.StreamEvents);
        // ESKİ kod: her zaman "a skipped — up to date" basardı (RED). Fix sonrası: gerçek reason (GREEN).
        Assert.Equal($"a skipped — {SkipReasons.InDependencyCycle}", line.Text);
    }

    /// <summary>[Task 2] Cycles koşusunda kapsam-dışı (<see cref="SkipReasons.OutOfCycleScope"/>) skip'ler
    /// proje başına satır YAZMAZ — sonraki stream olayından ÖNCE tek toplu Info satırına katlanır. "Güncel"
    /// (<see cref="SkipReasons.UpToDate"/>) skip AYRI kalır ve satır satır akmaya devam eder.</summary>
    [Fact]
    public void A_cycles_run_collapses_out_of_scope_skips_into_a_single_line_before_the_next_stream_event()
    {
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, TotalProjects: 5, Parallelism: 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\a.csproj", "A", true)]));

        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\s1.csproj", SkipReasons.OutOfCycleScope));
        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\s2.csproj", SkipReasons.OutOfCycleScope));
        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\s3.csproj", SkipReasons.OutOfCycleScope));
        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\y.csproj", SkipReasons.UpToDate));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));

        // ESKİ kod: 4 ayrı "skipped — up to date" satırı basardı (RED — kapsam-dışı satır YOK, tek toplu satır VAR).
        Assert.DoesNotContain(vm.StreamEvents, l => l.Text.Contains(SkipReasons.OutOfCycleScope));
        var outside = vm.StreamEvents.Single(l => l.Text == StreamText.OutsideCycleScope(3));
        var upToDate = vm.StreamEvents.Single(l => l.Text == StreamText.Skipped("y", SkipReasons.UpToDate));
        // Toplu satır, sıradaki stream olayından (buradaki up-to-date skip'in kendi PushStream'i) ÖNCE yayılır.
        Assert.True(vm.StreamEvents.IndexOf(outside) < vm.StreamEvents.IndexOf(upToDate));
    }

    /// <summary>[Review fix — Finding 2] <c>_outOfScopeSkips</c> yalnız <see cref="RunCompletedEvent"/>'te
    /// sıfırlanıyordu — motor bir Cycles koşusu ORTASINDA ölürse (RunCompletedEvent hiç gelmez) sayaç asılı
    /// kalır ve BİR SONRAKİ run'ın ilk <c>PushStream</c>'i (burada "Build started") önüne eski bir "N outside
    /// cycle scope — skipped" satırı sızdırır. Fix: <c>RunStartedEvent</c> de sayacı sıfırlar — her yeni
    /// run/segment temiz başlar.</summary>
    [Fact]
    public void A_new_run_does_not_leak_a_stale_out_of_scope_count_from_an_unfinished_cycles_run()
    {
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, TotalProjects: 3, Parallelism: 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\a.csproj", "A", true)]));
        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\s1.csproj", SkipReasons.OutOfCycleScope));
        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\s2.csproj", SkipReasons.OutOfCycleScope));
        // Motor burada ÖLDÜ — RunCompletedEvent hiç gelmedi (eski kodda sayaç HİÇ sıfırlanmıyor).
        int before = vm.StreamEvents.Count;

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, TotalProjects: 1, Parallelism: 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\x.csproj", "X", true)]));

        // Yeni run'ın açılış satırı, reset'ten sonra PUSH edilen İLK (ve TEK) satır olmalı — önünde/arkasında
        // sızmış bir "outside cycle scope" satırı OLMAMALI.
        var newLines = vm.StreamEvents.Skip(before).ToList();
        var line = Assert.Single(newLines);
        Assert.Equal("Build started — 1 projects, parallelism 4", line.Text);
    }

    /// <summary>[Task 2 regresyon pini] Toplayıcı yalnız <see cref="RunMode.Cycles"/>'a özgüdür — Build
    /// koşusunda "güncel" skipler eskisi gibi satır satır akmaya devam eder, aggregate EDİLMEZ.</summary>
    [Fact]
    public void A_build_run_does_not_aggregate_up_to_date_skips_each_gets_its_own_line()
    {
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, TotalProjects: 3, Parallelism: 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([]));

        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\x.csproj", SkipReasons.UpToDate));
        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\y.csproj", SkipReasons.UpToDate));
        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\z.csproj", SkipReasons.UpToDate));

        Assert.Equal(3, vm.StreamEvents.Count(
            l => l.Text.EndsWith("skipped — " + SkipReasons.UpToDate, StringComparison.Ordinal)));
    }

    // ============================================================ [Task 6] — build sonu cycles ipucu

    /// <summary>[Task 6] Bir Build koşusu, üyeleri hâlâ dirty (WillBuild==true) döngü üyeleri bırakarak biterse
    /// Completed satırından HEMEN SONRA "N cycle projects have pending changes — run Cycles" Info satırı gelir.
    /// Normal Build cycle üyelerine hiç dokunmaz (<c>SkipReasons.InDependencyCycle</c> pre-skip'i) — bu yüzden
    /// üyeler build-preview'ın dirty=true'sunu (WillBuild) hiç KAYBETMEZ.</summary>
    [Fact]
    public void A_completed_build_pushes_a_cycles_hint_after_completed_when_dirty_cycle_members_remain()
    {
        const string m1 = @"C:\p\m1.csproj";
        const string m2 = @"C:\p\m2.csproj";
        var vm = NewVm();
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(m1, "M1", 0, inCycle: true), Node(m2, "M2", 1, inCycle: true)],
            [[m1, m2]], [], []));

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, TotalProjects: 2, Parallelism: 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(m1, "M1", WillBuild: true),
            new BuildPreviewItem(m2, "M2", WillBuild: true),
        ]));
        vm.OnEvent(new ProjectSkippedEvent("r1", m1, SkipReasons.InDependencyCycle));
        vm.OnEvent(new ProjectSkippedEvent("r1", m2, SkipReasons.InDependencyCycle));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, Succeeded: 0, Failed: 0, Skipped: 2, Queued: 0, DurationMs: 500));

        var lines = vm.StreamEvents.ToList();
        int completedIndex = lines.FindIndex(l => l.Text.StartsWith("Completed", StringComparison.Ordinal));
        int hintIndex = lines.FindIndex(l => l.Text == StreamText.CyclesHint(2));
        Assert.True(completedIndex >= 0, "Completed satırı bulunamadı");
        Assert.True(hintIndex > completedIndex, "ipucu satırı Completed'tan SONRA gelmeli");
        Assert.Equal(StreamKind.Info, lines[hintIndex].Kind);
        Assert.Null(lines[hintIndex].ProjectId);
    }

    /// <summary>[Task 6] Bir Cycles koşusunun kendisi ipucu YAYMAZ — zaten o modda kullanıcı doğru düğmeyi
    /// kullanıyordur; aynı öneriyi tekrarlamak gürültüdür.</summary>
    [Fact]
    public void A_cycles_run_completion_does_not_push_the_hint_even_with_dirty_members_remaining()
    {
        const string m1 = @"C:\p\m1.csproj";
        const string m2 = @"C:\p\m2.csproj";
        var vm = NewVm();
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(m1, "M1", 0, inCycle: true), Node(m2, "M2", 1, inCycle: true)],
            [[m1, m2]], [], []));

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, TotalProjects: 2, Parallelism: 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(m1, "M1", WillBuild: true),
            new BuildPreviewItem(m2, "M2", WillBuild: true),
        ]));
        // [tavan] Tur tavanına dayanıp yakınsamadan bitebilir — üyeler yine de WillBuild=true kalabilir.
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, Succeeded: 0, Failed: 0, Skipped: 0, Queued: 0, DurationMs: 500));

        Assert.DoesNotContain(vm.StreamEvents, l => l.Text.EndsWith("run Cycles", StringComparison.Ordinal));
    }

    /// <summary>[Task 6] n==0 (döngü üyesi yok ya da hepsi zaten temiz) iken satır YOK — boş bir ipucu gürültü
    /// olurdu.</summary>
    [Fact]
    public void A_completed_build_pushes_no_hint_when_no_cycle_member_is_dirty()
    {
        const string a = @"C:\p\a.csproj";
        var vm = NewVm();
        vm.OnEvent(new WorkspaceTopologyEvent([Node(a, "A", 0)], [], [], []));

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, TotalProjects: 1, Parallelism: 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(a, "A", WillBuild: true)]));
        vm.OnEvent(new ProjectStartedEvent("r1", a, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", a, 100));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, Succeeded: 1, Failed: 0, Skipped: 0, Queued: 0, DurationMs: 500));

        Assert.DoesNotContain(vm.StreamEvents, l => l.Text.EndsWith("run Cycles", StringComparison.Ordinal));
    }

    /// <summary>[Task 6] Outcome=Stopped dalı Completed satırını hiç basmaz — ipucu da yayınlanmaz (kod yolu
    /// baştan ayrı dal).</summary>
    [Fact]
    public void A_stopped_run_pushes_no_hint_even_with_dirty_cycle_members_remaining()
    {
        const string m1 = @"C:\p\m1.csproj";
        const string m2 = @"C:\p\m2.csproj";
        var vm = NewVm();
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(m1, "M1", 0, inCycle: true), Node(m2, "M2", 1, inCycle: true)],
            [[m1, m2]], [], []));

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, TotalProjects: 2, Parallelism: 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(m1, "M1", WillBuild: true),
            new BuildPreviewItem(m2, "M2", WillBuild: true),
        ]));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, Succeeded: 0, Failed: 0, Skipped: 0, Queued: 2, DurationMs: 500));

        Assert.DoesNotContain(vm.StreamEvents, l => l.Text.EndsWith("run Cycles", StringComparison.Ordinal));
    }

    // ============================================================ §12 — tampon cap 260 doyumu

    [Fact]
    public void Buffer_count_saturates_at_the_two_hundred_sixty_cap()
    {
        var c = new StreamComposer();
        for (int i = 0; i < 300; i++) c.Push(isFail: false, nowMs: i * 1000L); // 300 > 260

        Assert.Equal(260, c.Count);                 // tampon cap'te doyar (mevcut testler ≤160'ta kalıyordu)
        Assert.Equal(260, StreamComposer.BufferCap); // cap literal (300'e çıkarsa bu düşer)
        Assert.Equal(150, StreamComposer.RenderSlice); // render dilimi literal
    }

    // ============================================================ [A13/T3b · b8] ölçü/geometri

    /// <summary>[A13/T3b · b8] design-v1 README §2.6: "Satır (min 24px, mono 12px)" (BuildApp.jsx:645
    /// <c>minHeight 24</c>) + glyph kolonu (BuildApp.jsx:653 <c>width 12</c>). İkisi de adlandırılmış sabit
    /// olarak koddaydı (<c>EventStreamRow.RowMinHeight</c>/<c>GlyphColumn</c>) ama hiçbir test GERÇEK bir
    /// satırı realize edip bu geometriyi ölçmüyordu.</summary>
    [StaFact]
    public void Stream_row_is_at_least_24px_tall_with_a_12px_glyph_column()
    {
        const string id = @"C:\p\a.csproj";
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", id, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", id, 1200)); // → glyph'li, tıklanabilir bir satır

        var (view, window, _) = Realize(vm);
        var row = view.Rows.Single(r => r.ViewModel?.ProjectId == id);

        Assert.Equal(24.0, row.MinHeight);
        Assert.Equal(12.0, row.GlyphHost.Width);
        // Realize zorunlu (kural 5): XAML/kod-tarafı literalini okumak yetmez — GERÇEK arrange sonrası
        // ActualHeight/ActualWidth bu değerleri taşıyor mu.
        Assert.True(row.ActualHeight >= 24.0, $"satır 24px altına küçüldü: {row.ActualHeight}px");
        Assert.Equal(12.0, row.GlyphHost.ActualWidth);
        GC.KeepAlive(window);
    }
}
