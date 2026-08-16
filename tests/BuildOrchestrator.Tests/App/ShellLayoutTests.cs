using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T35] Görünüm modu / split yüzdesi saf çekirdeği (<see cref="LayoutState"/>) — STA gerekmez, WPF'e dokunmaz.
/// Kaynak: design-v1 BuildApp.jsx:1143 (varsayılan), :1410-1417 (preset'ler), :1394/:1399/:1404 (clamp'ler).
/// </summary>
public class LayoutStateTests
{
    [Fact]
    public void Quad_preset_resets_all_three_splits_to_fifty()
        => Assert.Equal(new LayoutState(LayoutMode.Quad, 50, 50, 50),
                        new LayoutState(LayoutMode.Focus, 60, 74, 76).WithMode(LayoutMode.Quad));

    [Fact]
    public void List_preset_only_sets_right_to_fifty_and_keeps_col_and_left()
    {
        var s = new LayoutState(LayoutMode.Quad, 60, 74, 76).WithMode(LayoutMode.List);
        Assert.Equal(LayoutMode.List, s.Mode);
        Assert.Equal(60, s.ColPct); Assert.Equal(74, s.LeftPct); Assert.Equal(50, s.RightPct);
    }

    [Fact]
    public void Focus_preset_only_sets_right_to_seventysix()
        => Assert.Equal(76, new LayoutState(LayoutMode.Quad, 60, 74, 50).WithMode(LayoutMode.Focus).RightPct);

    [Theory]
    [InlineData(5, 28)] [InlineData(95, 72)] [InlineData(50, 50)]
    public void Column_split_is_clamped_between_twentyeight_and_seventytwo(double input, double expected)
        => Assert.Equal(expected, LayoutState.Default.WithCol(input).ColPct);

    [Theory]
    [InlineData(5, 18)] [InlineData(95, 82)]
    public void Row_splits_are_clamped_between_eighteen_and_eightytwo(double input, double expected)
    {
        Assert.Equal(expected, LayoutState.Default.WithLeft(input).LeftPct);
        Assert.Equal(expected, LayoutState.Default.WithRight(input).RightPct);
    }
}

/// <summary>
/// [T35] Pencere gövdesi <see cref="ShellRoot"/> — 2×2 panel yerleşimi, görünüm modları (graf gizleme) ve
/// <see cref="DsSplitter"/> kavrama bandı. WPF kaynak çekişmesi → serial collection; STA zorunlu.
/// </summary>
[Collection("Console UI (serial)")]
public class ShellLayoutTests
{
    [StaFact]
    public void Shell_has_four_panels_in_quad_mode_and_hides_the_graph_in_list_and_focus()
    {
        var shell = new ShellRoot();                 // MainWindow'un test edilebilir icerik kontrolu
        shell.ApplyLayout(new LayoutState(LayoutMode.Quad, 50, 50, 50));
        Assert.Equal(Visibility.Visible, shell.GraphHost.Visibility);
        shell.ApplyLayout(LayoutState.Default.WithMode(LayoutMode.List));
        Assert.Equal(Visibility.Collapsed, shell.GraphHost.Visibility);
        Assert.Equal(Visibility.Collapsed, shell.LeftSplitter.Visibility);   // graf gizliyken yatay splitter da gider
        shell.ApplyLayout(LayoutState.Default.WithMode(LayoutMode.Focus));
        Assert.Equal(Visibility.Collapsed, shell.GraphHost.Visibility);
    }

