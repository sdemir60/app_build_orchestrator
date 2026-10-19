using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-10-19] <b>Boş panel her durumda boş olduğunu söyler.</b> Proje listesinde satır yoksa liste
/// ikonu + <c>No projects</c>, grafta düğüm yoksa ağ ikonu + <c>No graph</c> görünür — hangi işlem boşaltmış olursa
/// olsun. Ayrı kalan üç durum: ilk açılış daveti (repo yok), keşif blokları (Sync tararken) ve filtre satırı (projeler
/// var, filtre gizliyor).
/// <para><b>Ölçülen kusur</b> (gerçek kabukta 12 senaryo): liste boş-durum kararını faza bağlıyordu — "No projects found
/// under this folder." yalnız Sync'in bittiği fazda (<c>Idle</c>) çıkardı. Clean ve Optimize fazı bilerek "henüz
/// bilinmiyor"a (<c>Boot</c>) alır; liste o sürede ve bu işlerin hata yollarında HİÇBİR ŞEY göstermiyordu, graf ise
/// düğümsüz her durumda kesikli "Graph appears after Sync" kutusunu çiziyordu. Her senaryo kendi testindedir.</para>
/// <para>Kabuk ÜRETİM kablajıyla kurulur (<see cref="MainWindowHost"/>) ve realize edilir; durum VM'in gerçek girişlerinden
/// sürülür. Görünürlük ata zincirinde ölçülür (<see cref="DsResources.IsShownWithin"/>) — HWND'siz <c>IsVisible</c>
/// kullanılmaz.</para>
/// </summary>
[Collection("Console UI (serial)")]
public class EmptyPanelViewTests
{
    private const string Root = @"C:\src\OSYS";

    /// <summary>İki projesi olan, Sync'lenmiş (Idle) kabuk; gönderimler "gider" ve bakım adımı beklemeden devreder —
    /// adımın görünür süresi bu testlerin konusu değil.</summary>
    private static (MainWindow window, RunViewModel vm) ShellWithProjects(TempDir temp)
    {
        var (window, vm, _) = MainWindowHost.NewWithProjects(temp, ("Alpha", null), ("Beta", null));
        MainWindowHost.AcceptSends(vm);
        vm.OperationHold = _ => Task.CompletedTask;
        return (window, vm);
    }

    /// <summary><paramref name="within"/> altında GÖRÜNÜR olup <paramref name="iconKey"/> geometrisini çizen kontur; yoksa
    /// <c>null</c>.</summary>
    private static Path? ShownIcon(DependencyObject within, ShellRoot shell, string iconKey)
    {
        var geometry = shell.FindResource(iconKey);
        return DsResources.Descendants(within).OfType<Path>()
            .SingleOrDefault(p => ReferenceEquals(p.Data, geometry) && DsResources.IsShownWithin(p, shell));
    }

    /// <summary>İki panel de boş görünümünde: listede satır yok, liste ikonu + <c>No projects</c>; grafta düğüm yok, ağ
    /// ikonu + <c>No graph</c>; keşif blokları kapalı.</summary>
    private static void AssertBothPanelsSayEmpty(MainWindow window)
    {
        var shell = window.Shell;
        var graph = shell.GraphHost;
        Assert.Empty(shell.ProjectsList.RowFlow.Items.OfType<ProjectRowViewModel>());
        Assert.True(DsResources.IsShownWithin(shell.NoProjectsOverlay, shell), "liste boş görünümünü göstermiyor");
        Assert.NotNull(ShownIcon(shell.NoProjectsOverlay, shell, "Icon.ListLines"));
        Assert.Equal(0, graph.NodeCount);
        Assert.True(graph.IsEmptyStateVisible, "graf boş görünümünü göstermiyor");
        Assert.NotNull(ShownIcon(graph, shell, "Icon.Network"));
        var texts = DsResources.ShownTexts(shell);
        Assert.Contains(InteractionText.ListEmpty, texts);
        Assert.Contains(InteractionText.GraphEmpty, texts);
        Assert.DoesNotContain(InteractionText.DiscoveringProjects, texts);
        Assert.DoesNotContain(InteractionText.GraphDiscovering, texts);
    }

    // ---------------------------------------------------------------- bakım işleri

    /// <summary>Kullanıcının bildirdiği senaryo: sol alttaki Clean'e basılınca liste ve graf aynı karede boşalır; tıklama
    /// anında ve iş sürerken iki panel de boş görünümünü gösterir. Devredilen Sync istenince keşif bloklarına geçilir
    /// (<c>DiscoveryBlockTests</c>).</summary>
    [StaFact]
    public async Task A_deep_clean_shows_the_empty_view_in_both_panels_while_it_runs()
    {
        using var temp = new TempDir();
        var (window, vm) = ShellWithProjects(temp);

        await vm.CleanCommand.ExecuteAsync(null);
        AssertBothPanelsSayEmpty(window);

        vm.OnEvent(new CleanStartedEvent(Root));
        AssertBothPanelsSayEmpty(window);
        GC.KeepAlive(window);
    }

