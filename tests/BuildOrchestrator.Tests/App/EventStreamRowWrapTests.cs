using System.Windows;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// <b>[DEĞİŞEN KURAL — 2026-11-15, kullanıcı kararı] Panele sığmayan olay satırı KIRILIR; devamı metnin başladığı hizadan sürer.</b>
///
/// <para><b>Eski iddia</b> (design-v1 <c>StreamRow</c>, BuildApp.jsx:645; ARCHITECTURE §13 "one-line events"): her olay TEK satırdır, sığmayan
/// metin sağdan kesilir. Uygulamada kesme üç nokta bile göstermiyordu: satırın içeriği yatay bir <c>StackPanel</c>'di, metne sınırsız
/// genişlik veriliyordu ve taşan kısım panelin kenarında sessizce kırpılıyordu — yatay kaydırma da yoktur.</para>
///
/// <para><b>Değişme gerekçesi (sahada, gerçek istemci açıkken derleme):</b> uzun bir satırın sonu okunamıyordu ve okunamayan kısım tam da
/// işe yarayan kısımdı (ayar kapalıyken "ayarı aç" diyen not). Kullanıcı: "sağa sığmayan alt satıra geçsin ama tarihin altından değil,
/// ikondan sonra yazının başladığı hizadan devam etsin".</para>
///
/// <para>Tek satıra sığan satırın geometrisi DEĞİŞMEZ (24 px, her şey aynı merkezde) — <see cref="A_row_that_fits_keeps_its_one_line_geometry"/>
/// bunu kırılma değişikliğinden bağımsız pinler.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class EventStreamRowWrapTests
{
    private const string A = @"C:\p\a.csproj";

    /// <summary>400 px'lik panele sığmayan bir akış satırı (kelime sınırlarında kırılabilir).</summary>
    private const string LongLine =
        "A in use by Acme.Host.exe — not replaced; turn on Replace files in use, then build this project again from its row";

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), MainWindowHost.NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    /// <summary>Kısa bir <c>built</c> satırı ve panele sığmayan bir not satırı taşıyan akış, <paramref name="width"/> genişliğinde kurulur.
    /// Hareket kapalıdır: satırlar yazılmadan, son hâlleriyle durur.</summary>
    private static (EventStreamView view, Window window, EventStreamRow shortRow, EventStreamRow longRow) Realize(double width = 400)
    {
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug"));
        vm.OnEvent(new ProjectStartedEvent("r1", A, "A"));
        vm.OnEvent(new ProjectNoteEvent("r1", A, LongLine)); // kısa hâli olmayan not: akış metni aynen yazar
        vm.OnEvent(new ProjectSucceededEvent("r1", A, 1200));

        var host = DsResources.NewHost();
        var view = new EventStreamView { AnimationsEnabledProvider = () => false, DataContext = vm };
        var window = DsResources.Realize(host, view, width, height: 400);
        var shortRow = view.Rows.Single(r => r.ViewModel?.Kind == StreamKind.Ok);
        var longRow = view.Rows.Single(r => r.ViewModel?.Text == LongLine);
        return (view, window, shortRow, longRow);
    }

    private static Point Origin(FrameworkElement element, FrameworkElement row) =>
        element.TransformToAncestor(row).Transform(new Point(0, 0));

    private static double CenterY(FrameworkElement element, FrameworkElement row) =>
        Origin(element, row).Y + element.ActualHeight / 2;

    [StaFact]
    public void A_row_that_does_not_fit_wraps_instead_of_running_off_the_panel()
    {
        var (_, window, shortRow, longRow) = Realize();

        Assert.True(longRow.ActualHeight > shortRow.ActualHeight + 10,
            $"sığmayan satır kırılmadı: {longRow.ActualHeight} px (tek satır {shortRow.ActualHeight} px)");
        double textRight = Origin(longRow.Text, longRow).X + longRow.Text.ActualWidth;
        Assert.True(textRight <= longRow.ActualWidth,
            $"metin satırın dışına taşıyor: sağ kenarı {textRight} px, satır {longRow.ActualWidth} px");
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_wrapped_lines_continue_under_the_text_not_under_the_time()
    {
        var (_, window, shortRow, longRow) = Realize();
        Assert.True(longRow.ActualHeight > shortRow.ActualHeight + 10, "ön-koşul: satır kırıldı");

        // Devam satırları metnin kendi bloğundadır; o blok saat ve ikonun SAĞINDA, tek satırlık satırdakiyle AYNI hizada başlar.
        double textLeft = Origin(longRow.Text, longRow).X;
        Assert.InRange(textLeft - Origin(shortRow.Text, shortRow).X, -0.6, 0.6);
        Assert.True(textLeft >= Origin(longRow.GlyphHost, longRow).X + longRow.GlyphHost.ActualWidth,
            "metin ikon kolonundan sonra başlamalı");
        Assert.True(Origin(longRow.GlyphHost, longRow).X >= Origin(longRow.Time, longRow).X + longRow.Time.ActualWidth,
            "ikon saatten sonra gelmeli");
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_time_and_the_glyph_stay_on_the_first_line_of_a_wrapped_row()
    {
        var (view, window, shortRow, longRow) = Realize();
        Assert.True(longRow.ActualHeight > shortRow.ActualHeight + 10, "ön-koşul: satır kırıldı");
        double lineHeight = (double)view.FindResource("LineHeight.Mono12");

        // Metnin İLK satırının merkezi: bloğun tepesi + yarım satır. Saat ve ikon o satırın ortasındadır — bloğun ortasında değil.
        double firstLine = Origin(longRow.Text, longRow).Y + lineHeight / 2;
        Assert.InRange(CenterY(longRow.Time, longRow) - firstLine, -1.5, 1.5);
        Assert.InRange(CenterY(longRow.GlyphHost, longRow) - firstLine, -1.5, 1.5);
        // ...ve o satır, tek satırlık bir satırın metniyle aynı yükseklikte başlar (satırlar arası ritim bozulmaz).
        Assert.InRange(firstLine - CenterY(shortRow.Text, shortRow), -1.5, 1.5);
        GC.KeepAlive(window);
    }

    /// <summary>Kırılma kuralı tek satıra sığan satıra DOKUNMAZ: yüksekliği 24 px, saat/ikon/metin aynı dikey merkezdedir (kırılmadan önceki
    /// geometri — bu test değişiklikten önce de yeşildir, taban pinidir).</summary>
    [StaFact]
    public void A_row_that_fits_keeps_its_one_line_geometry()
    {
        var (_, window, shortRow, _) = Realize();

        Assert.InRange(shortRow.ActualHeight, 23.99, 24.01);
        double middle = shortRow.ActualHeight / 2;
        Assert.InRange(CenterY(shortRow.Time, shortRow) - middle, -0.6, 0.6);
        Assert.InRange(CenterY(shortRow.GlyphHost, shortRow) - middle, -0.6, 0.6);
        Assert.InRange(CenterY(shortRow.Text, shortRow) - middle, -0.6, 0.6);
        GC.KeepAlive(window);
    }

    /// <summary>Panel genişleyince kırılan satır yeniden tek satıra iner: kırılma içeriğin değil, o anki genişliğin sonucudur.</summary>
    [StaFact]
    public void A_wide_panel_shows_the_same_row_on_one_line()
    {
        var (_, window, shortRow, longRow) = Realize(width: 1400);

        Assert.InRange(longRow.ActualHeight - shortRow.ActualHeight, -0.01, 0.01);
        GC.KeepAlive(window);
    }
}
