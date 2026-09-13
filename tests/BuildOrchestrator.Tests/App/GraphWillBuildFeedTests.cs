using BuildOrchestrator.App;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// <b>Liste ile graf AYNI hikâyeyi anlatır.</b> İki yüzey ayrışırsa kullanıcı hangisine güveneceğini bilemez;
/// bu dosya zincirin GERÇEK ucunu ölçer — VM'e olay akıtılır ve <b>çizilmiş</b> düğümün glyph rengine bakılır.
///
/// <para><b>[DEĞİŞEN KURAL — design v1.11.0 §9-1/§9-3]</b> Eski iddia: <i>"Sync'ten sonra listede amber olan
/// projenin graf küpü de amber olmalıdır"</i> — yani <c>BuildPreviewEvent</c> küpü PLAN rengiyle boyardı
/// (<c>Brush.DotDirty</c>/<c>Brush.DotClean</c>). v1.11.0 plan kanalını kaldırdı ve Sync'i BAŞLANGIÇ MODU
/// yaptı: <i>"Sync ve uygulama açılışı hiçbir şeyi renklendirmez"</i>. Yeni iddia bu yüzden terstir — önizleme
/// grafta HİÇBİR renk üretmez; küp bir işlem başlayana kadar nötr kalır.</para>
///
/// <para>Beslemenin kendisi (önizleme → graf) hâlâ ölçülüyor: kusur bir zamanlar tam oradaydı (graf yalnız
/// topoloji değişiminde kurulup bir daha haber almıyordu) ve şimdi işlem başlangıcının grafa ulaştığı
/// pinleniyor.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphWillBuildFeedTests
{
    // NodeVisuals proje Id'siyle anahtarlanır (ad benzersiz değildir) — fixture'ın Id kuralı MainWindowHost'ta.
    private static GraphNodeVisual VisualOf(MainWindow window, string name) =>
        window.Shell.GraphHost.NodeVisuals[MainWindowHost.IdOf(name)];

    private static System.Windows.Media.Color CoreColour(MainWindow window, string name) =>
        DsResources.ColorOf(VisualOf(window, name).Icon.Stroke);

    [StaFact]
    public void A_build_preview_after_sync_leaves_every_cube_neutral_because_sync_shows_no_plan()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("Dirty", null), ("Clean", null));
        var content = MainWindowHost.Realize(window);

        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(MainWindowHost.IdOf("Dirty"), "Dirty", true),
            new BuildPreviewItem(MainWindowHost.IdOf("Clean"), "Clean", false),
        ]));
        content.UpdateLayout();

        // Plan bilinse bile RENK yok: ikisi de başlangıç modunun nötr küpünü taşır.
        Assert.Equal(DsResources.TokenColor(window, "Brush.TextFaint"), CoreColour(window, "Dirty"));
        Assert.Equal(DsResources.TokenColor(window, "Brush.TextFaint"), CoreColour(window, "Clean"));
        Assert.NotEmpty(VisualOf(window, "Dirty").Square.StrokeDashArray); // kesikli = fresh
    }

    /// <summary>Bir işlem başlayınca başlangıç modu DÜŞER ve bu graf'a ULAŞIR: kesikli çerçeve düze döner.
    /// Besleme kusuru (graf haber almıyor) tam burada ölçülür.</summary>
    [StaFact]
    public void Starting_an_operation_drops_the_fresh_mode_and_the_graph_hears_about_it()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("Dirty", null), ("Clean", null));
        var content = MainWindowHost.Realize(window);
        Assert.NotEmpty(VisualOf(window, "Dirty").Square.StrokeDashArray); // ön-koşul

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug", 0));
        content.UpdateLayout();

        DispatcherPump.PumpUntil(
            () => VisualOf(window, "Dirty").Square.StrokeDashArray.Count == 0,
            TimeSpan.FromSeconds(3));
        Assert.Empty(VisualOf(window, "Dirty").Square.StrokeDashArray);
    }
}
