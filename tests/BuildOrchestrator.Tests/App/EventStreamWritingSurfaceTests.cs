using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Event stream'in ALT SATIRI yazı yüzeyidir: yeni bir olay orada, KENDİ renginde yazılır; yazım bitince
/// satır tamamlanmış hâliyle yukarı bırakılır ve imleç amber'a döner.
///
/// <para>[DEĞİŞEN KURAL] Önce her tampon satırı kendi yerinde daktilo ediyordu — hızlı bir koşuda alt alta
/// birkaç satır aynı anda soldan sağa açılıyordu. Sonra tekil bir "yazan satır" kuralı geldi. Kullanıcı
/// kararıyla yazım tümüyle alt satıra taşındı: yazan tek yer imlecin yanıdır, üstte biriken satırlar
/// hareketsizdir. İmleç yazarken satırın rengini, beklerken amber'ı taşır.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class EventStreamWritingSurfaceTests
{
    private const string A = @"C:\p\a.csproj";

    // Olaylar arası GERÇEK zaman: fırtına penceresi (StreamComposer) kapalı kalsın ki satırlar "instant"
    // değil YAZILARAK gelsin — testin konusu tam olarak yazım yüzeyidir.
    private long _clock = 100_000;

    private RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)),
            () => "r1", nowMs: () => _clock)
        { RootPath = @"D:\repo" };

    /// <summary>Akışa YAZILARAK gelen bir olay üretir. İlk satır hiç yazmaz (tasarım), o yüzden önce bir
    /// taban satır bırakılır; asıl ölçülen ikinci satırdır.</summary>
    private EventStreamRow WrittenRow(RunViewModel vm, EventStreamView view)
    {
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0, null));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(A, "A", true)])); // taban satır
        _clock += 10_000;                                                        // fırtına penceresi kapandı
        vm.OnEvent(new ProjectStartedEvent("r1", A, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", A, 1200));                    // yazılacak satır
        return view.Rows[^1];
    }

    private static (EventStreamView view, Window window) Realize(RunViewModel vm)
    {
        var host = DsResources.NewHost();
        var view = new EventStreamView { AnimationsEnabledProvider = () => true, DataContext = vm };
        return (view, DsResources.Realize(host, view));
    }

    private static Color Token(EventStreamView view, string key) =>
        ((SolidColorBrush)view.FindResource(key)).Color;

    private static Color CursorColour(EventStreamView view) =>
        ((SolidColorBrush)((Rectangle)view.ActiveCursorGlyph).Fill).Color;

    /// <summary>
    /// Bir olay yazılırken: metin ALT SATIRDA, kendi renginde; satırın kendisi tamponda henüz GİZLİ (aksi
    /// hâlde aynı metin hem üstte tamamlanmış hem altta yazılırken iki kez görünürdü). Yazım bitince satır
    /// görünür olur ve imleç amber'a döner.
    /// </summary>
    [StaFact]
    public void An_event_is_written_on_the_bottom_line_then_released_upward()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);

        var written = WrittenRow(vm, view);
        Assert.Equal(Visibility.Collapsed, written.Visibility);      // satır yazımını bekliyor
        Assert.Equal(Visibility.Visible, view.ActiveLine.Visibility);
        Assert.False(view.ActiveLineInstant);                        // gerçekten daktilo ediyor

        DispatcherPump.PumpUntil(() => written.Visibility == Visibility.Visible, TimeSpan.FromSeconds(5));

        Assert.Equal(Visibility.Visible, written.Visibility);        // yazım bitti → yukarı bırakıldı
        Assert.Equal(Token(view, "Brush.AmberText"), CursorColour(view)); // imleç amber'a döndü
        GC.KeepAlive(window);
    }

    /// <summary>Tampon satırları HAREKETSİZDİR: yazan tek yer alt satırdır, üstteki satır tam metnini gösterir.</summary>
    [StaFact]
    public void A_released_row_shows_its_whole_text_without_animating()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);
        var written = WrittenRow(vm, view);
        DispatcherPump.PumpUntil(() => written.Visibility == Visibility.Visible, TimeSpan.FromSeconds(5));

        Assert.Equal(vm.StreamEvents[^1].Text, written.Text.Text);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Yazım sürerken yeni bir olay gelirse bekleyen satır ANINDA yukarı bırakılır ve yeni metin yazılmaya
    /// başlar — "önceki pat diye üstte kalır, imleç yeni yazıya geçer".
    /// </summary>
    [StaFact]
    public void A_new_event_mid_write_releases_the_previous_one_at_once()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);
        var first = WrittenRow(vm, view);
        Assert.Equal(Visibility.Collapsed, first.Visibility); // ön-koşul: hâlâ yazılıyor

        _clock += 10_000;
        vm.OnEvent(new ProjectSkippedEvent("r1", A, SkipReasons.UpToDate)); // araya yeni bir olay girdi

        Assert.Equal(Visibility.Visible, first.Visibility); // önceki anında bırakıldı
        GC.KeepAlive(window);
    }
}
