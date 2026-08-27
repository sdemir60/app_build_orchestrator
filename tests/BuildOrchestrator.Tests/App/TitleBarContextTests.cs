using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.11.0 §2.1] Title bar YALNIZ markayı taşır: ürün markası + ürün adı + ayraç + firma logosu.
///
/// <para><b>[DEĞİŞEN KURAL]</b> design-v1 §2.1 title bar'da ayrıca mono bir <b>bağlam metni</b> istiyordu
/// (<c>OSYS · main</c>, worktree açıkken <c>· main-2</c> eki, repo yokken <c>no repository</c>) ve
/// <c>TitleBarContextTests</c> onu pinliyordu. v1.11.0 bu metni KALDIRDI: branch ve worktree bilgisi zaten alt
/// bardaki chip'lerde duruyordu (aynı olgu iki yerde), workspace adı ise alt bara —branch chip'inin soluna—
/// taşındı (§2.7-5a). Eski iddialar bu yüzden silinmedi, iki yeni kurala BÖLÜNDÜ: burada "title bar'da bağlam
/// metni YOK", <see cref="WorkspaceLabelTests"/>'te "workspace adı alt barda ve tooltip'i repo köküdür".</para>
///
/// <para><b>Otorite:</b> design-v1.11.0 prototipinin title bar'ında (BuildApp.jsx:2186-2192) marka kilidinden
/// başka hiçbir metin yoktur. README §3.1'in "empty fazında title bar'da mono <c>not configured</c>" cümlesi
/// v1.8.0'dan kalma bir kalıntıdır ve §2.1 ile çelişir; bağlayıcı olan prototiptir (README §"Fidelity").</para>
/// </summary>
[Collection("Console UI (serial)")]
public class TitleBarContextTests
{
    /// <summary>Marka kilidindeki TÜM metin blokları.</summary>
    private static IReadOnlyList<TextBlock> LogoLockTexts(MainWindow window)
    {
        var mark = DsResources.Descendants(window.RootShell).OfType<AppMark>().Single();
        var lockPanel = (Panel)LogicalTreeHelper.GetParent(mark);
        return [.. lockPanel.Children.Cast<UIElement>().OfType<TextBlock>()];
    }

