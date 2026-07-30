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
    private static ProjectNode Node(string name, int order, string? layer) =>
        new($@"C:\p\{name}.csproj", name, $@"C:\p\{name}.csproj", ["Osys"], [], order, layer is null ? null : order, layer, false, null);

    /// <summary>İki katmanlı örnek: Core(Alpha, Beta) · Ui(Gamma).</summary>
    private static WorkspaceTopologyEvent Topology() =>
        new([Node("Alpha", 0, "Core"), Node("Beta", 1, "Core"), Node("Gamma", 2, "Ui")], [], [], []);

    /// <summary>Listenin GERÇEKTEN gösterdiği satır adları (in-flow akıştan; başlıklar hariç).</summary>
    private static IReadOnlyList<string> VisibleRowNames(StickyLayerList list) =>
        [.. list.RowFlow.Items.OfType<ProjectRowViewModel>().Select(r => r.Name)];

    /// <summary>Listenin GERÇEKTEN gösterdiği katman başlıkları (ad + satır sayısı).</summary>
    private static IReadOnlyList<(string Name, int Rows)> VisibleHeaders(StickyLayerList list) =>
        [.. list.RowFlow.Items.OfType<StickyLayerList.HeaderEntry>().Select(h => (h.Name, h.RowCount))];

    /// <summary>ÜRETİM SIRASI: kabuk ÖNCE realize edilir, topoloji SONRA akar.</summary>
    private static (MainWindow window, RunViewModel vm, StickyLayerList list) NewShellWithProjects(TempDir temp)
    {
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);
        vm.OnEvent(Topology());
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 3, 0)); // Idle
        return (window, vm, window.Shell.ProjectsList);
    }

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
        // Topolojinin kendi reveal'i tamamlansın (o reveal MEŞRU — bu test onu değil, FİLTRE'yi ölçer).
        DispatcherPump.PumpUntil(() => list.RevealGeneration > 0, TimeSpan.FromSeconds(3));
        int afterTopology = list.RevealGeneration;

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
}
