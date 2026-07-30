using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T1 · madde 1.7] <see cref="DsSplitter"/>'ın sürükleme geri-bildirimi: çizgi sürüklerken
/// <c>Brush.AmberBorder</c>, bırakınca <c>Brush.Border</c> (<c>DsSplitter.cs:66-67</c>).
///
/// <para><b>Ölçülmüş boşluk:</b> bu iki satır TESTSİZDİ. <c>E5FoldTests.cs:97</c> <c>DragCompleted</c>'ı yalnız
/// <b>persist tetiği</b> olarak dinliyor — çizginin rengine hiç bakmıyor; iki <c>SetResourceReference</c>
/// silinse suite yeşil kalırdı (ayraç sürüklerken ölü görünürdü).</para>
///
/// <para>Tetik GERÇEK: <see cref="Thumb.DragStartedEvent"/>/<see cref="Thumb.DragCompletedEvent"/> yükseltilir —
/// fare sürüklemesinde taban <see cref="Thumb"/>'ın yükselttiği AYNI olaylar (handler doğrudan çağrılmaz).</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class SplitterDragTests
{
    /// <summary>Ayracı gerçek bir Grid + token'lı kaynak kapsamında realize eder — <c>Brush.*</c> anahtarları
    /// ancak ağaçta çözülür (<c>SetResourceReference</c> DynamicResource'tur).</summary>
    private static DsSplitter Realize(out Window window, out Border host)
    {
        var grid = new Grid { Width = 400, Height = 200 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var splitter = new DsSplitter { LineOrientation = SplitterLine.Vertical };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);

        host = DsResources.NewHost();
        window = DsResources.Realize(host, grid);
        return splitter;
    }

    [StaFact]
    public void The_splitter_line_turns_amber_while_dragging_and_returns_to_the_border_token_on_release()
    {
        var splitter = Realize(out var window, out var host);

        // Dinlenirken: nötr kenarlık rengi.
        Assert.Equal(DsResources.TokenColor(host, "Brush.Border"), DsResources.ColorOf(splitter.Line.Fill));

        splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        Assert.Equal(DsResources.TokenColor(host, "Brush.AmberBorder"), DsResources.ColorOf(splitter.Line.Fill));
        // Ayırt edici: amber ile nötr GERÇEKTEN farklı token'lardır (aynı olsalar test vacuous olurdu).
        Assert.NotEqual(DsResources.TokenColor(host, "Brush.Border"), DsResources.TokenColor(host, "Brush.AmberBorder"));

        splitter.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Assert.Equal(DsResources.TokenColor(host, "Brush.Border"), DsResources.ColorOf(splitter.Line.Fill));
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [A13/T1 — ölçülmüş kusur, regresyon kilidi] Ayraç çizgisi DİNLENİRKEN de çizilir. Bu iddia bu task'ta
    /// KIRMIZIYDI: <see cref="DsSplitter"/> <c>LogicalChildren</c>'ı açmadığı için <c>Line</c> mantıksal ağaçta
    /// görünmüyor, ctor'da ebeveynsiz çözülen <c>Brush.Border</c> referansı <c>null</c>'da kalıyordu — tam realize
    /// edilmiş bir <see cref="ShellRoot"/>'ta bile <c>Fill == null</c>, yani üç ayracın hiçbiri görünmüyordu.
    /// Yukarıdaki sürükleme testi bunu YAKALAYAMAZ (sürükleme handler'ları referansı ağaçtayken yeniden kurar).
    /// </summary>
    [StaFact]
    public void Every_shell_splitter_draws_its_resting_line_before_anyone_ever_drags_it()
    {
        var shell = new BuildOrchestrator.App.ShellRoot();
        var host = DsResources.NewHost();
        var window = DsResources.Realize(host, shell);

        var expected = DsResources.TokenColor(host, "Brush.Border");
        Assert.Equal(expected, DsResources.ColorOf(shell.ColumnSplitter.Line.Fill));
        Assert.Equal(expected, DsResources.ColorOf(shell.LeftSplitter.Line.Fill));
        Assert.Equal(expected, DsResources.ColorOf(shell.RightSplitter.Line.Fill));
        GC.KeepAlive(window);
    }
}