    /// <summary>Kilit tek bir metin taşır: ürün adı. Repo/branch/worktree hiçbir koşulda başlığa yazılmaz.</summary>
    [StaFact]
    public void The_title_bar_carries_the_brand_only_and_never_a_repo_or_branch_context()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window); // kabuk ÖNCE realize — veri SONRA akar (üretim sırası)

        vm.RootPath = @"C:\src\OSYS";
        vm.Branch = "main";
        vm.UseWorktree = true;
        vm.WorktreeName = "main-2";

        var texts = LogoLockTexts(window);
        Assert.Equal([BuildOrchestrator.App.Services.AppIdentity.Product], texts.Select(t => t.Text));
        GC.KeepAlive(window);
    }

    /// <summary>Repo yokken de öyle: eski <c>no repository</c> işareti title bar'dan tamamen kalktı.</summary>
    [StaFact]
    public void With_no_repository_the_title_bar_still_shows_nothing_but_the_brand()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        var texts = LogoLockTexts(window);
        Assert.Equal([BuildOrchestrator.App.Services.AppIdentity.Product], texts.Select(t => t.Text));
        GC.KeepAlive(window);
    }

    // Repo adı hesabının (kökün klasör adı) kendisi burada TEKRARLANMAZ — tek yeri
    // InteractionStateTests.The_repository_name_is_the_folder_name_of_the_root (kopya YASAK, CLAUDE.md).

    // ================================================================ [A13/T3b] ölçü/geometri (b10/b12)

    /// <summary>
    /// [design-v1.2.1 §2.1] Title bar'ın LOGO KİLİDİ: ürün markası 19px tam renk, firma logosu 10px ve %55
    /// opaklıkta. Hiyerarşi şarttır — ürün önde, firma arkada.
    ///
    /// <para><b>ESKİ İDDİA (design-v1 §1.1/§2.1):</b> "Delta logosu (dark varyant, <b>15px</b>)" ve title
    /// bar'da BAŞKA logo yoktu. Kural design-v1.2.0'da BİLEREK DEĞİŞTİ: uygulama kendi markasını kazandı,
    /// Delta ise firma logosu rolüne geçti ve küçülüp soldu. Bu yüzden 15 sayısı artık yanlıştır; test
    /// silinmedi, YENİ kuralı pinleyecek şekilde yeniden yazıldı.</para>
    ///
    /// <para>[A13/T3 fix-1 · B8'den devralınan kural] Seçici iddianın KENDİSİ değildir: öğeler ölçülerinden
    /// değil KİMLİKLERİNDEN (<c>x:Name</c> / tip) seçilir, ölçü SONRA assert edilir — aksi halde "logo
    /// büyüdü" ile "logo ağaçtan silindi" ayırt edilemez.</para></summary>
    [StaFact]
    public void The_title_bar_locks_a_19px_product_mark_ahead_of_a_10px_company_logo()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        var mark = DsResources.Descendants(window.RootShell).OfType<AppMark>().Single();
        var logo = window.TitleBarLogo;
        Assert.Contains(logo, DsResources.Descendants(window.RootShell)); // gerçekten AĞAÇTA (kurulup atılmamış)

        Assert.Equal(19.0, mark.Height);
        Assert.Equal(10.0, logo.Height);
        Assert.Equal(0.55, logo.Opacity, precision: 2); // firma logosu SOLUK
        Assert.Equal("Delta", logo.ToolTip);            // ikon-yalnız öğe: firma adı tooltip'te

        // Realize zorunlu (kural 5) — literal okumak yetmez. Tolerans: UseLayoutRounding="True" + test host'unun
        // DPI ölçeği ölçüyü BİR alt-piksele kaydırabilir; 1dip'lik pay bunu yutar ama yanlış bir sabiti YAKALAR.
        Assert.True(Math.Abs(mark.ActualHeight - 19.0) < 1.0,
            $"ürün markası ActualHeight beklenenden (19) çok uzak: {mark.ActualHeight}");
        Assert.True(Math.Abs(logo.ActualHeight - 10.0) < 1.0,
            $"firma logosu ActualHeight beklenenden (10) çok uzak: {logo.ActualHeight}");

        // Ürün markası firma logosunun SOLUNDA — hiyerarşinin yerleşim kanıtı.
        double markX = mark.TranslatePoint(new Point(0, 0), window.RootShell).X;
        double logoX = logo.TranslatePoint(new Point(0, 0), window.RootShell).X;
        Assert.True(markX < logoX, $"ürün markası firma logosunun solunda değil ({markX} ≥ {logoX})");
        GC.KeepAlive(window);
    }

    /// <summary>[design-v1.2.1 §2.1] Kilidin SIRASI: ürün markası → ürün adı → 1×13 ayraç → firma logosu.
    /// Ayraç, iki markayı ayıran öğedir; firma logosu düşerse onunla birlikte düşmelidir (bugün firma logosu
    /// her zaman var, ayraç da öyle). <b>[DEĞİŞEN KURAL — v1.11.0]</b> Zincirin sonundaki "→ bağlam" adımı
    /// kalktı: firma logosu artık kilidin SON öğesidir.</summary>
    [StaFact]
    public void The_logo_lock_places_a_hairline_separator_between_the_two_marks()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        var mark = DsResources.Descendants(window.RootShell).OfType<AppMark>().Single();
        var lockPanel = (Panel)LogicalTreeHelper.GetParent(mark);
        var children = lockPanel.Children.Cast<UIElement>().ToList();

        var separator = children.OfType<System.Windows.Shapes.Rectangle>().Single();
        Assert.Equal(1.0, separator.Width);
        Assert.Equal(13.0, separator.Height);

        Assert.True(children.IndexOf(mark) < children.IndexOf(separator));
        Assert.True(children.IndexOf(separator) < children.IndexOf(window.TitleBarLogo));
        Assert.Equal(children.Count - 1, children.IndexOf(window.TitleBarLogo)); // firma logosu SON öğe
        GC.KeepAlive(window);
    }
}
