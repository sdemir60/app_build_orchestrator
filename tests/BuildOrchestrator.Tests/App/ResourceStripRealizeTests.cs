using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Core.Diagnostics;
using Xunit.Abstractions;
using static BuildOrchestrator.Tests.ByteUnits;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kaynak şeridi · design v1.25.0 §2.1] Title bar'ın tam ortasındaki şeridin GERÇEK yerleşimi: pencerenin kabuğu üretimin tam kablajıyla kurulur
/// ve <b>içeriği</b> (<c>window.Content</c>) <c>Size.WindowMinWidth</c> genişliğinde ölçülür (CLAUDE.md "Realize testi": <c>Window.Measure/Arrange</c>
/// HWND'siz içeriğe inmez). Güncelleme hapı görünürken (en kalabalık title bar) ölçülür — sağ küme en uzun hâlindedir.
///
/// <para><b>Ölçülür, varsayılmaz; DPI-bağımsız:</b> <c>UseLayoutRounding</c> her kenarı cihaz pikseline oturtur; 125%'te 14 dip 14,4 olur. Bu
/// yüzden kenar boşlukları ve hizalar mutlak sayıyla değil <b>bir cihaz pikseli</b> toleransıyla iddia edilir (<see cref="OnePixel"/>); ilişkiler
/// (çakışma yok, eşit genişlik, merkez) ise toleranssız ya da en çok yarım piksel toleranslıdır. Pencere XAML'de doğuştan Maximized'dır ve
/// <c>MaximizeFix</c> çerçeve dolgusunu yazar; en dar pencereyi (Normal durum) ölçmek için durum önce Normal'e alınır.</para>
///
/// <para><b>Font notu:</b> gömülü Geist ve Geist Mono başsız süitte de çözülür (ölçüldü: <c>Typeface.TryGetGlyphTypeface</c> aile adları "Geist" ve
/// "Geist Mono"; 125%'te mono hane 6,4 dip). İddialar yine de mutlak font genişliğine değil ilişkiye dayanır (ghost en geniş değerdir, değer ghost'u
/// aşmaz, hücre genişliği değerden bağımsızdır) — font bir ortamda sistem yüzüne düşse de tutarlar.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class ResourceStripRealizeTests(ITestOutputHelper output)
{
    /// <summary>Güncelleme hapı görünür, pencere Normal durumda, içerik <paramref name="width"/> × 620'de ölçülü.</summary>
    private static (MainWindow window, RunViewModel vm, ResourceStrip strip) Realized(TempDir temp, double width = 1240)
    {
        var (window, vm) = MainWindowHost.NewRealizedWithOffer(temp);
        window.WindowState = WindowState.Normal; // XAML doğuştan Maximized: çerçeve dolgusu 0 → pencerenin gerçek en dar hâli
        MainWindowHost.Realize(window, width, 620);
        return (window, vm, window.ResourceStripView);
    }

    private static void Relayout(MainWindow window) => ((FrameworkElement)window.Content).UpdateLayout();

    /// <summary>Öğenin title bar (<c>TitleBarRow</c>) koordinatlarındaki kutusu.</summary>
    private static Rect Bounds(MainWindow window, FrameworkElement element) => DsResources.BoundsIn(element, window.TitleBarRow);

    /// <summary>Bir cihaz pikselinin dip karşılığı — yuvarlama bütçesi (her kenar en çok yarım piksel oynar, bir boşluk iki kenardan doğar).</summary>
    private static double OnePixel(MainWindow window) => 1.0 / VisualTreeHelper.GetDpi(window).DpiScaleX;

    private static Viewbox IconBox(Path icon) => DsResources.Ancestors(icon).OfType<Viewbox>().First();

    private static Color Token(MainWindow window, string key) => DsResources.TokenColor(window, key);

    // ---------------------------------------------------------------- hücreler, ikonlar, değerler

    [StaFact]
    public void The_strip_has_three_cells_in_order_each_with_its_icon_and_the_value_the_view_model_holds()
    {
        using var temp = new TempDir();
        var (window, vm, strip) = Realized(temp);
        vm.ApplyResourceSample(new ResourceSample(69, 310 * Mb, 85 * Mb, 4400 * Mb));
        Relayout(window);

        FrameworkElement[] cells = [strip.CpuCell, strip.MemoryCell, strip.ParallelCell];
        Assert.All(cells, c => Assert.True(c.ActualWidth > 0 && c.ActualHeight > 0, $"{c.Name} ölçülmemiş (vakum)"));
        var lefts = cells.Select(c => Bounds(window, c).Left).ToList();
        Assert.Equal(lefts.OrderBy(x => x), lefts); // soldan sağa CPU · RAM · paralellik

        Assert.Same(window.FindResource("Icon.Cpu"), strip.CpuIcon.Data);
        Assert.Same(window.FindResource("Icon.MemoryStick"), strip.MemoryIcon.Data);
        Assert.Same(window.FindResource("Icon.Parallel"), strip.ParallelIcon.Data);

        Assert.Equal("69%", strip.CpuValue.Text);
        Assert.Equal(ResourceStripText.Memory(4795 * Mb), strip.MemoryValue.Text);
        Assert.Equal(vm.StripCpuText, strip.CpuValue.Text);
        Assert.Equal(vm.StripMemoryText, strip.MemoryValue.Text);
        Assert.Equal(("0", "/4", "Balanced"), (strip.BuildingValue.Text, strip.AllowedValue.Text, strip.ModeValue.Text)); // boşta: 0/4 Balanced
        Assert.Equal((vm.StripBuildingText, vm.StripAllowedText, vm.StripModeText),
            (strip.BuildingValue.Text, strip.AllowedValue.Text, strip.ModeValue.Text));

        // Değerler VM'i canlı izler: çip dönünce paralellik hücresi, örnek gelince CPU/RAM aynı anda değişir.
        vm.SetPerfMode("Light");
        vm.ApplyResourceSample(new ResourceSample(null, null, null, null)); // okunamayan değer: aynı hücrede —
        Assert.Equal(("0", "/2", "Light"), (strip.BuildingValue.Text, strip.AllowedValue.Text, strip.ModeValue.Text));
        Assert.Equal((ResourceStripText.Unreadable, ResourceStripText.Unreadable), (strip.CpuValue.Text, strip.MemoryValue.Text));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_cells_have_no_label_only_an_icon_and_a_value_and_every_icon_is_drawn_faint_with_the_design_weight()
    {
        using var temp = new TempDir();
        var (window, _, strip) = Realized(temp);

        Path[] icons = [strip.CpuIcon, strip.MemoryIcon, strip.ParallelIcon];
        Assert.All(icons, p =>
        {
            Assert.Equal(Token(window, "Brush.TextFaint"), DsResources.ColorOf(p.Stroke));
            Assert.Null(p.Fill);
            Assert.Equal((PenLineCap.Round, PenLineCap.Round, PenLineJoin.Round), (p.StrokeStartLineCap, p.StrokeEndLineCap, p.StrokeLineJoin));
        });
        // Her ikon KENDİ sözlük kalınlığıyla çizilir; değerlerin tek pini IconGeometryTests'tedir (prototipin ikon başına kalınlığı).
        // [DEĞİŞEN KURAL — 2026-10-19] ESKİ: burada da üç ikonun 1.8 olduğu literal pinlenirdi; parallel 1.9'a çıkınca aynı sayının
        // ikinci kopyası olmasın diye bağ yapısal doğrulanır.
        Assert.Equal(new[] { "Icon.Cpu", "Icon.MemoryStick", "Icon.Parallel" }.Select(key => (double)window.FindResource(key + ".StrokeThickness")),
            icons.Select(p => p.StrokeThickness));

        // Etiket yok: ağaçtaki GÖRÜNÜR metinler yalnız değerler (ghost'lar gizlidir).
        var shown = DsResources.ShownTexts(strip);
        Assert.Equal(new[] { strip.CpuValue.Text, strip.MemoryValue.Text, strip.BuildingValue.Text, strip.AllowedValue.Text, strip.ModeValue.Text },
            shown);
        GC.KeepAlive(window);
    }

    /// <summary>[perf Faz A] Pencere tepsideyken kimsenin görmediği ekran işi yapılmaz: yüzey gizliyken VM'in şerit özellikleri (örnek, çip, pay)
    /// değişse de hücrelere ve tooltip'lere YAZILMAZ — bir metin yazımı ölçümü geçersizler ve koşu sürerken pay saniyede onlarca kez değişir.
    /// Görünür olunca şerit modele TEK geçişte yetişir. (Tüm gerçekleşmiş ağaç için aynı kuralı <c>HiddenSurfaceTests</c> pinler.)</summary>
    [StaFact]
    public void While_the_surface_is_hidden_the_strip_is_never_written_and_it_catches_up_in_one_pass_on_show()
    {
        using var temp = new TempDir();
        var (window, vm, strip) = Realized(temp);
        vm.ApplyResourceSample(new ResourceSample(12, 200 * Mb, 0, 0));
        Relayout(window);
        var before = (strip.CpuValue.Text, strip.MemoryValue.Text, strip.BuildingValue.Text, strip.AllowedValue.Text, strip.ModeValue.Text);
        var tooltipBefore = ((ToolTip)strip.CpuCell.ToolTip).Content;
        Assert.Equal(("12%", "200 MB", "0", "/4", "Balanced"), before); // ön-koşul: görünürken yazılır
        Assert.True(strip.IsMeasureValid && strip.BuildingValue.IsMeasureValid);

        window.SetSurfaceHidden(true);
        vm.ApplyResourceSample(new ResourceSample(97, 4400 * Mb, 85 * Mb, 0));
        vm.SetPerfMode("Light");
        vm.StripBuildingText = "2";

        Assert.Equal(before, (strip.CpuValue.Text, strip.MemoryValue.Text, strip.BuildingValue.Text, strip.AllowedValue.Text, strip.ModeValue.Text));
        Assert.Equal(tooltipBefore, ((ToolTip)strip.CpuCell.ToolTip).Content);
        Assert.All(DsResources.RealizedObjects(strip).OfType<UIElement>(), e => Assert.True(e.IsMeasureValid && e.IsArrangeValid, $"{e.GetType().Name} geçersizlendi"));
        Assert.NotEqual(before.Item1, vm.StripCpuText); // non-vacuous: model gerçekten ilerledi

        window.SetSurfaceHidden(false);

        Assert.Equal(
            (vm.StripCpuText, vm.StripMemoryText, vm.StripBuildingText, vm.StripAllowedText, vm.StripModeText),
            (strip.CpuValue.Text, strip.MemoryValue.Text, strip.BuildingValue.Text, strip.AllowedValue.Text, strip.ModeValue.Text));
        Assert.Equal(("97%", "2", "/2", "Light"), (strip.CpuValue.Text, strip.BuildingValue.Text, strip.AllowedValue.Text, strip.ModeValue.Text));
        Assert.Equal(vm.StripCpuTooltip, ((ToolTip)strip.CpuCell.ToolTip).Content);
        Assert.Equal(vm.StripParallelTooltip, ((ToolTip)strip.ParallelCell.ToolTip).Content);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- yer: tam orta, kimseyi oynatmaz, kimseyle çakışmaz

    [StaFact]
    public void The_strip_sits_at_the_exact_centre_of_the_title_bar_at_every_width()
    {
        using var temp = new TempDir();
        var (window, _, strip) = Realized(temp);
        var report = new List<string>();

        foreach (double width in new[] { 1240.0, 1400.0, 1920.0 })
        {
            MainWindowHost.Realize(window, width, 620);
            var bounds = Bounds(window, strip);
            Assert.True(bounds.Width > 0 && bounds.Width < 400, $"{width}: şerit ölçülmemiş ya da ortada tek parça değil ({bounds})");
            double offset = bounds.Left + bounds.Width / 2 - window.TitleBarRow.ActualWidth / 2;
            report.Add($"{width}: bar {window.TitleBarRow.ActualWidth:0.##}  şerit [{bounds.Left:0.##} .. {bounds.Right:0.##}] genişlik {bounds.Width:0.##}  merkez farkı {offset:0.###}");
            Assert.True(Math.Abs(offset) <= 0.5 + 1e-9, $"{width}: şeridin ortası bar ortasından {offset:0.###} dip kaymış (≤ 0,5 olmalı)");
        }

        output.WriteLine(string.Join(Environment.NewLine, report));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_cells_are_vertically_centred_in_the_title_bar()
    {
        using var temp = new TempDir();
        var (window, _, strip) = Realized(temp);

        var inner = MainWindowHost.TitleBarGrid(window); // hairline'dan sonra kalan iç kutu
        double barCenter = inner.ActualHeight / 2;
        foreach (FrameworkElement cell in new FrameworkElement[] { strip.CpuCell, strip.MemoryCell, strip.ParallelCell })
        {
            var bounds = DsResources.BoundsIn(cell, inner);
            Assert.True(bounds.Height > 0 && bounds.Height < inner.ActualHeight - 8, // bandı DOLDURAN bir hücre "ortalı" sayılmaz
                $"{cell.Name}: yüksekliği {bounds.Height} (bant {inner.ActualHeight}) — hücre kendi içeriği kadar olmalı");
            Assert.True(Math.Abs(bounds.Top + bounds.Height / 2 - barCenter) <= 1.0,
                $"{cell.Name}: dikey ortadan {bounds.Top + bounds.Height / 2 - barCenter:0.###} dip kaymış");
        }
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_update_pill_appearing_or_leaving_does_not_move_the_strip()
    {
        using var temp = new TempDir();
        var (window, _, strip) = Realized(temp);
        Assert.Equal(Visibility.Visible, window.UpdatePillSlot.Visibility); // ön-koşul: hap görünür
        var withPill = Bounds(window, strip);

        window.UpdatePillSlot.Visibility = Visibility.Collapsed;
        Relayout(window);

        Assert.True(withPill.Width > 0 && withPill.Width < 400, $"şerit ölçülmemiş ya da ortada tek parça değil ({withPill})");
        Assert.Equal(withPill, Bounds(window, strip));
        GC.KeepAlive(window);
    }

    /// <summary>En dar pencerede (1240) ve güncelleme hapı görünürken şerit ne sol logo kilidine, ne sağ kümeye (hap dahil), ne caption butonlarına
    /// değer. Boşluklar ölçülüp çıktıya yazılır (controller'ın göz kontrolü ve rapor için).</summary>
    [StaFact]
    public void At_the_narrowest_width_with_the_update_pill_showing_the_strip_touches_neither_cluster()
    {
        using var temp = new TempDir();
        var (window, _, strip) = Realized(temp);

        var stripBounds = Bounds(window, strip);
        var logoLock = Bounds(window, window.TitleBarLogoLock);
        var rightCluster = Bounds(window, (FrameworkElement)LogicalTreeHelper.GetParent(window.GearButton));
        var captionButtons = Bounds(window, (FrameworkElement)LogicalTreeHelper.GetParent(window.MinButton));
        var pill = Bounds(window, window.UpdatePill);

        Assert.True(window.UpdatePill.ActualWidth > 0, "hap hiç yer kaplamadı — ölçüm vakum");
        Assert.True(stripBounds.Width > 0 && logoLock.Width > 0 && rightCluster.Width > 0 && captionButtons.Width > 0);
        Assert.Equal(1240.0, window.TitleBarRow.ActualWidth); // pencere gerçekten en dar hâlinde (çerçeve dolgusu yok)
        output.WriteLine($"şerit      [{stripBounds.Left:0.##} .. {stripBounds.Right:0.##}] genişlik {stripBounds.Width:0.##} · merkez {stripBounds.Left + stripBounds.Width / 2:0.##} / bar merkezi {window.TitleBarRow.ActualWidth / 2:0.##}");
        output.WriteLine($"logo kilidi [{logoLock.Left:0.##} .. {logoLock.Right:0.##}]  → şerit solu ile boşluk {stripBounds.Left - logoLock.Right:0.##}");
        output.WriteLine($"sağ küme   [{rightCluster.Left:0.##} .. {rightCluster.Right:0.##}] (hap [{pill.Left:0.##} .. {pill.Right:0.##}])  → şerit sağı ile boşluk {rightCluster.Left - stripBounds.Right:0.##}");
        output.WriteLine($"caption    [{captionButtons.Left:0.##} .. {captionButtons.Right:0.##}]");

        Assert.False(stripBounds.IntersectsWith(logoLock), $"şerit {stripBounds} logo kilidiyle {logoLock} çakışıyor");
        Assert.False(stripBounds.IntersectsWith(rightCluster), $"şerit {stripBounds} sağ kümeyle {rightCluster} çakışıyor");
        Assert.False(stripBounds.IntersectsWith(captionButtons), $"şerit {stripBounds} caption butonlarıyla {captionButtons} çakışıyor");
        Assert.True(stripBounds.Left > logoLock.Right && stripBounds.Right < rightCluster.Left, "şerit iki kümenin ARASINDA değil");
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_strip_is_the_last_child_of_the_title_bar_grid_so_it_draws_over_the_dock_and_stays_centred_in_the_whole_cell()
    {
        using var temp = new TempDir();
        var (window, _, strip) = Realized(temp);

        var grid = MainWindowHost.TitleBarGrid(window);
        Assert.Same(grid, VisualTreeHelper.GetParent(strip));
        Assert.Equal(HorizontalAlignment.Center, strip.HorizontalAlignment);
        Assert.Equal(grid.Children.Count - 1, grid.Children.IndexOf(strip)); // son çocuk = en üstte çizilir
        var dock = MainWindowHost.TitleBarDock(window);                       // kümeler hâlâ dock'ta, şerit onları oynatmaz
        Assert.True(dock.Children.Count > 0);
        Assert.Equal(Panel.GetZIndex(dock), Panel.GetZIndex(strip));         // z-sırası yalnız çocuk sırasından gelir
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- sabit çerçeve

    /// <summary>Her hücre en geniş değer kadar yer ayırır (<c>100%</c> · <c>12.3 GB</c> · <c>12/12 Balanced</c>): değer <c>1% → 100%</c>,
    /// <c>310 MB → 12.3 GB</c>, <c>0/2 Light → 12/12 Balanced</c> olurken hiçbir hücrenin genişliği, şeridin kutusu ve değerin başlangıç x'i
    /// değişmez; değer ikonun yanında SOLA hizalıdır.</summary>
    [StaFact]
    public void Every_cell_keeps_its_width_and_the_value_its_left_edge_whatever_the_value_is()
    {
        using var temp = new TempDir();
        var (window, vm, strip) = Realized(temp);

        (double cpu, double memory, double parallel, Rect box, double cpuX, double memoryX, double buildingX) Snapshot() =>
            (strip.CpuCell.ActualWidth, strip.MemoryCell.ActualWidth, strip.ParallelCell.ActualWidth, Bounds(window, strip),
             DsResources.BoundsIn(strip.CpuValue, strip.CpuCell).Left, DsResources.BoundsIn(strip.MemoryValue, strip.MemoryCell).Left,
             DsResources.BoundsIn(strip.BuildingValue, strip.ParallelCell).Left);

        // En dar değerler: 1% · 310 MB · 0 /2 Light
        vm.SetPerfMode("Light");
        vm.ApplyResourceSample(new ResourceSample(1, 310 * Mb, 0, 0));
        Relayout(window);
        Assert.Equal(("1%", "310 MB", "0", "/2", "Light"),
            (strip.CpuValue.Text, strip.MemoryValue.Text, strip.BuildingValue.Text, strip.AllowedValue.Text, strip.ModeValue.Text));
        var narrow = Snapshot();
        double cpuLive = strip.CpuValue.ActualWidth, memoryLive = strip.MemoryValue.ActualWidth, modeRight = Bounds(window, strip.ModeValue).Right;

        // En geniş değerler: 100% · 12.3 GB · 12 /12 Balanced
        vm.ApplyResourceSample(new ResourceSample(100, 12595 * Mb, 0, 0));
        vm.StripBuildingText = "12";
        vm.StripAllowedText = "/12";
        vm.StripModeText = "Balanced";
        Relayout(window);
        Assert.Equal(("100%", "12.3 GB", "12", "/12", "Balanced"),
            (strip.CpuValue.Text, strip.MemoryValue.Text, strip.BuildingValue.Text, strip.AllowedValue.Text, strip.ModeValue.Text));
        var wide = Snapshot();

        output.WriteLine($"hücre genişlikleri (dar değerler): cpu {narrow.cpu:0.##} · ram {narrow.memory:0.##} · paralel {narrow.parallel:0.##} · şerit {narrow.box.Width:0.##}");
        output.WriteLine($"hücre genişlikleri (geniş değerler): cpu {wide.cpu:0.##} · ram {wide.memory:0.##} · paralel {wide.parallel:0.##} · şerit {wide.box.Width:0.##}");
        Assert.Equal(narrow, wide);

        // Non-vacuous: değerler gerçekten farklı genişlikte, hücre ghost kadar geniş (değerden fazla).
        Assert.True(strip.CpuValue.ActualWidth > cpuLive, "100% 1%'den geniş değil — ölçüm vakum");
        Assert.True(strip.MemoryValue.ActualWidth > memoryLive, "12.3 GB 310 MB'den geniş değil — ölçüm vakum");
        Assert.True(Bounds(window, strip.ModeValue).Right > modeRight, "mod sözcüğü sayıyı izlemiyor");
        Assert.True(narrow.cpu > IconBox(strip.CpuIcon).Width + cpuLive, "CPU hücresi ghost kadar yer ayırmıyor");

        Assert.Equal(HorizontalAlignment.Left, strip.CpuValue.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Left, strip.MemoryValue.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Left, strip.ParallelLive.HorizontalAlignment);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Every_cell_reserves_the_widest_value_with_hidden_ghost_texts_that_come_from_the_core_constants()
    {
        using var temp = new TempDir();
        var (window, _, strip) = Realized(temp);

        // Gizli (Hidden — Collapsed DEĞİL: alanı tutar) metinler, sırayla: CPU · RAM · paralellik sayısı · mod sözcüğü.
        var ghosts = DsResources.Descendants(strip).OfType<TextBlock>().Where(t => t.Visibility == Visibility.Hidden).ToList();
        Assert.Equal(
            new[] { ResourceStripText.CpuGhost, ResourceStripText.MemoryGhost, ResourceStripText.ParallelGhost, ResourceStripText.ModeGhost },
            ghosts.Select(t => t.Text));
        Assert.Equal(new[] { "100%", "12.3 GB", "12/12", "Balanced" }, ghosts.Select(t => t.Text)); // tasarımın sabit çerçeve sayıları
        Assert.All(ghosts, t => Assert.True(t.ActualWidth > 0, "ghost yer tutmuyor"));

        // Ghost'un yazı kimliği canlı değerinkiyle AYNI (yoksa ayırdığı yer yalan söyler).
        Assert.Equal(strip.CpuValue.FontFamily, ghosts[0].FontFamily);
        Assert.Equal(strip.CpuValue.FontSize, ghosts[0].FontSize);
        Assert.Equal(strip.MemoryValue.FontFamily, ghosts[1].FontFamily);
        Assert.Equal(strip.ModeValue.FontFamily, ghosts[3].FontFamily);
        Assert.Equal(strip.ModeValue.FontSize, ghosts[3].FontSize);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- caption: yalnız hücreler client, boşluklar ve bantlar yerel title bar'da

    [StaFact]
    public void The_strip_carries_the_chrome_flag_and_only_its_cells_have_a_hit_testable_background()
    {
        using var temp = new TempDir();
        var (window, _, strip) = Realized(temp);

        Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(strip));
        Assert.False(WindowChrome.GetIsHitTestVisibleInChrome(window.TitleBarLogoLock)); // logo kilidi caption bölgesi olarak kalır
        Assert.True(strip.IsHitTestVisible);
        // Şeridin KÖKÜNÜN zemini YOKTUR: zemin yalnız üç hücrededir (her biri kendi Transparent zeminini taşır) — gerekçe ve pin: aşağıdaki test.
        Assert.Null(strip.Background);
        Assert.All(new[] { strip.CpuCell, strip.MemoryCell, strip.ParallelCell }, cell => Assert.NotNull(cell.Background));
        GC.KeepAlive(window);
    }

    /// <summary>WindowChrome'un <c>WM_NCHITTEST</c> kararının aynısı: noktadaki en üst öğe <c>IsHitTestVisibleInChrome</c> taşıyorsa (kalıtsal) HER non-client
    /// sonuç — üst boyutlandırma bandı (HTTOP) dahil — CLIENT'a çevrilir; taşımıyorsa nokta caption/boyutlandırmada kalır. Başsız süitte
    /// <c>Window.InputHitTest</c> çalışmaz: onun süzgeci <c>IsVisible</c>'a bakar ve HWND'siz ağaçta hiçbir öğe görünür sayılmaz. Bu yüzden aynı geometri
    /// <see cref="VisualTreeHelper.HitTest(Visual, HitTestFilterCallback, HitTestResultCallback, HitTestParameters)"/> ile ve <c>InputHitTest</c>'in süzgeciyle
    /// (görünmeyen — <c>Hidden</c>/<c>Collapsed</c> — ve <c>IsHitTestVisible=false</c> öğeler atlanır) sorulur; nokta <c>TitleBarRow</c> koordinatındadır.
    /// <c>Hit</c>: noktadaki en üst görsel; <c>Client</c>: o öğe chrome'da client mı.</summary>
    private static (DependencyObject? Hit, bool Client) HitAt(MainWindow window, double x, double y)
    {
        DependencyObject? hit = null;
        VisualTreeHelper.HitTest(window.TitleBarRow,
            node => node is UIElement { Visibility: not Visibility.Visible } or UIElement { IsHitTestVisible: false }
                ? HitTestFilterBehavior.ContinueSkipSelfAndChildren
                : HitTestFilterBehavior.Continue,
            result =>
            {
                hit = result.VisualHit;
                return HitTestResultBehavior.Stop; // en üstteki
            },
            new PointHitTestParameters(new Point(x, y)));
        var element = hit is null ? null : DsResources.SelfAndAncestors(hit).OfType<UIElement>().FirstOrDefault();
        return (hit, element is not null && WindowChrome.GetIsHitTestVisibleInChrome(element));
    }

    /// <summary>
    /// <b>Tıklama alanı yalnız hücrelerdir</b> — kuralın nedeni <c>Views/ResourceStrip.xaml</c> başlığında (tek otorite). Pin: hücrelerin
    /// üstündeki ve altındaki bantta, üst boyutlandırma bandında ve 14px'lik boşluklarda en üst öğe chrome'da client DEĞİLDİR (caption/boyutlandırma
    /// yerel kalır); bir hücrenin üstünde — glif olmayan noktalar dahil (ikon→değer boşluğu, hayalet alanı) — en üst öğe hücrenin kendisi/torunudur ve
    /// client'tır (hücre üstündeki basışı pencere iletir: R6).
    /// </summary>
    [StaFact]
    public void Only_the_cells_are_chrome_client_while_the_gaps_and_the_bands_around_them_stay_with_the_native_title_bar()
    {
        using var temp = new TempDir();
        var (window, _, strip) = Realized(temp);
        var chrome = WindowChrome.GetWindowChrome(window);

        StackPanel[] cells = [strip.CpuCell, strip.MemoryCell, strip.ParallelCell];
        var boxes = cells.Select(cell => Bounds(window, cell)).ToList();
        double midY = boxes[0].Top + boxes[0].Height / 2;
        Assert.All(boxes, box => Assert.True(box.Width > 0 && box.Height > 0, "hücre ölçülmemiş (vakum)"));

        // (0) Şeridin kendi kutusu hücreler kadar yüksektir (MainWindow.xaml: VerticalAlignment=Center), bütün bandı kaplamaz: kökün zemini olmadığı için
        // hit-test'e etkisi yoktur, ama UIA sınırı ("Resource usage" grubu) ve şeride sonradan eklenecek her zemin/efekt hücrelerin dışına taşmaz.
        var stripBox = Bounds(window, strip);
        Assert.True(stripBox.Height <= boxes.Max(box => box.Height) + OnePixel(window),
            $"şeridin kutusu hücrelerden yüksek: şerit {stripBox.Height:0.##}, hücre {boxes.Max(box => box.Height):0.##} (bant {window.TitleBarRow.ActualHeight:0.##})");

        // (1) Hücreler client: merkez; sağ uç (hayalet alanı — Hidden hayalet hit-test edilmez, yalnız hücrenin kendi zemini tutar); ikon→değer boşluğu.
        for (int i = 0; i < cells.Length; i++)
        {
            var (centerHit, centerClient) = HitAt(window, boxes[i].Left + boxes[i].Width / 2, midY);
            Assert.True(centerClient && centerHit is not null && DsResources.IsSelfOrDescendantOf(centerHit, cells[i]),
                $"{cells[i].Name} merkezi client değil ya da hücrenin altında değil ({centerHit})");

            var (edgeHit, edgeClient) = HitAt(window, boxes[i].Right - 1, midY);
            Assert.True(edgeClient && ReferenceEquals(edgeHit, cells[i]), $"{cells[i].Name} sağ ucu hücrenin kendi zeminini vurmuyor ({edgeHit})");
        }
        var iconBox = Bounds(window, IconBox(strip.CpuIcon));
        var valueArea = Bounds(window, (FrameworkElement)strip.CpuValue.Parent);
        var (gapHit, gapClient) = HitAt(window, (iconBox.Right + valueArea.Left) / 2, midY);
        Assert.True(gapClient && ReferenceEquals(gapHit, strip.CpuCell), $"ikon→değer boşluğu hücrenin kendi zeminini vurmuyor ({gapHit})");

        // (2) Hücre arası 14px'ler: caption'da. (Kutu şeridin ÇEVRESİ içinde kalır ama hiçbir şeye vurmaz.)
        for (int i = 0; i + 1 < boxes.Count; i++)
        {
            double x = (boxes[i].Right + boxes[i + 1].Left) / 2;
            var (hit, client) = HitAt(window, x, midY);
            Assert.False(client, $"{cells[i].Name} ile {cells[i + 1].Name} arasındaki boşluk (x={x:0.##}) client: {hit}");
            Assert.False(hit is not null && DsResources.IsSelfOrDescendantOf(hit, strip), "boşluk şeridin bir öğesini vuruyor");
        }

        // (3) Hücrelerin üstündeki ve altındaki bantlar ve 6px'lik üst boyutlandırma bandı: yerel title bar'da.
        double resizeBand = chrome.ResizeBorderThickness.Top;
        Assert.Equal(6.0, resizeBand);
        foreach (var box in boxes)
        {
            double x = box.Left + box.Width / 2;
            Assert.True(box.Top - 3 > resizeBand && box.Bottom + 3 < chrome.CaptionHeight, "hücre bandın ortasında değil — ölçüm vakum");
            foreach (var (name, y) in new[] { ("üst kenar boyutlandırma bandı", resizeBand / 2), ("hücrenin üstü", box.Top - 3), ("hücrenin altı", box.Bottom + 3) })
            {
                var (hit, client) = HitAt(window, x, y);
                Assert.False(client, $"{name} (x={x:0.##}, y={y:0.##}) client: {hit}");
            }
        }
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- tooltip: aşağı açılır, metin VM'den

    [StaFact]
    public void The_three_cells_carry_a_tooltip_that_opens_below_and_reads_the_view_model()
    {
        using var temp = new TempDir();
        var (window, vm, strip) = Realized(temp);

        (FrameworkElement Cell, Func<string> Expected)[] cells =
        [
            (strip.CpuCell, () => vm.StripCpuTooltip),
            (strip.MemoryCell, () => vm.StripMemoryTooltip),
            (strip.ParallelCell, () => vm.StripParallelTooltip),
        ];

        void AssertTooltips()
        {
            foreach (var (cell, expected) in cells)
            {
                var tip = Assert.IsType<ToolTip>(cell.ToolTip);
                Assert.Equal(AppTooltip.Bottom, AppTooltip.GetSide(tip));
                Assert.False(string.IsNullOrWhiteSpace(expected()), $"{cell.Name}: VM tooltip'i boş");
                Assert.Equal(expected(), tip.Content);
            }
        }

        AssertTooltips();
        Assert.Equal($"CPU used by {AppIdentity.Product} — app, engine and compilers · cap 70%", vm.StripCpuTooltip);
        Assert.Equal("Balanced — up to 4 projects build in parallel", vm.StripParallelTooltip);

        // Tooltip VM'i CANLI izler (açıkken de yenilenir): örnek gelince RAM, çip dönünce paralellik + CPU cap'i.
        vm.ApplyResourceSample(new ResourceSample(52, 310 * Mb, 85 * Mb, 4400 * Mb));
        vm.SetPerfMode("Light");
        AssertTooltips();
        Assert.Equal($"Memory used by {AppIdentity.Product} · app 310 MB · engine 85 MB · compilers {ResourceStripText.Memory(4400 * Mb)}", vm.StripMemoryTooltip);
        Assert.Equal($"CPU used by {AppIdentity.Product} — app, engine and compilers · cap 40%", vm.StripCpuTooltip);
        Assert.Equal("Light — up to 2 projects build in parallel", vm.StripParallelTooltip);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- görünüm: token'dan renk, font ve ölçü

    [StaFact]
    public void Colours_fonts_and_sizes_come_from_the_tokens_values_dim_and_everything_else_faint()
    {
        using var temp = new TempDir();
        var (window, _, strip) = Realized(temp);
        var dim = Token(window, "Brush.TextDim");
        var faint = Token(window, "Brush.TextFaint");
        double size = (double)window.FindResource("FontSize.2xs");
        Assert.Equal(11.0, size); // Geist Mono 11px (FontSize.2xs)

        // Değerler: Geist Mono · 11px · tabular · text-dim.
        TextBlock[] values = [strip.CpuValue, strip.MemoryValue, strip.BuildingValue];
        Assert.All(values, v =>
        {
            Assert.Equal(AppFonts.Mono, v.FontFamily);
            Assert.Equal(size, v.FontSize);
            Assert.Equal(FontWeights.Normal, v.FontWeight);
            Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(v));
            Assert.Equal(dim, DsResources.ColorOf(v.Foreground));
        });

        // "/N": aynı mono yüz, bir ton soluk (text-faint) — renk yok, bilgi.
        Assert.Equal(AppFonts.Mono, strip.AllowedValue.FontFamily);
        Assert.Equal(size, strip.AllowedValue.FontSize);
        Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(strip.AllowedValue));
        Assert.Equal(faint, DsResources.ColorOf(strip.AllowedValue.Foreground));

        // Mod sözcüğü: Geist (pencere arayüz yazı tipini vermez, bu yüzden açıkça) · 11px · text-faint.
        Assert.Equal(AppFonts.Ui, strip.ModeValue.FontFamily);
        Assert.Equal(size, strip.ModeValue.FontSize);
        Assert.Equal(faint, DsResources.ColorOf(strip.ModeValue.Foreground));

        // Şeritte text-dim ve text-faint dışında hiçbir renk yok: amber/statü rengi yok, hover zemini yok.
        var colours = DsResources.RealizedObjects(strip).OfType<TextBlock>().Select(t => DsResources.ColorOf(t.Foreground))
            .Concat(DsResources.RealizedObjects(strip).OfType<Path>().Select(p => DsResources.ColorOf(p.Stroke))).Distinct().ToList();
        Assert.Equal(new HashSet<Color> { dim, faint }, colours.ToHashSet());
        Assert.Empty(DsResources.DynamicResourceTypeMismatches(strip));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_design_measures_hold_twelve_pixel_icons_fourteen_between_cells_five_after_each_icon_and_before_the_mode()
    {
        using var temp = new TempDir();
        var (window, _, strip) = Realized(temp);
        double px = OnePixel(window);

        Viewbox[] boxes = [IconBox(strip.CpuIcon), IconBox(strip.MemoryIcon), IconBox(strip.ParallelIcon)];
        Assert.All(boxes, b => Assert.Equal((12.0, 12.0), (b.Width, b.Height)));
        Assert.All(boxes, b => Assert.True(Math.Abs(b.ActualWidth - 12.0) <= px && Math.Abs(b.ActualHeight - 12.0) <= px, $"ikon kutusu {b.ActualWidth}×{b.ActualHeight}"));

        // Tasarım sayıları KENDİLERİ (yuvarlamasız): hücre arası 14, ikon→değer 5, mod sözcüğünün önü 5.
        Assert.Equal(new Thickness(14, 0, 0, 0), strip.MemoryCell.Margin);
        Assert.Equal(new Thickness(14, 0, 0, 0), strip.ParallelCell.Margin);
        Assert.Equal(new Thickness(5, 0, 0, 0), ((FrameworkElement)strip.CpuValue.Parent).Margin);
        Assert.Equal(new Thickness(5, 0, 0, 0), ((FrameworkElement)strip.MemoryValue.Parent).Margin);
        Assert.Equal(new Thickness(5, 0, 0, 0), ((FrameworkElement)strip.ParallelLive.Parent).Margin);
        Assert.Equal(new Thickness(5, 0, 0, 0), strip.ModeValue.Margin);

        // Ölçülen boşluklar: UseLayoutRounding her kenarı cihaz pikseline oturtur (125%'te 14 dip = 17,5 px → 18 px) ve bir boşluk iki kenardan doğar;
        // bütçe 2 cihaz pikselidir — 12/16 gibi yanlış bir ölçü bunu aşar (yukarıdaki tam Margin iddiaları zaten yakalar).
        double budget = 2 * px;
        void AssertGap(double expected, double actual, string what) =>
            Assert.True(Math.Abs(actual - expected) <= budget + 1e-9, $"{what}: {actual:0.###} dip (beklenen {expected} ± {budget:0.###})");

        // hücre arası 14
        AssertGap(14, Bounds(window, strip.MemoryCell).Left - Bounds(window, strip.CpuCell).Right, "CPU → RAM hücresi");
        AssertGap(14, Bounds(window, strip.ParallelCell).Left - Bounds(window, strip.MemoryCell).Right, "RAM → paralellik hücresi");
        // ikon → değer 5
        AssertGap(5, Bounds(window, strip.CpuValue).Left - Bounds(window, boxes[0]).Right, "CPU ikonu → değer");
        AssertGap(5, Bounds(window, strip.MemoryValue).Left - Bounds(window, boxes[1]).Right, "RAM ikonu → değer");
        AssertGap(5, Bounds(window, strip.BuildingValue).Left - Bounds(window, boxes[2]).Right, "paralellik ikonu → pay");
        // pay + "/N" bitişik, mod sözcüğü 5 sonra
        AssertGap(0, Bounds(window, strip.AllowedValue).Left - Bounds(window, strip.BuildingValue).Right, "pay → /N");
        AssertGap(5, Bounds(window, strip.ModeValue).Left - Bounds(window, strip.AllowedValue).Right, "/N → mod sözcüğü");
        // ikon ve değer aynı dikey ortada
        Assert.True(Math.Abs(Bounds(window, strip.CpuValue).Top + strip.CpuValue.ActualHeight / 2 - (Bounds(window, boxes[0]).Top + 6)) <= 1.0, "CPU değeri ikonla aynı hizada değil");
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- yalnız okur: animasyon, tıklama, imleç, odak yok

    [StaFact]
    public void Value_changes_play_no_animation_and_the_strip_never_reacts_to_the_pointer_or_takes_focus()
    {
        using var temp = new TempDir();
        var (window, vm, strip) = Realized(temp);

        vm.ApplyResourceSample(new ResourceSample(7, 123 * Mb, 0, 0));
        vm.SetPerfMode("Full");
        Relayout(window);
        var all = DsResources.RealizedObjects(strip).ToList();

        Assert.All(all.OfType<UIElement>(), e => Assert.False(e.HasAnimatedProperties, $"{e.GetType().Name} animasyonlu"));
        Assert.All(all.OfType<UIElement>(), e => Assert.Equal(1.0, e.Opacity));
        Assert.All(all.OfType<FrameworkElement>(), e => Assert.Null(e.Cursor)); // imleç değişmez
        Assert.Empty(all.OfType<ButtonBase>());
        Assert.Empty(all.OfType<TextBoxBase>());
        Assert.False(strip.Focusable);
        Assert.False(strip.IsTabStop);
        Assert.All(all.OfType<UIElement>(), e => Assert.False(e.Focusable, $"{e.GetType().Name} odak alabiliyor"));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_strip_announces_itself_as_a_named_group_to_screen_readers()
    {
        using var temp = new TempDir();
        var (window, _, strip) = Realized(temp);

        var peer = UIElementAutomationPeer.CreatePeerForElement(strip);
        Assert.NotNull(peer);
        Assert.Equal(AutomationControlType.Group, peer!.GetAutomationControlType());
        Assert.Equal(AccessibilityNames.ResourceUsage, peer.GetName());
        Assert.Equal("Resource usage", AutomationProperties.GetName(strip));
        GC.KeepAlive(window);
    }
}
