using System.Windows;
using System.Windows.Documents;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
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

    // ============================================================ §9 — aktif satır jump'ta daktilo eder (gerçek yol)

    [StaFact]
    public void Active_line_types_when_it_jumps_on_a_real_completion_path()
    {
        const string a = @"C:\p\a.csproj";
        const string b = @"C:\p\b.csproj";
        var vm = NewVm();
        var (view, window, _) = Realize(vm, forceAnimations: true);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", a, "A"));
        vm.OnEvent(new ProjectStartedEvent("r1", b, "B"));    // aktif satır → "B building…"
        // Gerçek yol: ProjectSucceeded ÖNCE PushStream (fırtına penceresi açılır) SONRA FinishBuilding çağırır.
        // Aktif proje (B) bittiği için aktif satır "A building…"e ATLAR — bu SetActive Push'tan µs sonra olduğundan
        // ESKİ kod burst=true hesaplar ve satırı instant basar (bug). Fix sonrası burst kapısı yok → daktilo koşar.
        vm.OnEvent(new ProjectSucceededEvent("r1", b, 1200));

        Assert.Equal("A building…", vm.ActiveLineText);        // aktif satır gerçekten atladı
        Assert.False(view.ActiveLineInstant);                  // ESKİ kod: instant (RED) — fix sonrası: daktilo (GREEN)
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

    [Fact]
    public void Continue_line_uses_remaining_will_build_count()
    {
        const string x = @"C:\p\x.csproj";
        const string y = @"C:\p\y.csproj";
        var vm = NewVm();
        BuildPreviewItem[] plan = [new(x, "X", true), new(y, "Y", true)];

        // Segment 1: iki proje will-build; X biter (1 tamamlandı).
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent(plan));
        vm.OnEvent(new ProjectStartedEvent("r1", x, "X"));
        vm.OnEvent(new ProjectSucceededEvent("r1", x, 900));

        // Segment 2 (Continue): aynı dondurulmuş plan yeniden yayınlanır — kalan = 2 - 1 = 1.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Continue, 2, 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent(plan));

        var line = vm.StreamEvents.Last(s => s.Text.StartsWith("Continue"));
        Assert.Equal("Continue — 1 remaining, parallelism 4", line.Text);
    }

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
        Assert.Equal("Continue — 3 remaining, parallelism 4", StreamText.Continue(3, 4));
        Assert.Equal("Completed — 12 succeeded · 3 skipped · 45s", StreamText.Completed(0, 12, 3, 0, 45000));
        Assert.Equal("Completed — 2 failed · 10 succeeded · 1 skipped · 1m 12s · 3 dependency-affected",
            StreamText.Completed(2, 10, 1, 3, 72000));
        // [cycle rounds/Task 8] Tur göstergesi — tek metin kaynağı (task-8-brief.md).
        Assert.Equal("cycle round 2/3 — A (+3 more)", StreamText.CycleRound(2, 3, "A", 4));
        // [cycles] Cycles koşusunun açılış satırı: "Build started" DEĞİL, ve sayı paralellik değil TUR tavanıdır
        // (tek kaynak CycleRoundPolicy.RoundCap — bu satır tavanı literal olarak yeniden yazMAZ).
        Assert.Equal(
            $"Cycles started — 8 projects, up to {BuildOrchestrator.Core.Planning.CycleRoundPolicy.RoundCap} rounds each",
            StreamText.CyclesStarted(8));
    }

    /// <summary>[cycles] Ve koşu GERÇEKTEN o satırı yayar: mod, ertelenen açılış satırının hangi metni
    /// seçeceğini belirler (satır <c>buildPreview</c>'a ertelenir — will-build sayısı orada hazırdır).</summary>
    [Fact]
    public async Task A_cycles_run_opens_the_stream_with_its_own_line_not_the_build_one()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, TotalProjects: 2, Parallelism: 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\a.csproj", "A", true)]));

        Assert.Contains(vm.StreamEvents, l => l.Text == StreamText.CyclesStarted(1));
        Assert.DoesNotContain(vm.StreamEvents, l => l.Text.StartsWith("Build started", StringComparison.Ordinal));
    }

    // ============================================================ [cycle rounds/Task 8] — tur göstergesi

    /// <summary>CycleRoundStartedEvent → stream tampon satırı, lider adı ResolveName ile (satırın kaynağı
    /// Projects listesi) ÇÖZÜLEREK. Satır grubun LİDERİNE bağlıdır (ProjectId=lider) — diğer proje-özel
    /// satırlar (built/failed/skipped) gibi tıklanabilir.</summary>
    [Fact]
    public void Cycle_round_started_pushes_the_round_indicator_line_with_the_leader_name_resolved()
    {
        var vm = NewVm();
        const string leaderId = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", leaderId, "A")); // Projects listesine "A" adıyla satır ekler

        vm.OnEvent(new CycleRoundStartedEvent("r1", leaderId, Round: 2, RoundCap: 3, MemberCount: 4));

        var line = vm.StreamEvents.Last();
        Assert.Equal("cycle round 2/3 — A (+3 more)", line.Text);
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