    /// <summary>Optimize, Clean'le aynı yüzeyi tıklamada boşaltır — iş sürerken iki panel de boş görünümünü gösterir.</summary>
    [StaFact]
    public async Task An_optimize_shows_the_empty_view_in_both_panels_while_it_runs()
    {
        using var temp = new TempDir();
        var (window, vm) = ShellWithProjects(temp);

        await vm.OptimizeCommand.ExecuteAsync(null);
        AssertBothPanelsSayEmpty(window);

        vm.OnEvent(new OptimizeStartedEvent(Root));
        AssertBothPanelsSayEmpty(window);
        GC.KeepAlive(window);
    }

    /// <summary>Hata veren bakım işinin arkasına Sync zincirlenmez: liste, kullanıcı Sync'e basana dek boş kalır ve bunu
    /// söyler.</summary>
    [StaTheory]
    [InlineData("clean")]
    [InlineData("optimize")]
    public async Task A_maintenance_job_that_fails_leaves_the_empty_view_until_the_user_syncs(string job)
    {
        using var temp = new TempDir();
        var (window, vm) = ShellWithProjects(temp);
        if (job == "clean")
        {
            await vm.CleanCommand.ExecuteAsync(null);
            vm.OnEvent(new CleanStartedEvent(Root));
            vm.OnEvent(new ErrorEvent("cleanFailed", "boom"));
        }
        else
        {
            await vm.OptimizeCommand.ExecuteAsync(null);
            vm.OnEvent(new OptimizeStartedEvent(Root));
            vm.OnEvent(new ErrorEvent("optimizeFailed", "boom"));
        }

        Assert.False(vm.SyncRequested); // ön-koşul: hata yolunda Sync zincirlenmez
        AssertBothPanelsSayEmpty(window);
        GC.KeepAlive(window);
    }

    /// <summary>Clean'in devrettiği Sync topoloji getirmeden düşer: keşif kapanır, iki panel boş görünümüne döner.</summary>
    [StaFact]
    public async Task A_clean_whose_sync_fails_shows_the_empty_view_in_both_panels()
    {
        using var temp = new TempDir();
        var (window, vm) = ShellWithProjects(temp);
        await vm.CleanCommand.ExecuteAsync(null);
        vm.OnEvent(new CleanStartedEvent(Root));
        vm.OnEvent(new CleanCompletedEvent(2, 4, 1_024, 0, 2));
        vm.OnEvent(new SyncStartedEvent(Root, "main"));

        vm.OnEvent(new ErrorEvent("planFailed", "boom"));

        Assert.False(vm.IsDiscovering); // ön-koşul: keşif kapandı
        AssertBothPanelsSayEmpty(window);
        GC.KeepAlive(window);
    }

    /// <summary>Clean sürerken motor kapanır: iş biter ama Sync gelmez — iki panel boş görünümünde kalır.</summary>
    [StaFact]
    public async Task An_engine_lost_during_a_clean_leaves_the_empty_view_in_both_panels()
    {
        using var temp = new TempDir();
        var (window, vm) = ShellWithProjects(temp);
        await vm.CleanCommand.ExecuteAsync(null);
        vm.OnEvent(new CleanStartedEvent(Root));

        vm.OnEngineExited(1);

        AssertBothPanelsSayEmpty(window);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- açılış ve Sync

    /// <summary>Açılışta repo var ama motor henüz hazır değil: ilk Sync istenmedi, keşif açılmadı — iki panel boş
    /// görünümünü gösterir.</summary>
    [StaFact]
    public void A_workspace_waiting_for_the_engine_shows_the_empty_view_in_both_panels()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.NewRealized(temp);

        vm.RootPath = Root;

        Assert.False(vm.IsDiscovering); // ön-koşul: motor hazır olmadan Sync istenmez
        AssertBothPanelsSayEmpty(window);
        GC.KeepAlive(window);
    }

    /// <summary>Sync koştu ve klasörde hiç proje bulamadı: iki panel boş görünümünü gösterir.
    /// <para><b>[DEĞİŞEN KURAL]</b> Liste burada eskiden düz metin "No projects found under this folder." gösterirdi, graf
    /// kesikli kutuda "Graph appears after Sync" — Sync zaten koşmuşken.</para></summary>
    [StaFact]
    public void A_sync_that_finds_no_projects_shows_the_empty_view_in_both_panels()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(temp); // Sync bitti, 0 proje