    /// <summary>
    /// [A13/T3b · b11 fix] ÖNCEKİ hâl TOTOLOJİKTİ: <c>Assert.Equal(DsSplitter.GrabBand, col.Width)</c> sabiti
    /// KENDİSİYLE kıyaslıyordu — <c>GrabBand</c> 7'den 40'a değişse bile bu satır YEŞİL kalırdı (üretim
    /// kusuruna kör). Otorite — design-v1 README §2.2 (Splitter'lar, BİREBİR): <i>"7px tutma alanı, görünür
    /// kısım 1px çizgi"</i>. Beklenen değer artık bu OTORİTE LİTERALİDİR, üretim sabitinden OKUNMAZ.
    ///
    /// <para><b>Ayırt edicilik (kural 1, kanıtlandı):</b> <c>DsSplitter.GrabBand</c> geçici olarak 40'a
    /// çekilip süit koşturuldu → bu test KIRMIZI düştü (<c>Assert.Equal(7.0, DsSplitter.GrabBand)</c>
    /// başarısız), eski totolojik hâliyle YEŞİL kalırdı. Değişiklik sonra geri alındı — bkz. T3b raporu.</para>
    ///
    /// <para>İkinci, AYRI bir iddia: sabitin GERÇEKTEN kullanıldığı (kolon genişliği / çizgi kalınlığı GrabBand'e
    /// eşit) — bu olmadan yalnız "7.0 sabittir" iddiası neyin kontrol edildiğini kanıtlamaz (brief'in b11
    /// notu: "İki iddia ayrılmazsa totoloji geri gelir").</para>
    /// </summary>
    [StaFact]
    public void Splitter_grab_band_is_seven_pixels_against_design_v1_not_against_itself()
    {
        // (1) OTORİTE İDDİASI — literal, üretim sabitinden BAĞIMSIZ okunur.
        Assert.Equal(7.0, DsSplitter.GrabBand);
        Assert.Equal(1.0, DsSplitter.LineThickness);

        // (2) KULLANIM İDDİASI — sabit gerçekten kolon genişliğine/çizgi kalınlığına uygulanmış mı.
        var col = new DsSplitter { LineOrientation = SplitterLine.Vertical };
        Assert.Equal(DsSplitter.GrabBand, col.Width);          // 7px kavrama alanı (dikey ayraç)
        Assert.Equal(DsSplitter.LineThickness, col.Line.Width); // ortada 1px görünür çizgi

        var row = new DsSplitter { LineOrientation = SplitterLine.Horizontal };
        Assert.Equal(DsSplitter.GrabBand, row.Height);
        Assert.Equal(DsSplitter.LineThickness, row.Line.Height);
    }

    /// <summary>
    /// [C1 review I-1] Sadece boyutları (Width/Height) doğrulamak yetmez: <c>Template = null</c> +
    /// <c>Background</c> ikilisi bir <see cref="System.Windows.Controls.Control"/> türevinde KENDİLİĞİNDEN hit
    /// test edilebilir bir yüzey ÜRETMEZ (Control'ün varsayılan <c>OnRender</c>'ı yoktur — boyama yalnız bir
    /// ControlTemplate üzerinden gelir). Bu test 7px kavrama bandının, ortadaki 1px görünür çizginin TAM
    /// ÜZERİNDE olmayan bir noktasını (~2px uzağı) hit-test eder: bant GERÇEKTEN tıklanabilir mi, yoksa yalnız
    /// 1px'lik çizgi mi tıklanabilir, ayırt eder.
    /// </summary>
    [StaFact]
    public void Grab_band_is_hit_testable_a_couple_pixels_off_the_visible_line_not_only_on_it()
    {
        var host = new Grid { Width = 200, Height = 100 };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var splitter = new DsSplitter { LineOrientation = SplitterLine.Vertical };
        Grid.SetColumn(splitter, 1);
        host.Children.Add(splitter);

        host.Measure(new Size(200, 100));
        host.Arrange(new Rect(0, 0, 200, 100));

        // Auto kolon, splitter'ın kendi Width'ine (7px) daralır; x = (200-7)/2 = 96.5'te başlar. Ortadaki 1px
        // görünür çizgi bu 7px'in TAM ortasındadır (x ~= 99.5..100.5). Bandın yakın kenarına 2px içeriden
        // (x = 98.5) — çizginin ÜZERİNDE değil, ama 7px bandın İÇİNDE — bir nokta prob'luyoruz.
        var probePoint = new Point(98.5, 50);
        var result = VisualTreeHelper.HitTest(host, probePoint);

        Assert.True(IsSplitterOrDescendant(result?.VisualHit, splitter),
            $"Nokta {probePoint} 7px kavrama bandı içinde ama DsSplitter yerine " +
            $"{result?.VisualHit?.GetType().Name ?? "hiçbir şey"} hit-test edildi.");
    }

    // [A13/T3 fix-2 · 7] Ata yürüyüşü DsResources.IsSelfOrDescendantOf'ta (kopya YASAK) — salt görsel kip,
    // eski davranışla birebir.
    private static bool IsSplitterOrDescendant(DependencyObject? hit, DsSplitter splitter) =>
        hit is not null && DsResources.IsSelfOrDescendantOf(hit, splitter);

