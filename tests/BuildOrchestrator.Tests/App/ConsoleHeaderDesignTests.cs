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
    /// bir başlıkta çağrılır. Sırayı TERSİNE çevirip <paramref name="arrange"/>'i (ShowProjectLog) host'a
    /// bağlanmadan ÖNCE çağırmak <see cref="ConsoleHeader.ApplyProjectNameShrink"/>'in okuduğu kardeş
    /// <c>ActualWidth</c>'leri (Back/glyph/statü/rozetler) sıfır bırakırdı — bunlar ancak GERÇEK bir
    /// Measure/Arrange geçişinden (yani host'a bağlandıktan) SONRA anlamlı olur. Amber rozet ikonlarının
    /// geometrisi/rengi kendisi bu sıraya duyarlı DEĞİLDİR (ConsoleHeader.xaml'de düz <c>{DynamicResource}</c>
    /// — Collapsed dalda bile, ağaca girer girmez çözülür); bu yardımcı yine de üretimin gerçek sırasını
    /// izler, çünkü ölçü iddiaları (bu dosyanın "dar genişlik" testi) doğru kardeş genişlikleri ister.</summary>
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
        header.ShowProjectLog(ConsoleHeaderRow.For(name, state, inCycle, depIssues, namePrefix), lineCount);
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

        Assert.Equal("Dependency issue: Sales.Data — last successful output referenced", header.DepIssueTooltip.Content);
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

        Assert.Equal(RowWarning.InCycle, header.CycleTooltip.Content); // "In a dependency cycle" — satırla AYNI
        GC.KeepAlive(window);
    }

    /// <summary>[Final review M-6] Tasarım iki rozetin tooltip'ini <c>side="bottom"</c> ister — uygulamanın
    /// yerleşim mekanizması explicit bir <see cref="ToolTip"/> üzerindeki <see cref="AppTooltip.Side"/>'dır
    /// (düz metin tooltip varsayılan Top'ta kalır).</summary>
    [StaFact]
    public void Both_badge_tooltips_open_below_the_badge()
    {
        var (header, window, _) = Realize(h => ShowProjectLog(h, inCycle: true, depIssues: ["OSYS.Sales.Data"]));

        var dep = Assert.IsType<ToolTip>(header.DepIssueBadge.ToolTip);
        var cycle = Assert.IsType<ToolTip>(header.CycleBadge.ToolTip);
        Assert.Equal(AppTooltip.Bottom, AppTooltip.GetSide(dep));
        Assert.Equal(AppTooltip.Bottom, AppTooltip.GetSide(cycle));
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

    // ---------------------------------------------------------------- geniş panel: kardeşler ada BİTİŞİK durur

    /// <summary>[review R1 finding 1] Prototipte (<c>BuildApp.jsx:2612</c>) proje adı <c>white-space: nowrap</c>
    /// bir <c>span</c>'dır — KISALIR ama asla BÜYÜMEZ, ve statü glyph'i onun HEMEN sağındadır. Eski sapma: ad
    /// sütunu Grid'in <c>"*"</c>'ıydı — geniş bir panelde KISA bir adla bile "*" sütunu kalan alanın TAMAMINI
    /// aldığı için glyph, adın metninden çok sonra, büyük bir boşlukla başlıyordu. Bu test dar DEĞİL geniş
    /// panelde koşar (bulgunun kendisi geniş panelde ortaya çıkıyordu).
    ///
    /// <para><b>[review sonrası ölçüldü] <see cref="TextBlock.ActualWidth"/> BURADA YANILTICIDIR:</b> bir
    /// <c>"*"</c> sütununda bile <c>TextBlock</c>'un LAYOUT KUTUSU sütunun tam genişliğine gerer (Stretch), ve
    /// bir SONRAKİ Auto sütun tam o kutunun bittiği yerden başlar — yani kutunun sağ kenarı komşu sütunla
    /// HER ZAMAN çakışır, hata kutunun İÇİNDEDİR (metin sola yaslı çizilir, kutunun geri kalanı boş kalır).
    /// Kanıt bu yüzden metnin KENDİ görünür genişliğini (bir <c>probe</c> ile) okur, kutunun
    /// <c>ActualWidth</c>'ini DEĞİL.</para></summary>
    [StaFact]
    public void A_wide_panel_with_a_short_name_keeps_the_status_glyph_snug_against_it()
    {
        const string name = "A";
        var (header, window, _) = Realize(h => ShowProjectLog(h, name: name, lineCount: 3), width: 600);

        var probe = new TextBlock
        {
            Text = name, FontFamily = header.ProjectNameText.FontFamily, FontSize = header.ProjectNameText.FontSize,
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        double nameLeft = header.ProjectNameText.TranslatePoint(new Point(0, 0), header.ProjectLogGroup).X;
        double visibleTextRight = nameLeft + probe.DesiredSize.Width;
        double glyphLeft = header.StatusGlyphIcon.TranslatePoint(new Point(0, 0), header.ProjectLogGroup).X;

        Assert.Equal(8.0, glyphLeft - visibleTextRight, 1); // README §9: öğe aralığı 8px — "büyük boşluk" DEĞİL
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- dar genişlik: yalnız proje adı kırpılır

    /// <summary>[README §9] "panel daralınca kısalan TEK öğe proje adıdır (Back ve sağdaki sayaç asla
    /// kırpılmaz)". Eski sapma: sol grup <c>StackPanel</c> olduğu için proje adı HİÇ kırpılmıyordu (panel
    /// taşardı). İddia şimdi GERÇEK kırpmadır — metnin kendi DOĞAL (kısıtlanmamış) genişliği, ekrana yerleşen
    /// genişlikten büyük olmalı (yalnız "sütun/komşu genişliği farklı" demek yetmez, WorkspaceLabelTests'in
    /// <c>probe</c> deseniyle AYNI kanıt biçimi — kopya değil, aynı idiom).</summary>
    [StaFact]
    public void A_narrow_header_really_trims_the_project_name_back_and_lines_stay_intact()
    {
        string longName = "OSYS." + new string('P', 60) + ".WorkOrder";
        var (narrow, narrowWindow, _) = Realize(h => ShowProjectLog(h, name: longName, lineCount: 128), width: 320);

        Assert.Equal(TextTrimming.CharacterEllipsis, narrow.ProjectNameText.TextTrimming);

        var probe = new TextBlock
        {
            Text = longName,
            FontFamily = narrow.ProjectNameText.FontFamily,
            FontSize = narrow.ProjectNameText.FontSize,
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.True(probe.DesiredSize.Width > narrow.ProjectNameText.ActualWidth,
            $"kırpma HİÇ olmadı: ham genişlik {probe.DesiredSize.Width}px, yerleşen {narrow.ProjectNameText.ActualWidth}px");
        Assert.True(narrow.ProjectNameText.ActualWidth > 0,
            "ad TAMAMEN kayboldu — bu genişlikte hâlâ bir miktar metin görünür olmalıydı (çok agresif kırpma)");

        // Back ve N lines TAM kalır — İKİNCİ (geniş) bir gerçekleştirmeyle ActualWidth'e karşı ActualWidth
        // kıyaslanır (DesiredSize Margin'i DAHİL eder, ActualWidth HARİÇ tutar — Back'in -6px marjıyla ikisini
        // karıştırmak yanlış pozitif/negatif üretirdi).
        var (wide, wideWindow, _) = Realize(h => ShowProjectLog(h, name: longName, lineCount: 128), width: 600);
        Assert.Equal(wide.BackButton.ActualWidth, narrow.BackButton.ActualWidth, 1);
        Assert.Equal(wide.LinesText.ActualWidth, narrow.LinesText.ActualWidth, 1);
        GC.KeepAlive(narrowWindow);
        GC.KeepAlive(wideWindow);
    }

    /// <summary>[Final review M-1] Sağ blok yalnız Copy log görünürlüğüyle değil, <c>N lines</c> metni
    /// genişleyince de (999 → 1000) büyür — sol bloğun payı daralır ve proje adının <c>MaxWidth</c>'i o kadar
    /// küçülmelidir. Tetik sağ bloğun KENDİ <c>SizeChanged</c>'idir; aksi halde ad, sağ blokla çakışana dek eski
    /// payını korurdu.</summary>
    [StaFact]
    public void Widening_the_line_count_text_shrinks_the_project_names_MaxWidth_by_the_same_amount()
    {
        string longName = "OSYS." + new string('P', 60) + ".WorkOrder";
        var (header, window, _) = Realize(h => ShowProjectLog(h, name: longName, lineCount: 999), width: 320);
        double maxBefore = header.ProjectNameText.MaxWidth;
        double rightBefore = header.RightBlock.ActualWidth;
        Assert.True(double.IsFinite(maxBefore), "test kurgusu: ad kısıtlanmış olmalıydı");

        header.SetLineCount(100_000); // Copy log zaten görünür — görünürlük değişmez, yalnız metin genişler
        header.UpdateLayout();

        double rightGrowth = header.RightBlock.ActualWidth - rightBefore;
        Assert.True(rightGrowth > 1, "test kurgusu: sayaç metni genişlemedi");
        Assert.Equal(maxBefore - rightGrowth, header.ProjectNameText.MaxWidth, 1);
        GC.KeepAlive(window);
    }
}
