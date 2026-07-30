using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildOrchestrator.App;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T2 · madde 2.1] Title bar'ın mono bağlam metni — <b>design-v1 §2.1 otoritesi</b>:
///
/// <para><i>"Sol: Delta logosu (dark varyant, 15px) + &quot;Build Orchestrator&quot; başlığı; ardından mono 11px
/// <c>text-dim</c> bağlam: <c>OSYS · main</c> — worktree aktifse <c>· main-2</c> eklenir (<c>text-faint</c>).
/// Repo yokken: <c>no repository</c>."</i></para>
///
/// <para><b>Ölçülen kusur (T2 envanteri):</b> <c>rg ContextText src tests docs</c> TEK isabet veriyordu —
/// <c>MainWindow.xaml:161</c>'deki XAML literali. Hiçbir C# dosyası <c>ContextText.Text</c>'e YAZMIYORDU, yani
/// başlık her koşulda <c>no repository</c>'de donmuştu: repo seçimi de, branch seçimi de, worktree de oraya
/// HİÇ ulaşmıyordu.</para>
///
/// <para><b>Tetikleyici kuralı (A12 dersi):</b> metin üretimdeki yoldan sürülür — VM'in gerçek
/// <c>RootPath</c>/<c>Branch</c>/<c>UseWorktree</c>/<c>WorktreeName</c> özellikleri set edilir ve kablo
/// (<c>PropertyChanged</c> → <c>RefreshTitleContext</c>) çalışsın diye pencere ÖNCE realize edilir, veri SONRA
/// akar. Kablo koparsa metin XAML varsayılanında kalır → kırmızı.</para>
/// </summary>
[Collection("Console UI (serial)")]
public class TitleBarContextTests
{
    /// <summary>Title bar'daki TÜM metin blokları — bağlam gövdesi + (varsa) worktree eki bunların içindedir.
    /// Ek, gövdeden AYRI bir <see cref="TextBlock"/> olmak ZORUNDADIR: iki farklı ton (text-dim gövde /
    /// text-faint ek) tek bir <c>Text</c> literaliyle verilemez.</summary>
    private static IReadOnlyList<TextBlock> TitleBarTexts(MainWindow window) =>
        [.. DsResources.Descendants(window.RootShell).OfType<TextBlock>()];

    private static TextBlock? WorktreeSuffix(MainWindow window) =>
        TitleBarTexts(window).FirstOrDefault(t => t.Text.StartsWith('·'));

