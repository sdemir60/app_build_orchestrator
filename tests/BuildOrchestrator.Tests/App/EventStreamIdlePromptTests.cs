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
/// [prototip BuildApp.jsx:900-909] Event stream'de yazacak bir şey kalmayınca satır KAYBOLMAZ: geriye saat +
/// yanıp sönen soluk imleçten oluşan bir bekleme satırı kalır — konsolun prompt satırının ikizi.
///
/// <para>Eskiden aktif satır tamamen gizleniyordu; koşu bitince akışın altında hiçbir işaret kalmıyordu.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class EventStreamIdlePromptTests
{
    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)), () => "r1")
        { RootPath = @"D:\repo" };

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

    [StaFact]
    public void After_the_writing_stops_a_prompt_line_stays_at_the_bottom()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);

        // Bir koşu: aktif satır canlı (amber), sonra biter.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0, null));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        Assert.Equal(Visibility.Visible, view.ActiveLine.Visibility);       // ön-koşul
        Assert.Equal(Token(view, "Brush.AmberText"), CursorColour(view));   // ön-koşul: canlıyken amber

        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 100));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 0, 100));

        // Satır DURUR: saat + yanıp sönen imleç, metin boş.
        Assert.Equal(Visibility.Visible, view.ActiveLine.Visibility);
        Assert.Equal("", view.ActiveText.Text);
        Assert.True(view.ActiveCursorGlyph.HasAnimatedProperties, "bekleme imleci yanıp sönmeli");
        GC.KeepAlive(window);
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL] İmlecin KENDİ ton kaynağı vardır; prompt metnini İZLEMEZ.</b>
    ///
    /// <para><b>Eski iddia:</b> imlecin kendi rengi yoktur, satırın rengini alır (prototipte
    /// <c>currentColor</c>) — <c>Fill</c>, <c>PART_ActiveText.Foreground</c>'a bind'liydi.</para>
    ///
    /// <para><b>Değişme gerekçesi (kullanıcı):</b> imleç en son olayın rengini söylemeli. Metne bağlıyken bunu
    /// yapması imkânsızdı: metin sabit amberdir ve öyle kalmalıdır (prompt bir göstergedir, yazı yüzeyi
    /// değil). İki kanal ayrıldı — metin amber, imleç son satırın ikon rengi.</para>
    /// </summary>
    [StaFact]
    public void The_cursor_has_its_own_tone_and_no_longer_follows_the_prompt_text()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0, null));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 100));

        // Son satır başarılı → imleç yeşil, metin hâlâ amber: aynı fırça DEĞİL.
        Assert.Equal(Token(view, "Brush.StatusSuccessText"), CursorColour(view));
        Assert.Equal(Token(view, "Brush.AmberText"), ((SolidColorBrush)view.ActiveText.Foreground).Color);
        Assert.NotSame(view.ActiveText.Foreground, ((Rectangle)view.ActiveCursorGlyph).Fill);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// <b>Bekleme modunda imleç AMBERDİR.</b> Koşu bitmiş, ekranda iş yok — imleç son olayın renginde asılı
    /// kalmaz. Bu, sahada ölçülen kusurun ta kendisidir (kullanıcı: "şu an bekleme modu ama imleç kırmızı").
    /// <para>Olay TAZE iken rengi taşıması ayrı bir testin konusudur
    /// (<see cref="EventStreamTypingTests.The_cursor_wears_a_fresh_events_colour_then_rests_at_amber"/>);
    /// burada ölçülen, pencerenin gerçekten KAPANDIĞIDIR.</para>
    /// </summary>
    [StaFact]
    public void The_cursor_rests_at_amber_once_the_run_is_over()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);
        Assert.Equal(Token(view, "Brush.AmberText"), CursorColour(view)); // hiç olay yok

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0, null));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 100));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 0, 100));
        Assert.NotEqual(Token(view, "Brush.AmberText"), CursorColour(view)); // ön-koşul: taze olay rengi taşındı

        DispatcherPump.PumpUntil(
            () => CursorColour(view) == Token(view, "Brush.AmberText"), TimeSpan.FromSeconds(5));

        Assert.Equal(Token(view, "Brush.AmberText"), CursorColour(view));
        GC.KeepAlive(window);
    }

    /// <summary>
    /// <b>ANINDA basılan olaylar prompt satırına dokunmaz.</b> Fırtına (ve hata) satırları yazılmaz, doğrudan
    /// tampona düşer — yazı yüzeyi olan prompt satırı onlar için hiç kullanılmaz, gösterge metni yerinde kalır.
    ///
    /// <para>[DEĞİŞEN KURAL — kapsam] Bu testin adı bir zamanlar "arriving events never disturb" idi ve o
    /// dönem prompt HİÇBİR olayı yazmıyordu. Artık yazıyor (bkz.
    /// <see cref="EventStreamTypingTests.The_event_is_written_at_the_prompt_line_then_released_into_the_buffer"/>);
    /// dokunulmadığı hâl, olayın ANINDA basıldığı hâldir ve test tam olarak onu ayırt eder.</para>
    ///
    /// <para>İmleç yine de o olayların rengini kısa bir tazelik penceresi boyunca taşır — yoksa hata satırları
    /// hiç yazılmadığı için kırmızı hiçbir yerde görünmezdi.</para>
    /// </summary>
    [StaFact]
    public void Instantly_printed_events_never_disturb_the_prompt_line()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 3, 4, "Debug", 0, null));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        Assert.Equal("A building…", view.ActiveText.Text); // ön-koşul

        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\b.csproj", SkipReasons.UpToDate));
        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\c.csproj", SkipReasons.UpToDate));
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(120)); // satırlar yazarken bile

        // METİN yerinden oynamaz — testin asıl iddiası budur ve DEĞİŞMEDİ.
        Assert.Equal("A building…", view.ActiveText.Text);
        Assert.Equal(Token(view, "Brush.AmberText"), ((SolidColorBrush)view.ActiveText.Foreground).Color);
        // [DEĞİŞEN KURAL] İmleç ise artık son satırın ikon rengini taşır (burada: atlanmış → gri). Eski hâli
        // burada amber bekliyordu; o kural "ikide bir sarı" olduğu için kaldırıldı.
        Assert.Equal(Token(view, "Brush.StatusSkippedText"), CursorColour(view));
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [DEĞİŞEN KURAL] Bekleme satırı İLK ANDAN itibaren durur — akış boşken de.
    ///
    /// <para>Eski iddia: akışta hiç olay yokken satır gizlenir ve orada "No events yet." boş-durum metni
    /// konuşur (prototip BuildApp.jsx:870). Değişme gerekçesi (kullanıcı kararı): konsol ilk açılışta zaten
    /// yanıp sönen bir imleç gösteriyor; event stream'in bunun yerine bir cümle yazması iki paneli
    /// asimetrik yapıyordu. İmleç uygulamanın "canlıyım" işareti; ilk karede orada olmalı, boş-durum
    /// cümlesine gerek yok.</para>
    /// </summary>
    [StaFact]
    public void The_prompt_line_is_there_from_the_first_frame_even_before_any_event()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);

        Assert.Equal(Visibility.Visible, view.ActiveLine.Visibility);
        Assert.Equal("", view.ActiveText.Text);
        Assert.Equal(Token(view, "Brush.AmberText"), CursorColour(view));
        Assert.True(view.ActiveCursorGlyph.HasAnimatedProperties, "bekleme imleci yanıp sönmeli");
        GC.KeepAlive(window);
    }

    /// <summary>
    /// AYIRT EDİCİ — TEK bir olay bile bekleme satırını yerinde bırakır.
    ///
    /// <para>Sahada görülen kusur: "sync diyorum, event stream'de imleç çıkmıyor; ikinci kez sync deyince
    /// çıkıyor". Satır yalnız AKTİF PROJE değiştiğinde kuruluyordu (<c>ActiveLineGeneration</c> guard'ı) ve
    /// bir Sync hiçbir proje başlatmadığı için kuşak hiç değişmiyordu. İkinci Sync'te çıkmasının nedeni,
    /// birinci Sync'in yazımı bitince guard'ın yan etkiyle sıfırlanmasıydı — yani düzelme tesadüftü.</para>
    /// </summary>
    [StaFact]
    public void A_single_sync_event_leaves_the_prompt_line_standing()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);

        vm.OnEvent(new SyncCompletedEvent("main", null, false, ProjectCount: 3, CycleCount: 0,
            ChangedCount: 1, ToBuildCount: 1, UpToDateCount: 2));

        Assert.Equal(Visibility.Visible, view.ActiveLine.Visibility);
        Assert.Equal(Token(view, "Brush.AmberText"), CursorColour(view));
        GC.KeepAlive(window);
    }
}
