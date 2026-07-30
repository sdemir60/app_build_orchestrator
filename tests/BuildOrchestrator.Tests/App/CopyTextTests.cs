using System.Linq;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T3a] Kopya metinleri (BİREBİR/verbatim) — a1-a12 envanterindeki kalemler arasında hiçbir mevcut test
/// dosyasına (<see cref="PopoverTests"/>/<see cref="ActionBarTests"/>/<see cref="ConsoleViewTests"/>/
/// <c>InteractionStateTests</c>/<see cref="SettingsDialogTests"/>/<see cref="RunViewModelStateTests"/>) AİT
/// OLMAYANLAR burada toplanır (görev talimatı: yeni fixture/kopya YASAK — tek yeni dosya).
///
/// <para><b>a8</b> panel caps başlıkları (<c>DEPENDENCY GRAPH</c>/<c>PROJECTS</c>/<c>EVENT STREAM</c>) ve
/// <c>← Back</c> ghost butonu dört AYRI kontrolde yaşar; hepsi tek bir realize edilmiş <see cref="ShellRoot"/>'tan
/// erişilir (<see cref="MainWindowHost"/>'un TAM <c>MainWindow</c> kurulumuna gerek yok — bu dört öğe ShellRoot
/// seviyesinde durur). <b>a12</b> "N lines"/"N events" sayaçlarının MONO ailesini (design-v1 §1.2: makine çıktısı
/// = Geist Mono) pinler — sayının kendisi zaten başka testlerde pinliydi, font ailesi değildi.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class CopyTextTests
{
    // ---------------------------------------------------------------- [A13/T3a · a8] panel caps başlıkları + ← Back

    [StaFact]
    public void Panel_caption_labels_and_the_back_button_glyph_text_are_verbatim()
    {
        var host = DsResources.NewHost();
        var shell = new ShellRoot();
        var window = DsResources.Realize(host, shell);

        // "DEPENDENCY GRAPH" — GraphView.xaml:19, adsız TrackedTextBlock (kod-tarafı erişilemez → ağaçtan bulunur).
        var caption = DsResources.RealizedObjects(shell.GraphHost).OfType<TrackedTextBlock>().Single();
        Assert.Equal("DEPENDENCY GRAPH", caption.Text);

        // "PROJECTS" (ShellRoot.xaml:48) + "EVENT STREAM" (EventStreamView.xaml:13) — ikisi de PanelHeader.Text.
        var headers = DsResources.RealizedObjects(shell).OfType<PanelHeader>().Select(h => h.Text).ToList();
        Assert.Contains("PROJECTS", headers);
        Assert.Contains("EVENT STREAM", headers);

        // "← Back" — ConsoleHeader.xaml:40 (Content="&#x2190; Back").
        Assert.Equal("← Back", shell.ConsoleHeaderControl.BackButton.Content);

        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [A13/T3a · a12] "N lines"/"N events" mono ailesi

    /// <summary>[A13/T3a · a12] design-v1 §1.2: makine çıktısı (sayaç dahil) DAİMA Geist Mono — sistem Consolas'ı
    /// tasarımın parçası DEĞİLDİR. <c>ConsoleModesTests.cs:34</c> sayının kendisini ("12 lines") pinliyordu,
    /// font ailesini değil.</summary>
    [StaFact]
    public void The_lines_and_events_counters_use_the_mono_font_family_not_the_ui_sans()
    {
        var header = new ConsoleHeader();
        Assert.Same(AppFonts.Mono, header.LinesText.FontFamily);

        var stream = new EventStreamView();
        Assert.Same(AppFonts.Mono, stream.Counter.FontFamily);
    }
}
