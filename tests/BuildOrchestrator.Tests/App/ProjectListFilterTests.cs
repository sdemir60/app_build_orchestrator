using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T2 · madde 2.5] <b>Proje listesi filtreye BAĞLANIR.</b>
///
/// <para><b>Ölçülen kusur (T2 sırasında bulundu — envanterin kaçırdığı 5. boşluk):</b>
/// <c>MainWindow.RefreshProjectGroups</c> listeyi <c>_vm.BuildLayerGroups()</c>'tan besliyordu ve o da
/// <c>LayerGrouping.Build(<b>Projects</b>, Topology)</c> — yani TÜM projeler.
/// <c>rg VisibleProjects src</c> → <b>sıfır tüketici</b> (yalnız tanım + bildirim). Dolayısıyla action bar'ın
/// statü chip'leri (<see cref="RunViewModel.ActiveFilter"/>) ve Ctrl+F filtre kutusu
/// (<see cref="RunViewModel.ProjectQuery"/>) listede <b>görsel olarak HİÇBİR ŞEY yapmıyordu</b>.</para>
///
/// <para><b>Tetikleyici kuralı (A12 dersi):</b> filtre üretimdeki yoldan sürülür — VM'in gerçek
/// <c>ActiveFilter</c>/<c>ProjectQuery</c> özellikleri set edilir ve pencere ÖNCE realize edilir, veri SONRA
/// akar. Liste doğrudan <c>SetGroups</c> ile beslenmez.</para>
/// </summary>
[Collection("Console UI (serial)")]
public class ProjectListFilterTests
{
    /// <summary>Listenin GERÇEKTEN gösterdiği satır adları (in-flow akıştan; başlıklar hariç).</summary>
    private static IReadOnlyList<string> VisibleRowNames(StickyLayerList list) =>
        [.. list.RowFlow.Items.OfType<ProjectRowViewModel>().Select(r => r.Name)];

    /// <summary>Listenin GERÇEKTEN gösterdiği katman başlıkları (ad + satır sayısı).</summary>
    private static IReadOnlyList<(string Name, int Rows)> VisibleHeaders(StickyLayerList list) =>
        [.. list.RowFlow.Items.OfType<StickyLayerList.HeaderEntry>().Select(h => (h.Name, h.RowCount))];

    /// <summary>[T2 fix-1 · I-F] Ortak fixture: iki katmanlı örnek — Core(Alpha, Beta) · Ui(Gamma).
    /// Kurulum <see cref="MainWindowHost.NewWithProjects"/>'ta (üretim sırası: kabuk ÖNCE realize, veri SONRA).</summary>
    private static (MainWindow window, RunViewModel vm, StickyLayerList list) NewShellWithProjects(TempDir temp) =>
        MainWindowHost.NewWithProjects(temp, ("Alpha", "Core"), ("Beta", "Core"), ("Gamma", "Ui"));

    // ---------------------------------------------------------------- 1) statü chip'i filtresi

    [StaFact]
    public void A_status_filter_narrows_the_list_to_the_matching_projects()
    {
        using var temp = new TempDir();
        var (window, vm, list) = NewShellWithProjects(temp);
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, VisibleRowNames(list)); // ön-koşul: hepsi görünür

        vm.OnEvent(new ProjectFailedEvent("r1", @"C:\p\Beta.csproj", 10, "boom"));
        vm.ActiveFilter = ProjectFilter.Failed;

