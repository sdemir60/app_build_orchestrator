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
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, projects.Length, 4, "Debug"));
        vm.OnEvent(new BuildPreviewEvent([.. projects.Select(p => new BuildPreviewItem(p.id, p.name, true))]));
    }

    /// <summary>[cycles] Motorun sıralı bir turda gönderdiği ilanlar: her üye başlar, derlemesi bitince grubunu
    /// beklemeye geçer (<c>CycleMemberHeldEvent</c>); SON üye hâlâ derleniyordur.</summary>
    private static void CompileInTurn(RunViewModel vm, (string id, string name)[] members)
    {
        for (int i = 0; i < members.Length; i++)
        {
            vm.OnEvent(new ProjectStartedEvent("r1", members[i].id, members[i].name));
            if (i < members.Length - 1) vm.OnEvent(new CycleMemberHeldEvent("r1", members[i].id));
        }
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

        // [design v1.11.0 §2.2 — DEĞİŞEN KURAL] Küme eskiden bir ✗ glyph'i ve "5 failed" + "· 1
        // dependency-affected" sayaç metinleriyle başlıyordu; [6b fold] onları burada pinliyordu. v1.11.0
        // ikisini de kaldırdı — aynı sayılar faz metninin bitiş satırında zaten var ve şerit tek satırda iki kez
        // sayı okuyordu. Kümede ARTIK yalnız chip'ler vardır.
        Assert.Empty(ribbon.FailureCluster.Children.OfType<TextBlock>());
        Assert.Empty(ribbon.FailureCluster.Children.OfType<BuildOrchestrator.App.Controls.StatusGlyph>());

        Assert.Empty(vm.ActiveFilters);
        ribbon.FailureMoreChip!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal([ProjectFilter.Failed], vm.ActiveFilters.Order()); // "+N more" → Failed filtresi
        GC.KeepAlive(window);
    }

    /// <summary>[design v1.20.0 §2.7 · Task 7 review 3] Şerit koşunun HİKÂYESİDİR: hata kümesi bu koşunun TÜM
    /// hatalarını taşır. <c>+N more</c> ise <c>failed</c> DURUM filtresini açar ve o filtre KIRMIZI görünen satırları
    /// listeler: kanıtsız hata (timeout · Stop · invoke hatası) çıktıyı bayat bırakır, satır gri görünür ve ○'nun
    /// (to build) altındadır. Kural bilerek böyledir — filtre ile ✗ rozeti aynı kovayı okur.</summary>
    [StaFact]
    public void More_opens_the_failed_state_filter_which_lists_only_rows_shown_red()
    {
        var vm = NewVm();
        var projects = Enumerable.Range(0, 5).Select(i => ($@"C:\p\fail{i}.csproj", $"Fail{i}")).ToArray();
        StartRun(vm, projects);
        for (int i = 0; i < projects.Length; i++)
        {
            var (id, name) = projects[i];
            vm.OnEvent(new ProjectStartedEvent("r1", id, name));
            vm.OnEvent(new ProjectFailedEvent("r1", id, 100, "exit 1", Evidence: i < 2)); // 2 kanıtlı, 3 kanıtsız
        }
        var (ribbon, window) = Realize(vm);

        ribbon.FailureMoreChip!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        Assert.Equal([ProjectFilter.Failed], vm.ActiveFilters.Order());
        Assert.Equal(5, vm.Counters.Failed);                                   // koşu tablosu: hepsi
        Assert.Equal(2, vm.Counters.Broken);                                   // ✗: yalnız kırmızı görünenler
        Assert.Equal(3, vm.Counters.Stale);                                    // ○: kanıtsız hatalar gri
        Assert.Equal(["Fail0", "Fail1"], vm.VisibleProjects.Select(r => r.Name).Order());
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
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 0, 0, "Debug"));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 0, 0, 0, 0, 100));

        Assert.Equal(AppPhase.Done, vm.Phase);
        Assert.True(vm.AllClean); // hiç willBuild yok → done+success glyph görünür
        Assert.Equal(10.0, ribbon.PhaseText.Margin.Left);
        GC.KeepAlive(window);
    }

    /// <summary>[P3 · Task 2] Güvenli çıkış uçuştaki bir Sync'i beklemeye başladığında şerit metni YENİLENİR: faz
    /// değişmez (hâlâ Syncing), değişen tek şey <see cref="RunViewModel.ExitPending"/>'tir — şerit onu dinlemezse
    /// bekleyiş boyunca eski "▸ Sync" satırında kalır ve kapatma tıklaması kaybolmuş görünür.</summary>
    [StaFact]
    public void The_ribbon_redraws_when_the_exit_starts_waiting()
    {
        var vm = NewVm();
        var (ribbon, window) = Realize(vm);
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main")); // çıkışın bekleyeceği iş
        Assert.Equal(RibbonText.SyncingWithFetch, ribbon.PhaseText.Text); // ön-koşul

        vm.RequestExit();

        Assert.Equal("▸ Stopping — wrapping up", ribbon.PhaseText.Text);
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
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug"));
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
    /// DEĞİL. Ara tur sonuçları yayılmadığı için bir SCC'nin üyeleri grup bitene kadar <c>Started</c>'ta kalır;
    /// şerit bunları çip yapsaydı 15 üyeli bir grupta dört çip + "+11" gösterirdi — sayaç chip'i "1 building"
    /// derken. Üç yüzey (satır glyph'i, sayaç, şerit) TEK predicate'ten (<c>ProjectRowViewModel.IsCompiling</c>)
    /// okur.
    /// <para><b>[DEĞİŞEN KURAL — dalgalı turlar]</b> Test eskiden üç başlama olayını arka arkaya verip "sıra
    /// SON başlayanda" varsayımını sınıyordu; App bekleyen üyeyi kardeşinin başlamasından tahmin ederdi. Motor
    /// artık her üyenin turdaki derlemesinin bitişini ilan eder (<c>CycleMemberHeldEvent</c>) — aynı dalgadaki
    /// üyeler birlikte derlendiği için tahmin yanlış hâle gelmişti. Test motorun gerçekte gönderdiği sırayı
    /// verir; iddia aynıdır.</para>
    /// </summary>
    [StaFact]
    public void Building_chips_show_only_the_members_compiling_now_inside_a_running_cycle_group()
    {
        var vm = NewVm();
        var nodes = new[] { ("a", "A"), ("b", "B"), ("c", "C") };
        vm.OnEvent(new WorkspaceTopologyEvent(
            [.. nodes.Select(p => new ProjectNode(p.Item1, p.Item2, p.Item1, [], [], 0, null, null, true, null))],
            [["a", "b", "c"]], [], []));
        StartRun(vm, nodes);
        CompileInTurn(vm, nodes); // A ve B derlendi, grubunu bekliyor; C derleniyor

        var (ribbon, window) = Realize(vm);

        Assert.Equal(1, vm.Counters.Building);
        Assert.Single(ribbon.BuildingChips);
        Assert.Equal("C", ChipLabel(ribbon.BuildingChips[0]));
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [cycles] Sıra bir üyeden diğerine geçtiğinde chip DE geçer. Yukarıdaki test şeridi olaylardan SONRA
    /// kurduğu için ilk çizimi ölçüyordu; sahada görülen kusur canlı şeritteydi: chip ilk üyede donuyor ve
    /// grup boyunca hep onu gösteriyordu (kullanıcı raporu: "UI.DMS sürekli dönüyor, başka proje derlense de
    /// değişmiyor").
    ///
    /// <para>Neden: <see cref="RunCounters"/> bir <c>readonly record struct</c>'tır ve grup içinde sıra el
    /// değiştirdiğinde sayaç demeti BYTE-BYTE aynı kalır (building yine 1, kuyruk yine aynı sayıda, toplam
    /// sabit) — yalnız KİMLİKLER yer değiştirir. Toolkit'in <c>[ObservableProperty]</c> setter'ı yapısal
    /// eşitlik görüp <c>PropertyChanged</c> yaymaz, şerit de chip'lerini yalnız <c>Counters</c> bildirimiyle
    /// tazelediği için hiç haber almazdı. Satır kartları doğru güncelleniyordu (onlar satırın kendi
    /// <c>PropertyChanged</c>'ine bağlı) — kullanıcının gördüğü asimetri buydu.</para>
    /// <para><b>[DEĞİŞEN KURAL — dalgalı turlar]</b> Sıranın el değiştirmesi artık motorun iki ilanıdır — biten
    /// üye için <c>CycleMemberHeldEvent</c>, başlayan için <c>ProjectStartedEvent</c> (eskiden yalnız ikincisi
    /// gelir, birincisi kardeşin başlamasından tahmin edilirdi; bkz. yukarıdaki testin notu).</para>
    /// </summary>
    [StaFact]
    public void Building_chip_follows_the_turn_as_it_moves_between_cycle_members()
    {
        var vm = NewVm();
        var nodes = new[] { ("a", "A"), ("b", "B"), ("c", "C") };
        vm.OnEvent(new WorkspaceTopologyEvent(
            [.. nodes.Select(p => new ProjectNode(p.Item1, p.Item2, p.Item1, [], [], 0, null, null, true, null))],
            [["a", "b", "c"]], [], []));
        StartRun(vm, nodes);
        CompileInTurn(vm, nodes); // C derleniyor

        var (ribbon, window) = Realize(vm); // şerit CANLI: bundan sonrasını bildirimle öğrenmek zorunda
        Assert.Equal("C", ChipLabel(ribbon.BuildingChips[0]));

        var before = vm.Counters;
        vm.OnEvent(new CycleMemberHeldEvent("r1", "c"));     // C'nin turu bitti...
        vm.OnEvent(new ProjectStartedEvent("r1", "a", "A")); // ...sıra yeni turda A'ya geçti

        Assert.Equal(before, vm.Counters);                   // sayaç demeti değişmedi — kusurun kaynağı
        Assert.Single(ribbon.BuildingChips);
        Assert.Equal("A", ChipLabel(ribbon.BuildingChips[0]));
        GC.KeepAlive(window);
    }

    // ================================================================ [perf Faz B · B5] chip'ler artımlı

    /// <summary>
    /// [perf Faz B · B5] Chip kümesini DEĞİŞTİRMEYEN bildirim chip'lere dokunmaz. Şerit kümeyi imzayla izler; filtre sorgusu
    /// <c>VisibleProjects</c> yayınlar ve şerit bunu dinler, ama chip'ler <c>Projects</c>'ten okunur — küme aynı kalır. Bu test
    /// eski kodda da yeşildir: imza, değişmeyen kümede tam yeniden kurmayı zaten engelliyordu (B5'in doğrulaması); artımlı yol
    /// bunu bozmamalı.
    /// </summary>
    [StaFact]
    public void A_notification_that_leaves_the_chip_sets_unchanged_does_not_touch_the_chips()
    {
        var vm = NewVm();
        var projects = new[] { (@"C:\p\a.csproj", "A"), (@"C:\p\b.csproj", "B"), (@"C:\p\f.csproj", "F") };
        StartRun(vm, projects);
        foreach (var (id, name) in projects) vm.OnEvent(new ProjectStartedEvent("r1", id, name));
        vm.OnEvent(new ProjectFailedEvent("r1", projects[2].Item1, 100, "exit 1"));
        var (ribbon, window) = Realize(vm);
        var building = ribbon.BuildingChips.ToList();
        var failure = ribbon.FailureChips.ToList();
        int rebuilt = ribbon.ChipsRebuiltCount;
        int notified = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(RunViewModel.VisibleProjects)) notified++; };

        vm.ProjectQuery = "a";
        vm.ProjectQuery = "ab";

        Assert.True(notified > 0, "ön-koşul: şeridin dinlediği bildirim gerçekten yayınlandı — yoksa aşağıdaki eşitlikler boşta yeşil olurdu");
        Assert.Equal(2, building.Count);                // ön-koşul: A ve B derleniyor, F hatalı
        Assert.Single(failure);
        Assert.Equal(building, ribbon.BuildingChips);   // aynı ÖRNEKLER
        Assert.Equal(failure, ribbon.FailureChips);
        Assert.Equal(rebuilt, ribbon.ChipsRebuiltCount);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz B · B5] Building kümesi değişince YALNIZ FARK uygulanır: gelen proje için bir chip eklenir, biten için çıkar;
    /// kalan chip'ler AYNI örnek olarak yerinde durur (spinner'ı ve şablonu yeniden kurmak ölçülen maliyetti — 25 sn'lik bir
    /// koşuda 301 ms). Çıkan chip ilk chip'se kalan yeni ilk olur ve gap'ini bırakır (ilk chip HARİÇ kuralı).
    /// </summary>
    [StaFact]
    public void A_changed_building_set_applies_only_the_difference()
    {
        var vm = NewVm();
        var projects = new[] { (@"C:\p\a.csproj", "A"), (@"C:\p\b.csproj", "B"), (@"C:\p\c.csproj", "C") };
        StartRun(vm, projects);
        vm.OnEvent(new ProjectStartedEvent("r1", projects[0].Item1, "A"));
        vm.OnEvent(new ProjectStartedEvent("r1", projects[1].Item1, "B"));
        var (ribbon, window) = Realize(vm);
        var (chipA, chipB) = (ribbon.BuildingChips[0], ribbon.BuildingChips[1]);
        int rebuilt = ribbon.ChipsRebuiltCount;

        vm.OnEvent(new ProjectStartedEvent("r1", projects[2].Item1, "C")); // {A,B} → {A,B,C}

        Assert.Equal(["A", "B", "C"], ribbon.BuildingChips.Select(ChipLabel));
        Assert.Same(chipA, ribbon.BuildingChips[0]); // KIRMIZI: bugün üç chip de yıkılıp baştan kurulur
        Assert.Same(chipB, ribbon.BuildingChips[1]);
        Assert.Equal(rebuilt + 1, ribbon.ChipsRebuiltCount);

        vm.OnEvent(new ProjectSucceededEvent("r1", projects[0].Item1, 100, [])); // {A,B,C} → {B,C}

        Assert.Equal(["B", "C"], ribbon.BuildingChips.Select(ChipLabel));
        Assert.Same(chipB, ribbon.BuildingChips[0]);
        Assert.Equal(0.0, chipB.Margin.Left);                 // artık İLK chip: gap yok
        Assert.Equal(4.0, ribbon.BuildingChips[1].Margin.Left);
        Assert.Equal(rebuilt + 2, ribbon.ChipsRebuiltCount);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz B · B5] Görünür chip'lerin ötesindeki üyeler yalnız taşan "+N" metnini değiştirir: dördüncüden sonra gelen her
    /// proje dört chip'i yıkıp kuruyordu, ekranda değişen tek şey bir sayıydı. Metin ilk taşmada kurulur, sonra aynı nesne
    /// güncellenir.
    /// </summary>
    [StaFact]
    public void The_overflow_text_follows_the_building_set_without_touching_the_visible_chips()
    {
        var vm = NewVm();
        var projects = Enumerable.Range(0, 6).Select(i => ($@"C:\p\proj{i}.csproj", $"Proj{i}")).ToArray();
        StartRun(vm, projects);
        for (int i = 0; i < 4; i++) vm.OnEvent(new ProjectStartedEvent("r1", projects[i].Item1, projects[i].Item2));
        var (ribbon, window) = Realize(vm);
        var chips = ribbon.BuildingChips.ToList();
        Assert.Equal(4, chips.Count);
        Assert.Null(ribbon.BuildingOverflow);            // ön-koşul: henüz taşan yok
        int rebuilt = ribbon.ChipsRebuiltCount;

        vm.OnEvent(new ProjectStartedEvent("r1", projects[4].Item1, projects[4].Item2)); // beşinci: "+1" belirir

        var overflow = ribbon.BuildingOverflow;
        Assert.Equal("+1", overflow!.Text);
        Assert.Equal(chips, ribbon.BuildingChips);       // KIRMIZI: bugün dört chip de yıkılıp baştan kurulur
        Assert.Equal(rebuilt + 1, ribbon.ChipsRebuiltCount);

        vm.OnEvent(new ProjectStartedEvent("r1", projects[5].Item1, projects[5].Item2)); // altıncı: aynı metin nesnesi, yeni sayı

        Assert.Same(overflow, ribbon.BuildingOverflow);
        Assert.Equal("+2", ribbon.BuildingOverflow!.Text);
        Assert.Equal(chips, ribbon.BuildingChips);
        Assert.Equal(rebuilt + 2, ribbon.ChipsRebuiltCount);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz B · B5] Hata kümesi için aynı kural: yeni hata yalnız bir chip ekler; dördüncü hatada "+1 more" belirir ve
    /// sonrakilerde AYNI chip'in metni güncellenir; tek şerit paneli yerinde kalır.
    /// </summary>
    [StaFact]
    public void A_changed_failure_set_applies_only_the_difference_and_the_more_chip_follows_the_count()
    {
        var vm = NewVm();
        var projects = Enumerable.Range(0, 5).Select(i => ($@"C:\p\fail{i}.csproj", $"Fail{i}")).ToArray();
        StartRun(vm, projects);
        void Fail(int i)
        {
            vm.OnEvent(new ProjectStartedEvent("r1", projects[i].Item1, projects[i].Item2));
            vm.OnEvent(new ProjectFailedEvent("r1", projects[i].Item1, 100, "exit 1"));
        }
        Fail(0);
        Fail(1);
        var (ribbon, window) = Realize(vm);
        var first = ribbon.FailureChips.ToList();
        var strip = Assert.Single(ribbon.FailureCluster.Children);
        Assert.Equal(2, first.Count);
        Assert.Null(ribbon.FailureMoreChip);

        Fail(2); // üçüncü hata: yalnız bir chip eklenir

        Assert.Equal(["Fail0", "Fail1", "Fail2"], ribbon.FailureChips.Select(ChipLabel));
        Assert.Same(first[0], ribbon.FailureChips[0]); // KIRMIZI: bugün küme yıkılıp baştan kurulur
        Assert.Same(first[1], ribbon.FailureChips[1]);
        Assert.Null(ribbon.FailureMoreChip);

        Fail(3); // dördüncü: "+1 more" belirir, üç chip yerinde

        Assert.Same(first[0], ribbon.FailureChips[0]);
        Assert.Equal("+1 more", ((TextBlock)ribbon.FailureMoreChip!.Content).Text);
        var more = ribbon.FailureMoreChip;

        Fail(4); // beşinci: aynı "more" chip'i, yalnız sayı

        Assert.Same(more, ribbon.FailureMoreChip);
        Assert.Equal("+2 more", ((TextBlock)ribbon.FailureMoreChip!.Content).Text);
        Assert.Same(first[0], ribbon.FailureChips[0]);
        Assert.Same(strip, Assert.Single(ribbon.FailureCluster.Children)); // tek şerit paneli yerinde
        Assert.Equal(Visibility.Visible, ribbon.FailureCluster.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz B · B5 · karar 1] Gizli yüzeyde şerit chip kurmaz (A5) ve dönüşte TAM kurulum yapar; artımlı yolun "son
    /// kurulan küme" durumu o kurulumla tutarlı olmalı — yoksa dönüşten sonraki ilk görünür değişimde fark yanlış hesaplanır
    /// (çift ya da eksik chip). Dönüş kurulumu modele eşit chip'ler verir; sonraki değişim YALNIZ farkı uygular ve panel
    /// modelle birebir kalır.
    /// </summary>
    [StaFact]
    public void After_a_hidden_return_the_next_visible_change_applies_only_the_difference()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(3);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, names);
        var ribbon = window.Shell.Ribbon;
        IEnumerable<string> ModelLabels() => vm.Projects.Where(p => p.IsCompiling)
            .Select(p => BuildOrchestrator.App.Graph.GraphNode.ShortLabel(p.Name, p.NamePrefix));
        window.SetSurfaceHidden(true);
        MainWindowHost.PreviewBuild(vm, names);
        MainWindowHost.StartBuild(vm, names);
        MainWindowHost.StartProject(vm, names[0]);       // gizliyken küme değişti: şerit chip kurmadı
        Assert.Empty(ribbon.BuildingChips);

        window.SetSurfaceHidden(false);                  // dönüş: tam kurulum

        Assert.Single(ModelLabels());
        Assert.Equal(ModelLabels(), ribbon.BuildingChips.Select(ChipLabel));
        var returned = ribbon.BuildingChips[0];
        var panel = Assert.IsAssignableFrom<Panel>(ribbon.FindName("PART_BuildingChips"));

        MainWindowHost.StartProject(vm, names[1]);       // görünürken değişim: yalnız fark

        Assert.Equal(2, ribbon.BuildingChips.Count);
        Assert.Equal(ModelLabels(), ribbon.BuildingChips.Select(ChipLabel));
        Assert.Same(returned, ribbon.BuildingChips[0]);  // KIRMIZI: bugün küme yıkılıp baştan kurulur
        Assert.Equal(ribbon.BuildingChips, panel.Children.OfType<ToggleButton>()); // panel = model: çift/eksik chip yok
        GC.KeepAlive(window);
    }

    // ================================================================ [design v1.11.0 §2.2 · §9-9] işlem pill'i

    /// <summary>Hiç işlem tetiklenmemişken (açılış) pill YOKTUR — adı olmayan bir işlemin etiketi de olmaz.</summary>
    [StaFact]
    public void With_no_operation_yet_the_ribbon_carries_no_operation_pill()
    {
        var vm = NewVm();
        var (ribbon, window) = Realize(vm);

        Assert.Null(vm.CurrentOperation);
        Assert.Equal(Visibility.Collapsed, ribbon.OpPill.Visibility);
        GC.KeepAlive(window);
    }

    // [kullanıcı kararı 2026-10-02] `The_pill_of_the_last_operation_does_not_come_alive_while_a_build_waits` silindi — kuyruk yok:
    // iş sürerken Build basılamaz, bekleyen istek (IsStarting) hiç oluşmaz; eski iddia (kullanıcı bildirimi 2026-09-29) ve
    // gerekçe `RunRequestDuringWorkTests` doc'unda.

    /// <summary>Pill koşarken AMBER yanar (amber-soft zemin, amber-border, amber-text) ve içinde spinner
    /// döner; koşu bitince NÖTRLEŞİR (zemin yok, border-strong, text-dim) ama <b>KALIR</b> — "ne yapmıştım?"
    /// sorusu bir sonraki işleme kadar cevaplı durur.</summary>
    [StaFact]
    public void The_pill_lights_amber_while_the_run_is_live_and_stays_neutral_after_it()
    {
        var vm = NewVm();
        var (ribbon, window) = Realize(vm);
        SetTopology(vm, (@"C:\p\a.csproj", "A"));
        StartRun(vm, (@"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));

        Assert.Equal(Visibility.Visible, ribbon.OpPill.Visibility);
        Assert.Equal(OperationLabel.Build, ribbon.OpText.Text);
        Assert.Same(ribbon.FindResource("Brush.AmberSoft"), ribbon.OpPill.Background);
        Assert.Same(ribbon.FindResource("Brush.AmberBorder"), ribbon.OpPill.BorderBrush);
        Assert.Equal(Visibility.Visible, ribbon.OpSpinner.Visibility);   // gösterge pill'in İÇİNDE
        Assert.Equal(Visibility.Collapsed, ribbon.OpGlyph.Visibility);

        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 100));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 100));

        Assert.Equal(Visibility.Visible, ribbon.OpPill.Visibility);       // KALIR
        Assert.Equal(OperationLabel.Build, ribbon.OpText.Text);
        Assert.Null(ribbon.OpPill.Background);                            // nötr = dolgusuz
        Assert.Same(ribbon.FindResource("Brush.BorderStrong"), ribbon.OpPill.BorderBrush);
        Assert.Equal(Visibility.Collapsed, ribbon.OpSpinner.Visibility);
        Assert.Equal(Visibility.Visible, ribbon.OpGlyph.Visibility);      // sonuç glyph'i pill'in içinde
        Assert.Equal(BuildOrchestrator.App.Controls.VisualStatus.Succeeded, ribbon.OpGlyph.Status);
        GC.KeepAlive(window);
    }

    /// <summary>Pill varken faz metninin KENDİ glyph'i çizilmez — aynı işaret satırda iki kez durmaz.</summary>
    [StaFact]
    public void The_phase_glyph_is_not_drawn_next_to_the_pill()
    {
        var vm = NewVm();
        var (ribbon, window) = Realize(vm);
        SetTopology(vm, (@"C:\p\a.csproj", "A"));
        StartRun(vm, (@"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 100));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 100));

        Assert.Equal(Visibility.Visible, ribbon.OpGlyph.Visibility);
        Assert.Equal(Visibility.Collapsed, ribbon.PhaseGlyph.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>Etiket işlemi AYIRT EDER: Rebuild ve Resolve kendi sözcüklerini yazar.</summary>
    [StaTheory]
    [InlineData(RunMode.Rebuild, OperationLabel.Rebuild)]
    [InlineData(RunMode.Cycles, OperationLabel.Resolve)]
    [InlineData(RunMode.Build, OperationLabel.Build)]
    public void Each_run_mode_writes_its_own_word_into_the_pill(RunMode mode, string expected)
        => Assert.Equal(expected, OperationLabel.ForRunMode(mode));

    /// <summary>[DEĞİŞEN KURAL — v1.13.2] Eski kural (v1.11.0): hedefli işlemlerde (satırdan tetiklenen
    /// build/rebuild/clean) hedefin kısa adı em-dash ile ekleniyordu — <c>OperationLabel.Compose(label, target)</c>
    /// (<c>REBUILD — Sales.Core</c>). Tasarım v1.13.2: "tek proje derlemesi şeritte tam koşudan ayırt edilmiyor,
    /// süreç de birebir aynı" — <c>Compose</c> KALKTI. Pill'in TEK üreticisi artık
    /// <see cref="OperationLabel.ForRunMode"/>'dur ve hiçbir hedef parametresi almaz; bu, tek tek çağrı yerlerinin
    /// hedef geçmediğini değil, hedef EKLEME YOLUNUN kendisinin API'den bir daha geri gelmediğini pinler.</summary>
    [Fact]
    public void OperationLabel_no_longer_exposes_a_way_to_append_a_target()
    {
        Assert.Null(typeof(OperationLabel).GetMethod("Compose"));
    }
}
