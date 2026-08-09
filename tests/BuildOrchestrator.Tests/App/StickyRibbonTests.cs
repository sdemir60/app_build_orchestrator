using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
/// [T39] design-v1 sticky şerit görünümü (<see cref="StickyRibbon"/>, BuildApp.jsx:778-812). Şerit GERÇEKTEN
/// kurulur (ekran dışı pencere + merge zinciri) — 32px içerik / 2px progress geometrisi, building chip taşması,
/// hata kümesi (3 chip + "+N more" → Failed filtresi) ve Syncing'de belirsiz mod pinlenir.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class StickyRibbonTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    private static (StickyRibbon ribbon, Window window) Realize(RunViewModel vm, bool forceAnimations = false)
    {
        var host = DsResources.NewHost();
        var ribbon = new StickyRibbon { DataContext = vm };
        if (forceAnimations) ribbon.AnimationsEnabledProvider = () => true;
        var window = DsResources.Realize(host, ribbon);
        return (ribbon, window);
    }

    private static void StartRun(RunViewModel vm, params (string id, string name)[] projects)
    {
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, projects.Length, 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([.. projects.Select(p => new BuildPreviewItem(p.id, p.name, true))]));
    }

    /// <summary>[D5] Topolojiyi kurar → kısa-ad öneki (NamePrefix) satırlara itilir; chip'ler onu okur.</summary>
    private static void SetTopology(RunViewModel vm, params (string id, string name)[] projects) =>
        vm.OnEvent(new WorkspaceTopologyEvent(
            [.. projects.Select(p => new ProjectNode(p.id, p.name, p.id, [], [], 0, null, null, false, null))],
            [], [], []));

    /// <summary>[D2 review fix, Finding 4] Bir chip'in görünür etiketi — Content her zaman [ikon, TextBlock] StackPanel'i.</summary>
    private static string ChipLabel(ToggleButton chip) =>
        ((TextBlock)((StackPanel)chip.Content).Children[1]).Text;

    [StaFact]
    public void Ribbon_is_thirtytwo_pixels_over_a_two_pixel_progress_bar_with_zero_radius()
    {
        var vm = NewVm();
        var (ribbon, window) = Realize(vm);

        Assert.Equal(32.0, ribbon.ContentRow.Height);
        Assert.Equal(2.0, ribbon.ProgressTrack.Height);
        Assert.Equal(new CornerRadius(0), ribbon.ProgressTrack.CornerRadius);
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T4 · n6] design-v1 README:48 "DAİMA tabular rakam" — faz metni (<c>PART_PhaseText</c>,
    /// <c>StickyRibbon.xaml:38</c>) mono taşıyan altı üretim yerinden biridir (envanter + kapsam kararı:
    /// <see cref="ProjectRowTests.The_project_row_sha_and_duration_columns_are_tabular"/>'ın XML doc'unda).</summary>
    [StaFact]
    public void The_phase_text_is_tabular()
    {
        var vm = NewVm();
        var (ribbon, window) = Realize(vm);

        Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(ribbon.PhaseText));
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T3c · c9] README §2.2: "Kalıcı durum satırı; surface-base, altta border-subtle." Yükseklik
    /// (32/2px) zaten pinliydi (yukarıdaki test); şeridin KENDİ zemini/alt çizgisi testsizdi — root Border
    /// başka bir fırçaya (ör. Brush.Surface) bağlansa süit yeşil kalırdı.</summary>
    [StaFact]
    public void The_ribbon_root_is_surface_base_with_a_border_subtle_bottom_line()
    {
        var vm = NewVm();
        var (ribbon, window) = Realize(vm);

        var root = Assert.IsType<Border>(ribbon.Content);
        Assert.Same(ribbon.FindResource("Brush.SurfaceBase"), root.Background);
        Assert.Same(ribbon.FindResource("Brush.BorderSubtle"), root.BorderBrush);
        Assert.Equal(new Thickness(0, 0, 0, 1), root.BorderThickness);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void At_most_four_building_chips_are_shown_and_the_overflow_is_plain_text()
    {
        var vm = NewVm();
        var projects = Enumerable.Range(0, 6).Select(i => ($@"C:\p\proj{i}.csproj", $"Proj{i}")).ToArray();
        StartRun(vm, projects);
        foreach (var (id, name) in projects) vm.OnEvent(new ProjectStartedEvent("r1", id, name));

        var (ribbon, window) = Realize(vm);

        Assert.Equal(4, ribbon.BuildingChips.Count);        // ilk 4 chip
        Assert.NotNull(ribbon.BuildingOverflow);            // taşan +2 DÜZ metin (ToggleButton DEĞİL — statik tip TextBlock?, ayrıca tıklanamaz)
        Assert.Equal("+2", ribbon.BuildingOverflow!.Text);

        // [D2 review fix, Finding 3] chip'ler arası 4px gap (BuildApp.jsx:783 flex gap:4) — ilk chip HARİÇ.
        Assert.Equal(0.0, ribbon.BuildingChips[0].Margin.Left);
        Assert.Equal(4.0, ribbon.BuildingChips[1].Margin.Left);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Failure_cluster_shows_three_chips_and_a_more_chip_that_applies_the_failed_filter()
    {
        var vm = NewVm();
        var projects = Enumerable.Range(0, 5).Select(i => ($@"C:\p\fail{i}.csproj", $"Fail{i}")).ToArray();
        const string depProjectId = @"C:\p\dep.csproj"; // [6b fold] "N dependency-affected" segmentini de tetikler (succeeded + dep-issue)
        StartRun(vm, [.. projects, (depProjectId, "Dep")]);
        foreach (var (id, name) in projects)
        {
            vm.OnEvent(new ProjectStartedEvent("r1", id, name));
            vm.OnEvent(new ProjectFailedEvent("r1", id, 100, "exit 1"));
        }
        vm.OnEvent(new ProjectStartedEvent("r1", depProjectId, "Dep"));
        vm.OnEvent(new ProjectSucceededEvent("r1", depProjectId, 100, ["dependent X henüz derlenmedi"]));

        var (ribbon, window) = Realize(vm);

        Assert.Equal(3, ribbon.FailureChips.Count);   // ilk 3 hatalı chip
        Assert.NotNull(ribbon.FailureMoreChip);        // "+2 more"

        // [D2 review fix, Finding 3] chip'ler arası 4px gap (BuildApp.jsx:801 flex gap:4) — ilk chip HARİÇ; "more" chip de dahil.
        Assert.Equal(0.0, ribbon.FailureChips[0].Margin.Left);
        Assert.Equal(4.0, ribbon.FailureChips[1].Margin.Left);
        Assert.Equal(4.0, ribbon.FailureMoreChip!.Margin.Left);

        // [6b fold] Failure-cluster metnini pinle: "N failed" + "· N dependency-affected" (view kodunda kuruluyor,
        // RibbonText.Compose'ta DEĞİL — bunlar chip-sayımının kapsamadığı segmentler).
        var texts = ribbon.FailureCluster.Children.OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("5 failed", texts);
        Assert.Contains("· 1 dependency-affected", texts);

        Assert.Null(vm.ActiveFilter);
        ribbon.FailureMoreChip!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(ProjectFilter.Failed, vm.ActiveFilter); // "+N more" → Failed filtresi
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_building_chip_click_selects_that_project()
    {
        var vm = NewVm();
        var projects = new[] { ($@"C:\p\a.csproj", "A"), ($@"C:\p\b.csproj", "B") };
        StartRun(vm, projects);
        foreach (var (id, name) in projects) vm.OnEvent(new ProjectStartedEvent("r1", id, name));

        var (ribbon, window) = Realize(vm);

        ribbon.BuildingChips[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(@"C:\p\a.csproj", vm.SelectedProjectId);
        Assert.False(ribbon.BuildingChips[0].IsChecked); // momentary — aktif amber yapışmaz
        GC.KeepAlive(window);
    }

    [StaFact] // [D2 review fix, Finding 4 · D5] design-v1 label={BO.shortName(n)} — veri-türevli ortak öneki atar.
    public void Building_and_failure_chip_labels_use_the_short_project_name()
    {
        // [D5] Önek artık hardcode değil: topoloji (OSYS.Foo) → NamePrefix "OSYS." satıra itilir → chip kırpar.
        var vmBuilding = NewVm();
        SetTopology(vmBuilding, (@"C:\p\OSYS.Foo.csproj", "OSYS.Foo"));
        StartRun(vmBuilding, (@"C:\p\OSYS.Foo.csproj", "OSYS.Foo"));
        vmBuilding.OnEvent(new ProjectStartedEvent("r1", @"C:\p\OSYS.Foo.csproj", "OSYS.Foo"));
        var (buildingRibbon, buildingWindow) = Realize(vmBuilding);
        Assert.Equal("Foo", ChipLabel(buildingRibbon.BuildingChips[0]));
        GC.KeepAlive(buildingWindow);

        var vmFailed = NewVm();
        SetTopology(vmFailed, (@"C:\p\OSYS.Bar.csproj", "OSYS.Bar"));
        StartRun(vmFailed, (@"C:\p\OSYS.Bar.csproj", "OSYS.Bar"));
        vmFailed.OnEvent(new ProjectStartedEvent("r1", @"C:\p\OSYS.Bar.csproj", "OSYS.Bar"));
        vmFailed.OnEvent(new ProjectFailedEvent("r1", @"C:\p\OSYS.Bar.csproj", 100, "exit 1"));
        var (failedRibbon, failedWindow) = Realize(vmFailed);
        Assert.Equal("Bar", ChipLabel(failedRibbon.FailureChips[0]));
        GC.KeepAlive(failedWindow);
    }

    [StaFact] // [D2 review fix, Finding 5] glyph collapsed → leading gap yok; glyph görünür → glyph→metin gap:10.
    public void Phase_text_margin_follows_glyph_visibility()
    {
        var vm = NewVm();
        var (ribbon, window) = Realize(vm);

        Assert.Equal(0.0, ribbon.PhaseText.Margin.Left); // Boot: glyph yok

        vm.OnEvent(new WorkspaceTopologyEvent([], [], [], []));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 0, 0, "Debug", 0));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 0, 0, 0, 0, 100));

        Assert.Equal(AppPhase.Done, vm.Phase);
        Assert.True(vm.AllClean); // hiç willBuild yok → done+success glyph görünür
        Assert.Equal(10.0, ribbon.PhaseText.Margin.Left);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Sync_phase_puts_the_progress_bar_into_indeterminate_mode()
    {
        var vm = NewVm();
        var (ribbon, window) = Realize(vm, forceAnimations: true);

        // Başlangıç (Boot): belirsiz DEĞİL.
        Assert.False(ribbon.IsIndeterminate);

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main")); // → Syncing
        Assert.True(ribbon.IsIndeterminate);

        // Gerçek bir sweep saati (compositor tick) — HWND'li ekran dışı pencerede indikatör TranslateX animate olur.
        DispatcherPump.PumpUntil(
            () => DependencyPropertyHelper.GetValueSource(ribbon.IndicatorTranslate, TranslateTransform.XProperty).IsAnimated,
            TimeSpan.FromSeconds(2));
        Assert.True(DependencyPropertyHelper.GetValueSource(ribbon.IndicatorTranslate, TranslateTransform.XProperty).IsAnimated);

        // Sync bitince (Idle) belirsiz mod bırakılır ve sweep durur.
        vm.OnEvent(new WorkspaceTopologyEvent([], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 0, 0)); // → Idle
        Assert.False(ribbon.IsIndeterminate);
        Assert.False(DependencyPropertyHelper.GetValueSource(ribbon.IndicatorTranslate, TranslateTransform.XProperty).IsAnimated);
        GC.KeepAlive(window);
    }

    /// <summary>[planlama görünürlüğü] <see cref="AppPhase.Starting"/> de <see cref="AppPhase.Syncing"/> gibi
    /// belirsiz moddadır: motor çalışıyor ama daha PLAN yok, dolayısıyla ölçülebilir bir yüzde de yok.
    /// Determinate bırakılsaydı çubuk <c>willBuild==0</c> yüzünden %0'da DONAR ve şeridin "▸ Starting" metniyle
    /// çelişirdi — hareketsiz bir çubuk, takılmış bir uygulamanın en güçlü işaretidir.</summary>
    [StaFact]
    public void Starting_phase_puts_the_progress_bar_into_indeterminate_mode()
    {
        var vm = NewVm();
        var (ribbon, window) = Realize(vm, forceAnimations: true);
        Assert.False(ribbon.IsIndeterminate); // Boot: belirsiz DEĞİL

        vm.Phase = AppPhase.Starting;
        Assert.True(ribbon.IsIndeterminate);

        // runStarted geldi: artık plan VAR (willBuild biliniyor) → determinate ilerlemeye geçilir.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.False(ribbon.IsIndeterminate);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [A13/T6 · t5 — <b>ÜRETİM AÇIĞI PİNİ</b>] Şeridin <c>· N warnings</c> segmenti <see cref="RibbonText.Compose"/>'ta
    /// VAR ve <c>RibbonTextTests</c> onu birebir pinliyor; eksik olan <b>BESLEME</b>dir.
    ///
    /// <para><b>Ölçülen gerçek:</b> tek üretim çağrısı sayıyı SABİT sıfır geçiyor
    /// (<c>StickyRibbon.xaml.cs:211</c> <c>warnings: 0</c>, yanında kendi "wire gap" notuyla) ve App'te
    /// sayılacak bir kaynak da yok: derleyici warning sayısı IPC sözleşmesinde HİÇ taşınmıyor
    /// (<c>RunCompletedEvent</c> = Succeeded/Failed/Skipped/Queued/DurationMs/DepIssueCount) — ikinci bir
    /// üretim notu bunu ayrıca yazıyor (<c>StreamText.cs:50</c>). Otorite ise sayıyı istiyor
    /// (<c>BuildApp.jsx:768-769</c> + <c>build-data.js:530-537</c>: koşuda derlenen projelerin <c>warn</c> tipli,
    /// dep-OLMAYAN log satırlarının sayısı).</para>
    ///
    /// <para><b>Bu yüzden burada pozitif iddia (sayı görünüyor/artıyor) KURULAMAZ</b> — kurmak, testin değil bir
    /// ÖZELLİĞİN işi olurdu (log-parse + IPC alanı + VM özelliği + kablo). Kural gereği üretim sapması
    /// DÜZELTİLMEDİ, RAPORLANDI (T6 raporu · Concerns). Pinlenen şey bugünkü DÜRÜST davranıştır: uygulamaya
    /// gerçekten bir derleyici warning'i ulaşsa bile şerit uydurma bir sayı GÖSTERMEZ. Kablo bağlandığı gün bu
    /// test KIRILIR ve pozitif iddiaya (<c>· 1 warnings</c>) çevrilmelidir — açık sessizce kapanamaz.</para>
    /// </summary>
    [StaFact]
    public void A_compiler_warning_that_reaches_the_app_does_not_reach_the_ribbon_because_no_wire_feeds_it()
    {
        const string projectId = @"C:\p\A.csproj";
        const string warningLine = "Class1.cs(7,17): warning CS0219: The variable 'x' is assigned but never used";

        // Üretim sırası: kabuk ÖNCE realize, veri SONRA (A12 dersi).
        var vm = NewVm();
        var (ribbon, window) = Realize(vm);

        SetTopology(vm, (projectId, "A"));
        StartRun(vm, (projectId, "A"));
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));
        vm.OnEvent(new ProjectLogEvent("r1", projectId, 1, warningLine));
        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 120));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, Succeeded: 1, Failed: 0, Skipped: 0, Queued: 0, DurationMs: 1200));

        // Ön-koşullar (vakum yasak): (a) koşu GERÇEKTEN bitti ve şerit done satırını kurdu; (b) warning satırı
        // GERÇEKTEN uygulamaya ulaştı — koşu dokümanında (run transkripti) duruyor.
        Assert.Equal(AppPhase.Done, vm.Phase);
        Assert.False(vm.AllClean); // willBuild dolu → "Completed — …" dalı (all-clean "Everything up to date" DEĞİL)
        Assert.Contains(warningLine, vm.GetRunDocumentText(), StringComparison.Ordinal);

        // AÇIK: metin "· N warnings" segmentini TAŞIMAZ. (Segment, Compose biçiminde tam olarak "skipped" ile
        // geçen süre ARASINA girer — RibbonText.cs:119-125.)
        Assert.StartsWith("Completed — 1 succeeded · 0 skipped · ", ribbon.PhaseText.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("warnings", ribbon.PhaseText.Text, StringComparison.Ordinal);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [cycles] Şeridin building chip'leri de "ŞU AN derleniyor" sorusunu sorar — "Started durumundadır"ı
    /// DEĞİL. Bir SCC'nin üyeleri tek tek invoke edilir ve ara tur sonuçları yayılmadığı için grup bitene
    /// kadar hepsi <c>Started</c>'ta kalır; şerit bunları çip yapsaydı 15 üyeli bir grupta dört çip + "+11"
    /// gösterirdi — sayaç chip'i "1 building" derken. Üç yüzey (satır glyph'i, sayaç, şerit) TEK predicate'ten
    /// (<c>ProjectRowViewModel.IsCompiling</c>) okur.
    /// </summary>
    [StaFact]
    public void Building_chips_show_only_the_member_whose_turn_it_is_inside_a_running_cycle_group()
    {
        var vm = NewVm();
        var nodes = new[] { ("a", "A"), ("b", "B"), ("c", "C") };
        vm.OnEvent(new WorkspaceTopologyEvent(
            [.. nodes.Select(p => new ProjectNode(p.Item1, p.Item2, p.Item1, [], [], 0, null, null, true, null))],
            [["a", "b", "c"]], [], []));
        StartRun(vm, nodes);
        foreach (var (id, name) in nodes) vm.OnEvent(new ProjectStartedEvent("r1", id, name)); // sıra C'de

        var (ribbon, window) = Realize(vm);

        Assert.Equal(1, vm.Counters.Building);
        Assert.Single(ribbon.BuildingChips);
        Assert.Equal("C", ChipLabel(ribbon.BuildingChips[0]));
        GC.KeepAlive(window);
    }
}
