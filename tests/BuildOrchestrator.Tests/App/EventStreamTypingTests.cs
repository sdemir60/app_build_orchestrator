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
/// [DEĞİŞEN KURAL · prototip §6] Daktilo EN YENİ TAMPON SATIRINA aittir: satır kendi yerinde, kendi rengi ve
/// kendi glyph'iyle yazılır. Prompt satırı buna hiç karışmaz.
///
/// <para>Eski iddia (bu dosyanın önceki hâli, <c>EventStreamWritingSurfaceTests</c>): yazı yüzeyi ALT
/// SATIRDIR — olay orada kendi renginde yazılır, bitince tamponda görünür olur ve imleç amber'a döner.
/// Değişme gerekçesi (sahada görüldü): satır bırakıldığı anda 12px'lik imleç sütunu statü glyph'ine
/// dönüşüyordu ve metin hiç değişmese de göz bunu "renk değişti" diye okuyordu; akış kararsız görünüyordu.
/// Prototipin kendi modelinde bu kopukluk yoktur — satır ilk karesinden itibaren son hâlindeki renk ve
/// glyph'le durur, yalnız metni açılır.</para>
///
/// <para>Aynı anda YALNIZ bir satır yazar: yeni bir satır gelince öncekinin yazımı anında tamamlanır. Bu
/// kural, "her satırın kendi zamanlayıcısı" döneminde hızlı bir koşuda alt alta birkaç satırın aynı anda
/// soldan açılmasına yol açan kusurun düzeltmesidir ve korunur.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class EventStreamTypingTests
{
    private const string A = @"C:\p\a.csproj";
    private const string B = @"C:\p\b.csproj";

    // Olaylar arası GERÇEK zaman: fırtına penceresi (StreamComposer) kapalı kalsın ki satır "instant" değil
    // YAZILARAK gelsin — testin konusu tam olarak budur.
    private long _clock = 100_000;

    private RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)),
            () => "r1", nowMs: () => _clock)
        { RootPath = @"D:\repo" };

    private static (EventStreamView view, Window window) Realize(RunViewModel vm)
    {
        var host = DsResources.NewHost();
        var view = new EventStreamView { AnimationsEnabledProvider = () => true, DataContext = vm };
        return (view, DsResources.Realize(host, view));
    }

    private static Color Token(EventStreamView view, string key) => ((SolidColorBrush)view.FindResource(key)).Color;
    private static Color CursorColour(EventStreamView view) =>
        ((SolidColorBrush)((Rectangle)view.ActiveCursorGlyph).Fill).Color;

    /// <summary>Yazılacak bir satır üretir. İlk satır hiç yazmaz (prototip <c>prevNewest==null</c>), o yüzden
    /// önce bir taban satır bırakılır; ölçülen ikincisidir.</summary>
    private EventStreamRow WrittenRow(RunViewModel vm, EventStreamView view)
    {
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0, null));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(A, "A", true)])); // taban satır
        _clock += 10_000;                                                        // fırtına penceresi kapandı
        vm.OnEvent(new ProjectStartedEvent("r1", A, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", A, 1200));                    // yazılacak satır
        return view.Rows[^1];
    }

    [StaFact]
    public void The_newest_row_types_in_its_own_place_and_is_never_hidden()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);

        var written = WrittenRow(vm, view);

        Assert.Equal(Visibility.Visible, written.Visibility);       // satır ASLA gizlenmez
        Assert.True(written.IsTyping, "en yeni satır kendi yerinde daktilo etmeli");
        Assert.Same(view.TypingRow, written);

        DispatcherPump.PumpUntil(() => !written.IsTyping, TimeSpan.FromSeconds(5));
        Assert.Equal(vm.StreamEvents[^1].Text, written.Text.Text);  // yazım bitti → tam metin
        GC.KeepAlive(window);
    }

    /// <summary>Satırın rengi İLK KARESİNDEN itibaren son rengidir — yazım sırasında değişmez.</summary>
    [StaFact]
    public void A_typing_row_already_wears_its_final_colour()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);

        var written = WrittenRow(vm, view);
        var whileTyping = ((SolidColorBrush)written.Text.Foreground).Color;
        DispatcherPump.PumpUntil(() => !written.IsTyping, TimeSpan.FromSeconds(5));

        Assert.Equal(whileTyping, ((SolidColorBrush)written.Text.Foreground).Color);
        Assert.Equal(Token(view, vm.StreamEvents[^1].TextBrushKey), whileTyping);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Only_the_newest_row_types_a_new_one_completes_the_previous_at_once()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);
        var first = WrittenRow(vm, view);
        Assert.True(first.IsTyping); // ön-koşul

        _clock += 10_000;
        vm.OnEvent(new ProjectSkippedEvent("r1", B, SkipReasons.UpToDate)); // araya yeni bir satır girdi

        Assert.False(first.IsTyping, "önceki satırın yazımı ANINDA tamamlanmalı");
        Assert.Equal(vm.StreamEvents[^2].Text, first.Text.Text);
        Assert.Same(view.TypingRow, view.Rows[^1]);
        GC.KeepAlive(window);
    }

    /// <summary>Prompt satırı bir olayın rengini ASLA almaz — yazım sürerken bile amber kalır.</summary>
    [StaFact]
    public void The_prompt_line_stays_amber_while_a_row_is_typing()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);

        var written = WrittenRow(vm, view);
        Assert.True(written.IsTyping); // ön-koşul

        Assert.Equal(Token(view, "Brush.AmberText"), CursorColour(view));
        GC.KeepAlive(window);
    }
}
