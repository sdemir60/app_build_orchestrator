using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Konsol başlığının GERÇEKTEN realize edilmiş (ekran dışı pencere + ApplyTemplate) hâline dair stil/ölçü/renk iddiaları —
/// bir Style/DynamicResource'un XAML'de YAZILI olması onun doğru ÇÖZÜLDÜĞÜNÜ kanıtlamaz. <see cref="ConsoleModesTests"/> mod
/// geçişini ve içerik doldurmayı test eder.
///
/// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-25]</b> Proje sayfasının başlığı solda YALNIZ proje adını taşır; sağda
/// Copy log + <c>N lines</c> kalır. Eski iddialar (v1.18.0 §9 "Konsol başlığı ve <c>Back</c> satırı"): adın solunda Back
/// (DS Ghost.Sm, 24 px, çizilmiş <c>Icon.Back</c> + "Back", ikon butonun animasyonlu Foreground'unu izler), sağında 13 px
/// <c>StatusGlyph</c> (building'de dönen spinner, yerleşik durumda halka) + koşu statüsü sözcüğü (<c>StatusGlyph.RunLabelFor</c>:
/// Discovered · Queued · Building · Succeeded · Failed · Skipped) + amber <c>dependency issue</c> / <c>dependency cycle</c>
/// rozetleri (kontur üçgeni, tam liste tooltip'leri, ikisi de altta açılır) dururdu; öğeler 8 px aralıklıydı ve adın genişliği
/// bu kardeşlerin gerçek genişliğinden elle hesaplanırdı (<c>ApplyProjectNameShrink</c>). Başlık seçili satırı da canlı
/// izlerdi (<c>MainWindow.TrackHeaderRow</c> → <c>RefreshStatus</c>; <c>ConsoleHeaderLiveRefreshTests</c>) ve koşu
/// statüsü sözcüğünün tablosu <c>VisualStatusTests.A_run_story_surface_names_the_runs_result</c>'ta pinliydi.
/// Değişme gerekçesi: kullanıcı proje sayfasında bunlara gerek görmedi — koşuya girmemiş bir projede kesikli halka +
/// "Discovered" bir yükleme göstergesi gibi okunuyordu (bildirim: "proje adının sağında loading ve Discovered görünüyor").
/// Durum, bağımlılık sorunu ve döngü üyeliği listede (şerit, nokta, glyph, karar etiketi, uyarı üçgeni) ve grafta kalır;
/// anlatıya dönüş Back olmadan da vardır: aynı satıra ya da düğüme yeniden tıklamak, grafın boş zeminine tıklamak ya da Esc
/// (ARCHITECTURE §13.7 — <c>GraphClickTests</c>, <c>ProjectRowInputTests</c>, <c>MainWindowInputTests</c>).</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ConsoleHeaderDesignTests
{
    /// <summary>[önce parent, sonra içerik] Üretimde <c>ShowProjectLog</c> her zaman zaten pencereye eklenmiş bir başlıkta
    /// çağrılır; yardımcı bu sırayı izler, çünkü genişlik ve konum iddiaları gerçek bir Measure/Arrange geçişi ister.</summary>
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

    /// <summary>Başlık artık satırı hiç almaz, yalnız adı: eskiden başlıkta bir öğe açan durumlar (derleniyor, bağımlılık
    /// sorunu, döngü üyeliği) ona ulaşamaz. Kırmızı bu testlerde eski imzayla (<c>ShowProjectLog(row, n)</c>), o üç durumu
    /// birden taşıyan bir satırla gösterildi: başlıkta glyph vardı ve ad Back yüzünden 70,8 px içeride başlıyordu.</summary>
    private static ConsoleHeader ShowProjectLog(ConsoleHeader header, string name = "OSYS.Sales.Core", int lineCount = 42)
    {
        header.ShowProjectLog(name, lineCount);
        return header;
    }

    // ---------------------------------------------------------------- proje sayfası: yalnız ad

    /// <summary>Başlıkta görünen her şey: soldaki ad ve sağdaki Copy log + <c>N lines</c>. Back, statü glyph'i (ve spinner'ı),
    /// statü sözcüğü ve iki rozet yoktur — sınıf özetindeki karar.</summary>
    [StaFact]
    public void The_project_log_header_shows_only_the_project_name_and_the_right_block()
    {
        var (header, window, _) = Realize(h => ShowProjectLog(h, lineCount: 42));

        var shown = DsResources.Descendants(header).OfType<FrameworkElement>().Where(e => e.IsVisible).ToList();
        Assert.DoesNotContain(shown, e => e is StatusGlyph or BuildingSpinner);
        Assert.Equal(new[] { header.CopyLogButton }, shown.OfType<Button>());
        Assert.Equal(new[] { "OSYS.Sales.Core", "42 lines" }, shown.OfType<TextBlock>().Select(t => t.Text));
        Assert.False(header.ConsoleLabel.IsVisible);
        GC.KeepAlive(window);
    }

    /// <summary>Ad, anlatı kipindeki CONSOLE etiketiyle AYNI yerden başlar: panelin iç boşluğundan. Eski iddia: ad Back'in
    /// 8 px sağındaydı ve glyph/statü/rozetler 8 px aralıkla onu izlerdi (README §9). Back kalkınca o 8 px adı boşluğa
    /// iterdi; sağ bloktaki 8 px aralık (<c>N lines</c>) aynı kalır.</summary>
    [StaFact]
    public void The_project_name_starts_where_the_console_caption_does()
    {
        var (header, window, _) = Realize(h => ShowProjectLog(h));
        double nameLeft = header.ProjectNameText.TranslatePoint(new Point(0, 0), header.RootGrid).X;

        header.ShowNarrative(0);
        header.UpdateLayout();
        double captionLeft = header.ConsoleLabel.TranslatePoint(new Point(0, 0), header.RootGrid).X;

        Assert.Equal(captionLeft, nameLeft, 1);
        Assert.Equal(8.0, header.LinesText.Margin.Left);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- compact etiketi (anlatı modu)

    /// <summary>[idle-memory · dalga 2 · spec §6.5] Anlatı kompakt yazılırken sağ blokta sayacın HEMEN sağında <c>compact</c>
    /// etiketi durur — sayaçla AYNI tipografi (Geist Mono 11, soluk metin) ve başlığın öğe aralığı (8px). Realize edilmiş başlıkta
    /// ÇÖZÜLEN değerler okunur: yazılı bir DynamicResource'un doğru çözüldüğünü yalnız realize kanıtlar.</summary>
    [StaFact]
    public void The_compact_tag_follows_the_line_counter_in_its_typography_eight_pixels_apart()
    {
        var (header, window, host) = Realize(h => { h.ShowNarrative(1234); h.SetCompact(true); });

        Assert.Equal(Visibility.Visible, header.CompactText.Visibility);
        Assert.Equal("compact", header.CompactText.Text);
        Assert.Equal(header.RightBlock.Children.IndexOf(header.LinesText) + 1, header.RightBlock.Children.IndexOf(header.CompactText));
        Assert.Equal(8.0, header.CompactText.Margin.Left);
        Assert.Same(AppFonts.Mono, header.CompactText.FontFamily);
        Assert.Equal(header.LinesText.FontSize, header.CompactText.FontSize);
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextFaint"), DsResources.ColorOf(header.CompactText.Foreground));
        Assert.True(header.CompactText.ActualWidth > 0, "the tag was laid out in the realized header");
        GC.KeepAlive(window);
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

    // ---------------------------------------------------------------- geniş panel: ad doğal genişliğinde

    /// <summary>Prototipte (<c>BuildApp.jsx:2612</c>) proje adı <c>white-space: nowrap</c> bir <c>span</c>'dır — KISALIR ama
    /// asla BÜYÜMEZ. Eski iddia: geniş panelde kısa adın sağındaki statü glyph'i ada bitişik (8 px) dururdu; glyph kalktı.
    /// Kalan iddia adın kendisidir: geniş bir panelde kutusu metnin doğal genişliğindedir, panele gerilmez.</summary>
    [StaFact]
    public void A_wide_panel_draws_a_short_name_at_its_natural_width()
    {
        const string name = "A";
        var (header, window, _) = Realize(h => ShowProjectLog(h, name: name, lineCount: 3), width: 600);

        var probe = new TextBlock
        {
            Text = name, FontFamily = header.ProjectNameText.FontFamily, FontSize = header.ProjectNameText.FontSize,
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Equal(probe.DesiredSize.Width, header.ProjectNameText.ActualWidth, 1);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- dar genişlik: yalnız proje adı kırpılır

    /// <summary>[README §9] "panel daralınca kısalan TEK öğe proje adıdır (sağdaki sayaç asla kırpılmaz)". Eski sapma: sol
    /// grup <c>StackPanel</c> olduğu için proje adı HİÇ kırpılmıyordu (panel taşardı). İddia GERÇEK kırpmadır — metnin kendi
    /// DOĞAL (kısıtlanmamış) genişliği, ekrana yerleşen genişlikten büyük olmalı (<c>WorkspaceLabelTests</c>'in <c>probe</c>
    /// deseniyle AYNI kanıt biçimi). Eski iddianın Back'e dair yarısı ("Back kırpılmaz") Back'le birlikte kalktı.</summary>
    [StaFact]
    public void A_narrow_header_really_trims_the_project_name_and_the_line_count_stays_intact()
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

        // N lines TAM kalır — İKİNCİ (geniş) bir gerçekleştirmeyle ActualWidth'e karşı ActualWidth kıyaslanır.
        var (wide, wideWindow, _) = Realize(h => ShowProjectLog(h, name: longName, lineCount: 128), width: 600);
        Assert.Equal(wide.LinesText.ActualWidth, narrow.LinesText.ActualWidth, 1);
        GC.KeepAlive(narrowWindow);
        GC.KeepAlive(wideWindow);
    }

    /// <summary>[Final review M-1] Sağ blok yalnız Copy log görünürlüğüyle değil, <c>N lines</c> metni genişleyince de
    /// (999 → 1000) büyür — ad o kadar geri çekilmeli, sağ blokla asla çakışmamalıdır. Eski iddia: adın elle hesaplanan
    /// <c>MaxWidth</c>'i sağ bloğun büyümesi kadar küçülür. Kardeşler kalkınca adın payını yerleşimin kendisi verir (sol
    /// sütun kalan alandır); iddia artık mekanizmayı değil sonucu okur: ad kırpılı kalır ve sağ kenarı sağ bloğun sol
    /// kenarını geçmez.</summary>
    [StaFact]
    public void Widening_the_line_count_text_never_lets_the_project_name_run_under_it()
    {
        string longName = "OSYS." + new string('P', 60) + ".WorkOrder";
        var (header, window, _) = Realize(h => ShowProjectLog(h, name: longName, lineCount: 999), width: 320);
        double rightBefore = header.RightBlock.ActualWidth;

        header.SetLineCount(100_000); // Copy log zaten görünür — görünürlük değişmez, yalnız metin genişler
        header.UpdateLayout();

        Assert.True(header.RightBlock.ActualWidth - rightBefore > 1, "test kurgusu: sayaç metni genişlemedi");
        double nameRight = header.ProjectNameText.TranslatePoint(new Point(header.ProjectNameText.ActualWidth, 0), header.RootGrid).X;
        double rightLeft = header.RightBlock.TranslatePoint(new Point(0, 0), header.RootGrid).X;
        Assert.True(nameRight <= rightLeft + 0.5, $"ad sağ bloğun altına taştı: adın sağı {nameRight}px, sağ blok {rightLeft}px'te başlıyor");
        GC.KeepAlive(window);
    }
}