    [StaFact]
    public void Applying_a_layout_records_it_and_leaves_the_graph_visible_in_quad()
    {
        var shell = new ShellRoot();
        var state = new LayoutState(LayoutMode.Quad, 61, 33, 76);
        shell.ApplyLayout(state);
        Assert.Equal(state, shell.Layout);
        Assert.Equal(Visibility.Visible, shell.GraphHost.Visibility);
        Assert.Equal(Visibility.Visible, shell.LeftSplitter.Visibility);
    }

    /// <summary>
    /// PROJECTS başlığındaki filtre kutusu şeridi KAPLAMAZ — altında ve üstünde boşluk bırakır.
    ///
    /// <para>Kutu md ölçüsündeyken (28) şerit yüksekliğinin (<c>Size.PanelHeaderHeight</c>, 28) tamamını
    /// yiyordu: kendi kenarları şeridin kenarlarıyla üst üste biniyor, odak halkası (<c>Size.FocusRingWidth</c>,
    /// öğenin DIŞINA çizilir) şeridin dışına taşıyordu. Pay, halkanın sığacağı kadardır — sayı testte sabit
    /// değil, token'dan okunur.</para>
    /// </summary>
    [StaFact]
    public void The_projects_filter_box_leaves_room_above_and_below_inside_the_panel_header()
    {
        var host = DsResources.NewHost();
        var shell = new ShellRoot();
        var window = DsResources.Realize(host, shell, 900, 600);

        var box = shell.ProjectFilterBox;
        var header = DsResources.Ancestors(box).OfType<PanelHeader>().Single();
        double clearance = (double)host.FindResource("Size.FocusRingWidth");

        double top = box.TransformToAncestor(header).Transform(new Point(0, 0)).Y;
        double bottom = header.ActualHeight - top - box.ActualHeight;

        Assert.True(top >= clearance && bottom >= clearance,
            $"Filtre kutusu {header.ActualHeight}px şeritte {box.ActualHeight}px — üst pay {top}, alt pay {bottom}; " +
            $"her ikisi de en az {clearance} olmalı (odak halkası bu payın içine çizilir).");

        GC.KeepAlive(window);
    }
}

/// <summary>
/// [T35 fold #1] Title-bar yüksekliğinin (40) TEK kaynağı Size.TitleBarHeight token'ıdır: MainWindow.xaml'de
/// artık ne WindowChrome CaptionHeight literali ne de title-bar satırının Height="40" literali VARDIR — ikisi de
/// kod-tarafı (MainWindow ctor) o token'dan sürülür. Bu, B1'de tespit edilen "unpinned third definition site"ı
/// kapatır (token değeri DesignTokenScaleTests'te 40'a pinlidir).
/// </summary>
[Collection("Console UI (serial)")]
public sealed class TitleBarHeightSingleSourceTests
{
    private static string ReadAppSource(string relative)
        => File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, relative));

    [Fact]
    public void MainWindow_xaml_carries_no_forty_pixel_title_bar_literal()
    {
        string xaml = ReadAppSource("MainWindow.xaml");
        Assert.DoesNotContain("CaptionHeight=", xaml);   // WindowChrome yüksekliği XAML'de attribute literali DEĞİL
        Assert.DoesNotContain("Height=\"40\"", xaml);    // title-bar satırı da literal DEĞİL
    }

    [Fact]
    public void MainWindow_derives_the_title_bar_height_from_the_size_token()
    {
        string cs = ReadAppSource("MainWindow.xaml.cs");
        Assert.Contains("Size.TitleBarHeight", cs);   // tek kaynak token
        // [C1 review I-3] Tam türetim ifadesi pinlenir — yalnız "TitleRow.Height" aranırsa
        // "TitleRow.Height = new GridLength(40)" gibi bir hardcode de testi GEÇERDİ (vacuous pin).
        Assert.Contains("TitleRow.Height = new GridLength(titleBarHeight)", cs); // satır ondan sürülür, literal DEĞİL
        Assert.Contains("CaptionHeight = titleBarHeight", cs); // WindowChrome ondan sürülür
    }

    [StaFact]
    public void The_title_bar_height_token_is_forty()
        // [C1 review I-2] Aynı XamlReader.Load(Tokens.xaml) yükleme deseni DesignTokenScaleTests içinde de
        // ayrıca duruyordu (kopya YASAK, CLAUDE.md) — üçüncü bir kopya açmak yerine ortak DsResources.Load
        // yardımcısı (T60) kullanılır.
        => Assert.Equal(40.0, (double)DsResources.Load("Tokens.xaml")["Size.TitleBarHeight"]);
}
