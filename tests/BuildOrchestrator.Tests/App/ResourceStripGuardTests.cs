using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using BuildOrchestrator.Core.Diagnostics;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kaynak şeridi · design v1.25.0 §2.1] Şeridin kaynak-taramalı guard'ları: görünüm dosyası <c>Views/ResourceStrip.xaml</c> ham renk ve ham ölçü
/// taşımaz (her sayı bir token'dır), hareket makinesi taşımaz (değer değişiminde animasyon/geçiş YOK), işaretçiye tepki vermez (tıklanmaz, hover
/// zemini yok, amber yok, imleç yok) ve hayalet metinleri Core'un sabitlerinden alır. Guard'lar mevcut motion/renk guard'larının desenindedir
/// (<see cref="SourceGuard"/>): <b>tarama boş dönerse sessizce yeşil kalmasın</b> diye dosyanın var olduğu ve tokenları gerçekten kullandığı ayrıca
/// pinlenir; her kuralın kendi kanıtı sahte bir girdiyle sınanır.
/// </summary>
[Collection("Console UI (serial)")] // token sözlüğünü yükleyen StaFact — bkz. ConsoleUiSerialCollection
public class ResourceStripGuardTests
{
    private static readonly string XamlRelative = Path.Combine("Views", "ResourceStrip.xaml");
    private static readonly string CodeRelative = Path.Combine("Views", "ResourceStrip.xaml.cs");

