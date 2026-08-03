using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T1 · madde 1.7] <see cref="DsSplitter"/>'ın etkileşim geri-bildirimi: çizgi sürüklerken
/// <c>Brush.AmberBorder</c>, bırakınca <c>Brush.Border</c> (<c>DsSplitter.cs:66-67</c>) — artı klavye
/// resize'ının persist yolu.
///
/// <para><b>Ölçülmüş boşluk:</b> renk takası TESTSİZDİ. <c>E5FoldTests</c> <c>DragCompleted</c>'ı yalnız
/// <b>persist tetiği</b> olarak dinliyordu — çizginin rengine hiç bakmıyor; iki <c>SetResourceReference</c>
/// silinse suite yeşil kalırdı (ayraç sürüklerken ölü görünürdü).</para>
///
/// <para>Tetik GERÇEK: <see cref="Thumb.DragStartedEvent"/>/<see cref="Thumb.DragCompletedEvent"/> yükseltilir —
/// fare sürüklemesinde taban <see cref="Thumb"/>'ın yükselttiği AYNI olaylar (handler doğrudan çağrılmaz).</para>
///
/// <para>[fix-1 · S2] Realize kurulumu <see cref="SplitterHost"/>'ta (ortak); klavye-resize testi konu olarak
/// buraya aittir ve <c>E5FoldTests</c>'ten TAŞINDI.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class SplitterDragTests
{
    [StaFact]
    public void The_splitter_line_turns_amber_while_dragging_and_returns_to_the_border_token_on_release()
    {
        var s = SplitterHost.ThreeColumnGrid();

        // Dinlenirken: nötr kenarlık rengi.
        Assert.Equal(DsResources.TokenColor(s.Host, "Brush.Border"), DsResources.ColorOf(s.Splitter.Line.Fill));

        s.Splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        Assert.Equal(DsResources.TokenColor(s.Host, "Brush.AmberBorder"), DsResources.ColorOf(s.Splitter.Line.Fill));
        // Ayırt edici: amber ile nötr GERÇEKTEN farklı token'lardır (aynı olsalar test vacuous olurdu).
        Assert.NotEqual(DsResources.TokenColor(s.Host, "Brush.Border"), DsResources.TokenColor(s.Host, "Brush.AmberBorder"));

        s.Splitter.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Assert.Equal(DsResources.TokenColor(s.Host, "Brush.Border"), DsResources.ColorOf(s.Splitter.Line.Fill));
        GC.KeepAlive(s.Window);
    }

    /// <summary>
    /// [fix-1 · I-A ile düzeltilmiş iddia] Ayraç çizgisi, kimse onu sürüklemeden ÖNCE de token'ını çözmüş
    /// olmalıdır — <b>bu testin koştuğu headless realize host'unda da.</b>
    ///
    /// <para><b>Bu iddia bu task'ta KIRMIZIYDI:</b> <see cref="DsSplitter"/> <c>LogicalChildren</c>'ı açmadığı için
    /// <c>Line</c> WPF'in ağaç yürüyüşlerinde görünmüyor, ctor'da ebeveynsizken çözülen <c>Brush.Border</c>
    /// referansı <c>null</c>'da kalıyordu — tam realize edilmiş bir <c>ShellRoot</c>'ta bile <c>Fill == null</c>
    /// ölçüldü. Yukarıdaki sürükleme testi bunu YAKALAYAMAZ (sürükleme handler'ları referansı ağaçtayken
    /// yeniden kurar ve kusuru kendiliğinden kapatır).</para>
    ///
    /// <para><b>Kapsam dürüstlüğü:</b> bu bir "üretimde ayraçlar görünmüyordu" iddiası DEĞİLDİR ve öyle
    /// ÖLÇÜLMEMİŞTİR. Üretimde <c>App.xaml</c> aynı sözlükleri <c>Application.Resources</c>'a MainWindow'dan ÖNCE
    /// merge eder ve WPF'in kaynak araması ağaç yürüyüşünden sonra oraya düşer; headless host'ta ise
    /// <see cref="Application"/> YOKTUR ve kaynak kapsamı ctor'dan SONRA enjekte edilir. Testin koruduğu şey
    /// budur: test host'u üretimle aynı sonucu versin ki bu assert vacuous bir <c>null</c> üzerinden geçmesin.</para></summary>
    [StaFact]
    public void Every_shell_splitter_resolves_its_resting_line_token_before_anyone_ever_drags_it()
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

    // ------------------------------------------------------------------ [a11y kararı] klavye resize persist
    // [fix-1 · S2] E5FoldTests'ten TAŞINDI — konu olarak ayraca ait; kurulum artık ortak SplitterHost'ta.
    [StaFact]
    public void Keyboard_resizing_the_splitter_commits_through_the_drag_completed_path()
    {
        var s = SplitterHost.ThreeColumnGrid();

        // Basıştan ÖNCEKİ sol-kolon genişliği (ShellRoot persist'i ActualWidth okur). 2 star-kolon 400px'i
        // ~yarı yarıya paylaşır (ayraç Auto ~7px) → başlangıçta > 0 ve iki taraf ~eşit.
        double prePress = s.Grid.ColumnDefinitions[0].ActualWidth;
        Assert.True(prePress > 0);

        bool committed = false;
        double atCompletion = double.NaN;
        s.Splitter.DragCompleted += (_, _) =>
        {
            committed = true;
            // ShellRoot'un persist yolu TAM BURADA ActualWidth okur — ayırt edici gerçek: bu okuma TAZE mi?
            atCompletion = s.Grid.ColumnDefinitions[0].ActualWidth;
        };

        s.Splitter.Focus();
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(s.Splitter)!, 0, Key.Left)
        { RoutedEvent = Keyboard.KeyDownEvent };
        s.Splitter.RaiseEvent(key);

        Assert.True(key.Handled);   // taban GridSplitter ok-tuşuyla resize etti
        Assert.True(committed);     // ...ve DsSplitter persist'i DragCompleted ile tetikledi
        // AYIRT EDİCİ: Sol ok sol-kolonu KÜÇÜLTÜR; DragCompleted anında okunan ActualWidth resize'ı YANSITMALI
        // (basıştan küçük). UpdateLayout() olmadan taban yalnız async arrange planlar → okuma BAYAT kalır
        // (atCompletion == prePress) → persist stale oranı yazar. Bu assert o hatayı yakalar.
        Assert.True(atCompletion < prePress,
            $"DragCompleted anında ActualWidth taze olmalı (resize sonrası küçülmüş): prePress={prePress}, atCompletion={atCompletion}");
        GC.KeepAlive(s.Window);
    }
}
