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
    /// (otorite ayrıca DS <c>TitleBar</c>: <c>_ds_bundle.js:1201</c> <c>img { height: 15 }</c>). Testsizdi.
    ///
    /// <para>[A13/T3 fix-1 · B8] Seçici ARTIK iddianın kendisi DEĞİL: eski hâli logoyu
    /// <c>Single(v =&gt; v.Height == 15)</c> ile, yani <b>15 olduğu için</b> buluyordu — "logo 20px oldu" ile
    /// "logo ağaçtan silindi" ayırt edilemiyordu ve mutasyon bir ölçü hatası değil
    /// <c>Sequence contains no matching element</c> gürültüsü üretiyordu. Öğe artık KİMLİĞİNDEN
    /// (<c>MainWindow.xaml x:Name="TitleBarLogo"</c>) seçilir, ölçü SONRA assert edilir.</para></summary>
    [StaFact]
    public void The_title_bar_logo_is_fifteen_pixels_tall()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        var logo = window.TitleBarLogo;
        Assert.Contains(logo, DsResources.Descendants(window.RootShell)); // gerçekten AĞAÇTA (kurulup atılmamış)
        Assert.Equal(15.0, logo.Height);
        // Realize zorunlu (kural 5) — literal okumak yetmez. Tolerans: UseLayoutRounding="True" (MainWindow.xaml)
        // + test host'unun DPI ölçeği (150%'de ölçüldü: 15dip*1.5=22.5px → 22'ye yuvarlanır → 14.667dip) 15'i
        // BİR alt-piksele kaydırabilir; 1dip'lik pay bunu yutar ama YANLIŞ bir sabiti (ör. 20) YAKALAR.
        Assert.True(Math.Abs(logo.ActualHeight - 15.0) < 1.0,
            $"logo ActualHeight beklenenden ({15.0}) çok uzak: {logo.ActualHeight}");
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [A13/T3b · b12 → fix-1 · B2+B3+C7] Title bar bağlam metninin kırpması.
    ///
    /// <para><b>Otorite bu iki SAYI için (320/200) SESSİZDİR</b> — design-v1 §2.1 ve <c>BuildApp.jsx:1436-1444</c>
    /// yalnız iki span + <c>gap:8</c> yapısını tarif eder, bir genişlik bütçesi VERMEZ;
    /// <c>design-wpf-feasibility-analysis</c>'te de bir ölçüm yoktur. Sayılar T2'nin kendi fix'inden gelir.
    /// Bu yüzden test sayıyı DEĞİL <b>davranışı</b> pinler (brief kural 4/7) ve beklenen sınırı üretimin kendi
    /// <see cref="FrameworkElement.MaxWidth"/>'inden okur — koordinatör yarın 320'yi 280 yapsa test kırılmaz,
    /// ama kırpma bozulursa kırılır.</para>
    ///
    /// <para><b>WPF gerçeği (fix-2 · 1 — ÖLÇÜLDÜ, lens2'nin gerekçesi ÇÜRÜTÜLDÜ):</b> lens2
    /// <i>"<c>TextTrimming</c> silinse <c>MaxWidth</c> metni yine 320'de kırpardı ve test yeşil kalırdı"</i>
    /// diyordu. <b>Öyle değil.</b> <c>MaxWidth</c> yalnız <c>MeasureCore</c>'u sınırlar;
    /// <c>FrameworkElement.ArrangeCore</c> yerleştirme boyutunu <b><c>unclippedDesiredSize</c></b>'dan alır —
    /// yani ölçümde kırpılan öğe, ARZULADIĞI (kırpılmamış) genişliğe yerleşir. Bu makinede ölçüldü:
    /// <c>TextTrimming</c> kaldırılınca <c>MaxWidth="320"</c> DURUYORKEN <c>ActualWidth</c> <b>1086,67</b>
    /// oluyor (bağımsız re-review probe'u aynı olguyu farklı bir metinle ölçtü: <c>None</c> → 1316,
    /// <c>CharacterEllipsis</c> → 317,32). Dolayısıyla <c>ActualWidth &lt;= MaxWidth</c> bir WPF garantisi
    /// DEĞİL, gerçek bir iddiadır: yalnız metin <c>MaxWidth</c>'e GERÇEKTEN sığacak biçimde kırpıldığında
    /// sağlanır.</para>
    ///
    /// <para><b>Testin ayırt ettiği şey (fix-2 · 4):</b> <c>MaxWidth</c> silinirse (2) kırmızı olur (kırpma HİÇ
    /// olmadı — ham genişlik yerleşenden küçük); <c>TextTrimming</c> silinirse yine (2) kırmızı olur (yerleşen
    /// 1086,67 &gt; ham 1075,8 — arrange kırpılmamış boyutu kullanıyor) ve (3) de aşılır;
    /// <c>CharacterEllipsis</c> → <c>WordEllipsis</c> yapılırsa YALNIZ (4) yakalar — bu mutasyon fix-1 öncesi
    /// hâlde YEŞİL kalırdı, kalemin asıl açığı buydu.</para>
    ///
    /// <para><b>Beş ayrı ayak:</b> (1) öğe gerçekten yerleşti (<c>ActualWidth==0</c> ile önemsizce geçen vakum
    /// penceresi kapalı), (2) kırpma GERÇEKTEN oldu (ham ölçülen genişlik yerleşenden büyük), (3) sınır
    /// aşılmadı — <b>tam eşitlik DEĞİL</b>: <c>ActualWidth == MaxWidth</c> <c>UseLayoutRounding</c> + DPI'a
    /// bağlıdır (bu makinede ~320, re-review makinesinde 317,32) ve WPF bunu garanti etmez; garantiye
    /// çevrilen iddia SINIRdır, (4) kırpma <b>ellipsis</b>'tir, (5) metin caption/layout butonlarının ALTINA
    /// taşmıyor.</para>
    /// </summary>
    /// <param name="worktreeSuffix"><c>false</c>: gövde (<c>ContextText</c>) · <c>true</c>: ek
    /// (<c>ContextWorktreeText</c>) — iki test karbon kopyaydı (fix-1 · B4).</param>
    [StaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_over_long_title_context_is_ellipsised_and_never_creeps_under_the_layout_buttons(bool worktreeSuffix)
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window); // kabuk ÖNCE realize — veri SONRA akar (üretim sırası)

        if (worktreeSuffix)
        {
            vm.RootPath = @"C:\src\OSYS";
            vm.Branch = "main";
            vm.UseWorktree = true;
            vm.WorktreeName = new string('w', 80);
        }
        else
        {
            vm.RootPath = @"C:\src\" + new string('R', 80);
            vm.Branch = new string('b', 80);
        }

        // En dar desteklenen genişlikte (Size.WindowMinWidth=1240) yeniden yerleş — bolluk (1400px varsayılan)
        // altında taşma iddiası anlamsız olurdu. Realize etmenin TEK yeri MainWindowHost'tur (fix-1 · B4).
        MainWindowHost.Realize(window, width: 1240);

        var text = worktreeSuffix ? WorktreeSuffix(window) : window.ContextText;
        Assert.NotNull(text);

        // (1) Ön-koşul: öğe GERÇEKTEN yerleşti (aksi hâlde alttaki sınır iddiaları önemsizce geçerdi).
        Assert.True(text.ActualWidth > 0, "bağlam metni hiç yerleşmedi — sınır iddiaları önemsizce geçerdi");

        // (2) Kırpma GERÇEKTEN oldu: sınırsız ölçülen ham genişlik yerleşenden BÜYÜK. pack:// font headless
        // çözülmediği için file:// eşdeğeriyle ölçülür (ProjectRowTests.Sha deseni, kopya YASAK).
        var probe = new TextBlock
        { Text = text.Text, FontFamily = DsResources.MonoFontFamily, FontSize = text.FontSize };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.True(probe.DesiredSize.Width > text.ActualWidth,
            $"kırpma HİÇ olmadı: ham genişlik {probe.DesiredSize.Width}px, yerleşen {text.ActualWidth}px");

        // (3) Sınır aşılmadı — beklenen değer ÜRETİMDEN okunur (otorite sessiz) ve iddia TAM EŞİTLİK DEĞİL:
        // ActualWidth == MaxWidth bir WPF garantisi değildir (UseLayoutRounding + DPI; 150%'de 317,32 ölçüldü).
        // Buna karşılık ActualWidth <= MaxWidth de bedava DEĞİLDİR: ArrangeCore unclippedDesiredSize kullandığı
        // için trimming olmadan ActualWidth 1316'ya fırlar (fix-2 · 1).
        Assert.True(text.ActualWidth <= text.MaxWidth,
            $"bağlam metni MaxWidth={text.MaxWidth}'i aştı: {text.ActualWidth}px");

        // (4) Kırpma ELLIPSIS'tir — MaxWidth tek başına glyph ortasından keserdi (lens2).
        Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);

        // (5) Caption/layout butonlarının ALTINA taşma yok.
        double rightEdge = text.TranslatePoint(new Point(text.ActualWidth, 0), window).X;
        double layoutButtonsLeftEdge = window.LayQuadButton.TranslatePoint(new Point(0, 0), window).X;
        Assert.True(rightEdge <= layoutButtonsLeftEdge,
            $"bağlam metni ({rightEdge}px) layout butonlarının ({layoutButtonsLeftEdge}px) ALTINA taştı");
        GC.KeepAlive(window);
    }
}