    private static readonly Regex XmlComment = new("<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>XAML'in YORUMSUZ markup'ı — yorumlar yasağı anlatabilir (burada "Storyboard yok" gibi), ihlal sayılmaz.</summary>
    private static string Markup() =>
        XmlComment.Replace(File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, XamlRelative)), "");

    // ---------------------------------------------------------------- ham renk ve ham ölçü yok

    /// <summary><c>NoHardcodedColorTests</c>'in XAML kalıbı: tırnak/açı-parantez sınırlı hex ya da scRGB.</summary>
    private static readonly Regex ColourLiteral = new(
        "(?:\"|>)\\s*(?:#[0-9a-fA-F]{3,8}|sc#[0-9.,\\s]+)\\s*(?:\"|<)", RegexOptions.Compiled);

    /// <summary>Sayısal ölçü öznitelikleri (<c>Width="12"</c>, <c>Margin="14,0,0,0"</c>, <c>FontSize="11"</c> …). <c>{DynamicResource …}</c> ile
    /// başlayan değer sayı değildir → temiz. <c>StrokeThickness</c> da burada: ikon kalınlığı <c>Icon.X.StrokeThickness</c> sözlüğünden gelir.</summary>
    private static readonly Regex MeasureAttribute = new(
        "\\b(?:Width|Height|MinWidth|MinHeight|MaxWidth|MaxHeight|Margin|Padding|FontSize|StrokeThickness|BorderThickness|CornerRadius)" +
        "\\s*=\\s*\"\\s*[-+]?[0-9.]", RegexOptions.Compiled);

    /// <summary>Aynı sayı bir <c>Setter</c> içinde: <c>&lt;Setter Property="Width" Value="12" /&gt;</c> — öznitelik kalıbını dolaşmanın yolu.</summary>
    private static readonly Regex MeasureSetter = new(
        "Property\\s*=\\s*\"(?:Width|Height|MinWidth|MinHeight|MaxWidth|MaxHeight|Margin|Padding|FontSize|StrokeThickness)\"" +
        "\\s+Value\\s*=\\s*\"\\s*[-+]?[0-9.]", RegexOptions.Compiled);

    [Fact]
    public void The_strip_markup_carries_no_raw_colour_literal()
    {
        Assert.Contains("x:Class=\"BuildOrchestrator.App.Views.ResourceStrip\"", Markup(), StringComparison.Ordinal); // dosya gerçekten okundu
        Assert.Empty(SourceGuard.ScanText(XamlRelative, Markup(), ColourLiteral));
    }

    [Fact]
    public void The_strip_markup_carries_no_numeric_measurement_every_size_is_a_token()
    {
        Assert.Empty(SourceGuard.ScanText(XamlRelative, Markup(), MeasureAttribute));
        Assert.Empty(SourceGuard.ScanText(XamlRelative, Markup(), MeasureSetter));
    }

    /// <summary>Guard'ın KENDİ kanıtı: sahte markup'ta renk, öznitelik ölçüsü ve setter ölçüsü satır numarasıyla yakalanır; token'lı satırlar ve
    /// yorum yakalanmaz.</summary>
    [Fact]
    public void The_literal_rules_see_planted_colours_and_sizes_but_not_tokens_or_comments()
    {
        const string fake = """
            <!-- Width="12" ve #ffffff yalnız bir yorumda -->
            <StackPanel Margin="{DynamicResource Thickness.ResourceStripCellGap}">
              <Border Background="#0e0e10" Width="{DynamicResource Size.ResourceStripIcon}" />
              <Border Width="12" Height="14" />
              <TextBlock FontSize="11" Foreground="{DynamicResource Brush.TextDim}" />
              <Style><Setter Property="Margin" Value="5,0,0,0" /></Style>
              <Style><Setter Property="Margin" Value="{DynamicResource Thickness.ResourceStripValueGap}" /></Style>
            </StackPanel>
            """;
        string markup = XmlComment.Replace(fake, "");

        Assert.Single(SourceGuard.ScanText("Fake.xaml", markup, ColourLiteral));
        var measures = SourceGuard.ScanText("Fake.xaml", markup, MeasureAttribute);
        Assert.Equal(3, measures.Count); // Width="12" · Height="14" · FontSize="11"
        Assert.Single(SourceGuard.ScanText("Fake.xaml", markup, MeasureSetter));
    }

    // ---------------------------------------------------------------- hareket makinesi yok

    /// <summary>XAML: animasyon, geçiş, tetikleyici ve saat sözcükleri. Değer değişiminde hiçbir şey oynamaz — hücre metni olduğu gibi yazılır.</summary>
    private static readonly Regex XamlMotion = new(
        @"Storyboard|BeginStoryboard|Animation|KeyFrame|Transition|EventTrigger|DataTrigger|\bTrigger\b|\.Triggers|Clock",
        RegexOptions.Compiled);

    /// <summary>C#: <c>BeginAnimation</c>, <c>DoubleAnimation</c> ve aileleri, <c>Transition</c> yardımcıları, saatler ve zamanlayıcılar.</summary>
    private static readonly Regex CodeMotion = new(
        @"Storyboard|BeginAnimation|ApplyAnimationClock|Animation|Transition|MotionTokens|PopIn\b|DispatcherTimer|IPollTimer|Clock",
        RegexOptions.Compiled);

    [Fact]
    public void The_strip_has_no_motion_machinery_in_its_markup_or_its_code()
    {
        string code = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, CodeRelative));
        Assert.Contains("class ResourceStrip", code, StringComparison.Ordinal); // dosya gerçekten okundu

        Assert.Empty(SourceGuard.ScanText(XamlRelative, Markup(), XamlMotion));
        Assert.Empty(SourceGuard.ScanText(CodeRelative, code, CodeMotion, skipCommentLines: true));
    }

    [Fact]
    public void The_motion_rules_see_planted_animation_and_ignore_prose_about_it()
    {
        const string xaml = """
            <!-- Storyboard yok: yalnız bir yorum -->
            <UserControl>
              <UserControl.Triggers><EventTrigger RoutedEvent="Loaded"><BeginStoryboard><Storyboard /></BeginStoryboard></EventTrigger></UserControl.Triggers>
            </UserControl>
            """;
        Assert.NotEmpty(SourceGuard.ScanText("Fake.xaml", XmlComment.Replace(xaml, ""), XamlMotion));

        const string code = """
            // BeginAnimation kullanılmaz — bu satır yalnızca yasağı ANLATIYOR
            private void Fade() => Opacity.BeginAnimation(OpacityProperty, new DoubleAnimation());
            """;
        var hits = SourceGuard.ScanText("Fake.cs", code, CodeMotion, skipCommentLines: true);
        Assert.NotEmpty(hits);                                              // ihlal satırı yakalandı…
        Assert.All(hits, hit => Assert.StartsWith("Fake.cs:2: ", hit));    // …yalnız o satır: 1. satırdaki yasağı ANLATAN yorum sayılmadı
    }

    // ---------------------------------------------------------------- gizli yüzeyde yazım yok: XAML bağı yasak

    /// <summary>Şeridin değerleri XAML'de bağlanmaz: bir <c>{Binding}</c> pencere tepsideyken de hücreyi yazar ve ölçümü geçersizler (perf Faz A —
    /// koşu sürerken pay saniyede onlarca kez değişir). Yazım kod-tarafındadır ve kalıtsal <c>HiddenSurface</c> sinyaline bakar
    /// (<c>ResourceStripRealizeTests</c> davranışı, <c>HiddenSurfaceTests</c> tüm ağacı pinler; bu guard bağın geri sızmasını çitler).</summary>
    private static readonly Regex MarkupBinding = new(@"\{\s*(?:Binding|MultiBinding|x:Bind|TemplateBinding)", RegexOptions.Compiled);

    [Fact]
    public void The_strip_never_binds_in_markup_its_code_writes_under_the_hidden_surface_gate()
    {
        string code = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, CodeRelative));

        Assert.Empty(SourceGuard.ScanText(XamlRelative, Markup(), MarkupBinding));
        Assert.Contains("HiddenSurface.GetIsHidden(this)", code, StringComparison.Ordinal);
        Assert.Contains("HiddenSurface.BecameVisible(e)", code, StringComparison.Ordinal);
        Assert.NotEmpty(SourceGuard.ScanText("Fake.xaml", """<TextBlock Text="{Binding StripCpuText}" />""", MarkupBinding)); // guard'ın kanıtı
    }

    // ---------------------------------------------------------------- tıklama alanı yalnız hücreler

    /// <summary>Şeridin KÖKÜNÜN zemini yoktur; <c>Background="Transparent"</c> yalnız üç hücrededir (neden: <c>Views/ResourceStrip.xaml</c> başlığı;
    /// davranış pini: <c>ResourceStripRealizeTests</c>). Bu guard zeminin geri sızmasını kaynakta çitler.</summary>
    [Fact]
    public void The_strip_root_declares_no_background_only_the_three_cells_carry_a_transparent_one()
    {
        string markup = Markup();
        var root = Regex.Match(markup, @"<UserControl\b[^>]*>", RegexOptions.Singleline);

        Assert.True(root.Success, "kök <UserControl> bulunamadı");
        Assert.DoesNotContain("Background", root.Value, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(markup, "Background=\"Transparent\"").Count);
        Assert.Equal(3, Regex.Matches(markup, @"x:Name=""(?:Cpu|Memory|Parallel)Cell""[^>]*Background=""Transparent""", RegexOptions.Singleline).Count);
    }

    // ---------------------------------------------------------------- sürükleme yerel caption'dan geçer, Window.DragMove'dan değil

    private static readonly string WindowCodeRelative = "MainWindow.ResourceStrip.cs";

    private static readonly Regex DragMoveCall = new(@"\bDragMove\b", RegexOptions.Compiled);

    /// <summary>Şerit sürüklemeyi yerel caption'a devreder, <c>Window.DragMove</c>'a değil (neden: <c>MainWindow.ForwardCaptionDrag</c>'ın özeti).
    /// Davranış pini bir dikişin üstündedir (<c>ResourceStripWiringTests</c>) ve dikiş üretimin gerçek yolunu — <c>ReleaseCapture</c> +
    /// <c>SendMessage</c> — çalıştırmaz; bu guard o gerçek yolda <c>DragMove</c>'un geri sızmasını kaynakta çitler ve yerel çağrının hâlâ orada
    /// olduğunu pinler — kapanış mesajı dahil: taşıma döngüsünün yuttuğu tuş bırakılışını WPF'e geri veren <c>WM_LBUTTONUP</c> (o satır düşerse
    /// WPF farenin hâlâ basılı olduğunu sanır; dikiş bunu göremez). [temizlik madde 4: liste <c>WM_LBUTTONUP</c>'u içermiyordu.]</summary>
    [Fact]
    public void The_strip_forwards_a_drag_to_the_native_caption_and_never_calls_DragMove()
    {
        string code = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, WindowCodeRelative));

        Assert.Contains("class MainWindow", code, StringComparison.Ordinal); // dosya gerçekten okundu
        Assert.Empty(SourceGuard.ScanText(WindowCodeRelative, code, DragMoveCall, skipCommentLines: true));
        foreach (string native in new[] { "Win32.ReleaseCapture(", "Win32.SendMessage(", "Win32.WM_NCLBUTTONDOWN", "Win32.HTCAPTION", "Win32.WM_LBUTTONUP" })
            Assert.Contains(native, code, StringComparison.Ordinal);

        // Guard'ın kanıtı: koddaki çağrı satır numarasıyla yakalanır, onu anlatan yorum yakalanmaz.
        var hits = SourceGuard.ScanText("Fake.cs", """
            // DragMove yalnız bir yorumda: bu satır yasağı ANLATIYOR
            private void Drag() => DragMove();
            """, DragMoveCall, skipCommentLines: true);
        Assert.StartsWith("Fake.cs:2: ", Assert.Single(hits));
    }

    // ---------------------------------------------------------------- işaretçiye tepki vermez, amber taşımaz

    /// <summary>Şerit yalnız okur: tıklanmaz (buton/komut/tıklama yok), hover zemini almaz (<c>IsMouseOver</c>/giriş-çıkış yok), imleç değişmez
    /// (<c>Cursor</c> yok) ve amber/statü rengi taşımaz (title bar'da accent ağırlığı logonun chevron'unda kalır — §6).</summary>
    private static readonly Regex PointerOrAccent = new(
        @"IsMouseOver|IsMouseDirectlyOver|IsPressed|MouseEnter|MouseLeave|MouseDown|MouseUp|Cursor|Click|Command|Button|Hyperlink|Brush\.Amber|Brush\.Status|Brush\.Focus",
        RegexOptions.Compiled);

    [Fact]
    public void The_strip_markup_never_reacts_to_the_pointer_and_never_carries_the_accent()
    {
        Assert.Empty(SourceGuard.ScanText(XamlRelative, Markup(), PointerOrAccent));
        Assert.NotEmpty(SourceGuard.ScanText("Fake.xaml", """<Border Cursor="Hand" Background="{DynamicResource Brush.AmberSoft}" />""", PointerOrAccent));
    }

    // ---------------------------------------------------------------- hayalet metinler ve token'lar gerçekten kullanılıyor

    [Fact]
    public void The_ghost_texts_are_the_core_constants_and_are_never_retyped()
    {
        string markup = Markup();

        foreach (string constant in new[] { nameof(ResourceStripText.CpuGhost), nameof(ResourceStripText.MemoryGhost), nameof(ResourceStripText.ParallelGhost), nameof(ResourceStripText.ModeGhost) })
            Assert.Contains($"ResourceStripText.{constant}", markup, StringComparison.Ordinal);
        Assert.Empty(SourceGuard.ScanText(XamlRelative, markup, new Regex("[\"'>]\\s*(?:100%|12\\.3 GB|12/12|Balanced)\\s*[\"'<]")));
    }

    [Fact]
    public void The_strip_reads_its_icons_sizes_fonts_and_colours_by_token_key()
    {
        string markup = Markup();

        string[] keys =
        [
            "Icon.Cpu", "Icon.MemoryStick", "Icon.Parallel",
            "Icon.Cpu.StrokeThickness", "Icon.MemoryStick.StrokeThickness", "Icon.Parallel.StrokeThickness",
            "Size.ResourceStripIcon", "Size.ResourceStripIconViewBox", "Thickness.ResourceStripCellGap", "Thickness.ResourceStripValueGap",
            "FontSize.2xs", "FontWeight.Body", "Brush.TextDim", "Brush.TextFaint",
        ];
        // Anahtar TAM olarak geçmeli: uzun bir anahtar (ör. Size.ResourceStripIconViewBox) kısa olanı (Size.ResourceStripIcon) karşılamış sayılmaz.
        Assert.All(keys, key => Assert.Matches($@"(?<![\w.]){Regex.Escape(key)}(?![\w.])", markup));
        Assert.Contains("AppFonts.Mono", markup, StringComparison.Ordinal);
        Assert.Contains("AppFonts.Ui", markup, StringComparison.Ordinal);
        Assert.Contains("Typography.NumeralAlignment", markup, StringComparison.Ordinal);
    }

    /// <summary>Şeridin kendi ölçüleri tasarımın sayılarıdır (README §2.1: 12px ikon, hücre arası 14px, ikon/değer ve sayı/mod arası 5px) ve
    /// şeridin üç ikonunun 24'lük (Lucide viewBox) Canvas kutusu — her biri TEK yerde (<c>Tokens.xaml</c>) tanımlıdır. Viewbox anahtarı şeride özeldir:
    /// uygulamanın öbür 24'lük Canvas'ları (ConsoleHeader, GraphView, Settings …) literal 24 taşımaya devam eder, genel adlı bir token onlara da
    /// ait görünürdü.</summary>
    [StaFact]
    public void The_strip_measure_tokens_are_the_design_numbers_each_defined_exactly_once()
    {
        var tokens = DsResources.Load("Tokens.xaml");

        Assert.Equal(12.0, (double)tokens["Size.ResourceStripIcon"]);
        Assert.Equal(24.0, (double)tokens["Size.ResourceStripIconViewBox"]);
        Assert.Equal(new Thickness(14, 0, 0, 0), (Thickness)tokens["Thickness.ResourceStripCellGap"]);
        Assert.Equal(new Thickness(5, 0, 0, 0), (Thickness)tokens["Thickness.ResourceStripValueGap"]);

        foreach (string key in new[] { "Size.ResourceStripIcon", "Size.ResourceStripIconViewBox", "Thickness.ResourceStripCellGap", "Thickness.ResourceStripValueGap" })
        {
            var definitions = RepoPaths.AppSourceFiles("*.xaml")
                .Where(f => File.ReadAllText(f).Contains($"x:Key=\"{key}\"", StringComparison.Ordinal))
                .Select(f => Path.GetRelativePath(RepoPaths.AppSrcRoot, f)).ToList();
            Assert.Equal([Path.Combine("Resources", "Tokens.xaml")], definitions);
        }
    }

    [Fact]
    public void The_strip_view_files_exist_and_are_scanned_by_the_app_wide_guards_too()
    {
        var xaml = SourceGuard.ScannedAppFiles("*.xaml");
        var cs = SourceGuard.ScannedAppFiles("*.cs");

        Assert.Contains(XamlRelative, xaml);
        Assert.Contains(CodeRelative, cs);
        Assert.Contains("MainWindow.ResourceStrip.cs", cs);
    }
}
