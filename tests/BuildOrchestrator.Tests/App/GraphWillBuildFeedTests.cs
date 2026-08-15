using BuildOrchestrator.App;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Sync'ten sonra <b>listede amber olan projenin graf küpü de amber olmalıdır.</b> İki yüzey aynı planı
/// anlatır; ayrışırlarsa kullanıcı hangisine güveneceğini bilemez.
///
/// <para>Sahada görülen kusur tam buydu: liste satırı "derlenecek" derken graf düğümünün içi nötr kalıyordu.
/// Küpün renk kararı doğruydu — kusur BESLEMEDEYDİ. <c>BuildPreviewEvent</c> satırların <c>WillBuild</c>'ini
/// dolduruyor ama grafı besleyen hiçbir sinyal ateşlenmiyordu: graf yalnız topoloji değişiminde yeniden
/// kuruluyor (o an satırlar henüz planı bilmiyor) ve <c>Counters</c> değişiminde statü itiliyor —
/// <c>RunCounters</c> ise <c>WillBuild</c>'i hiç okumadığı için önizleme sonrası birebir aynı kalıyor ve
/// bildirimi yutuyordu.</para>
///
/// <para>Bu dosya zincirin GERÇEK ucunu ölçer: VM'e bir önizleme olayı akıtılır ve <b>çizilmiş</b> düğümün
/// glyph rengine bakılır. Süitte bu soruyu soran başka test yoktu — <c>MainWindowHost</c> fixture'ı
/// <c>BuildPreviewEvent</c> hiç göndermiyordu.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphWillBuildFeedTests
{
    private static System.Windows.Media.Color CoreColour(MainWindow window, string name) =>
        DsResources.ColorOf(window.Shell.GraphHost.NodeVisuals[name].Icon.Stroke);

    [StaFact]
    public void A_build_preview_after_sync_paints_the_cube_of_every_project_that_will_build()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("Dirty", null), ("Clean", null));
        var content = MainWindowHost.Realize(window);

        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(MainWindowHost.IdOf("Dirty"), "Dirty", true),
            new BuildPreviewItem(MainWindowHost.IdOf("Clean"), "Clean", false),
        ]));
        content.UpdateLayout();

        Assert.Equal(DsResources.TokenColor(window, "Brush.DotDirty"), CoreColour(window, "Dirty"));
        Assert.Equal(DsResources.TokenColor(window, "Brush.DotClean"), CoreColour(window, "Clean"));
    }
}