        Assert.Equal(new[] { "Beta" }, VisibleRowNames(list));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Clearing_the_status_filter_brings_every_project_back()
    {
        using var temp = new TempDir();
        var (window, vm, list) = NewShellWithProjects(temp);
        vm.OnEvent(new ProjectFailedEvent("r1", @"C:\p\Beta.csproj", 10, "boom"));
        vm.ActiveFilter = ProjectFilter.Failed;
        Assert.Single(VisibleRowNames(list)); // ön-koşul

        vm.ToggleFilter(null); // Σ chip'inin yolu

        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, VisibleRowNames(list));
        GC.KeepAlive(window);
    }

    /// <summary>Bir satır koşarken statü değiştirirse aktif filtrenin altında CANLI girip çıkar — aksi halde
    /// "Failed" filtresi açıkken yeni bir hata listeye hiç düşmezdi.</summary>
    [StaFact]
    public void A_row_that_changes_state_enters_the_active_filter_live()
    {
        using var temp = new TempDir();
        var (window, vm, list) = NewShellWithProjects(temp);
        vm.ActiveFilter = ProjectFilter.Failed;
        Assert.Empty(VisibleRowNames(list)); // ön-koşul: henüz hiç failed yok

        vm.OnEvent(new ProjectFailedEvent("r1", @"C:\p\Gamma.csproj", 10, "boom"));

        Assert.Equal(new[] { "Gamma" }, VisibleRowNames(list));
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- 2) metin araması + AND kesişimi

    [StaFact]
    public void The_text_query_narrows_the_list_and_only_matches_project_names()
    {
        using var temp = new TempDir();
        var (window, vm, list) = NewShellWithProjects(temp);

        vm.ProjectQuery = "et"; // "Beta" içinde geçer (case-insensitive alt-dize)

        Assert.Equal(new[] { "Beta" }, VisibleRowNames(list));

        vm.ProjectQuery = "csproj"; // YOL/id'de geçer ama ADDA geçmez → hiçbir şey eşleşmemeli
        Assert.Empty(VisibleRowNames(list));

        vm.ProjectQuery = "Osys";   // sln ADINDA geçer ama proje adında geçmez → eşleşmemeli
        Assert.Empty(VisibleRowNames(list));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_text_query_and_the_status_filter_intersect_with_AND()
    {
        using var temp = new TempDir();
        var (window, vm, list) = NewShellWithProjects(temp);
        vm.OnEvent(new ProjectFailedEvent("r1", @"C:\p\Alpha.csproj", 10, "boom"));
        vm.OnEvent(new ProjectFailedEvent("r1", @"C:\p\Beta.csproj", 10, "boom"));

        vm.ActiveFilter = ProjectFilter.Failed;
        Assert.Equal(new[] { "Alpha", "Beta" }, VisibleRowNames(list)); // yalnız statü

        vm.ProjectQuery = "Alpha";                                      // + ad → kesişim
        Assert.Equal(new[] { "Alpha" }, VisibleRowNames(list));

        vm.ProjectQuery = "Gamma";                                      // ad tutar ama statü tutmaz → AND düşer
        Assert.Empty(VisibleRowNames(list));
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- 4) katman başlıkları filtreden sonra

    [StaFact]
    public void A_layer_emptied_by_the_filter_loses_its_header_and_the_others_recount()
    {
        using var temp = new TempDir();
        var (window, vm, list) = NewShellWithProjects(temp);
        Assert.Equal(new[] { ("Core", 2), ("Ui", 1) }, VisibleHeaders(list)); // ön-koşul

        vm.ProjectQuery = "Alpha"; // Core'dan 1 satır kalır, Ui TAMAMEN boşalır

        Assert.Equal(new[] { ("Core", 1) }, VisibleHeaders(list)); // Ui başlığı KAYBOLDU, Core sayısı düştü
        Assert.Equal(new[] { "Alpha" }, VisibleRowNames(list));
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- A12 SINIFI: reveal stagger'ı oynamamalı

    /// <summary>
    /// <b>[A12 sınıfı regresyon guard'ı]</b> <c>StickyLayerList.SetGroups</c> <c>_revealPending</c>'i KOŞULSUZ
    /// kuruyordu; liste filtreye olduğu gibi bağlansaydı <b>her tuş vuruşunda kart reveal stagger'ı baştan
    /// oynardı</b>. Prototip otoritesi de aynı yönde: <c>revealKey</c> yalnız sync/topolojide artar
    /// (<c>BuildApp.jsx:1378</c>), filtrede ARTMAZ.
    ///
    /// <para>Ayrım (<c>SetGroups(groups, reveal:)</c>) kaldırılırsa bu test KIRMIZI verir.</para>
    /// </summary>
    [StaFact]
    public void Refreshing_the_list_for_a_filter_does_not_replay_the_reveal_stagger()
    {
        using var temp = new TempDir();
        var (window, vm, list) = NewShellWithProjects(temp);
        // [T2 fix-1 · I-E] Motion ön-koşulu kardeş testle EŞİTLENDİ: reveal ancak animasyonlar açıkken
        // kuşak ilerletir, yoksa taban çizgisi 0'da kalırdı.
        list.AnimationsEnabledProvider = () => true;
        // Topolojinin kendi reveal'i tamamlansın (o reveal MEŞRU — bu test onu değil, FİLTRE'yi ölçer).
        DispatcherPump.PumpUntil(() => list.RevealGeneration > 0, TimeSpan.FromSeconds(3));
        int afterTopology = list.RevealGeneration;
        // [T2 fix-1 · I-E] TABAN ÇİZGİSİ ASSERT'İ — PumpUntil timeout'ta HATA VERMEZ. Bu satır olmadan
        // afterTopology 0 kalabilir ve aşağıdaki eşitlik `0 == 0` ile TRIVIAL yeşil olurdu; yani A12'de
        // kapatılan regresyonun tersi guard'ı sessizce görünmez hâle gelirdi.
        Assert.True(afterTopology > 0, "topoloji reveal'i hiç oynamadı — bu testin taban çizgisi YOK (vakum)");

        vm.ProjectQuery = "a";       // liste GERÇEKTEN yeniden kuruluyor…
        vm.ProjectQuery = "al";
        vm.ProjectQuery = "alp";
        DispatcherPump.PumpUntil(() => list.RevealGeneration != afterTopology, TimeSpan.FromMilliseconds(400));

        Assert.Equal(new[] { "Alpha" }, VisibleRowNames(list)); // …evet, gerçekten yeniden kuruldu (non-vacuous)
        Assert.Equal(afterTopology, list.RevealGeneration);     // …ama reveal HİÇ yeniden oynamadı
        GC.KeepAlive(window);
    }

    /// <summary>Ayrımın diğer yönü, <see cref="StickyLayerList"/> seviyesinde doğrudan: <c>reveal: false</c>
    /// verilen bir <c>SetGroups</c> kademeli belirişi TETİKLEMEZ, tek argümanlı (varsayılan) çağrı TETİKLER.
    /// Varsayılanın korunması <see cref="StickyRevealTriggerTests"/>'in bozulmadığının da güvencesidir.</summary>
    [StaFact]
    public void SetGroups_plays_the_reveal_only_when_it_is_asked_to()
    {
        var list = new StickyLayerList { AnimationsEnabledProvider = () => true };
        var host = DsResources.NewHost();
        var window = DsResources.Realize(host, list);
        IReadOnlyList<object> rows = [new ProjectRowViewModel(@"C:\p\a.csproj", "A", ProjectRowState.Pending)];

        int before = list.RevealGeneration;
        list.SetGroups([new StickyLayerList.LayerGroup("", rows)], reveal: false);
        DispatcherPump.PumpUntil(() => list.RevealGeneration != before, TimeSpan.FromMilliseconds(400));
        Assert.Equal(before, list.RevealGeneration); // sessiz tazeleme

        list.SetGroups([new StickyLayerList.LayerGroup("", rows)]); // varsayılan = topoloji yolu
        DispatcherPump.PumpUntil(() => list.RevealGeneration != before, TimeSpan.FromSeconds(3));
        Assert.NotEqual(before, list.RevealGeneration); // reveal OYNAR
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [T2 fix-1 · I-D] follow throttle korunur

    /// <summary>
    /// <b>[T2 fix-1 · I-D — regresyon]</b> Bir filtre tazelemesi follow-mode'un 550ms throttle saatini
    /// SIFIRLAMAZ.
    ///
    /// <para><b>Ölçülen kusur:</b> <c>SetGroups</c> her çağrıda <c>new FollowScrollController(...)</c>
    /// yapıyordu. Taze controller'da <c>_lastMoveAtMs == long.MinValue</c> → <c>elapsed = double.MaxValue</c> →
    /// <c>ShouldMove</c> HEP true. Eskiden bu yalnız topoloji değişiminde olurdu; 2.5'ten sonra görünür küme
    /// her değiştiğinde oluyor, yani koşarken bir statü filtresi açıkken HER proje event'i throttle'ı
    /// atlatıyordu (design-v1 §3.3 kadansı etkisizleşiyordu).</para>
    ///
    /// <para>Burada NESNE KİMLİĞİ pinlenir (yerleşimden bağımsız, deterministik); throttle'ın gerçekten
    /// korunduğu ise saf <see cref="FollowScrollControllerTests"/> tarafında ölçülür.</para>
    /// </summary>
    [StaFact]
    public void A_filter_refresh_rebinds_the_follow_controller_instead_of_recreating_it()
    {
        using var temp = new TempDir();
        var (window, vm, list) = NewShellWithProjects(temp);
        var before = list.FollowController;
        Assert.NotNull(before); // ön-koşul: topoloji akışı controller'ı kurdu

        vm.ProjectQuery = "alp";  // filtre → SetGroups (YENİ LayoutMetrics)

        Assert.Equal(new[] { "Alpha" }, VisibleRowNames(list)); // gerçekten yeniden kuruldu
        Assert.Same(before, list.FollowController);                      // ...ama controller AYNI nesne
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [T2 fix-1 · I-C] çift reset yok

    /// <summary>
    /// <b>[T2 fix-1 · I-C — regresyon]</b> Bir topoloji değişimi listeyi <b>TEK KEZ</b> kurar.
    ///
    /// <para><b>Ölçülen kusur:</b> <c>OnWorkspaceTopology</c> <c>RefreshRunSurface()</c>'i
    /// <c>TopologyChanged</c>'DEN ÖNCE çağırıyordu. Zincir: <c>RefreshRunSurface</c> →
    /// <c>OnPropertyChanged(VisibleProjects)</c> → <c>RefreshVisibleRows</c> (imza değişti, guard tutmaz) →
    /// <c>ApplyProjectGroups(reveal:false)</c> [1. tam reset, tamamen çöp]; hemen ardından
    /// <c>TopologyChanged</c> → <c>ApplyProjectGroups(reveal:true)</c> [2. tam reset]. Guard yalnız
    /// <c>reveal:false</c> dalında olduğu için "churn imza guard'ıyla kesiliyor" savunması bu yol için
    /// GEÇERSİZDİ — 191 satırlık realize maliyeti iki katına çıkıyordu.</para>
    ///
    /// <para>Sonda: <c>ItemsSource</c> ataması <see cref="StickyLayerList"/> için TAM reset'tir, yani her
    /// kurulum YENİ bir <c>Items</c> koleksiyonu üretir. <c>ItemContainerGenerator.ItemsChanged</c> olayı
    /// bu reset'leri SAYAR.</para>
    /// </summary>
    [StaFact]
    public void A_topology_change_rebuilds_the_list_exactly_once()
    {
        using var temp = new TempDir();
        var (window, vm, list) = NewShellWithProjects(temp);

        int resets = 0;
        list.RowFlow.ItemContainerGenerator.ItemsChanged += (_, _) => resets++;

        // YENİ bir topoloji (imza değişir → TopologyChanged ateşler) — üretimdeki tek giriş noktası.
        vm.OnEvent(new WorkspaceTopologyEvent(
            [MainWindowHost.Node("Alpha", 0, "Core"), MainWindowHost.Node("Delta", 1, "Ui")], [], [], []));

        Assert.Equal(new[] { "Alpha", "Delta" }, VisibleRowNames(list)); // gerçekten kuruldu (non-vacuous)
        Assert.Equal(1, resets);                                         // ...ve YALNIZ BİR KEZ
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [T2 fix-2 · m9] scroll offset ÖLÇÜMÜ

    /// <summary>
    /// <b>[T2 fix-2 · m9 — iddia düzeltmesi]</b> <c>task-T2-report.md</c>'nin (§2.5) ve
    /// <see cref="StickyLayerList.SetGroups(System.Collections.Generic.IReadOnlyList{StickyLayerList.LayerGroup}, bool)"/>'ün
    /// XML doc'unun eski iddiası — "görünür küme değiştiğinde listenin başa dönmesi doğru davranıştır" — hiç
    /// ÖLÇÜLMEMİŞTİ. Burada üretim yolundan (<see cref="MainWindowHost.NewWithProjects"/>) ÖLÇÜLÜYOR.
    ///
    /// <para><b>Sonuç (ölçüldü):</b> WPF <c>ScrollViewer</c>, içerideki <c>ItemsControl.ItemsSource</c> tam reset
    /// yese bile <c>VerticalOffset</c>'i <b>KORUR</b> (yeni extent'e clamp eder) — liste BAŞA DÖNMEZ. İddia
    /// YANLIŞTI; bu test gerçeği pinler.</para>
    ///
    /// <para><b>Ayırt edici kanıt:</b> bu testi yazdıktan sonra <c>SetGroups</c>'un sonuna bilerek
    /// <c>Scroll.ScrollToVerticalOffset(0)</c> eklenip test KIRMIZI görüldü, sonra geri alındı (bkz.
    /// task-T2-report.md §4) — yani bu test gerçekten üretim davranışını ölçüyor, sahte-yeşil değil.</para>
    /// </summary>
    [StaFact]
    public void Filtering_the_list_preserves_the_scroll_offset_instead_of_snapping_to_the_top()
    {
        using var temp = new TempDir();
        var nodes = Enumerable.Range(0, 80).Select(i => ($"P{i}", (string?)"Core")).ToArray();
        var (window, vm, list) = MainWindowHost.NewWithProjects(temp, nodes);

        // Ön-koşul: liste GERÇEKTEN kaydırılabilir (80 satır × 36px + başlık >> viewport) — vakum değil.
        DispatcherPump.PumpUntil(() => list.Scroll.ScrollableHeight > 200, TimeSpan.FromSeconds(3));
        Assert.True(list.Scroll.ScrollableHeight > 200,
            $"liste kaydırılamıyor (ScrollableHeight={list.Scroll.ScrollableHeight}) — senaryo kurulamadı");

        list.Scroll.ScrollToVerticalOffset(100);
        DispatcherPump.PumpUntil(() => list.Scroll.VerticalOffset >= 99.5, TimeSpan.FromSeconds(3));
        Assert.True(list.Scroll.VerticalOffset >= 99.5, "test scroll'u tutmadı — ön-koşul kurulamadı");

        // 70/80 proje Failed'e düşer — 10 satır filtrelenir ama kalan 70 satır YİNE viewport'tan büyük kalır,
        // yani clamp'in kendisi 100'ü kesip 0'a indirmez (post-filtre extent hâlâ bol bol scrollable).
        for (int i = 0; i < 70; i++)
            vm.OnEvent(new ProjectFailedEvent("r1", MainWindowHost.IdOf($"P{i}"), 10, "boom"));
        vm.ActiveFilter = ProjectFilter.Failed;

        DispatcherPump.PumpUntil(() => VisibleRowNames(list).Count == 70, TimeSpan.FromSeconds(3));
        Assert.Equal(70, VisibleRowNames(list).Count); // filtre gerçekten uygulandı (non-vacuous)

        Assert.True(list.Scroll.VerticalOffset >= 99.5,
            $"[ÖLÇÜLDÜ] filtre altında scroll offset'i KORUNMALIYDI, BAŞA dönmemeliydi " +
            $"(gözlemlenen VerticalOffset={list.Scroll.VerticalOffset})");
        GC.KeepAlive(window);
    }
}
