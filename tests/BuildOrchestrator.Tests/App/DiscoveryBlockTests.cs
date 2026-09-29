using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.24.0 §2.3 · §2.4 · §9] <b>Keşif sürerken panel boş kalmaz</b> — kabuk yüzü. Sync proje kümesini
/// keşfederken (VM: <see cref="RunViewModel.IsDiscovering"/>) graf ve proje listesi "ne oluyor"u söyleyen sabit bir
/// blok gösterir, liste bulunan projeleri sayar; başlıkta graf sayacı, arama kutusu, <c>build-order</c> etiketi ve filtre
/// çipi keşif boyunca gizlidir. Topoloji gelince iki panel her zamanki gibi dolar.
/// <para>Kabuk ÜRETİM kablajıyla kurulur (<see cref="MainWindowHost"/>) ve realize edilir; durum VM'in gerçek
/// girişlerinden sürülür (Sync kipinin üretim girişi, Clean'in devri, motor olayları). Görünürlük ata zincirinde
/// ölçülür (<see cref="DsResources.ShownTexts"/>) — HWND'siz <c>IsVisible</c> kullanılmaz.</para>
/// </summary>
[Collection("Console UI (serial)")]
public class DiscoveryBlockTests
{
    private const string Root = @"C:\src\OSYS";

