using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
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

    private static (EventStreamView view, Window window) Realize(RunViewModel vm, bool motion = true)
    {
        var host = DsResources.NewHost();
        var view = new EventStreamView { AnimationsEnabledProvider = () => motion, DataContext = vm };
        return (view, DsResources.Realize(host, view));
    }

    private static Color Token(EventStreamView view, string key) => ((SolidColorBrush)view.FindResource(key)).Color;
    private static Color CursorColour(EventStreamView view) =>
        ((SolidColorBrush)((Rectangle)view.ActiveCursorGlyph).Fill).Color;

    /// <summary>Yazılacak bir satır üretir. İlk satır hiç yazmaz (prototip <c>prevNewest==null</c>), o yüzden
    /// önce bir taban satır bırakılır; ölçülen ikincisidir.</summary>
    private EventStreamRow WrittenRow(RunViewModel vm, EventStreamView view)
    {
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", null));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(A, "A", true)])); // taban satır
        _clock += 10_000;                                                        // fırtına penceresi kapandı
        vm.OnEvent(new ProjectStartedEvent("r1", A, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", A, 1200));                    // yazılacak satır
        return view.Rows[^1];
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL] Yazı yüzeyi ALTTAKİ İMLEÇ SATIRIDIR; olay orada yazılır, bitince tampona bırakılır.</b>
    ///
    /// <para><b>Eski iddia:</b> satır kendi yerinde (tamponda, imleç satırının bir üstünde) yazılır ve ASLA
    /// gizlenmez; prompt satırı hiç karışmaz.</para>
    ///
    /// <para><b>Değişme gerekçesi (kullanıcı, sahada):</b> "yeni satır da cursor satırında olmalı, sonra üst
    /// satıra geçecek — üstündeki gereksiz satırda sağa doğru açılır animasyon çalışıyor". Gördüğü animasyon
    /// tek ve doğru olandı, ama istediği yerde değildi.</para>
    ///
    /// <para><b>Eski modeli bir kez kaldıran gerekçe artık geçersiz:</b> bırakma anında 12px'lik imleç sütunu
    /// statü glyph'ine dönüşüyor ve göz bunu "renk değişti" diye okuyordu. İmleç bugün zaten o olayın ikon
    /// rengini taşıyor, yani devir teslim renk-tutarlı.</para>
    ///
    /// <para>Satır yazım boyunca GİZLİDİR: aksi halde olay hem altta yazılırken hem yukarıda dururken iki kez
    /// görünür, üstelik tampon daha yazım başlamadan büyüyüp üstteki her şeyi yukarı iterdi.</para>
    /// </summary>
    [StaFact]
    public void The_event_is_written_at_the_prompt_line_then_released_into_the_buffer()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);

        var written = WrittenRow(vm, view);
        string full = vm.StreamEvents[^1].Text;

        // Yazım SÜRERKEN: satır gizli, açılan şey prompt satırıdır.
        Assert.True(written.IsTyping, "en yeni olay yazılmalı");
        Assert.Equal(Visibility.Collapsed, written.Visibility);
        Assert.Equal(full, written.Text.Text); // tampon satırı ZATEN tam — o hiç animasyon oynatmaz

        // [karakter kilitlenmesi] Satırın GENİŞLİĞİ ilk kareden itibaren sabittir: kuyruk boş bırakılmaz,
        // ŞEFFAF basılır. Metin hiçbir anda akmaz/reflow olmaz — daktilonun en rahatsız edici yanı buydu.
        Assert.Equal(full.Length, view.ActiveText.Text.Length);
        var runs = view.ActiveText.Inlines.OfType<Run>().ToList();
        Assert.True(runs.Count >= 2, "kilitlenmiş metin + titreyen pencere (+ şeffaf kuyruk) ayrı Run'lardır");
        Assert.Equal(Brushes.Transparent, runs[^1].Foreground);
        Assert.StartsWith(runs[0].Text, full, StringComparison.Ordinal); // kilitlenen kısım GERÇEK metindir

        DispatcherPump.PumpUntil(() => !written.IsTyping, TimeSpan.FromSeconds(5));

        // Bırakma: satır tamponda görünür ve TAM; prompt göstergeye döner.
        Assert.Equal(Visibility.Visible, written.Visibility);
        Assert.Equal(full, written.Text.Text);
        Assert.DoesNotContain(full, view.ActiveText.Text, StringComparison.Ordinal);
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

    /// <summary>Pencere tepsiye inip döner: gizliyken bir olay gelir (satır kurulmaz, ekran bayat işaretlenir), görününce görünüm satırları
    /// modelden yeniden kurar (<c>EventStreamView.OnPropertyChanged</c> → <c>RebuildRows</c>). Görünümün yeniden kurulumunu tetikleyen
    /// üretim yolu budur; pompa yoktur, yani ağaçtan <c>Unloaded</c> gelmeden ve daktilo hiç tik atmadan sonuç okunur.</summary>
    private void RebuildThroughHideAndShow(RunViewModel vm, EventStreamView view)
    {
        HiddenSurface.SetIsHidden(view, true);
        vm.OnEvent(new ProjectSkippedEvent("r1", B, SkipReasons.UpToDate)); // gizliyken gelen olay
        HiddenSurface.SetIsHidden(view, false);
    }

    /// <summary>
    /// <b>Satırları yeniden kuran görünüm atılan satırın daktilo saatini durdurur.</b> Yeniden kurulum satırlarını atarken DataContext'lerini
    /// koparır; yazmakta olan satır bunların arasındaysa kopuş saati yarıda bırakır: <c>OnTypeTick</c> VM'i göremeyince erken döner ve
    /// <c>FinishTyping</c>'e hiç ulaşmaz, saat yalnız <c>Unloaded</c> ile durur — ağaçtan <c>Unloaded</c> gelmeyen (hiç yüklenmemiş) satırda
    /// Render önceliğinde sonsuza dek tıklar. Kural Add dalındakinin AYNISIDIR (yeni satır gelince önceki <c>FinishTyping</c> ile
    /// kapatılır): yeniden kurulum da atılan satırın yazımını DataContext'ten ÖNCE kapatır ve yazan satır referansını bırakır.
    /// </summary>
    [StaFact]
    public void Rebuilding_the_rows_stops_the_typewriter_clock_of_the_discarded_row()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);
        var written = WrittenRow(vm, view);
        Assert.True(written.IsTyping);               // ön-koşul: animasyonlar açık, en yeni satır yazıyor
        Assert.Same(written, view.TypingRow);        // ön-koşul: görünüm onu yazan satır olarak tutuyor

        RebuildThroughHideAndShow(vm, view);

        Assert.DoesNotContain(written, view.Rows);   // ön-koşul: satır gerçekten atıldı (yeniden kurulumdan geçildi)
        Assert.False(written.IsTyping, "atılan satırın daktilo saati durmalı");
        Assert.Null(view.TypingRow);                 // görünüm atılan satırı yazan satır olarak tutmaz
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Atılan satır prompt satırında YAZILIYORKEN yeniden kurulum prompt satırını göstergeye döndürür. Yazı yüzeyi alttaki imleç satırıdır:
    /// yazılan satır tamponda gizlidir ve prompt metni onu aynalar; yazımın bitişi (<c>TypingEnded</c> → <c>ReleaseToBuffer</c>) satırı
    /// bırakır ve göstergeyi geri getirir. Yeniden kurulum yazımı kapatmazsa atılan satır yazı yüzeyi olarak ASILI kalır
    /// (<c>_writingRow</c>): dönüşteki <c>UpdateActiveLine</c> "yazı yüzeyi kullanımda" diye göstergeyi yazmadan döner ve prompt satırı
    /// atılmış satırın yarım metnini taşır. Yazımı kapatmanın <c>TypingEnded</c> yan etkisi atılan satır için tam da gereken şeydir.
    /// </summary>
    [StaFact]
    public void Rebuilding_the_rows_returns_the_prompt_line_to_the_indicator_when_the_discarded_row_was_being_written()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);
        WrittenRow(vm, view);
        Assert.Equal(vm.StreamEvents[^1].Text.Length, view.ActiveText.Text.Length); // ön-koşul: prompt satırı yazılan metni aynalıyor
        Assert.NotEqual(vm.ActiveLineText ?? "", view.ActiveText.Text);             // ön-koşul: ...ve bu gösterge metni DEĞİL

        RebuildThroughHideAndShow(vm, view);

        Assert.Equal(vm.ActiveLineText ?? "", view.ActiveText.Text); // prompt göstergeye döndü: atılan satırın yarım metni kalmadı
        GC.KeepAlive(window);
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL] İmleç olay TAZE iken onun ikon rengini taşır, pencere kapanınca amber'a döner.</b>
    ///
    /// <para><b>İki uç da sahada denendi ve ikisi de yanlıştı.</b> (1) Rengi yalnız YAZIM boyunca vermek:
    /// "ikide bir sarı" — pencere satır arasındaki boşluktan kısa, yeşil neredeyse hiç görülmüyor; üstelik
    /// hata satırları hiç yazılmadığı için (<c>StreamComposer</c> hataları anında basar) kırmızı görünemiyor
    /// bile. (2) Rengi süresiz KORUMAK: beklerken imleç son olayın renginde kalıyor — koşu bitmiş, ekranda iş
    /// yok, imleç hâlâ kırmızı (kullanıcı: "şu an bekleme modu ama imleç kırmızı").</para>
    ///
    /// <para><b>Doğrusu bir tazelik penceresidir:</b> olay olurken rengi, olay geçince amber. Pencere anında
    /// basılan satırları da kapsar — kırmızının görünebildiği tek yer orasıdır.</para>
    ///
    /// <para><b>METİN DEĞİŞMEDİ:</b> prompt metni her zaman amberdir; imleç ona bağlı değildir.</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.12.1 §2.6]</b> Bu tazelik penceresi artık YALNIZ renk turu
    /// dönmezken görünür: motion açıkken imlecin rengini <see cref="CursorHop"/> sürer (satır paletinde altı
    /// adımlı tur) ve ton kanalı onu ezmez. Test bu yüzden <b>reduced-motion</b> kurar — pinlenen kural aynı
    /// kural, yalnız görünür olduğu kip açıkça yazılır.</para>
    /// </summary>
    [StaFact]
    public void The_cursor_wears_a_fresh_events_colour_then_rests_at_amber()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm, motion: false);
        Assert.Equal(Token(view, "Brush.AmberText"), CursorColour(view)); // hiç olay yok → dinlenme

        // Başarılı satır TAZE iken yeşil...
        var written = WrittenRow(vm, view);
        Assert.Equal(Token(view, "Brush.StatusSuccessText"), CursorColour(view));
        Assert.Equal(Token(view, "Brush.AmberText"), ((SolidColorBrush)view.ActiveText.Foreground).Color);

        // ...pencere kapanınca amber. (Yazım + CursorHoldMs'ten uzun bir tavan.)
        DispatcherPump.PumpUntil(
            () => CursorColour(view) == Token(view, "Brush.AmberText"), TimeSpan.FromSeconds(5));
        Assert.Equal(Token(view, "Brush.AmberText"), CursorColour(view));

        // HATA satırı hiç YAZILMAZ ama TAZEDİR — kırmızı ancak bu pencerede görünebilir.
        _clock += 10_000;
        vm.OnEvent(new ProjectStartedEvent("r1", B, "B"));
        vm.OnEvent(new ProjectFailedEvent("r1", B, 900, "MSB4181"));
        Assert.Equal(Token(view, "Brush.StatusFailText"), CursorColour(view));

        DispatcherPump.PumpUntil(
            () => CursorColour(view) == Token(view, "Brush.AmberText"), TimeSpan.FromSeconds(5));
        Assert.Equal(Token(view, "Brush.AmberText"), CursorColour(view));
        GC.KeepAlive(window);
    }

    /// <summary>
    /// <b>WPF'in ERTELENMİŞ <c>Loaded</c>'ı açılışı bozmaz.</b>
    ///
    /// <para><b>Ölçülen kusur (kullanıcı: "pat pat diye değişiyor"):</b> açılış satır ağaca eklenirken başlar
    /// (<c>StartTypingIfPending</c>), ama WPF <c>Loaded</c>'ı ERTELENMİŞ yayar ve <c>OnLoaded</c> açılışı ikinci
    /// kez uygulamaya çalışırdı; "zaten oynadı" guard'ının erken dönüşü metni TAM hâline sıçratıyor, bir sonraki
    /// tick onu geri alıyordu. <c>Loaded</c> artık yalnız parıltıyı uygular.</para>
    ///
    /// <para>Test <b>üretimdeki gerçek tetiği</b> kullanır: <c>Loaded</c>'ı elle yayar ve açılışın ne bittiğini
    /// ne de sıçradığını doğrular.</para>
    /// </summary>
    [StaFact]
    public void A_deferred_Loaded_never_disturbs_the_writing()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);

        var written = WrittenRow(vm, view);
        Assert.True(written.IsTyping); // ön-koşul
        string lockedBefore = LockedText(view);

        written.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

        Assert.True(written.IsTyping, "Loaded açılışı bitirmemeli");
        Assert.Equal(lockedBefore, LockedText(view)); // ne ilerledi ne sıçradı
        GC.KeepAlive(window);
    }

    /// <summary>Prompt satırında KİLİTLENMİŞ (gerçek) kısım — ilk Run.</summary>
    private static string LockedText(EventStreamView view) =>
        view.ActiveText.Inlines.OfType<Run>().First().Text;

    /// <summary>
    /// <b>Animasyon kapalıyken geçen satırlar SONRADAN yazmaz.</b>
    ///
    /// <para><b>Ölçülen kusur (kullanıcı: "üstteki satırlarda sağa doğru açılma"):</b> animasyon kapalıyken
    /// <c>ApplyTypewriter</c> erken dönüyor ama <c>TypePlayed</c>'i İŞARETLEMİYORDU. O satırlar "hiç yazmadım"
    /// olarak kalıyor; panel yeniden kurulduğunda (koleksiyon Reset'i → <c>RebuildRows</c>) her birinin
    /// <c>Loaded</c>'ı yazımı BAŞLATIYOR ve ekranda alt alta birkaç eski satır aynı anda soldan açılıyordu.</para>
    ///
    /// <para>Motion sözleşmesi zaten bunu söyler: sinyal sonradan açılınca GERİYE DÖNÜK animasyon başlatılmaz.
    /// "Anında basıldı" da bir oynanmışlıktır.</para>
    /// </summary>
    [StaFact]
    public void Rows_that_passed_while_motion_was_off_never_type_later()
    {
        bool animations = false;
        var vm = NewVm();
        var host = DsResources.NewHost();
        var view = new EventStreamView { AnimationsEnabledProvider = () => animations, DataContext = vm };
        var window = DsResources.Realize(host, view);

        WrittenRow(vm, view);                        // motion KAPALI iken geçti
        _clock += 10_000;
        vm.OnEvent(new ProjectSkippedEvent("r1", B, SkipReasons.UpToDate));

        animations = true;                           // kullanıcı OS ayarını açtı
        foreach (var row in view.Rows) row.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

        Assert.DoesNotContain(view.Rows, r => r.IsTyping);
        GC.KeepAlive(window);
    }
}
