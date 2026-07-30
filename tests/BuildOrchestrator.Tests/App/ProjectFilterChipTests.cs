using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T2 · madde 2.3] <c>PROJECTS</c> panel başlığındaki <b>kaldırılabilir filtre chip'i</b>.
///
/// <para><b>design-v1 §2.4 (otorite):</b> <i>"Panel başlığı: caps <c>PROJECTS</c> + mono <c>build-order</c>
/// etiketi; aktif filtre varsa kaldırılabilir chip (ör. <c>Failed ✕</c>)."</i></para>
///
/// <para><b>Ölçülen kusur:</b> <c>ShellRoot.xaml:48-76</c> yalnız <c>PROJECTS</c> + <c>build-order</c> etiketi
/// + filtre <c>TextBox</c>'ı taşıyordu; <c>rg ActiveFilter</c> başlıkta HİÇBİR isabet vermiyordu. Kullanıcı bir
/// statü chip'ine bastığında listenin neden daraldığını gösteren TEK gösterge action bar'ın ta kendisiydi.</para>
///
/// <para>Chip görünümü mevcut DS kontrolünden gelir (<c>Ds.Chip</c> — <see cref="DsControlTemplateTests"/>
/// aktif görünümü zaten pinliyor); yeni bir chip stili İCAT EDİLMEZ.</para>
/// </summary>
[Collection("Console UI (serial)")]
public class ProjectFilterChipTests
{
    private static ProjectNode Node(string name, int order) =>
        new($@"C:\p\{name}.csproj", name, $@"C:\p\{name}.csproj", ["Osys"], [], order, null, null, false, null);

    private static (MainWindow window, RunViewModel vm) NewShell(TempDir temp)
    {
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);
        vm.OnEvent(new WorkspaceTopologyEvent([Node("Alpha", 0), Node("Beta", 1)], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 2, 0));
        return (window, vm);
    }

    /// <summary><c>PROJECTS</c> panel başlığı — chip ORAYA ait (action bar'ın sayaç chip'leriyle karıştırılamaz).</summary>
    private static PanelHeader ProjectsHeader(MainWindow window) =>
        DsResources.Descendants(window.Shell).OfType<PanelHeader>().Single(h => h.Text == "PROJECTS");

    /// <summary>Başlıkta GÖRÜNÜR durumdaki chip'ler.
    /// <para><c>UpdateLayout</c> ZORUNLU: chip filtre yokken <c>Collapsed</c>'dır ve WPF bir Collapsed öğenin
    /// İÇERİĞİNİ hiç genişletmez — görünür olduktan sonra bir yerleşim geçişi olmadan şablonu (etiket + ✕
    /// geometrisi) görsel ağaçta YOKTUR. Üretimde bu geçişi WPF kendi yapar.</para></summary>
    private static IReadOnlyList<ToggleButton> HeaderChips(MainWindow window)
    {
        var header = ProjectsHeader(window);
        header.UpdateLayout();
        return [.. DsResources.Descendants(header).OfType<ToggleButton>()
            .Where(c => c.Visibility == Visibility.Visible)];
    }

    private static string TextOf(DependencyObject chip) =>
        string.Concat(DsResources.Descendants(chip).OfType<TextBlock>().Select(t => t.Text));

    [StaFact]
    public void With_no_active_filter_the_header_carries_no_chip()
    {
        using var temp = new TempDir();
        var (window, _) = NewShell(temp);

        Assert.Empty(HeaderChips(window));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void An_active_status_filter_puts_a_labelled_chip_into_the_projects_header()
    {
        using var temp = new TempDir();
        var (window, vm) = NewShell(temp);

        vm.ActiveFilter = ProjectFilter.Failed;

        var chip = Assert.Single(HeaderChips(window));
        Assert.Equal("Failed", TextOf(chip));       // ProjectFilter.Label — yeni etiket UYDURULMAZ
        GC.KeepAlive(window);
    }

    /// <summary>Etiketler <see cref="ProjectFilter.Label"/>'dan gelir; "dep" için "Dependency issues".</summary>
    [StaTheory]
    [InlineData(ProjectFilter.Building, "Building")]
    [InlineData(ProjectFilter.Succeeded, "Succeeded")]
    [InlineData(ProjectFilter.Skipped, "Skipped")]
    [InlineData(ProjectFilter.Dep, "Dependency issues")]
    public void The_chip_label_comes_from_the_existing_filter_label_table(string filter, string label)
    {
        using var temp = new TempDir();
        var (window, vm) = NewShell(temp);

        vm.ActiveFilter = filter;

        Assert.Equal(label, TextOf(Assert.Single(HeaderChips(window))));
        GC.KeepAlive(window);
    }

    /// <summary>"Kaldırılabilir" iddiasının kendisi: chip'e (✕'ine) tıklamak filtreyi TEMİZLER — ve liste
    /// gerçekten geri gelir (2.5 ile birlikte anlamlı).</summary>
    [StaFact]
    public void Clicking_the_chip_removes_the_filter_and_the_chip_itself()
    {
        using var temp = new TempDir();
        var (window, vm) = NewShell(temp);
        vm.ActiveFilter = ProjectFilter.Failed;
        var chip = Assert.Single(HeaderChips(window));

        chip.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); // GERÇEK routed event

        Assert.Null(vm.ActiveFilter);
        Assert.Empty(HeaderChips(window));
        Assert.Equal(2, window.Shell.ProjectsList.RowFlow.Items.OfType<ProjectRowViewModel>().Count());
        GC.KeepAlive(window);
    }

    /// <summary>Chip AKTİF görünümdedir (amber) — design-v1'de <c>DS.Chip active</c> ile çizilir; pasif gri bir
    /// chip "şu an bir filtre uygulanıyor" mesajını vermezdi.</summary>
    [StaFact]
    public void The_chip_is_drawn_in_the_active_amber_state_of_the_design_system_chip()
    {
        using var temp = new TempDir();
        var (window, vm) = NewShell(temp);
        var host = DsResources.NewHost();

        vm.ActiveFilter = ProjectFilter.Failed;
        var chip = Assert.Single(HeaderChips(window));
        chip.UpdateLayout();

        Assert.True(chip.IsChecked); // Ds.Chip'in aktif (amber) tetikleyicisi
        Assert.Equal(DsResources.TokenColor(host, "Brush.AmberSoft"), DsResources.ColorOf(chip.Background));
        GC.KeepAlive(window);
    }

    /// <summary>Kaldırma göstergesi ÇİZİLMİŞ bir geometridir (Icons.xaml), ham bir <c>✕</c> karakteri DEĞİL —
    /// repo'nun tek ikon stratejisi budur (T64) ve ham <c>✕</c> (U+2715) zaten <see cref="AntiSlopTests"/>'in
    /// emoji taramasına takılırdı.</summary>
    [StaFact]
    public void The_remove_affordance_is_a_drawn_glyph_not_a_raw_character()
    {
        using var temp = new TempDir();
        var (window, vm) = NewShell(temp);

        vm.ActiveFilter = ProjectFilter.Failed;
        var chip = Assert.Single(HeaderChips(window));

        var glyph = Assert.Single(DsResources.Descendants(chip).OfType<System.Windows.Shapes.Path>());
        Assert.NotNull(glyph.Data);                       // geometri GERÇEKTEN çözüldü
        Assert.DoesNotContain('\u2715', TextOf(chip));    // metinde ham ✕ YOK
        GC.KeepAlive(window);
    }
}
