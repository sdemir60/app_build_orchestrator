using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [v1.18.0 §9 "Konsol başlığı ve `Back` satırı"] README'nin "WPF tarafında sık sapan noktalar" kontrol
/// listesini BİREBİR pinler: Back'in DS Ghost.Sm kabuğu (24px + Icon.Back + margin-left -6), statü glyph'inin
/// ÇİZİLDİĞİ (building'de dönen spinner), dependency-issue/cycle rozetlerinin amber renk + tam-liste tooltip'i,
/// öğe aralığının 8px olduğu, panel daralınca KIRPILAN TEK öğenin proje adı olduğu ve Copy log'un DS
/// IconButton kabuğuna taşındığı. <see cref="ConsoleModesTests"/> mod geçişini ve içerik doldurmayı test eder;
/// burası GERÇEKTEN realize edilmiş (ekran dışı pencere + ApplyTemplate) stil/ölçü/renk iddialarını taşır —
/// bir Style/DynamicResource'un XAML'de YAZILI olması onun doğru ÇÖZÜLDÜĞÜNÜ kanıtlamaz.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ConsoleHeaderDesignTests
{
    /// <summary>[önce parent, sonra içerik] Üretimde <c>ShowProjectLog</c> her zaman zaten pencereye eklenmiş
    /// bir başlıkta çağrılır (Application kaynakları erişilebilir). Sırayı TERSİNE çevirip <paramref name="arrange"/>'i
    /// (ShowProjectLog) host'a bağlanmadan ÖNCE çağırmak, amber rozet ikonlarının boyanmasını (ctor'da DEĞİL,
    /// her çağrıda taze çözülür — bkz. ConsoleHeader.xaml.cs) headless host'ta erkenden çözümsüz bırakırdı;
    /// bu yardımcı üretimin gerçek sırasını izler.</summary>
    private static (ConsoleHeader header, Window window, Border host) Realize(
        Action<ConsoleHeader> arrange, double width = 400, double height = 60)
    {
        var header = new ConsoleHeader();
        var host = DsResources.NewHost();
        var window = DsResources.Realize(host, header, width, height);
        arrange(header);
        header.UpdateLayout(); // içerik değişti (Visibility/Text) — ölçüm/yerleşim tazelenir
        return (header, window, host);
    }

    private static ConsoleHeader ShowProjectLog(ConsoleHeader header, string name = "OSYS.Sales.Core",
        ProjectRowState state = ProjectRowState.Succeeded, bool inCycle = false,
        IReadOnlyList<string>? depIssues = null, string namePrefix = "OSYS.", int lineCount = 42)
    {
        header.ShowProjectLog(name, state, inCycle, depIssues, namePrefix, lineCount);
        return header;
    }

    // ---------------------------------------------------------------- Back: DS Ghost.Sm + ikon + -6 marj

    [StaFact]
    public void Back_uses_the_ghost_sm_shell_at_24px_with_the_drawn_arrow_icon()
    {
        var (header, window, host) = Realize(h => ShowProjectLog(h));

        Assert.Same(host.FindResource("Ds.Button.Ghost.Sm"), header.BackButton.Style);
        Assert.Equal(24.0, header.BackButton.Height);
        Assert.Equal(new Thickness(-6, 0, 0, 0), header.BackButton.Margin);

        var icon = Assert.Single(DsResources.Descendants(header.BackButton).OfType<Path>());
        Assert.Same(host.FindResource("Icon.Back"), icon.Data);

        var label = Assert.Single(DsResources.Descendants(header.BackButton).OfType<TextBlock>());
        Assert.Equal("Back", label.Text);

        Assert.Equal(AccessibilityNames.BackButton, AutomationProperties.GetName(header.BackButton));
        GC.KeepAlive(window);
    }

    /// <summary>Eski sapma: Back ikonsuz bir metin butonuydu (<c>Style="{x:Null}"</c>, <c>Content="← Back"</c>
    /// Unicode karakteri) — dolayısıyla renk sabitti ve hover'da DEĞİŞMİYORDU. Şimdi ikon, DS ghost butonunun
    /// <b>animasyonlu</b> Foreground'unu izler (currentColor): ctor'da <see cref="IconVisual.BoundToForeground"/>
    /// ile kurulan bağ ikonun <c>Stroke</c>'unu butonun gerçek <c>Foreground</c>'una bağlar.</summary>
    [StaFact]
    public void Back_icon_stroke_is_bound_to_the_buttons_own_foreground_not_a_fixed_brush()
    {
        var (header, window, _) = Realize(h => ShowProjectLog(h));

        var icon = Assert.Single(DsResources.Descendants(header.BackButton).OfType<Path>());
        var expression = icon.GetBindingExpression(Shape.StrokeProperty);
        Assert.NotNull(expression);
        Assert.Same(header.BackButton, expression!.ResolvedSource);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- statü glyph'i ÇİZİLİR

    /// <summary>[README §9] "building iken dönen amber arc (spinner)". Eski sapma: statü glyph'i düz Unicode
    /// bir <c>TextBlock</c>'tu (<c>▸</c>), hiçbir şey dönmüyordu.</summary>
    [StaFact]
    public void Building_status_shows_the_drawn_spinner_not_a_static_glyph()
    {
        var (header, window, _) = Realize(h => ShowProjectLog(h, state: ProjectRowState.Started));

        Assert.Equal(GraphStatus.Building, header.StatusGlyphIcon.Status);
        Assert.Equal(13.0, header.StatusGlyphIcon.Size);
        var spinner = Assert.Single(DsResources.Descendants(header.StatusGlyphIcon).OfType<BuildingSpinner>());
        Assert.Equal(Visibility.Visible, spinner.Visibility);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_settled_status_hides_the_spinner_and_shows_the_status_ring_instead()
    {
        var (header, window, _) = Realize(h => ShowProjectLog(h, state: ProjectRowState.Succeeded));

        Assert.Equal(GraphStatus.Succeeded, header.StatusGlyphIcon.Status);
        var spinner = Assert.Single(DsResources.Descendants(header.StatusGlyphIcon).OfType<BuildingSpinner>());
        Assert.Equal(Visibility.Collapsed, spinner.Visibility);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- dependency-issue / cycle rozetleri

    /// <summary>Eski sapma: rozet KIRMIZIydı (<c>Icon.DepWarn</c> dolu üçgen + <c>Brush.StatusFailText</c>) —
    /// tasarım amber ister (<c>Icon.AlertTri</c> kontur üçgeni + <c>Brush.AmberText</c>), satırdaki uyarı
    /// üçgeniyle AYNI dil.</summary>
    [StaFact]
    public void Dependency_issue_badge_is_amber_not_red_and_uses_the_outline_triangle()
    {
        var (header, window, host) = Realize(h => ShowProjectLog(h,
            depIssues: ["OSYS.Sales.Data"], namePrefix: "OSYS."));

        Assert.Equal(Visibility.Visible, header.DepIssueBadge.Visibility);

        var icon = Assert.Single(DsResources.Descendants(header.DepIssueBadge).OfType<Path>());
        Assert.Same(host.FindResource("Icon.AlertTri"), icon.Data);
        var amber = DsResources.TokenColor(host, "Brush.AmberText");
        Assert.Equal(amber, DsResources.ColorOf(icon.Stroke));

        var text = Assert.Single(DsResources.Descendants(header.DepIssueBadge).OfType<TextBlock>());
        Assert.Equal("dependency issue", text.Text);
        Assert.Equal(amber, DsResources.ColorOf(text.Foreground));

        Assert.Equal("Dependency issue: Sales.Data — last successful output referenced", header.DepIssueBadge.ToolTip);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Cycle_badge_is_amber_with_the_outline_triangle_and_the_shared_row_warning_text()
    {
        var (header, window, host) = Realize(h => ShowProjectLog(h, inCycle: true));

        Assert.Equal(Visibility.Visible, header.CycleBadge.Visibility);

        var icon = Assert.Single(DsResources.Descendants(header.CycleBadge).OfType<Path>());
        Assert.Same(host.FindResource("Icon.AlertTri"), icon.Data);
        Assert.Equal(DsResources.TokenColor(host, "Brush.AmberText"), DsResources.ColorOf(icon.Stroke));

        var text = Assert.Single(DsResources.Descendants(header.CycleBadge).OfType<TextBlock>());
        Assert.Equal("dependency cycle", text.Text);

        Assert.Equal(RowWarning.InCycle, header.CycleBadge.ToolTip); // "In a dependency cycle" — satırla AYNI
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- öğe aralığı 8px

    /// <summary>Eski sapma: aralıklar 5/10 karışıktı (README §9: 8px sabit).</summary>
    [StaFact]
    public void Project_log_items_are_spaced_eight_pixels_apart()
    {
        var header = new ConsoleHeader();
        ShowProjectLog(header);

        Assert.Equal(8.0, header.ProjectNameText.Margin.Left);
        Assert.Equal(8.0, header.StatusGlyphIcon.Margin.Left);
        Assert.Equal(8.0, header.StatusNameText.Margin.Left);
        Assert.Equal(8.0, header.DepIssueBadge.Margin.Left);
        Assert.Equal(8.0, header.CycleBadge.Margin.Left);
        Assert.Equal(8.0, header.LinesText.Margin.Left);
    }

    // ---------------------------------------------------------------- Copy log → DS IconButton

    /// <summary>Eski sapma: Copy log özel bir kabuktu (<c>Style="{x:Null}"</c>, elle Background/BorderThickness/
    /// Padding) — tasarım DS <c>IconButton size="sm"</c> ister (bu uygulamada <c>Ds.IconButton</c> zaten 22×22
    /// sm ölçüsüdür). Başarılı-kopya görseli (1.4s yeşil ✓) KORUNUR — bu test yalnız kabuğu pinler.</summary>
    [StaFact]
    public void Copy_log_uses_the_ds_icon_button_shell()
    {
        var (header, window, host) = Realize(h => ShowProjectLog(h, lineCount: 5));

        Assert.Same(host.FindResource("Ds.IconButton"), header.CopyLogButton.Style);
        Assert.Equal(22.0, header.CopyLogButton.Width);
        Assert.Equal(22.0, header.CopyLogButton.Height);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- dar genişlik: yalnız proje adı kırpılır

    /// <summary>[README §9] "panel daralınca kısalan TEK öğe proje adıdır (Back ve sağdaki sayaç asla
    /// kırpılmaz)". Eski sapma: sol grup <c>StackPanel</c> olduğu için proje adı HİÇ kırpılmıyordu (panel
    /// taşardı) — artık proje adı TEK "*" sütunudur, Back/glyph/statü/rozetler ve sağ blok Auto'dur.</summary>
    [StaFact]
    public void A_narrow_header_ellipsises_only_the_project_name_back_and_lines_stay_intact()
    {
        // İki AYRI gerçekleştirme (dar/geniş) karşılaştırılır — DesiredSize (Margin DAHİL) ile ActualWidth
        // (Margin HARİÇ) aynı ağaçta karşılaştırmak Back'in -6px marjıyla yanlış pozitif/negatif üretirdi;
        // ActualWidth'i ActualWidth'e karşı ölçmek bu tuzağı atlar.
        string longName = "OSYS." + new string('P', 60) + ".WorkOrder";
        var (narrow, narrowWindow, _) = Realize(h => ShowProjectLog(h, name: longName, lineCount: 128), width: 260);
        var (wide, wideWindow, _) = Realize(h => ShowProjectLog(h, name: longName, lineCount: 128), width: 600);

        Assert.Equal(TextTrimming.CharacterEllipsis, narrow.ProjectNameText.TextTrimming);
        Assert.True(narrow.ProjectNameText.ActualWidth < wide.ProjectNameText.ActualWidth,
            $"proje adı hiç kırpılmadı: dar panelde {narrow.ProjectNameText.ActualWidth}px, "
            + $"geniş panelde {wide.ProjectNameText.ActualWidth}px");

        Assert.Equal(wide.BackButton.ActualWidth, narrow.BackButton.ActualWidth, 1); // Back KIRPILMAZ/küçülmez
        Assert.Equal(wide.LinesText.ActualWidth, narrow.LinesText.ActualWidth, 1);   // N lines de KIRPILMAZ
        GC.KeepAlive(narrowWindow);
        GC.KeepAlive(wideWindow);
    }
}