        Assert.Equal(AppPhase.Idle, vm.Phase); // ön-koşul
        AssertBothPanelsSayEmpty(window);
        GC.KeepAlive(window);
    }

    /// <summary>Build bittikten sonra kendiliğinden koşan (sessiz) Sync klasörde artık hiç proje bulamadı: faz koşunun
    /// sonucunda (<c>Done</c>) kalır. Liste eskiden HİÇBİR ŞEY göstermezdi — "proje yok" kararı yalnız <c>Idle</c>'a
    /// bağlıydı.</summary>
    [StaFact]
    public async Task A_silent_sync_that_finds_no_projects_after_a_build_shows_the_empty_view_in_both_panels()
    {
        using var temp = new TempDir();
        var (window, vm) = ShellWithProjects(temp);
        MainWindowHost.RunBuild(vm, "Alpha", "Beta");
        Assert.Equal(AppPhase.Done, vm.Phase); // ön-koşul: koşu bitti

        await MainWindowHost.StartSync(vm, SyncMode.Silent);
        MainWindowHost.ReplySync(vm); // klasörde artık proje yok

        AssertBothPanelsSayEmpty(window);
        GC.KeepAlive(window);
    }

    /// <summary>İlk açılışta (repo seçilmemiş) liste kurulum davetini taşır — boş görünüm değil; graf ise boş görünümünü
    /// gösterir.
    /// <para><b>[DEĞİŞEN KURAL]</b> Graf burada eskiden kesikli kutuda "Graph appears after Sync" gösterirdi.</para></summary>
    [StaFact]
    public void On_first_run_the_list_invites_and_the_graph_shows_its_empty_view()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.NewRealized(temp);
        var shell = window.Shell;

        Assert.True(DsResources.IsShownWithin(shell.ListInviteOverlay, shell));
        Assert.False(DsResources.IsShownWithin(shell.NoProjectsOverlay, shell));
        Assert.True(shell.GraphHost.IsEmptyStateVisible);
        Assert.NotNull(ShownIcon(shell.GraphHost, shell, "Icon.Network"));
        Assert.Contains(InteractionText.GraphEmpty, DsResources.ShownTexts(shell));
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- görünüm

    /// <summary>İki boş görünüm keşif bloklarının ikon kimliğiyle çizilir (ARCHITECTURE §14.4): ikon 28px, opaklık 0.7,
    /// kontur 1.4 <c>text-faint</c>, yuvarlak uç; 10 altında 12px <c>text-faint</c> metin, arayüz yazı tipiyle. Liste liste
    /// ikonunu, graf ağ ikonunu taşır. Token bağlarının tipi de doğrulanır.</summary>
    [StaFact]
    public void Both_empty_views_are_drawn_with_the_discovery_icon_look()
    {
        using var temp = new TempDir();
        var (window, _, _) = MainWindowHost.NewWithProjects(temp); // Sync bitti, 0 proje: iki panel boş
        var shell = window.Shell;
        shell.UpdateLayout();

        AssertIconView(shell, shell.NoProjectsOverlay, "Icon.ListLines", InteractionText.ListEmpty);
        AssertIconView(shell, shell.GraphHost, "Icon.Network", InteractionText.GraphEmpty);
        Assert.Empty(DsResources.DynamicResourceTypeMismatches((DependencyObject)window.Content));
        GC.KeepAlive(window);
    }

    /// <summary>Bir boş görünümün çizimi: <paramref name="within"/> altında görünür TEK ikon ve altındaki metin.</summary>
    private static void AssertIconView(ShellRoot shell, DependencyObject within, string iconKey, string caption)
    {
        var icon = Assert.Single(DsResources.Descendants(within).OfType<Viewbox>(),
            v => DsResources.IsShownWithin(v, shell));
        Assert.Equal((28.0, 28.0), (icon.Width, icon.Height));
        Assert.Equal(0.7, icon.Opacity, 3);
        var path = Assert.Single(DsResources.Descendants(icon).OfType<Path>());
        Assert.Same(shell.FindResource(iconKey), path.Data);
        Assert.Equal(1.4, path.StrokeThickness, 3);
        Assert.Equal(DsResources.TokenColor(shell, "Brush.TextFaint"), DsResources.ColorOf(path.Stroke));
        Assert.Equal(PenLineCap.Round, path.StrokeStartLineCap);

        var text = Assert.Single(DsResources.Descendants(within).OfType<TextBlock>(),
            t => DsResources.IsShownWithin(t, shell) && DsResources.DisplayedText(t) == caption);
        Assert.Equal(12.0, text.FontSize);
        Assert.Equal(DsResources.TokenColor(shell, "Brush.TextFaint"), DsResources.ColorOf(text.Foreground));
        Assert.Equal(new Thickness(0, 10, 0, 0), text.Margin);
        Assert.Equal(AppFonts.Ui, text.FontFamily);
    }
}