    /// <summary>Kurulu, realize edilmiş ve bir workspace'i olan (henüz Sync'lenmemiş) kabuk; gönderimler "gider".</summary>
    private static (MainWindow window, RunViewModel vm) NewShellWithWorkspace(TempDir temp)
    {
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);
        vm.RootPath = Root;
        MainWindowHost.AcceptSends(vm);
        return (window, vm);
    }

    private static bool IsShown(UIElement element, MainWindow window) =>
        DsResources.IsShownWithin(element, window.Shell);

    private static TextBlock TextIn(DependencyObject root, string text) =>
        DsResources.Descendants(root).OfType<TextBlock>().Single(t => t.Text == text);

    private static Color ColorOf(Brush? brush) => DsResources.ColorOf(brush);

    private static Color Token(FrameworkElement host, string key) => DsResources.TokenColor(host, key);

    // ---------------------------------------------------------------- liste bloğu

    /// <summary>[§9 v1.24.0 · plan A5] Liste bloğu tasarımın sayılarıyla çizilir: zemin <c>surface-base</c>, yatay
    /// padding 24; Lucide list 28px (stroke 1.4, <c>text-faint</c>, opaklık 0.7, altında +2) · 8 ·
    /// <c>Discovering projects</c> 13px/500 <c>text-secondary</c> · 8 · mono 11px tabular, satır kırmayan sayaç
    /// (toplam <c>text-dim</c>, gerisi <c>text-faint</c>). Token bağlarının tipi de doğrulanır.</summary>
    [StaFact]
    public async Task The_list_block_is_drawn_to_the_design_numbers()
    {
        using var temp = new TempDir();
        var (window, vm) = NewShellWithWorkspace(temp);
        await MainWindowHost.StartSync(vm, SyncMode.Appended);
        window.Shell.UpdateLayout();

        var block = window.Shell.PART_Discovering;
        Assert.True(IsShown(block, window), "keşif bloğu görünmüyor");
        Assert.Equal(Token(window.Shell, "Brush.SurfaceBase"), ColorOf(block.Background));
        Assert.Equal(new Thickness(24, 0, 24, 0), block.Padding);

        var icon = Assert.Single(DsResources.Descendants(block).OfType<Viewbox>());
        Assert.Equal((28.0, 28.0), (icon.Width, icon.Height));
        Assert.Equal(0.7, icon.Opacity, 3);
        Assert.Equal(new Thickness(0, 0, 0, 2), icon.Margin);
        var path = Assert.Single(DsResources.Descendants(icon).OfType<Path>());
        Assert.Same(window.Shell.FindResource("Icon.ListLines"), path.Data);
        Assert.Equal(1.4, path.StrokeThickness, 3);
        Assert.Equal(Token(window.Shell, "Brush.TextFaint"), ColorOf(path.Stroke));
        Assert.Equal(PenLineCap.Round, path.StrokeStartLineCap);

        var title = TextIn(block, InteractionText.DiscoveringProjects);
        Assert.Equal(13.0, title.FontSize);
        Assert.Equal(FontWeights.Medium, title.FontWeight);
        Assert.Equal(Token(window.Shell, "Brush.TextSecondary"), ColorOf(title.Foreground));
        Assert.Equal(new Thickness(0, 8, 0, 0), title.Margin);

        var counter = window.Shell.PART_DiscoveryCount;
        Assert.Equal(AppFonts.Mono, counter.FontFamily);
        Assert.Equal(11.0, counter.FontSize);
        Assert.Equal(TextWrapping.NoWrap, counter.TextWrapping);
        Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(counter));
        Assert.Equal(Token(window.Shell, "Brush.TextFaint"), ColorOf(counter.Foreground));
        Assert.Equal(Token(window.Shell, "Brush.TextDim"), ColorOf(window.Shell.PART_DiscoveryTotal.Foreground));
        Assert.Equal(new Thickness(0, 8, 0, 0), counter.Margin);

        Assert.Empty(DsResources.DynamicResourceTypeMismatches((DependencyObject)window.Content));
        GC.KeepAlive(window);
    }

    /// <summary>[§9 v1.24.0 · erişilebilirlik] Sayaç sakin bir canlı bölgedir (<c>aria-live="polite"</c>): sayı
    /// DEĞİŞİNCE ekran okuyucuya bir kez duyurulur; aynı değerin yeniden gelmesi duyurulmaz. Duyurulan metin satırın
    /// tamamıdır (UIA adı).
    /// <para><b>Ölçülen tuzak:</b> satır iki run'dır (toplam ayrı renkte) ve run metni sonradan yazıldığında
    /// <c>TextBlock.Text</c> BOŞ döner — UIA adı oradan türeseydi ekran okuyucu boş bir bölgeyi duyururdu. Ad bu yüzden
    /// açıkça yazılır.</para></summary>
    [StaFact]
    public async Task The_counter_is_a_polite_live_region_that_speaks_when_the_count_changes()
    {
        using var temp = new TempDir();
        var (window, vm) = NewShellWithWorkspace(temp);
        await MainWindowHost.StartSync(vm, SyncMode.Appended);
        vm.OnEvent(new SyncStartedEvent(Root, "main"));
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(window.Shell.PART_DiscoveryCount));
        int before = window.Shell.DiscoveryAnnouncements;

        vm.OnEvent(new SyncDiscoveryEvent(29, 0));
        Assert.Equal(before + 1, window.Shell.DiscoveryAnnouncements);
        Assert.Equal("29 found", AutomationProperties.GetName(window.Shell.PART_DiscoveryCount));

        vm.OnEvent(new SyncDiscoveryEvent(29, 0)); // aynı sayı — söyleyecek yeni bir şey yok
        Assert.Equal(before + 1, window.Shell.DiscoveryAnnouncements);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- graf bloğu

    /// <summary>[§2.3 · §9 v1.24.0 · plan A6] Graf bloğu: zemin <c>surface-base</c>; Lucide network 28px (stroke 1.4,
    /// <c>text-faint</c>, opaklık 0.7) · 10 · <c>Graph appears once projects are discovered</c> 12px <c>text-faint</c>.
    /// Kesikli Sync-öncesi kutu, düğüm yüzeyi ve başlığın <c>N projects · N dependencies</c> sayacı keşif boyunca
    /// gizlidir (plan K7).</summary>
    [StaFact]
    public async Task The_graph_block_is_drawn_to_the_design_numbers_and_hides_the_rest_of_the_panel()
    {
        using var temp = new TempDir();
        var (window, vm) = NewShellWithWorkspace(temp);
        var graph = window.Shell.GraphHost;
        await MainWindowHost.StartSync(vm, SyncMode.Appended);
        window.Shell.UpdateLayout();

        Assert.True(graph.IsDiscoveryStateVisible);
        Assert.False(graph.IsEmptyStateVisible);
        Assert.False(graph.IsViewportVisible);
        Assert.Equal(Visibility.Collapsed, graph.CountsText.Visibility);

        var block = graph.DiscoveryState;
        Assert.Equal(Token(graph, "Brush.SurfaceBase"), ColorOf(block.Background));
        var icon = Assert.Single(DsResources.Descendants(block).OfType<Viewbox>());
        Assert.Equal((28.0, 28.0), (icon.Width, icon.Height));
        Assert.Equal(0.7, icon.Opacity, 3);
        var path = Assert.Single(DsResources.Descendants(icon).OfType<Path>());
        Assert.Same(graph.FindResource("Icon.Network"), path.Data);
        Assert.Equal(1.4, path.StrokeThickness, 3);
        Assert.Equal(Token(graph, "Brush.TextFaint"), ColorOf(path.Stroke));

        var label = TextIn(block, InteractionText.GraphDiscovering);
        Assert.Equal(12.0, label.FontSize);
        Assert.Equal(Token(graph, "Brush.TextFaint"), ColorOf(label.Foreground));
        Assert.Equal(new Thickness(0, 10, 0, 0), label.Margin);
        GC.KeepAlive(window);
    }

    /// <summary>[§9 v1.24.0] First run (workspace yok) DEĞİŞMEDİ: listede kurulum daveti, grafta kesikli
    /// <c>Graph appears after Sync</c> kutusu — keşif blokları çıkmaz.</summary>
    [StaFact]
    public void The_first_run_screen_is_unchanged()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);
        var graph = window.Shell.GraphHost;

        Assert.True(graph.IsEmptyStateVisible);
        Assert.Equal(InteractionText.GraphEmpty, graph.EmptyStateText);
        Assert.False(graph.IsDiscoveryStateVisible);
        Assert.True(IsShown(window.Shell.ListInviteOverlay, window));
        Assert.False(IsShown(window.Shell.PART_Discovering, window));
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- başlık araçları (plan K6)

    /// <summary>[§9 v1.24.0 · plan K6] Keşif boyunca PROJECTS başlığında arama kutusu, <c>build-order</c> etiketi ve
    /// filtre çipi gizlidir; filtre Sync'te KORUNUR ve keşif bitince çipiyle geri gelir. Filtre hiçbir satırı
    /// eşleştirmese de "No projects match this filter." keşif bloğunun önüne geçemez.</summary>
    [StaFact]
    public async Task While_discovering_the_list_tools_step_aside_and_the_filter_comes_back_with_the_projects()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(temp, ("Alpha", null), ("Beta", null));
        var shell = window.Shell;
        var tools = (UIElement)shell.PART_ProjectsHeader.LeftContent!;
        vm.ToggleFilter(ProjectFilter.Failed); // hiçbir proje failed değil → "filtre eşleşmedi"
        Assert.True(IsShown(shell.PART_NoFilterMatch, window)); // ön-koşul
        Assert.True(IsShown(shell.ProjectFilterChip, window));

        MainWindowHost.AcceptSends(vm);
        await MainWindowHost.StartSync(vm, SyncMode.Manual);

        Assert.True(IsShown(shell.PART_Discovering, window));
        Assert.False(IsShown(shell.PART_NoFilterMatch, window));
        Assert.Equal(Visibility.Collapsed, tools.Visibility);           // build-order + filtre çipi
        Assert.Equal(Visibility.Collapsed, shell.ProjectFilterBox.Visibility);

        MainWindowHost.ReplySync(vm, ("Alpha", null), ("Beta", null));

        Assert.False(IsShown(shell.PART_Discovering, window));
        Assert.Equal(Visibility.Visible, tools.Visibility);
        Assert.Equal(Visibility.Visible, shell.ProjectFilterBox.Visibility);
        Assert.True(IsShown(shell.ProjectFilterChip, window));          // filtre Sync'te korundu
        Assert.Contains(ProjectFilter.Failed, vm.ActiveFilters);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- uçtan uca: Clean'in devri

    /// <summary>
    /// [kullanıcı gözlemi 2026-09-29 · design v1.24.0] Tasarımın doğduğu senaryo — Clean'e basılır, iş biter, devrettiği
    /// Sync keşfeder, topoloji gelir. Her adımda ekranda ne olduğu ölçülür:
    /// <list type="number">
    /// <item>Clean'in KENDİ penceresi (plan K2) değişmedi: keşif bloğu yok, grafta kesikli Sync-öncesi kutu.</item>
    /// <item>Devredilen Sync istendiği anda iki panel keşif bloğunu gösterir; harici tanım olduğu için sayaç kırılımlı
    /// başlar (<c>0 found · 0 repository · 0 external</c>); başlık araçları ve graf sayacı gizlidir.</item>
    /// <item>Motorun kümülatif raporları sayacı ilerletir (önce ana repo, sonra harici kök — plan K3).</item>
    /// <item>Topoloji gelince bloklar kalkar; satırlar, graf düğümleri, graf sayacı ve başlık araçları döner.</item>
    /// </list>
    /// </summary>
    [StaFact]
    public async Task The_clean_hand_over_walks_from_the_task_window_through_discovery_to_the_projects()
    {
        using var temp = new TempDir();
        var (window, vm, list) = MainWindowHost.NewWithProjects(temp, ("Alpha", null), ("Beta", null));
        var shell = window.Shell;
        var graph = shell.GraphHost;
        var tools = (UIElement)shell.PART_ProjectsHeader.LeftContent!;
        vm.ExternalProjects = [new ExternalProject(@"C:\ext\Shared")];
        MainWindowHost.AcceptSends(vm);
        vm.OperationHold = _ => Task.CompletedTask; // adımın görünür süresi bu testin konusu değil — devir anında olur

        // 1) Clean'in kendi penceresi
        await vm.CleanCommand.ExecuteAsync(null);
        vm.OnEvent(new CleanStartedEvent(Root));
        Assert.False(IsShown(shell.PART_Discovering, window));
        Assert.False(graph.IsDiscoveryStateVisible);
        Assert.True(graph.IsEmptyStateVisible);

        // 2) Devredilen Sync istendi
        vm.OnEvent(new CleanCompletedEvent(2, 4, 1_024, 0, 2));
        Assert.True(vm.SyncRequested); // ön-koşul: devir gerçekten bir Sync istedi
        var texts = DsResources.ShownTexts(shell);
        Assert.Contains(InteractionText.DiscoveringProjects, texts);
        Assert.Contains("0 found · 0 repository · 0 external", texts);
        Assert.Contains(InteractionText.GraphDiscovering, texts);
        Assert.False(graph.IsEmptyStateVisible);
        Assert.Equal(Visibility.Collapsed, graph.CountsText.Visibility);
        Assert.Equal(Visibility.Collapsed, tools.Visibility);
        Assert.Equal(Visibility.Collapsed, shell.ProjectFilterBox.Visibility);

        // 3) Motor keşfediyor — sayaç kümülatif ilerler
        vm.OnEvent(new SyncStartedEvent(Root, "main"));
        Assert.True(IsShown(shell.PART_Discovering, window));
        vm.OnEvent(new SyncDiscoveryEvent(2, 0));
        Assert.Contains("2 found · 2 repository · 0 external", DsResources.ShownTexts(shell));
        vm.OnEvent(new SyncDiscoveryEvent(2, 1));
        Assert.Contains("3 found · 2 repository · 1 external", DsResources.ShownTexts(shell));

        // 4) Topoloji: bloklar kalkar, iki panel dolar
        vm.OnEvent(new WorkspaceTopologyEvent(
            [MainWindowHost.Node("Alpha", 0), MainWindowHost.Node("Beta", 1), MainWindowHost.Node("Shared", 2)], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 3, 0));
        texts = DsResources.ShownTexts(shell);
        Assert.DoesNotContain(InteractionText.DiscoveringProjects, texts);
        Assert.DoesNotContain(InteractionText.GraphDiscovering, texts);
        Assert.Equal(3, list.RowFlow.Items.OfType<ProjectRowViewModel>().Count());
        Assert.Equal(3, graph.NodeCount);
        Assert.True(graph.IsViewportVisible);
        Assert.Equal(Visibility.Visible, graph.CountsText.Visibility);
        Assert.Equal("3 projects · 0 dependencies", graph.HeaderCountsText);
        Assert.Equal(Visibility.Visible, tools.Visibility);
        Assert.Equal(Visibility.Visible, shell.ProjectFilterBox.Visibility);
        GC.KeepAlive(window);
    }
}
