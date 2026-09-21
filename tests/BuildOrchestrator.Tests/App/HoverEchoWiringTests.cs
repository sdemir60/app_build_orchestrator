using System.Windows;
using System.Windows.Input;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Liste ↔ graf karşılıklı hover'ın ÜRETİM kablajı: ana pencerede grafikteki düğüme giren fare, listede aynı
/// projenin çizilmiş satırını standart hover'a sokar; listede hover edilen proje de grafikte hover olur.
/// <c>GraphHoverEcho</c>'nun kendi davranışı <c>ProjectRowInputTests</c>'te; burada kanıtlanan, pencerenin
/// onu gerçekten bağladığıdır.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class HoverEchoWiringTests
{
    private static ProjectRow RowOf(FrameworkElement content, string name) =>
        DsResources.RealizedObjects(content).OfType<ProjectRow>()
            .Single(r => r.DataContext is BuildOrchestrator.App.ViewModels.ProjectRowViewModel vm
                && vm.Id == MainWindowHost.IdOf(name));

    [StaFact]
    public void Hovering_a_graph_node_shows_the_standard_hover_on_its_list_row()
    {
        using var dir = new TempDir();
        var (window, _, _) = MainWindowHost.NewWithProjects(dir, ("Alpha", null), ("Beta", null));
        var content = MainWindowHost.Realize(window);
        var row = RowOf(content, "Beta");
        Assert.Equal(Visibility.Visible, row.DecisionText.Visibility); // ön-koşul: hover yok

        var body = window.Shell.GraphHost.NodeVisuals[MainWindowHost.IdOf("Beta")].Body;
        body.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        content.UpdateLayout();

        Assert.Equal(Visibility.Collapsed, row.DecisionText.Visibility);
        Assert.Equal(Visibility.Visible, row.HoverIcons!.Visibility);
    }

    [StaFact]
    public void Hovering_a_list_row_hovers_its_graph_node()
    {
        using var dir = new TempDir();
        var (window, _, _) = MainWindowHost.NewWithProjects(dir, ("Alpha", null), ("Beta", null));
        var content = MainWindowHost.Realize(window);

        RowOf(content, "Alpha").RaiseEvent(
            new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });

        Assert.Equal(MainWindowHost.IdOf("Alpha"), window.Shell.GraphHost.HoveredNode);
    }
}