    [StaFact]
    public void With_no_repository_the_title_context_reads_the_verbatim_empty_marker()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Assert.Equal("no repository", window.ContextText.Text);
        Assert.Null(WorktreeSuffix(window)); // repo yokken worktree eki de OLMAZ
        GC.KeepAlive(window);
    }

    /// <summary>design-v1 §2.1'in ana iddiası: repo seçilince bağlam <c>&lt;repo-adı&gt; · &lt;branch&gt;</c> olur.
    /// Repo adı kökün KLASÖR adıdır (prototipteki sabit <c>'OSYS'</c> literalinin gerçek karşılığı) — T2
    /// envanterinde ölçüldü ki bu kavram üretimde hiç HESAPLANMIYORDU.</summary>
    [StaFact]
    public void Choosing_a_repository_and_branch_writes_them_into_the_title_context()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window); // kabuk ÖNCE realize — veri SONRA akar (üretim sırası)

        vm.RootPath = @"C:\src\OSYS";
        vm.Branch = "main";

        Assert.Equal("OSYS · main", window.ContextText.Text);
        GC.KeepAlive(window);
    }

    /// <summary>Branch daha bilinmiyorken (envanter gelmeden) sallantıda bir ayraç bırakılmaz — yalnız repo adı.</summary>
    [StaFact]
    public void Before_the_branch_is_known_the_context_shows_the_repository_alone()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        vm.RootPath = @"D:\Projects\Delta\OSYS\";  // sondaki ayraç repo adını BOZMAMALI

        Assert.Equal("OSYS", window.ContextText.Text);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Worktree eki AYRI ve DAHA SOLUK: gövde <c>Brush.TextDim</c>, ek <c>Brush.TextFaint</c> (design-v1 §2.1).
    /// Tek bir TextBlock'a iki ton verilemeyeceği için ek ayrı bir öğedir; ikisi de mono 11px
    /// (<c>FontSize.2xs</c>) kalır.
    /// </summary>
    [StaFact]
    public void An_active_worktree_appends_a_fainter_suffix_next_to_the_context()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        var host = DsResources.NewHost(); // token karşılaştırması için AYNI merge zinciri
        MainWindowHost.Realize(window);

        vm.RootPath = @"C:\src\OSYS";
        vm.Branch = "main";
        vm.UseWorktree = true;
        vm.WorktreeName = "main-2";

        var suffix = WorktreeSuffix(window);
        Assert.NotNull(suffix);
        Assert.Equal("· main-2", suffix.Text);
        Assert.Equal("OSYS · main", window.ContextText.Text); // gövde EK'İ İÇERMEZ (iki ayrı ton)

        Assert.Equal(DsResources.TokenColor(host, "Brush.TextFaint"), DsResources.ColorOf(suffix.Foreground));
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextDim"), DsResources.ColorOf(window.ContextText.Foreground));
        Assert.Equal((double)host.FindResource("FontSize.2xs"), suffix.FontSize); // mono 11px
        GC.KeepAlive(window);
    }

    /// <summary>Worktree kapatılınca ek KAYBOLUR (yapışıp kalmaz).</summary>
    [StaFact]
    public void Turning_the_worktree_off_removes_the_suffix_again()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        vm.RootPath = @"C:\src\OSYS";
        vm.Branch = "main";
        vm.UseWorktree = true;
        vm.WorktreeName = "main-2";
        Assert.NotNull(WorktreeSuffix(window)); // ön-koşul

        vm.UseWorktree = false;

        Assert.Null(WorktreeSuffix(window));
        GC.KeepAlive(window);
    }

    // ================================================================ [A13/T3b] ölçü/geometri (b10/b12)

    /// <summary>[A13/T3b · b10] design-v1 README §1.1/§2.1: "Delta logosu (dark varyant, 15px yükseklik)"
    /// (ayrıca §1.1 satır 68, §2.1 satır 107). Testsizdi.</summary>
    [StaFact]
    public void The_title_bar_logo_is_fifteen_pixels_tall()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        // Logo Viewbox'ı design-v1'in tek 15px yükseklikli Viewbox'ıdır (layout ikonları/gear 16x16'dır) —
        // tek eşleşen ayırt edici.
        var logo = DsResources.Descendants(window.RootShell).OfType<Viewbox>().Single(v => v.Height == 15);
        // Realize zorunlu (kural 5) — literal okumak yetmez. Tolerans: UseLayoutRounding="True" (MainWindow.xaml)
        // + test host'unun DPI ölçeği (150%'de ölçüldü: 15dip*1.5=22.5px → 22'ye yuvarlanır → 14.667dip) 15'i
        // BİR alt-piksele kaydırabilir; 1dip'lik pay bunu yutar ama YANLIŞ bir sabiti (ör. 20) YAKALAR.
        Assert.True(Math.Abs(logo.ActualHeight - 15.0) < 1.0,
            $"logo ActualHeight beklenenden ({15.0}) çok uzak: {logo.ActualHeight}");
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [A13/T3b · b12] Title bar bağlam metninin <c>MaxWidth</c> kırpması (<c>ContextText</c>=320,
    /// <c>ContextWorktreeText</c>=200) — T2'nin fix'inden devreden borç (koordinatör notu). T2'nin XAML yorumu
    /// "sınırlar tek satırda kalacak şekilde ölçülüdür" diyordu; ne design-v1 README/BuildApp.jsx ne de
    /// design-wpf-feasibility-analysis bu iki SAYI için bir genişlik bütçesi/ölçüm KAYDETMİYOR (§2.1 yalnız
    /// iki-span + <c>gap:8</c> yapısını tarif eder — bir MaxWidth değil). Otorite/ölçüm bu iki sayı için
    /// SESSİZ: rakamlar bu yüzden DEĞİŞTİRİLMEDİ (kural 4), ama yanlış "ölçülüdür" iddiası MainWindow.xaml'de
    /// düzeltildi (bu commit). Kalan, kapsam dahilindeki görev: kırpmanın GERÇEKTEN çalıştığını VE
    /// caption/layout butonlarının ALTINA taşmadığını pinlemek.
    /// </summary>
    [StaFact]
    public void An_extremely_long_repository_and_branch_name_is_clamped_and_never_creeps_under_the_layout_buttons()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        vm.RootPath = @"C:\src\" + new string('R', 80);
        vm.Branch = new string('b', 80);
        // [gerçek ölçüm] En dar desteklenen genişlikte (Size.WindowMinWidth=1240) yerleş — bolluk (1400px
        // varsayılan) altında bu iddia anlamsız olurdu (DockPanel'in kendisi hiç sıkışmaz).
        ((FrameworkElement)window.Content).Measure(new Size(1240, 800));
        ((FrameworkElement)window.Content).Arrange(new Rect(0, 0, 1240, 800));
        window.RootShell.UpdateLayout();

        // Kontrol grubu: MaxWidth OLMASAYDI bu metin çok daha geniş render ederdi — pack:// font headless
        // çözülmediği için file:// eşdeğeriyle ölçülür (ProjectRowTests.Sha deseni, kopya YASAK).
        var probe = new TextBlock
        { Text = window.ContextText.Text, FontFamily = DsResources.MonoFontFamily, FontSize = window.ContextText.FontSize };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.True(probe.DesiredSize.Width > 320,
            $"kontrol grubu önemsiz: kısaltılmamış metin zaten 320px altında ({probe.DesiredSize.Width}px)");

        Assert.True(window.ContextText.ActualWidth <= 320.01,
            $"ContextText MaxWidth=320'yi aştı: {window.ContextText.ActualWidth}px");

        double contextRightEdge = window.ContextText.TranslatePoint(new Point(window.ContextText.ActualWidth, 0), window).X;
        double layoutButtonsLeftEdge = window.LayQuadButton.TranslatePoint(new Point(0, 0), window).X;
        Assert.True(contextRightEdge <= layoutButtonsLeftEdge,
            $"bağlam metni ({contextRightEdge}px) layout butonlarının ({layoutButtonsLeftEdge}px) ALTINA taştı");
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T3b · b12] Worktree ekinin (ContextWorktreeText) 200px kırpması — yukarıdaki testin
    /// AYNI gerekçesi, ek için.</summary>
    [StaFact]
    public void An_extremely_long_worktree_suffix_is_clamped_and_never_creeps_under_the_layout_buttons()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        vm.RootPath = @"C:\src\OSYS";
        vm.Branch = "main";
        vm.UseWorktree = true;
        vm.WorktreeName = new string('w', 80);
        ((FrameworkElement)window.Content).Measure(new Size(1240, 800));
        ((FrameworkElement)window.Content).Arrange(new Rect(0, 0, 1240, 800));
        window.RootShell.UpdateLayout();

        var suffix = WorktreeSuffix(window);
        Assert.NotNull(suffix); // ön-koşul

        var probe = new TextBlock
        { Text = suffix!.Text, FontFamily = DsResources.MonoFontFamily, FontSize = suffix.FontSize };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.True(probe.DesiredSize.Width > 200,
            $"kontrol grubu önemsiz: kısaltılmamış ek zaten 200px altında ({probe.DesiredSize.Width}px)");

        Assert.True(suffix.ActualWidth <= 200.01, $"ContextWorktreeText MaxWidth=200'ü aştı: {suffix.ActualWidth}px");

        double suffixRightEdge = suffix.TranslatePoint(new Point(suffix.ActualWidth, 0), window).X;
        double layoutButtonsLeftEdge = window.LayQuadButton.TranslatePoint(new Point(0, 0), window).X;
        Assert.True(suffixRightEdge <= layoutButtonsLeftEdge,
            $"worktree eki ({suffixRightEdge}px) layout butonlarının ({layoutButtonsLeftEdge}px) ALTINA taştı");
        GC.KeepAlive(window);
    }
}
