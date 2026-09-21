using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Graph;

/// <summary>
/// Liste ile graf arasındaki karşılıklı hover. Tek ortak değer <see cref="RunViewModel.HoveredProjectId"/>'dir:
/// imlecin gerçek hover'ı (satırda ya da düğümde) onu yazar, karşı yüzey onu STANDART hover'ıyla gösterir.
/// </summary>
public static class GraphHoverEcho
{
    /// <summary>İki yön: düğümdeki gerçek hover değeri yazar; değer düğümde yansır. Yansıma geri bildirilmez
    /// (<see cref="GraphView.EchoHover"/>), yani döngü yoktur.</summary>
    public static void Wire(RunViewModel vm, GraphView graph)
    {
        graph.HoveredNodeChanged += (_, id) => vm.HoveredProjectId = id;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RunViewModel.HoveredProjectId)) graph.EchoHover(vm.HoveredProjectId);
        };
    }
}
