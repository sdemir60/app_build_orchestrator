using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [D7 re-review][Fix1] Settings modal'ının klavye odak tuzağı (focus trap). Scrim FAREYİ bloklar ama Tab
/// öntanımlı olarak arka plandaki kontrollere KAÇAR (ör. Settings açıkken Tab ile arka plandaki Build butonuna
/// gidip Space/Enter ile bir run başlatmak mümkündü). <c>SettingsDialog.xaml</c>'daki
/// <c>KeyboardNavigation.TabNavigation/ControlTabNavigation="Cycle"</c> + <c>FocusManager.IsFocusScope="True"</c>
/// (Scrim kökü) bunu düzeltir. Gerçek <see cref="Window"/> içinde kurulur (StaFact) — bkz. <see cref="DsResources"/>.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class SettingsDialogFocusTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    // [A13/T3 fix-1 · C13] FakeStore ARTIK tek yerde: SettingsDialogHost.FakeStore (iki dosyada ikizdi).
    private static SettingsDialogHost.FakeStore NewStore() => new();

    /// <summary>Yapısal asgari kanıt: diyaloğun scrim kökü gerçekten bir Cycle klavye-gezinme kapsayıcısı ve
    /// bir odak kapsamı (FocusScope) mı — Tab'ın alt-ağaç dışına kaçamayacağının doğrudan kanıtı.</summary>
    [StaFact]
    public void Settings_dialog_scrim_is_a_cyclic_keyboard_focus_scope()
    {
        var host = DsResources.NewHost();
        var dialog = new SettingsDialog();
        var window = DsResources.Realize(host, dialog);

        Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(dialog.Scrim));
        Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetControlTabNavigation(dialog.Scrim));
        Assert.True(FocusManager.GetIsFocusScope(dialog.Scrim));
        GC.KeepAlive(window);
    }

    /// <summary>Gerçek gezinme kanıtı: aynı pencerede arka planda odaklanabilir bir kontrol (Build butonunun
    /// yerini tutan) + açık diyalog dururken, diyalog alt-ağacından başlayarak tekrar tekrar "Sonraki" gezinme
    /// (Tab'ın WPF içindeki gerçek mekanizması — <see cref="UIElement.MoveFocus"/>) yapılır: kontrol sayısından
    /// FAZLA turda ne odak arka plan kontrolüne kaçar ne de diyalog alt-ağacının dışına çıkar (Cycle sarar).
    /// <para>[About] İDDİANIN GÖVDESİ ARTIK <see cref="FocusTrap"/>'te: About modali aynı iddiaya ihtiyaç
    /// duyuyor ve 20 satırlık yürüyüş ikinci kez yazılacaktı (kopya YASAK, CLAUDE.md). Kurulum ve beklenti
    /// AYNEN korundu — davranış değişmedi.</para></summary>
    [StaFact]
    public async Task Tab_navigation_cannot_escape_the_open_dialog_to_reach_a_background_control()
    {
        var host = DsResources.NewHost();
        var background = new Button { Content = "Background Build", Focusable = true, Width = 90, Height = 24 };
        var root = new Grid();
        root.Children.Add(background);

        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var dialog = new SettingsDialog();
        root.Children.Add(dialog);

        var window = DsResources.Realize(host, root);

        dialog.Open(run, NewStore(), () => null);
        root.UpdateLayout(); // diyalog artık Visible — satır/buton container'ları yerleşsin

        FocusTrap.AssertCannotEscape(dialog.Scrim, background);
        GC.KeepAlive(window);
    }

    // ================================================================ [A13/T3b] ölçü/geometri (b2/b3)

    /// <summary>[A13/T3b · b2 → task-D6/T12] design-v1 README §2.9: "Settings dialog (760px)".
    /// <c>DesignTokenScaleTests.cs:141</c> içinde geçen 620 AYRI bir kalemdir (<c>Size.WindowMinHeight</c>) —
    /// karıştırılmaz (brief notu).
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.13.1]</b> ESKİ İDDİA: 620px — üç dialog (Settings/About/What's new)
    /// AYNI kalıbı paylaşıyordu. YENİ: her dialog bugünkü içeriğine değil BÜYÜME YÖNÜNE göre ölçülüyor;
    /// Settings en çok büyüyecek olan (bugün root + katman kartları, yarın MSBuild yolu/paralellik/worktree
    /// havuzu/bildirim tercihleri — form + iki kolonlu kart en geniş bileşimdir) → 760px. Büyüme sürerse bir
    /// bölüm listesi (sol nav) eklenir, genişlik yine 760'ta kalır.</para></summary>
    [StaFact]
    public void Settings_dialog_shell_is_seven_hundred_sixty_pixels_wide()
    {
        // [fix-1 · B6/C9] Kurulum + EngineHost sahipliği tek yerde (SettingsDialogHost).
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using (scope)
        {
            var shell = (Border)VisualTreeHelper.GetChild(dialog.Scrim, 0);
            Assert.Equal(760.0, shell.Width);
            Assert.Equal(760.0, shell.ActualWidth); // realize zorunlu — literal okumak yetmez (kural 5)
        }
    }

    // ================================================================ [task-D6/T12 · design v1.14.0 §2.9]
    // Gövde kendi içinde kaydırılır: üst sınır min(pencere-yüksekliği × 56%, 460px) — SAF hesap
    // SettingsBodyHeight'ta (kopya YASAK); burada yalnız GERÇEK pencereye KABLAJ sınanır. Alt taban 300px
    // ScrollViewer.MinHeight'a sabit bağlanır (XAML, x:Static).

    /// <summary>Alt taban HER pencerede sabittir — WPF'in kendi Min/Max önceliği (Min, Max'ı ezer) çok küçük
    /// pencerede dialogun çökmesini bu tek satır üzerinden engeller.</summary>
    [StaFact]
    public void Settings_body_min_height_is_300_so_the_dialog_never_collapses()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Equal(SettingsBodyHeight.MinFloor, dialog.Body.MinHeight);
        }
    }

    /// <summary>Büyük pencere: gövde 460px'te SABİTLENİR (tasarımın üst sınırı) — pencere ne kadar büyürse
    /// büyüsün dialog ekranı kaplamaya devam ETMEZ.</summary>
    [StaFact]
    public void Settings_body_max_height_caps_at_460_in_a_tall_window()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(windowHeight: 1200);
        using (scope)
        {
            Assert.Equal(460.0, dialog.Body.MaxHeight);
        }
    }

    /// <summary>Orta pencere: üst sınır pencere yüksekliğinin GERÇEKTEN %56'sını izler (ne taban ne tavana
    /// yapışık) — kablajın <see cref="SettingsBodyHeight.MaxHeightFor"/>'u GERÇEK <c>Window.ActualHeight</c>'la
    /// çağırdığının doğrudan kanıtı.</summary>
    [StaFact]
    public void Settings_body_max_height_follows_56_percent_of_the_window_between_the_floor_and_the_cap()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(windowHeight: 700);
        using (scope)
        {
            Assert.Equal(SettingsBodyHeight.MaxHeightFor(700), dialog.Body.MaxHeight, precision: 3);
            Assert.Equal(392.0, dialog.Body.MaxHeight, precision: 3); // 700 × 0.56 — ne 300 ne 460
        }
    }

    /// <summary>Ruling task-D6: hesap pencere yeniden boyutlandığında YENİDEN çağrılır — bir kere hesaplanıp
    /// unutulmaz. GraphRealizationPerfTests'teki AYNI desen (<c>window.Height = …; content.UpdateLayout();</c>).</summary>
    [StaFact]
    public void Settings_body_max_height_updates_when_the_hosting_window_is_resized()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(windowHeight: 1200);
        using (scope)
        {
            Assert.Equal(460.0, dialog.Body.MaxHeight); // başlangıç: tavana yapışık

            var window = Window.GetWindow(dialog)!;
            window.Height = 700;
            dialog.UpdateLayout();

            Assert.Equal(392.0, dialog.Body.MaxHeight, precision: 3); // YENİDEN hesaplandı — 700 × 0.56
        }
    }

    /// <summary>[task-D6/T12] Prototipin <c>padding-right:10px / margin-right:-10px</c> hilesinin WPF karşılığı:
    /// scrollbar sütunu HER ZAMAN ayrılır (<c>Auto</c> yerine <c>Visible</c>) — böylece bar gerektiğinde
    /// belirmesi/kaybolması içerik genişliğini OYNATMAZ. DS'in <c>IsEnabled=False</c> tetikleyicisi (kaydıracak
    /// şey yokken track'i gizleyen, ScrollBarStyleTests'teki "restraint" kuralı) bu modda da devrededir — WPF'in
    /// stok ScrollViewer şablonu <c>Maximum=0</c> iken bar'ı otomatik <c>IsEnabled=False</c> yapar (ölçüldü),
    /// yani boşken çirkin bir "dolu hap" da GÖRÜNMEZ.</summary>
    [StaFact]
    public void Settings_body_reserves_the_scrollbar_column_instead_of_auto_collapsing_it()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Equal(ScrollBarVisibility.Visible, dialog.Body.VerticalScrollBarVisibility);
        }
    }

    /// <summary>[task-D6/T12] SONUÇ testi (mekanizma değil): bir katman kartının PATTERN input'u — DockPanel'in
    /// <c>LastChildFill</c> ile "esnek genişlik" alanı, dolayısıyla scrollbar sütunu daralırsa/genişlerse İLK
    /// etkilenen ölçü — az satırda (scrollbar GEREKMEZ) ve çok satırda (scrollbar GERÇEKTEN taşar) AYNI genişliği
    /// ölçer. Non-vacuous: iki senaryonun GERÇEKTEN farklı scroll durumunda olduğu ayrıca doğrulanır.</summary>
    [StaFact]
    public void Settings_body_layer_card_width_stays_constant_whether_or_not_the_scrollbar_is_needed()
    {
        var (fits, _, _, fitsScope) = SettingsDialogHost.OpenRealized(
            r => r.LayerPatterns = [new LayerPattern(0, "^A", "Alpha")], windowHeight: 1200);
        using var _fitsScope = fitsScope;
        var (overflowing, _, _, overflowScope) = SettingsDialogHost.OpenRealized(
            r => r.LayerPatterns = [.. Enumerable.Range(0, 20).Select(i => new LayerPattern(i, "^A" + i, "Layer " + i))],
            windowHeight: 1200);
        using var _overflowScope = overflowScope;

        var fitsBar = BodyVerticalScrollBar(fits.Body);
        var overflowingBar = BodyVerticalScrollBar(overflowing.Body);
        Assert.Equal(0.0, fitsBar.Maximum);          // non-vacuous: 1 satır GERÇEKTEN kaymaz
        Assert.True(overflowingBar.Maximum > 0);     // non-vacuous: 20 satır GERÇEKTEN kayar (460'ı aşar)

        double fitsWidth = PatternInputOf(fits.LayersList, ((SettingsDraftViewModel)fits.DataContext).Layers[0]).ActualWidth;
        double overflowingWidth = PatternInputOf(overflowing.LayersList, ((SettingsDraftViewModel)overflowing.DataContext).Layers[0]).ActualWidth;

        Assert.True(fitsWidth > 0);
        Assert.Equal(fitsWidth, overflowingWidth, precision: 1); // scrollbar belirmesi genişliği OYNATMADI
    }

    /// <summary>[A13/T3b · b3] design-v1 README §2.9: "Katman kartları (36px + 6px boşluk) ... ad inputu
    /// (170px)". Önceden yalnız 42px (=36+6) aritmetiği DragReorderTests.cs'teki sürükleme eşiğinden DOLAYLI
    /// pinliydi (RowStep=42 — sürükleme mantığının kendi sabiti, kartın GERÇEK yerleşimi değil). Bu test iki
    /// gerçek kartı realize edip aralarındaki GERÇEK piksel farkını ve ad inputunun GERÇEK genişliğini ölçer.</summary>
    [StaFact]
    public void Layer_cards_are_36px_tall_with_a_6px_gap_and_a_170px_name_input()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(run =>
            run.LayerPatterns = [new LayerPattern(0, "^A", "Layer A"), new LayerPattern(1, "^B", "Layer B")]);
        using var _scope = scope;

        var editor = (SettingsDraftViewModel)dialog.DataContext;
        Assert.Equal(2, editor.Layers.Count); // ön-koşul: iki kart gerçekten var

        var card0 = CardBorder(dialog.LayersList, editor.Layers[0]);
        var card1 = CardBorder(dialog.LayersList, editor.Layers[1]);

        Assert.Equal(36.0, card0.ActualHeight);
        Assert.Equal(new Thickness(0, 0, 0, 6), card0.Margin);

        // GERÇEK dikey mesafe: kart1'in üst kenarı − kart0'ın üst kenarı = 36 (yükseklik) + 6 (alt boşluk) = 42.
        double top0 = card0.TranslatePoint(new Point(0, 0), dialog).Y;
        double top1 = card1.TranslatePoint(new Point(0, 0), dialog).Y;
        Assert.Equal(42.0, top1 - top0, precision: 1);

        var nameBox = DsResources.Descendants(card0).OfType<TextBox>()
            .Single(t => BuildOrchestrator.App.Controls.DsChrome.GetWatermark(t) == "Layer name");
        Assert.Equal(170.0, nameBox.Width);
        Assert.Equal(170.0, nameBox.ActualWidth);
    }

    private static Border CardBorder(ItemsControl list, LayerRowViewModel row)
    {
        var presenter = (ContentPresenter)list.ItemContainerGenerator.ContainerFromItem(row)!;
        presenter.ApplyTemplate();
        return (Border)VisualTreeHelper.GetChild(presenter, 0);
    }

    /// <summary>Bir katman kartının PATTERN input'u (ad input'unun İKİZİ — <c>CardBorder</c>'ın izinden gider).</summary>
    private static TextBox PatternInputOf(ItemsControl list, LayerRowViewModel row) =>
        DsResources.Descendants(CardBorder(list, row)).OfType<TextBox>()
            .Single(t => DsChrome.GetWatermark(t) != "Layer name");

    /// <summary>Gövdenin KENDİ dikey scrollbar'ı — <c>Descendants(body).OfType&lt;ScrollBar&gt;()</c> tek başına
    /// YETMEZ: her katman kartındaki TextBox'ın KENDİ şablonu da bir <c>PART_ContentHost</c> ScrollViewer'ı (ve
    /// onun görünmez scrollbar'larını) taşır — birden fazla "dikey" bar bulunur. <c>PART_VerticalScrollBar</c>
    /// ScrollViewer'ın KENDİ şablonunun sabit (WPF sözleşmesi) parça adıdır, iç içe olanlarla KARIŞMAZ.</summary>
    private static ScrollBar BodyVerticalScrollBar(ScrollViewer body) =>
        (ScrollBar)body.Template.FindName("PART_VerticalScrollBar", body);
}
